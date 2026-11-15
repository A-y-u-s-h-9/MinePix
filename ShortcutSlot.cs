using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MinePix;

/// <summary>A slot in the quick-actions panel — either an empty "+ Add" slot or a pinned app.</summary>
public sealed class ShortcutSlot : INotifyPropertyChanged
{
    public string? Path { get; set; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Path);

    public string Glyph { get; private set; } = "＋";
    public string Label { get; private set; } = "Add";
    public string ToolTip { get; private set; } = "Add a shortcut";
    public bool GlyphVisible { get; private set; }
    public string Foreground { get; private set; } = "#6E6E73";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ShortcutSlot(string? path, string label, string glyph)
    {
        Path = path;
        Label = label;
        Glyph = glyph;
        GlyphVisible = !string.IsNullOrEmpty(glyph);
        ToolTip = path ?? "Add a shortcut";
        Foreground = path is null ? "#6E6E73" : "#F5F5F7";
    }

    public void Refresh(string label, string glyph)
    {
        Label = label;
        Glyph = glyph;
        GlyphVisible = !string.IsNullOrEmpty(glyph);
        Foreground = Path is null ? "#6E6E73" : "#F5F5F7";
        ToolTip = Path ?? "Add a shortcut";
        Raise();
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
