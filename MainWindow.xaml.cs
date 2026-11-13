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
/// Collapsed pill shows the clock; hovering expands it into a card.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _clockTimer;
    private TaskbarIcon? _tray;
    private bool _expanded;
    private bool _islandHidden;

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

        InitTray();
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
            "glass" => "#3D2B3344", // glass — bluish tint, ~24% alpha
            "black" => "#80100F11", // black — mostly opaque, still a hint of the backdrop
            _ => "#1E1E1D23",       // graphite — ~12% alpha, bright backdrops show through
        };

        Island.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        Expand();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;

        AnimateWidthHeight(_settings.ExpandedWidth, _settings.ExpandedHeight);

        // crossfade pill -> card
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
        _tray?.Dispose();
        base.OnClosed(e);
    }
}
