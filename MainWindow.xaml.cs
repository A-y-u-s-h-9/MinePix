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
    private const int BottomCornerRadius = 18;

    private readonly Settings _settings;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _clockTimer;
    private TaskbarIcon? _tray;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _expanded;
    private bool _islandHidden;

    public MainWindow()
    {
        InitializeComponent();

        _settings = Settings.Load();

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

        // keep the window centered while its width animates, and keep the notch region in sync
        SizeChanged += (_, _) =>
        {
            Left = CenteredLeft(ActualWidth);
            if (_hwnd != IntPtr.Zero)
                DwmBlur.ApplyNotchRegion(_hwnd, (int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), BottomCornerRadius);
        };

        InitTray();
    }

    private static double CenteredLeft(double width) => (SystemParameters.PrimaryScreenWidth - width) / 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ApplyTheme();
        DwmBlur.ApplyNotchRegion(_hwnd, (int)Width, (int)Height, BottomCornerRadius);
    }

    /// <summary>
    /// Applies the configured theme: WPF tint + acrylic strength.
    /// "glass" is the lightest tint; "black" is nearly opaque; "graphite" is the default.
    /// </summary>
    private void ApplyTheme()
    {
        var (border, alpha, r, g, b) = _settings.Theme?.ToLowerInvariant() switch
        {
            "glass" => ("#80292D38", (byte)0x90, (byte)0x1A, (byte)0x1E, (byte)0x28),
            "black" => ("#CC0D0D0F", (byte)0xC0, (byte)0x08, (byte)0x08, (byte)0x0A),
            _ => ("#B31E1D22", (byte)0xB0, (byte)0x1E, (byte)0x1D, (byte)0x22), // graphite
        };

        Island.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));

        if (_hwnd != IntPtr.Zero)
            DwmBlur.EnableAcrylic(_hwnd, alpha, r, g, b);
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
