using System.Drawing.Imaging;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace Ducz.LocalConnect.App;

internal sealed class RemoteHostService
{
    private const int FrameIntervalMilliseconds = 90;

    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _acceptLoopTask;

    public bool IsRunning => _listener is not null;

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
        StatusChanged?.Invoke($"aguardando cliente na porta {port}.");
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
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
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

        StatusChanged?.Invoke($"cliente conectado: {networkClient.Client.RemoteEndPoint}");

        var sendFramesTask = SendFramesLoopAsync(stream, connectionToken);
        var receiveInputsTask = ReceiveInputsLoopAsync(stream, connectionToken);

        await Task.WhenAny(sendFramesTask, receiveInputsTask);
        linkedCancellation.Cancel();

        try
        {
            await Task.WhenAll(sendFramesTask, receiveInputsTask);
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

    private static async Task SendFramesLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = CapturePrimaryScreen();
            await RemoteProtocol.WriteFrameAsync(stream, frame, cancellationToken);
            await Task.Delay(FrameIntervalMilliseconds, cancellationToken);
        }
    }

    private async Task ReceiveInputsLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await RemoteProtocol.ReadInputAsync(stream, cancellationToken);

            try
            {
                NativeInput.Apply(message);
            }
            catch (Win32Exception exception)
            {
                StatusChanged?.Invoke($"falha ao aplicar entrada remota: {exception.Message}");
            }
        }
    }

    private static RemoteFrame CapturePrimaryScreen()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Nenhuma tela principal foi encontrada.");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        using var stream = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 55L);
        bitmap.Save(stream, encoder, encoderParameters);

        return new RemoteFrame(bounds.Width, bounds.Height, stream.ToArray());
    }
}