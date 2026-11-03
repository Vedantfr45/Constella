using System.Windows;
using System.Windows.Media;

namespace Constella.Overlay;

/// <summary>The four-point sparkle star used for every node, plus its soft halo.</summary>
internal static class Sparkle
{
    /// <summary>How far the concave sides bow toward the centre (smaller = thinner arms).</summary>
    private const double Waist = 0.13;

    /// <summary>Star outline with a radius of 1, centred on (0, 0).</summary>
    public static Geometry Shape { get; } = CreateShape();

    public static void Draw(DrawingContext dc, Point center, double radius, Brush fill, Brush halo,
                            double opacity, double haloScale, double haloOpacity)
    {
        if (opacity <= 0.004 || radius <= 0.05)
        {
            return;
        }
        double haloRadius = radius * haloScale;
        dc.PushOpacity(opacity * haloOpacity);
        dc.DrawEllipse(halo, null, center, haloRadius, haloRadius);
        dc.Pop();

        dc.PushTransform(new MatrixTransform(radius, 0, 0, radius, center.X, center.Y));
        dc.PushOpacity(opacity);
        dc.DrawGeometry(fill, null, Shape);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>A radial glow brush: solid colour in the middle fading to nothing.</summary>
    public static Brush CreateHalo(Color color)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(color, 0),
                new GradientStop(Color.FromArgb(90, color.R, color.G, color.B), 0.25),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static Geometry CreateShape()
    {
        var tips = new[] { new Point(0, -1), new Point(1, 0), new Point(0, 1), new Point(-1, 0) };
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(tips[0], isFilled: true, isClosed: true);
            for (int i = 1; i <= tips.Length; i++)
            {
                Point from = tips[i - 1], to = tips[i % tips.Length];
                var control = new Point((from.X + to.X) * Waist, (from.Y + to.Y) * Waist);
                ctx.QuadraticBezierTo(control, to, isStroked: false, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
