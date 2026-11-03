using System;
using System.Diagnostics;
using System.IO;

namespace Constella.Core;

/// <summary>
/// Tiny thread-safe file logger (%LOCALAPPDATA%\Constella\constella.log).
/// The file is restarted when it grows past 1 MB so it never fills the disk.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {message}";
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes)
                {
                    file.Delete();
                }
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down (e.g. the file is open in an editor).
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
