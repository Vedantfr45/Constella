using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Constellation;

/// <summary>
/// "Constellation" style: every lyric line is a constellation and every word a star. As each
/// word is sung its star fades in, a thin line draws itself from the previous star, and the word
/// appears large and glowing beside it before shrinking into a small dim label.
/// </summary>
internal sealed class ConstellationStyle : ILyricStyle
{
    private const double StarFadeIn = 0.35;
    private const double LinkDrawSeconds = 0.3;
    private const double LinkOpacity = 0.5;
    private const double EmphasisRise = 0.2;
    private const double EmphasisFall = 0.45;
    private const double RestingLabelOpacity = 0.85;
    private const double HoldAfterLastWord = 5.0;
    private const double MeteorChance = 0.35;
    private const double MeteorMinGapSeconds = 6;
    private const int NoLine = int.MinValue;

    private readonly Grid _root = new() { IsHitTestVisible = false };
    private readonly DrawLayer _baseLayer;
    private readonly DrawLayer _glowLayer;
    private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 28, Opacity = 0.9 };
    private readonly List<LineConstellation> _live = new();
    private readonly List<Meteor> _meteors = new();
    private readonly Random _random = new();

    private LyricSheet? _sheet;
    private int _lineIndex = NoLine;
    private LyricLook _look = new(LyricFonts.All[0], 1);
    private Size _size;
    private double _pixelsPerDip = 1;
    private double _lastMeteorWall = double.NegativeInfinity;
    private bool _drewLastFrame;
    private FrameContext _frame;
    private PaletteBrushes? _brushes;

    public ConstellationStyle()
    {
        _baseLayer = new DrawLayer(DrawBase);
        _glowLayer = new DrawLayer(DrawGlow) { Effect = _glow };
        _root.Children.Add(_baseLayer);
        _root.Children.Add(_glowLayer);
    }

    public UIElement Visual => _root;

    public void SetLook(LyricLook look)
    {
        _look = look;
        Relayout();
    }

    public void SetSheet(LyricSheet? sheet, double wall)
    {
        foreach (var constellation in _live)
        {
            constellation.BeginFade(wall);
        }
        _sheet = sheet;
        _lineIndex = NoLine;
    }

    public bool Update(in FrameContext frame)
    {
        _frame = frame;
        var size = new Size(_root.ActualWidth, _root.ActualHeight);
        if (size.Width < 1 || size.Height < 1)
        {
            return false;
        }
        if (size != _size || Math.Abs(frame.PixelsPerDip - _pixelsPerDip) > 0.001)
        {
            _size = size;
            _pixelsPerDip = frame.PixelsPerDip;
            Relayout();
        }
        if (_brushes is null || _brushes.Palette != frame.Palette)
        {
            _brushes = new PaletteBrushes(frame.Palette);
            _glow.Color = frame.Palette.Lyric;
        }

        AdvanceLine(frame);
        double wall = frame.Wall;
        _live.RemoveAll(c => c.IsGone(wall));
        _meteors.RemoveAll(m => m.IsDone(wall));

        bool visible = _live.Count > 0 || _meteors.Count > 0;
        if (visible || _drewLastFrame)
        {
            // One extra redraw after everything is gone clears the last frame.
            _baseLayer.InvalidateVisual();
            _glowLayer.InvalidateVisual();
        }
        _drewLastFrame = visible;
        return visible;
    }

    /// <summary>Starts a new constellation when the song reaches a new line (or seeks to one).</summary>
    private void AdvanceLine(in FrameContext frame)
    {
        if (_sheet is null)
        {
            return;
        }

        int index = _sheet.IndexAt(frame.SongTime);
        if (index != _lineIndex)
        {
            bool naturalNext = _lineIndex >= 0 && index == _lineIndex + 1;
            foreach (var old in _live)
            {
                old.BeginFade(frame.Wall);
            }
            if (index >= 0 && _sheet.Lines[index].Words.Count > 0)
            {
                _live.Add(Build(_sheet.Lines[index], index));
            }
            if (naturalNext && frame.Wall - _lastMeteorWall > MeteorMinGapSeconds && _random.NextDouble() < MeteorChance)
            {
                _meteors.Add(new Meteor(_size, _random, frame.Wall));
                _lastMeteorWall = frame.Wall;
            }
            _lineIndex = index;
        }

        // Long instrumental after a line: let the constellation go instead of hanging around.
        var current = _live.LastOrDefault(c => !c.IsFading);
        if (current is not null && frame.SongTime > current.Line.LastWordEnd + HoldAfterLastWord)
        {
            current.BeginFade(frame.Wall);
        }
    }

    private LineConstellation Build(LyricLine line, int index) => new(
        line,
        index,
        ConstellationLayout.Arrange(line, index, _size, _look, _pixelsPerDip),
        ConstellationLayout.DriftFor(line, index, _size));

    /// <summary>Recomputes star and label positions after a size, font or scale change.</summary>
    private void Relayout()
    {
        if (_size.Width < 1)
        {
            return;
        }
        foreach (var constellation in _live)
        {
            constellation.Nodes = ConstellationLayout.Arrange(constellation.Line, constellation.LineIndex, _size, _look, _pixelsPerDip);
        }
    }

    /// <summary>
    /// How "current" a word is: rises quickly when it starts, falls smoothly when the next word
    /// starts. 1 = large glowing label, 0 = small resting label.
    /// </summary>
    private static double Emphasis(StarNode node, double time)
    {
        if (time < node.Word.Start)
        {
            return 0;
        }
        double rise = Ease.OutCubic(Ease.Progress(time, node.Word.Start, EmphasisRise));
        double fall = Ease.InOutCubic(Ease.Progress(time, node.ReleaseAt, EmphasisFall));
        return rise * (1 - fall);
    }

    /// <summary>Lines, stars, resting labels and shooting stars.</summary>
    private void DrawBase(DrawingContext dc)
    {
        if (_brushes is null)
        {
            return;
        }
        double t = _frame.SongTime;
        double wall = _frame.Wall;
        double unit = ConstellationLayout.Unit(_size, _look);

        foreach (var constellation in _live)
        {
            double alpha = constellation.Alpha(wall);
            if (alpha <= 0.003)
            {
                continue;
            }
            var nodes = constellation.Nodes;
            var offset = constellation.Offset(wall);
            dc.PushTransform(new TranslateTransform(offset.X, offset.Y));

            // Links: each draws itself from the previous star toward this one.
            dc.PushOpacity(alpha * LinkOpacity);
            for (int i = 1; i < nodes.Length && t >= nodes[i].Word.Start; i++)
            {
                double draw = Ease.OutCubic(Ease.Progress(t, nodes[i].Word.Start, LinkDrawSeconds));
                Point from = nodes[i - 1].Star;
                dc.DrawLine(_brushes.LinkPen, from, from + (nodes[i].Star - from) * draw);
            }
            dc.Pop();

            for (int i = 0; i < nodes.Length && t >= nodes[i].Word.Start; i++)
            {
                var node = nodes[i];
                double appear = Ease.Progress(t, node.Word.Start, StarFadeIn);
                double emphasis = Ease.OutCubic(Emphasis(node, t));
                double twinkle = 1 + 0.08 * Math.Sin(wall * 2.4 + node.TwinklePhase);
                double radius = Ease.Lerp(ConstellationLayout.RestingStarUnits, ConstellationLayout.ActiveStarUnits, emphasis)
                                * unit * Ease.Lerp(0.4, 1, Ease.OutBack(appear)) * twinkle;
                Sparkle.Draw(
                    dc, node.Star, radius,
                    emphasis > 0.5 ? _brushes.Highlight : _brushes.Lyric,
                    emphasis > 0.5 ? _brushes.HighlightHalo : _brushes.LyricHalo,
                    alpha * Ease.OutCubic(appear) * Ease.Lerp(0.75, 1, emphasis),
                    haloScale: Ease.Lerp(3, 4.5, emphasis),
                    haloOpacity: Ease.Lerp(0.22, 0.55, emphasis));

                // The small label fades in as the big one shrinks away (they meet at the same size).
                double resting = alpha * RestingLabelOpacity * Ease.InOutCubic(Ease.Progress(t, node.ReleaseAt, EmphasisFall));
                if (resting > 0.01)
                {
                    node.Resting.SetForegroundBrush(_brushes.Secondary);
                    dc.PushOpacity(resting);
                    dc.DrawText(node.Resting, ConstellationLayout.LabelOrigin(
                        node.Star, node.Resting, node.Side, node.Nudge, ConstellationLayout.RestingGapUnits * unit));
                    dc.Pop();
                }
            }
            dc.Pop();
        }

        foreach (var meteor in _meteors)
        {
            meteor.Draw(dc, wall, _brushes);
        }
    }

    /// <summary>The large current word. This layer carries the glow effect.</summary>
    private void DrawGlow(DrawingContext dc)
    {
        if (_brushes is null)
        {
            return;
        }
        double t = _frame.SongTime;
        double wall = _frame.Wall;
        double unit = ConstellationLayout.Unit(_size, _look);
        double minScale = ConstellationLayout.RestingTextUnits / ConstellationLayout.ActiveTextUnits;

        foreach (var constellation in _live)
        {
            double alpha = constellation.Alpha(wall);
            var offset = constellation.Offset(wall);
            foreach (var node in constellation.Nodes)
            {
                double emphasis = Emphasis(node, t);
                if (emphasis <= 0.002 || alpha <= 0.003)
                {
                    continue;
                }
                double eased = Ease.OutCubic(emphasis);
                double scale = Ease.Lerp(minScale, 1, eased);
                double gap = Ease.Lerp(ConstellationLayout.RestingGapUnits, ConstellationLayout.ActiveGapUnits, eased) * unit;
                var text = node.Active;
                text.SetForegroundBrush(_brushes.Highlight);

                // Anchor beside the star and scale around that point, so the word grows out of its star.
                double anchorX = node.Star.X + offset.X + node.ActiveSide * gap;
                double anchorY = node.Star.Y + offset.Y;
                var local = new Point(node.ActiveSide > 0 ? 0 : -text.Width, -text.Height / 2 + node.ActiveNudge * text.Height);

                dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, anchorX, anchorY));
                dc.PushOpacity(alpha * emphasis);
                dc.DrawText(text, local);
                dc.Pop();
                dc.Pop();
            }
        }
    }
}
