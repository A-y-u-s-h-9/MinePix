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

        Width = _settings.CollapsedWidth;
        Height = _settings.CollapsedHeight;
        Top = 0;
        Left = CenteredLeft(Width);

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_settings.CollapseDelayMs) };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); Collapse(); };

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

        // dev affordance: force the expanded card open (used for headless screenshots/tests)
        if (Environment.GetEnvironmentVariable("MINEPIX_FORCE_EXPANDED") == "1")
            Dispatcher.BeginInvoke(new Action(Expand), DispatcherPriority.Loaded);

        if (Environment.GetEnvironmentVariable("MINEPIX_FORCE_TAB") is string forceTab && (forceTab == "clipboard" || forceTab == "controls"))
            Dispatcher.BeginInvoke(new Action(() => { Expand(); SetTab(forceTab); }), DispatcherPriority.Loaded);
    }

    private static double CenteredLeft(double width) => (SystemParameters.PrimaryScreenWidth - width) / 2;

    /// <summary>
    /// Applies the configured theme: the island tint. The DWM blur/acrylic APIs were probed and
    /// rejected on this stack — they force the layered WPF window to composite opaque.
    /// Pure per-pixel alpha gives the cleanest true-glass look; tint is kept low.
    /// </summary>
    private void ApplyTheme()
    {
        var border = _settings.Theme?.ToLowerInvariant() switch
        {
            "glass" => "#8026313D", // glass — bluish, ~50% alpha, readable over bright scenes
            "black" => "#B0100F11", // black — mostly opaque, still a hint of the backdrop
            _ => "#661F1F25",       // graphite — ~40% alpha, still glassy, rows stay readable
        };

        Island.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
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

        AnimateWidthHeight(_settings.ExpandedWidth, _activeExpandedHeight);

        AnimateOpacity(PillView, 0, 120, () => PillView.Visibility = Visibility.Collapsed);
        ExpandedView.Visibility = Visibility.Visible;
        AnimateOpacity(ExpandedView, 1, 200);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;

        AnimateWidthHeight(_settings.CollapsedWidth, _settings.CollapsedHeight);

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
        bool has = MusicService.Instance.HasSession;

        MusicCard.Visibility = _tab == "music" && has ? Visibility.Visible : Visibility.Collapsed;
        if (_tab == "music")
            SourceChip.Text = has && MusicService.Instance.SourceApp.Length > 0 ? MusicService.Instance.SourceApp : "MinePix";

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

        UpdatePillMusicState();
        UpdateProgressTick();
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

        TabMusicButton.IsChecked = tab == "music";
        TabClipboardButton.IsChecked = tab == "clipboard";
        TabControlsButton.IsChecked = tab == "controls";

        MusicCard.Visibility = tab == "music" && MusicService.Instance.HasSession ? Visibility.Visible : Visibility.Collapsed;
        ClipPanel.Visibility = tab == "clipboard" ? Visibility.Visible : Visibility.Collapsed;
        ControlsPanel.Visibility = tab == "controls" ? Visibility.Visible : Visibility.Collapsed;

        switch (tab)
        {
            case "clipboard":
                SourceChip.Text = "local only";
                _activeExpandedHeight = 330;
                RefreshClipboardList();
                break;
            case "controls":
                SourceChip.Text = "quick settings";
                _activeExpandedHeight = 330;
                _ = RefreshControlsAsync();
                break;
            default:
                SourceChip.Text = MusicService.Instance.HasSession && MusicService.Instance.SourceApp.Length > 0 ? MusicService.Instance.SourceApp : "MinePix";
                _activeExpandedHeight = _settings.ExpandedHeight;
                break;
        }
        AnimateWidthHeight(_settings.ExpandedWidth, _activeExpandedHeight);
    }

    private void TabMusic_Click(object sender, RoutedEventArgs e) => SetTab("music");
    private void TabClipboard_Click(object sender, RoutedEventArgs e) => SetTab("clipboard");
    private void TabControls_Click(object sender, RoutedEventArgs e) => SetTab("controls");

    // ---------- controls ----------

    private double SliderFraction(Border track, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(track);
        return track.ActualWidth <= 0 ? 0 : Math.Clamp(p.X / track.ActualWidth, 0, 1);
    }

    private async Task RefreshControlsAsync()
    {
        try
        {
            // volume
            float vol = CoreAudio.GetVolume();
            bool muted = CoreAudio.GetMuted();
            if (!_volumeDragging && VolumeTrack.ActualWidth > 0)
                UpdateVolume(vol, muted, VolumeTrack.ActualWidth);

            // brightness
            int? brightness = BrightnessService.GetBrightness();
            if (brightness is null)
            {
                BrightnessPercent.Text = "--%";
                BrightnessHint.Visibility = Visibility.Visible;
                BrightnessTrack.IsEnabled = false;
                BrightnessTrack.Opacity = 0.4;
                BrightnessFill.Width = 0;
                BrightnessThumb.Visibility = Visibility.Collapsed;
            }
            else
            {
                BrightnessHint.Visibility = Visibility.Collapsed;
                BrightnessTrack.IsEnabled = true;
                BrightnessTrack.Opacity = 1;
                BrightnessThumb.Visibility = Visibility.Visible;
                if (!_brightnessDragging && BrightnessTrack.ActualWidth > 0)
                    UpdateBrightness(brightness.Value / 100.0, BrightnessTrack.ActualWidth);
                BrightnessPercent.Text = $"{brightness.Value}%";
            }

            // radios
            _radios = await RadioService.GetRadiosAsync();
            WifiToggle.IsEnabled = _radios.Wifi is not null;
            BluetoothToggle.IsEnabled = _radios.Bluetooth is not null;
            WifiToggle.Opacity = _radios.Wifi is not null ? 1 : 0.4;
            BluetoothToggle.Opacity = _radios.Bluetooth is not null ? 1 : 0.4;
            WifiToggle.IsChecked = _radios.Wifi is not null && RadioService.IsOn(_radios.Wifi);
            BluetoothToggle.IsChecked = _radios.Bluetooth is not null && RadioService.IsOn(_radios.Bluetooth);
        }
        catch
        {
            // controls are best-effort; the panel still renders
        }
    }

    private void UpdateVolume(float vol, bool muted, double trackWidth)
    {
        VolumeFill.Width = vol * trackWidth;
        VolumeThumb.Margin = new Thickness(vol * trackWidth - 9, 0, 0, 0);
        VolumePercent.Text = muted ? "Muted" : $"{(int)Math.Round(vol * 100)}%";
        MuteButton.Content = muted ? "\U0001F507" : (vol < 0.5f ? "\U0001F509" : "\U0001F50A");
    }

    private void UpdateBrightness(double fraction, double trackWidth)
    {
        BrightnessFill.Width = fraction * trackWidth;
        BrightnessThumb.Margin = new Thickness(fraction * trackWidth - 9, 0, 0, 0);
    }

    private void ApplyVolumeFromEvent(System.Windows.Input.MouseEventArgs e)
    {
        if (VolumeTrack.ActualWidth <= 0) return;
        double fraction = SliderFraction(VolumeTrack, e);
        CoreAudio.SetVolume((float)fraction);
        bool muted = CoreAudio.GetMuted() && fraction == 0;
        if (fraction > 0) { CoreAudio.SetMuted(false); muted = false; }
        UpdateVolume((float)fraction, CoreAudio.GetMuted(), VolumeTrack.ActualWidth);
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
        if (BrightnessTrack.ActualWidth <= 0) return;
        double fraction = SliderFraction(BrightnessTrack, e);
        int level = (int)Math.Round(fraction * 100);
        if (DateTime.Now - _lastBrightnessApply > TimeSpan.FromMilliseconds(150) || level % 5 == 0)
        {
            BrightnessService.SetBrightness(level);
            _lastBrightnessApply = DateTime.Now;
        }
        UpdateBrightness(level / 100.0, BrightnessTrack.ActualWidth);
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

    private async void WifiToggle_Click(object sender, RoutedEventArgs e)
    {
        bool want = WifiToggle.IsChecked == true;
        await RadioService.SetAsync(_radios.Wifi, want);
        await Task.Delay(350);
        await RefreshControlsAsync();
    }

    private async void BluetoothToggle_Click(object sender, RoutedEventArgs e)
    {
        bool want = BluetoothToggle.IsChecked == true;
        await RadioService.SetAsync(_radios.Bluetooth, want);
        await Task.Delay(350);
        await RefreshControlsAsync();
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

        var settingsItem = new MenuItem { Header = "Open settings folder" };
        settingsItem.Click += (_, _) =>
        {
            System.IO.Directory.CreateDirectory(Settings.SettingsDir);
            System.Diagnostics.Process.Start("explorer.exe", Settings.SettingsDir);
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
