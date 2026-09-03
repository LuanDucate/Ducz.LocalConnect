using System.Diagnostics;
using System.Net.Sockets;
using Ducz.LocalConnect.Core.Audio;
using Ducz.LocalConnect.Core.Clipboard;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Sessions;

public sealed class ClientSession : IAsyncDisposable
{
    private const int FileChunkSize = 256 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly AudioStreamClient _audio = new();

    private PacketChannel? _channel;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private Task? _livenessLoop;
    private Task? _statsLoop;

    private long _lastReceivedTicks;
    private long _framesThisSecond;
    private long _bytesThisSecond;
    private double _latencyMilliseconds;

    public ClientSession()
    {
        _audio.Log += message => Log?.Invoke($"Audio: {message}");
    }

    public event Action<RemoteFrame>? FrameReceived;
    public event Action<IReadOnlyList<RemoteMonitorInfo>>? MonitorsChanged;
    public event Action<SessionStats>? StatsUpdated;
    public event Action<string>? ClipboardReceived;
    public event Action<string>? Log;

    /// <summary>Raised once when the session ends for any reason other than <see cref="DisconnectAsync"/>.</summary>
    public event Action<string>? Disconnected;

    public bool IsConnected => _channel is not null;

    public IReadOnlyList<RemoteMonitorInfo> Monitors { get; private set; } = Array.Empty<RemoteMonitorInfo>();

    public bool AudioMuted
    {
        get => _audio.Muted;
        set => _audio.Muted = value;
    }

    public async Task ConnectAsync(ClientOptions options, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        options.Validate();

        var client = new TcpClient { NoDelay = true };
        PacketChannel? channel = null;
        try
        {
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeout.CancelAfter(ConnectTimeout);
                try
                {
                    await client.ConnectAsync(options.Host, options.Port, connectTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"No host answered at {options.Host}:{options.Port}. Check the address, the port and the firewall on the host.");
                }
            }

            channel = new PacketChannel(client);
            await channel.SendAsync((stream, ct) => PacketWriter.WriteHandshakeAsync(stream, options.Pin.Trim(), ct), cancellationToken).ConfigureAwait(false);

            var result = await PacketReader.ReadAsync(channel.Stream, cancellationToken).ConfigureAwait(false);
            if (result is not HandshakeResultPacket handshake)
            {
                throw new ProtocolException("The host did not answer the handshake.");
            }

            if (!handshake.Success)
            {
                throw new UnauthorizedAccessException(handshake.Message);
            }

            // Server may still turn us away (another client is active) right after authenticating.
            var next = await PacketReader.ReadAsync(channel.Stream, cancellationToken).ConfigureAwait(false);
            Monitors = next switch
            {
                MonitorListPacket list => list.Monitors,
                HandshakeResultPacket rejected => throw new InvalidOperationException(rejected.Message),
                _ => throw new ProtocolException("The host did not send its monitor list."),
            };

            try
            {
                await _audio.ConnectAsync(options.Host, options.Port + ProtocolLimits.AudioPortOffset, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Audio is a nice-to-have; a blocked audio port shouldn't kill the session.
                Log?.Invoke($"Audio unavailable: {exception.Message}");
            }

            var lifetime = new CancellationTokenSource();
            _channel = channel;
            _lifetime = lifetime;
            _lastReceivedTicks = Stopwatch.GetTimestamp();
            _receiveLoop = ReceiveLoopAsync(channel, lifetime.Token);
            _livenessLoop = LivenessLoopAsync(channel, lifetime.Token);
            _statsLoop = StatsLoopAsync(lifetime.Token);
        }
        catch
        {
            channel?.Dispose();
            if (channel is null)
            {
                client.Dispose();
            }

            await _audio.DisconnectAsync().ConfigureAwait(false);
            throw;
        }

        Log?.Invoke($"Connected to {options.Host}:{options.Port}.");
        MonitorsChanged?.Invoke(Monitors);
    }

    public Task DisconnectAsync() => TeardownAsync(reason: null);

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    public ValueTask SendMouseMoveAsync(int x, int y)
    {
        var channel = _channel;
        var token = _lifetime?.Token ?? CancellationToken.None;
        if (channel is null)
        {
            return ValueTask.CompletedTask;
        }

        // Drop the sample if a send is in flight; the next move will carry the newer position.
        return new ValueTask(channel.TrySendAsync((stream, ct) => PacketWriter.WriteMouseMoveAsync(stream, x, y, ct), token).AsTask());
    }

    public ValueTask SendMouseButtonAsync(RemoteMouseButton button, bool isDown, int x, int y)
        => SendAsync((stream, ct) => PacketWriter.WriteMouseButtonAsync(stream, button, isDown, x, y, ct));

    public ValueTask SendMouseWheelAsync(int x, int y, int delta)
        => SendAsync((stream, ct) => PacketWriter.WriteMouseWheelAsync(stream, x, y, delta, ct));

    public ValueTask SendKeyAsync(int virtualKey, bool isDown)
        => SendAsync((stream, ct) => PacketWriter.WriteKeyAsync(stream, virtualKey, isDown, ct));

    public ValueTask SendClipboardTextAsync(string text)
        => SendAsync((stream, ct) => PacketWriter.WriteClipboardSetAsync(stream, text, ct));

    public ValueTask RequestClipboardAsync()
        => SendAsync((stream, ct) => PacketWriter.WriteClipboardRequestAsync(stream, ct));

    public ValueTask SelectMonitorAsync(string deviceName)
        => SendAsync((stream, ct) => PacketWriter.WriteMonitorSelectAsync(stream, deviceName, ct));

