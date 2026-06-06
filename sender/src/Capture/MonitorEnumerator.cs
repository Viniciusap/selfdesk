using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SelfDesk.Sender.Capture;

public static class MonitorEnumerator
{
    // Try DXGI first — works from Session 0 (Windows Service).
    // GDI (EnumDisplayMonitors) only sees monitors in the interactive session — useless from services.
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        try { return EnumerateViaDxgi(); }
        catch { }
        return EnumerateViaGdi();
    }

    private static IReadOnlyList<MonitorInfo> EnumerateViaDxgi()
    {
        using var device  = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.None);
        using var dxgiDev = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDev.GetAdapter();

        var entries = new List<(int left, int top, int right, int bottom, string name, uint idx)>();
        uint i = 0;
        while (adapter.EnumOutputs(i, out IDXGIOutput output).Success)
        {
            using (output)
            {
                var d = output.Description;
                entries.Add((d.DesktopCoordinates.Left, d.DesktopCoordinates.Top,
                             d.DesktopCoordinates.Right, d.DesktopCoordinates.Bottom,
                             d.DeviceName, i));
            }
            i++;
        }

        if (entries.Count == 0) throw new InvalidOperationException("Nenhum output DXGI");

        entries.Sort((a, b) =>
        {
            bool aP = a.left == 0 && a.top == 0;
            bool bP = b.left == 0 && b.top == 0;
            if (aP != bP) return aP ? -1 : 1;
            return a.left.CompareTo(b.left);
        });

        return entries.Select((e, idx) =>
        {
            bool primary = e.left == 0 && e.top == 0;
            int w = e.right - e.left, h = e.bottom - e.top;
            return new MonitorInfo(idx, e.left, e.top, w, h,
                primary ? $"Monitor {idx + 1} — {w}×{h} (Principal)"
                        : $"Monitor {idx + 1} — {w}×{h}",
                primary);
        }).ToList();
    }

    private static IReadOnlyList<MonitorInfo> EnumerateViaGdi()
    {
        var raw = new List<(RECT rect, bool primary)>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hmon, ref info))
                raw.Add((info.rcMonitor, (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);

        raw.Sort((a, b) =>
        {
            if (a.primary != b.primary) return a.primary ? -1 : 1;
            return a.rect.left.CompareTo(b.rect.left);
        });

        return raw.Select((m, i) =>
        {
            int w = m.rect.right - m.rect.left, h = m.rect.bottom - m.rect.top;
            return new MonitorInfo(i, m.rect.left, m.rect.top, w, h,
                m.primary ? $"Monitor 1 — {w}×{h} (Principal)"
                          : $"Monitor {i + 1} — {w}×{h}",
                m.primary);
        }).ToList();
    }

    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
