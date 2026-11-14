using System.Windows;
using System.Windows.Input;

namespace MinePix;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private bool _loading = true;

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        SyncUiFromSettings();
        _loading = false;
    }

    /// <summary>Reflects the current settings instance into every control.</summary>
    private void SyncUiFromSettings()
    {
        ThemeGraphite.IsChecked = _settings.Theme?.ToLowerInvariant() is "graphite" or null;
        ThemeGlass.IsChecked = _settings.Theme?.ToLowerInvariant() == "glass";
        ThemeBlack.IsChecked = _settings.Theme?.ToLowerInvariant() == "black";

        IslandModeSwitch.IsChecked = _settings.UseDynamicIsland;
        MusicInPillSwitch.IsChecked = _settings.ShowMusicInPill;
        StartWithWindowsSwitch.IsChecked = _settings.StartWithWindows;
        ClipboardEnabledSwitch.IsChecked = _settings.ClipboardEnabled;

        CollapsedWidthSlider.Value = _settings.CollapsedWidth;
        CollapsedWidthLabel.Text = $"{(int)_settings.CollapsedWidth} px";

        ExpandedHeightSlider.Value = _settings.ExpandedHeight;
        ExpandedHeightLabel.Text = $"{(int)_settings.ExpandedHeight} px";

        IslandTopOffsetSlider.Value = _settings.IslandTopOffset;
        IslandTopOffsetLabel.Text = $"{(int)_settings.IslandTopOffset} px";

        CollapseDelaySlider.Value = _settings.CollapseDelayMs;
        CollapseDelayLabel.Text = $"{_settings.CollapseDelayMs} ms";
    }

    private void Persist()
    {
        _settings.Save();
        if (Owner is MainWindow main)
        {
            try { main.RefreshFromSettings(); } catch { }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    // ---------- theme ----------

    private void ThemeGraphite_Click(object sender, RoutedEventArgs e) { _settings.Theme = "graphite"; Persist(); }
    private void ThemeGlass_Click(object sender, RoutedEventArgs e)    { _settings.Theme = "glass"; Persist(); }
    private void ThemeBlack_Click(object sender, RoutedEventArgs e)    { _settings.Theme = "black"; Persist(); }

    // ---------- appearance ----------

    private void IslandModeSwitch_Click(object sender, RoutedEventArgs e)
    {
        _settings.UseDynamicIsland = IslandModeSwitch.IsChecked == true;
        Persist();
    }

    private void MusicInPillSwitch_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowMusicInPill = MusicInPillSwitch.IsChecked == true;
        Persist();
    }

    // ---------- behaviour ----------

    private void StartWithWindowsSwitch_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartWithWindowsSwitch.IsChecked == true;
        SyncAutoStart(_settings.StartWithWindows);
        Persist();
    }

    /// <summary>Creates or deletes the HKCU Run key so autostart matches the setting.</summary>
    private static void SyncAutoStart(bool enabled)
    {
        if (enabled)
        {
            try
            {
                var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(exe))
                    Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "MinePix", exe);
            }
            catch { }
        }
        else
        {
            try { Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)?.DeleteValue("MinePix", false); } catch { }
        }
    }

    private void ClipboardEnabledSwitch_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClipboardEnabled = ClipboardEnabledSwitch.IsChecked == true;
        Persist();
    }

    private void CollapsedWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.CollapsedWidth = e.NewValue;
        CollapsedWidthLabel.Text = $"{(int)e.NewValue} px";
        Persist();
    }

    private void ExpandedHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.ExpandedHeight = e.NewValue;
        ExpandedHeightLabel.Text = $"{(int)e.NewValue} px";
        Persist();
    }

    private void IslandTopOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.IslandTopOffset = e.NewValue;
        IslandTopOffsetLabel.Text = $"{(int)e.NewValue} px";
        Persist();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Save();
        try
        {
            var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(exe))
            {
                // defer the relaunch ~1s so this instance releases the single-instance mutex first
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c ping 127.0.0.1 -n 2 >nul && start \"\" \"{exe}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
            }
        }
        catch { }
        Application.Current.Shutdown();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dr = MessageBox.Show(
            this,
            "Reset all MinePix settings to their factory defaults?\nThis overwrites your current configuration.",
            "MinePix — Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (dr != MessageBoxResult.Yes)
            return;

        var d = new Settings();
        _settings.Theme = d.Theme;
        _settings.UseDynamicIsland = d.UseDynamicIsland;
        _settings.StartWithWindows = d.StartWithWindows;
        _settings.ShowMusicInPill = d.ShowMusicInPill;
        _settings.ClipboardEnabled = d.ClipboardEnabled;
        _settings.ClipboardMaxItems = d.ClipboardMaxItems;
        _settings.CollapsedWidth = d.CollapsedWidth;
        _settings.CollapsedHeight = d.CollapsedHeight;
        _settings.ExpandedWidth = d.ExpandedWidth;
        _settings.ExpandedHeight = d.ExpandedHeight;
        _settings.IslandTopOffset = d.IslandTopOffset;
        _settings.CollapseDelayMs = d.CollapseDelayMs;

        // keep the autostart registry entry consistent with the reset default
        SyncAutoStart(_settings.StartWithWindows);

        _loading = true;
        SyncUiFromSettings();
        _loading = false;

        // applies live (theme, sizes, offset, delay) via the shared settings + Owner refresh
        Persist();
    }

    private void CollapseDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.CollapseDelayMs = (int)e.NewValue;
        CollapseDelayLabel.Text = $"{_settings.CollapseDelayMs} ms";
        Persist();
    }

    // ---------- about ----------

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Settings.SettingsDir);
        System.Diagnostics.Process.Start("explorer.exe", Settings.SettingsDir);
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start("explorer", "https://github.com/A-y-u-s-h-9/MinePix");

    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer", "https://github.com/A-y-u-s-h-9/MinePix/releases/latest");
        }
        catch { }
    }
}
