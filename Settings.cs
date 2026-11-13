using System.IO;
using System.Text.Json;

namespace MinePix;

/// <summary>
/// User-tweakable settings, persisted to %APPDATA%\MinePix\settings.json.
/// Best-effort: a missing or corrupt file falls back to defaults, never crashes.
/// </summary>
public sealed class Settings
{
    public string Theme { get; set; } = "graphite";
    public bool StartWithWindows { get; set; } = false;
    public bool ShowMusicInPill { get; set; } = true;
    public double CollapsedWidth { get; set; } = 220;
    public double CollapsedHeight { get; set; } = 34;
    public double ExpandedWidth { get; set; } = 560;
    public double ExpandedHeight { get; set; } = 235;
    public int CollapseDelayMs { get; set; } = 250;

    public static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");

    public static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (s is not null) return s;
            }
        }
        catch
        {
            // corrupt settings -> start from defaults, don't lose the user over it
        }

        var defaults = new Settings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // settings are best-effort; the app must not fail because of them
        }
    }
}