    public ValueTask SelectQualityAsync(StreamQuality quality)
        => SendAsync((stream, ct) => PacketWriter.WriteQualitySelectAsync(stream, quality, ct));

    /// <summary>Asks the host to press Ctrl+Alt+Del on itself. Whether Windows obeys depends on the host's policy.</summary>
    public ValueTask SendSecureAttentionAsync()
        => SendAsync(PacketWriter.WriteSecureAttentionAsync);

    /// <summary>Tells the host to pause (false) or resume (true) the screen stream for this session.</summary>
    public ValueTask SetStreamingAsync(bool active)
        => SendAsync((stream, ct) => PacketWriter.WriteStreamControlAsync(stream, active, ct));

    /// <summary>Streams a file to the host. <paramref name="progress"/> receives 0–1.</summary>
    public async Task SendFileAsync(string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var channel = _channel;
        var lifetime = _lifetime;
        if (channel is null || lifetime is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("File not found.", path);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var token = linked.Token;

        // Hold the channel for the whole transfer so chunks stay contiguous; input is
        // paused meanwhile, which is the expected trade-off for a manual "send file".
        await channel.SendAsync(async (stream, ct) =>
        {
            await PacketWriter.WriteFileMetadataAsync(stream, info.Name, info.Length, ct).ConfigureAwait(false);

            await using var file = info.OpenRead();
            var buffer = new byte[FileChunkSize];
            long sent = 0;
            int read;
            while ((read = await file.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await PacketWriter.WriteFileChunkAsync(stream, buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                sent += read;
                progress?.Report(info.Length == 0 ? 1 : (double)sent / info.Length);
            }
        }, token).ConfigureAwait(false);

        Log?.Invoke($"File sent: {info.Name}");
    }

    private ValueTask SendAsync(Func<Stream, CancellationToken, ValueTask> write)
    {
        var channel = _channel;
        var lifetime = _lifetime;
        if (channel is null || lifetime is null)
        {
            return ValueTask.CompletedTask;
        }

        return channel.SendAsync(write, lifetime.Token);
    }

    private async Task ReceiveLoopAsync(PacketChannel channel, CancellationToken token)
    {
        string reason;
        try
        {
            while (true)
            {
                var packet = await PacketReader.ReadAsync(channel.Stream, token).ConfigureAwait(false);
                Volatile.Write(ref _lastReceivedTicks, Stopwatch.GetTimestamp());

                switch (packet)
                {
                    case FramePacket frame:
                        Interlocked.Increment(ref _framesThisSecond);
                        Interlocked.Add(ref _bytesThisSecond, frame.Frame.ImageBytes.Length);
                        FrameReceived?.Invoke(frame.Frame);
                        break;

                    case PingPacket ping:
                        await channel.SendAsync((stream, ct) => PacketWriter.WritePongAsync(stream, ping.Timestamp, ct), token).ConfigureAwait(false);
                        break;

                    case PongPacket pong:
                        _latencyMilliseconds = Stopwatch.GetElapsedTime(pong.Timestamp).TotalMilliseconds;
                        break;

                    case ClipboardResponsePacket clipboard:
                        ClipboardService.SetText(clipboard.Text);
                        ClipboardReceived?.Invoke(clipboard.Text);
                        break;

                    case MonitorListPacket monitors:
                        Monitors = monitors.Monitors;
                        MonitorsChanged?.Invoke(Monitors);
                        break;

                    case HandshakeResultPacket rejected when !rejected.Success:
                        throw new InvalidOperationException(rejected.Message);

                    default:
                        throw new ProtocolException($"Unexpected packet {packet.Type} from host.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or SocketException or ObjectDisposedException)
        {
            reason = "The host closed the connection.";
        }
        catch (Exception exception)
        {
            reason = exception.Message;
        }

        await TeardownAsync(reason).ConfigureAwait(false);
    }

    private async Task LivenessLoopAsync(PacketChannel channel, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(Liveness.PingInterval, token).ConfigureAwait(false);

                if (Stopwatch.GetElapsedTime(Volatile.Read(ref _lastReceivedTicks)) > Liveness.Timeout)
                {
                    await TeardownAsync("The host stopped responding.").ConfigureAwait(false);
                    return;
                }

                await channel.SendAsync((stream, ct) => PacketWriter.WritePingAsync(stream, Stopwatch.GetTimestamp(), ct), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task StatsLoopAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                var frames = Interlocked.Exchange(ref _framesThisSecond, 0);
                var bytes = Interlocked.Exchange(ref _bytesThisSecond, 0);
                StatsUpdated?.Invoke(new SessionStats(frames, bytes / 1024d, _latencyMilliseconds));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TeardownAsync(string? reason)
    {
        var channel = Interlocked.Exchange(ref _channel, null);
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        if (channel is null || lifetime is null)
        {
            return;
        }

        lifetime.Cancel();
        channel.Client.Close();
        await _audio.DisconnectAsync().ConfigureAwait(false);

        var loops = new[] { _receiveLoop, _livenessLoop, _statsLoop };
        _receiveLoop = null;
        _livenessLoop = null;
        _statsLoop = null;

        if (reason is null)
        {
            foreach (var loop in loops)
            {
                if (loop is null)
                {
                    continue;
                }

                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        channel.Dispose();
        lifetime.Dispose();
        Monitors = Array.Empty<RemoteMonitorInfo>();

        Log?.Invoke(reason ?? "Disconnected.");
        MonitorsChanged?.Invoke(Monitors);
        if (reason is not null)
        {
            Disconnected?.Invoke(reason);
        }
    }
}
