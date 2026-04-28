using System.Net.Sockets;

namespace Ducz.LocalConnect.App;

internal sealed class RemoteClientService
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _receiveLoopTask;

    public bool IsConnected => _client?.Connected == true && _stream is not null;

    public event Action<string>? StatusChanged;

    public event Action<RemoteFrame>? FrameReceived;

    public event Action<bool>? ConnectionChanged;

    public async Task ConnectAsync(string host, int port)
    {
        if (IsConnected)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Informe o IP ou nome do host.");
        }

        var client = new TcpClient
        {
            NoDelay = true
        };

        await client.ConnectAsync(host, port);
        _client = client;
        _stream = client.GetStream();
        _lifetimeCancellation = new CancellationTokenSource();
        _receiveLoopTask = ReceiveFramesLoopAsync(_stream, _lifetimeCancellation.Token);

        StatusChanged?.Invoke($"conectado a {host}:{port}.");
        ConnectionChanged?.Invoke(true);
    }

    public Task DisconnectAsync()
    {
        return DisconnectAsync(waitForReceiveLoop: true);
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
            StatusChanged?.Invoke("cliente desconectado.");
            ConnectionChanged?.Invoke(false);
        }
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

    private async Task ReceiveFramesLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await RemoteProtocol.ReadFrameAsync(stream, cancellationToken);
                FrameReceived?.Invoke(frame);
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
            StatusChanged?.Invoke($"erro no cliente: {exception.Message}");
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