using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace MinePix;

/// <summary>
/// The island: borderless, transparent, always-on-top, flush with the top edge (notch look).
/// Collapsed pill shows the clock (or the current track); hovering expands the card.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _hoverCheckTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _mediaTimer;
    private readonly DispatcherTimer _clipStatusTimer;
    private TaskbarIcon? _tray;
    private bool _expanded;
    private bool _islandHidden;
    private bool _seeking;
    private bool _controlsActive;
    private string _tab = "music";
    private bool _volumeDragging;
    private bool _brightnessDragging;
    private DateTime _lastBrightnessApply = DateTime.MinValue;
    private double _activeExpandedHeight;
    private readonly DispatcherTimer _controlsTimer;
    private RadioService.Snapshot _radios = new(null, null);

    public MainWindow()
    {
        _settings = Settings.Load();
        _activeExpandedHeight = _settings.ExpandedHeight;

        InitializeComponent();
        ApplyTheme();
        ApplyIslandShape();

        _settingsCollapsedWidth = _settings.CollapsedWidth;
        _settingsCollapsedHeight = _settings.CollapsedHeight;

        Width = ComputeCollapsedWidth();
        Height = ComputeCollapsedHeight();
        Top = 14;
        Left = CenteredLeft(Width);

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_settings.CollapseDelayMs) };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); Collapse(); };

        // polling backstop — the Island's hit-test region can flake (transparent borders, edge cases),
        // so confirm the cursor is actually inside before we collapse.
        _hoverCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _hoverCheckTimer.Tick += (_, _) =>
        {
            if (!_expanded) return;
            var p = System.Windows.Input.Mouse.GetPosition(this);
            bool inside = p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
            if (inside)
                _collapseTimer.Stop();
            else if (!_collapseTimer.IsEnabled)
                _collapseTimer.Start();
        };
        _hoverCheckTimer.Start();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => PillClock.Text = DateTime.Now.ToString("HH:mm");
        PillClock.Text = DateTime.Now.ToString("HH:mm");
        _clockTimer.Start();

        // keep the window centered while its width animates
        SizeChanged += (_, _) => Left = CenteredLeft(ActualWidth);

        // music
        MusicService.Instance.Changed += OnMusicChanged;
        _ = MusicService.Instance.InitializeAsync(Dispatcher);
        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _mediaTimer.Tick += (_, _) => UpdateProgressTick();
        _mediaTimer.Start();

        // clipboard
        ClipboardStore.MaxItems = _settings.ClipboardMaxItems;
        if (_settings.ClipboardEnabled)
            ClipboardService.Instance.Start();
        ClipboardService.Instance.ItemsChanged += RefreshClipboardList;

        _clipStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _clipStatusTimer.Tick += (_, _) => { _clipStatusTimer.Stop(); ClipStatus.Visibility = Visibility.Collapsed; };

        _controlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _controlsTimer.Tick += (_, _) =>
        {
            if (_controlsActive)
                _ = RefreshControlsAsync();
        };
        _controlsTimer.Start();

        InitTray();

        // dev affordance: force-open the standalone settings window (for headless screenshots/tests)
        if (Environment.GetEnvironmentVariable("MINEPIX_FORCE_SETTINGS") == "1")
            Dispatcher.BeginInvoke(new Action(() => { var w = new SettingsWindow(_settings) { Owner = this }; w.Show(); }), DispatcherPriority.Loaded);

        // prime the music UI so the idle card is visible immediately
        RefreshMusicUi();

        // dev affordance: force the expanded card open (used for headless screenshots/tests)
        if (Environment.GetEnvironmentVariable("MINEPIX_FORCE_EXPANDED") == "1")
            Dispatcher.BeginInvoke(new Action(Expand), DispatcherPriority.Loaded);

        if (Environment.GetEnvironmentVariable("MINEPIX_FORCE_TAB") is string forceTab && (forceTab == "clipboard" || forceTab == "controls"))
            Dispatcher.BeginInvoke(new Action(() => { Expand(); SetTab(forceTab); }), DispatcherPriority.Loaded);

        var forceSelector = Environment.GetEnvironmentVariable("MINEPIX_FORCE_SELECTOR");
        if (forceSelector == "wifi" || forceSelector == "bluetooth")
        {
            Dispatcher.BeginInvoke(new Action(() => { Expand(); ShowSelector(forceSelector == "wifi" ? SelectorMode.Wifi : SelectorMode.Bluetooth); }), DispatcherPriority.Loaded);
        }
    }

    private double _settingsCollapsedWidth;
    private double _settingsCollapsedHeight;

    /// <summary>
    /// Applies the current island shape (dynamic-island vs classic pill): window size,
    /// corner radius, camera dot sizing and the drop-shadow sizing.
    /// </summary>
    private void ApplyIslandShape()
    {
        if (_settings.UseDynamicIsland)
        {
            // a full capsule: dynamic island shape, not a notch
            Resources["PillRadius"] = new CornerRadius(22);
            PillDot.Width = 10; PillDot.Height = 10;
            PillDot.Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1F));
        }
        else
        {
            // classic: flat top, rounded bottom
            Resources["PillRadius"] = new CornerRadius(0, 0, 18, 18);
            PillDot.Width = 8; PillDot.Height = 8;
            PillDot.Fill = new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xFF));
        }

        // island top offset for "floating" feel
        if (!_expanded)
        {
            Top = 14;
            Width = ComputeCollapsedWidth();
            Height = ComputeCollapsedHeight();
            Left = CenteredLeft(Width);
        }
    }

    private double ComputeCollapsedWidth()  => _settings.CollapsedWidth;
    private double ComputeCollapsedHeight() => _settings.UseDynamicIsland ? 44 : _settings.CollapsedHeight;

    private static double CenteredLeft(double width) => (SystemParameters.PrimaryScreenWidth - width) / 2;

    /// <summary>
    /// Called from the standalone settings window so the island reflects changes live.
    /// Re-reads the shared Settings instance, applies theme, and re-syncs pill/size.
    /// </summary>
    public void RefreshFromSettings()
    {
        ApplyTheme();
        ApplyIslandShape();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(_settings.CollapseDelayMs);

        if (!_expanded)
        {
            Width = ComputeCollapsedWidth();
            Height = ComputeCollapsedHeight();
            Left = CenteredLeft(Width);
        }
        else
        {
            Width = _settings.ExpandedWidth;
            Height = _settings.ExpandedHeight;
            Left = CenteredLeft(Width);
        }
    }

    /// <summary>
    /// Applies the configured theme: the island tint. The DWM blur/acrylic APIs were probed and
    /// rejected on this stack — they force the layered WPF window to composite opaque.
    /// Pure per-pixel alpha gives the cleanest true-glass look; tint is kept low.
    /// </summary>
    /// <summary>
    /// Returns true when the current system App Theme (Windows Personalization) is light.
    /// Falls back to dark on any error or older builds.
    /// </summary>
    private static bool IsAppLightTheme()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            return v is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyTheme()
    {
        var glass = _settings.Theme?.ToLowerInvariant() is "glass";
        bool light = IsAppLightTheme();
        var themeKey = _settings.Theme?.ToLowerInvariant();

        Color top, mid, bot;
        switch (themeKey)
        {
            case "glass":
                top = Color.FromArgb(0x44, 0xC8, 0xD4, 0xE0);
                mid = Color.FromArgb(0x33, 0x90, 0xA4, 0xB8);
                bot = Color.FromArgb(0x44, 0x20, 0x26, 0x30);
                break;
            case "black":
                top = Color.FromArgb(0xBF, 0x14, 0x14, 0x16);
                mid = Color.FromArgb(0x99, 0x11, 0x11, 0x13);
                bot = Color.FromArgb(0xBF, 0x06, 0x06, 0x08);
                break;
            default: // graphite
                top = light ? Color.FromArgb(0x55, 0xF5, 0xF5, 0xF7) : Color.FromArgb(0x59, 0x2E, 0x2E, 0x35);
                mid = light ? Color.FromArgb(0x38, 0xE8, 0xE8, 0xED) : Color.FromArgb(0x40, 0x38, 0x38, 0x3F);
                bot = light ? Color.FromArgb(0x55, 0xB8, 0xB8, 0xC2) : Color.FromArgb(0x59, 0x1F, 0x1F, 0x26);
                break;
        }

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        gradient.GradientStops.Add(new GradientStop(top, 0.0));
        gradient.GradientStops.Add(new GradientStop(mid, 0.5));
        gradient.GradientStops.Add(new GradientStop(bot, 1.0));

        Island.Background = gradient;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // start the travelling glint sweep once the window has a handle
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var anim = new DoubleAnimation
        {
            From = -70,
            To = 800,
            Duration = new Duration(TimeSpan.FromSeconds(6.5)),
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(anim, TravellingGlintTransform);
        Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.XProperty));
        sb.Children.Add(anim);
        sb.Begin(this);
    }

    // ---------- island expand / collapse ----------

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        Expand();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_seeking) return; // don't collapse while dragging the seek bar
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;

        // snap immediately so the island never gets stuck at compact height
        Width  = _settings.ExpandedWidth;
        Height = _activeExpandedHeight;

        AnimateOpacity(PillView, 0, 120, () => PillView.Visibility = Visibility.Collapsed);
        ExpandedView.Visibility = Visibility.Visible;
        AnimateOpacity(ExpandedView, 1, 200);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;

        Width  = ComputeCollapsedWidth();
        Height = ComputeCollapsedHeight();

        AnimateOpacity(ExpandedView, 0, 120, () => ExpandedView.Visibility = Visibility.Collapsed);
        PillView.Visibility = Visibility.Visible;
        AnimateOpacity(PillView, 1, 200);
    }

    private void AnimateWidthHeight(double width, double height)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(240));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
    }

    private void AnimateOpacity(UIElement element, double to, int durationMs, Action? onCompleted = null)
    {
        var animation = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(durationMs)));
        animation.Completed += (_, _) => onCompleted?.Invoke();
        element.BeginAnimation(OpacityProperty, animation);
    }

    // ---------- music ----------

    private void OnMusicChanged()
    {
        RefreshMusicUi();
        UpdatePillMusicState();
        UpdateProgressTick();
    }

    private void RefreshMusicUi()
    {
        bool has = MusicService.Instance.HasSession;

        MusicCard.Visibility = _tab == "music" ? Visibility.Visible : Visibility.Collapsed;

        if (has)
        {
            MusicTitle.Text = MusicService.Instance.Title.Length > 0 ? MusicService.Instance.Title : "Unknown track";
            MusicArtist.Text = MusicService.Instance.Artist.Length > 0 ? MusicService.Instance.Artist : "Unknown artist";

            if (MusicService.Instance.Artwork is not null)
            {
                MusicArt.Source = MusicService.Instance.Artwork;
                MusicArt.Visibility = Visibility.Visible;
                MusicArtPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                MusicArt.Visibility = Visibility.Collapsed;
                MusicArtPlaceholder.Visibility = Visibility.Visible;
            }

            PlayPauseButton.Content = MusicService.Instance.IsPlaying ? "\u23F8" : "\u25B6";
            MusicDuration.Text = MusicService.FormatTime(MusicService.Instance.Duration);
        }
        else
        {
            MusicTitle.Text = "Nothing playing";
            MusicArtist.Text = "Start something in any media app";
            MusicDuration.Text = "0:00";
            MusicPos.Text = "0:00";
            SeekFill.Width = 0;
            PlayPauseButton.Content = "\u25B6";
            MusicArt.Visibility = Visibility.Collapsed;
            MusicArtPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void UpdatePillMusicState()
    {
        bool showMusic = _settings.ShowMusicInPill && MusicService.Instance.HasSession
                         && MusicService.Instance.Title.Length > 0;

        PillDot.Visibility = showMusic ? Visibility.Collapsed : Visibility.Visible;
        PillNote.Visibility = showMusic ? Visibility.Visible : Visibility.Collapsed;
        PillTitle.Text = showMusic ? MusicService.Instance.Title : "MinePix";
    }

    private void UpdateProgressTick()
    {
        if (!MusicService.Instance.HasSession) return;

        var duration = MusicService.Instance.Duration;
        var position = MusicService.Instance.DisplayPosition;
        if (duration.TotalMilliseconds <= 0) return;

        if (!_seeking)
            MusicPos.Text = MusicService.FormatTime(position);
        MusicDuration.Text = MusicService.FormatTime(duration);

        if (!_seeking && SeekTrack.ActualWidth > 0)
        {
            double fraction = position.TotalMilliseconds / duration.TotalMilliseconds;
            fraction = Math.Clamp(fraction, 0, 1);
            SeekFill.Width = fraction * SeekTrack.ActualWidth;
        }
    }

    private double SeekFractionFromEvent(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(SeekTrack);
        double width = SeekTrack.ActualWidth;
        return width <= 0 ? 0 : Math.Clamp(p.X / width, 0, 1);
    }

    private void SeekTrack_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!MusicService.Instance.HasSession) return;
        _seeking = true;
        SeekTrack.CaptureMouse();
        SeekFill.Width = SeekFractionFromEvent(e) * SeekTrack.ActualWidth;
        MusicPos.Text = MusicService.FormatTime(TimeSpan.FromMilliseconds(SeekFractionFromEvent(e) * MusicService.Instance.Duration.TotalMilliseconds));
        e.Handled = true;
    }

    private void SeekTrack_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_seeking) return;
        SeekFill.Width = SeekFractionFromEvent(e) * SeekTrack.ActualWidth;
        MusicPos.Text = MusicService.FormatTime(TimeSpan.FromMilliseconds(SeekFractionFromEvent(e) * MusicService.Instance.Duration.TotalMilliseconds));
    }

    private async void SeekTrack_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        SeekTrack.ReleaseMouseCapture();
        await MusicService.Instance.SeekAsync(SeekFractionFromEvent(e));
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
        => _ = MusicService.Instance.SkipPreviousAsync();

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        => _ = MusicService.Instance.TogglePlayPauseAsync();

    private void NextButton_Click(object sender, RoutedEventArgs e)
        => _ = MusicService.Instance.SkipNextAsync();

    // ---------- tabs (Music / Clipboard / Controls) ----------

    private void SetTab(string tab)
    {
        _tab = tab;
        _controlsActive = tab == "controls";

        var activeStyle = (Style)FindResource("IslandIconButtonActive");
        var inactiveStyle = (Style)FindResource("IslandIconButton");
        IconMusic.Style = tab == "music" ? activeStyle : inactiveStyle;
        IconClipboard.Style = tab == "clipboard" ? activeStyle : inactiveStyle;
        IconControls.Style = tab == "controls" ? activeStyle : inactiveStyle;
        IconSettings.Style = inactiveStyle; // settings is momentary (opens window)

        MusicCard.Visibility = tab == "music" && MusicService.Instance.HasSession ? Visibility.Visible : Visibility.Collapsed;
        ClipPanel.Visibility = tab == "clipboard" ? Visibility.Visible : Visibility.Collapsed;
        ControlsPanel.Visibility = tab == "controls" ? Visibility.Visible : Visibility.Collapsed;

        switch (tab)
        {
            case "clipboard":
                _activeExpandedHeight = _settings.ExpandedHeight;
                RefreshClipboardList();
                break;
            case "controls":
                _activeExpandedHeight = _settings.ExpandedHeight;
                _ = RefreshControlsAsync();
                break;
            case "music":
                _activeExpandedHeight = _settings.ExpandedHeight;
                RefreshMusicUi();
                break;
            default:
                _activeExpandedHeight = _settings.ExpandedHeight;
                RefreshMusicUi();
                break;
        }
        AnimateWidthHeight(_settings.ExpandedWidth, _activeExpandedHeight);
    }

    private void IconMusic_Click(object sender, RoutedEventArgs e)     => SetTab("music");
    private void IconClipboard_Click(object sender, RoutedEventArgs e) => SetTab("clipboard");
    private void IconControls_Click(object sender, RoutedEventArgs e)  => SetTab("controls");
    private void IconSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings) { Owner = this };
        win.Show();
    }

    // ---------- controls ----------

    private double VerticalSliderFraction(Border track, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(track);
        if (track.ActualHeight <= 0) return 0;
        // 1.0 at the top of the fill, 0.0 at the bottom
        return Math.Clamp(1.0 - (p.Y / track.ActualHeight), 0, 1);
    }

    private static System.Windows.Media.Brush Brush(string hex) =>
        new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);

    private async Task RefreshControlsAsync()
    {
        try
        {
            // volume
            float vol = CoreAudio.GetVolume();
            bool muted = CoreAudio.GetMuted();
            if (!_volumeDragging && VolumeTrack.ActualHeight > 0)
                UpdateVolume(vol, muted, VolumeTrack.ActualHeight);

            // brightness
            int? brightness = BrightnessService.GetBrightness();
            if (brightness is null)
            {
                BrightnessPercent.Text = "--%";
                BrightnessHint.Visibility = Visibility.Visible;
                BrightnessTrack.IsEnabled = false;
                BrightnessTrack.Opacity = 0.4;
                BrightnessFill.Height = 0;
            }
            else
            {
                BrightnessHint.Visibility = Visibility.Collapsed;
                BrightnessTrack.IsEnabled = true;
                BrightnessTrack.Opacity = 1;
                if (!_brightnessDragging && BrightnessTrack.ActualHeight > 0)
                    UpdateBrightness(brightness.Value / 100.0, BrightnessTrack.ActualHeight);
                BrightnessPercent.Text = $"{brightness.Value}%";
            }

            // radios
            _radios = await RadioService.GetRadiosAsync();
            WifiToggle.IsEnabled = _radios.Wifi is not null;
            BluetoothToggle.IsEnabled = _radios.Bluetooth is not null;
            WifiToggle.Opacity = _radios.Wifi is not null ? 1 : 0.45;
            BluetoothToggle.Opacity = _radios.Bluetooth is not null ? 1 : 0.45;
            WifiToggle.IsChecked = _radios.Wifi is not null && RadioService.IsOn(_radios.Wifi);
            BluetoothToggle.IsChecked = _radios.Bluetooth is not null && RadioService.IsOn(_radios.Bluetooth);

            var wifiOn = WifiToggle.IsChecked == true;
            var btOn = BluetoothToggle.IsChecked == true;
            WifiCaption.Text = wifiOn ? (RadioService.WifiName() ?? "Wi-Fi") : "Wi-Fi";
            WifiCaption.Foreground = Brush(wifiOn ? "#F5F5F7" : "#8E8E93");
            WifiSubCaption.Text = wifiOn ? "Connected" : "Off";
            WifiSubCaption.Foreground = Brush(wifiOn ? "#8AB4FF" : "#6E6E73");

            BluetoothCaption.Text = "Bluetooth";
            BluetoothCaption.Foreground = Brush(btOn ? "#F5F5F7" : "#8E8E93");
            BluetoothSubCaption.Text = btOn ? "On" : "Off";
            BluetoothSubCaption.Foreground = Brush(btOn ? "#8AB4FF" : "#6E6E73");

            // mute tile state
            bool muted2 = CoreAudio.GetMuted();
            MuteCaption.Text = "Sound";
            MuteCaption.Foreground = Brush(muted2 ? "#8E8E93" : "#F5F5F7");
            MuteSubCaption.Text = muted2 ? "Muted" : "On";
            MuteSubCaption.Foreground = Brush(muted2 ? "#8E8E93" : "#8AB4FF");
            MuteToggleTile.IsChecked = !muted2;

            // battery + power plan
            var snap = PowerService.GetSnapshot();
            var activeStyle = (Style)FindResource("IslandPowerModeButtonActive");
            var inactiveStyle = (Style)FindResource("IslandPowerModeButton");
            PowerBatteryButton.Style = snap is not null && snap.ActiveScheme == "Battery saver" ? activeStyle : inactiveStyle;
            PowerBalancedButton.Style = snap is not null && snap.ActiveScheme == "Balanced" ? activeStyle : inactiveStyle;
            PowerPerformanceButton.Style = snap is not null && snap.ActiveScheme == "Performance" ? activeStyle : inactiveStyle;

            if (snap is not null)
            {
                BatteryGlyph.Text = snap.PluggedIn ? "\U0001F50B" : "\U0001FAAB";
                BatteryPercent.Text = $"{snap.Percent}%";
                BatteryStatus.Text = snap.PluggedIn
                    ? (snap.Percent >= 100 ? "Full " : "Charging")
                    : snap.Percent <= 20 ? "Low" : "On battery";
                BatteryPercent.Foreground = Brush(
                    snap.Percent <= 20 ? "#FF453A" : "#30D158");
            }
            else
            {
                BatteryGlyph.Text = "—";
                BatteryPercent.Text = "n/a";
                BatteryStatus.Text = "No battery";
                BatteryPercent.Foreground = Brush("#6E6E73");
            }

            // mirror selector state if the selector is open
            if (_selector != SelectorMode.None)
            {
                SelectorRadioState.Text = _selector == SelectorMode.Wifi
                    ? (wifiOn ? (RadioService.WifiName() ?? "On") : "Off")
                    : (btOn ? "On" : "Off");
                SetSelectorRadioSwitchState();
            }
        }
        catch (Exception ex)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "controls.log"),
                    $"{DateTime.Now:s} RefreshControlsAsync: {ex}{Environment.NewLine}");
            }
            catch { }
        }
    }

    private void UpdateVolume(float vol, bool muted, double trackHeight)
    {
        VolumeFill.Height = vol * trackHeight;
        VolumePercent.Text = muted ? "Muted" : $"{(int)Math.Round(vol * 100)}%";
        MuteButton.Content = muted ? "\U0001F507" : (vol < 0.5f ? "\U0001F509" : "\U0001F50A");
    }

    private void UpdateBrightness(double fraction, double trackHeight)
    {
        BrightnessFill.Height = fraction * trackHeight;
    }

    private void ApplyVolumeFromEvent(System.Windows.Input.MouseEventArgs e)
    {
        if (VolumeTrack.ActualHeight <= 0) return;
        double fraction = VerticalSliderFraction(VolumeTrack, e);
        CoreAudio.SetVolume((float)fraction);
        if (fraction > 0) CoreAudio.SetMuted(false);
        UpdateVolume((float)fraction, CoreAudio.GetMuted(), VolumeTrack.ActualHeight);
    }

    private void VolumeTrack_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _volumeDragging = true;
        VolumeTrack.CaptureMouse();
        ApplyVolumeFromEvent(e);
        e.Handled = true;
    }

    private void VolumeTrack_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_volumeDragging) ApplyVolumeFromEvent(e);
    }

    private void VolumeTrack_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _volumeDragging = false;
        VolumeTrack.ReleaseMouseCapture();
        ApplyVolumeFromEvent(e);
    }

    private void ApplyBrightnessFromEvent(System.Windows.Input.MouseEventArgs e)
    {
        if (BrightnessTrack.ActualHeight <= 0) return;
        double fraction = VerticalSliderFraction(BrightnessTrack, e);
        int level = (int)Math.Round(fraction * 100);
        if (DateTime.Now - _lastBrightnessApply > TimeSpan.FromMilliseconds(150) || level % 5 == 0)
        {
            BrightnessService.SetBrightness(level);
            _lastBrightnessApply = DateTime.Now;
        }
        UpdateBrightness(level / 100.0, BrightnessTrack.ActualHeight);
        BrightnessPercent.Text = $"{level}%";
    }

    private void BrightnessTrack_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!BrightnessTrack.IsEnabled) return;
        _brightnessDragging = true;
        BrightnessTrack.CaptureMouse();
        ApplyBrightnessFromEvent(e);
        e.Handled = true;
    }

    private void BrightnessTrack_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_brightnessDragging) ApplyBrightnessFromEvent(e);
    }

    private void BrightnessTrack_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_brightnessDragging) return;
        _brightnessDragging = false;
        BrightnessTrack.ReleaseMouseCapture();
        ApplyBrightnessFromEvent(e);
    }

    private async void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        CoreAudio.SetMuted(!CoreAudio.GetMuted());
        await RefreshControlsAsync();
    }

    private void WifiToggle_Click(object sender, RoutedEventArgs e)
        => ShowSelector(SelectorMode.Wifi);

    private void BluetoothToggle_Click(object sender, RoutedEventArgs e)
        => ShowSelector(SelectorMode.Bluetooth);

    private async void SelectorRadioSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool want = ((ToggleButton)sender).IsChecked == true;
        if (_selector == SelectorMode.Wifi)
            await RadioService.SetAsync(_radios.Wifi, want);
        else if (_selector == SelectorMode.Bluetooth)
            await RadioService.SetAsync(_radios.Bluetooth, want);
        await Task.Delay(350);
        await RefreshControlsAsync();
        await PopulateSelectorAsync();
    }

    // ---------- clipboard ----------

    private void RefreshClipboardList()
    {
        string search = ClipSearch?.Text?.Trim() ?? "";
        if (ClipSearchPlaceholder is not null)
            ClipSearchPlaceholder.Visibility = string.IsNullOrEmpty(search) ? Visibility.Visible : Visibility.Collapsed;

        var items = ClipboardStore.GetRecent(search);
        var rows = items.Select(i => new ClipRowViewModel
        {
            Id = i.Id,
            Glyph = i.Kind switch { "files" => "F", "image" => "I", _ => "T" },
            Preview = i.Preview,
            MetaLine = i.Meta + " · " + ClipboardService.RelativeTime(i.LastUsedAt),
            Pinned = i.Pinned,
        }).ToList();

        var total = ClipboardStore.Count();
        ClipList.ItemsSource = rows;
        ClipCount.Text = total == 1 ? "1 item" : $"{total} items";
        ClipEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClipSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshClipboardList();

    private void ClipRow_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button || e.OriginalSource is ToggleButton) return;
        if (sender is FrameworkElement fe && fe.DataContext is ClipRowViewModel vm)
        {
            var item = ClipboardStore.GetById(vm.Id);
            if (item is not null)
            {
                ClipboardService.CopyBack(item);
                FlashClipStatus("Copied ✓");
                RefreshClipboardList();
            }
        }
    }

    private void ClipPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && long.TryParse(btn.Tag?.ToString(), out long id))
        {
            ClipboardStore.SetPinned(id, btn.IsChecked == true);
            RefreshClipboardList();
        }
    }

    private void ClipDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && long.TryParse(btn.Tag?.ToString(), out long id))
        {
            ClipboardStore.Delete(id);
            RefreshClipboardList();
        }
    }

    private void ClipClearUnpinned_Click(object sender, RoutedEventArgs e)
    {
        ClipboardStore.ClearUnpinned();
        FlashClipStatus("Cleared unpinned");
        RefreshClipboardList();
    }

    private void ClipClearAll_Click(object sender, RoutedEventArgs e)
    {
        ClipboardStore.ClearAll();
        FlashClipStatus("Cleared everything");
        RefreshClipboardList();
    }

    private void FlashClipStatus(string text)
    {
        ClipStatus.Text = text;
        ClipStatus.Visibility = Visibility.Visible;
        _clipStatusTimer.Stop();
        _clipStatusTimer.Start();
    }

    private async void MuteTile_Click(object sender, RoutedEventArgs e)
    {
        CoreAudio.SetMuted(!CoreAudio.GetMuted());
        await RefreshControlsAsync();
    }

    private async void PowerBattery_Click(object sender, RoutedEventArgs e)
    {
        PowerService.SetScheme("Battery");
        await Task.Delay(300);
        await RefreshControlsAsync();
    }

    private async void PowerBalanced_Click(object sender, RoutedEventArgs e)
    {
        PowerService.SetScheme("Balanced");
        await Task.Delay(300);
        await RefreshControlsAsync();
    }

    private async void PowerPerformance_Click(object sender, RoutedEventArgs e)
    {
        PowerService.SetScheme("Performance");
        await Task.Delay(300);
        await RefreshControlsAsync();
    }

    // ---------- selector sheet (Wi-Fi / Bluetooth) ----------

    private SelectorMode _selector = SelectorMode.None;
    private const int SelectorWifiHeight = 380;

    private void SetSelectorRadioSwitchState()
    {
        bool on;
        if (_selector == SelectorMode.Wifi) on = _radios.Wifi is not null && RadioService.IsOn(_radios.Wifi);
        else on = _radios.Bluetooth is not null && RadioService.IsOn(_radios.Bluetooth);

        SelectorRadioSwitch.IsChecked = on;
        SelectorGlyphCircle.Fill = Brush(on ? "#8AB4FF" : "#1FFFFFFF");
        SelectorGlyph.Foreground = Brush(on ? "#0D1B2A" : "#8E8E93");
        SelectorRadioName.Foreground = Brush("#F5F5F7");
        SelectorRadioState.Foreground = Brush(on ? "#8AB4FF" : "#6E6E73");
    }

    private async void ShowSelector(SelectorMode mode)
    {
        _selector = mode;

        // hide the normal card content
        MusicCard.Visibility = Visibility.Collapsed;
        ClipPanel.Visibility = Visibility.Collapsed;
        ControlsPanel.Visibility = Visibility.Collapsed;
        IconBarCentered.Visibility = Visibility.Collapsed;
        IconSettings.Visibility = Visibility.Collapsed;

        SelectorPanel.Visibility = Visibility.Visible;
        SelectorTitle.Text = mode == SelectorMode.Wifi ? "Wi-Fi" : "Bluetooth";
        SelectorRefreshButton.Visibility = mode == SelectorMode.Wifi ? Visibility.Visible : Visibility.Collapsed;
        SelectorSettingsLink.Content = mode == SelectorMode.Wifi ? "Network Settings →" : "Connected Devices →";
        SelectorStatus.Visibility = Visibility.Collapsed;

        // glyph per mode
        if (mode == SelectorMode.Wifi)
        {
            SelectorGlyph.Text = "\U0001F4F6";
            SelectorGlyph.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji");
            SelectorGlyph.FontSize = 22;
        }
        else
        {
            SelectorGlyph.Text = "BT";
            SelectorGlyph.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Semibold");
            SelectorGlyph.FontSize = 18;
        }
        SelectorRadioName.Text = mode == SelectorMode.Wifi ? "Wi-Fi" : "Bluetooth";

        await RefreshControlsAsync();
        SetSelectorRadioSwitchState();

        AnimateWidthHeight(_settings.ExpandedWidth, SelectorWifiHeight);
        await PopulateSelectorAsync();
    }

    private void HideSelector()
    {
        _selector = SelectorMode.None;
        SelectorPanel.Visibility = Visibility.Collapsed;

        // restore tab strip + hint
        IconBarCentered.Visibility = Visibility.Visible;
        IconSettings.Visibility = Visibility.Visible;

        // reapply current tab content
        SetTab(_tab);
    }

    private async Task PopulateSelectorAsync()
    {
        SelectorList.ItemsSource = null;
        var rows = new List<SelectorRowViewModel>();

        if (_selector == SelectorMode.Wifi)
        {
            foreach (var net in WlanService.Networks())
            {
                rows.Add(new SelectorRowViewModel
                {
                    Glyph = "\U0001F4F6", // 📶
                    Title = net.Ssid,
                    Subtitle = net.Connected ? "Connected"
                        : net.HasProfile ? "Saved network"
                        : net.SecurityEnabled ? "Secured — needs password"
                        : "Open network",
                    Right = net.Connected ? "✓" : $"{net.SignalQuality}%",
                    RightBrush = net.Connected
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0xB4, 0xFF))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8E, 0x8E, 0x93)),
                    Secured = net.SecurityEnabled,
                    Connected = net.Connected,
                    HasProfile = net.HasProfile,
                    Payload = net.Ssid,
                });
            }
            SetSelectorEmpty(rows.Count == 0 ? "No networks visible right now. Refresh, or check if Wi-Fi is on." : "");
        }
        else if (_selector == SelectorMode.Bluetooth)
        {
            foreach (var dev in await BluetoothService.PairedAsync())
            {
                rows.Add(new SelectorRowViewModel
                {
                    Glyph = "\U0001F433", // placeholder; refined below by name
                    Title = dev.Name,
                    Subtitle = dev.Kind == "BLE" ? "Bluetooth Low Energy" : "Classic Bluetooth",
                    Right = "",
                    Payload = dev.Id,
                });
            }
            foreach (var row in rows)
            {
                var n = row.Title.ToLowerInvariant();
                row.Glyph =
                    n.Contains("iphone") || n.Contains("phone") || n.Contains("android") ? "\U0001F4F1" :
                    n.Contains("buds") || n.Contains("headphone") || n.Contains("airpod") || n.Contains("sound") ? "\U0001F3A7" :
                    n.Contains("keyboard") ? "\U0001F232" :
                    n.Contains("mouse") || n.Contains("trackpad") ? "\U0001F5B1" :
                    n.Contains("watch") ? "⌚" :
                    n.Contains("speaker") ? "\U0001F50A" :
                    "\U0001F4E5";
            }
            SetSelectorEmpty(rows.Count == 0 ? "No paired devices. Pair one in Windows Settings." : "");
        }

        SelectorList.ItemsSource = rows;
    }

    private void SetSelectorEmpty(string message)
    {
        SelectorEmpty.Text = message;
        SelectorEmpty.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowSelectorStatus(string message)
    {
        SelectorStatus.Text = message;
        SelectorStatus.Visibility = Visibility.Visible;
    }

    private void SelectorBack_Click(object sender, RoutedEventArgs e) => HideSelector();

    private async void SelectorRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_selector == SelectorMode.Wifi) WlanService.Scan();
        await PopulateSelectorAsync();
    }

    private void SelectorOpenSettings_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start("explorer", _selector == SelectorMode.Wifi ? "ms-settings:network-wifi" : "ms-settings:connecteddevices");

    private void SelectorList_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // handled per-row; kept to satisfy XAML wiring
    }

    private async void SelectorRow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SelectorRowViewModel row)
        {
            if (_selector == SelectorMode.Wifi)
            {
                if (row.Connected)
                {
                    WlanService.Disconnect();
                    ShowSelectorStatus($"Disconnected from {row.Title}");
                }
                else if (row.HasProfile || !row.Secured)
                {
                    if (!row.HasProfile && !row.Secured)
                    {
                        // open network, no profile — install a temporary open profile then connect
                        if (WlanService.EnsureOpenProfileAndConnect(row.Title))
                        {
                            ShowSelectorStatus($"Connecting to {row.Title}…");
                        }
                        else
                        {
                            ShowSelectorStatus($"Couldn't join {row.Title}");
                        }
                    }
                    else
                    {
                        if (WlanService.Connect(row.Title))
                            ShowSelectorStatus($"Connecting to {row.Title}…");
                        else
                            ShowSelectorStatus($"Couldn't join {row.Title}");
                    }
                }
                else
                {
                    ShowSelectorStatus($"{row.Title} needs a password — add it in Settings.");
                    System.Diagnostics.Process.Start("explorer", "ms-settings:network-wifi");
                }

                await Task.Delay(1200);
                await PopulateSelectorAsync();
            }
            else if (_selector == SelectorMode.Bluetooth)
            {
                System.Diagnostics.Process.Start("explorer", "ms-settings:connecteddevices");
            }
        }
    }

    // ---------- tray ----------

    private void InitTray()
    {
        var icon = new BitmapImage();
        icon.BeginInit();
        icon.UriSource = new Uri("pack://application:,,,/Assets/minepix.png", UriKind.Absolute);
        icon.CacheOption = BitmapCacheOption.OnLoad;
        icon.EndInit();

        var menu = new ContextMenu();

        var toggleItem = new MenuItem { Header = "Show / hide island" };
        toggleItem.Click += (_, _) => ToggleIsland();

        var settingsItem = new MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) =>
        {
            RefreshFromSettings();
            var win = new SettingsWindow(_settings) { Owner = this };
            win.Show();
        };

        var exitItem = new MenuItem { Header = "Exit MinePix" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(toggleItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon
        {
            ToolTipText = "MinePix — your top edge, doing things",
            IconSource = icon,
            ContextMenu = menu,
        };
        _tray.TrayMouseDoubleClick += (_, _) => ToggleIsland();
    }

    private void ToggleIsland()
    {
        _islandHidden = !_islandHidden;
        Visibility = _islandHidden ? Visibility.Hidden : Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        MusicService.Instance.Changed -= OnMusicChanged;
        ClipboardService.Instance.ItemsChanged -= RefreshClipboardList;
        _tray?.Dispose();
        base.OnClosed(e);
    }
}
