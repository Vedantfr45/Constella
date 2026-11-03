using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;
using Constella.Media;

namespace Constella.Overlay;

/// <summary>
/// A quiet title card when a song starts (title, artist), which also says "No synced lyrics"
/// when none could be found. It fades away on its own.
/// </summary>
internal sealed class TrackCard : FrameworkElement
{
    private const double FadeIn = 0.9;
    private const double FadeOut = 1.2;
    private const double MinShow = 1.6;
    private const double MaxShowWithLyrics = 4.5;
    private const double ShowWithoutLyrics = 6.0;

    private TrackInfo? _track;
    private LyricsStatus? _status;
    private double _firstLineStart;
    private double _shownAt;
    private double? _fadeOutAt;
    private double _alpha;
    private double _wall;
    private LyricPalette _palette;
    private LyricFont _font = LyricFonts.All[0];
    private double _scale = 1;

    public TrackCard() => IsHitTestVisible = false;

    public void SetLook(LyricLook look)
    {
        _font = look.Font;
        _scale = look.TextScale;
        InvalidateVisual();
    }

    public void Show(TrackInfo? track, double wall)
    {
        _track = track;
        _status = null;
        _shownAt = wall;
        _fadeOutAt = track is null ? wall : null;
    }

    public void SetLyrics(LyricsResult result)
    {
        _status = result.Status;
        _firstLineStart = result.Sheet?.FirstLineStart ?? 0;
    }

    /// <returns>True while the card is visible.</returns>
    public bool Update(double wall, double songTime, LyricPalette palette)
    {
        _wall = wall;
        _palette = palette;
        if (_track is null && _alpha <= 0)
        {
            return false;
        }

        double age = wall - _shownAt;
        if (_fadeOutAt is null && age > MinShow)
        {
            bool done = _status switch
            {
                LyricsStatus.Found => songTime >= _firstLineStart - 0.6 || age > MaxShowWithLyrics,
                LyricsStatus.NotFound or LyricsStatus.Failed => age > ShowWithoutLyrics,
                _ => age > 10, // Still looking: don't sit on screen forever.
            };
            if (done)
            {
                _fadeOutAt = wall;
            }
        }

        double fadeIn = Ease.OutCubic(Ease.Progress(wall, _shownAt, FadeIn));
        double fadeOut = _fadeOutAt is double start ? 1 - Ease.InOutSine(Ease.Progress(wall, start, FadeOut)) : 1;
        double alpha = fadeIn * fadeOut;
        bool changed = Math.Abs(alpha - _alpha) > 0.001;
        _alpha = alpha;
        if (changed)
        {
            InvalidateVisual();
        }
        return alpha > 0.001;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_track is null || _alpha <= 0.001 || ActualHeight < 1)
        {
            return;
        }
        double unit = ActualHeight / 1000.0 * _scale;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double rise = (1 - Ease.OutCubic(Ease.Progress(_wall, _shownAt, FadeIn))) * 10 * unit;
        double centerX = ActualWidth / 2;
        double y = ActualHeight * 0.44 + rise;

        var title = Text(_track.Title, _font.Active, 34 * unit, _palette.Highlight, ppd);
        title.MaxTextWidth = ActualWidth * 0.7;
        title.MaxLineCount = 2;
        title.Trimming = TextTrimming.CharacterEllipsis;
        title.TextAlignment = TextAlignment.Center;

        dc.PushOpacity(_alpha);
        dc.DrawText(title, new Point(centerX - title.MaxTextWidth / 2, y - title.Height));
        y += 10 * unit;

        if (_track.Artist.Length > 0)
        {
            var artist = Text(_track.Artist.ToUpperInvariant(), _font.Resting, 14 * unit, _palette.Secondary, ppd);
            dc.DrawText(artist, new Point(centerX - artist.Width / 2, y));
            y += artist.Height + 18 * unit;
        }

        if (_status is LyricsStatus.NotFound or LyricsStatus.Failed)
        {
            string note = _status == LyricsStatus.NotFound ? "No synced lyrics" : "Lyrics unavailable offline";
            var status = Text(note, _font.Resting, 13 * unit, _palette.Secondary, ppd);
            dc.PushOpacity(0.6);
            dc.DrawText(status, new Point(centerX - status.Width / 2, y));
            dc.Pop();
        }
        dc.Pop();
    }

    private static FormattedText Text(string text, Typeface typeface, double size, Color color, double ppd)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Math.Max(size, 4), brush, ppd);
    }
}
