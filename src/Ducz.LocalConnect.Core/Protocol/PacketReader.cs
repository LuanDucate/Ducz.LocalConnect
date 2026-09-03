using System.Buffers.Binary;
using System.Text;

namespace Ducz.LocalConnect.Core.Protocol;

public static class PacketReader
{
    public static async ValueTask<Packet> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var typeByte = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        var type = (PacketType)typeByte;

        return type switch
        {
            PacketType.Handshake => await ReadHandshakeAsync(stream, cancellationToken).ConfigureAwait(false),
            PacketType.HandshakeResult => await ReadHandshakeResultAsync(stream, cancellationToken).ConfigureAwait(false),
            PacketType.Frame => new FramePacket(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.MouseMove => new InputPacket(await ReadMouseMoveAsync(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.MouseDown or PacketType.MouseUp => new InputPacket(await ReadMouseButtonAsync(stream, type, cancellationToken).ConfigureAwait(false)),
            PacketType.MouseWheel => new InputPacket(await ReadMouseWheelAsync(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.KeyDown or PacketType.KeyUp => new InputPacket(await ReadKeyAsync(stream, type, cancellationToken).ConfigureAwait(false)),
            PacketType.ClipboardSet => new ClipboardSetPacket(await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.ClipboardRequest => new ClipboardRequestPacket(),
            PacketType.ClipboardResponse => new ClipboardResponsePacket(await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.FileMetadata => await ReadFileMetadataAsync(stream, cancellationToken).ConfigureAwait(false),
            PacketType.FileChunk => new FileChunkPacket(await ReadBufferAsync(stream, ProtocolLimits.MaxFileChunkBytes, cancellationToken).ConfigureAwait(false)),
            PacketType.MonitorSelect => new MonitorSelectPacket(await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.MonitorList => new MonitorListPacket(await ReadMonitorListAsync(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.Ping => new PingPacket(await ReadInt64Async(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.Pong => new PongPacket(await ReadInt64Async(stream, cancellationToken).ConfigureAwait(false)),
            PacketType.QualitySelect => await ReadQualitySelectAsync(stream, cancellationToken).ConfigureAwait(false),
            PacketType.SecureAttention => new SecureAttentionPacket(),
            PacketType.StreamControl => new StreamControlPacket(await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) == 1),
            _ => throw new ProtocolException($"Unknown packet type 0x{typeByte:X2}."),
        };
    }

    private static async ValueTask<QualitySelectPacket> ReadQualitySelectAsync(Stream stream, CancellationToken cancellationToken)
    {
        var value = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        var quality = (StreamQuality)value;
        if (quality is not (StreamQuality.Balanced or StreamQuality.Sharp or StreamQuality.Lossless))
        {
            throw new ProtocolException($"Unknown stream quality {value}.");
        }

        return new QualitySelectPacket(quality);
    }

    private static async ValueTask<HandshakePacket> ReadHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = await ReadExactAsync(stream, 4 + 2, cancellationToken).ConfigureAwait(false);
        if (!head.AsSpan(0, 4).SequenceEqual(ProtocolLimits.Magic))
        {
            throw new ProtocolException("The peer is not a Ducz LocalConnect host or client.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(4, 2));
        var pin = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
        return new HandshakePacket(version, pin);
    }

    private static async ValueTask<HandshakeResultPacket> ReadHandshakeResultAsync(Stream stream, CancellationToken cancellationToken)
    {
        var success = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) == 1;
        var message = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
        return new HandshakeResultPacket(success, message);
    }

    private static async ValueTask<RemoteFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, 1 + 6 * 4, cancellationToken).ConfigureAwait(false);
        var kind = (RemoteFrameKind)header[0];
        if (kind is not (RemoteFrameKind.Full or RemoteFrameKind.Delta))
        {
            throw new ProtocolException($"Unknown frame kind {header[0]}.");
        }

        var span = header.AsSpan(1);
        var desktopWidth = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
        var desktopHeight = BinaryPrimitives.ReadInt32LittleEndian(span[4..8]);
        var x = BinaryPrimitives.ReadInt32LittleEndian(span[8..12]);
        var y = BinaryPrimitives.ReadInt32LittleEndian(span[12..16]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(span[16..20]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(span[20..24]);

        if (!IsValidDimension(desktopWidth) || !IsValidDimension(desktopHeight) || !IsValidDimension(width) || !IsValidDimension(height)
            || x < 0 || y < 0 || x + width > desktopWidth || y + height > desktopHeight)
        {
            throw new ProtocolException("Frame geometry is out of range.");
        }

        var imageBytes = await ReadBufferAsync(stream, ProtocolLimits.MaxFrameBytes, cancellationToken).ConfigureAwait(false);
        return new RemoteFrame(kind, desktopWidth, desktopHeight, x, y, width, height, imageBytes);
    }

    private static bool IsValidDimension(int value) => value > 0 && value <= ProtocolLimits.MaxDesktopDimension;

    private static async ValueTask<RemoteInputMessage> ReadMouseMoveAsync(Stream stream, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 8, cancellationToken).ConfigureAwait(false);
        return new RemoteInputMessage(
            PacketType.MouseMove,
            X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)),
            Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)));
    }

    private static async ValueTask<RemoteInputMessage> ReadMouseButtonAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 9, cancellationToken).ConfigureAwait(false);
        var button = (RemoteMouseButton)payload[0];
        if (button is not (RemoteMouseButton.Left or RemoteMouseButton.Right or RemoteMouseButton.Middle))
        {
            throw new ProtocolException($"Unknown mouse button {payload[0]}.");
        }

        return new RemoteInputMessage(
            type,
            X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1, 4)),
            Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(5, 4)),
            Button: button);
    }

