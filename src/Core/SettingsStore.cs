using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Constella.Core;

/// <summary>
/// Owns the current <see cref="AppSettings"/>. Changes go through <see cref="Update"/>, which
/// normalises, notifies listeners immediately and writes to disk shortly after (debounced,
/// so dragging a slider doesn't write the file 60 times a second).
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly DispatcherTimer _saveTimer;

    public SettingsStore()
    {
        Current = Load();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
    }

    /// <summary>Raised after every change, with the new settings.</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>The live settings. Treat as read-only; change them with <see cref="Update"/>.</summary>
    public AppSettings Current { get; private set; }

    public void Update(Action<AppSettings> change)
    {
        var next = Current.Clone();
        change(next);
        next.Normalize();
        Current = next;
        Changed?.Invoke(next);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes now (called on quit).</summary>
    public void Flush()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            Save();
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Settings file unreadable, using defaults ({ex.Message})");
        }
        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            // Write to a temp file first so a crash mid-write can't leave a half-written settings file.
            string temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
