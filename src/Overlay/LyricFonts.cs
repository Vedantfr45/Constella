using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Constella.Overlay;

/// <summary>A font the lyrics can use, with the weights for resting words and the sung word.</summary>
public sealed record LyricFont(string Id, string Name, FontFamily Family, FontWeight RestingWeight, FontWeight ActiveWeight)
{
    public Typeface Resting => new(Family, FontStyles.Normal, RestingWeight, FontStretches.Normal);

    public Typeface Active => new(Family, FontStyles.Normal, ActiveWeight, FontStretches.Normal);
}

/// <summary>
/// The font choices. Segoe UI ships with Windows; the others are bundled (SIL Open Font License)
/// in Assets/Fonts so they look the same on every PC.
/// </summary>
public static class LyricFonts
{
    private static readonly Uri PackRoot = new("pack://application:,,,/");

    public static IReadOnlyList<LyricFont> All { get; } = new[]
    {
        new LyricFont("sans", "Sans", new FontFamily("Segoe UI Variable Display, Segoe UI"), FontWeights.Normal, FontWeights.SemiBold),
        new LyricFont("rounded", "Rounded", Bundled("Nunito"), FontWeights.Normal, FontWeights.Bold),
        new LyricFont("serif", "Serif", Bundled("Cormorant Garamond"), FontWeights.Normal, FontWeights.Bold),
        new LyricFont("grotesk", "Grotesk", Bundled("Space Grotesk"), FontWeights.Normal, FontWeights.Bold),
        new LyricFont("mono", "Mono", Bundled("JetBrains Mono"), FontWeights.Normal, FontWeights.Bold),
    };

    public static LyricFont ById(string id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];

    /// <summary>A font family embedded in this app's resources.</summary>
    public static FontFamily Bundled(string familyName) => new(PackRoot, "./Assets/Fonts/#" + familyName);
}
