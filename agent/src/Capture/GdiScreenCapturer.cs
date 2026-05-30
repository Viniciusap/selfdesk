using System.Runtime.InteropServices;

namespace SelfDesk.Agent.Capture;

/// <summary>
/// Captura a tela via GDI BitBlt.
/// Funciona sem dependências externas; é compatível com todos os monitores.
/// Para Phase 4+, substituir por DxgiScreenCapturer (Vortice) para menor latência.
/// </summary>
public sealed class GdiScreenCapturer : IScreenCapturer
{
    private readonly int _left;
    private readonly int _top;
    private readonly int _width;
    private readonly int _height;

    public GdiScreenCapturer()
    {
        _left   = GetSystemMetrics(SM_XVIRTUALSCREEN);
        _top    = GetSystemMetrics(SM_YVIRTUALSCREEN);
        _width  = GetSystemMetrics(SM_CXSCREEN);
        _height = GetSystemMetrics(SM_CYSCREEN);
    }

    public CapturedFrame CaptureFrame()
    {
        var hdcScreen = GetDC(IntPtr.Zero);
        var hdcMem    = CreateCompatibleDC(hdcScreen);
        var hBitmap   = CreateCompatibleBitmap(hdcScreen, _width, _height);
        var hOld      = SelectObject(hdcMem, hBitmap);

        BitBlt(hdcMem, 0, 0, _width, _height, hdcScreen, _left, _top, SRCCOPY);

        var bi = new BITMAPINFOHEADER
        {
            biSize        = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth       = _width,
            biHeight      = -_height,
            biPlanes      = 1,
            biBitCount    = 32,
            biCompression = BI_RGB,
        };
        var bgra = new byte[_width * _height * 4];
        GetDIBits(hdcMem, hBitmap, 0, (uint)_height, bgra, ref bi, DIB_RGB_COLORS);

        SelectObject(hdcMem, hOld);
        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);

        return new CapturedFrame(_width, _height, bgra,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void Dispose() { }

    // ── P/Invoke ────────────────────────────────────────────────────
    private const uint SRCCOPY      = 0xCC0020;
    private const uint BI_RGB       = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const int  SM_XVIRTUALSCREEN = 76;
    private const int  SM_YVIRTUALSCREEN = 77;
    private const int  SM_CXSCREEN       = 0;
    private const int  SM_CYSCREEN       = 1;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")]  private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")]  private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);
    [DllImport("gdi32.dll")]  private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);
    [DllImport("gdi32.dll")]  private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")]  private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint  biSize;
        public int   biWidth;
        public int   biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint  biCompression;
        public uint  biSizeImage;
        public int   biXPelsPerMeter;
        public int   biYPelsPerMeter;
        public uint  biClrUsed;
        public uint  biClrImportant;
    }
}
