using System;
using System.Windows;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Overlay.Constellation;

/// <summary>A shooting star that streaks across the sky between two lines.</summary>
internal sealed class Meteor
{
    private const double Duration = 1.1;

    private readonly Point _start;
    private readonly Vector _direction;
    private readonly double _distance;
    private readonly double _tailLength;
    private readonly double _startWall;

    public Meteor(Size area, Random random, double wall)
    {
        _startWall = wall;
        bool leftToRight = random.NextDouble() < 0.5;
        double angle = random.Range(14, 30) * Math.PI / 180;
        _direction = new Vector(Math.Cos(angle) * (leftToRight ? 1 : -1), Math.Sin(angle));
        double x = leftToRight ? random.Range(0.08, 0.45) : random.Range(0.55, 0.92);
        _start = new Point(area.Width * x, area.Height * random.Range(0.06, 0.26));
        _distance = area.Width * random.Range(0.22, 0.36);
        _tailLength = area.Height * 0.13;
    }

    public bool IsDone(double wall) => wall - _startWall >= Duration;

    public void Draw(DrawingContext dc, double wall, PaletteBrushes brushes)
    {
        double p = Ease.Progress(wall, _startWall, Duration);
        double presence = Math.Sin(Math.PI * p);
        if (presence <= 0.01)
        {
            return;
        }

        Point head = _start + _direction * (_distance * Ease.OutCubic(p));
        Point tail = head - _direction * (_tailLength * (0.35 + 0.65 * presence));
        Color color = brushes.Palette.Highlight;

        var gradient = new LinearGradientBrush(Ease.WithAlpha(color, 0), color, tail, head)
        {
            MappingMode = BrushMappingMode.Absolute,
        };
        gradient.Freeze();
        var pen = new Pen(gradient, 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        dc.PushOpacity(presence * 0.9);
        dc.DrawLine(pen, tail, head);
        dc.DrawEllipse(brushes.Highlight, null, head, 1.5, 1.5);
        dc.Pop();
    }
}
