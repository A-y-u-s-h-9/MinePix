using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace MinePix;

public enum IslandPage { Home, Music, Clipboard, Controls, Actions, Bell, Chat, Folder, Apps, Globe, Settings, Bluetooth }

public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _mediaTimer;
    private readonly DispatcherTimer _controlsTimer;
    private TaskbarIcon? _tray;
    private IslandPage _page = IslandPage.Home;
    private bool _expanded;
    private bool _islandHidden;
    private bool _seeking;
    private bool _volumeDragging;
    private bool _brightnessDragging;

    public MainWindow()
    {
        _settings = Settings.Load();

        InitializeComponent();
        ApplyTheme();

        Width = _settings.CollapsedWidth;
        Height = _settings.CollapsedHeight;
        Top = 0;
        Left = CenteredLeft(Width);

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_settings.CollapseDelayMs) };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); Collapse(); };

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();

        // keep collapsed width centered on resize
        SizeChanged += (_, _) => Left = CenteredLeft(ActualWidth);

        // rich page timers
        MusicService.Instance.Changed += OnMusicChanged;
        _ = MusicService.Instance.InitializeAsync(Dispatcher);
        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _mediaTimer.Tick += (_, _) => UpdateProgressTick();
        _mediaTimer.Start();

        ClipboardStore.MaxItems = _settings.ClipboardMaxItems;
        if (_settings.ClipboardEnabled)
            ClipboardService.Instance.Start();
        ClipboardService.Instance.ItemsChanged += RefreshClipboardList;

        _controlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _controlsTimer.Tick += (_, _) =>
        {
            if (_page == IslandPage.Controls)
                _ = RefreshControlsAsync();
        };
        _controlsTimer.Start();

        InitTray();

        // debug affordance for headless screenshots
        var forceExpand = Environment.GetEnvironmentVariable("MINEPIX_FORCE_EXPANDED");
        if (forceExpand == "1")
            Dispatcher.BeginInvoke(new Action(Expand), DispatcherPriority.Loaded);

        var forceTab = Environment.GetEnvironmentVariable("MINEPIX_FORCE_TAB");
        if (!string.IsNullOrEmpty(forceTab) && Enum.TryParse<IslandPage>(forceTab, true, out var forced))
            Dispatcher.BeginInvoke(new Action(() => { Expand(); ShowPage(forced); }), DispatcherPriority.Loaded);
    }

    private void ApplyTheme()
    {
        var border = _settings.Theme?.ToLowerInvariant() switch
        {
            "glass" => "#3D2B3344",   // glass — airy, bluish, bright backdrops show through
            "black" => "#F2100F11",   // black — near-solid
            _ => "#45222429",          // graphite — liquid glass, ~27% alpha, warm tint
        };
        Island.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
    }

    private static double CenteredLeft(double width) => (SystemParameters.PrimaryScreenWidth - width) / 2;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        HomeClock.Text = now.ToString("h:mm");
        HomeDay.Text = now.ToString("ddd").ToUpperInvariant();
        HomeTokyoClock.Text = "Tokyo " + TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Tokyo Standard Time").ToString("h:mm tt");
    }

    // ---------- pages / navigation ----------

    private void ShowPage(IslandPage page)
    {
        _page = page;

        PageHome.Visibility        = page == IslandPage.Home ? Visibility.Visible : Visibility.Collapsed;
        PageMusic.Visibility       = page == IslandPage.Music ? Visibility.Visible : Visibility.Collapsed;
        PageClipboard.Visibility   = page == IslandPage.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        PageControls.Visibility    = page == IslandPage.Controls ? Visibility.Visible : Visibility.Collapsed;
        PageActions.Visibility     = page == IslandPage.Actions ? Visibility.Visible : Visibility.Collapsed;
        PageBell.Visibility        = page == IslandPage.Bell ? Visibility.Visible : Visibility.Collapsed;
        PageChat.Visibility        = page == IslandPage.Chat ? Visibility.Visible : Visibility.Collapsed;
        PageFolder.Visibility      = page == IslandPage.Folder ? Visibility.Visible : Visibility.Collapsed;
        PageApps.Visibility        = page == IslandPage.Apps ? Visibility.Visible : Visibility.Collapsed;
        PageGlobe.Visibility       = page == IslandPage.Globe ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility    = page == IslandPage.Settings ? Visibility.Visible : Visibility.Collapsed;

        // highlight the active icon
        var map = new Dictionary<IslandPage, Button>
        {
            [IslandPage.Music]     = IconMusic,
            [IslandPage.Globe]     = IconGlobe,
            [IslandPage.Bell]      = IconBell,
            [IslandPage.Chat]      = IconChat,
            [IslandPage.Folder]    = IconFolder,
            [IslandPage.Clipboard] = IconClipboard,
            [IslandPage.Apps]      = IconGrid,
            [IslandPage.Controls]  = IconSliders,
            [IslandPage.Bluetooth] = IconHeadphones,
            [IslandPage.Actions]   = IconActions,
            [IslandPage.Settings]  = IconSettings,
        };
        foreach (var kv in map)
        {
            kv.Value.Style = kv.Key == page
                ? (Style)FindResource("IconButtonActive")
                : (Style)FindResource("IconButton");
        }
        if (page == IslandPage.Controls)
            _ = RefreshControlsAsync();
        if (page == IslandPage.Clipboard)
            RefreshClipboardList();
    }

    private void IconMusic_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Music);
    private void IconGlobe_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Globe);
    private void IconBell_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Bell);
    private void IconChat_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Chat);
    private void IconFolder_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Folder);
    private void IconClipboard_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Clipboard);
    private void IconGrid_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Apps);
    private void IconSliders_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Controls);
    private void IconHeadphones_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Bluetooth);
    private void IconActions_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Actions);
    private void IconSettings_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Settings);

    private void HomeBellFloating_Click(object sender, RoutedEventArgs e) => ShowPage(IslandPage.Bell);

    // ---------- expand / collapse ----------

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        Expand();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_seeking || _volumeDragging || _brightnessDragging)
            return;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;

        Width = _settings.ExpandedWidth;
        Height = _settings.ExpandedHeight;
        Left = CenteredLeft(Width);

        AnimateOpacity(PillView, 0, 120, () => PillView.Visibility = Visibility.Collapsed);
        ExpandedView.Visibility = Visibility.Visible;
        AnimateOpacity(ExpandedView, 1, 180);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;

        AnimateOpacity(ExpandedView, 0, 120, () => ExpandedView.Visibility = Visibility.Collapsed);
        PillView.Visibility = Visibility.Visible;
        AnimateOpacity(PillView, 1, 200);

        Width = _settings.CollapsedWidth;
        Height = _settings.CollapsedHeight;
        Left = CenteredLeft(Width);
    }

    private void AnimateOpacity(UIElement element, double to, int durationMs, Action? onCompleted = null)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var animation = new DoubleAnimation(to, duration);
        animation.Completed += (_, _) => onCompleted?.Invoke();
        element.BeginAnimation(OpacityProperty, animation);
    }

    // ---------- music ----------

    private void OnMusicChanged()
    {
        bool has = MusicService.Instance.HasSession;
        if (_page == IslandPage.Music)
        {
            if (!has)
            {
                MusicTitle.Text = "Nothing playing";
                MusicArtist.Text = "Start something in Spotify, YouTube, or any app";
                MusicArt.Visibility = Visibility.Collapsed;
                MusicArtFallback.Visibility = Visibility.Visible;
                MusicPosition.Text = "0:00";
                MusicDuration.Text = "0:00";
                SeekFill.Width = 0;
                MusicPlayPauseButton.Content = "▶";
            }
            else
            {
                MusicTitle.Text = MusicService.Instance.Title;
                MusicArtist.Text = MusicService.Instance.Artist;
                MusicDuration.Text = MusicService.FormatTime(MusicService.Instance.Duration);
                MusicPlayPauseButton.Content = MusicService.Instance.IsPlaying ? "⏸" : "▶";
                if (MusicService.Instance.Artwork is not null)
                {
                    MusicArt.Source = MusicService.Instance.Artwork;
                    MusicArt.Visibility = Visibility.Visible;
                    MusicArtFallback.Visibility = Visibility.Collapsed;
                }
                else
                {
                    MusicArt.Visibility = Visibility.Collapsed;
                    MusicArtFallback.Visibility = Visibility.Visible;
                }
            }
        }
        UpdateProgressTick();
    }

    private void UpdateProgressTick()
    {
        if (!MusicService.Instance.HasSession) return;
        var duration = MusicService.Instance.Duration;
        var position = MusicService.Instance.DisplayPosition;
        if (duration.TotalMilliseconds <= 0) return;

        MusicPosition.Text = MusicService.FormatTime(position);
        MusicDuration.Text = MusicService.FormatTime(duration);
        double fraction = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        SeekFill.Width = fraction * (SeekTrack.ActualWidth > 0 ? SeekTrack.ActualWidth : 200);
    }

    private double SeekFractionFromEvent(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(SeekTrack);
        return SeekTrack.ActualWidth <= 0 ? 0 : Math.Clamp(p.X / SeekTrack.ActualWidth, 0, 1);
    }

    private void SeekTrack_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _seeking = true;
        SeekTrack.CaptureMouse();
        ApplySeek(e);
        e.Handled = true;
    }
    private void SeekTrack_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_seeking) ApplySeek(e);
    }
    private async void SeekTrack_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        SeekTrack.ReleaseMouseCapture();
        await MusicService.Instance.SeekAsync(SeekFractionFromEvent(e));
    }
    private void ApplySeek(System.Windows.Input.MouseEventArgs e)
    {
        double fraction = SeekFractionFromEvent(e);
        SeekFill.Width = fraction * SeekTrack.ActualWidth;
        MusicPosition.Text = MusicService.FormatTime(TimeSpan.FromMilliseconds(fraction * MusicService.Instance.Duration.TotalMilliseconds));
    }

    private void MusicPrevButton_Click(object sender, RoutedEventArgs e) => _ = MusicService.Instance.SkipPreviousAsync();
    private void MusicPlayPauseButton_Click(object sender, RoutedEventArgs e) => _ = MusicService.Instance.TogglePlayPauseAsync();
    private void MusicNextButton_Click(object sender, RoutedEventArgs e) => _ = MusicService.Instance.SkipNextAsync();

    // ---------- volume ----------

    private void ApplyVolumeFromEvent(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(VolumeTrack);
        double pos = VolumeTrack.ActualWidth <= 0 ? 0 : Math.Clamp(p.X / VolumeTrack.ActualWidth, 0, 1);
        CoreAudio.SetVolume((float)pos);
        if (pos > 0) CoreAudio.SetMuted(false);
        UpdateVolumeUI(pos);
    }

    private void UpdateVolumeUI(double pos)
    {
        float vol = CoreAudio.GetVolume();
        bool muted = CoreAudio.GetMuted();
        VolumeFill.Width = vol * VolumeTrack.ActualWidth;
        VolumePercent.Text = muted ? "Muted" : $"{(int)Math.Round(vol * 100)}%";
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

    // ---------- brightness ----------

    private void BrightnessTrack_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
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
        _brightnessDragging = false;
        BrightnessTrack.ReleaseMouseCapture();
        ApplyBrightnessFromEvent(e);
    }
    private void ApplyBrightnessFromEvent(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(BrightnessTrack);
        if (BrightnessTrack.ActualHeight <= 0) return;
        double pos = 1.0 - Math.Clamp(p.Y / BrightnessTrack.ActualHeight, 0, 1);
        int level = (int)Math.Round(pos * 100);
        BrightnessService.SetBrightness(level);
        UpdateBrightnessUI(level);
    }

    private void UpdateBrightnessUI(int level)
    {
        BrightnessFill.Height = (level / 100.0) * BrightnessTrack.ActualHeight;
        BrightnessPercent.Text = $"{level}%";
    }

    // ---------- controls ----------

    private async Task RefreshControlsAsync()
    {
        try
        {
            float vol = CoreAudio.GetVolume();
            bool muted = CoreAudio.GetMuted();
            VolumeFillVertical.Height = vol * (VolumeTrackVertical.ActualHeight > 0 ? VolumeTrackVertical.ActualHeight : 200);
            VolumePercent.Text = muted ? "Muted" : $"{(int)Math.Round(vol * 100)}%";
            MuteToggle.Content = muted ? "🔇" : (vol < 0.5f ? "🔉" : "🔊");
            WifiToggle.IsChecked = RadioService.IsOn((await RadioService.GetRadiosAsync()).Wifi);
            BluetoothToggle.IsChecked = RadioService.IsOn((await RadioService.GetRadiosAsync()).Bluetooth);
            var brightness = BrightnessService.GetBrightness();
            if (brightness is not null) UpdateBrightnessUI(brightness.Value);
        }
        catch { }
    }

    private void WifiToggle_Click(object sender, RoutedEventArgs e)
    {
        _ = ToggleRadioAsync(true);
    }
    private void BluetoothToggle_Click(object sender, RoutedEventArgs e)
    {
        _ = ToggleRadioAsync(false);
    }
    private void NightLightToggle_Click(object sender, RoutedEventArgs e)
    {
        // no public API — no-op placeholder
    }
    private void FocusToggle_Click(object sender, RoutedEventArgs e)
    {
        // no public API — no-op placeholder
    }

    private async Task ToggleRadioAsync(bool wifi)
    {
        var radios = await RadioService.GetRadiosAsync();
        var radio = wifi ? radios.Wifi : radios.Bluetooth;
        if (radio is null) return;
        bool want = !RadioService.IsOn(radio);
        await RadioService.SetAsync(radio, want);
        await Task.Delay(350);
        _ = RefreshControlsAsync();
    }

    private void MuteToggle_Click(object sender, RoutedEventArgs e)
    {
        CoreAudio.SetMuted(!CoreAudio.GetMuted());
        _ = RefreshControlsAsync();
    }

    private void VolumeTrackVertical_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _volumeDragging = true;
        VolumeTrackVertical.CaptureMouse();
        ApplyVolumeVertical(e);
        e.Handled = true;
    }
    private void VolumeTrackVertical_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_volumeDragging) ApplyVolumeVertical(e);
    }
    private void VolumeTrackVertical_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _volumeDragging = false;
        VolumeTrackVertical.ReleaseMouseCapture();
        ApplyVolumeVertical(e);
    }
    private void ApplyVolumeVertical(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(VolumeTrackVertical);
        if (VolumeTrackVertical.ActualHeight <= 0) return;
        double pos = 1.0 - Math.Clamp(p.Y / VolumeTrackVertical.ActualHeight, 0, 1);
        CoreAudio.SetVolume((float)pos);
        if (pos > 0) CoreAudio.SetMuted(false);
        float vol = CoreAudio.GetVolume();
        VolumeFillVertical.Height = vol * VolumeTrackVertical.ActualHeight;
        VolumePercent.Text = vol <= 0 ? "Muted" : $"{(int)Math.Round(vol * 100)}%";
    }

    // ---------- clipboard ----------

    private string _clipboardCategory = "all";

    private void ClipCatAll_Click(object sender, RoutedEventArgs e) { _clipboardCategory = "all"; SetCategoryStyle(ClipCatAll); RefreshClipboardList(); }
    private void ClipCatText_Click(object sender, RoutedEventArgs e) { _clipboardCategory = "text"; SetCategoryStyle(ClipCatText); RefreshClipboardList(); }
    private void ClipCatLinks_Click(object sender, RoutedEventArgs e) { _clipboardCategory = "links"; SetCategoryStyle(ClipCatLinks); RefreshClipboardList(); }
    private void ClipCatImages_Click(object sender, RoutedEventArgs e) { _clipboardCategory = "image"; SetCategoryStyle(ClipCatImages); RefreshClipboardList(); }
    private void ClipCatFiles_Click(object sender, RoutedEventArgs e) { _clipboardCategory = "files"; SetCategoryStyle(ClipCatFiles); RefreshClipboardList(); }
    private void ClipCatSnippets_Click(object sender, RoutedEventArgs e) { _clipboardCategory = "snippets"; SetCategoryStyle(ClipCatSnippets); RefreshClipboardList(); }

    private void SetCategoryStyle(Button active)
    {
        var chips = new[] { ClipCatAll, ClipCatText, ClipCatLinks, ClipCatImages, ClipCatFiles, ClipCatSnippets };
        foreach (var b in chips) b.Style = b == active ? (Style)FindResource("CategoryChipSelected") : (Style)FindResource("CategoryChip");
    }

    private void ClipClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        ClipboardStore.ClearAll();
        RefreshClipboardList();
    }

    private void RefreshClipboardList()
    {
        var search = _clipboardCategory == "all" ? "" : $"kind = '{_clipboardCategory}'";
        var items = ClipboardStore.GetByCategory(_clipboardCategory);
        ClipCardGrid.Children.Clear();
        if (items.Count == 0)
        {
            ClipEmpty.Visibility = Visibility.Visible;
            return;
        }
        ClipEmpty.Visibility = Visibility.Collapsed;
        foreach (var item in items) ClipCardGrid.Children.Add(BuildClipCard(item));
    }

    private Border BuildClipCard(ClipboardItem item)
    {
        // placeholder card body — replaced with rich card grid in next milestone phase
        var border = new Border
        {
            Width = 120,
            Height = 140,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(110, 31, 32, 38)),
            Padding = new Thickness(8),
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            ClipboardService.CopyBack(item);
        };

        var stack = new StackPanel();
        var header = new TextBlock
        {
            Text = item.Kind switch
            {
                "text" => "Text",
                "image" => "Image",
                "files" => "Files",
                _ => item.Kind,
            },
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var body = new TextBlock
        {
            Text = item.Preview,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(230, 245, 245, 247)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 70,
            TextWrapping = TextWrapping.Wrap,
        };
        var footer = new TextBlock
        {
            Text = item.Meta,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 200, 200, 205)),
            Margin = new Thickness(0, 6, 0, 0),
        };
        stack.Children.Add(header);
        stack.Children.Add(body);
        stack.Children.Add(footer);
        border.Child = stack;
        return border;
    }

    // ---------- actions ----------

    private void ActionScreenshot_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start("explorer", "ms-screenclip:");

    private void ActionSnip_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start("explorer", "ms-screenclip:SnippingTool");

    private void ActionRecord_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start("explorer", "ms-gamebar:");

    // ---------- settings ----------

    private void SettingsOpenStore_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Settings.SettingsDir);
        System.Diagnostics.Process.Start("explorer", Settings.SettingsDir);
    }

    private void SettingsOpenRepo_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start("explorer", "https://github.com/A-y-u-s-h-9/MinePix");

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
        settingsItem.Click += (_, _) => SettingsOpenStore_Click(this, new RoutedEventArgs());
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
