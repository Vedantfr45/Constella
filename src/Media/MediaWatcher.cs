using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Constella.Core;
using Windows.Media.Control;
using Windows.Storage.Streams;

using MediaManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using MediaProperties = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties;

namespace Constella.Media;

/// <summary>
/// Watches Windows' media sessions (GlobalSystemMediaTransportControlsSessionManager), the same
/// source as the volume flyout's "now playing" card. Works with Spotify, browsers (YouTube,
/// YouTube Music), Apple Music, etc. Polls on a background task, and polls again straight away
/// when the followed app reports a play/pause, seek or song change, so the lyrics react within a
/// few milliseconds instead of waiting for the next poll. Every event is raised on the UI thread
/// through the dispatcher passed in.
/// </summary>
internal sealed class MediaWatcher : IDisposable
{
    private static readonly TimeSpan ActivePoll = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMilliseconds(1500);
    private const int MaxArtAttempts = 6;
    private const long MaxArtBytes = 16 * 1024 * 1024;

    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private MediaManager? _manager;
    private MediaSession? _watched;
    private TrackInfo? _track;
    private int _artAttempts;
    private bool _artDone;

    public MediaWatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>A different song (or nothing, as null) is now the active one.</summary>
    public event Action<TrackInfo?>? TrackChanged;

    /// <summary>Fresh play state and position for the active song.</summary>
    public event Action<PlaybackSample>? PlaybackSampled;

    /// <summary>Album art bytes for the active song, or null if the app never published any.</summary>
    public event Action<byte[]?>? ArtChanged;

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    public void Dispose()
    {
        _cts.Cancel();
        Watch(null);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool active = false;
            try
            {
                active = await PollAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // WinRT errors surface as COMException/other exceptions when sessions come and go.
                // Drop the manager and build a fresh one next time.
                Log.Warn($"Media session poll failed: {ex.GetType().Name}: {ex.Message}");
                Watch(null);
                _manager = null;
            }

            try
            {
                // Sleeps until the next poll, or until the followed app reports a change.
                await _wake.WaitAsync(active ? ActivePoll : IdlePoll, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Follows change events from <paramref name="session"/> (and stops following the previous one).</summary>
    private void Watch(MediaSession? session)
    {
        if (ReferenceEquals(session, _watched))
        {
            return;
        }
        try
        {
            if (_watched is not null)
            {
                _watched.PlaybackInfoChanged -= OnSessionChanged;
                _watched.TimelinePropertiesChanged -= OnSessionChanged;
                _watched.MediaPropertiesChanged -= OnSessionChanged;
            }
            if (session is not null)
            {
                session.PlaybackInfoChanged += OnSessionChanged;
                session.TimelinePropertiesChanged += OnSessionChanged;
                session.MediaPropertiesChanged += OnSessionChanged;
            }
        }
        catch (Exception ex)
        {
            // The session may already be gone; polling still covers it.
            Log.Warn($"Media session events unavailable: {ex.Message}");
        }
        _watched = session;
    }

    /// <summary>Raised on a WinRT thread pool thread: just wakes the poll loop.</summary>
    private void OnSessionChanged<T>(MediaSession sender, T args)
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
    }

    /// <returns>True when a song is present (so we poll faster).</returns>
    private async Task<bool> PollAsync()
    {
        _manager ??= await MediaManager.RequestAsync();
        var picked = await PickSessionAsync(_manager).ConfigureAwait(false);
        Watch(picked?.Session);
        if (picked is null)
        {
            if (_track is not null)
            {
                _track = null;
                Post(() => TrackChanged?.Invoke(null));
            }
            return false;
        }

        var (session, props) = picked.Value;
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        double rate = playback.PlaybackRate is double r && r > 0 ? r : 1.0;

        double duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
        double? knownDuration = duration > 1 ? duration : null;

        var track = new TrackInfo(
            props.Title.Trim(),
            FirstNonEmpty(props.Artist, props.AlbumArtist),
            props.AlbumTitle?.Trim() ?? string.Empty,
            FriendlyAppName(session.SourceAppUserModelId),
            knownDuration);

        if (track.Identity != _track?.Identity)
        {
            _track = track;
            _artAttempts = 0;
            _artDone = false;
            Post(() => TrackChanged?.Invoke(track));
        }

        double position = EstimatePosition(timeline, playing, rate, knownDuration);
        var sample = new PlaybackSample(playing, position, rate, Stopwatch.GetTimestamp());
        Post(() => PlaybackSampled?.Invoke(sample));

        if (!_artDone)
        {
            await TryDeliverArtAsync(props.Thumbnail).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>
    /// Apps only update the timeline now and then, stamping it with LastUpdatedTime. While playing,
    /// add the time that has passed since that stamp to get the real position.
    /// </summary>
    private static double EstimatePosition(
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline, bool playing, double rate, double? duration)
    {
        double position = (timeline.Position - timeline.StartTime).TotalSeconds;
        if (playing)
        {
            double age = (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds;
            // A stamp older than an hour (or in the future) is bogus; some apps never set it.
            if (age > 0 && age < 3600)
            {
                position += age * rate;
            }
        }
        if (duration is double d)
        {
            position = Math.Min(position, d);
        }
        return Math.Max(0, position);
    }

    /// <summary>
    /// Picks the session to follow: a playing one first, then Windows' "current" session, then any
    /// paused one. Sessions without a title are skipped; some apps register an empty session that
    /// claims to be playing and would otherwise hide the real music player.
    /// </summary>
    private static async Task<(MediaSession Session, MediaProperties Props)?> PickSessionAsync(MediaManager manager)
    {
        var sessions = manager.GetSessions();
        var current = manager.GetCurrentSession();
        var ordered = new List<(int Rank, MediaSession Session)>();
        foreach (var session in sessions)
        {
            bool playing = session.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool isCurrent = current is not null && session.SourceAppUserModelId == current.SourceAppUserModelId;
            ordered.Add((playing ? 0 : isCurrent ? 1 : 2, session));
        }
        ordered.Sort((a, b) => a.Rank.CompareTo(b.Rank));

        foreach (var (_, session) in ordered)
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is not null && !string.IsNullOrWhiteSpace(props.Title))
            {
                return (session, props);
            }
        }
        return null;
    }

    /// <summary>Reads the thumbnail; some apps publish it a few polls after the song starts, so retry.</summary>
    private async Task TryDeliverArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        _artAttempts++;
        byte[]? bytes = null;
        if (thumbnail is not null)
        {
            try
            {
                using var stream = await thumbnail.OpenReadAsync();
                if (stream.Size > 0 && stream.Size <= MaxArtBytes)
                {
                    using var source = stream.AsStreamForRead();
                    using var memory = new MemoryStream();
                    await source.CopyToAsync(memory).ConfigureAwait(false);
                    bytes = memory.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Album art read failed: {ex.Message}");
            }
        }

        if (bytes is not null || _artAttempts >= MaxArtAttempts)
        {
            _artDone = true;
            Post(() => ArtChanged?.Invoke(bytes));
        }
    }

    private void Post(Action action)
    {
        if (!_cts.IsCancellationRequested)
        {
            _ = _dispatcher.InvokeAsync(action);
        }
    }

    private static string FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : b?.Trim() ?? string.Empty;

    /// <summary>"Spotify.exe" → "Spotify"; "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "Microsoft.ZuneMusic".</summary>
    private static string FriendlyAppName(string appId)
    {
        string name = appId;
        int bang = name.LastIndexOf('!');
        if (bang >= 0)
        {
            name = name[(bang + 1)..];
        }
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        return name;
    }
}
