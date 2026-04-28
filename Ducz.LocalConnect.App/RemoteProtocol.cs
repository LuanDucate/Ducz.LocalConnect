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
    KeyUp = 7,
    AuthRequest = 8,
    AuthResult = 9,
    ClipboardSet = 10,
    ClipboardRequest = 11,
    ClipboardResponse = 12,
    FileMetadata = 13,
    FileChunk = 14,
    MonitorSelect = 15,
    MonitorList = 16
}

internal enum RemoteMouseButton : byte
{
    Left = 1,
    Right = 2,
    Middle = 3
}

internal enum RemoteFrameKind : byte
{
    Full = 1,
    Delta = 2
}

internal sealed record RemoteFrame(
    RemoteFrameKind Kind,
    int DesktopWidth,
    int DesktopHeight,
    int X,
    int Y,
    int Width,
    int Height,
    byte[] ImageBytes);

internal sealed record RemoteInputMessage(PacketType Type, int X = 0, int Y = 0, int Delta = 0, int KeyCode = 0, RemoteMouseButton Button = RemoteMouseButton.Left);
internal sealed record RemoteMonitorInfo(string DeviceName, string DisplayName, bool IsPrimary, bool IsSelected);

internal abstract record ClientPacket(PacketType Type);
internal sealed record InputClientPacket(RemoteInputMessage Input) : ClientPacket(Input.Type);
internal sealed record AuthRequestClientPacket(string Pin) : ClientPacket(PacketType.AuthRequest);
internal sealed record ClipboardSetClientPacket(string Text) : ClientPacket(PacketType.ClipboardSet);
internal sealed record ClipboardRequestClientPacket() : ClientPacket(PacketType.ClipboardRequest);
internal sealed record FileMetadataClientPacket(string FileName, long FileSize) : ClientPacket(PacketType.FileMetadata);
internal sealed record FileChunkClientPacket(byte[] Content) : ClientPacket(PacketType.FileChunk);
internal sealed record MonitorSelectClientPacket(string DeviceName) : ClientPacket(PacketType.MonitorSelect);

internal abstract record ServerPacket(PacketType Type);
internal sealed record FrameServerPacket(RemoteFrame Frame) : ServerPacket(PacketType.Frame);
internal sealed record AuthResultServerPacket(bool Success, string Message) : ServerPacket(PacketType.AuthResult);
internal sealed record ClipboardResponseServerPacket(string Text) : ServerPacket(PacketType.ClipboardResponse);
internal sealed record MonitorListServerPacket(IReadOnlyList<RemoteMonitorInfo> Monitors) : ServerPacket(PacketType.MonitorList);

internal static class RemoteProtocol
{
    public static async Task WriteFrameAsync(Stream stream, RemoteFrame frame, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { (byte)PacketType.Frame, (byte)frame.Kind }, cancellationToken);

