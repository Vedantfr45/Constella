using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Constella.Core;
using Constella.Overlay;

namespace Constella.Ui;

/// <summary>
/// "Late-night studio console" design tokens: warm near-black, paper-white type and one
/// signal-orange accent. Shared by the settings window and the tray menu.
/// </summary>
internal static class Theme
{
    public static readonly Color BgColor = Hex("#0E0D0C");
    public static readonly Color SurfaceColor = Hex("#161513");
    public static readonly Color RaisedColor = Hex("#1D1C19");
    public static readonly Color LineColor = Hex("#2A2825");
    public static readonly Color LineStrongColor = Hex("#3A3733");
    public static readonly Color TextColor = Hex("#F2EDE4");
    public static readonly Color MutedColor = Hex("#9A948A");
    public static readonly Color FaintColor = Hex("#625D56");
    public static readonly Color AccentColor = Hex("#FF5A1F");
    public static readonly Color AccentHoverColor = Hex("#FF7442");
    public static readonly Color AccentSoftColor = Color.FromArgb(31, 255, 90, 31);
    public static readonly Color DangerColor = Hex("#FF6B6B");

    public static readonly SolidColorBrush Bg = Frozen(BgColor);
    public static readonly SolidColorBrush Surface = Frozen(SurfaceColor);
    public static readonly SolidColorBrush Raised = Frozen(RaisedColor);
    public static readonly SolidColorBrush Line = Frozen(LineColor);
    public static readonly SolidColorBrush LineStrong = Frozen(LineStrongColor);
    public static readonly SolidColorBrush Text = Frozen(TextColor);
    public static readonly SolidColorBrush Muted = Frozen(MutedColor);
    public static readonly SolidColorBrush Faint = Frozen(FaintColor);
    public static readonly SolidColorBrush Accent = Frozen(AccentColor);
    public static readonly SolidColorBrush Danger = Frozen(DangerColor);

    /// <summary>Display face for headings (bundled Space Grotesk).</summary>
    public static readonly FontFamily Display = LyricFonts.Bundled("Space Grotesk");

    public static readonly FontFamily Body = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>Numbers, values and key caps.</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>Close to cubic-bezier(0.22, 1, 0.36, 1): fast start, long soft landing.</summary>
    public static readonly IEasingFunction EaseOut = CreateEase();

    public static readonly Duration Quick = new(TimeSpan.FromMilliseconds(180));

    public static readonly Duration Smooth = new(TimeSpan.FromMilliseconds(260));

    /// <summary>False when the user turned off animations in Windows settings.</summary>
    public static bool MotionEnabled => SystemParameters.ClientAreaAnimation;

    public static TextBlock Label(string text, double size, Brush brush, FontFamily? font = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush,
        FontFamily = font ?? Body,
        FontWeight = weight ?? FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Smoothly changes a (non-frozen) brush to a new colour.</summary>
    public static void FadeTo(SolidColorBrush brush, Color color, Duration? duration = null)
    {
        if (!MotionEnabled)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
            return;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(color, duration ?? Quick) { EasingFunction = EaseOut });
    }

    public static Color Hex(string hex) => ColorUtil.ParseHexOr(hex, Colors.Magenta);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static IEasingFunction CreateEase()
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
