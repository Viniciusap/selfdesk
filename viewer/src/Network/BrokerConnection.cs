using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using ViewerWire = SelfDesk.Viewer.Protocol.WireProtocol;

namespace SelfDesk.Viewer.Network;

public sealed class BrokerConnection : IAsyncDisposable
{
    private readonly ViewerConfig _cfg;
    private readonly ILogger _log;
    private TcpClient? _tcp;
    private SslStream? _ssl;

    private readonly byte[] _headerBuf = new byte[ProtocolSizes.HeaderSize];
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Action<byte, string, ReadOnlyMemory<byte>>? MessageReceived;

    public BrokerConnection(ViewerConfig cfg, ILogger log)
    {
        _cfg = cfg;
        _log = log;
    }

    public async Task ConnectAndAuthAsync(CancellationToken ct)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_cfg.BrokerHost, _cfg.BrokerPort, ct);

        X509Certificate2Collection? caBundle = null;
        if (!string.IsNullOrEmpty(_cfg.TlsCaPath) && File.Exists(_cfg.TlsCaPath))
            caBundle = [X509Certificate2.CreateFromPem(File.ReadAllText(_cfg.TlsCaPath))];

        if (caBundle is null)
            throw new InvalidOperationException(
                "TLS_CA_PATH não configurado ou arquivo não encontrado — CA pinning obrigatório. " +
                "Execute bootstrap.ps1 -Role receiver para gerar o .env correto.");

        _ssl = new SslStream(_tcp.GetStream(), false, (_, cert, _, _) =>
        {
            if (cert is null) return false;
            var serverCert = new X509Certificate2(cert);
            return caBundle.Any(ca => IsSignedBy(serverCert, ca));
        });

        await _ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = _cfg.BrokerHost,
        }, ct);

        await SendRawAsync(ViewerWire.BuildHello("receiver", "receiver"), ct);

        var (type, _, _) = await ReadHeaderAsync(ct);
        if (type != MessageType.Challenge)
            throw new InvalidDataException($"Esperava CHALLENGE, recebeu 0x{type:X2}");

        var nonce = await ReadPayloadAsync(ProtocolSizes.NonceSize, ct);
        await SendRawAsync(WireProtocol.BuildAuth(nonce, _cfg.SharedSecret), ct);

        var (authType, _, _) = await ReadHeaderAsync(ct);
        if (authType == MessageType.AuthFail)
            throw new UnauthorizedAccessException("AUTH_FAIL: segredo incorreto");
        if (authType != MessageType.AuthOk)
            throw new InvalidDataException($"Esperava AUTH_OK, recebeu 0x{authType:X2}");

        _log.LogInformation("Viewer autenticado no broker");
    }

    public async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (type, peerId, length) = await ReadHeaderAsync(ct);
            var payload = length > 0 ? await ReadPayloadAsync((int)length, ct) : Array.Empty<byte>();

            switch (type)
            {
                case MessageType.Ping:
                    await SendRawAsync(WireProtocol.BuildPong(payload), ct);
                    break;
                case MessageType.Pong:
                    break;
                case MessageType.Bye:
                    _log.LogInformation("BYE recebido");
                    return;
                default:
                    MessageReceived?.Invoke(type, peerId, payload.AsMemory());
                    break;
            }
        }
    }

    public async Task StartHeartbeatAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
            await SendRawAsync(WireProtocol.BuildPing(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);
    }

    public async Task SendAsync(byte[] message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try { await _ssl!.WriteAsync(message, ct); }
        finally { _writeLock.Release(); }
    }

    private async Task<(byte type, string peerId, uint length)> ReadHeaderAsync(CancellationToken ct)
    {
        await ReadExactAsync(_headerBuf, ct);
        return WireProtocol.ParseHeader(_headerBuf);
    }

    private const int MaxPayloadBytes = 10 * 1024 * 1024; // 10 MB

    private async Task<byte[]> ReadPayloadAsync(int length, CancellationToken ct)
    {
        if (length > MaxPayloadBytes)
            throw new InvalidDataException($"Payload de {length} bytes excede limite de {MaxPayloadBytes} bytes");
        var buf = new byte[length];
        await ReadExactAsync(buf, ct);
        return buf;
    }

    private async Task ReadExactAsync(byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buf.Length)
        {
            var read = await _ssl!.ReadAsync(buf.AsMemory(offset), ct);
            if (read == 0) throw new EndOfStreamException("Conexão encerrada pelo broker");
            offset += read;
        }
    }

    private async Task SendRawAsync(byte[] data, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try { await _ssl!.WriteAsync(data, ct); }
        finally { _writeLock.Release(); }
    }

    private static bool IsSignedBy(X509Certificate2 cert, X509Certificate2 ca)
    {
        try
        {
            var chain = new X509Chain();
            chain.ChainPolicy.TrustMode           = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode      = X509RevocationMode.NoCheck;
            return chain.Build(cert);
        }
        catch { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ssl is not null) await _ssl.DisposeAsync();
        _tcp?.Dispose();
        _writeLock.Dispose();
    }
}