    private static async ValueTask<RemoteInputMessage> ReadMouseWheelAsync(Stream stream, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 12, cancellationToken).ConfigureAwait(false);
        return new RemoteInputMessage(
            PacketType.MouseWheel,
            X: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)),
            Y: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)),
            Delta: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4)));
    }

    private static async ValueTask<RemoteInputMessage> ReadKeyAsync(Stream stream, PacketType type, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        return new RemoteInputMessage(type, KeyCode: BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static async ValueTask<FileMetadataPacket> ReadFileMetadataAsync(Stream stream, CancellationToken cancellationToken)
    {
        var fileName = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
        var size = await ReadInt64Async(stream, cancellationToken).ConfigureAwait(false);
        if (size < 0)
        {
            throw new ProtocolException("Negative file size.");
        }

        return new FileMetadataPacket(fileName, size);
    }

    private static async ValueTask<IReadOnlyList<RemoteMonitorInfo>> ReadMonitorListAsync(Stream stream, CancellationToken cancellationToken)
    {
        var countBytes = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(countBytes);
        if (count < 0 || count > ProtocolLimits.MaxMonitors)
        {
            throw new ProtocolException($"Monitor count {count} is out of range.");
        }

        var monitors = new List<RemoteMonitorInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var deviceName = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
            var displayName = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
            var flags = await ReadExactAsync(stream, 2, cancellationToken).ConfigureAwait(false);
            monitors.Add(new RemoteMonitorInfo(deviceName, displayName, flags[0] == 1, flags[1] == 1));
        }

        return monitors;
    }

    private static async ValueTask<string> ReadStringAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = await ReadBufferAsync(stream, ProtocolLimits.MaxStringBytes, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes.Span);
    }

    private static async ValueTask<long> ReadInt64Async(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = await ReadExactAsync(stream, 8, cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    /// <summary>Reads [int32 length][bytes], refusing lengths above <paramref name="maxLength"/> before allocating.</summary>
    private static async ValueTask<ReadOnlyMemory<byte>> ReadBufferAsync(Stream stream, int maxLength, CancellationToken cancellationToken)
    {
        var lengthBytes = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length < 0 || length > maxLength)
        {
            throw new ProtocolException($"Payload length {length} exceeds the limit of {maxLength} bytes.");
        }

        if (length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = await ReadExactAsync(stream, 1, cancellationToken).ConfigureAwait(false);
        return bytes[0];
    }

    private static async ValueTask<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }
}
