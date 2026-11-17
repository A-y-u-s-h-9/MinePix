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
    public bool UseDynamicIsland { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool ShowMusicInPill { get; set; } = true;
    public bool ClipboardEnabled { get; set; } = true;
    public int ClipboardMaxItems { get; set; } = 100;
    public double CollapsedWidth { get; set; } = 220;
    public double CollapsedHeight { get; set; } = 34;
    public double ExpandedWidth { get; set; } = 480;
    public double ExpandedHeight { get; set; } = 270;
    public double IslandTopOffset { get; set; } = 10;
    public List<string> QuickShortcuts { get; set; } = new();
    public int CollapseDelayMs { get; set; } = 250;

    // ---------- M8: game strip ----------
    public bool GameStripEnabled { get; set; } = true;
    public bool GameShowFps { get; set; } = true;
    public bool GameShowCpu { get; set; } = true;
    public bool GameShowGpu { get; set; } = true;
    public bool GameShowVram { get; set; } = true;
    public bool GameShowTempPower { get; set; } = true;
    /// <summary>Process names force-treated as games regardless of fullscreen (windowed games).</summary>
    public List<string> GameProcessNames { get; set; } = new();
    /// <summary>Process names never treated as games (fullscreen browsers/terminals/etc.). Seeded with sane defaults.</summary>
    public List<string> GameExclusions { get; set; } = new()
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe",
        "code.exe", "devenv.exe", "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe", "onenote.exe",
        "notepad.exe", "msteams.exe", "slack.exe", "zoom.exe", "vlc.exe",
        "mpc-hc.exe", "mpc-hc64.exe", "mpv.exe", "spotify.exe",
        "discord.exe", "telegram.exe", "signal.exe",
        "explorer.exe", "shelltaskbar.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
        "searchhost.exe", "textinputhost.exe", "applicationframehost.exe", "msedgewebview2.exe",
        "windowsterminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "soffice.exe", "searchapplication.exe", "dwm.exe", "minepix.exe",
    };

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
