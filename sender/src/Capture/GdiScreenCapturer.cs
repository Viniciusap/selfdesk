using System.Runtime.InteropServices;

namespace SelfDesk.Sender.Capture;

// BUG1: GDI handles and BGRA buffer cached in constructor — zero allocation per frame.
// BUG2: uses monitor rect via MonitorEnumerator instead of SM_CXSCREEN — supports multi-monitor.
public sealed class GdiScreenCapturer : IScreenCapturer
{
    private int    _left;
    private int    _top;
    private int    _width;
    private int    _height;
    private byte[] _bgra = [];

    private IntPtr _hdcScreen;
    private IntPtr _hdcMem;
    private IntPtr _hBitmap;
    private IntPtr _hOldBitmap;
    private BITMAPINFOHEADER _bi;

    public GdiScreenCapturer() => InitMonitor(0);

    public void SwitchMonitor(int monitorIndex)
    {
        ReleaseHandles();
        InitMonitor(monitorIndex);
    }

    private void InitMonitor(int monitorIndex)
    {
        var monitors = MonitorEnumerator.Enumerate();
        var m = monitorIndex < monitors.Count ? monitors[monitorIndex] : monitors[0];

        _left   = m.Left;
        _top    = m.Top;
        _width  = m.Width;
        _height = m.Height;
        _bgra   = new byte[_width * _height * 4];

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

    private void ReleaseHandles()
    {
        if (_hdcMem == IntPtr.Zero) return;
        SelectObject(_hdcMem, _hOldBitmap);
        DeleteObject(_hBitmap);
        DeleteDC(_hdcMem);
        ReleaseDC(IntPtr.Zero, _hdcScreen);
        _hdcMem = IntPtr.Zero;
    }

    public void Dispose() => ReleaseHandles();

    // ── P/Invoke ────────────────────────────────────────────────────
    private const uint SRCCOPY        = 0xCC0020;
    private const uint BI_RGB         = 0;
    private const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
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
