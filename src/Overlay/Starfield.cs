using System;
using System.Windows;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Overlay;

/// <summary>Faint, slowly twinkling background stars.</summary>
internal sealed class Starfield : FrameworkElement
{
    private const int StarCount = 150;
    private const int AlphaLevels = 24;

    private readonly record struct Star(Point Position, double Radius, double Brightness, double Speed, double Phase);

    private Star[] _stars = Array.Empty<Star>();
    private Size _builtFor;
    private Brush[] _levels = Array.Empty<Brush>();
    private Color _color;
    private double _wall;
    private int _frame;

    public Starfield() => IsHitTestVisible = false;

    /// <summary>Twinkling is slow, so redrawing every other frame is plenty.</summary>
    public void Update(double wall, Color color)
    {
        _wall = wall;
        if (color != _color || _levels.Length == 0)
        {
            _color = color;
            _levels = new Brush[AlphaLevels + 1];
            for (int i = 0; i <= AlphaLevels; i++)
            {
                var brush = new SolidColorBrush(Ease.WithAlpha(color, i / (double)AlphaLevels));
                brush.Freeze();
                _levels[i] = brush;
            }
            InvalidateVisual();
        }
        else if (++_frame % 2 == 0)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width < 1 || _levels.Length == 0)
        {
            return;
        }
        if (size != _builtFor)
        {
            Build(size);
        }

        foreach (var star in _stars)
        {
            double twinkle = 0.55 + 0.45 * Math.Sin(_wall * star.Speed + star.Phase);
            int level = (int)Math.Round(Ease.Clamp01(star.Brightness * twinkle) * AlphaLevels);
            if (level > 0)
            {
                dc.DrawEllipse(_levels[level], null, star.Position, star.Radius, star.Radius);
            }
        }
    }

    private void Build(Size size)
    {
        _builtFor = size;
        var random = new Random(1977); // Fixed seed: the same sky every time.
        _stars = new Star[StarCount];
        for (int i = 0; i < StarCount; i++)
        {
            bool bright = random.NextDouble() < 0.08;
            _stars[i] = new Star(
                new Point(random.NextDouble() * size.Width, random.NextDouble() * size.Height),
                bright ? random.Range(1.1, 1.6) : random.Range(0.45, 1.0),
                bright ? random.Range(0.35, 0.55) : random.Range(0.08, 0.3),
                random.Range(0.4, 1.8),
                random.Range(0, Math.PI * 2));
        }
    }
}
