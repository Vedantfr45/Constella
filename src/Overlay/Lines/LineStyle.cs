using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// Base for styles that show one lyric line at a time in the middle of the screen. It follows the
/// song from line to line, keeps the outgoing line around while it animates away, and draws in
/// two passes: a plain layer, and a layer with a soft glow for the words in the spotlight.
/// A subclass only decides how a line is laid out and how it is drawn at a given moment.
/// </summary>
/// <typeparam name="TLayout">Whatever the style precomputes for a line (text and positions).</typeparam>
internal abstract class LineStyle<TLayout> : ILyricStyle where TLayout : class
{
    /// <summary>How long a new line takes to arrive and an old one to leave.</summary>
    protected const double EnterSeconds = 0.45;
    protected const double ExitSeconds = 0.4;

    /// <summary>After the last word, a line stays this long before leaving on its own (instrumental breaks).</summary>
    private const double HoldAfterLastWord = 5.0;
    private const int NoLine = int.MinValue;

    /// <summary>How far a line rises in and drifts out, in <see cref="Unit"/>s.</summary>
    private const double ShiftUnits = 28;

    private readonly Grid _root = new() { IsHitTestVisible = false };
    private readonly DrawLayer _baseLayer;
    private readonly DrawLayer _glowLayer;
    private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 26, Opacity = 0.85 };
    private readonly List<LiveLine> _live = new();
    private LyricSheet? _sheet;
    private int _lineIndex = NoLine;
    private bool _drewLastFrame;
    private PaletteBrushes? _brushes;

    protected LineStyle()
    {
        _baseLayer = new DrawLayer(dc => DrawAll(dc, glowPass: false));
        _glowLayer = new DrawLayer(dc => DrawAll(dc, glowPass: true)) { Effect = _glow };
        _root.Children.Add(_baseLayer);
        _root.Children.Add(_glowLayer);
    }

    /// <summary>A line on screen, with its timing on the app clock.</summary>
    protected sealed class LiveLine
    {
        public LiveLine(LyricLine line, int index, double enteredAt, TLayout layout)
        {
            Line = line;
            Index = index;
            EnteredAt = enteredAt;
            Layout = layout;
        }

        public LyricLine Line { get; }

        public int Index { get; }

        public double EnteredAt { get; }

        public double? LeftAt { get; set; }

        public TLayout Layout { get; set; }
    }

    public UIElement Visual => _root;

    protected LyricLook Look { get; private set; } = new(LyricFonts.All[0], 1);

    /// <summary>The style's drawing area in device-independent pixels.</summary>
    protected Size Area { get; private set; }

    protected double PixelsPerDip { get; private set; } = 1;

    /// <summary>The frame being drawn.</summary>
    protected FrameContext Frame { get; private set; }

    /// <summary>The lyrics being followed (for styles that look ahead to the next line).</summary>
    protected LyricSheet? Sheet => _sheet;

    /// <summary>Brushes for this frame's palette (set before anything is drawn).</summary>
    protected PaletteBrushes Brushes => _brushes!;

    /// <summary>1/1000 of the reference height, times the user's text size: sizes scale with any screen.</summary>
    protected double Unit => Look.Unit(Area);

    /// <summary>Where text may go: the area minus a margin that keeps it clear of the edges and the glow.</summary>
    protected Rect Safe => SafeArea.Of(Area);

    public void SetLook(LyricLook look)
    {
        Look = look;
        Relayout();
    }

    public void SetSheet(LyricSheet? sheet, double wall)
    {
        foreach (var line in _live)
        {
            line.LeftAt ??= wall;
        }
        _sheet = sheet;
        _lineIndex = NoLine;
    }

    public bool Update(in FrameContext frame)
    {
        Frame = frame;
        var size = new Size(_root.ActualWidth, _root.ActualHeight);
        if (size.Width < 1 || size.Height < 1)
        {
            return false;
        }
        if (size != Area || Math.Abs(frame.PixelsPerDip - PixelsPerDip) > 0.001)
        {
            Area = size;
            PixelsPerDip = frame.PixelsPerDip;
            Relayout();
        }
        if (_brushes is null || _brushes.Palette != frame.Palette)
        {
            _brushes = new PaletteBrushes(frame.Palette);
            _glow.Color = frame.Palette.Lyric;
            OnPaletteChanged(frame.Palette);
        }

        AdvanceLine(frame);
        double wall = frame.Wall;
        _live.RemoveAll(l => l.LeftAt is double left && wall > left + ExitSeconds);

        bool visible = _live.Count > 0;
        if (visible || _drewLastFrame)
        {
            // One extra redraw after everything is gone clears the last frame.
            _baseLayer.InvalidateVisual();
            _glowLayer.InvalidateVisual();
        }
        _drewLastFrame = visible;
        return visible;
    }

    /// <summary>Precomputes text and positions for a line at the current size and font.</summary>
    protected abstract TLayout Arrange(LyricLine line, int index);

    /// <summary>Draws one line.</summary>
    /// <param name="enter">0..1 as the line arrives (eased).</param>
    /// <param name="exit">0..1 as the line leaves (eased); 0 while it is current.</param>
    /// <param name="glowPass">True for the glowing layer (words in the spotlight), false for the plain one.</param>
    protected abstract void DrawLine(DrawingContext dc, LiveLine line, double enter, double exit, bool glowPass);

    /// <summary>The line as centred text in the middle of the area, shrunk if needed to stay inside <see cref="Safe"/>.</summary>
    /// <param name="textUnits">Font size in <see cref="Unit"/>s.</param>
    protected WordFlow CenteredFlow(LyricLine line, double textUnits, Typeface typeface) => WordFlow.Centered(
        line.Words, typeface, textUnits * Unit, Safe.Width, new Point(Area.Width / 2, Area.Height / 2), PixelsPerDip,
        maxHeight: Safe.Height - 2 * ShiftUnits * Unit);

    /// <summary>
    /// One word at <paramref name="fontSize"/>, made smaller if it would be wider or taller than
    /// the given room (for styles that draw single big words).
    /// </summary>
    protected FormattedText FittedWord(string word, Typeface typeface, double fontSize, double maxWidth, double maxHeight)
    {
        var text = WordFlow.Text(word, typeface, fontSize, PixelsPerDip);
        double fit = Math.Min(maxWidth / Math.Max(1, text.Width), maxHeight / Math.Max(1, text.Height));
        return fit < 1 ? WordFlow.Text(word, typeface, fontSize * fit, PixelsPerDip) : text;
    }

    /// <summary>Vertical offset for a line that rises in from below and drifts up as it leaves.</summary>
    protected double Shift(double enter, double exit) => ((1 - enter) * ShiftUnits - exit * ShiftUnits) * Unit;

    /// <summary>Lets a style derive extra colours when the palette changes.</summary>
    protected virtual void OnPaletteChanged(LyricPalette palette)
    {
    }

    /// <summary>
    /// How "current" a word is: rises quickly as it is sung and falls as the next word starts.
    /// 1 = in the spotlight, 0 = not yet sung or already done.
    /// </summary>
    protected static double Emphasis(LyricLine line, int wordIndex, double time, double rise = 0.15, double fall = 0.3)
    {
        var word = line.Words[wordIndex];
        if (time < word.Start)
        {
            return 0;
        }
        double release = wordIndex + 1 < line.Words.Count ? line.Words[wordIndex + 1].Start : word.End + 0.6;
        return Ease.OutCubic(Ease.Progress(time, word.Start, rise)) * (1 - Ease.InOutCubic(Ease.Progress(time, release, fall)));
    }

    private void AdvanceLine(in FrameContext frame)
    {
        if (_sheet is null)
        {
            return;
        }

        int index = _sheet.IndexAt(frame.SongTime);
        if (index != _lineIndex)
        {
            foreach (var old in _live)
            {
                old.LeftAt ??= frame.Wall;
            }
            if (index >= 0 && _sheet.Lines[index].Words.Count > 0 && Area.Width >= 1)
            {
                var line = _sheet.Lines[index];
                _live.Add(new LiveLine(line, index, frame.Wall, Arrange(line, index)));
            }
            _lineIndex = index;
        }

        // Long instrumental after a line: let it go instead of hanging around.
        var current = _live.LastOrDefault(l => l.LeftAt is null);
        if (current is not null && frame.SongTime > current.Line.LastWordEnd + HoldAfterLastWord)
        {
            current.LeftAt = frame.Wall;
        }
    }

    private void Relayout()
    {
        if (Area.Width < 1)
        {
            return;
        }
        foreach (var line in _live)
        {
            line.Layout = Arrange(line.Line, line.Index);
        }
    }

    private void DrawAll(DrawingContext dc, bool glowPass)
    {
        double wall = Frame.Wall;
        foreach (var line in _live)
        {
            double enter = Ease.OutCubic(Ease.Progress(wall, line.EnteredAt, EnterSeconds));
            double exit = line.LeftAt is double left ? Ease.InOutSine(Ease.Progress(wall, left, ExitSeconds)) : 0;
            if (enter * (1 - exit) > 0.003)
            {
                DrawLine(dc, line, enter, exit, glowPass);
            }
        }
    }
}
