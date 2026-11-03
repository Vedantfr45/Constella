using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Constella.Lyrics;

namespace Constella.Overlay.Lines;

/// <summary>
/// A lyric line set as centred text: words wrap into rows no wider than a limit, each row is
/// centred, and the block is centred on a point. Keeps every word's text and box so styles can
/// colour, clip or underline words one at a time.
/// </summary>
internal sealed class WordFlow
{
    /// <summary>Fitting gives up shrinking after this many tries (each try re-wraps the line).</summary>
    private const int FitAttempts = 4;

    private WordFlow(FormattedText[] texts, Rect[] boxes, double fontSize, double height)
    {
        Texts = texts;
        Boxes = boxes;
        FontSize = fontSize;
        Height = height;
    }

    /// <summary>One text per word. Set the colour with SetForegroundBrush before drawing.</summary>
    public FormattedText[] Texts { get; }

    /// <summary>Where each word sits (top-left of its text).</summary>
    public Rect[] Boxes { get; }

    public double FontSize { get; }

    /// <summary>Height of the whole block (all rows).</summary>
    public double Height { get; }

    /// <summary>
    /// Sets the words centred on <paramref name="center"/>, wrapping rows at <paramref name="maxWidth"/>.
    /// If a single word is wider than that, or the rows are taller than <paramref name="maxHeight"/>,
    /// the font is made smaller until it fits, so text never runs off the screen.
    /// </summary>
    public static WordFlow Centered(
        IReadOnlyList<LyricWord> words, Typeface typeface, double fontSize, double maxWidth, Point center, double pixelsPerDip,
        double maxHeight = double.PositiveInfinity)
    {
        var flow = Wrap(words, typeface, fontSize, maxWidth, center, pixelsPerDip);
        for (int attempt = 0; attempt < FitAttempts && flow.Texts.Length > 0; attempt++)
        {
            double widest = 0;
            foreach (var text in flow.Texts)
            {
                widest = Math.Max(widest, text.Width);
            }
            double fit = Math.Min(maxWidth / Math.Max(1, widest), maxHeight / Math.Max(1, flow.Height));
            if (fit >= 1)
            {
                break;
            }
            // A little extra so a re-wrap that adds a row doesn't need another round.
            fontSize *= fit * 0.97;
            flow = Wrap(words, typeface, fontSize, maxWidth, center, pixelsPerDip);
        }
        return flow;
    }

    private static WordFlow Wrap(
        IReadOnlyList<LyricWord> words, Typeface typeface, double fontSize, double maxWidth, Point center, double pixelsPerDip)
    {
        var texts = new FormattedText[words.Count];
        for (int i = 0; i < words.Count; i++)
        {
            texts[i] = Text(words[i].Text, typeface, fontSize, pixelsPerDip);
        }

        // Greedy wrap into rows.
        double space = fontSize * 0.3;
        var rows = new List<(int First, int Count, double Width)>();
        int first = 0;
        double width = 0;
        for (int i = 0; i < texts.Length; i++)
        {
            double add = (i > first ? space : 0) + texts[i].WidthIncludingTrailingWhitespace;
            if (i > first && width + add > maxWidth)
            {
                rows.Add((first, i - first, width));
                first = i;
                width = texts[i].WidthIncludingTrailingWhitespace;
            }
            else
            {
                width += add;
            }
        }
        if (texts.Length > 0)
        {
            rows.Add((first, texts.Length - first, width));
        }

        double rowHeight = fontSize * 1.25;
        double top = center.Y - rows.Count * rowHeight / 2;
        var boxes = new Rect[texts.Length];
        for (int r = 0; r < rows.Count; r++)
        {
            var (start, count, rowWidth) = rows[r];
            double x = center.X - rowWidth / 2;
            for (int i = start; i < start + count; i++)
            {
                double y = top + r * rowHeight + (rowHeight - texts[i].Height) / 2;
                boxes[i] = new Rect(x, y, texts[i].WidthIncludingTrailingWhitespace, texts[i].Height);
                x += boxes[i].Width + space;
            }
        }
        return new WordFlow(texts, boxes, fontSize, rows.Count * rowHeight);
    }

    public static FormattedText Text(string text, Typeface typeface, double size, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Math.Max(size, 4), System.Windows.Media.Brushes.White, pixelsPerDip);
}
