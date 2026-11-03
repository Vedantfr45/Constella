using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Constellation;

/// <summary>
/// Places the stars of one line: left to right across the middle 70% of the screen in a seeded
/// zig-zag (the same line always gets the same shape), then puts each label beside its star
/// where it doesn't collide with other labels or stars.
/// </summary>
internal static class ConstellationLayout
{
    /// <summary>Sizes are in "units" of 1/1000 of the screen height, so the look scales with any screen.</summary>
    public const double RestingTextUnits = 17;
    public const double ActiveTextUnits = 50;
    public const double RestingStarUnits = 4.6;
    public const double ActiveStarUnits = 10.5;
    public const double RestingGapUnits = 9;
    public const double ActiveGapUnits = 18;

    private const double UsableWidth = 0.70;
    private const int MaxWordsPerRow = 9;

    /// <summary>Label positions to try, in order: (side, vertical nudge in text heights).</summary>
    private static readonly (int Side, double Nudge)[] LabelSpots =
    {
        (1, 0), (-1, 0), (1, -0.85), (1, 0.85), (-1, -0.85), (-1, 0.85),
    };

    public static double Unit(Size area, LyricLook look) => look.Unit(area);

    public static StarNode[] Arrange(LyricLine line, int lineIndex, Size area, LyricLook look, double pixelsPerDip)
    {
        var random = StableRandom.For(line.Text, lineIndex);
        double unit = Unit(area, look);
        var stars = PlaceStars(line.Words.Count, area, random);

        // Every star is an obstacle for every label.
        double starBox = RestingStarUnits * unit * 2.4;
        var obstacles = new List<Rect>();
        foreach (var star in stars)
        {
            obstacles.Add(new Rect(star.X - starBox / 2, star.Y - starBox / 2, starBox, starBox));
        }

        var nodes = new StarNode[stars.Length];
        for (int i = 0; i < stars.Length; i++)
        {
            var word = line.Words[i];
            var resting = Text(word.Text, look.Font.Resting, RestingTextUnits * unit, pixelsPerDip);
            var (side, nudge, box) = PlaceLabel(stars[i], resting, RestingGapUnits * unit, area, obstacles);
            obstacles.Add(box);
            var (active, activeSide, activeNudge) = FitActive(
                word.Text, stars[i], side, nudge, ActiveGapUnits * unit, ActiveTextUnits * unit, look, area, pixelsPerDip);

            double nextStart = i + 1 < stars.Length ? line.Words[i + 1].Start : Math.Min(word.End + 0.8, line.End);
            nodes[i] = new StarNode
            {
                Word = word,
                Star = stars[i],
                Resting = resting,
                Active = active,
                ActiveSide = activeSide,
                ActiveNudge = activeNudge,
                Side = side,
                Nudge = nudge,
                ReleaseAt = Math.Max(nextStart, word.Start + 0.25),
                TwinklePhase = random.Range(0, Math.PI * 2),
            };
        }
        return nodes;
    }

    /// <summary>A gentle drift direction (mostly upward) for when the line fades out.</summary>
    public static Vector DriftFor(LyricLine line, int lineIndex, Size area)
    {
        var random = StableRandom.For(line.Text, lineIndex + 7919);
        double scale = area.Height / 1000.0;
        return new Vector(random.Range(-18, 18) * scale, -random.Range(10, 24) * scale);
    }

    /// <summary>Where the label's top-left corner goes for a given star, side and nudge.</summary>
    public static Point LabelOrigin(Point star, FormattedText text, int side, double nudge, double gap) => new(
        side > 0 ? star.X + gap : star.X - gap - text.Width,
        star.Y - text.Height / 2 + nudge * text.Height);

