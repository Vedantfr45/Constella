using System;

namespace Constella.Core;

/// <summary>How much of the desktop the overlay hides behind the lyrics.</summary>
public enum Backdrop
{
    Clear,
    Dim,
    Black,
}

/// <summary>How the edge glow moves.</summary>
public enum GlowMode
{
    /// <summary>Light runs around the screen edge, chasing its own tail.</summary>
    Run,

    /// <summary>The whole edge brightens and dims with the music's loudness.</summary>
    Breathe,

    /// <summary>A steady light (cheapest: drawn once).</summary>
    Still,
}

/// <summary>
/// Everything the user can change. Saved as JSON in %APPDATA%\Constella\settings.json.
/// Values are clamped by <see cref="Normalize"/> after loading, so a hand-edited file can't break the app.
/// </summary>
public sealed class AppSettings
{
    public const string DefaultLyricColor = "#F1E6C8";
    public const string DefaultHighlightColor = "#FFF7E2";
    public const string DefaultSecondaryColor = "#A89D84";

    /// <summary><see cref="GlowColor"/> value meaning "use the lyric colour".</summary>
    public const string GlowMatchesLyrics = "lyrics";

    public const string DefaultGlowColor = "#FF2A44";

    // Hotkey modifier flags, matching the Win32 RegisterHotKey MOD_* values.
    public const int ModAlt = 0x1;
    public const int ModControl = 0x2;
    public const int ModShift = 0x4;
    public const int ModWin = 0x8;

    /// <summary>Lyric style id from <c>LyricStyles</c> (Overlay/ILyricStyle.cs).</summary>
    public string Style { get; set; } = "constellation";

    /// <summary>Font id from <c>LyricFonts</c>.</summary>
    public string Font { get; set; } = "sans";

    /// <summary>Text size multiplier, 0.7 to 1.5.</summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>Lyric text and star brightness, 0.3 to 1 (the edge glow has its own intensity).</summary>
    public double LyricBrightness { get; set; } = 1.0;

    public string LyricColor { get; set; } = DefaultLyricColor;

    public string HighlightColor { get; set; } = DefaultHighlightColor;

    public string SecondaryColor { get; set; } = DefaultSecondaryColor;

    public bool AutoColorFromArt { get; set; }

    /// <summary>Windows device name of the chosen monitor (e.g. \\.\DISPLAY2). Null means the main screen.</summary>
    public string? Monitor { get; set; }

    public Backdrop Backdrop { get; set; } = Backdrop.Dim;

    public bool EdgeGlow { get; set; } = true;

    /// <summary>Edge glow strength, 0 to 1 (in Run mode, the snake's brightness; Rimic uses 0.9).</summary>
    public double GlowIntensity { get; set; } = 0.9;

    public GlowMode GlowMode { get; set; } = GlowMode.Run;

    /// <summary>"#RRGGBB", or <see cref="GlowMatchesLyrics"/> to follow the lyric colour.</summary>
    public string GlowColor { get; set; } = DefaultGlowColor;

    /// <summary>How fast the light runs around the edge in Run mode, 1 to 10 (5 = one lap in 8 s, as in Rimic).</summary>
    public double GlowSpeed { get; set; } = 5;

    /// <summary>Lock screen clock: 24-hour ("17:05") instead of 12-hour ("5:05").</summary>
    public bool Clock24Hour { get; set; }

    /// <summary>Seconds to delay the lyrics by (negative shows them earlier). -3 to +3.</summary>
    public double LyricsOffset { get; set; }

    public int HotkeyModifiers { get; set; } = ModControl | ModAlt;

    /// <summary>Win32 virtual-key code. Default is the L key.</summary>
    public int HotkeyKey { get; set; } = 0x4C;

    public bool StartWithWindows { get; set; }

    /// <summary>Whether the overlay is switched on (the show/hide hotkey flips this).</summary>
    public bool LyricsVisible { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Clamps numbers and replaces invalid values with defaults.</summary>
    public void Normalize()
    {
        var defaults = new AppSettings();
        Style = string.IsNullOrWhiteSpace(Style) ? defaults.Style : Style;
        Font = string.IsNullOrWhiteSpace(Font) ? defaults.Font : Font;
        TextScale = Clamp(TextScale, 0.7, 1.5, defaults.TextScale);
        LyricBrightness = Clamp(LyricBrightness, 0.3, 1, defaults.LyricBrightness);
        GlowIntensity = Clamp(GlowIntensity, 0, 1, defaults.GlowIntensity);
        GlowSpeed = Clamp(GlowSpeed, 1, 10, defaults.GlowSpeed);
        GlowColor = GlowColor == GlowMatchesLyrics ? GlowColor : ValidHex(GlowColor, DefaultGlowColor);
        if (!Enum.IsDefined(GlowMode))
        {
            GlowMode = defaults.GlowMode;
        }
        LyricsOffset = Math.Round(Clamp(LyricsOffset, -3, 3, 0), 1);
        LyricColor = ValidHex(LyricColor, DefaultLyricColor);
        HighlightColor = ValidHex(HighlightColor, DefaultHighlightColor);
        SecondaryColor = ValidHex(SecondaryColor, DefaultSecondaryColor);
        if (!Enum.IsDefined(Backdrop))
        {
            Backdrop = defaults.Backdrop;
        }
        const int allMods = ModAlt | ModControl | ModShift | ModWin;
        if ((HotkeyModifiers & allMods) == 0 || (HotkeyModifiers & ~allMods) != 0 || HotkeyKey is <= 0 or > 0xFE)
        {
            HotkeyModifiers = defaults.HotkeyModifiers;
            HotkeyKey = defaults.HotkeyKey;
        }
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static string ValidHex(string? value, string fallback) =>
        ColorUtil.TryParseHex(value, out var color) ? ColorUtil.ToHex(color) : fallback;
}
