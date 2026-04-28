using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace Ducz.LocalConnect.App;

internal sealed class RemoteHostService
{
    private const int FrameIntervalMilliseconds = 33;
    private const int AudioPortOffset = 1;

    private readonly RemoteAudioHostService _audioService = new();
    private readonly ScreenDiffEncoder _screenDiffEncoder = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _acceptLoopTask;
    private string _accessPin = "123456";
    private string? _currentScreenDeviceName;

    public bool IsRunning => _listener is not null;

    public event Action<string>? StatusChanged;

    public void Start(int port, string accessPin)
    {
        if (_listener is not null)
        {
            return;
        }

        _accessPin = string.IsNullOrWhiteSpace(accessPin) ? "123456" : accessPin.Trim();
        _currentScreenDeviceName = null;
        _screenDiffEncoder.SetScreen(null);
        _lifetimeCancellation = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _audioService.StatusChanged += HandleAudioStatusChanged;
        _audioService.Start(port + AudioPortOffset);
        StatusChanged?.Invoke($"Waiting for a client on ports {port} and {port + AudioPortOffset}.");
        _acceptLoopTask = AcceptLoopAsync(_lifetimeCancellation.Token);
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

            await _audioService.StopAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _audioService.StatusChanged -= HandleAudioStatusChanged;
            lifetimeCancellation?.Dispose();
            StatusChanged?.Invoke("Host stopped.");
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
            StatusChanged?.Invoke($"Host error: {exception.Message}");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connectionToken = linkedCancellation.Token;
        using var networkClient = client;
        using var stream = networkClient.GetStream();

        var authenticated = await AuthenticateClientAsync(stream, connectionToken);
        if (!authenticated)
        {
            return;
        }

        StatusChanged?.Invoke($"Client authenticated: {networkClient.Client.RemoteEndPoint}");

        var sendFramesTask = SendFramesLoopAsync(stream, connectionToken);
        var receivePacketsTask = ReceiveClientPacketsLoopAsync(stream, connectionToken);

        await Task.WhenAny(sendFramesTask, receivePacketsTask);
        linkedCancellation.Cancel();

        try
        {
            await Task.WhenAll(sendFramesTask, receivePacketsTask);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Connection closed with an error: {exception.Message}");
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            StatusChanged?.Invoke("Client disconnected. Waiting for a new connection.");
        }
    }

    private async Task<bool> AuthenticateClientAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var packet = await RemoteProtocol.ReadClientPacketAsync(stream, cancellationToken);
        if (packet is not AuthRequestClientPacket authPacket)
        {
            await RemoteProtocol.WriteAuthResultAsync(stream, success: false, "Invalid handshake.", cancellationToken);
            return false;
        }

        var valid = string.Equals(authPacket.Pin, _accessPin, StringComparison.Ordinal);
        await RemoteProtocol.WriteAuthResultAsync(stream, valid, valid ? "Authenticated." : "Invalid PIN.", cancellationToken);
        if (valid)
        {
            await RemoteProtocol.WriteMonitorListAsync(stream, GetAvailableMonitors(), cancellationToken);
        }

