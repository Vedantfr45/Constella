using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Constella.Core;

namespace Constella.Ui;

/// <summary>A system-wide keyboard shortcut (works while any app has focus).</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0xC0DE;
    private readonly HwndSource _window;
    private bool _registered;

    public GlobalHotkey()
    {
        // A message-only window: invisible, just receives WM_HOTKEY.
        _window = new HwndSource(new HwndSourceParameters("ConstellaHotkey")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <returns>False if the combination is already taken by another app.</returns>
    public bool Register(int modifiers, int virtualKey)
    {
        Unregister();
        _registered = NativeMethods.RegisterHotKey(_window.Handle, HotkeyId, (uint)modifiers | NativeMethods.MOD_NOREPEAT, (uint)virtualKey);
        if (!_registered)
        {
            Log.Warn($"Hotkey registration failed (Win32 error {Marshal.GetLastWin32Error()})");
        }
        return _registered;
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    private void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
