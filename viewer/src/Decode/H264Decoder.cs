// H264 decoder via FFmpeg.AutoGen.
// Para ativar:
//   1. Adicionar <PackageReference Include="FFmpeg.AutoGen" Version="7.1.0" /> ao Viewer.csproj
//   2. Colocar as DLLs FFmpeg no PATH ou pasta do binário
//   3. Registrar H264Decoder em vez de JpegFrameDecoder em App.xaml.cs quando ENCODER=qsv|nvenc
//
// Tratamento de keyframe:
//   Quando FLAGS bit0 (FlagKeyframe) = 1, forçar seek para I-frame no decoder.
//   Frames P/B sem keyframe anterior devem ser descartados até o próximo keyframe.

namespace SelfDesk.Viewer.Decode;

/// <summary>
/// Placeholder para o decoder H264 (Fase 4).
/// Substitua pela implementação FFmpeg.AutoGen quando disponível.
/// </summary>
public sealed class H264Decoder : IFrameDecoder
{
    public H264Decoder() =>
        throw new NotSupportedException(
            "H264Decoder requer FFmpeg.AutoGen. Ver comentário em H264Decoder.cs.");

    public DecodedFrame Decode(ReadOnlySpan<byte> data, int width, int height, long timestampMs) =>
        throw new NotSupportedException();
}
