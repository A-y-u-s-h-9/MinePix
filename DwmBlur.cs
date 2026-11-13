using System.Runtime.InteropServices;

namespace MinePix;

/// <summary>
/// DWM composition tricks: live acrylic blur of whatever is behind the island,
/// and a custom window region so the square top (flush with the screen edge)
/// and the rounded bottom corners both clip correctly.
/// </summary>
internal static class DwmBlur
{
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WcaAccentPolicy = 19; // WCA_ACCENT_POLICY

    private const int RgnOr = 2; // RGN_OR

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor; // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hrgn, bool redraw);

    /// <summary>
    /// Acrylic blur-behind the window. Alpha/color are ABGR 0xAABBGGRR packed in a DWORD.
    /// Falls back to plain blur on Windows builds without acrylic (pre-1809).
    /// </summary>
    public static void EnableAcrylic(IntPtr hwnd, byte alpha, byte r, byte g, byte b)
    {
        int gradient = (alpha << 24) | (b << 16) | (g << 8) | r;

        var accent = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = 2, // draw all borders
            GradientColor = gradient,
            AnimationId = 0,
        };

        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size,
            };

            if (SetWindowCompositionAttribute(hwnd, ref data) == 0)
            {
                accent.AccentState = AccentEnableBlurBehind;
                Marshal.StructureToPtr(accent, ptr, false);
                SetWindowCompositionAttribute(hwnd, ref data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Clip the window to the notch silhouette: square across the top,
    /// bottom corners rounded by <paramref name="bottomRadius"/>.
    /// </summary>
    public static void ApplyNotchRegion(IntPtr hwnd, int width, int height, int bottomRadius)
    {
        if (width <= 0 || height <= 0 || bottomRadius * 2 >= height) return;

        IntPtr top = CreateRectRgn(0, 0, width, height - bottomRadius);
        IntPtr bottom = CreateRoundRectRgn(0, height - 2 * bottomRadius, width, height, bottomRadius * 2, bottomRadius * 2);
        IntPtr merged = CreateRectRgn(0, 0, 0, 0);

        CombineRgn(merged, top, bottom, RgnOr);
        SetWindowRgn(hwnd, merged, true);

        DeleteObject(top);
        DeleteObject(bottom);
        // merged is owned by the window after SetWindowRgn; must not be deleted here.
    }
}
