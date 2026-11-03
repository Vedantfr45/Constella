using System;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// "Spotlight": one huge word at a time, centred. Each word punches in as it is sung and the
/// previous one swells and fades away behind it.
/// </summary>
internal sealed class SpotlightStyle : LineStyle<FormattedText[]>
{
    private const double TextUnits = 190;
    private const double InSeconds = 0.22;
    private const double OutSeconds = 0.2;

    /// <summary>How much the outgoing word grows as it fades.</summary>
    private const double OutSwell = 1.18;

    protected override FormattedText[] Arrange(LyricLine line, int index)
    {
        var typeface = new Typeface(Look.Font.Family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        // The outgoing word swells, so leave room for its biggest size inside the safe area.
        double maxWidth = Safe.Width / OutSwell, maxHeight = Safe.Height / OutSwell;
        var texts = new FormattedText[line.Words.Count];
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i] = FittedWord(line.Words[i].Text, typeface, TextUnits * Unit, maxWidth, maxHeight);
        }
        return texts;
    }

    protected override void DrawLine(DrawingContext dc, LiveLine line, double enter, double exit, bool glowPass)
    {
        // Everything is drawn on the glow layer: a single word carries the whole screen.
        if (!glowPass)
        {
            return;
        }
        var words = line.Line.Words;
        double t = Frame.SongTime;
        int current = -1;
        while (current + 1 < words.Count && t >= words[current + 1].Start)
        {
            current++;
        }
        if (current < 0)
        {
            return;
        }

        dc.PushOpacity(1 - exit);
        // The word before the current one, swelling as it fades out.
        if (current > 0)
        {
            double gone = Ease.OutCubic(Ease.Progress(t, words[current].Start, OutSeconds));
            if (gone < 1)
            {
                DrawWord(dc, line.Layout[current - 1], Ease.Lerp(1, OutSwell, gone), 1 - gone, Brushes.Lyric);
            }
        }
        double arrive = Ease.Progress(t, words[current].Start, InSeconds);
        DrawWord(dc, line.Layout[current], Ease.Lerp(0.78, 1, Ease.OutBack(arrive)), Ease.OutCubic(Math.Min(1, arrive * 2)), Brushes.Highlight);
        dc.Pop();
    }

    private void DrawWord(DrawingContext dc, FormattedText text, double scale, double alpha, Brush brush)
    {
        var center = new Point(Area.Width / 2, Area.Height / 2);
        text.SetForegroundBrush(brush);
        dc.PushTransform(new ScaleTransform(scale, scale, center.X, center.Y));
        dc.PushOpacity(alpha);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        dc.Pop();
        dc.Pop();
    }
}
