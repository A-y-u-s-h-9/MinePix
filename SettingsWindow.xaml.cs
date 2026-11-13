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

        // Reflect current settings into the UI
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

        CollapseDelaySlider.Value = _settings.CollapseDelayMs;
        CollapseDelayLabel.Text = $"{_settings.CollapseDelayMs} ms";

        _loading = false;
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
        if (_settings.StartWithWindows)
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
        Persist();
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
