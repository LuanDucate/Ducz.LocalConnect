using System.Net;
using System.Net.Sockets;
using Ducz.LocalConnect.Core.Audio;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Sessions;

public sealed class HostServer : IAsyncDisposable
{
    private readonly AudioStreamHost _audio = new();
    private readonly SemaphoreSlim _sessionSlot = new(1, 1);

    private HostSession? _currentSession; // guarded by the session-slot semaphore's barriers
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;
    private HostState _state;

    public HostServer()
    {
        _audio.Log += message => Log?.Invoke($"Audio: {message}");
    }

    public event Action<HostState>? StateChanged;
    public event Action<string>? Log;
    public event Action<EndPoint?>? ClientConnected;
    public event Action? ClientDisconnected;

    public HostState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            StateChanged?.Invoke(value);
        }
    }

    public bool IsRunning => _listener is not null;

    public HostOptions? Options { get; private set; }

    public void Start(HostOptions options)
    {
        if (_listener is not null)
        {
            return;
        }

        options.Validate();
        Options = options;

        var listener = new TcpListener(IPAddress.Any, options.Port);
        listener.Start();

        try
        {
            _audio.Start(options.Port + ProtocolLimits.AudioPortOffset);
        }
        catch
        {
            listener.Stop();
            throw;
        }

        _listener = listener;
        _lifetime = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(listener, _lifetime.Token);
        State = HostState.Listening;
        Log?.Invoke($"Listening on port {options.Port} (audio on {options.Port + ProtocolLimits.AudioPortOffset}).");
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

        await _audio.StopAsync().ConfigureAwait(false);
        lifetime.Dispose();
        State = HostState.Stopped;
        Log?.Invoke("Host stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _sessionSlot.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        var sessions = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                sessions.RemoveAll(task => task.IsCompleted);
                sessions.Add(HandleClientAsync(client, cancellationToken));
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
            Log?.Invoke($"Listener error: {exception.Message}");
        }

        await Task.WhenAll(sessions).ConfigureAwait(false);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var options = Options!;
        var session = new HostSession(client, options, cancellationToken, message => Log?.Invoke(message));
        var endpoint = session.RemoteEndPoint;
        var slotTaken = false;

        try
        {
            if (!await session.AuthenticateAsync(cancellationToken).ConfigureAwait(false))
            {
                Log?.Invoke($"Rejected connection from {endpoint}.");
                return;
            }

            if (!_sessionSlot.Wait(0, CancellationToken.None))
            {
                Log?.Invoke($"{endpoint} is taking over the session.");
                _currentSession?.RequestStop();
                if (!await _sessionSlot.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false))
                {
                    await session.RejectAsync("The host is busy and did not free up in time. Try again.", cancellationToken).ConfigureAwait(false);
                    Log?.Invoke($"Turned away {endpoint}: the previous client did not release in time.");
                    return;
                }
            }

            slotTaken = true;
            _currentSession = session;
            State = HostState.Connected;
            ClientConnected?.Invoke(endpoint);
            Log?.Invoke($"Client connected: {endpoint}");

            await session.RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log?.Invoke($"Session ended with an error: {exception.Message}");
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);

            if (slotTaken)
            {
                Interlocked.CompareExchange(ref _currentSession, null, session);
                _sessionSlot.Release();
                ClientDisconnected?.Invoke();
                if (!cancellationToken.IsCancellationRequested && _currentSession is null)
                {
                    State = HostState.Listening;
                    Log?.Invoke("Client disconnected. Waiting for a new connection.");
                }
            }
        }
    }
}
