using System;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// "Light wipe": karaoke-style. The line waits in a dim colour and light sweeps through it,
/// letter by letter, in time with each word.
/// </summary>
internal sealed class WipeStyle : LineStyle<WordFlow>
{
    private const double TextUnits = 58;
    private const double DimOpacity = 0.38;

    protected override WordFlow Arrange(LyricLine line, int index) => CenteredFlow(line, TextUnits, Look.Font.Active);

    protected override void DrawLine(DrawingContext dc, LiveLine line, double enter, double exit, bool glowPass)
    {
        var flow = line.Layout;
        double t = Frame.SongTime;
        dc.PushTransform(new TranslateTransform(0, Shift(enter, exit)));
        dc.PushOpacity(enter * (1 - exit));
        for (int i = 0; i < flow.Texts.Length; i++)
        {
            var text = flow.Texts[i];
            var box = flow.Boxes[i];
            if (!glowPass)
            {
                text.SetForegroundBrush(Brushes.Secondary);
                dc.PushOpacity(DimOpacity);
                dc.DrawText(text, box.Location);
                dc.Pop();
                continue;
            }

            var word = line.Line.Words[i];
            double lit = Ease.Clamp01((t - word.Start) / Math.Max(0.05, word.End - word.Start));
            if (lit <= 0)
            {
                continue;
            }
            // The clip reaches a little past the glyphs vertically so ascenders and descenders stay whole.
            double pad = flow.FontSize * 0.2;
            var clip = new RectangleGeometry(new Rect(box.X - pad, box.Y - pad, pad + text.Width * lit, box.Height + 2 * pad));
            text.SetForegroundBrush(Brushes.Highlight);
            dc.PushClip(clip);
            dc.DrawText(text, box.Location);
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }
}
