using System;
using System.Diagnostics;

namespace Constella.Media;

/// <summary>
/// A smooth song clock. Media apps only report the position about twice a second (and with some
/// jitter), so the clock runs on its own high-resolution timer and is gently pulled toward each
/// report. Big differences (a seek) snap immediately; small ones are corrected a bit at a time so
/// the animation never visibly jumps.
/// </summary>
internal sealed class PlaybackClock
{
    /// <summary>A difference bigger than this is treated as a seek and snapped to.</summary>
    private const double SeekThreshold = 0.4;

    /// <summary>Fraction of a small difference corrected per report.</summary>
    private const double SlewFactor = 0.5;

    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private double _anchorPosition;
    private double _anchorWall;
    private double _rate = 1;
    private double _pausedAtWall;

    public bool IsPlaying { get; private set; }

    /// <summary>Seconds on the clock's own timer; used for animations that run regardless of the song.</summary>
    public double WallSeconds => _wall.Elapsed.TotalSeconds;

    /// <summary>Current song position in seconds.</summary>
    public double Position => IsPlaying ? _anchorPosition + (WallSeconds - _anchorWall) * _rate : _anchorPosition;

    /// <summary>How long playback has been paused (0 while playing).</summary>
    public double SecondsPaused => IsPlaying ? 0 : WallSeconds - _pausedAtWall;

    /// <summary>Starts over for a new song.</summary>
    public void Reset()
    {
        Anchor(0);
        IsPlaying = false;
        _pausedAtWall = WallSeconds;
    }

    public void Sync(PlaybackSample sample)
    {
        // The sample was read on a background thread; move it forward by the time it took to get here.
        double reported = sample.PositionSeconds;
        if (sample.IsPlaying)
        {
            reported += Stopwatch.GetElapsedTime(sample.Timestamp).TotalSeconds * sample.Rate;
        }

        double current = Position;
        double error = reported - current;
        bool resumed = sample.IsPlaying && !IsPlaying;
        bool paused = !sample.IsPlaying && IsPlaying;

        if (resumed || Math.Abs(error) > SeekThreshold)
        {
            // Starting or resuming: our clock was frozen, so the app's value is the better one.
            Anchor(reported);
        }
        else if (paused)
        {
            // Pausing with only a small difference: keep our own (smoother) value so a word that
            // has just lit up doesn't flicker back off.
            Anchor(current);
        }
        else if (sample.IsPlaying)
        {
            Anchor(current + error * SlewFactor);
        }
        else
        {
            Anchor(reported);
        }

        if (paused)
        {
            _pausedAtWall = WallSeconds;
        }
        IsPlaying = sample.IsPlaying;
        _rate = sample.Rate;
    }

    private void Anchor(double position)
    {
        _anchorPosition = position;
        _anchorWall = WallSeconds;
    }
}
