using System.Windows;
using System.Windows.Controls;
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
    private TaskbarIcon? _tray;
    private bool _expanded;
    private bool _islandHidden;
    private bool _seeking;

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

        InitTray();

        // dev affordance: force the expanded card open (used for headless screenshots/tests)
        if (Environment.GetEnvironmentVariable("MINEPIX_FORCE_EXPANDED") == "1")
            Dispatcher.BeginInvoke(new Action(Expand), DispatcherPriority.Loaded);
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
            _ => "#571F1E24",       // graphite — ~34% alpha, real glass but text stays readable
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

        AnimateWidthHeight(_settings.ExpandedWidth, _settings.ExpandedHeight);

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

        MusicCard.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
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
        var position = MusicService.Instance.Position;
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
        _tray?.Dispose();
        base.OnClosed(e);
    }
}
