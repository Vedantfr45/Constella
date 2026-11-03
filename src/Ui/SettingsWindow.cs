using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Constella.Core;
using Constella.Overlay;
using Constella.Ui.Controls;
using Forms = System.Windows.Forms;

namespace Constella.Ui;

/// <summary>
/// The settings window, in the "studio console" look: numbered panels, thin borders, one accent.
/// Every control applies its change immediately (the overlay updates live).
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly ISettingsHost _host;
    private readonly StackPanel[] _columns = { new(), new(), new() };
    private readonly ChipGroup _styleChips = new();
    private readonly ChipGroup _fontChips = new();
    private readonly ValueSlider _sizeSlider = new(0.7, 1.5, 0.05);
    private readonly TextBlock _sizeValue = ValueLabel();
    private readonly ValueSlider _brightnessSlider = new(0.3, 1, 0.05);
    private readonly TextBlock _brightnessValue = ValueLabel();
    private readonly ToggleSwitch _autoColor = new();
    private readonly ColorSwatch _lyricSwatch = new();
    private readonly ColorSwatch _highlightSwatch = new();
    private readonly ColorSwatch _secondarySwatch = new();
    private readonly ChipGroup _monitorChips = new();
    private readonly ChipGroup _backdropChips = new();
    private readonly ToggleSwitch _glowToggle = new();
    private readonly ValueSlider _glowSlider = new(0, 1, 0.05);
    private readonly TextBlock _glowValue = ValueLabel();
    private readonly ChipGroup _glowModeChips = new();
    private readonly ChipGroup _glowColorChips = new();
    private readonly ColorSwatch _glowSwatch = new();
    private readonly ValueSlider _speedSlider = new(1, 10, 0.5);
    private readonly TextBlock _speedValue = ValueLabel();
    private readonly ToggleSwitch _clockToggle = new();
    private readonly ValueSlider _offsetSlider = new(-3, 3, 0.1);
    private readonly TextBlock _offsetValue = ValueLabel();
    private readonly TextBlock _appTiming = Hint(string.Empty);
    private readonly HotkeyBox _hotkeyBox = new();
    private readonly TextBlock _hotkeyHint = Hint("Works from any app.");
    private readonly ToggleSwitch _startupToggle = new();
    private readonly Ellipse _statusDot = new() { Width = 6, Height = 6, Margin = new Thickness(0, 1, 8, 0) };
    private readonly TextBlock _statusText = Theme.Label(string.Empty, 12, Theme.Muted);
    private readonly PillButton _toggleButton = new("Hide lyrics", primary: true, keycap: "");
    private readonly FrameworkElement[] _revealed;

    public SettingsWindow(ISettingsHost host)
    {
        _host = host;
        Title = "Constella";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Constella.ico"));
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanMinimize;
        SizeToContent = SizeToContent.Height;
        Width = 1180;
        // Never taller than the screen above the taskbar; the panels scroll with the wheel if needed.
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // A normal window with a taskbar button, so it can be minimised, restored and covered by
        // other apps. It only goes topmost while the overlay hides the desktop (SetAboveOverlay).
        ShowInTaskbar = true;
        FontFamily = Theme.Body;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var header = BuildHeader();
        var body = BuildBody();
        var footer = BuildFooter();
        _revealed = new FrameworkElement[] { header }
            .Concat(_columns.SelectMany(c => c.Children.OfType<FrameworkElement>()))
            .Append(footer)
            .ToArray();

        var layout = new DockPanel { LastChildFill = true };
        var topBar = BuildTopBar();
        DockPanel.SetDock(topBar, Dock.Top);
        layout.Children.Add(topBar);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(body);

        Content = new Border
        {
            Margin = new Thickness(24), // Room for the shadow.
            Background = Theme.Bg,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 8, Opacity = 0.55, Color = Colors.Black, Direction = 270 },
            Child = layout,
        };

        WireEvents();
        SyncFromSettings(host.Settings.Current);
        UpdateNowPlaying();
        UpdateTimingSummary();
        host.Settings.Changed += SyncFromSettings;
        Closed += (_, _) => host.Settings.Changed -= SyncFromSettings;
        Loaded += (_, _) =>
        {
            CenterInWorkArea();
            Reveal();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not HotkeyBox)
            {
                Close();
            }
        };
    }

    /// <summary>Refreshes the status pill (called when the song or play state changes).</summary>
    public void UpdateNowPlaying()
    {
        string? playing = _host.NowPlaying;
        _statusText.Text = playing ?? "Nothing playing";
        _statusDot.Fill = playing is null ? Theme.Faint : Theme.Accent;
    }

    /// <summary>Refreshes the per-app timing line (called after Ctrl+Alt+[ or ]).</summary>
    public void UpdateTimingSummary()
    {
        string? summary = _host.AppTimingSummary;
        _appTiming.Text = summary is null ? "No app nudges yet." : "Nudged: " + summary;
    }

    /// <summary>
    /// The overlay is a topmost window covering the screen; while it shows a Dim or Black backdrop
    /// this window stays above it so it can still be reached. The rest of the time it is an
    /// ordinary window.
    /// </summary>
    public void SetAboveOverlay(bool above)
    {
        Topmost = above;
        KeepAboveOverlay();
    }

    /// <summary>Puts the window back above the overlay after the overlay re-asserts its place on top.</summary>
    public void KeepAboveOverlay()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (Topmost && hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    /// <summary>Shows a problem with the shortcut in the hint line.</summary>
    public void ShowHotkeyProblem(string message)
    {
        _hotkeyHint.Text = message;
        _hotkeyHint.Foreground = Theme.Danger;
    }

    private void SyncFromSettings(AppSettings s)
    {
        _styleChips.Selected = s.Style;
        _fontChips.Selected = s.Font;
        _sizeSlider.Value = s.TextScale;
        _sizeValue.Text = $"{Math.Round(s.TextScale * 100)}%";
        _brightnessSlider.Value = s.LyricBrightness;
        _brightnessValue.Text = $"{Math.Round(s.LyricBrightness * 100)}%";
        _autoColor.IsOn = s.AutoColorFromArt;
        _lyricSwatch.Color = ColorUtil.ParseHexOr(s.LyricColor, Colors.White);
        _highlightSwatch.Color = ColorUtil.ParseHexOr(s.HighlightColor, Colors.White);
        _secondarySwatch.Color = ColorUtil.ParseHexOr(s.SecondaryColor, Colors.White);
        _monitorChips.Selected = s.Monitor ?? Forms.Screen.PrimaryScreen?.DeviceName;
        _backdropChips.Selected = s.Backdrop;
        _glowToggle.IsOn = s.EdgeGlow;
        _glowSlider.Value = s.GlowIntensity;
        _glowValue.Text = $"{Math.Round(s.GlowIntensity * 100)}%";
        _glowModeChips.Selected = s.GlowMode;
        _glowColorChips.Selected = s.GlowColor;
        _glowSwatch.Color = s.GlowColor == AppSettings.GlowMatchesLyrics
            ? ColorUtil.ParseHexOr(s.LyricColor, Colors.White)
            : ColorUtil.ParseHexOr(s.GlowColor, Colors.White);
        _speedSlider.Value = s.GlowSpeed;
        _speedValue.Text = $"{s.GlowSpeed:0.0}";
        SetEnabled(s.EdgeGlow, _glowSlider, _glowModeChips, _glowColorChips, _glowSwatch);
        SetEnabled(s.EdgeGlow && s.GlowMode == GlowMode.Run, _speedSlider);
        _clockToggle.IsOn = s.Clock24Hour;
        _offsetSlider.Value = s.LyricsOffset;
        _offsetValue.Text = s.LyricsOffset == 0 ? "0.0 s" : $"{s.LyricsOffset:+0.0;-0.0} s";
        _hotkeyBox.SetHotkey(s.HotkeyModifiers, s.HotkeyKey);
        _startupToggle.IsOn = s.StartWithWindows;
        _toggleButton.Text = s.LyricsVisible ? "Hide lyrics" : "Show lyrics";
        _toggleButton.Keycap = HotkeyBox.Describe(s.HotkeyModifiers, s.HotkeyKey).Replace(" + ", "+");
    }

    private void WireEvents()
    {
        var store = _host.Settings;
        _styleChips.SelectionChanged += key => store.Update(s => s.Style = (string)key);
        _fontChips.SelectionChanged += key => store.Update(s => s.Font = (string)key);
        _sizeSlider.ValueChanged += v => store.Update(s => s.TextScale = v);
        _brightnessSlider.ValueChanged += v => store.Update(s => s.LyricBrightness = v);
        _autoColor.Toggled += on => store.Update(s => s.AutoColorFromArt = on);
        // Picking a colour by hand switches album-art colours off, otherwise the pick would be ignored.
        _lyricSwatch.Picked += c => store.Update(s => { s.LyricColor = ColorUtil.ToHex(c); s.AutoColorFromArt = false; });
        _highlightSwatch.Picked += c => store.Update(s => { s.HighlightColor = ColorUtil.ToHex(c); s.AutoColorFromArt = false; });
        _secondarySwatch.Picked += c => store.Update(s => { s.SecondaryColor = ColorUtil.ToHex(c); s.AutoColorFromArt = false; });
        _monitorChips.SelectionChanged += key => store.Update(s => s.Monitor = (string)key);
        _backdropChips.SelectionChanged += key => store.Update(s => s.Backdrop = (Backdrop)key);
        _glowToggle.Toggled += on => store.Update(s => s.EdgeGlow = on);
        _glowSlider.ValueChanged += v => store.Update(s => s.GlowIntensity = v);
        _glowModeChips.SelectionChanged += key => store.Update(s => s.GlowMode = (GlowMode)key);
        _glowColorChips.SelectionChanged += key => store.Update(s => s.GlowColor = (string)key);
        _glowSwatch.Picked += c => store.Update(s => s.GlowColor = ColorUtil.ToHex(c));
        _speedSlider.ValueChanged += v => store.Update(s => s.GlowSpeed = v);
        _clockToggle.Toggled += on => store.Update(s => s.Clock24Hour = on);
        _offsetSlider.ValueChanged += v => store.Update(s => s.LyricsOffset = v);
        _startupToggle.Toggled += on => store.Update(s => s.StartWithWindows = on);
        _hotkeyBox.Captured += (mods, key) =>
        {
            if (_host.TryChangeHotkey(mods, key))
            {
                _hotkeyHint.Text = "Works from any app.";
                _hotkeyHint.Foreground = Theme.Faint;
            }
            else
            {
                ShowHotkeyProblem($"{HotkeyBox.Describe(mods, key)} is taken by another app.");
            }
        };
        _toggleButton.Click += _host.ToggleLyrics;
    }

    private Grid BuildTopBar()
    {
        var bar = new Grid { Margin = new Thickness(28, 22, 20, 0), Background = Brushes.Transparent };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };

        var mark = new Path
        {
            Data = Sparkle.Shape,
            Fill = new SolidColorBrush(Theme.Hex(AppSettings.DefaultLyricColor)),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 10, 0),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Color = Theme.Hex(AppSettings.DefaultLyricColor), Opacity = 0.7 },
        };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(mark);
        brand.Children.Add(Theme.Label("Constella", 17, Theme.Text, Theme.Display, FontWeights.Bold));
        Grid.SetColumn(brand, 0);
        bar.Children.Add(brand);

        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(_statusDot);
        status.Children.Add(_statusText);
        _statusText.MaxWidth = 300;
        var pill = new Border
        {
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(12, 5, 14, 6),
            Child = status,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(pill, 2);
        bar.Children.Add(pill);

        // Closing only closes this window; Constella keeps running in the tray.
        var minimize = IconButton("M 0 5 L 10 5", "Minimise");
        minimize.MouseLeftButtonUp += (_, _) => WindowState = WindowState.Minimized;
        Grid.SetColumn(minimize, 3);
        bar.Children.Add(minimize);
        var close = IconButton("M 0 0 L 10 10 M 10 0 L 0 10", "Close (Constella keeps running in the tray)");
        close.MouseLeftButtonUp += (_, _) => Close();
        Grid.SetColumn(close, 4);
        bar.Children.Add(close);
        return bar;
    }

    private StackPanel BuildHeader()
    {
        var headline = new TextBlock
        {
            FontFamily = Theme.Display,
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
        };
        headline.Inlines.Add("Lyrics, drawn in ");
        headline.Inlines.Add(new System.Windows.Documents.Run("light") { Foreground = Theme.Accent });
        headline.Inlines.Add(".");

        var sub = Theme.Label(
            "Follows whatever is playing in Spotify, YouTube Music in your browser, or any app in Windows' media controls.",
            14, Theme.Muted);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 6, 0, 0);

        var header = new StackPanel { Margin = new Thickness(28, 26, 28, 22) };
        header.Children.Add(headline);
        header.Children.Add(sub);
        return header;
    }

    private ScrollViewer BuildBody()
    {
        foreach (var (id, name) in LyricStyles.All)
        {
            _styleChips.Add(id, name);
        }
        foreach (var font in LyricFonts.All)
        {
            _fontChips.Add(font.Id, font.Name, font.Family);
        }
        var screens = Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var b = screens[i].Bounds;
            string name = $"Screen {i + 1} · {b.Width}×{b.Height}" + (screens[i].Primary ? " · main" : string.Empty);
            _monitorChips.Add(screens[i].DeviceName, name);
        }
        _backdropChips.Add(Backdrop.Clear, "Clear");
        _backdropChips.Add(Backdrop.Dim, "Dim");
        _backdropChips.Add(Backdrop.Black, "Black");
        _glowModeChips.Add(GlowMode.Run, "Run");
        _glowModeChips.Add(GlowMode.Breathe, "Breathe");
        _glowModeChips.Add(GlowMode.Still, "Still");
        _glowColorChips.Add(AppSettings.GlowMatchesLyrics, "Lyrics");
        _glowColorChips.Add(AppSettings.DefaultGlowColor, "Red");
        _glowColorChips.Add("#FFFFFF", "White");

        _columns[0].Children.Add(Panel("01", "Look",
            Stacked("Style", _styleChips),
            Stacked("Font", _fontChips),
            Stacked("Text size", WithValue(_sizeSlider, _sizeValue)),
            Stacked("Lyric brightness", WithValue(_brightnessSlider, _brightnessValue))));

        _columns[0].Children.Add(Panel("02", "Colour",
            Inline("Match album art", _autoColor, "Tints the lyrics from the song's cover. Black & white covers get white on silver."),
            Swatches(),
            ResetColoursLink()));

        _columns[1].Children.Add(Panel("03", "Screen",
            Stacked("Show on", _monitorChips),
            Stacked("Backdrop", _backdropChips, "Dim and Black cover the desktop. Hide them again with the show / hide shortcut or the tray menu.")));

        _columns[1].Children.Add(Panel("04", "Edge light",
            Inline("Edge glow", _glowToggle, "Light around the edges of the screen."),
            Stacked("Motion", _glowModeChips, "Run chases around the screen; Breathe follows the loudness."),
            Stacked("Colour", GlowColorRow()),
            Stacked("Intensity", WithValue(_glowSlider, _glowValue)),
            Stacked("Run speed", WithValue(_speedSlider, _speedValue))));

        var loadButton = new PillButton("Load .lrc file…", primary: false) { HorizontalAlignment = HorizontalAlignment.Left };
        loadButton.Click += _host.LoadLyricsFile;

        _columns[2].Children.Add(Panel("05", "Timing & keys",
            Stacked("Lyrics timing", WithValue(_offsetSlider, _offsetValue),
                "For every song. Drag right if words light up too early."),
            Stacked("Per-app nudge", AppTimingRow(),
                "While a song plays, Ctrl+Alt+[ shows lyrics 0.1 s earlier and Ctrl+Alt+] later. Saved separately for each app (Spotify, Chrome…)."),
            Stacked("Show / hide shortcut", _hotkeyBox, _hotkeyHint),
            Inline("Start with Windows", _startupToggle, null),
            Stacked("Your own lyrics", loadButton, "Used for the song that's playing, and remembered.")));

        var lockButton = new PillButton("Show lock screen", primary: false, keycap: AppController.LockShortcut.Replace(" + ", "+"))
        {
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        lockButton.Click += _host.ShowLockScreen;

        _columns[2].Children.Add(Panel("06", "Lock screen",
            Stacked("Phone-style lock screen", lockButton, "Clock, lyrics and now playing, full screen. Any key or click closes it."),
            Inline("24-hour clock", _clockToggle, null)));

        var grid = new Grid { Margin = new Thickness(28, 0, 28, 0) };
        for (int i = 0; i < _columns.Length; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(_columns[i], i * 2);
            grid.Children.Add(_columns[i]);
        }

        // No visible scroll bar (the default one doesn't suit the dark look); the wheel still scrolls
        // when the window is capped at the screen height.
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = grid,
        };
    }

    /// <summary>The current per-app nudges and a link to clear them.</summary>
    private StackPanel AppTimingRow()
    {
        var reset = Theme.Label("Reset all apps", 12, Theme.Faint);
        reset.Cursor = Cursors.Hand;
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 4, 0, 0);
        reset.MouseEnter += (_, _) => reset.Foreground = Theme.Accent;
        reset.MouseLeave += (_, _) => reset.Foreground = Theme.Faint;
        reset.MouseLeftButtonUp += (_, _) => _host.ResetAppTimings();

        _appTiming.Foreground = Theme.Muted;
        var row = new StackPanel();
        row.Children.Add(_appTiming);
        row.Children.Add(reset);
        return row;
    }

    /// <summary>Quick colour choices for the edge light, plus a swatch for any other colour.</summary>
    private StackPanel GlowColorRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _glowColorChips.VerticalAlignment = VerticalAlignment.Center;
        _glowSwatch.Width = _glowSwatch.Height = 28;
        _glowSwatch.CornerRadius = new CornerRadius(14);
        _glowSwatch.Margin = new Thickness(0, 0, 0, 8);
        _glowSwatch.ToolTip = "Any colour";
        row.Children.Add(_glowColorChips);
        row.Children.Add(_glowSwatch);
        return row;
    }

    private static void SetEnabled(bool enabled, params UIElement[] controls)
    {
        foreach (var control in controls)
        {
            control.IsEnabled = enabled;
            control.Opacity = enabled ? 1 : 0.35;
        }
    }

    /// <summary>Centres the window in the screen area above the taskbar.</summary>
    private void CenterInWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
        Top = area.Top + Math.Max(0, (area.Height - ActualHeight) / 2);
    }

    private Grid BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(28, 6, 28, 24) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var credit = Theme.Label("Built by Vedant", 11, Theme.Faint, Theme.Mono);
        footer.Children.Add(credit);

        var close = new PillButton("Close", primary: false, keycap: "Esc") { Margin = new Thickness(0, 0, 10, 0) };
        close.Click += Close;
        var quit = new PillButton("Quit", primary: false) { Margin = new Thickness(0, 0, 10, 0) };
        quit.Click += _host.Quit;
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(close);
        actions.Children.Add(quit);
        actions.Children.Add(_toggleButton);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        return footer;
    }

    /// <summary>A numbered panel with a heading and rows.</summary>
    private static Border Panel(string number, string title, params UIElement[] rows)
    {
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(Theme.Label(title, 15, Theme.Text, Theme.Display, FontWeights.Bold));
        var numberLabel = Theme.Label(number, 11, Theme.Faint, Theme.Mono);
        numberLabel.HorizontalAlignment = HorizontalAlignment.Right;
        heading.Children.Add(numberLabel);

        var stack = new StackPanel();
        stack.Children.Add(heading);
        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }
        return new Border
        {
            Background = Theme.Surface,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 18, 20, 10),
            Margin = new Thickness(0, 0, 0, 16),
            Child = stack,
        };
    }

    /// <summary>Label above, control below (for chips and sliders).</summary>
    private static StackPanel Stacked(string label, UIElement control, string? hint = null) =>
        Stacked(label, control, hint is null ? null : Hint(hint));

    private static StackPanel Stacked(string label, UIElement control, TextBlock? hint)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var caption = Theme.Label(label, 12, Theme.Muted);
        caption.Margin = new Thickness(0, 0, 0, 8);
        row.Children.Add(caption);
        row.Children.Add(control);
        if (hint is not null)
        {
            hint.Margin = new Thickness(0, 6, 0, 0);
            row.Children.Add(hint);
        }
        return row;
    }

    /// <summary>Label on the left, control on the right (for switches).</summary>
    private static Grid Inline(string label, UIElement control, string? hint)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.Label(label, 13, Theme.Text));
        if (hint is not null)
        {
            var hintText = Hint(hint);
            hintText.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(hintText);
        }
        row.Children.Add(text);
        Grid.SetColumn((FrameworkElement)control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Grid WithValue(FrameworkElement slider, TextBlock value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(value, 1);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return grid;
    }

    private UniformGrid Swatches()
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 10) };
        grid.Children.Add(SwatchCell(_lyricSwatch, "Lyric"));
        grid.Children.Add(SwatchCell(_highlightSwatch, "Highlight"));
        grid.Children.Add(SwatchCell(_secondarySwatch, "Secondary"));
        return grid;
    }

    private static StackPanel SwatchCell(ColorSwatch swatch, string caption)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal };
        cell.Children.Add(swatch);
        var label = Theme.Label(caption, 12, Theme.Muted);
        label.Margin = new Thickness(10, 0, 0, 0);
        cell.Children.Add(label);
        return cell;
    }

    private TextBlock ResetColoursLink()
    {
        var link = Theme.Label("Reset to cream & gold", 12, Theme.Faint);
        link.Cursor = Cursors.Hand;
        link.HorizontalAlignment = HorizontalAlignment.Left;
        link.Margin = new Thickness(0, 0, 0, 12);
        link.MouseEnter += (_, _) => link.Foreground = Theme.Accent;
        link.MouseLeave += (_, _) => link.Foreground = Theme.Faint;
        link.MouseLeftButtonUp += (_, _) => _host.Settings.Update(s =>
        {
            s.LyricColor = AppSettings.DefaultLyricColor;
            s.HighlightColor = AppSettings.DefaultHighlightColor;
            s.SecondaryColor = AppSettings.DefaultSecondaryColor;
            s.AutoColorFromArt = false;
        });
        return link;
    }

    private static Border IconButton(string pathData, string tooltip)
    {
        var icon = new Path
        {
            Data = Geometry.Parse(pathData),
            Stroke = Theme.Muted,
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = icon,
        };
        button.MouseEnter += (_, _) => { button.Background = Theme.Raised; icon.Stroke = Theme.Text; };
        button.MouseLeave += (_, _) => { button.Background = Brushes.Transparent; icon.Stroke = Theme.Muted; };
        return button;
    }

    private static TextBlock Hint(string text)
    {
        var hint = Theme.Label(text, 11.5, Theme.Faint);
        hint.TextWrapping = TextWrapping.Wrap;
        return hint;
    }

    private static TextBlock ValueLabel() => Theme.Label(string.Empty, 12, Theme.Text, Theme.Mono);

    /// <summary>Staggered rise-in of the header, panels and footer.</summary>
    private void Reveal()
    {
        if (!Theme.MotionEnabled)
        {
            return;
        }
        for (int i = 0; i < _revealed.Length; i++)
        {
            var element = _revealed[i];
            var shift = new TranslateTransform(0, 14);
            element.RenderTransform = shift;
            element.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(60 * i);
            var duration = new Duration(TimeSpan.FromMilliseconds(520));
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { BeginTime = delay, EasingFunction = Theme.EaseOut });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { BeginTime = delay, EasingFunction = Theme.EaseOut });
        }
    }
}
