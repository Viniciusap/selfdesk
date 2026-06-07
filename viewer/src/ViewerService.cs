using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Concentus;
using Concentus.Structs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Viewer.Audio;
using SelfDesk.Viewer.Decode;
using SelfDesk.Viewer.Network;
using ViewerWire = SelfDesk.Viewer.Protocol.WireProtocol;
using SelfDesk.Viewer.ViewModels;
using SelfDesk.Viewer.Views;

namespace SelfDesk.Viewer;

public sealed class ViewerService : BackgroundService
{
    private readonly ViewerConfig            _cfg;
    private readonly MainWindowViewModel     _vm;
    private readonly MainWindow              _window;
    private readonly ILogger<ViewerService>  _log;
    private readonly IFrameDecoder           _decoder;
    private readonly IAudioPlayer            _audioPlayer;
    private          IOpusDecoder?            _audioDecoder;
    private const int MaxClipboardBytes = 1 * 1024 * 1024; // 1 MB
    private volatile string                   _lastClipboard = string.Empty;

    public ViewerService(
        IOptions<ViewerConfig> cfg,
        MainWindowViewModel vm,
        MainWindow window,
        IFrameDecoder decoder,
        IAudioPlayer audioPlayer,
        ILogger<ViewerService> log)
    {
        _cfg         = cfg.Value;
        _vm          = vm;
        _window      = window;
        _decoder     = decoder;
        _audioPlayer = audioPlayer;
        _log         = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(2);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ct);
                retryDelay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Session ended. Reconnecting in {Delay}s", retryDelay.TotalSeconds);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _vm.IsConnected      = false;
                    _vm.ConnectionStatus = $"Reconnecting in {retryDelay.TotalSeconds:0}s…";
                    _vm.Senders.Clear();
                });
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        await using var conn = new BrokerConnection(_cfg, _log);

        void WireMonitorCallback(SenderViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SenderViewModel.SelectedMonitor) &&
                    vm.SelectedMonitor is not null)
                {
                    _ = conn.SendAsync(
                        ViewerWire.BuildSelectMonitor(vm.AgentId, vm.SelectedMonitor.Index), ct);
                }
            };
        }

        _vm.Senders.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (SenderViewModel vm in e.NewItems) WireMonitorCallback(vm);
        };
        foreach (var existing in _vm.Senders) WireMonitorCallback(existing);

        conn.MessageReceived += (type, peerId, payload) =>
        {
            if (type == MessageType.Clipboard)
            {
                var text = Encoding.UTF8.GetString(payload.Span);
                if (text == _lastClipboard) return;
                _lastClipboard = text;
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try { System.Windows.Clipboard.SetText(text); }
                    catch { }
                });
                return;
            }
            if (type == MessageType.SenderUp)
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(payload.ToArray());
                    if (!doc.RootElement.TryGetProperty("agentId", out var agentEl)) return;
                    var displayId = agentEl.GetString() ?? peerId;
                    if (displayId.Length > 64) { _log.LogWarning("SENDER_UP agentId too long — ignored"); return; }
                    var mac = doc.RootElement.TryGetProperty("mac", out var macEl) ? macEl.GetString() : null;
                    var ver = doc.RootElement.TryGetProperty("version", out var verEl) ? verEl.GetString() : null;
                    Application.Current?.Dispatcher.InvokeAsync(() => _vm.AddSender(displayId, mac, ver));
                    // S63: use envelope peerId for routing, JSON agentId only for display
                    _ = conn.SendAsync(ViewerWire.BuildRequestIdr(peerId), ct);
                }
                catch (Exception ex) { _log.LogWarning(ex, "Malformed SENDER_UP payload"); }
                return;
            }
            if (type == MessageType.MonitorList)
            {
                try
                {
                    var monitors = System.Text.Json.JsonDocument.Parse(payload.ToArray())
                        .RootElement.EnumerateArray()
                        .Take(16) // S61: cap monitor list to prevent UI freeze
                        .Select(el => new ViewModels.MonitorViewModel
                        {
                            Index     = el.GetProperty("index").GetInt32(),
                            Name      = el.GetProperty("name").GetString() ?? $"Monitor {el.GetProperty("index").GetInt32() + 1}",
                            IsPrimary = el.GetProperty("isPrimary").GetBoolean(),
                        })
                        .ToList();

                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        var sender = _vm.Senders.FirstOrDefault(s => s.AgentId == peerId);
                        if (sender is null) return;
                        sender.Monitors.Clear();
                        foreach (var m in monitors) sender.Monitors.Add(m);
                        sender.SelectedMonitor ??= sender.Monitors.FirstOrDefault(m => m.IsPrimary)
                                                   ?? sender.Monitors.FirstOrDefault();
                    });
                }
                catch (Exception ex) { _log.LogWarning(ex, "Failed to process MONITOR_LIST"); }
                return;
            }
            OnMessage(type, peerId, payload);
        };

        await conn.ConnectAndAuthAsync(ct);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _vm.IsConnected      = true;
            _vm.ConnectionStatus = "Connected";
        });

        void InputHandler(byte[] msg)   => _ = conn.SendAsync(msg, ct);
        void FileHandler(string[] files) => _ = SendFilesAsync(files, conn, ct);
        _window.InputSend   += InputHandler;
        _window.FileDropped += FileHandler;

        var heartbeat = conn.StartHeartbeatAsync(ct);
        var recvLoop = conn.RunReceiveLoopAsync(ct);

        var clipboardLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(ct))
            {
                string? text = null;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try { text = System.Windows.Clipboard.ContainsText()
                        ? System.Windows.Clipboard.GetText() : null; }
                    catch { text = null; }
                });
                if (text is null || text == _lastClipboard) continue;
                _lastClipboard = text;
                var targetId = _vm.SelectedSender?.AgentId;
                if (string.IsNullOrEmpty(targetId)) continue;
                var clipBytes = Encoding.UTF8.GetBytes(text);
                if (clipBytes.Length > MaxClipboardBytes) continue;
                var msg = WireProtocol.BuildClipboard(clipBytes, targetId);
                try { await conn.SendAsync(msg, ct); }
                catch { break; }
            }
        }, ct);

        try
        {
            await Task.WhenAny(recvLoop, clipboardLoop, heartbeat);
        }
        finally
        {
            _window.InputSend   -= InputHandler;
            _window.FileDropped -= FileHandler;
        }
    }

    private uint _transferIdCounter;
    private const int ChunkSize = 512 * 1024;

    private async Task SendFilesAsync(string[] paths, BrokerConnection conn, CancellationToken ct)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var targetId = _vm.SelectedSender?.AgentId ?? string.Empty;
            if (string.IsNullOrEmpty(targetId)) continue;

            var id       = Interlocked.Increment(ref _transferIdCounter);
            var fileName = Path.GetFileName(path);
            var info     = new FileInfo(path);
            long total   = info.Length;
            long sent    = 0;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _vm.TransferProgress = 0;
                _vm.TransferStatus   = $"Enviando {fileName}…";
                _vm.NotifyStatusBarChanged();
            });

            try
            {
                await conn.SendAsync(ViewerWire.BuildFileHeader(id, total, fileName, targetId), ct);

                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buf = new byte[ChunkSize];
                int read;
                while ((read = await fs.ReadAsync(buf.AsMemory(0, ChunkSize), ct)) > 0)
                {
                    await conn.SendAsync(ViewerWire.BuildFileChunk(id, buf.AsSpan(0, read), targetId), ct);
                    sent += read;
                    var pct = (int)(sent * 100 / total);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _vm.TransferProgress = pct;
                        _vm.TransferStatus   = $"Enviando {fileName} — {pct}%";
                        _vm.NotifyStatusBarChanged();
                    });
                }

                await conn.SendAsync(ViewerWire.BuildFileDone(id, targetId), ct);
                _log.LogInformation("Arquivo enviado: {Name}", fileName);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to send {Name}", fileName);
                try { await conn.SendAsync(ViewerWire.BuildFileError(id, targetId), ct); } catch { }
            }
            finally
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _vm.TransferProgress = -1;
                    _vm.TransferStatus   = string.Empty;
                    _vm.NotifyStatusBarChanged();
                });
            }
        }
    }

    private void OnMessage(byte type, string peerId, ReadOnlyMemory<byte> payload)
    {
        switch (type)
        {
            case MessageType.SenderDown:
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(payload.ToArray());
                    if (!doc.RootElement.TryGetProperty("agentId", out var agentEl)) break;
                    var id = agentEl.GetString() ?? peerId;
                    Application.Current?.Dispatcher.Invoke(() => _vm.RemoveSender(id));
                }
                catch (Exception ex) { _log.LogWarning(ex, "Malformed SENDER_DOWN payload"); }
                break;
            }
            case MessageType.VideoFrame:
                ProcessVideoFrame(peerId, payload);
                break;
            case MessageType.AudioFrame:
                ProcessAudioFrame(payload);
                break;
        }
    }

    private void ProcessAudioFrame(ReadOnlyMemory<byte> payload)
    {
        const int HeaderSize    = 12;
        const int FrameSamples  = 960;
        const int Channels      = 2;

        if (payload.Length <= HeaderSize) return;
        var opusData = payload.Span[HeaderSize..];

        try
        {
            _audioDecoder ??= OpusCodecFactory.CreateDecoder(48000, 2);
            var pcm = new short[FrameSamples * Channels];
            _audioDecoder.Decode(opusData, pcm.AsSpan(), FrameSamples, false);
            _audioPlayer.AddSamples(pcm);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to decode audio frame"); }
    }

    private void ProcessVideoFrame(string senderId, ReadOnlyMemory<byte> payload)
    {
        var span  = payload.Span;
        if (span.Length < VideoFrameOffsets.Data) return;

        var ts     = BinaryPrimitives.ReadInt64BigEndian(span[VideoFrameOffsets.Timestamp..]);
        var width  = (int)BinaryPrimitives.ReadUInt16BigEndian(span[VideoFrameOffsets.Width..]);
        var height = (int)BinaryPrimitives.ReadUInt16BigEndian(span[VideoFrameOffsets.Height..]);
        var codec  = span[VideoFrameOffsets.Codec];
        var data   = span[VideoFrameOffsets.Data..];

        if (codec != VideoFrameOffsets.CodecJpeg && codec != VideoFrameOffsets.CodecH264) return;

        DecodedFrame decoded;
        try { decoded = _decoder.Decode(data, width, height, ts); }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to decode frame"); return; }

        var rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts;

        // BUG3: InvokeAsync decouples the receive loop from the UI thread — no stutter waiting for blit
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var sender = _vm.Senders.FirstOrDefault(s => s.AgentId == senderId);
            if (sender is not null)
            {
                sender.LastRttMs   = rtt;
                sender.FrameWidth  = decoded.Width;
                sender.FrameHeight = decoded.Height;
                sender.Codec       = codec == VideoFrameOffsets.CodecH264 ? "H264" : "JPEG";
                _vm.NotifyStatusBarChanged();
            }

            if (_vm.SelectedSender?.AgentId != senderId) return;

            if (_vm.VideoFrame is null ||
                _vm.VideoFrame.PixelWidth  != decoded.Width ||
                _vm.VideoFrame.PixelHeight != decoded.Height)
            {
                var src  = PresentationSource.FromVisual(_window);
                var dpiX = src is not null ? 96.0 * src.CompositionTarget.TransformToDevice.M11 : 96.0;
                var dpiY = src is not null ? 96.0 * src.CompositionTarget.TransformToDevice.M22 : 96.0;
                _vm.VideoFrame = new WriteableBitmap(
                    decoded.Width, decoded.Height, dpiX, dpiY, PixelFormats.Bgra32, null);
            }

            _vm.VideoFrame.Lock();
            _vm.VideoFrame.WritePixels(
                new Int32Rect(0, 0, decoded.Width, decoded.Height),
                decoded.Bgra, decoded.Width * 4, 0);
            _vm.VideoFrame.AddDirtyRect(new Int32Rect(0, 0, decoded.Width, decoded.Height));
            _vm.VideoFrame.Unlock();
        });
    }
}