    private static Point[] PlaceStars(int count, Size area, Random random)
    {
        var stars = new Point[count];
        int rows = count > MaxWordsPerRow ? 2 : 1;
        int perRow = (int)Math.Ceiling(count / (double)rows);

        for (int i = 0; i < count; i++)
        {
            int row = i / perRow;
            int column = i % perRow;
            int inRow = Math.Min(perRow, count - row * perRow);

            // Short lines don't stretch across the whole width.
            double span = area.Width * UsableWidth * Math.Min(1, 0.3 + inRow * 0.1);
            double step = inRow > 1 ? span / (inRow - 1) : 0;
            double x = inRow > 1 ? (area.Width - span) / 2 + column * step : area.Width / 2;
            x += random.Range(-0.28, 0.28) * step;

            double rowCenter = area.Height * (rows == 1 ? 0.5 : 0.5 + (row - 0.5) * 0.24);
            double amplitude = area.Height * (rows == 1 ? 0.15 : 0.08);
            // Zig-zag: alternate up/down, with an occasional same-side step so it looks hand-drawn.
            double direction = (i % 2 == 0 ? -1 : 1) * (random.NextDouble() < 0.2 ? -1 : 1);
            double y = rowCenter + direction * amplitude * random.Range(0.25, 1.0);
            stars[i] = new Point(x, y);
        }
        return stars;
    }

    /// <summary>
    /// Picks where the large label goes so it stays inside the safe area at full size: beside the
    /// star on the small label's side if it fits, else the other side; it drops its vertical
    /// nudge if that pushes it past the top or bottom; and as a last resort it is set smaller.
    /// </summary>
    private static (FormattedText Text, int Side, double Nudge) FitActive(
        string word, Point star, int side, double nudge, double gap, double fontSize, LyricLook look, Size area, double pixelsPerDip)
    {
        var safe = SafeArea.Of(area);
        var text = Text(word, look.Font.Active, fontSize, pixelsPerDip);
        double Room(int s) => s > 0 ? safe.Right - (star.X + gap) : star.X - gap - safe.Left;
        bool FitsVertically(double h, double n) => star.Y - h / 2 + n * h >= safe.Top && star.Y + h / 2 + n * h <= safe.Bottom;

        int activeSide = Room(side) >= text.Width ? side : Room(-side) >= text.Width ? -side : Room(1) >= Room(-1) ? 1 : -1;
        double activeNudge = FitsVertically(text.Height, nudge) ? nudge : 0;

        // Smaller font when even the roomier side is too narrow, or the label is too tall at the star's height.
        double room = Math.Max(1, Room(activeSide));
        double halfHeight = Math.Max(1, Math.Min(star.Y - safe.Top, safe.Bottom - star.Y));
        double fit = Math.Min(room / Math.Max(1, text.Width), 2 * halfHeight / Math.Max(1, text.Height));
        if (fit < 1)
        {
            text = Text(word, look.Font.Active, fontSize * fit, pixelsPerDip);
        }
        return (text, activeSide, activeNudge);
    }

    private static (int Side, double Nudge, Rect Box) PlaceLabel(
        Point star, FormattedText text, double gap, Size area, List<Rect> obstacles)
    {
        var screen = SafeArea.Of(area);
        (int Side, double Nudge, Rect Box)? fallback = null;

        foreach (var (side, nudge) in LabelSpots)
        {
            var origin = LabelOrigin(star, text, side, nudge, gap);
            var box = new Rect(origin, new Size(text.Width, text.Height));
            if (!screen.Contains(box))
            {
                continue;
            }
            fallback ??= (side, nudge, box);
            var padded = box;
            padded.Inflate(4, 2);
            if (!obstacles.Exists(o => o.IntersectsWith(padded) && !o.Contains(star)))
            {
                return (side, nudge, box);
            }
        }

        if (fallback is not null)
        {
            return fallback.Value;
        }
        // Nothing fits (a very long word): use the side with more room.
        int roomier = star.X - screen.Left > screen.Right - star.X ? -1 : 1;
        return (roomier, 0, new Rect(LabelOrigin(star, text, roomier, 0, gap), new Size(text.Width, text.Height)));
    }

    private static FormattedText Text(string text, Typeface typeface, double size, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Math.Max(size, 4), Brushes.White, pixelsPerDip);
}
