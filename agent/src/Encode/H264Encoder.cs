// H264 hardware encoder via FFmpeg.AutoGen.
// Para ativar:
//   1. Adicionar <PackageReference Include="FFmpeg.AutoGen" Version="7.1.0" /> ao Agent.csproj
//   2. Colocar as DLLs FFmpeg (ffmpeg.exe, avcodec-60.dll, etc.) no PATH ou na pasta do binário
//   3. Alterar Program.cs para registrar H264Encoder em vez de JpegFrameEncoder quando ENCODER=qsv|nvenc
//   4. Remover o #pragma disable abaixo
//
// Seleção de encoder por .env:
//   ENCODER=qsv   → h264_qsv (Intel Quick Sync — preferido em laptops com iGPU Intel)
//   ENCODER=nvenc → h264_nvenc (NVIDIA — alternativa quando NVENC disponível)
//   ENCODER=jpeg  → JpegFrameEncoder (padrão, sem dependência de hardware)

#pragma warning disable CS1998 // async method without await

using SelfDesk.Agent.Capture;
using SelfDesk.Agent.Protocol;

namespace SelfDesk.Agent.Encode;

/// <summary>
/// Placeholder para o encoder H264 por hardware (Fase 4).
/// Substitua pela implementação FFmpeg.AutoGen quando as DLLs estiverem disponíveis.
/// </summary>
public sealed class H264Encoder : IFrameEncoder
{
    private readonly string _encoderName;

    public H264Encoder(string encoder = "h264_qsv")
    {
        _encoderName = encoder switch
        {
            "qsv"   => "h264_qsv",
            "nvenc" => "h264_nvenc",
            _       => encoder,
        };
        throw new NotSupportedException(
            $"H264 encoder '{_encoderName}' requer FFmpeg.AutoGen. " +
            "Adicione o pacote e as DLLs FFmpeg ao projeto (ver comentário em H264Encoder.cs).");
    }

    public EncodedFrame Encode(CapturedFrame frame) => throw new NotSupportedException();
    public void Dispose() { }
}
