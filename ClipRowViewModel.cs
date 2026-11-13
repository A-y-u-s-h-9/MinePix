namespace MinePix;

/// <summary>Row view-model for the clipboard list in the island.</summary>
public sealed class ClipRowViewModel
{
    public long Id { get; set; }
    public string Glyph { get; set; } = "T";
    public string Preview { get; set; } = "";
    public string MetaLine { get; set; } = "";
    public bool Pinned { get; set; }
}
