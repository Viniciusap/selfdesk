namespace SelfDesk.Viewer.Protocol;

public static class ProtocolVersion
{
    public const byte Current = 0x01;
}

public static class MessageType
{
    public const byte Hello      = 0x01;
    public const byte Auth       = 0x02;
    public const byte AuthOk     = 0x03;
    public const byte AuthFail   = 0x04;
    public const byte Challenge  = 0x05;
    public const byte VideoFrame = 0x10;
    public const byte RequestIdr = 0x11;
    public const byte InputEvent = 0x20;
    public const byte SenderUp   = 0x30;
    public const byte SenderDown = 0x31;
    public const byte Ping       = 0x40;
    public const byte Pong       = 0x41;
    public const byte Bye        = 0x50;
    public const byte FileHeader     = 0x60;
    public const byte Clipboard      = 0x61;
    public const byte FileChunk      = 0x62;
    public const byte FileDone       = 0x63;
    public const byte FileError      = 0x64;
    public const byte MonitorList    = 0x70;
    public const byte SelectMonitor  = 0x71;
    public const byte AudioFrame     = 0x80;
}

public static class ProtocolSizes
{
    public const int HeaderSize   = 22;
    public const int PeerIdOffset = 2;
    public const int PeerIdSize   = 16;
    public const int LengthOffset = 18;
    public const int NonceSize    = 32;
}

public static class VideoFrameOffsets
{
    public const int Timestamp = 0;
    public const int Width     = 8;
    public const int Height    = 10;
    public const int Codec     = 12;
    public const int Flags     = 13;
    public const int Data      = 14;

    public const byte CodecJpeg    = 0x01;
    public const byte CodecH264    = 0x02;
    public const byte FlagKeyframe = 0x01;
}

public static class InputEventKind
{
    public const byte MouseMove   = 0x01;
    public const byte MouseButton = 0x02;
    public const byte MouseWheel  = 0x03;
    public const byte Key         = 0x10;

    public const byte StateUp   = 0;
    public const byte StateDown = 1;

    public const byte BtnLeft   = 0;
    public const byte BtnRight  = 1;
    public const byte BtnMiddle = 2;

    public const byte ModShift = 0x01;
    public const byte ModCtrl  = 0x02;
    public const byte ModAlt   = 0x04;
    public const byte ModWin   = 0x08;
}
