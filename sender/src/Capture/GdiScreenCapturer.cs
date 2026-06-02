using System.Runtime.InteropServices;

namespace SelfDesk.Sender.Capture;

// BUG1: handles GDI e buffer BGRA cacheados no construtor — zero alocação por frame.
// BUG2: SM_CXVIRTUALSCREEN(78)/SM_CYVIRTUALSCREEN(79) em vez de SM_CXSCREEN(0)/SM_CYSCREEN(1).
public sealed class GdiScreenCapturer : IScreenCapturer
{
    private readonly int    _left;
    private readonly int    _top;
    private readonly int    _width;
    private readonly int    _height;
    private readonly byte[] _bgra;

    private readonly IntPtr _hdcScreen;
    private readonly IntPtr _hdcMem;
    private readonly IntPtr _hBitmap;
    private readonly IntPtr _hOldBitmap;
    private          BITMAPINFOHEADER _bi;

    public GdiScreenCapturer()
    {
        _left   = GetSystemMetrics(SM_XVIRTUALSCREEN);
        _top    = GetSystemMetrics(SM_YVIRTUALSCREEN);
        _width  = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        _height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        _bgra = new byte[_width * _height * 4];

        _hdcScreen  = GetDC(IntPtr.Zero);
        _hdcMem     = CreateCompatibleDC(_hdcScreen);
        _hBitmap    = CreateCompatibleBitmap(_hdcScreen, _width, _height);
        _hOldBitmap = SelectObject(_hdcMem, _hBitmap);

        _bi = new BITMAPINFOHEADER
        {
            biSize        = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth       = _width,
            biHeight      = -_height,
            biPlanes      = 1,
            biBitCount    = 32,
            biCompression = BI_RGB,
        };
    }

    public CapturedFrame CaptureFrame()
    {
        BitBlt(_hdcMem, 0, 0, _width, _height, _hdcScreen, _left, _top, SRCCOPY);
        GetDIBits(_hdcMem, _hBitmap, 0, (uint)_height, _bgra, ref _bi, DIB_RGB_COLORS);

        return new CapturedFrame(_width, _height, _bgra,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void Dispose()
    {
        SelectObject(_hdcMem, _hOldBitmap);
        DeleteObject(_hBitmap);
        DeleteDC(_hdcMem);
        ReleaseDC(IntPtr.Zero, _hdcScreen);
    }

    // ── P/Invoke ────────────────────────────────────────────────────
    private const uint SRCCOPY        = 0xCC0020;
    private const uint BI_RGB         = 0;
    private const uint DIB_RGB_COLORS = 0;

    private const int SM_XVIRTUALSCREEN  = 76;
    private const int SM_YVIRTUALSCREEN  = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern int    GetSystemMetrics(int nIndex);
    [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")]  private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")]  private static extern bool   DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")]  private static extern bool   DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]  private static extern bool   BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);
    [DllImport("gdi32.dll")]  private static extern int    GetDIBits(IntPtr hdc, IntPtr hBitmap, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint   biSize;
        public int    biWidth;
        public int    biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint   biCompression;
        public uint   biSizeImage;
        public int    biXPelsPerMeter;
        public int    biYPelsPerMeter;
        public uint   biClrUsed;
        public uint   biClrImportant;
    }
}
