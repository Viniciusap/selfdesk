using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace SelfDesk.Viewer.Decode;

public sealed unsafe class H264Decoder : IFrameDecoder, IDisposable
{
    private AVCodecContext* _codecCtx;
    private AVFrame*        _hwFrame;   // output from decoder (may be CUDA surface)
    private AVFrame*        _swFrame;   // CPU copy (NV12 or YUV420P)
    private AVFrame*        _bgraFrame;
    private SwsContext*     _swsCtx;
    private AVPacket*       _pkt;
    private int  _swsWidth;
    private int  _swsHeight;
    private bool _seenKeyframe;
    private bool _hwDecode;

    private const int AvErrorEagain = -11;
    private const int AvErrorEof    = unchecked((int)0xDFB9B0BB);
    private const int InputPadding  = 64;

    public H264Decoder()
    {
        ffmpeg.RootPath = AppContext.BaseDirectory;

        // Try NVDEC first; automatic fallback to software decoder
        AVCodec* codec = ffmpeg.avcodec_find_decoder_by_name("h264_cuvid");
        bool triedHw   = codec != null;

        if (codec == null)
            codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);

        if (codec == null)
            throw new InvalidOperationException("Nenhum decoder H264 encontrado.");

        _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecCtx == null)
            throw new InvalidOperationException("Failed to allocate AVCodecContext.");

        int ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
        if (ret < 0 && triedHw)
        {
            // h264_cuvid failed (driver/GPU unavailable) — fall back to software
            var ctx = _codecCtx;
            ffmpeg.avcodec_free_context(&ctx);
            _codecCtx = null;
            codec     = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
            ret       = ffmpeg.avcodec_open2(_codecCtx, codec, null);
            triedHw   = false;
        }
        if (ret < 0)
            throw new InvalidOperationException($"Failed to open H264 decoder (code {ret}).");

        _hwDecode  = triedHw;
        _hwFrame   = ffmpeg.av_frame_alloc();
        _swFrame   = ffmpeg.av_frame_alloc();
        _bgraFrame = ffmpeg.av_frame_alloc();
        _pkt       = ffmpeg.av_packet_alloc();
    }

    public DecodedFrame Decode(ReadOnlySpan<byte> data, int width, int height, long timestampMs)
    {
        if (data.IsEmpty)
            throw new InvalidOperationException("NAL data vazio.");

        bool isKeyframe = ContainsIdr(data);
        if (isKeyframe)
        {
            ffmpeg.avcodec_flush_buffers(_codecCtx);
            _seenKeyframe = true;
        }
        if (!_seenKeyframe)
            throw new InvalidOperationException("Waiting for H264 keyframe (IDR).");

        var padded = new byte[data.Length + InputPadding];
        data.CopyTo(padded);

        fixed (byte* dataPtr = padded)
        {
            _pkt->data = dataPtr;
            _pkt->size = data.Length;

            int ret = ffmpeg.avcodec_send_packet(_codecCtx, _pkt);
            if (ret < 0)
                throw new InvalidOperationException($"avcodec_send_packet error: {ret}");

            ret = ffmpeg.avcodec_receive_frame(_codecCtx, _hwFrame);
            if (ret == AvErrorEagain) throw new InvalidOperationException("Decoder buffering.");
            if (ret == AvErrorEof)   throw new InvalidOperationException("Decoder EOF.");
            if (ret < 0)             throw new InvalidOperationException($"avcodec_receive_frame error: {ret}");
        }

        // If cuvid returned a CUDA surface, transfer to CPU (NV12)
        AVFrame* decodeFrame;
        if (_hwDecode && _hwFrame->format == (int)AVPixelFormat.AV_PIX_FMT_CUDA)
        {
            ffmpeg.av_frame_unref(_swFrame);
            int xfer = ffmpeg.av_hwframe_transfer_data(_swFrame, _hwFrame, 0);
            if (xfer < 0)
                throw new InvalidOperationException($"av_hwframe_transfer_data falhou: {xfer}");
            decodeFrame = _swFrame;
        }
        else
        {
            decodeFrame = _hwFrame;
        }

        int actualW = decodeFrame->width;
        int actualH = decodeFrame->height;

        if (_swsCtx == null || _swsWidth != actualW || _swsHeight != actualH)
        {
            if (_swsCtx != null) ffmpeg.sws_freeContext(_swsCtx);

            _swsCtx = ffmpeg.sws_getContext(
                actualW, actualH, (AVPixelFormat)decodeFrame->format,
                actualW, actualH, AVPixelFormat.AV_PIX_FMT_BGRA,
                ffmpeg.SWS_BILINEAR, null, null, null);

            if (_swsCtx == null)
                throw new InvalidOperationException("Falha ao criar SwsContext.");

            ffmpeg.av_frame_unref(_bgraFrame);
            _bgraFrame->width  = actualW;
            _bgraFrame->height = actualH;
            _bgraFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            ffmpeg.av_frame_get_buffer(_bgraFrame, 0);

            _swsWidth  = actualW;
            _swsHeight = actualH;
        }

        var srcSlice  = new byte*[8];
        var srcStride = new int[8];
        for (uint i = 0; i < 8; i++)
        {
            srcSlice[i]  = decodeFrame->data[i];
            srcStride[i] = decodeFrame->linesize[i];
        }
        byte*[] dstSlice  = { _bgraFrame->data[0u] };
        int[]   dstStride = { _bgraFrame->linesize[0u] };

        ffmpeg.sws_scale(_swsCtx, srcSlice, srcStride, 0, actualH, dstSlice, dstStride);

        int bgraSize = actualW * actualH * 4;
        var bgra = new byte[bgraSize];
        fixed (byte* bgraPtr = bgra)
            Buffer.MemoryCopy(_bgraFrame->data[0], bgraPtr, bgraSize, bgraSize);

        ffmpeg.av_frame_unref(_hwFrame);

        return new DecodedFrame(actualW, actualH, bgra, timestampMs);
    }

    private static bool ContainsIdr(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
            {
                if (i + 4 < data.Length && (data[i + 4] & 0x1F) == 5) return true;
                i += 3;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_pkt       != null) { var p = _pkt;       ffmpeg.av_packet_free(&p);       _pkt       = null; }
        if (_hwFrame   != null) { var f = _hwFrame;   ffmpeg.av_frame_free(&f);        _hwFrame   = null; }
        if (_swFrame   != null) { var f = _swFrame;   ffmpeg.av_frame_free(&f);        _swFrame   = null; }
        if (_bgraFrame != null) { var f = _bgraFrame; ffmpeg.av_frame_free(&f);        _bgraFrame = null; }
        if (_swsCtx    != null) { ffmpeg.sws_freeContext(_swsCtx);                     _swsCtx    = null; }
        if (_codecCtx  != null) { var c = _codecCtx; ffmpeg.avcodec_free_context(&c); _codecCtx  = null; }
    }
}
