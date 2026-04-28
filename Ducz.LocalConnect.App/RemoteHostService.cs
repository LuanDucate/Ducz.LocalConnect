using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace Ducz.LocalConnect.App;

internal sealed class RemoteHostService
{
    private const int FrameIntervalMilliseconds = 60;
    private const int AudioPortOffset = 1;

    private readonly RemoteAudioHostService _audioService = new();
    private readonly ScreenDiffEncoder _screenDiffEncoder = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _acceptLoopTask;
    private string _accessPin = "123456";

    public bool IsRunning => _listener is not null;

    public event Action<string>? StatusChanged;

    public void Start(int port, string accessPin)
    {
        if (_listener is not null)
        {
            return;
        }

        _accessPin = string.IsNullOrWhiteSpace(accessPin) ? "123456" : accessPin.Trim();
        _lifetimeCancellation = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _audioService.StatusChanged += HandleAudioStatusChanged;
        _audioService.Start(port + AudioPortOffset);
        StatusChanged?.Invoke($"aguardando cliente nas portas {port} e {port + AudioPortOffset}.");
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
            StatusChanged?.Invoke("host parado.");
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
            StatusChanged?.Invoke($"erro no host: {exception.Message}");
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

        StatusChanged?.Invoke($"cliente autenticado: {networkClient.Client.RemoteEndPoint}");

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
            StatusChanged?.Invoke($"conexão encerrada com erro: {exception.Message}");
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            StatusChanged?.Invoke("cliente desconectado. aguardando nova conexão.");
        }
    }

    private async Task<bool> AuthenticateClientAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var packet = await RemoteProtocol.ReadClientPacketAsync(stream, cancellationToken);
        if (packet is not AuthRequestClientPacket authPacket)
        {
            await RemoteProtocol.WriteAuthResultAsync(stream, success: false, "Handshake inválido.", cancellationToken);
            return false;
        }

        var valid = string.Equals(authPacket.Pin, _accessPin, StringComparison.Ordinal);
        await RemoteProtocol.WriteAuthResultAsync(stream, valid, valid ? "Autenticado." : "PIN inválido.", cancellationToken);
        return valid;
    }

    private async Task SendFramesLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = _screenDiffEncoder.CaptureNextFrame();
            if (frame is not null)
            {
                await RemoteProtocol.WriteFrameAsync(stream, frame, cancellationToken);
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
                        StatusChanged?.Invoke("clipboard atualizado pelo cliente.");
                        break;
                    case ClipboardRequestClientPacket:
                        await RemoteProtocol.WriteClipboardResponseAsync(stream, WindowsClipboard.GetText(), cancellationToken);
                        break;
                    case FileMetadataClientPacket fileMetadataPacket:
                        fileStream?.Dispose();
                        var destinationPath = BuildIncomingFilePath(fileMetadataPacket.FileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        currentFileName = fileMetadataPacket.FileName;
                        expectedBytes = fileMetadataPacket.FileSize;
                        receivedBytes = 0;
                        StatusChanged?.Invoke($"recebendo arquivo: {currentFileName}");
                        break;
                    case FileChunkClientPacket fileChunkPacket when fileStream is not null:
                        await fileStream.WriteAsync(fileChunkPacket.Content, cancellationToken);
                        receivedBytes += fileChunkPacket.Content.Length;
                        if (receivedBytes >= expectedBytes)
                        {
                            await fileStream.FlushAsync(cancellationToken);
                            fileStream.Dispose();
                            fileStream = null;
                            StatusChanged?.Invoke($"arquivo recebido: {currentFileName}");
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
            StatusChanged?.Invoke($"falha ao aplicar entrada remota: {exception.Message}");
        }
    }

    private static string BuildIncomingFilePath(string fileName)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Ducz LocalConnect Recebidos");
        return Path.Combine(directory, Path.GetFileName(fileName));
    }

    private void HandleAudioStatusChanged(string message)
    {
        StatusChanged?.Invoke($"áudio: {message}");
    }
}