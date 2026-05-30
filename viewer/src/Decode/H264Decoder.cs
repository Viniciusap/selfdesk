using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace SelfDesk.Viewer.Decode;

public sealed unsafe class H264Decoder : IFrameDecoder, IDisposable
{
    private AVCodecContext* _codecCtx;
    private AVFrame*        _yuvFrame;
    private AVFrame*        _bgraFrame;
    private SwsContext*     _swsCtx;
    private AVPacket*       _pkt;
    private int  _swsWidth;
    private int  _swsHeight;
    private bool _seenKeyframe;

    private const int AvErrorEagain = -11;
    private const int AvErrorEof    = unchecked((int)0xDFB9B0BB);
    // AV_INPUT_BUFFER_PADDING_SIZE = 64
    private const int InputPadding = 64;

    public H264Decoder()
    {
        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        if (codec == null)
            throw new InvalidOperationException("H264 software decoder (libavcodec) not found.");

        _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecCtx == null)
            throw new InvalidOperationException("Failed to allocate AVCodecContext for H264 decoder.");

        int ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
        if (ret < 0)
            throw new InvalidOperationException($"Failed to open H264 decoder (code {ret}).");

        _yuvFrame  = ffmpeg.av_frame_alloc();
        _bgraFrame = ffmpeg.av_frame_alloc();
        _pkt       = ffmpeg.av_packet_alloc();
    }

    public DecodedFrame Decode(ReadOnlySpan<byte> data, int width, int height, long timestampMs)
    {
        if (data.IsEmpty)
            throw new InvalidOperationException("Empty H264 NAL data.");

        // Check for keyframe (IDR NAL unit, type 5) before committing frames to decoder
        bool isKeyframe = ContainsIdr(data);
        if (isKeyframe)
        {
            // Flush decoder so stale state is cleared
            ffmpeg.avcodec_flush_buffers(_codecCtx);
            _seenKeyframe = true;
        }

        if (!_seenKeyframe)
            throw new InvalidOperationException("Waiting for H264 keyframe (IDR).");

        // Copy with padding required by FFmpeg bitstream readers
        var padded = new byte[data.Length + InputPadding];
        data.CopyTo(padded);

        fixed (byte* dataPtr = padded)
        {
            _pkt->data = dataPtr;
            _pkt->size = data.Length;

            int ret = ffmpeg.avcodec_send_packet(_codecCtx, _pkt);
            if (ret < 0)
                throw new InvalidOperationException($"avcodec_send_packet error: {ret}");

            ret = ffmpeg.avcodec_receive_frame(_codecCtx, _yuvFrame);
            if (ret == AvErrorEagain)
                throw new InvalidOperationException("Decoder buffering — no frame output yet.");
            if (ret == AvErrorEof)
                throw new InvalidOperationException("Decoder EOF.");
            if (ret < 0)
                throw new InvalidOperationException($"avcodec_receive_frame error: {ret}");
        }

        int actualW = _yuvFrame->width;
        int actualH = _yuvFrame->height;

        // Re-create swscale context on dimension change
        if (_swsCtx == null || _swsWidth != actualW || _swsHeight != actualH)
        {
            if (_swsCtx != null) ffmpeg.sws_freeContext(_swsCtx);

            _swsCtx = ffmpeg.sws_getContext(
                actualW, actualH, (AVPixelFormat)_yuvFrame->format,
                actualW, actualH, AVPixelFormat.AV_PIX_FMT_BGRA,
                ffmpeg.SWS_BILINEAR, null, null, null);

            if (_swsCtx == null)
                throw new InvalidOperationException("Failed to create SwsContext (YUV→BGRA).");

            ffmpeg.av_frame_unref(_bgraFrame);
            _bgraFrame->width  = actualW;
            _bgraFrame->height = actualH;
            _bgraFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            ffmpeg.av_frame_get_buffer(_bgraFrame, 0);

            _swsWidth  = actualW;
            _swsHeight = actualH;
        }

        // YUV → BGRA
        var srcSlice  = new byte*[8];
        var srcStride = new int[8];
        for (uint i = 0; i < 8; i++)
        {
            srcSlice[i]  = _yuvFrame->data[i];
            srcStride[i] = _yuvFrame->linesize[i];
        }

        byte*[] dstSlice  = { _bgraFrame->data[0u] };
        int[]   dstStride = { _bgraFrame->linesize[0u] };

        ffmpeg.sws_scale(_swsCtx, srcSlice, srcStride, 0, actualH, dstSlice, dstStride);

        // Copy BGRA pixels out
        int bgraSize = actualW * actualH * 4;
        var bgra = new byte[bgraSize];
        fixed (byte* bgraPtr = bgra)
            Buffer.MemoryCopy(_bgraFrame->data[0], bgraPtr, bgraSize, bgraSize);

        ffmpeg.av_frame_unref(_yuvFrame);

        return new DecodedFrame(actualW, actualH, bgra, timestampMs);
    }

    private static bool ContainsIdr(ReadOnlySpan<byte> data)
    {
        // Annex-B start codes: 00 00 00 01 or 00 00 01
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
            {
                if (i + 4 < data.Length && (data[i + 4] & 0x1F) == 5) // IDR
                    return true;
                i += 3;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_pkt != null)       { var p = _pkt;       ffmpeg.av_packet_free(&p);       _pkt       = null; }
        if (_yuvFrame != null)  { var f = _yuvFrame;   ffmpeg.av_frame_free(&f);        _yuvFrame  = null; }
        if (_bgraFrame != null) { var f = _bgraFrame;  ffmpeg.av_frame_free(&f);        _bgraFrame = null; }
        if (_swsCtx != null)    { ffmpeg.sws_freeContext(_swsCtx);                      _swsCtx    = null; }
        if (_codecCtx != null)  { var c = _codecCtx;   ffmpeg.avcodec_free_context(&c); _codecCtx  = null; }
    }
}
