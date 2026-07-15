using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SelfDesk.Sender.Capture;

public readonly record struct CursorShape(ushort HotspotX, ushort HotspotY, ushort Width, ushort Height, byte[] Bgra);

// Issue #22: GDI-based cursor shape capture. Uses Icon.FromHandle + Icon.ToBitmap,
// which normalizes color, monochrome (XOR-masked), and per-pixel-alpha cursors alike
// into a plain 32bpp ARGB bitmap — sidesteps the mono/color special-casing that a raw
// GetIconInfo + GetDIBits approach would need. Works regardless of which IScreenCapturer
// (GDI or DXGI) is active.
public static class CursorShapeProvider
{
    private const int MaxCursorDim = 128; // real cursors are tiny (32x32 typical); sanity cap

    private const int CURSOR_SHOWING = 0x00000001;

    // Cheap per-tick check — callers should only call Capture() when this changes.
    public static IntPtr GetCurrentCursorHandle()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci)) return IntPtr.Zero;
        if ((ci.flags & CURSOR_SHOWING) == 0) return IntPtr.Zero;
        return ci.hCursor;
    }

    public static CursorShape? Capture(IntPtr hCursor)
    {
        if (hCursor == IntPtr.Zero) return null;
        if (!GetIconInfo(hCursor, out var info)) return null;

        try
        {
            using var icon = Icon.FromHandle(hCursor);
            using var bmp  = icon.ToBitmap();

            if (bmp.Width <= 0 || bmp.Height <= 0 || bmp.Width > MaxCursorDim || bmp.Height > MaxCursorDim)
                return null;

            var bgra = new byte[bmp.Width * bmp.Height * 4];
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(data.Scan0, bgra, 0, bgra.Length); }
            finally { bmp.UnlockBits(data); }

            var hotspotX = (ushort)Math.Clamp(info.xHotspot, 0, bmp.Width - 1);
            var hotspotY = (ushort)Math.Clamp(info.yHotspot, 0, bmp.Height - 1);

            return new CursorShape(hotspotX, hotspotY, (ushort)bmp.Width, (ushort)bmp.Height, bgra);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.hbmMask  != IntPtr.Zero) DeleteObject(info.hbmMask);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

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
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
