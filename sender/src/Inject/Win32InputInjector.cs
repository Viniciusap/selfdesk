using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SelfDesk.Sender.Capture;
using SelfDesk.Sender.Protocol;

namespace SelfDesk.Sender.Inject;

public sealed class Win32InputInjector : IInputInjector, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<ushort, DateTime> _keysDown = new();
    private readonly System.Threading.Timer _stuckKeyTimer;
    private byte _trackedMods;
    private int _monitorIndex;
    private MonitorInfo? _monitor;

    public int MonitorIndex
    {
        get => _monitorIndex;
        set
        {
            _monitorIndex = value;
            var list = MonitorEnumerator.Enumerate();
            _monitor = value < list.Count ? list[value] : list.Count > 0 ? list[0] : null;
        }
    }

    public Win32InputInjector()
    {
        MonitorIndex = 0; // initializes _monitor with the primary monitor
        _stuckKeyTimer = new System.Threading.Timer(
            ReleaseStuckKeys, null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void Dispose() => _stuckKeyTimer.Dispose();

    public void Inject(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0) return;
        var kind = payload[0];
        var rest = payload[1..];

        switch (kind)
        {
            case InputEventKind.MouseMove when rest.Length >= 4:
                InjectMouseMove(
                    BinaryPrimitives.ReadUInt16BigEndian(rest),
                    BinaryPrimitives.ReadUInt16BigEndian(rest[2..]));
                break;

            case InputEventKind.MouseMoveRel when rest.Length >= 4:
                InjectMouseMoveRel(
                    BinaryPrimitives.ReadInt16BigEndian(rest),
                    BinaryPrimitives.ReadInt16BigEndian(rest[2..]));
                break;

            case InputEventKind.MouseButton when rest.Length >= 6:
                InjectMouseButton(
                    rest[0],
                    rest[1],
                    BinaryPrimitives.ReadUInt16BigEndian(rest[2..]),
                    BinaryPrimitives.ReadUInt16BigEndian(rest[4..]));
                break;

            case InputEventKind.MouseWheel when rest.Length >= 6:
                InjectMouseWheel(
                    BinaryPrimitives.ReadInt16BigEndian(rest),
                    BinaryPrimitives.ReadUInt16BigEndian(rest[2..]),
                    BinaryPrimitives.ReadUInt16BigEndian(rest[4..]));
                break;

            case InputEventKind.Key when rest.Length >= 4:
                lock (_syncRoot)
                {
                    InjectKey(
                        BinaryPrimitives.ReadUInt16BigEndian(rest),
                        rest[2],
                        rest[3]);
                }
                break;
        }
    }

    public void ReleaseAllModifiers()
    {
        // VK: LShift, RShift, LCtrl, RCtrl, LAlt, RAlt, LWin, RWin
        ushort[] mods = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];
        lock (_syncRoot)
        {
            foreach (var vk in mods)
            {
                var input = MakeKey(vk, false);
                SendInput(1, ref input, Marshal.SizeOf<INPUT>());
            }
            _trackedMods = 0;
        }
    }

    // Remaps normalized coords [0,65535] from the captured frame to the virtual screen space.
    // Without remap, normX=65535 maps to the right edge of the virtual screen (all monitors),
    // but should map to the right edge of the selected monitor only.
    private (int ax, int ay) Remap(ushort normX, ushort normY)
    {
        var m = _monitor;
        if (m is null)
            return (normX, normY);

        int vLeft   = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop    = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth  = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        double physX = m.Left + (double)normX * m.Width  / 65535.0;
        double physY = m.Top  + (double)normY * m.Height / 65535.0;

        int ax = (int)((physX - vLeft) * 65535.0 / vWidth);
        int ay = (int)((physY - vTop)  * 65535.0 / vHeight);
        return (ax, ay);
    }

    private void InjectMouseMove(ushort normX, ushort normY)
    {
        var (ax, ay) = Remap(normX, normY);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi   = new MOUSEINPUT
            {
                dx      = ax,
                dy      = ay,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private void InjectMouseMoveRel(short dx, short dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi   = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private void InjectMouseButton(byte btn, byte state, ushort normX, ushort normY)
    {
        uint downFlag, upFlag;
        uint mouseData = 0;

        (downFlag, upFlag, mouseData) = btn switch
        {
            InputEventKind.BtnLeft   => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0u),
            InputEventKind.BtnRight  => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0u),
            InputEventKind.BtnMiddle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0u),
            InputEventKind.BtnX1     => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1),
            InputEventKind.BtnX2     => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2),
            _                        => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0u),
        };

        var (ax, ay) = Remap(normX, normY);
        var flags = (state == InputEventKind.StateDown ? downFlag : upFlag)
                  | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi   = new MOUSEINPUT { dx = ax, dy = ay, dwFlags = flags, mouseData = mouseData },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private void InjectMouseWheel(short delta, ushort normX, ushort normY)
    {
        var (ax, ay) = Remap(normX, normY);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi   = new MOUSEINPUT
            {
                dx        = ax,
                dy        = ay,
                mouseData = (uint)(delta * 120),
                dwFlags   = MOUSEEVENTF_WHEEL | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private void InjectKey(ushort vk, byte state, byte mods)
    {
        // must be called under _syncRoot lock
        var modBit = GetModBit(vk);
        FixModifiers(modBit, mods);

        var input = MakeKey(vk, state == InputEventKind.StateDown);
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());

        if (modBit != 0)
        {
            if (state == InputEventKind.StateDown) _trackedMods |= modBit;
            else _trackedMods &= (byte)~modBit;
        }

        if (state == InputEventKind.StateDown)
            _keysDown[vk] = DateTime.UtcNow;
        else
            _keysDown.Remove(vk);
    }

    // Ensures tracked modifier state matches what the viewer reports before injecting a key.
    // Skips the modifier bit for the key currently being injected to avoid double-injection.
    private void FixModifiers(byte skipBit, byte mods)
    {
        FixMod(skipBit, InputEventKind.ModShift, 0xA0, mods);
        FixMod(skipBit, InputEventKind.ModCtrl,  0xA2, mods);
        FixMod(skipBit, InputEventKind.ModAlt,   0xA4, mods);
        FixMod(skipBit, InputEventKind.ModWin,   0x5B, mods);
    }

    private void FixMod(byte skipBit, byte modBit, ushort modVk, byte mods)
    {
        if (modBit == skipBit) return;
        var shouldBeDown = (mods & modBit) != 0;
        var isDown       = (_trackedMods & modBit) != 0;
        if (shouldBeDown == isDown) return;

        var input = MakeKey(modVk, shouldBeDown);
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());

        if (shouldBeDown) _trackedMods |= modBit;
        else              _trackedMods &= (byte)~modBit;
    }

    private void ReleaseStuckKeys(object? state)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(10);
        List<ushort>? stale = null;

        lock (_syncRoot)
        {
            foreach (var kvp in _keysDown)
            {
                if (kvp.Value < cutoff)
                {
                    stale ??= [];
                    stale.Add(kvp.Key);
                }
            }
            if (stale is null) return;
            foreach (var vk in stale)
            {
                _keysDown.Remove(vk);
                var input = MakeKey(vk, false);
                SendInput(1, ref input, Marshal.SizeOf<INPUT>());
                var mb = GetModBit(vk);
                if (mb != 0) _trackedMods &= (byte)~mb;
            }
        }
    }

    private static byte GetModBit(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => InputEventKind.ModShift,
        0xA2 or 0xA3 => InputEventKind.ModCtrl,
        0xA4 or 0xA5 => InputEventKind.ModAlt,
        0x5B or 0x5C => InputEventKind.ModWin,
        _ => 0,
    };

    private static bool IsExtendedKey(ushort vk) => vk is
        0xA3 or 0xA5 or         // RCtrl, RAlt (AltGr)
        0x5B or 0x5C or         // LWin, RWin
        0x21 or 0x22 or 0x23 or 0x24 or  // PgUp, PgDn, End, Home
        0x25 or 0x26 or 0x27 or 0x28 or  // Left, Up, Right, Down
        0x2D or 0x2E or         // Ins, Del
        0x6F;                   // Numpad /

    private static INPUT MakeKey(ushort vk, bool down)
    {
        bool extended = IsExtendedKey(vk);
        uint flags = down ? 0u : KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        var wScan = (ushort)MapVirtualKey(vk, extended ? MAPVK_VK_TO_VSC_EX : MAPVK_VK_TO_VSC);
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki   = new KEYBDINPUT { wVk = vk, wScan = wScan, dwFlags = flags },
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN  = 76;
    private const int SM_YVIRTUALSCREEN  = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint MAPVK_VK_TO_VSC    = 0;
    private const uint MAPVK_VK_TO_VSC_EX = 3;

    // ── P/Invoke types ─────────────────────────────────────────────
    private const uint INPUT_MOUSE    = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE        = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN    = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP      = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN   = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP     = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN  = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP    = 0x0040;
    private const uint MOUSEEVENTF_XDOWN       = 0x0080;
    private const uint MOUSEEVENTF_XUP         = 0x0100;
    private const uint MOUSEEVENTF_WHEEL       = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE    = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP       = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion _union;

        public MOUSEINPUT mi
        {
            get => _union.mi;
            set => _union.mi = value;
        }
        public KEYBDINPUT ki
        {
            get => _union.ki;
            set => _union.ki = value;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT   mi;
        [FieldOffset(0)] public KEYBDINPUT   ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int  dx;
        public int  dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public nint   dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint   uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
