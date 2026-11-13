using System.Collections;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MinePix;

/// <summary>
/// Watches the Windows clipboard. Captures text, files and images into the local SQLite store,
/// skipping password-manager exclusions and re-capturing copies made back through MinePix.
/// </summary>
public sealed class ClipboardService
{
    public static ClipboardService Instance { get; } = new();

    public event Action? ItemsChanged;

    private const int WmClipboardUpdate = 0x031D;
    private const long MaxTextBytes = 512 * 1024;   // 512 KB of text is already absurd for a clipboard
    private const long MaxImageBytes = 10 * 1024 * 1024;

    private static readonly string[] ExclusionMarkers =
    {
        "excludeclipboardcontent", "exclude_clipboard", "viewer ignore",
        "canincludeinclipboardhistory", "password"
    };

    private HwndSource? _source;
    private string? _lastCapturedHash;
    private bool _ignoreNextCapture;

    public void Start()
    {
        if (_source is not null) return;
        try
        {
            var parameters = new HwndSourceParameters("MinePixClipboardListener")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x00080000 | 0x08000000, // WS_EX_LAYERED | WS_EX_NOACTIVATE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            AddClipboardFormatListener(_source.Handle);

            ClipboardStore.EnsureSchema();
            ClipboardStore.Prune();
        }
        catch
        {
            // clipboard watch is best-effort; island works fine without it
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            Capture();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Call right before MinePix sets the clipboard itself, so it doesn't re-capture.</summary>
    public void SuppressNextCapture() => _ignoreNextCapture = true;

    public void Capture()
    {
        if (_ignoreNextCapture)
        {
            _ignoreNextCapture = false;
            return;
        }

        IDataObject? data;
        try { data = Clipboard.GetDataObject(); }
        catch { return; }
        if (data is null) return;

        // password managers opt out — respect that
        try
        {
            foreach (var format in data.GetFormats())
            {
                var lower = format.ToLowerInvariant();
                foreach (var marker in ExclusionMarkers)
                    if (lower.Contains(marker)) return;
            }
        }
        catch { /* formats unreadable — proceed with best effort */ }

        try
        {
            // 1) text
            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                var text = data.GetData(DataFormats.UnicodeText) as string;
                if (string.IsNullOrEmpty(text)) return;
                if (Encoding.UTF8.GetByteCount(text) > MaxTextBytes) return;
                var hash = ClipboardStore.HashText(text);
                if (hash == _lastCapturedHash) return;
                _lastCapturedHash = hash;
                ClipboardStore.InsertOrTouch(ClipboardKind.Text, text, null, hash);
                RaiseItemsChanged();
                return;
            }

            // 2) files
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (data.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>();
                if (files.Length == 0) return;
                if (files.Length > 50) files = files.Take(50).ToArray();
                var json = JsonSerializer.Serialize(files);
                var hash = ClipboardStore.HashFiles(files);
                if (hash == _lastCapturedHash) return;
                _lastCapturedHash = hash;
                ClipboardStore.InsertOrTouch(ClipboardKind.Files, json, null, hash);
                RaiseItemsChanged();
                return;
            }

            // 3) image (CF_DIB via Clipboard.GetImage)
            if (data.GetDataPresent(DataFormats.Bitmap))
            {
                var bitmap = Clipboard.GetImage();
                if (bitmap is null) return;
                var pngBytes = EncodePng(ScaleToMaxSide(bitmap, 1920));
                if (pngBytes is null || pngBytes.Length > MaxImageBytes) return;

                var hash = ClipboardStore.HashBytes(pngBytes);
                if (hash == _lastCapturedHash) return;
                _lastCapturedHash = hash;

                Directory.CreateDirectory(ClipboardStore.CacheDir);
                var path = Path.Combine(ClipboardStore.CacheDir, hash.Substring(0, 16) + ".png");
                File.WriteAllBytes(path, pngBytes);
                ClipboardStore.InsertOrTouch(ClipboardKind.Image, path, path, hash);
                RaiseItemsChanged();
            }
        }
        catch { /* never let a clipboard capture crash the island */ }
    }

    private static BitmapSource ScaleToMaxSide(BitmapSource source, int maxSide)
    {
        if (source.PixelWidth <= maxSide && source.PixelHeight <= maxSide) return source;
        double scale = (double)maxSide / Math.Max(source.PixelWidth, source.PixelHeight);
        var scaled = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private static byte[]? EncodePng(BitmapSource source)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Copy an entry back to the clipboard so the user can paste it elsewhere.</summary>
    public static void CopyBack(ClipboardItem item)
    {
        var svc = Instance;
        svc.SuppressNextCapture();
        try
        {
            switch (item.Kind)
            {
                case "text":
                    Clipboard.SetText(item.Content);
                    break;
                case "files":
                    var files = JsonSerializer.Deserialize<string[]>(item.Content) ?? Array.Empty<string>();
                    var sc = new StringCollection();
                    sc.AddRange(files);
                    Clipboard.SetFileDropList(sc);
                    break;
                case "image":
                    if (File.Exists(item.Content))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(item.Content);
                        bmp.EndInit();
                        bmp.Freeze();
                        Clipboard.SetImage(bmp);
                    }
                    break;
            }
        }
        catch { }
    }

    /// <summary>Human-friendly relative age for the clipboard list.</summary>
    public static string RelativeTime(long unixSeconds)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
        var span = DateTime.Now - dt;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    private void RaiseItemsChanged()
    {
        try { Application.Current?.Dispatcher.Invoke(() => ItemsChanged?.Invoke()); } catch { }
    }
}
