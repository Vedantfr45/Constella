using System;
using Microsoft.Win32;

namespace Constella.Core;

/// <summary>Adds or removes Constella from the current user's "run at sign-in" list (no admin needed).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Constella";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is string exe)
            {
                key.SetValue(ValueName, $"\"{exe}\" {App.BackgroundArgument}");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Log.Error("Could not update start-with-Windows setting", ex);
        }
    }
}
