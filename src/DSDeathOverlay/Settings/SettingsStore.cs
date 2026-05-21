using System;
using System.IO;
using System.Text.Json;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Settings;

/// <summary>
/// Persisted overlay configuration. Plain record so JSON serialization is trivial.
/// </summary>
public sealed record OverlaySettings
{
    public double Left { get; init; } = 20;
    public double Top { get; init; } = 20;
    public double FontSize { get; init; } = 28;
}

/// <summary>
/// Loads and saves <see cref="OverlaySettings"/> to
/// <c>%LOCALAPPDATA%\DSDeathOverlay\settings.json</c>. All errors are logged and
/// degrade gracefully to defaults — settings are nice-to-have, not critical.
/// </summary>
public sealed class SettingsStore
{
    private readonly ILogger _log;
    private readonly string _path;

    public OverlaySettings Current { get; set; } = new();

    private SettingsStore(string path, ILogger log)
    {
        _path = path;
        _log = log;
    }

    public static SettingsStore Load(ILogger log)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DSDeathOverlay");
        string path = Path.Combine(dir, "settings.json");
        var store = new SettingsStore(path, log);

        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<OverlaySettings>(json);
                if (s is not null) store.Current = s;
            }
        }
        catch (Exception ex)
        {
            log.Log($"Failed to load settings from {path}: {ex.Message}. Using defaults.");
        }

        return store;
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            _log.Log($"Failed to save settings to {_path}: {ex.Message}");
        }
    }
}
