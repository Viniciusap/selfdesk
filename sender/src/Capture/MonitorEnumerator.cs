using System.Runtime.InteropServices;

namespace SelfDesk.Sender.Capture;

public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var raw = new List<(RECT rect, bool primary, string device)>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hmon, ref info))
                raw.Add((info.rcMonitor, (info.dwFlags & MONITORINFOF_PRIMARY) != 0, info.GetDevice()));
            return true;
        }, IntPtr.Zero);

        // primary first, then left-to-right
        raw.Sort((a, b) =>
        {
            if (a.primary != b.primary) return a.primary ? -1 : 1;
            return a.rect.left.CompareTo(b.rect.left);
        });

        return raw.Select((m, i) => new MonitorInfo(
            i,
            m.rect.left,
            m.rect.top,
            m.rect.right  - m.rect.left,
            m.rect.bottom - m.rect.top,
            m.primary ? $"Monitor 1 — {m.rect.right - m.rect.left}×{m.rect.bottom - m.rect.top} (Principal)"
                      : $"Monitor {i + 1} — {m.rect.right - m.rect.left}×{m.rect.bottom - m.rect.top}",
            m.primary
        )).ToList();
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

        public string GetDevice() => szDevice ?? string.Empty;
    }
}
