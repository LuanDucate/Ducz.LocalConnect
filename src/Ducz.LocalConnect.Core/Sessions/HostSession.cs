using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ducz.LocalConnect.Core.Capture;
using Ducz.LocalConnect.Core.Clipboard;
using Ducz.LocalConnect.Core.Input;
using Ducz.LocalConnect.Core.Protocol;
using Ducz.LocalConnect.Core.Transfer;

namespace Ducz.LocalConnect.Core.Sessions;

internal sealed class HostSession : IAsyncDisposable
{
    private readonly PacketChannel _channel;
    private readonly HostOptions _options;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _lifetime;
    private readonly ScreenStreamer _streamer;
    private readonly InputInjector _input;
    private readonly FileTransferReceiver _files;

    private long _lastReceivedTicks = Stopwatch.GetTimestamp();

    public HostSession(TcpClient client, HostOptions options, CancellationToken hostToken, Action<string> log)
    {
        _channel = new PacketChannel(client);
        _options = options;
        _log = log;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        _streamer = new ScreenStreamer(options.Capture, SendFrameAsync);
        _input = new InputInjector(() => _streamer.CurrentDisplay?.Bounds.Location ?? System.Drawing.Point.Empty);
        _files = new FileTransferReceiver(options.ReceivedFilesDirectory);

        _streamer.Failed += exception => Fail($"Screen capture stopped: {exception.Message}");
        _streamer.Log += message => log(message);
        _input.Failed += exception => log($"Input rejected: {exception.Message}");
        _files.Started += (name, size) => log($"Receiving {name} ({FormatBytes(size)})…");
        _files.Completed += path => log($"File saved: {path}");
    }

    public EndPoint? RemoteEndPoint => _channel.Client.Client.RemoteEndPoint;

    /// <summary>Asks this session to end promptly (used when a newer client takes over the slot).</summary>
    public void RequestStop() => _lifetime.Cancel();

    /// <summary>Performs the handshake. Returns false (after telling the client why) if it fails.</summary>
    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        var token = handshakeTimeout.Token;

