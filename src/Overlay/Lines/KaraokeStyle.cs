using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// "Karaoke": the line sits centred and fills with colour from left to right as it is sung, with
/// a soft leading edge; the next line waits underneath, small and dim, so you can read ahead.
/// </summary>
internal sealed class KaraokeStyle : LineStyle<KaraokeStyle.Layout>
{
    private const double TextUnits = 66;
    private const double NextTextUnits = 38;
    private const double UnsungOpacity = 0.5;
    private const double NextOpacity = 0.4;

    /// <summary>Width of the soft fill edge, in font sizes.</summary>
    private const double Feather = 0.35;

    /// <param name="Next">The following line, or null at the end of the song.</param>
    internal sealed record Layout(WordFlow Current, WordFlow? Next);

    protected override Layout Arrange(LyricLine line, int index)
    {
        // The current line gets most of the safe area; the next line fits in what's left below it.
        var safe = Safe;
        double middle = Area.Height * 0.47;
        var current = WordFlow.Centered(line.Words, Look.Font.Active, TextUnits * Unit, safe.Width,
            new Point(Area.Width / 2, middle), PixelsPerDip, maxHeight: safe.Height * 0.6);

        var nextLine = Sheet?.Lines.Skip(index + 1).FirstOrDefault(l => l.Words.Count > 0);
        WordFlow? next = null;
        if (nextLine is not null)
        {
            double top = middle + current.Height / 2 + NextTextUnits * Unit * 0.9;
            double room = safe.Bottom - top;
            if (room > NextTextUnits * Unit * 0.5)
            {
                // Set it once to learn its size, then again just below the current line.
                var probe = WordFlow.Centered(nextLine.Words, Look.Font.Resting, NextTextUnits * Unit, safe.Width,
                    new Point(Area.Width / 2, 0), PixelsPerDip, maxHeight: room);
                next = WordFlow.Centered(nextLine.Words, Look.Font.Resting, probe.FontSize, safe.Width,
                    new Point(Area.Width / 2, top + probe.Height / 2), PixelsPerDip);
            }
        }
        return new Layout(current, next);
    }

    protected override void DrawLine(DrawingContext dc, LiveLine line, double enter, double exit, bool glowPass)
    {
        var flow = line.Layout.Current;
        double t = Frame.SongTime;
        dc.PushTransform(new TranslateTransform(0, Shift(enter, exit)));
        dc.PushOpacity(enter * (1 - exit));

        for (int i = 0; i < flow.Texts.Length; i++)
        {
            var text = flow.Texts[i];
            var box = flow.Boxes[i];
            var word = line.Line.Words[i];
            double lit = Ease.Clamp01((t - word.Start) / Math.Max(0.05, word.End - word.Start));
            if (!glowPass)
            {
                // The unsung part, under the fill.
                if (lit < 1)
                {
                    text.SetForegroundBrush(Brushes.Secondary);
                    dc.PushOpacity(UnsungOpacity);
                    dc.DrawText(text, box.Location);
                    dc.Pop();
                }
                continue;
            }
            if (lit <= 0)
            {
                continue;
            }
            text.SetForegroundBrush(Brushes.Highlight);
            if (lit >= 1)
            {
                dc.DrawText(text, box.Location);
                continue;
            }
            dc.PushOpacityMask(FillMask(box.X, text.Width, flow.FontSize, lit));
            dc.DrawText(text, box.Location);
            dc.Pop();
        }

        // The next line fades away as this one leaves, while it takes this one's place.
        if (!glowPass && line.Layout.Next is WordFlow next)
        {
            dc.PushOpacity(NextOpacity);
            for (int i = 0; i < next.Texts.Length; i++)
            {
                next.Texts[i].SetForegroundBrush(Brushes.Secondary);
                dc.DrawText(next.Texts[i], next.Boxes[i].Location);
            }
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Opaque up to the fill point, then a soft ramp to clear, across one word.</summary>
    private static Brush FillMask(double left, double width, double fontSize, double lit)
    {
        double feather = fontSize * Feather;
        // The ramp runs from -feather to width, so lit = 0 is fully clear and lit = 1 fully filled.
        double edge = left - feather + (width + feather) * lit;
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(edge, 0),
            EndPoint = new Point(edge + feather, 0),
        };
        brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }
}
