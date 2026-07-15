using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace SelfDesk.Viewer.Cursor;

// Issue #22: builds a real Win32 HCURSOR from the sender's BGRA cursor bitmap so the
// viewer shows the actual remote cursor shape (resize arrows, text beam, etc.) instead
// of a fixed placeholder.
internal static class CursorShapeFactory
{
    public static System.Windows.Input.Cursor? Create(byte[] bgra, int width, int height, int hotspotX, int hotspotY)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize        = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth       = width,
                biHeight      = -height, // negative = top-down DIB, matches our row-major BGRA layout
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = BI_RGB,
            },
        };

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr colorBmp = IntPtr.Zero;
        IntPtr maskBmp  = IntPtr.Zero;
        try
        {
            colorBmp = CreateDIBSection(screenDc, ref bmi, DIB_RGB_COLORS, out var bitsPtr, IntPtr.Zero, 0);
            if (colorBmp == IntPtr.Zero || bitsPtr == IntPtr.Zero) return null;
            Marshal.Copy(bgra, 0, bitsPtr, bgra.Length);

            // AND mask content is irrelevant for a 32bpp color bitmap with real per-pixel
            // alpha — Windows composites using the alpha channel since XP. Still required
            // by CreateIconIndirect, so pass an all-zero 1bpp bitmap of the right size.
            maskBmp = CreateBitmap(width, height, 1, 1, IntPtr.Zero);
            if (maskBmp == IntPtr.Zero) return null;

            var iconInfo = new ICONINFO
            {
                fIcon    = false, // false = cursor (uses hotspot), true = icon
                xHotspot = hotspotX,
                yHotspot = hotspotY,
                hbmMask  = maskBmp,
                hbmColor = colorBmp,
            };

            var hIcon = CreateIconIndirect(ref iconInfo);
            if (hIcon == IntPtr.Zero) return null;

            return CursorInteropHelper.Create(new SafeCursorHandle(hIcon));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            // CreateIconIndirect copies hbmMask/hbmColor internally — safe (and required)
            // to delete our copies regardless of success.
            if (colorBmp != IntPtr.Zero) DeleteObject(colorBmp);
            if (maskBmp  != IntPtr.Zero) DeleteObject(maskBmp);
        }
    }

    private sealed class SafeCursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeCursorHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => DestroyIcon(handle);
    }

    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB         = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public uint biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr lpvBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
