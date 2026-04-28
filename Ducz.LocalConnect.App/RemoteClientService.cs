using System.Net.Sockets;

namespace Ducz.LocalConnect.App;

internal sealed class RemoteClientService
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly RemoteAudioClientService _audioService = new();
    private const int AudioPortOffset = 1;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _receiveLoopTask;

    public bool IsConnected => _client?.Connected == true && _stream is not null;

    public event Action<string>? StatusChanged;

    public event Action<RemoteFrame>? FrameReceived;

    public event Action<bool>? ConnectionChanged;

    public async Task ConnectAsync(string host, int port, string pin)
    {
        if (IsConnected)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Enter the host IP or host name.");
        }

        var client = new TcpClient
        {
            NoDelay = true
        };

        await client.ConnectAsync(host, port);
        var stream = client.GetStream();
        await RemoteProtocol.WriteAuthRequestAsync(stream, pin.Trim(), CancellationToken.None);

        var authPacket = await RemoteProtocol.ReadServerPacketAsync(stream, CancellationToken.None);
        if (authPacket is not AuthResultServerPacket authResult || !authResult.Success)
        {
            client.Dispose();
            throw new InvalidOperationException(authPacket is AuthResultServerPacket result ? result.Message : "Authentication failed.");
        }

        try
        {
            await _audioService.ConnectAsync(host, port + AudioPortOffset);

            _client = client;
            _stream = stream;
            _lifetimeCancellation = new CancellationTokenSource();
            _receiveLoopTask = ReceiveServerPacketsLoopAsync(_stream, _lifetimeCancellation.Token);
        }
        catch
        {
            client.Dispose();
            await _audioService.DisconnectAsync();
            throw;
        }

        StatusChanged?.Invoke($"Connected to {host}:{port}, audio on port {port + AudioPortOffset}.");
        ConnectionChanged?.Invoke(true);
    }

    public Task DisconnectAsync()
    {
        return DisconnectAsync(waitForReceiveLoop: true);
    }

    public Task SendMouseMoveAsync(int x, int y)
    {
        return SendAsync((stream, cancellationToken) => RemoteProtocol.WriteMouseMoveAsync(stream, x, y, cancellationToken));
    }

    public Task SendMouseButtonAsync(int x, int y, RemoteMouseButton button, bool isDown)
    {
        return SendAsync((stream, cancellationToken) => RemoteProtocol.WriteMouseButtonAsync(stream, isDown, button, x, y, cancellationToken));
    }

    public Task SendMouseWheelAsync(int x, int y, int delta)
    {
        return SendAsync((stream, cancellationToken) => RemoteProtocol.WriteMouseWheelAsync(stream, x, y, delta, cancellationToken));
    }

    public Task SendKeyAsync(int keyCode, bool isDown)
    {
        return SendAsync((stream, cancellationToken) => RemoteProtocol.WriteKeyAsync(stream, keyCode, isDown, cancellationToken));
    }

    public Task SendClipboardTextAsync(string text)
    {
        return SendAsync((stream, cancellationToken) => RemoteProtocol.WriteClipboardSetAsync(stream, text, cancellationToken));
    }

    public Task RequestClipboardAsync()
    {
        return SendAsync((stream, cancellationToken) => RemoteProtocol.WriteClipboardRequestAsync(stream, cancellationToken));
    }

    public async Task SendFileAsync(string filePath)
    {
        var stream = _stream;
        var cancellation = _lifetimeCancellation;
        if (stream is null || cancellation is null)
        {
            return;
        }

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("File not found.", filePath);
        }

        var lockAcquired = false;
        try
        {
            await _sendLock.WaitAsync(cancellation.Token);
            lockAcquired = true;

            await RemoteProtocol.WriteFileMetadataAsync(stream, fileInfo.Name, fileInfo.Length, cancellation.Token);
            await using var fileStream = fileInfo.OpenRead();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await fileStream.ReadAsync(buffer, cancellation.Token)) > 0)
            {
                await RemoteProtocol.WriteFileChunkAsync(stream, buffer, read, cancellation.Token);
            }

            StatusChanged?.Invoke($"File sent: {fileInfo.Name}");
        }
        finally
        {
            if (lockAcquired)
            {
                _sendLock.Release();
            }
        }
    }

    private async Task DisconnectAsync(bool waitForReceiveLoop)
    {
        var client = _client;
        var stream = _stream;
        var lifetimeCancellation = _lifetimeCancellation;
        var receiveLoopTask = _receiveLoopTask;

        _client = null;
        _stream = null;
        _lifetimeCancellation = null;
        _receiveLoopTask = null;

        if (client is null)
        {
            return;
        }

        try
        {
            lifetimeCancellation?.Cancel();
            stream?.Close();
            client.Close();
            await _audioService.DisconnectAsync();
            if (waitForReceiveLoop && receiveLoopTask is not null)
            {
                await receiveLoopTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            stream?.Dispose();
            client.Dispose();
            lifetimeCancellation?.Dispose();
            StatusChanged?.Invoke("Client disconnected.");
            ConnectionChanged?.Invoke(false);
        }
    }

    private async Task ReceiveServerPacketsLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await RemoteProtocol.ReadServerPacketAsync(stream, cancellationToken);
                switch (packet)
                {
                    case FrameServerPacket framePacket:
                        FrameReceived?.Invoke(framePacket.Frame);
                        break;
                    case ClipboardResponseServerPacket clipboardPacket:
                        WindowsClipboard.SetText(clipboardPacket.Text);
                        StatusChanged?.Invoke("Remote clipboard copied to this computer.");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            await DisconnectAsync(waitForReceiveLoop: false);
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Client error: {exception.Message}");
            await DisconnectAsync(waitForReceiveLoop: false);
        }
    }

    private async Task SendAsync(Func<NetworkStream, CancellationToken, Task> sendOperation)
    {
        var stream = _stream;
        var cancellation = _lifetimeCancellation;
        if (stream is null || cancellation is null)
        {
            return;
        }

        var lockAcquired = false;

        try
        {
            await _sendLock.WaitAsync(cancellation.Token);
            lockAcquired = true;
            await sendOperation(stream, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (lockAcquired)
            {
                _sendLock.Release();
            }
        }
    }
}