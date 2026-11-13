namespace MinePix;

/// <summary>Mode for the selector sheet; None = normal tab view.</summary>
public enum SelectorMode { None, Wifi, Bluetooth }

/// <summary>Row view-model for the Wi-Fi/Bluetooth selector list.</summary>
public sealed class SelectorRowViewModel
{
    public long Id { get; set; }          // stable row id for clicks
    public string Glyph { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Right { get; set; } = "";   // e.g. "37%" or "✓"
    public System.Windows.Media.Brush RightBrush { get; set; } = System.Windows.Media.Brushes.Gray;
    public bool Secured { get; set; }
    public bool Connected { get; set; }
    public bool HasProfile { get; set; }
    public string Payload { get; set; } = "";
}
