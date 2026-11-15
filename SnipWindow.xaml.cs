using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MinePix;

/// <summary>
/// Full-screen region picker. Drag a rectangle, then Enter (or release) captures it.
/// Escape cancels. The capture is taken in physical pixels.
/// </summary>
public partial class SnipWindow : Window
{
    private Point _dragStart;
    private bool _dragging;
    private Rect _selection;

    /// <summary>Raised with the captured region in physical pixels (x, y, w, h).</summary>
    public event Action<int, int, int, int>? RegionCaptured;

    public SnipWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var s = ScreenCaptureService.VirtualScreen();
            double scale = DpiScale();
            // VirtualScreen returns physical pixels; WPF Left/Top are DIPs
            Left   = s.X / scale;
            Top    = s.Y / scale;
            Width  = s.Width  / scale;
            Height = s.Height / scale;
            UpdateDim();
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragging = true;
        CaptureMouse();
        SelectionBox.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        UpdateSelection(_dragStart, _dragStart);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateSelection(_dragStart, e.GetPosition(this));
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        UpdateSelection(_dragStart, e.GetPosition(this));

        if (_selection.Width > 4 && _selection.Height > 4)
            Finish();
        else
            Cancel();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_selection.Width > 4 && _selection.Height > 4)
                Finish();
            e.Handled = true;
        }
    }

    private void Finish()
    {
        var s = ScreenCaptureService.VirtualScreen();
        double scale = DpiScale();

        // _selection is in DIPs (WPF coordinates relative to window top-left)
        // window top-left in DIPs = s.X/scale, s.Y/scale
        // physical pixel coords = DIP * scale
        int x = (int)Math.Round(s.X + _selection.X * scale);
        int y = (int)Math.Round(s.Y + _selection.Y * scale);
        int w = (int)Math.Round(_selection.Width  * scale);
        int h = (int)Math.Round(_selection.Height * scale);

        Close();
        RegionCaptured?.Invoke(x, y, w, h);
    }

    private void Cancel() => Close();

    private void UpdateSelection(Point a, Point b)
    {
        _selection = new Rect(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        SelectionBox.Margin = new Thickness(_selection.X, _selection.Y, 0, 0);
        SelectionBox.Width = _selection.Width;
        SelectionBox.Height = _selection.Height;

        SizeText.Text = $"{Math.Round(_selection.Width)} × {Math.Round(_selection.Height)}";
        SizeBadge.Margin = new Thickness(_selection.X, Math.Max(4, _selection.Y - 30), 0, 0);
    }

    private void UpdateDim()
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var hole = new RectangleGeometry(_selection);
        DimPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            UpdateSelection(_dragStart, e.GetPosition(this));
            UpdateDim();
        }
    }

    private double DpiScale()
    {
        try
        {
            var src = PresentationSource.FromVisual(this);
            return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
        catch
        {
            return 1.0;
        }
    }
}
