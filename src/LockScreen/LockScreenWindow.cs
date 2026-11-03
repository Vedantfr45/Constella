using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Constella.Core;
using Constella.Lyrics;
using Constella.Media;
using Constella.Overlay;

namespace Constella.LockScreen;

/// <summary>
/// A phone-style lock screen to show off the lyrics: date and big clock on top, the lyrics in the
/// middle, a now-playing card below and the running edge light around it, on a black wallpaper
/// drawn in code (a faint glow of the song's colour rising from the bottom). It's only a display:
/// it never asks for a password, and any key or click closes it.
/// </summary>
internal sealed class LockScreenWindow : Window
{
    /// <summary>Input is ignored this long after opening, so releasing the shortcut doesn't close it again.</summary>
    private const double InputGuardSeconds = 0.8;

    private const int CoverPixels = 192;

    /// <summary>How strongly the song's colour shows in the wallpaper glow (it stays mostly black).</summary>
    private const double GlowStrength = 0.32;

    private readonly PlaybackClock _clock;
    private readonly Grid _root = new();
    private readonly Rectangle _wallpaper = new();
    private readonly Grid _lyricsArea = new();
    private readonly LockScreenChrome _chrome = new();
    private readonly EdgeGlow _edge = new();
    private readonly Toast _toast = new();
    private readonly PaletteFader _palette;
    private readonly double _openedAt;
    private ILyricStyle _style;
    private string _styleId = string.Empty;
    private LyricLook? _look;
    private LyricFont _font = LyricFonts.All[0];
    private double _textScale = 1;
    private double _brightness = 1;
    private LyricSheet? _sheet;
    private Color? _wallpaperFor;
    private double _offsetSeconds;
    private double _lastWall;
    private IntPtr _hwnd;
    private string? _monitor;
    private bool _closing;

    public LockScreenWindow(PlaybackClock clock, LyricPalette palette)
    {
        _clock = clock;
        _palette = new PaletteFader(palette);
        _openedAt = clock.WallSeconds;
        _lastWall = _openedAt;
        _style = LyricStyles.Create("constellation");

        Title = "Constella lock screen";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.None;
        Left = -32000; // Off screen until SourceInitialized puts it on the chosen monitor.
        Top = -32000;
        Width = 1;
        Height = 1;

        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(_root, TextRenderingMode.Grayscale);
        _lyricsArea.Children.Add(_style.Visual);
        _root.Children.Add(_wallpaper);
        _root.Children.Add(_lyricsArea);
        _root.Children.Add(_chrome);
        _root.Children.Add(_edge);
        _root.Children.Add(_toast);
        Content = _root;

        _root.SizeChanged += (_, e) =>
        {
            PlaceLyricsArea(e.NewSize.Height);
            UpdateLook();
        };
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += (_, e) => Dismiss(e);
        // A click closes it on release, and the press is swallowed too, so neither half of the
        // click falls through to the window underneath once this one is gone.
        PreviewMouseDown += (_, e) => e.Handled = true;
        PreviewMouseUp += (_, e) => Dismiss(e);
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            // Switching away (e.g. Alt+Tab or the Win key) ends it too.
            if (!_closing)
            {
                Close();
            }
        };
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    public void ApplySettings(AppSettings settings)
    {
        if (settings.Style != _styleId)
        {
            _lyricsArea.Children.Remove(_style.Visual);
            _style = LyricStyles.Create(settings.Style);
            _styleId = settings.Style;
            _lyricsArea.Children.Add(_style.Visual);
            _style.SetSheet(_sheet, _clock.WallSeconds);
            if (_look is not null)
            {
                _style.SetLook(_look);
            }
        }
        _font = LyricFonts.ById(settings.Font);
        _textScale = settings.TextScale;
        UpdateLook();
        _brightness = settings.LyricBrightness;
        _style.Visual.Opacity = _brightness;
        _edge.ApplySettings(settings);
        _chrome.SetClockFormat(settings.Clock24Hour);
        if (settings.Monitor != _monitor)
        {
            _monitor = settings.Monitor;
            ScreenCover.Apply(_hwnd, _monitor);
        }
    }

