using System.Net;
using System.Net.Sockets;
using NAudio.Wave;

namespace Ducz.LocalConnect.App;

internal sealed class RemoteAudioHostService
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _acceptLoopTask;

    public event Action<string>? StatusChanged;

    public void Start(int port)
    {
        if (_listener is not null)
        {
            return;
        }

        _lifetimeCancellation = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _acceptLoopTask = AcceptLoopAsync(_lifetimeCancellation.Token);
        StatusChanged?.Invoke($"Waiting for an audio client on port {port}");
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        var lifetimeCancellation = _lifetimeCancellation;
        var acceptLoopTask = _acceptLoopTask;

        _listener = null;
        _lifetimeCancellation = null;
        _acceptLoopTask = null;

        if (listener is null)
        {
            return;
        }

        try
        {
            lifetimeCancellation?.Cancel();
            listener.Stop();

            if (acceptLoopTask is not null)
            {
                await acceptLoopTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lifetimeCancellation?.Dispose();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_listener is not null && !cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;
                await HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Audio service error: {exception.Message}");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connectionToken = linkedCancellation.Token;
        using var networkClient = client;
        using var stream = networkClient.GetStream();
        using var capture = new WasapiLoopbackCapture();

        capture.DataAvailable += async (_, args) =>
        {
            try
            {
                await _sendLock.WaitAsync(connectionToken);
                await RemoteAudioProtocol.WriteAudioChunkAsync(stream, args.Buffer.AsMemory(0, args.BytesRecorded), connectionToken);
            }
            catch
            {
                linkedCancellation.Cancel();
            }
            finally
            {
                if (_sendLock.CurrentCount == 0)
                {
                    _sendLock.Release();
                }
            }
        };

        StatusChanged?.Invoke($"Client connected: {networkClient.Client.RemoteEndPoint}");
        await RemoteAudioProtocol.WriteWaveFormatAsync(stream, capture.WaveFormat, connectionToken);
        capture.StartRecording();

        try
        {
            while (!connectionToken.IsCancellationRequested)
            {
                await Task.Delay(250, connectionToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            capture.StopRecording();
            if (!cancellationToken.IsCancellationRequested)
            {
                StatusChanged?.Invoke("Client disconnected");
            }
        }
    }
}