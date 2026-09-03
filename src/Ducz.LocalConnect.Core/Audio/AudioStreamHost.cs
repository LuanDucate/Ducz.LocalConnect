using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using NAudio.Wave;

namespace Ducz.LocalConnect.Core.Audio;
public sealed class AudioStreamHost
{
    private const int QueueCapacity = 64; // ~1.3 s of 20 ms buffers

    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;

    public event Action<string>? Log;

    public bool IsRunning => _listener is not null;

    public void Start(int port)
    {
        if (_listener is not null)
        {
            return;
        }

        _lifetime = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_listener, _lifetime.Token);
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        var lifetime = _lifetime;
        var acceptLoop = _acceptLoop;
        _listener = null;
        _lifetime = null;
        _acceptLoop = null;

        if (listener is null || lifetime is null)
        {
            return;
        }

        lifetime.Cancel();
        listener.Stop();
        if (acceptLoop is not null)
        {
            await acceptLoop.ConfigureAwait(false);
        }

        lifetime.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                await StreamToClientAsync(client, cancellationToken).ConfigureAwait(false);
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
            Log?.Invoke($"Audio listener stopped: {exception.Message}");
        }
    }

    private async Task StreamToClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linked.Token;

        using (client)
        await using (var stream = client.GetStream())
        using (var capture = new WasapiLoopbackCapture())
        {
            var queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

            capture.DataAvailable += (_, args) =>
            {
                if (args.BytesRecorded == 0)
                {
                    return;
                }

                // Copy and hand off; never touch the socket from the audio thread.
                var chunk = new byte[args.BytesRecorded];
                Buffer.BlockCopy(args.Buffer, 0, chunk, 0, args.BytesRecorded);
                queue.Writer.TryWrite(chunk);
            };
            capture.RecordingStopped += (_, args) => queue.Writer.TryComplete(args.Exception);

            Log?.Invoke($"Audio client connected: {client.Client.RemoteEndPoint}");

            try
            {
                await AudioProtocol.WriteWaveFormatAsync(stream, capture.WaveFormat, token).ConfigureAwait(false);
                capture.StartRecording();

                await foreach (var chunk in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await AudioProtocol.WriteChunkAsync(stream, chunk, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                // Client went away; normal.
            }
            catch (Exception exception)
            {
                Log?.Invoke($"Audio stream ended: {exception.Message}");
            }
            finally
            {
                capture.StopRecording();
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            Log?.Invoke("Audio client disconnected.");
        }
    }
}
