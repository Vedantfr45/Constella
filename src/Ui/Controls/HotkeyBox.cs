using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Ui.Controls;

/// <summary>Shows the current shortcut; click it, then press a new key combination to change it.</summary>
internal sealed class HotkeyBox : System.Windows.Controls.Border
{
    private readonly System.Windows.Controls.TextBlock _text;
    private int _modifiers;
    private int _virtualKey;

    public HotkeyBox()
    {
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        BorderBrush = Theme.LineStrong;
        Background = Theme.Bg;
        Padding = new Thickness(12, 6, 12, 7);
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        HorizontalAlignment = HorizontalAlignment.Left;
        _text = Theme.Label(string.Empty, 12, Theme.Text, Theme.Mono);
        Child = _text;
    }

    /// <summary>Raised with (MOD_* flags, virtual-key code) when the user presses a new combination.</summary>
    public event Action<int, int>? Captured;

    public void SetHotkey(int modifiers, int virtualKey)
    {
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        if (!IsKeyboardFocused)
        {
            _text.Text = Describe(modifiers, virtualKey);
        }
    }

    /// <summary>"Ctrl + Alt + L" style text for a shortcut.</summary>
    public static string Describe(int modifiers, int virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & AppSettings.ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & AppSettings.ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & AppSettings.ModShift) != 0) parts.Add("Shift");
        if ((modifiers & AppSettings.ModWin) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            Key.Oem3 => "`",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            _ => key.ToString(),
        });
        return string.Join(" + ", parts);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        BorderBrush = Theme.Accent;
        _text.Foreground = Theme.Muted;
        _text.Text = "Press a shortcut…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        BorderBrush = Theme.LineStrong;
        _text.Foreground = Theme.Text;
        _text.Text = Describe(_modifiers, _virtualKey);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // Wait for the actual key.
        }

        int modifiers = 0;
        var held = Keyboard.Modifiers;
        if (held.HasFlag(ModifierKeys.Control)) modifiers |= AppSettings.ModControl;
        if (held.HasFlag(ModifierKeys.Alt)) modifiers |= AppSettings.ModAlt;
        if (held.HasFlag(ModifierKeys.Shift)) modifiers |= AppSettings.ModShift;
        if (held.HasFlag(ModifierKeys.Windows)) modifiers |= AppSettings.ModWin;
        if (modifiers == 0 || modifiers == AppSettings.ModShift)
        {
            _text.Text = "Add Ctrl or Alt";
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        Keyboard.ClearFocus();
        Captured?.Invoke(modifiers, virtualKey);
    }
}
