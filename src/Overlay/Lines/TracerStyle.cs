using System;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// "Underline": a glowing bar glides from word to word underneath the line, and each word
/// brightens as the bar reaches it.
/// </summary>
internal sealed class TracerStyle : LineStyle<WordFlow>
{
    private const double TextUnits = 56;
    private const double GlideSeconds = 0.22;
    private const double UpcomingOpacity = 0.45;
    private const double SungOpacity = 0.92;

    protected override WordFlow Arrange(LyricLine line, int index) => CenteredFlow(line, TextUnits, Look.Font.Active);

    protected override void DrawLine(DrawingContext dc, LiveLine line, double enter, double exit, bool glowPass)
    {
        var flow = line.Layout;
        var words = line.Line.Words;
        double t = Frame.SongTime;
        dc.PushTransform(new TranslateTransform(0, Shift(enter, exit)));
        dc.PushOpacity(enter * (1 - exit));

        int current = -1;
        for (int i = 0; i < words.Count; i++)
        {
            double emphasis = Emphasis(line.Line, i, t, fall: 0.2);
            var text = flow.Texts[i];
            if (glowPass)
            {
                if (emphasis > 0.002)
                {
                    text.SetForegroundBrush(Brushes.Highlight);
                    dc.PushOpacity(emphasis);
                    dc.DrawText(text, flow.Boxes[i].Location);
                    dc.Pop();
                }
            }
            else
            {
                bool sung = t >= words[i].Start;
                text.SetForegroundBrush(sung ? Brushes.Lyric : Brushes.Secondary);
                dc.PushOpacity((sung ? SungOpacity : UpcomingOpacity) * (1 - emphasis));
                dc.DrawText(text, flow.Boxes[i].Location);
                dc.Pop();
            }
            if (t >= words[i].Start)
            {
                current = i;
            }
        }

        if (glowPass && current >= 0)
        {
            DrawBar(dc, flow, current, words[current].Start, line.Line.LastWordEnd, t);
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>The underline, gliding from the previous word to the current one, fading after the line ends.</summary>
    private void DrawBar(DrawingContext dc, WordFlow flow, int current, double start, double lineEnd, double t)
    {
        double glide = Ease.OutCubic(Ease.Progress(t, start, GlideSeconds));
        Rect to = Underline(flow, current);
        // The first word's bar grows out from its left edge.
        Rect from = current > 0 ? Underline(flow, current - 1) : new Rect(to.X, to.Y, 0, to.Height);
        var bar = new Rect(
            Ease.Lerp(from.X, to.X, glide),
            Ease.Lerp(from.Y, to.Y, glide),
            Ease.Lerp(from.Width, to.Width, glide),
            to.Height);
        double fade = 1 - Ease.Progress(t, lineEnd + 0.3, 0.4);
        if (fade <= 0)
        {
            return;
        }
        dc.PushOpacity(fade);
        dc.DrawRoundedRectangle(Brushes.Highlight, null, bar, bar.Height / 2, bar.Height / 2);
        dc.Pop();
    }

    private static Rect Underline(WordFlow flow, int index)
    {
        var box = flow.Boxes[index];
        double thickness = Math.Max(2, flow.FontSize * 0.07);
        return new Rect(box.X, box.Bottom - flow.FontSize * 0.08, flow.Texts[index].Width, thickness);
    }
}
