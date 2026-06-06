using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SelfDesk.Sender.Capture;

// DXGI Desktop Duplication — GPU-accelerated, zero GPU copy per frame (staging→_bgra only).
// Requires Windows 8+ and a D3D11-capable GPU. Set CAPTURER=dxgi in .env.
// Fails silently and falls back to GDI in RDP/VM sessions (see Program.cs).
public sealed class DxgiScreenCapturer : IScreenCapturer
{
    private readonly ID3D11Device        _device;
    private readonly ID3D11DeviceContext _context;
    private          IDXGIOutputDuplication _duplication = null!;
    private          ID3D11Texture2D?    _staging;

    private int    _width;
    private int    _height;
    private byte[] _bgra;

    public DxgiScreenCapturer()
    {
        _device  = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.None);
        _context = _device.ImmediateContext;
        _bgra    = [];
        InitDuplication(0);
    }

    public void SwitchMonitor(int monitorIndex)
    {
        _staging?.Dispose();
        _staging = null;
        InitDuplication(monitorIndex);
    }

    private void InitDuplication(int outputIndex)
    {
        _duplication?.Dispose();

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter    = dxgiDevice.GetAdapter();

        adapter.EnumOutputs((uint)outputIndex, out IDXGIOutput rawOutput).CheckError();
        using (rawOutput)
        {
            using var output1 = rawOutput.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(_device);
        }

        var desc = _duplication.Description;
        _width   = (int)desc.ModeDescription.Width;
        _height  = (int)desc.ModeDescription.Height;
        _bgra    = new byte[_width * _height * 4];
    }

    public CapturedFrame CaptureFrame()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = _duplication.AcquireNextFrame(16u, out _, out IDXGIResource resource);
        if (result.Failure)
            return new CapturedFrame(_width, _height, _bgra, now);

        try
        {
            using var texture = resource.QueryInterface<ID3D11Texture2D>();

            if (_staging is null)
            {
                var td = texture.Description;
                _width  = (int)td.Width;
                _height = (int)td.Height;
                if (_bgra.Length != _width * _height * 4)
                    _bgra = new byte[_width * _height * 4];

                _staging = _device.CreateTexture2D(new Texture2DDescription
                {
                    Width             = td.Width,
                    Height            = td.Height,
                    Format            = td.Format,
                    MipLevels         = 1,
                    ArraySize         = 1,
                    SampleDescription = new SampleDescription { Count = 1, Quality = 0 },
                    Usage             = ResourceUsage.Staging,
                    BindFlags         = BindFlags.None,
                    CPUAccessFlags    = CpuAccessFlags.Read,
                    MiscFlags         = ResourceOptionFlags.None,
                }, Span<SubresourceData>.Empty);
            }

            _context.CopyResource(_staging, texture);
        }
        finally
        {
            resource.Dispose();
            _duplication.ReleaseFrame();
        }

        _context.Map(_staging!, 0u, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped);
        try
        {
            unsafe
            {
                var src = (byte*)mapped.DataPointer;
                for (int row = 0; row < _height; row++)
                    new ReadOnlySpan<byte>(src + (long)row * mapped.RowPitch, _width * 4)
                        .CopyTo(_bgra.AsSpan(row * _width * 4, _width * 4));
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0u);
        }

        return new CapturedFrame(_width, _height, _bgra, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void Dispose()
    {
        _staging?.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
