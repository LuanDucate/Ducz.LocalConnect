using System.Net.Sockets;

namespace Ducz.LocalConnect.Core.Sessions;

internal sealed class PacketChannel : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PacketChannel(TcpClient client)
    {
        Client = client;
        Stream = client.GetStream();
    }

    public TcpClient Client { get; }

    public NetworkStream Stream { get; }

    public async ValueTask SendAsync(Func<Stream, CancellationToken, ValueTask> write, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await write(Stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Like <see cref="SendAsync"/> but gives up immediately if a send is in flight.
    /// Used for mouse moves, where a dropped sample is better than a queued one.</summary>
    public async ValueTask<bool> TrySendAsync(Func<Stream, CancellationToken, ValueTask> write, CancellationToken cancellationToken)
    {
        if (!_writeLock.Wait(0, CancellationToken.None))
        {
            return false;
        }

        try
        {
            await write(Stream, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        Stream.Dispose();
        Client.Dispose();
        _writeLock.Dispose();
    }
}
