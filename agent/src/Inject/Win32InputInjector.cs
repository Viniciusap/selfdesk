using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SelfDesk.Agent.Protocol;

namespace SelfDesk.Agent.Inject;

public sealed class Win32InputInjector : IInputInjector
{
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
                InjectKey(
                    BinaryPrimitives.ReadUInt16BigEndian(rest),
                    rest[2],
                    rest[3]);
                break;
        }
    }

    private static void InjectMouseMove(ushort normX, ushort normY)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi   = new MOUSEINPUT
            {
                dx          = normX,
                dy          = normY,
                dwFlags     = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                mouseData   = 0,
                time        = 0,
                dwExtraInfo = 0,
            },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private static void InjectMouseButton(byte btn, byte state, ushort normX, ushort normY)
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

        var flags = (state == InputEventKind.StateDown ? downFlag : upFlag)
                  | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi   = new MOUSEINPUT { dx = normX, dy = normY, dwFlags = flags, mouseData = mouseData },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private static void InjectMouseWheel(short delta, ushort normX, ushort normY)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi   = new MOUSEINPUT
            {
                dx        = normX,
                dy        = normY,
                mouseData = (uint)(delta * 120),
                dwFlags   = MOUSEEVENTF_WHEEL | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private static void InjectKey(ushort vk, byte state, byte mods)
    {
        var list = new List<INPUT>();

        if ((mods & InputEventKind.ModShift) != 0) list.Add(MakeKey(0xA0, state == InputEventKind.StateDown));
        if ((mods & InputEventKind.ModCtrl)  != 0) list.Add(MakeKey(0xA2, state == InputEventKind.StateDown));
        if ((mods & InputEventKind.ModAlt)   != 0) list.Add(MakeKey(0xA4, state == InputEventKind.StateDown));

        list.Add(MakeKey(vk, state == InputEventKind.StateDown));

        var arr = list.ToArray();
        SendInputArray(arr);
    }

    private static INPUT MakeKey(ushort vk, bool down)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki   = new KEYBDINPUT
            {
                wVk      = vk,
                wScan    = 0,
                dwFlags  = down ? 0u : KEYEVENTF_KEYUP,
                time     = 0,
                dwExtraInfo = 0,
            },
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray)] INPUT[] pInputs, int cbSize);

    private static void SendInputArray(INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

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

    private const uint KEYEVENTF_KEYUP = 0x0002;

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
