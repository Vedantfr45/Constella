using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Constella.Core;

namespace Constella.Ui;

/// <summary>
/// The notification-area icon. Left-click opens or closes settings; right-click shows a small dark menu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _nowPlaying;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _lockScreen;

    public TrayIcon()
    {
        _nowPlaying = new ToolStripMenuItem("Nothing playing") { Enabled = false };
        _toggle = new ToolStripMenuItem("Hide lyrics", null, (_, _) => ToggleRequested?.Invoke());
        _lockScreen = new ToolStripMenuItem("Lock screen", null, (_, _) => LockScreenRequested?.Invoke());
        var settings = new ToolStripMenuItem("Settings…", null, (_, _) => SettingsRequested?.Invoke());
        settings.Font = new Font(settings.Font, System.Drawing.FontStyle.Bold);
        var loadLrc = new ToolStripMenuItem("Load .lrc file…", null, (_, _) => LoadLyricsRequested?.Invoke());
        var quit = new ToolStripMenuItem("Quit Constella", null, (_, _) => QuitRequested?.Invoke());

        _menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false },
            ShowImageMargin = false,
            BackColor = ToDrawing(Theme.RaisedColor),
            ForeColor = ToDrawing(Theme.TextColor),
            Padding = new Padding(4),
        };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _nowPlaying, new ToolStripSeparator(), _toggle, _lockScreen, settings, loadLrc, new ToolStripSeparator(), quit,
        });
        foreach (ToolStripItem item in _menu.Items)
        {
            item.ForeColor = ToDrawing(Theme.TextColor);
            item.Padding = new Padding(4, 3, 4, 3);
        }
        _nowPlaying.ForeColor = ToDrawing(Theme.MutedColor);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Constella",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                SettingsToggleRequested?.Invoke();
            }
        };
    }

    /// <summary>The menu's "Settings…" item: always opens (or brings forward) the settings window.</summary>
    public event Action? SettingsRequested;

    /// <summary>Left-click on the icon: opens settings, or closes them if they're showing.</summary>
    public event Action? SettingsToggleRequested;

    public event Action? ToggleRequested;

    public event Action? LockScreenRequested;

    public event Action? LoadLyricsRequested;

    public event Action? QuitRequested;

    public void SetNowPlaying(string? text)
    {
        _nowPlaying.Text = text ?? "Nothing playing";
        string tip = text is null ? "Constella" : "Constella · " + text;
        _icon.Text = tip.Length > 127 ? tip[..126] + "…" : tip; // Windows limit for tray tooltips.
    }

    public void SetLyricsVisible(bool visible, string shortcut)
    {
        _toggle.Text = visible ? "Hide lyrics" : "Show lyrics";
        _toggle.ShortcutKeyDisplayString = shortcut;
    }

    public void SetLockShortcut(string shortcut) => _lockScreen.ShortcutKeyDisplayString = shortcut;

    /// <summary>Shows a short Windows notification from the tray icon.</summary>
    public void Notify(string title, string text) => _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Constella.ico"));
        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static Color ToDrawing(System.Windows.Media.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>Recolours the standard WinForms menu to match the settings window.</summary>
    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        private static readonly Color Raised = ToDrawing(Theme.RaisedColor);
        private static readonly Color Line = ToDrawing(Theme.LineStrongColor);
        private static readonly Color Hover = ToDrawing(Theme.LineColor);

        public override Color ToolStripDropDownBackground => Raised;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Raised;
        public override Color ImageMarginGradientBegin => Raised;
        public override Color ImageMarginGradientMiddle => Raised;
        public override Color ImageMarginGradientEnd => Raised;
    }
}
