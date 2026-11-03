using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Media;

namespace Constella.LockScreen;

/// <summary>
/// Everything on the lock screen that isn't lyrics: the date and large clock at the top, the
/// now-playing card (cover, title, artist, progress) near the bottom, and a quiet exit hint.
/// Sizes are in units of 1/1000 of the screen height.
/// </summary>
internal sealed class LockScreenChrome : FrameworkElement
{
    /// <summary>Where the lyrics area starts and ends, as fractions of the height (between the clock and the card).</summary>
    public const double LyricsTop = 0.30;
    public const double LyricsBottom = 0.78;

    private static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");

    private TrackInfo? _track;
    private ImageSource? _cover;
    private double _position;
    private bool _clock24Hour;
    private string _shownTime = string.Empty;
    private int _shownProgressPixel = -1;

    public LockScreenChrome() => IsHitTestVisible = false;

    public void SetTrack(TrackInfo? track, ImageSource? cover)
    {
        _track = track;
        _cover = cover;
        InvalidateVisual();
    }

    public void SetClockFormat(bool clock24Hour)
    {
        _clock24Hour = clock24Hour;
        InvalidateVisual();
    }

    /// <summary>Redraws only when the minute or the progress bar visibly changes.</summary>
    public void Update(double songPosition)
    {
        _position = songPosition;
        string time = TimeText();
        int progressPixel = (int)(Progress() * 1000);
        if (time != _shownTime || progressPixel != _shownProgressPixel)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1)
        {
            return;
        }
        double u = h / 1000.0;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var now = DateTime.Now;

        // Date, then the big clock right under it.
        var date = Label(now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture), Display, FontWeights.SemiBold, 24 * u, 0.85, ppd);
        dc.DrawText(date, new Point(w / 2 - date.Width / 2, 62 * u));
        _shownTime = TimeText();
        var clock = Label(_shownTime, Display, FontWeights.SemiBold, 150 * u, 0.92, ppd);
        dc.DrawText(clock, new Point(w / 2 - clock.Width / 2, 82 * u));

        if (_track is not null)
        {
            DrawNowPlaying(dc, w, h, u, ppd);
        }

        var hint = Label("Press any key to exit", Text, FontWeights.Normal, 13 * u, 0.42, ppd);
        dc.DrawText(hint, new Point(w / 2 - hint.Width / 2, h - 46 * u));
    }

    /// <summary>A frosted card: cover on the left, title and artist beside it, progress bar and times underneath.</summary>
    private void DrawNowPlaying(DrawingContext dc, double w, double h, double u, double ppd)
    {
        var track = _track!;
        double cardWidth = 480 * u, pad = 18 * u, art = 64 * u;
        var card = new Rect(w / 2 - cardWidth / 2, h * LyricsBottom + 30 * u, cardWidth, pad + art + 16 * u + 4 * u + 26 * u + pad * 0.6);
        dc.DrawRoundedRectangle(Frozen(Color.FromArgb(34, 255, 255, 255)), new Pen(Frozen(Color.FromArgb(36, 255, 255, 255)), 1),
            card, 24 * u, 24 * u);

        var artBox = new Rect(card.X + pad, card.Y + pad, art, art);
        if (_cover is not null)
        {
            dc.PushClip(new RectangleGeometry(artBox, 10 * u, 10 * u));
            dc.DrawImage(_cover, artBox);
            dc.Pop();
        }
        else
        {
            dc.DrawRoundedRectangle(Frozen(Color.FromArgb(40, 255, 255, 255)), null, artBox, 10 * u, 10 * u);
        }

        double textX = artBox.Right + 16 * u, textWidth = card.Right - pad - textX;
        var title = Label(track.Title, Text, FontWeights.SemiBold, 19 * u, 0.95, ppd, textWidth);
        var artist = Label(track.Artist, Text, FontWeights.Normal, 16 * u, 0.62, ppd, textWidth);
        double textTop = artBox.Y + art / 2 - (title.Height + artist.Height) / 2;
        dc.DrawText(title, new Point(textX, textTop));
        dc.DrawText(artist, new Point(textX, textTop + title.Height));

        // Progress bar (only when the app tells us the song length).
        if (track.DurationSeconds is double duration)
        {
            double progress = Progress();
            _shownProgressPixel = (int)(progress * 1000);
            var bar = new Rect(card.X + pad, artBox.Bottom + 16 * u, cardWidth - 2 * pad, 4 * u);
            dc.DrawRoundedRectangle(Frozen(Color.FromArgb(64, 255, 255, 255)), null, bar, bar.Height / 2, bar.Height / 2);
            dc.DrawRoundedRectangle(Frozen(Color.FromArgb(230, 255, 255, 255)), null,
                new Rect(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * progress), bar.Height), bar.Height / 2, bar.Height / 2);

            double elapsed = Math.Clamp(_position, 0, duration);
            var left = Label(Minutes(elapsed), Text, FontWeights.Normal, 12 * u, 0.55, ppd);
            var right = Label("-" + Minutes(duration - elapsed), Text, FontWeights.Normal, 12 * u, 0.55, ppd);
            dc.DrawText(left, new Point(bar.X, bar.Bottom + 6 * u));
            dc.DrawText(right, new Point(bar.Right - right.Width, bar.Bottom + 6 * u));
        }
    }

    private double Progress() =>
        _track?.DurationSeconds is double d && d > 0 ? Ease.Clamp01(_position / d) : 0;

    /// <summary>"5:07" (12-hour, no AM/PM, like a phone) or "17:07".</summary>
    private string TimeText() => DateTime.Now.ToString(_clock24Hour ? "HH:mm" : "h:mm", CultureInfo.InvariantCulture);

    private static string Minutes(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private static FormattedText Label(string text, FontFamily family, FontWeight weight, double size, double opacity, double ppd, double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), Math.Max(size, 4),
            Frozen(Color.FromArgb((byte)Math.Round(255 * opacity), 255, 255, 255)), ppd);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }
        return formatted;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