        Packet packet;
        try
        {
            packet = await PacketReader.ReadAsync(_channel.Stream, token).ConfigureAwait(false);
        }
        catch (ProtocolException exception)
        {
            await RejectAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (packet is not HandshakePacket handshake)
        {
            await RejectAsync("Expected a handshake.", cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (handshake.Version != ProtocolLimits.Version)
        {
            await RejectAsync($"Protocol version mismatch: host is v{ProtocolLimits.Version}, client is v{handshake.Version}. Update both sides.", cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!string.Equals(handshake.Pin, _options.Pin, StringComparison.Ordinal))
        {
            await RejectAsync("Invalid PIN.", cancellationToken).ConfigureAwait(false);
            return false;
        }

        await _channel.SendAsync((stream, ct) => PacketWriter.WriteHandshakeResultAsync(stream, true, "Authenticated.", ct), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RejectAsync(string reason, CancellationToken cancellationToken)
    {
        await _channel.SendAsync((stream, ct) => PacketWriter.WriteHandshakeResultAsync(stream, false, reason, ct), cancellationToken).ConfigureAwait(false);

        try
        {
            _channel.Client.Client.Shutdown(SocketShutdown.Send);

            using var drainTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drainTimeout.CancelAfter(TimeSpan.FromSeconds(1));
            var sink = new byte[1024];
            while (await _channel.Stream.ReadAsync(sink, drainTimeout.Token).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // The peer is gone or slow to close; nothing more to preserve.
        }
    }

    public async Task RunAsync()
    {
        var token = _lifetime.Token;

        await SendMonitorListAsync(token).ConfigureAwait(false);
        _streamer.Start(deviceName: null, token);

        var receive = ReceiveLoopAsync(token);
        var liveness = LivenessLoopAsync(token);

        await Task.WhenAny(receive, liveness, _streamer.Completion).ConfigureAwait(false);
        _lifetime.Cancel();

        try
        {
            await Task.WhenAll(receive, liveness).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _streamer.StopAsync().ConfigureAwait(false);
        _streamer.Dispose();
        _input.Dispose();
        _files.Dispose();
        _channel.Dispose();
        _lifetime.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var packet = await PacketReader.ReadAsync(_channel.Stream, token).ConfigureAwait(false);
                Volatile.Write(ref _lastReceivedTicks, Stopwatch.GetTimestamp());

                switch (packet)
                {
                    case InputPacket input:
                        _input.Enqueue(input.Input);
                        break;

                    case PingPacket ping:
                        await _channel.SendAsync((stream, ct) => PacketWriter.WritePongAsync(stream, ping.Timestamp, ct), token).ConfigureAwait(false);
                        break;

                    case PongPacket:
                        break;

                    case ClipboardSetPacket clipboard:
                        ClipboardService.SetText(clipboard.Text);
                        _log("Clipboard updated by the client.");
                        break;

                    case ClipboardRequestPacket:
                        var text = ClipboardService.GetText();
                        await _channel.SendAsync((stream, ct) => PacketWriter.WriteClipboardResponseAsync(stream, text, ct), token).ConfigureAwait(false);
                        break;

                    case MonitorSelectPacket select:
                        _streamer.SelectDisplay(select.DeviceName);
                        await SendMonitorListAsync(token, select.DeviceName).ConfigureAwait(false);
                        _log($"Sharing {DisplayInfo.Resolve(select.DeviceName).DeviceName}.");
                        break;

                    case FileMetadataPacket metadata:
                        _files.Begin(metadata.FileName, metadata.FileSize);
                        break;

                    case FileChunkPacket chunk:
                        await _files.WriteAsync(chunk.Content, token).ConfigureAwait(false);
                        break;

                    case QualitySelectPacket quality:
                        _streamer.SetQuality(quality.Quality);
                        _log($"Stream quality: {quality.Quality}.");
                        break;

                    case StreamControlPacket streamControl:
                        _streamer.SetActive(streamControl.Active);
                        break;

                    case SecureAttentionPacket:
                        if (SecureAttention.TrySend(out var sasError))
                        {
                            _log("Ctrl+Alt+Del requested by the client. If nothing happened, this computer's policy blocks software Ctrl+Alt+Del (see docs/troubleshooting.md).");
                        }
                        else
                        {
                            _log($"Ctrl+Alt+Del failed: {sasError}");
                        }

                        break;

                    default:
                        throw new ProtocolException($"Unexpected packet {packet.Type} from client.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or SocketException or ObjectDisposedException)
        {
            // Peer closed the connection.
        }
        catch (ProtocolException exception)
        {
            _log($"Protocol error: {exception.Message}");
        }
    }

    private async Task LivenessLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(Liveness.PingInterval, token).ConfigureAwait(false);

                var silence = Stopwatch.GetElapsedTime(Volatile.Read(ref _lastReceivedTicks));
                if (silence > Liveness.Timeout)
                {
                    _log("Client stopped responding.");
                    return;
                }

                await _channel.SendAsync((stream, ct) => PacketWriter.WritePingAsync(stream, Stopwatch.GetTimestamp(), ct), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    private ValueTask SendFrameAsync(RemoteFrame frame, CancellationToken token)
        => _channel.SendAsync((stream, ct) => PacketWriter.WriteFrameAsync(stream, frame, ct), token);

    private ValueTask SendMonitorListAsync(CancellationToken token, string? selectedDeviceName = null)
    {
        var selected = DisplayInfo.Resolve(selectedDeviceName ?? _streamer.CurrentDisplay?.DeviceName);
        var monitors = DisplayInfo.GetAll()
            .Select((display, index) => new RemoteMonitorInfo(
                display.DeviceName,
                display.BuildDisplayName(index),
                display.IsPrimary,
                string.Equals(display.DeviceName, selected.DeviceName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return _channel.SendAsync((stream, ct) => PacketWriter.WriteMonitorListAsync(stream, monitors, ct), token);
    }

    private void Fail(string message)
    {
        _log(message);
        _lifetime.Cancel();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes / (1024d * 1024d):0.#} MB",
    };
}
