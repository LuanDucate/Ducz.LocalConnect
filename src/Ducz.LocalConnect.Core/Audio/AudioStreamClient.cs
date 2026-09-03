using System.Net.Sockets;
using NAudio.Wave;

namespace Ducz.LocalConnect.Core.Audio;

public sealed class AudioStreamClient : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private TcpClient? _client;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private WaveOutEvent? _output;
    private bool _muted;

    public event Action<string>? Log;

    public bool IsConnected => _client is not null;

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            var output = _output;
            if (output is not null)
            {
                output.Volume = value ? 0f : 1f;
            }
        }
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return;
        }

        var client = new TcpClient { NoDelay = true };
        var lifetime = new CancellationTokenSource();

        try
        {
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeout.CancelAfter(ConnectTimeout);
                await client.ConnectAsync(host, port, connectTimeout.Token).ConfigureAwait(false);
            }

            var stream = client.GetStream();
            var format = await AudioProtocol.ReadWaveFormatAsync(stream, cancellationToken).ConfigureAwait(false);

            var buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
            };

            var output = new WaveOutEvent { DesiredLatency = 150 };
            output.Init(buffer);
            output.Volume = _muted ? 0f : 1f;
            output.Play();

            _client = client;
            _lifetime = lifetime;
            _output = output;
            _receiveLoop = ReceiveLoopAsync(stream, buffer, lifetime.Token);
        }
        catch
        {
            lifetime.Dispose();
            client.Dispose();
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        var client = _client;
        var lifetime = _lifetime;
        var receiveLoop = _receiveLoop;
        var output = _output;
        _client = null;
        _lifetime = null;
        _receiveLoop = null;
        _output = null;

        if (client is null || lifetime is null)
        {
            return;
        }

        lifetime.Cancel();
        client.Close();

        if (receiveLoop is not null)
        {
            try
            {
                await receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        output?.Stop();
        output?.Dispose();
        client.Dispose();
        lifetime.Dispose();
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    private async Task ReceiveLoopAsync(NetworkStream stream, BufferedWaveProvider buffer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var chunk = await AudioProtocol.ReadChunkAsync(stream, cancellationToken).ConfigureAwait(false);
                buffer.AddSamples(chunk, 0, chunk.Length);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ObjectDisposedException)
        {
            Log?.Invoke("Audio stream ended.");
        }
        catch (Exception exception)
        {
            Log?.Invoke($"Audio error: {exception.Message}");
        }
    }
}
