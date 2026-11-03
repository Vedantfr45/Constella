using System;
using System.Linq;
using Constella.Core;
using Forms = System.Windows.Forms;

namespace Constella.Overlay;

/// <summary>Places a window exactly over one monitor, above other windows.</summary>
internal static class ScreenCover
{
    /// <param name="monitor">Windows device name of the monitor; null or unknown means the main screen.</param>
    public static void Apply(IntPtr hwnd, string? monitor)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == monitor) ?? Forms.Screen.PrimaryScreen;
        if (screen is null)
        {
            return;
        }
        // Physical pixels, so the window covers the screen exactly at any scaling.
        var b = screen.Bounds;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_NOACTIVATE);
    }
}
