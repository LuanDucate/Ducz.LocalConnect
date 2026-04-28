using System.Net.Sockets;
using NAudio.Wave;

namespace Ducz.LocalConnect.App;

internal sealed class RemoteAudioClientService
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _receiveLoopTask;
    private WaveOutEvent? _waveOut;

    public async Task ConnectAsync(string host, int port)
    {
        if (_client is not null)
        {
            return;
        }

        var client = new TcpClient
        {
            NoDelay = true
        };

        await client.ConnectAsync(host, port);
        var stream = client.GetStream();
        var cancellation = new CancellationTokenSource();

        try
        {
            var format = await RemoteAudioProtocol.ReadWaveFormatAsync(stream, cancellation.Token);
            var buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true
            };

            var waveOut = new WaveOutEvent
            {
                DesiredLatency = 150
            };
            waveOut.Init(buffer);
            waveOut.Play();

            _client = client;
            _stream = stream;
            _lifetimeCancellation = cancellation;
            _waveOut = waveOut;
            _receiveLoopTask = ReceiveLoopAsync(stream, buffer, cancellation.Token);
        }
        catch
        {
            cancellation.Dispose();
            stream.Dispose();
            client.Dispose();
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        var client = _client;
        var stream = _stream;
        var lifetimeCancellation = _lifetimeCancellation;
        var receiveLoopTask = _receiveLoopTask;
        var waveOut = _waveOut;

        _client = null;
        _stream = null;
        _lifetimeCancellation = null;
        _receiveLoopTask = null;
        _waveOut = null;

        if (client is null)
        {
            return;
        }

        try
        {
            lifetimeCancellation?.Cancel();
            stream?.Close();
            client.Close();
            if (receiveLoopTask is not null)
            {
                await receiveLoopTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            waveOut?.Stop();
            waveOut?.Dispose();
            stream?.Dispose();
            client.Dispose();
            lifetimeCancellation?.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, BufferedWaveProvider buffer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var chunk = await RemoteAudioProtocol.ReadAudioChunkAsync(stream, cancellationToken);
                buffer.AddSamples(chunk, 0, chunk.Length);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await DisconnectAsync();
        }
    }
}