    /// <summary>The song, its lyrics and its cover (any of them may be missing).</summary>
    public void SetSong(TrackInfo? track, LyricSheet? sheet, byte[]? art)
    {
        _sheet = sheet;
        _style.SetSheet(sheet, _clock.WallSeconds);
        _chrome.SetTrack(track, Decode(art, CoverPixels));
    }

    public void SetPalette(LyricPalette palette) => _palette.FadeTo(palette, _clock.WallSeconds, animate: true);

    /// <summary>Total seconds to delay the lyrics by (the same value the overlay uses).</summary>
    public void SetLyricOffset(double seconds) => _offsetSeconds = seconds;

    public void ShowToast(string message) => _toast.Show(message, _clock.WallSeconds);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // Keep it out of Alt+Tab; unlike the overlay it does take focus, so keys reach it.
        long style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64() | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        ScreenCover.Apply(_hwnd, _monitor);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double wall = _clock.WallSeconds;
        double dt = Math.Clamp(wall - _lastWall, 0, 0.1);
        _lastWall = wall;

        var palette = _palette.Update(wall);
        UpdateWallpaper(palette.Ambient);
        double songTime = _clock.Position - _offsetSeconds;
        _style.Update(new FrameContext(wall, dt, songTime, palette, VisualTreeHelper.GetDpi(this).PixelsPerDip));
        _chrome.Update(_clock.Position);
        _toast.Update(wall);
        _edge.Tick(wall, palette.Lyric, loudness: 0.6); // No loudness meter here; Breathe just glows steadily.
    }

    /// <summary>
    /// Text is sized against the whole screen, not the lyrics band, so words come out the same
    /// size as on the overlay (the styles still keep them inside the band).
    /// </summary>
    private void UpdateLook()
    {
        var look = new LyricLook(_font, _textScale, _root.ActualHeight);
        if (look != _look)
        {
            _look = look;
            _style.SetLook(look);
        }
    }

    /// <summary>
    /// The wallpaper: black, with a faint glow of a darkened ambient colour rising from below the
    /// bottom edge, like a dark phone wallpaper. Rebuilt only when the colour changes.
    /// </summary>
    private void UpdateWallpaper(Color ambient)
    {
        if (ambient == _wallpaperFor)
        {
            return;
        }
        _wallpaperFor = ambient;
        var glow = Ease.Mix(Colors.Black, ambient, 0.6);
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 1.15),
            GradientOrigin = new Point(0.5, 1.15),
            RadiusX = 0.85,
            RadiusY = 0.95,
        };
        brush.GradientStops.Add(new GradientStop(Ease.WithAlpha(glow, GlowStrength), 0));
        brush.GradientStops.Add(new GradientStop(Ease.WithAlpha(glow, GlowStrength * 0.35), 0.45));
        brush.GradientStops.Add(new GradientStop(Ease.WithAlpha(glow, 0), 1));
        brush.Freeze();
        _wallpaper.Fill = brush;
    }

    /// <summary>The lyrics sit between the clock and the now-playing card.</summary>
    private void PlaceLyricsArea(double height) => _lyricsArea.Margin =
        new Thickness(0, height * LockScreenChrome.LyricsTop, 0, height * (1 - LockScreenChrome.LyricsBottom));

    private void Dismiss(InputEventArgs e)
    {
        e.Handled = true;
        if (!_closing && _clock.WallSeconds - _openedAt >= InputGuardSeconds)
        {
            Close();
        }
    }

    private static BitmapSource? Decode(byte[]? bytes, int width)
    {
        if (bytes is null)
        {
            return null;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.DecodePixelWidth = width;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or FileFormatException)
        {
            Log.Warn($"Album art could not be decoded for the lock screen: {ex.Message}");
            return null;
        }
    }
}