        return valid;
    }

    private async Task SendFramesLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = _screenDiffEncoder.CaptureNextFrame();
            if (frame is not null)
            {
                await SendAsync(stream, (networkStream, token) => RemoteProtocol.WriteFrameAsync(networkStream, frame, token), cancellationToken);
            }

            await Task.Delay(FrameIntervalMilliseconds, cancellationToken);
        }
    }

    private async Task ReceiveClientPacketsLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        FileStream? fileStream = null;
        string? currentFileName = null;
        long expectedBytes = 0;
        long receivedBytes = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await RemoteProtocol.ReadClientPacketAsync(stream, cancellationToken);
                switch (packet)
                {
                    case InputClientPacket inputPacket:
                        ApplyInput(inputPacket.Input);
                        break;
                    case ClipboardSetClientPacket clipboardPacket:
                        WindowsClipboard.SetText(clipboardPacket.Text);
                        StatusChanged?.Invoke("Clipboard updated by the client.");
                        break;
                    case ClipboardRequestClientPacket:
                        await SendAsync(stream, (networkStream, token) => RemoteProtocol.WriteClipboardResponseAsync(networkStream, WindowsClipboard.GetText(), token), cancellationToken);
                        break;
                    case MonitorSelectClientPacket monitorPacket:
                        var changedMonitor = SetSelectedScreen(monitorPacket.DeviceName);
                        await SendAsync(stream, (networkStream, token) => RemoteProtocol.WriteMonitorListAsync(networkStream, GetAvailableMonitors(), token), cancellationToken);
                        StatusChanged?.Invoke($"Shared monitor changed to {changedMonitor}.");
                        break;
                    case FileMetadataClientPacket fileMetadataPacket:
                        fileStream?.Dispose();
                        var destinationPath = BuildIncomingFilePath(fileMetadataPacket.FileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        currentFileName = fileMetadataPacket.FileName;
                        expectedBytes = fileMetadataPacket.FileSize;
                        receivedBytes = 0;
                        StatusChanged?.Invoke($"Receiving file: {currentFileName}");
                        break;
                    case FileChunkClientPacket fileChunkPacket when fileStream is not null:
                        await fileStream.WriteAsync(fileChunkPacket.Content, cancellationToken);
                        receivedBytes += fileChunkPacket.Content.Length;
                        if (receivedBytes >= expectedBytes)
                        {
                            await fileStream.FlushAsync(cancellationToken);
                            fileStream.Dispose();
                            fileStream = null;
                            StatusChanged?.Invoke($"File received: {currentFileName}");
                        }
                        break;
                }
            }
        }
        finally
        {
            fileStream?.Dispose();
        }
    }

    private void ApplyInput(RemoteInputMessage message)
    {
        try
        {
            NativeInput.Apply(message);
        }
        catch (Win32Exception exception)
        {
            StatusChanged?.Invoke($"Failed to apply remote input: {exception.Message}");
        }
    }

    private static string BuildIncomingFilePath(string fileName)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Ducz LocalConnect Received Files");
        return Path.Combine(directory, Path.GetFileName(fileName));
    }

    private string SetSelectedScreen(string? deviceName)
    {
        var selectedScreen = Screen.AllScreens.FirstOrDefault(screen => string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            ?? Screen.AllScreens.FirstOrDefault(screen => screen.Primary)
            ?? Screen.AllScreens.First();

        _currentScreenDeviceName = selectedScreen.DeviceName;
        _screenDiffEncoder.SetScreen(_currentScreenDeviceName);
        return BuildMonitorDisplayName(selectedScreen, Array.IndexOf(Screen.AllScreens, selectedScreen));
    }

    private IReadOnlyList<RemoteMonitorInfo> GetAvailableMonitors()
    {
        var screens = Screen.AllScreens;
        var selectedDeviceName = _currentScreenDeviceName;
        return screens
            .Select((screen, index) => new RemoteMonitorInfo(
                screen.DeviceName,
                BuildMonitorDisplayName(screen, index),
                screen.Primary,
                string.Equals(screen.DeviceName, selectedDeviceName, StringComparison.OrdinalIgnoreCase)
                    || (selectedDeviceName is null && screen.Primary)))
            .ToArray();
    }

    private static string BuildMonitorDisplayName(Screen screen, int index)
    {
        var primarySuffix = screen.Primary ? " (Primary)" : string.Empty;
        return $"Monitor {index + 1}: {screen.Bounds.Width}x{screen.Bounds.Height} at {screen.Bounds.X},{screen.Bounds.Y}{primarySuffix}";
    }

    private async Task SendAsync(NetworkStream stream, Func<NetworkStream, CancellationToken, Task> sendOperation, CancellationToken cancellationToken)
    {
        var lockAcquired = false;

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            lockAcquired = true;
            await sendOperation(stream, cancellationToken);
        }
        finally
        {
            if (lockAcquired)
            {
                _sendLock.Release();
            }
        }
    }

    private void HandleAudioStatusChanged(string message)
    {
        StatusChanged?.Invoke($"Audio: {message}");
    }
}