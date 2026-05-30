using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SelfDesk.Agent.Capture;
using SelfDesk.Agent.Protocol;

namespace SelfDesk.Agent.Encode;

public sealed unsafe class H264Encoder : IFrameEncoder
{
    private readonly string _encoderName;
    private readonly int _targetFps;
    private readonly ILogger<H264Encoder> _log;

    private AVCodecContext* _codecCtx;
    private AVFrame* _dstFrame;
    private SwsContext* _swsCtx;
    private AVPacket* _pkt;
    private int _width;
    private int _height;
    private long _pts;
    private bool _initialized;

    // AVERROR(EAGAIN) = -11; AVERROR_EOF = -541478725
    private const int AvErrorEagain = -11;
    private const int AvErrorEof    = unchecked((int)0xDFB9B0BB);

    public H264Encoder(string encoder, int targetFps, ILogger<H264Encoder> log)
    {
        _encoderName = encoder.ToLowerInvariant() switch
        {
            "qsv"   => "h264_qsv",
            "nvenc" => "h264_nvenc",
            _       => encoder,
        };
        _targetFps = targetFps;
        _log       = log;
    }

    private void Initialize(int width, int height)
    {
        var codec = ffmpeg.avcodec_find_encoder_by_name(_encoderName);
        if (codec == null)
            throw new InvalidOperationException(
                $"H264 encoder '{_encoderName}' not found. " +
                "Ensure FFmpeg DLLs are in the executable directory and hardware is supported.");

        _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecCtx == null)
            throw new InvalidOperationException("Failed to allocate AVCodecContext.");

        _codecCtx->width      = width;
        _codecCtx->height     = height;
        _codecCtx->time_base  = new AVRational { num = 1, den = _targetFps };
        _codecCtx->framerate  = new AVRational { num = _targetFps, den = 1 };
        _codecCtx->gop_size   = _targetFps * 2;
        _codecCtx->max_b_frames = 0;
        _codecCtx->pix_fmt    = AVPixelFormat.AV_PIX_FMT_NV12;
        _codecCtx->bit_rate   = 4_000_000;

        int ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
        if (ret < 0)
            throw new InvalidOperationException(
                $"Failed to open encoder '{_encoderName}' (code {ret}). " +
                "Check that hardware is available and drivers are installed.");

        _dstFrame = ffmpeg.av_frame_alloc();
        _dstFrame->width  = width;
        _dstFrame->height = height;
        _dstFrame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        ffmpeg.av_frame_get_buffer(_dstFrame, 0);

        _swsCtx = ffmpeg.sws_getContext(
            width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
            width, height, AVPixelFormat.AV_PIX_FMT_NV12,
            ffmpeg.SWS_BILINEAR, null, null, null);

        if (_swsCtx == null)
            throw new InvalidOperationException("Failed to create SwsContext (BGRA→NV12).");

        _pkt   = ffmpeg.av_packet_alloc();
        _width = width;
        _height = height;
        _pts   = 0;

        _log.LogInformation("H264Encoder initialised: encoder={Encoder} {Width}x{Height} {Fps}fps",
            _encoderName, width, height, _targetFps);

        _initialized = true;
    }

    public EncodedFrame Encode(CapturedFrame frame)
    {
        if (!_initialized || _width != frame.Width || _height != frame.Height)
        {
            FreeContexts();
            Initialize(frame.Width, frame.Height);
        }

        fixed (byte* bgraPtr = frame.Bgra)
        {
            byte*[] srcSlice  = { bgraPtr };
            int[]   srcStride = { frame.Width * 4 };

            byte*[] dstSlice  = { _dstFrame->data[0u], _dstFrame->data[1u] };
            int[]   dstStride = { _dstFrame->linesize[0u], _dstFrame->linesize[1u] };

            ffmpeg.sws_scale(_swsCtx, srcSlice, srcStride, 0, frame.Height, dstSlice, dstStride);
        }

        _dstFrame->pts = _pts++;

        int ret = ffmpeg.avcodec_send_frame(_codecCtx, _dstFrame);
        if (ret < 0)
            throw new InvalidOperationException($"avcodec_send_frame error: {ret}");

        ret = ffmpeg.avcodec_receive_packet(_codecCtx, _pkt);
        if (ret == AvErrorEagain || ret == AvErrorEof)
            return new EncodedFrame(Array.Empty<byte>(), VideoFrameOffsets.CodecH264, 0,
                frame.Width, frame.Height, frame.TimestampMs);

        if (ret < 0)
            throw new InvalidOperationException($"avcodec_receive_packet error: {ret}");

        bool keyframe = (_pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
        var data = new byte[_pkt->size];
        Marshal.Copy((IntPtr)_pkt->data, data, 0, _pkt->size);
        ffmpeg.av_packet_unref(_pkt);

        return new EncodedFrame(
            data,
            VideoFrameOffsets.CodecH264,
            keyframe ? (byte)1 : (byte)0,
            frame.Width,
            frame.Height,
            frame.TimestampMs);
    }

    private void FreeContexts()
    {
        if (!_initialized) return;

        if (_pkt != null)      { var p = _pkt;      ffmpeg.av_packet_free(&p);      _pkt      = null; }
        if (_dstFrame != null) { var f = _dstFrame;  ffmpeg.av_frame_free(&f);       _dstFrame = null; }
        if (_swsCtx != null)   { ffmpeg.sws_freeContext(_swsCtx);                    _swsCtx   = null; }
        if (_codecCtx != null) { var c = _codecCtx;  ffmpeg.avcodec_free_context(&c); _codecCtx = null; }

        _initialized = false;
    }

    public void Dispose() => FreeContexts();
}
