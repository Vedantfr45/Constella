using System;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// "Type-on": each word is typed out letter by letter as it is sung, with a blinking caret.
/// Words are placed where the finished line will sit, so nothing shifts while typing.
/// </summary>
internal sealed class TypeStyle : LineStyle<WordFlow>
{
    private const double TextUnits = 54;

    /// <summary>Typing speed cap: a long word never takes longer than this per letter.</summary>
    private const double SecondsPerLetter = 0.07;

    protected override WordFlow Arrange(LyricLine line, int index) => CenteredFlow(line, TextUnits, Look.Font.Active);

    protected override void DrawLine(DrawingContext dc, LiveLine line, double enter, double exit, bool glowPass)
    {
        var flow = line.Layout;
        var words = line.Line.Words;
        double t = Frame.SongTime;
        dc.PushTransform(new TranslateTransform(0, Shift(enter, exit)));
        dc.PushOpacity(enter * (1 - exit));

        // Where the caret sits: after the last letter typed so far (or before the first word).
        var caret = new Point(flow.Boxes[0].X, flow.Boxes[0].Y);
        double caretHeight = flow.Boxes[0].Height;
        bool typing = false;

        for (int i = 0; i < words.Count && t >= words[i].Start; i++)
        {
            string letters = words[i].Text;
            double duration = Math.Min(letters.Length * SecondsPerLetter, Math.Max(0.05, (words[i].End - words[i].Start) * 0.85));
            int shown = (int)Math.Ceiling(letters.Length * Ease.Progress(t, words[i].Start, duration));
            var box = flow.Boxes[i];
            typing |= shown < letters.Length;

            var text = shown >= letters.Length
                ? flow.Texts[i]
                : WordFlow.Text(letters[..Math.Max(shown, 1)], Look.Font.Active, flow.FontSize, PixelsPerDip);
            if (!glowPass)
            {
                bool newest = i == words.Count - 1 || t < words[i + 1].Start;
                text.SetForegroundBrush(newest ? Brushes.Highlight : Brushes.Lyric);
                dc.DrawText(text, box.Location);
            }
            caret = new Point(box.X + text.Width + flow.FontSize * 0.06, box.Y);
            caretHeight = box.Height;
        }

        // The caret blinks while waiting and stays solid while letters are appearing.
        bool caretOn = typing || Frame.Wall * 1.8 % 1 < 0.55;
        if (glowPass && caretOn && exit <= 0)
        {
            double width = Math.Max(2, flow.FontSize * 0.06);
            dc.DrawRectangle(Brushes.Highlight, null, new Rect(caret.X, caret.Y + caretHeight * 0.12, width, caretHeight * 0.76));
        }
        dc.Pop();
        dc.Pop();
    }
}
