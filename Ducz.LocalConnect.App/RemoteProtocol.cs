using System.Buffers.Binary;

namespace Ducz.LocalConnect.App;

internal enum PacketType : byte
{
    Frame = 1,
    MouseMove = 2,
    MouseDown = 3,
    MouseUp = 4,
    MouseWheel = 5,
    KeyDown = 6,
    KeyUp = 7
}

internal enum RemoteMouseButton : byte
{
    Left = 1,
    Right = 2,
    Middle = 3
}

internal sealed record RemoteFrame(int Width, int Height, byte[] ImageBytes);

internal sealed record RemoteInputMessage(PacketType Type, int X = 0, int Y = 0, int Delta = 0, int KeyCode = 0, RemoteMouseButton Button = RemoteMouseButton.Left);

internal static class RemoteProtocol
{
    public static async Task WriteFrameAsync(Stream stream, RemoteFrame frame, CancellationToken cancellationToken)
    {
        var header = new byte[13];
        header[0] = (byte)PacketType.Frame;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1, 4), frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(5, 4), frame.Height);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), frame.ImageBytes.Length);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(frame.ImageBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<RemoteFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, 13, cancellationToken);
        EnsurePacket(header[0], PacketType.Frame);

        var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5, 4));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(9, 4));
        var imageBytes = await ReadExactAsync(stream, imageLength, cancellationToken);

        return new RemoteFrame(width, height, imageBytes);
    }

    public static Task WriteMouseMoveAsync(Stream stream, int x, int y, CancellationToken cancellationToken)
    {
        var payload = new byte[9];
        payload[0] = (byte)PacketType.MouseMove;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), y);
        return WritePacketAsync(stream, payload, cancellationToken);
    }

    public static Task WriteMouseButtonAsync(Stream stream, bool isDown, RemoteMouseButton button, int x, int y, CancellationToken cancellationToken)
    {
        var payload = new byte[10];
        payload[0] = isDown ? (byte)PacketType.MouseDown : (byte)PacketType.MouseUp;
        payload[1] = (byte)button;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(2, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6, 4), y);
        return WritePacketAsync(stream, payload, cancellationToken);
    }

    public static Task WriteMouseWheelAsync(Stream stream, int x, int y, int delta, CancellationToken cancellationToken)
    {
        var payload = new byte[13];
        payload[0] = (byte)PacketType.MouseWheel;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(9, 4), delta);
        return WritePacketAsync(stream, payload, cancellationToken);
    }

    public static Task WriteKeyAsync(Stream stream, int keyCode, bool isDown, CancellationToken cancellationToken)
    {
        var payload = new byte[5];
        payload[0] = isDown ? (byte)PacketType.KeyDown : (byte)PacketType.KeyUp;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), keyCode);
        return WritePacketAsync(stream, payload, cancellationToken);
    }

    public static async Task<RemoteInputMessage> ReadInputAsync(Stream stream, CancellationToken cancellationToken)
    {
        var packetTypeBuffer = await ReadExactAsync(stream, 1, cancellationToken);
        var packetType = (PacketType)packetTypeBuffer[0];

        return packetType switch
        {
            PacketType.MouseMove => await ReadMouseMoveAsync(stream, packetType, cancellationToken),
            PacketType.MouseDown or PacketType.MouseUp => await ReadMouseButtonAsync(stream, packetType, cancellationToken),
            PacketType.MouseWheel => await ReadMouseWheelAsync(stream, packetType, cancellationToken),
            PacketType.KeyDown or PacketType.KeyUp => await ReadKeyAsync(stream, packetType, cancellationToken),
            _ => throw new InvalidDataException($"Pacote de entrada desconhecido: {packetType}.")
        };
    }

    private static async Task<RemoteInputMessage> ReadMouseMoveAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 8, cancellationToken);
        return new RemoteInputMessage(
            type,
            X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)),
            Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)));
    }

    private static async Task<RemoteInputMessage> ReadMouseButtonAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 9, cancellationToken);
        return new RemoteInputMessage(
            type,
            X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1, 4)),
            Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(5, 4)),
            Button: (RemoteMouseButton)payload[0]);
    }

    private static async Task<RemoteInputMessage> ReadMouseWheelAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 12, cancellationToken);
        return new RemoteInputMessage(
            type,
            X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)),
            Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)),
            Delta: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4)));
    }

    private static async Task<RemoteInputMessage> ReadKeyAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 4, cancellationToken);
        return new RemoteInputMessage(type, KeyCode: BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static async Task WritePacketAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void EnsurePacket(byte actualType, PacketType expectedType)
    {
        if (actualType != (byte)expectedType)
        {
            throw new InvalidDataException($"Pacote inesperado. Esperado {(byte)expectedType}, recebido {actualType}.");
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var read = 0;

        while (read < length)
        {
            var currentRead = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
            if (currentRead == 0)
            {
                throw new EndOfStreamException("A conexão foi encerrada.");
            }

            read += currentRead;
        }

        return buffer;
    }
}