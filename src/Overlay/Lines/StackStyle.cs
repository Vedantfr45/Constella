using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// "Stack": kinetic type. Big bold words pop in one at a time and pile up in the middle of the
/// screen, the newest at the bottom, pushing the older ones up and out. The word being sung sits
/// in a filled rounded box in the lyric colour with dark letters; the box hands over to each new
/// word as it arrives.
/// </summary>
internal sealed class StackStyle : LineStyle<StackStyle.Layout>
{
    private const double TextUnits = 84;
    private const int MaxVisible = 4;
    private const double PopSeconds = 0.32;
    private const double BoxInSeconds = 0.16;
    private const double BoxOutSeconds = 0.2;
    private const double OlderOpacity = 0.78;

    /// <summary>Room a word takes in the pile, in text heights (box padding included).</summary>
    private const double RoomPerHeight = 1.06;

    /// <summary>How far past full size a word bounces as it pops in (Ease.OutBack peaks about 10% over).</summary>
    private const double PopOvershoot = 1.1;

    /// <summary>The box's side padding, in text heights.</summary>
    private const double BoxPadX = 0.2;

    private Brush _boxFill = System.Windows.Media.Brushes.White;
    private Brush _boxText = System.Windows.Media.Brushes.Black;

    /// <summary>One word, ready to draw. <see cref="Height"/> is the room it takes in the pile, box padding included.</summary>
    internal sealed record StackWord(FormattedText Text, double Height);

    internal sealed class Layout
    {
        public Layout(StackWord[] words) => Words = words;

        public StackWord[] Words { get; }
    }

    protected override Layout Arrange(LyricLine line, int index)
    {
        var typeface = new Typeface(Look.Font.Family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        // A full pile (plus the word shrinking out at the top) must fit the safe area's height, and
        // a word bouncing in, box and all, must not reach past its sides.
        double maxWordHeight = Safe.Height / (MaxVisible + 0.5) / RoomPerHeight;
        double maxWordWidth = Safe.Width / PopOvershoot - 2 * BoxPadX * TextUnits * Unit;
        var words = new StackWord[line.Words.Count];
        for (int i = 0; i < words.Length; i++)
        {
            var text = FittedWord(line.Words[i].Text, typeface, TextUnits * Unit, maxWordWidth, maxWordHeight);
            words[i] = new StackWord(text, text.Height * RoomPerHeight);
        }
        return new Layout(words);
    }

    protected override void OnPaletteChanged(LyricPalette palette)
    {
        // The box is the lyric colour itself; its letters are a near-black of the same hue.
        var (hue, saturation, _) = ColorUtil.ToHsl(palette.Lyric);
        _boxFill = Frozen(palette.Lyric);
        _boxText = Frozen(ColorUtil.FromHsl(hue, saturation * 0.5, 0.09));
    }

    protected override void DrawLine(DrawingContext dc, LiveLine line, double enter, double exit, bool glowPass)
    {
        var words = line.Layout.Words;
        var timing = line.Line.Words;
        double t = Frame.SongTime;

        int sung = 0;
        while (sung < words.Length && t >= timing[sung].Start)
        {
            sung++;
        }
        if (sung == 0)
        {
            return;
        }

        // The newest word's arrival drives the whole pile: it grows into place while the word
        // that no longer fits shrinks away at the top.
        double arrival = Ease.OutCubic(Ease.Progress(t, timing[sung - 1].Start, PopSeconds));
        int first = Math.Max(0, sung - MaxVisible - 1);
        double total = 0;
        for (int i = first; i < sung; i++)
        {
            total += Room(words, timing, i, sung, arrival, t);
        }

        double y = Area.Height / 2 - total / 2 + Shift(enter, exit);
        dc.PushOpacity(enter * (1 - exit));
        for (int i = first; i < sung; i++)
        {
            double room = Room(words, timing, i, sung, arrival, t);
            if (room > 0.5)
            {
                bool leaving = sung - i > MaxVisible;
                double pop = Ease.Lerp(0.55, 1, Ease.OutBack(Ease.Progress(t, timing[i].Start, PopSeconds)));
                double alpha = Ease.Progress(t, timing[i].Start, 0.12) * (leaving ? 1 - arrival : 1);
                var center = new Point(Area.Width / 2, y + room / 2);
                dc.PushTransform(new ScaleTransform(pop, pop, center.X, center.Y));
                DrawWord(dc, words[i].Text, center, alpha, Boxed(timing, i, t), glowPass);
                dc.Pop();
            }
            y += room;
        }
        dc.Pop();
    }

    /// <summary>
    /// Vertical room a word takes up right now: it grows in as it arrives, and the oldest word
    /// shrinks out as the newest one arrives.
    /// </summary>
    private static double Room(StackWord[] words, IReadOnlyList<LyricWord> timing, int i, int sung, double arrival, double t)
    {
        double grow = Ease.OutCubic(Ease.Progress(t, timing[i].Start, PopSeconds));
        double keep = sung - i > MaxVisible ? 1 - arrival : 1;
        return words[i].Height * grow * keep;
    }

    /// <summary>
    /// 0..1, how much the word is in its box: on while it is the newest word, handing over as the
    /// next word starts. The line's last word keeps its box until the line leaves.
    /// </summary>
    private static double Boxed(IReadOnlyList<LyricWord> timing, int i, double t)
    {
        double on = Ease.OutCubic(Ease.Progress(t, timing[i].Start, BoxInSeconds));
        double off = i + 1 < timing.Count ? Ease.InOutCubic(Ease.Progress(t, timing[i + 1].Start, BoxOutSeconds)) : 0;
        return on * (1 - off);
    }

    /// <summary>
    /// The plain word goes on the base layer and the boxed word on the glow layer (so the box
    /// gives off a soft light); the two cross-fade as the box comes and goes.
    /// </summary>
    private void DrawWord(DrawingContext dc, FormattedText text, Point center, double alpha, double boxed, bool glowPass)
    {
        var origin = new Point(center.X - text.Width / 2, center.Y - text.Height / 2);
        if (glowPass)
        {
            if (boxed <= 0.002)
            {
                return;
            }
            double padX = text.Height * BoxPadX, padY = text.Height * 0.04;
            var box = new Rect(origin.X - padX, origin.Y - padY, text.Width + 2 * padX, text.Height + 2 * padY);
            dc.PushOpacity(alpha * boxed);
            dc.DrawRoundedRectangle(_boxFill, null, box, text.Height * 0.16, text.Height * 0.16);
            text.SetForegroundBrush(_boxText);
            dc.DrawText(text, origin);
            dc.Pop();
        }
        else if (boxed < 0.998)
        {
            text.SetForegroundBrush(Brushes.Lyric);
            dc.PushOpacity(alpha * OlderOpacity * (1 - boxed));
            dc.DrawText(text, origin);
            dc.Pop();
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