        var header = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), frame.DesktopWidth);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), frame.DesktopHeight);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), frame.X);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), frame.Y);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20, 4), frame.Height);
        await stream.WriteAsync(header, cancellationToken);
        await WriteBufferAsync(stream, frame.ImageBytes, cancellationToken);
    }

    public static async Task WriteMouseMoveAsync(Stream stream, int x, int y, CancellationToken cancellationToken)
    {
        var payload = new byte[9];
        payload[0] = (byte)PacketType.MouseMove;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), y);
        await stream.WriteAsync(payload, cancellationToken);
    }

    public static async Task WriteMouseButtonAsync(Stream stream, bool isDown, RemoteMouseButton button, int x, int y, CancellationToken cancellationToken)
    {
        var payload = new byte[10];
        payload[0] = isDown ? (byte)PacketType.MouseDown : (byte)PacketType.MouseUp;
        payload[1] = (byte)button;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(2, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6, 4), y);
        await stream.WriteAsync(payload, cancellationToken);
    }

    public static async Task WriteMouseWheelAsync(Stream stream, int x, int y, int delta, CancellationToken cancellationToken)
    {
        var payload = new byte[13];
        payload[0] = (byte)PacketType.MouseWheel;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(9, 4), delta);
        await stream.WriteAsync(payload, cancellationToken);
    }

    public static async Task WriteKeyAsync(Stream stream, int keyCode, bool isDown, CancellationToken cancellationToken)
    {
        var payload = new byte[5];
        payload[0] = isDown ? (byte)PacketType.KeyDown : (byte)PacketType.KeyUp;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), keyCode);
        await stream.WriteAsync(payload, cancellationToken);
    }

    public static Task WriteAuthRequestAsync(Stream stream, string pin, CancellationToken cancellationToken)
    {
        return WriteStringPacketAsync(stream, PacketType.AuthRequest, pin, cancellationToken);
    }

    public static async Task WriteAuthResultAsync(Stream stream, bool success, string message, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { (byte)PacketType.AuthResult, success ? (byte)1 : (byte)0 }, cancellationToken);
        await WriteBufferAsync(stream, System.Text.Encoding.UTF8.GetBytes(message), cancellationToken);
    }

    public static Task WriteClipboardSetAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        return WriteStringPacketAsync(stream, PacketType.ClipboardSet, text, cancellationToken);
    }

    public static async Task WriteClipboardRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { (byte)PacketType.ClipboardRequest }, cancellationToken);
    }

    public static Task WriteClipboardResponseAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        return WriteStringPacketAsync(stream, PacketType.ClipboardResponse, text, cancellationToken);
    }

    public static async Task WriteFileMetadataAsync(Stream stream, string fileName, long fileSize, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { (byte)PacketType.FileMetadata }, cancellationToken);
        await WriteStringAsync(stream, fileName, cancellationToken);
        var fileSizeBuffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(fileSizeBuffer, fileSize);
        await stream.WriteAsync(fileSizeBuffer, cancellationToken);
    }

    public static async Task WriteFileChunkAsync(Stream stream, byte[] content, int count, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { (byte)PacketType.FileChunk }, cancellationToken);
        await WriteBufferAsync(stream, content.AsMemory(0, count), cancellationToken);
    }

    public static Task WriteMonitorSelectAsync(Stream stream, string deviceName, CancellationToken cancellationToken)
    {
        return WriteStringPacketAsync(stream, PacketType.MonitorSelect, deviceName, cancellationToken);
    }

    public static async Task WriteMonitorListAsync(Stream stream, IReadOnlyList<RemoteMonitorInfo> monitors, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { (byte)PacketType.MonitorList }, cancellationToken);

        var countBuffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(countBuffer, monitors.Count);
        await stream.WriteAsync(countBuffer, cancellationToken);

        foreach (var monitor in monitors)
        {
            await WriteStringAsync(stream, monitor.DeviceName, cancellationToken);
            await WriteStringAsync(stream, monitor.DisplayName, cancellationToken);
            await stream.WriteAsync(new[] { monitor.IsPrimary ? (byte)1 : (byte)0, monitor.IsSelected ? (byte)1 : (byte)0 }, cancellationToken);
        }
    }

    public static async Task<ClientPacket> ReadClientPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var packetType = await ReadPacketTypeAsync(stream, cancellationToken);
        return packetType switch
        {
            PacketType.MouseMove => new InputClientPacket(await ReadMouseMoveAsync(stream, packetType, cancellationToken)),
            PacketType.MouseDown or PacketType.MouseUp => new InputClientPacket(await ReadMouseButtonAsync(stream, packetType, cancellationToken)),
            PacketType.MouseWheel => new InputClientPacket(await ReadMouseWheelAsync(stream, packetType, cancellationToken)),
            PacketType.KeyDown or PacketType.KeyUp => new InputClientPacket(await ReadKeyAsync(stream, packetType, cancellationToken)),
            PacketType.AuthRequest => new AuthRequestClientPacket(await ReadStringAsync(stream, cancellationToken)),
            PacketType.ClipboardSet => new ClipboardSetClientPacket(await ReadStringAsync(stream, cancellationToken)),
            PacketType.ClipboardRequest => new ClipboardRequestClientPacket(),
            PacketType.FileMetadata => await ReadFileMetadataAsync(stream, cancellationToken),
            PacketType.FileChunk => new FileChunkClientPacket(await ReadBufferAsync(stream, cancellationToken)),
            PacketType.MonitorSelect => new MonitorSelectClientPacket(await ReadStringAsync(stream, cancellationToken)),
            _ => throw new InvalidDataException($"Unknown client packet: {packetType}.")
        };
    }

    public static async Task<ServerPacket> ReadServerPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var packetType = await ReadPacketTypeAsync(stream, cancellationToken);
        return packetType switch
        {
            PacketType.Frame => new FrameServerPacket(await ReadFrameBodyAsync(stream, cancellationToken)),
            PacketType.AuthResult => await ReadAuthResultAsync(stream, cancellationToken),
            PacketType.ClipboardResponse => new ClipboardResponseServerPacket(await ReadStringAsync(stream, cancellationToken)),
            PacketType.MonitorList => new MonitorListServerPacket(await ReadMonitorListAsync(stream, cancellationToken)),
            _ => throw new InvalidDataException($"Unknown server packet: {packetType}.")
        };
    }

    private static async Task<IReadOnlyList<RemoteMonitorInfo>> ReadMonitorListAsync(Stream stream, CancellationToken cancellationToken)
    {
        var countBuffer = await ReadExactAsync(stream, 4, cancellationToken);
        var count = BinaryPrimitives.ReadInt32LittleEndian(countBuffer);
        var monitors = new List<RemoteMonitorInfo>(count);

        for (var index = 0; index < count; index++)
        {
            var deviceName = await ReadStringAsync(stream, cancellationToken);
            var displayName = await ReadStringAsync(stream, cancellationToken);
            var flags = await ReadExactAsync(stream, 2, cancellationToken);
            monitors.Add(new RemoteMonitorInfo(deviceName, displayName, flags[0] == 1, flags[1] == 1));
        }

        return monitors;
    }

    private static async Task<RemoteFrame> ReadFrameBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        var frameKindBuffer = await ReadExactAsync(stream, 1, cancellationToken);
        var header = await ReadExactAsync(stream, 24, cancellationToken);

        var desktopWidth = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var desktopHeight = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        var x = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
        var y = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20, 4));
        var imageBytes = await ReadBufferAsync(stream, cancellationToken);

        return new RemoteFrame((RemoteFrameKind)frameKindBuffer[0], desktopWidth, desktopHeight, x, y, width, height, imageBytes);
    }

    private static async Task<ServerPacket> ReadAuthResultAsync(Stream stream, CancellationToken cancellationToken)
    {
        var successBuffer = await ReadExactAsync(stream, 1, cancellationToken);
        var message = await ReadStringAsync(stream, cancellationToken);
        return new AuthResultServerPacket(successBuffer[0] == 1, message);
    }

    private static async Task<FileMetadataClientPacket> ReadFileMetadataAsync(Stream stream, CancellationToken cancellationToken)
    {
        var fileName = await ReadStringAsync(stream, cancellationToken);
        var fileSizeBuffer = await ReadExactAsync(stream, 8, cancellationToken);
        return new FileMetadataClientPacket(fileName, BinaryPrimitives.ReadInt64LittleEndian(fileSizeBuffer));
    }

    private static async Task<RemoteInputMessage> ReadMouseMoveAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 8, cancellationToken);
        return new RemoteInputMessage(type, X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)), Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)));
    }

    private static async Task<RemoteInputMessage> ReadMouseButtonAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 9, cancellationToken);
        return new RemoteInputMessage(type, X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1, 4)), Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(5, 4)), Button: (RemoteMouseButton)payload[0]);
    }

    private static async Task<RemoteInputMessage> ReadMouseWheelAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 12, cancellationToken);
        return new RemoteInputMessage(type, X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)), Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)), Delta: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4)));
    }

    private static async Task<RemoteInputMessage> ReadKeyAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 4, cancellationToken);
        return new RemoteInputMessage(type, KeyCode: BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static Task WriteStringPacketAsync(Stream stream, PacketType packetType, string value, CancellationToken cancellationToken)
    {
        return WritePacketWithBufferAsync(stream, packetType, System.Text.Encoding.UTF8.GetBytes(value), cancellationToken);
    }

    private static async Task WritePacketWithBufferAsync(Stream stream, PacketType packetType, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { (byte)packetType }, cancellationToken);
        await WriteBufferAsync(stream, payload, cancellationToken);
    }

    private static Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        return WriteBufferAsync(stream, System.Text.Encoding.UTF8.GetBytes(value), cancellationToken);
    }

    private static async Task<string> ReadStringAsync(Stream stream, CancellationToken cancellationToken)
    {
        return System.Text.Encoding.UTF8.GetString(await ReadBufferAsync(stream, cancellationToken));
    }

    private static async Task<byte[]> ReadBufferAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = await ReadExactAsync(stream, 4, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        return await ReadExactAsync(stream, length, cancellationToken);
    }

    private static async Task WriteBufferAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
        await stream.WriteAsync(lengthBuffer, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
    }

    private static async Task<PacketType> ReadPacketTypeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = await ReadExactAsync(stream, 1, cancellationToken);
        return (PacketType)buffer[0];
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
                throw new EndOfStreamException("The connection was closed.");
            }

            read += currentRead;
        }

        return buffer;
    }
}