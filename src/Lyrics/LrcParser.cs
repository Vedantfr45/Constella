using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Constella.Lyrics;

/// <summary>
/// Parses LRC lyrics. Supports plain line stamps <c>[01:23.45]</c>, several stamps on one line,
/// the <c>[offset:+/-ms]</c> tag, and "enhanced" per-word stamps <c>&lt;01:23.45&gt;</c>.
/// When a line has no word stamps its words are spread across the line, weighted by length.
/// </summary>
public static partial class LrcParser
{
    /// <summary>Longest a plain line's words are spread over, per word (stops slow songs dragging).</summary>
    private const double MaxSecondsPerWord = 0.75;

    /// <summary>How long the last line is assumed to last.</summary>
    private const double LastLineHold = 4.0;

    [GeneratedRegex(@"^\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex LineStamp();

    [GeneratedRegex(@"<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>")]
    private static partial Regex WordStamp();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <returns>The lyrics, or null if the text has no time stamps.</returns>
    public static LyricSheet? Parse(string content)
    {
        double offset = 0;
        var entries = new List<(double Time, string Body)>();

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim().TrimStart('﻿');
            var offsetMatch = OffsetTag().Match(line);
            if (offsetMatch.Success)
            {
                offset = int.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0;
                continue;
            }

            var times = new List<double>();
            Match stamp;
            while ((stamp = LineStamp().Match(line)).Success)
            {
                times.Add(ToSeconds(stamp));
                line = line[stamp.Length..].TrimStart();
            }
            foreach (double time in times)
            {
                entries.Add((time, line));
            }
        }

        if (entries.Count == 0)
        {
            return null;
        }

        // Stable sort keeps file order for equal stamps.
        var sorted = entries.Select((e, i) => (e.Time, e.Body, i))
            .OrderBy(e => e.Time).ThenBy(e => e.i)
            .Select(e => (Time: e.Time - offset, e.Body))
            .ToList();

        bool hasWordTiming = false;
        var lines = new List<LyricLine>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var (start, body) = sorted[i];
            double? nextStart = i + 1 < sorted.Count ? sorted[i + 1].Time : null;
            bool enhanced = WordStamp().IsMatch(body);
            hasWordTiming |= enhanced;

            var words = enhanced ? ParseTimedWords(body, start, nextStart, offset) : SpreadWords(SplitWords(body), start, nextStart);
            string text = string.Join(' ', words.Select(w => w.Text));
            double end = nextStart ?? (words.Count > 0 ? words[^1].End : start) + LastLineHold;
            lines.Add(new LyricLine(start, Math.Max(end, start), text, words));
        }
        return new LyricSheet(lines, hasWordTiming);
    }

    /// <summary>Handles "&lt;00:12.00&gt;Hello &lt;00:12.40&gt;world" (text before the first stamp starts with the line).</summary>
    private static List<LyricWord> ParseTimedWords(string body, double lineStart, double? nextStart, double offset)
    {
        var segments = new List<(double Start, string Text)>();
        var matches = WordStamp().Matches(body);
        if (matches[0].Index > 0)
        {
            segments.Add((lineStart, body[..matches[0].Index]));
        }
        for (int m = 0; m < matches.Count; m++)
        {
            int textStart = matches[m].Index + matches[m].Length;
            int textEnd = m + 1 < matches.Count ? matches[m + 1].Index : body.Length;
            segments.Add((ToSeconds(matches[m]) - offset, body[textStart..textEnd]));
        }

        var words = new List<LyricWord>();
        for (int s = 0; s < segments.Count; s++)
        {
            var parts = SplitWords(segments[s].Text);
            if (parts.Count == 0)
            {
                continue; // A stamp with no text just marks where the previous word ends.
            }
            double segStart = segments[s].Start;
            double? segEnd = s + 1 < segments.Count ? segments[s + 1].Start : null;
            words.AddRange(SpreadWords(parts, segStart, segEnd ?? nextStart));
        }
        return words;
    }

    /// <summary>Spreads words over [start, end), giving longer words more time.</summary>
    private static List<LyricWord> SpreadWords(IReadOnlyList<string> parts, double start, double? nextStart)
    {
        var words = new List<LyricWord>(parts.Count);
        if (parts.Count == 0)
        {
            return words;
        }

        double maxSpan = Math.Max(1.5, parts.Count * MaxSecondsPerWord);
        double span = nextStart is double next ? Math.Min((next - start) * 0.9, maxSpan) : maxSpan;
        span = Math.Max(span, 0.2);

        double[] weights = parts.Select(Weight).ToArray();
        double total = weights.Sum();
        double t = start;
        for (int i = 0; i < parts.Count; i++)
        {
            double length = span * weights[i] / total;
            words.Add(new LyricWord(parts[i], t, t + length));
            t += length;
        }
        return words;
    }

    /// <summary>Letters plus a constant, so even "a" or "I" gets a fair moment.</summary>
    private static double Weight(string word) => word.Count(char.IsLetterOrDigit) + 1.5;

    private static List<string> SplitWords(string text)
    {
        // Music-note-only lines (♪) mark instrumental parts: no words.
        return Whitespace().Split(text.Trim())
            .Where(w => w.Length > 0 && w.Any(char.IsLetterOrDigit))
            .ToList();
    }

    private static double ToSeconds(Match m)
    {
        int minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        double fraction = 0;
        if (m.Groups[3].Success)
        {
            string digits = m.Groups[3].Value;
            fraction = int.Parse(digits, CultureInfo.InvariantCulture) / Math.Pow(10, digits.Length);
        }
        return minutes * 60 + seconds + fraction;
    }
}
