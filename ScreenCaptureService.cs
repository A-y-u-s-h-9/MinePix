using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinePix;

/// <summary>
/// Native screen capture: full screen or a region, saved to Pictures\Screenshots and
/// copied to the clipboard. Pure Win32 (BitBlt) + WPF encoding — no extra dependencies.
/// </summary>
internal static class ScreenCaptureService
{
    private const int SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;

    // BITMAPINFOHEADER is exactly 40 bytes: biPlanes and biBitCount are WORDs, everything else
    // is DWORD/LONG. Declaring all eleven fields as uint made the struct 44 bytes, so biSize was
    // wrong and CreateDIBSection rejected the header (returning NULL) at every resolution.
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr bits, IntPtr section, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetSystemMetrics(int index);

    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public static string ScreenshotsFolder
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }

    /// <summary>Virtual desktop bounds in physical pixels.</summary>
    public static (int X, int Y, int Width, int Height) VirtualScreen() =>
        (GetSystemMetrics(SM_XVIRTUALSCREEN),
         GetSystemMetrics(SM_YVIRTUALSCREEN),
         GetSystemMetrics(SM_CXVIRTUALSCREEN),
         GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>Captures a region (physical pixels) and returns the saved PNG path, or null.</summary>
    public static string? CaptureRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        try
        {
            var bitmap = CaptureBitmap(x, y, width, height);
            if (bitmap is null)
                return null;

            string path = Path.Combine(
                ScreenshotsFolder, $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var fs = File.Create(path))
                encoder.Save(fs);

            TryCopyToClipboard(bitmap);
            return path;
        }
        catch (Exception ex)
        {
            LogFail("CaptureRegion", ex);
            return null;
        }
    }

    /// <summary>Captures the whole virtual desktop.</summary>
    public static string? CaptureFullScreen()
    {
        var s = VirtualScreen();
        return CaptureRegion(s.X, s.Y, s.Width, s.Height);
    }

    /// <summary>Grabs a region of the screen into a BitmapSource (physical pixels).</summary>
    public static BitmapSource? CaptureBitmap(int x, int y, int width, int height)
    {
        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, bmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                LogPublic("CaptureBitmap", new InvalidOperationException("GetDC(null) returned zero"));
                return null;
            }

            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero)
            {
                LogPublic("CaptureBitmap", new InvalidOperationException("CreateCompatibleDC returned zero"));
                return null;
            }

            // Use the screen DC for CreateDIBSection (more reliable than a pure memory DC)
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,   // negative = top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)(width * height * 4),
                }
            };

            IntPtr bits;
            bmp = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (bmp == IntPtr.Zero || bits == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                LogPublic("CaptureBitmap", new InvalidOperationException(
                    $"CreateDIBSection failed: bmp={bmp}, bits={bits}, w={width}, h={height}, win32err={err}"));
                return null;
            }

            oldBmp = SelectObject(memDc, bmp);
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY))
            {
                int err = Marshal.GetLastWin32Error();
                LogPublic("CaptureBitmap", new InvalidOperationException($"BitBlt returned false, win32err={err}"));
                return null;
            }

            int stride = width * 4;
            var buffer = new byte[stride * height];
            Marshal.Copy(bits, buffer, 0, buffer.Length);

            var bmpSource = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, null, buffer, stride);
            bmpSource.Freeze();
            return bmpSource;
        }
        catch (Exception ex)
        {
            LogPublic("CaptureBitmap", ex);
            return null;
        }
        finally
        {
            if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static void TryCopyToClipboard(BitmapSource bitmap)
    {
        try
        {
            Clipboard.SetImage(bitmap);
        }
        catch
        {
            // clipboard can be momentarily locked by another app
        }
    }

    private static void LogFail(string where, Exception ex) => LogPublic(where, ex);

    public static void LogPublic(string where, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "capture.log"),
                $"{DateTime.Now:o} {where}: {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
