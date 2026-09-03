using System.Buffers.Binary;
using System.Text;

namespace Ducz.LocalConnect.Core.Protocol;
public static class PacketWriter
{
    public static ValueTask WriteHandshakeAsync(Stream stream, string pin, CancellationToken cancellationToken)
    {
        var pinBytes = Encoding.UTF8.GetBytes(pin);
        var buffer = new byte[1 + 4 + 2 + 4 + pinBytes.Length];
        buffer[0] = (byte)PacketType.Handshake;
        ProtocolLimits.Magic.CopyTo(buffer.AsSpan(1, 4));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(5, 2), ProtocolLimits.Version);
        WriteLengthPrefixed(buffer.AsSpan(7), pinBytes);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static ValueTask WriteHandshakeResultAsync(Stream stream, bool success, string message, CancellationToken cancellationToken)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var buffer = new byte[1 + 1 + 4 + messageBytes.Length];
        buffer[0] = (byte)PacketType.HandshakeResult;
        buffer[1] = success ? (byte)1 : (byte)0;
        WriteLengthPrefixed(buffer.AsSpan(2), messageBytes);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static async ValueTask WriteFrameAsync(Stream stream, RemoteFrame frame, CancellationToken cancellationToken)
    {
        // [type][kind][desktopW][desktopH][x][y][w][h][len]  then the JPEG bytes as a second write,
        // which avoids copying what can be a multi-megabyte image into a temporary buffer.
        var header = new byte[2 + 6 * 4 + 4];
        header[0] = (byte)PacketType.Frame;
        header[1] = (byte)frame.Kind;
        var span = header.AsSpan(2);
        BinaryPrimitives.WriteInt32LittleEndian(span[..4], frame.DesktopWidth);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], frame.DesktopHeight);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..12], frame.X);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..16], frame.Y);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..24], frame.Height);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..28], frame.ImageBytes.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame.ImageBytes, cancellationToken).ConfigureAwait(false);
    }

    public static ValueTask WriteMouseMoveAsync(Stream stream, int x, int y, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 + 8];
        buffer[0] = (byte)PacketType.MouseMove;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(5, 4), y);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static ValueTask WriteMouseButtonAsync(Stream stream, RemoteMouseButton button, bool isDown, int x, int y, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 + 1 + 8];
        buffer[0] = (byte)(isDown ? PacketType.MouseDown : PacketType.MouseUp);
        buffer[1] = (byte)button;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(2, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(6, 4), y);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static ValueTask WriteMouseWheelAsync(Stream stream, int x, int y, int delta, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 + 12];
        buffer[0] = (byte)PacketType.MouseWheel;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(5, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(9, 4), delta);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static ValueTask WriteKeyAsync(Stream stream, int keyCode, bool isDown, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 + 4];
        buffer[0] = (byte)(isDown ? PacketType.KeyDown : PacketType.KeyUp);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1, 4), keyCode);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static ValueTask WriteClipboardSetAsync(Stream stream, string text, CancellationToken cancellationToken)
        => WriteStringPacketAsync(stream, PacketType.ClipboardSet, text, cancellationToken);

    public static ValueTask WriteClipboardRequestAsync(Stream stream, CancellationToken cancellationToken)
        => stream.WriteAsync(new[] { (byte)PacketType.ClipboardRequest }, cancellationToken);

    public static ValueTask WriteClipboardResponseAsync(Stream stream, string text, CancellationToken cancellationToken)
        => WriteStringPacketAsync(stream, PacketType.ClipboardResponse, text, cancellationToken);

    public static ValueTask WriteFileMetadataAsync(Stream stream, string fileName, long fileSize, CancellationToken cancellationToken)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fileName);
        var buffer = new byte[1 + 4 + nameBytes.Length + 8];
        buffer[0] = (byte)PacketType.FileMetadata;
        var next = WriteLengthPrefixed(buffer.AsSpan(1), nameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(1 + next, 8), fileSize);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static async ValueTask WriteFileChunkAsync(Stream stream, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var header = new byte[1 + 4];
        header[0] = (byte)PacketType.FileChunk;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1, 4), content.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    public static ValueTask WriteMonitorSelectAsync(Stream stream, string deviceName, CancellationToken cancellationToken)
        => WriteStringPacketAsync(stream, PacketType.MonitorSelect, deviceName, cancellationToken);

    public static ValueTask WriteMonitorListAsync(Stream stream, IReadOnlyList<RemoteMonitorInfo> monitors, CancellationToken cancellationToken)
    {
        var encoded = new List<(byte[] Device, byte[] Display)>(monitors.Count);
        var size = 1 + 4;
        foreach (var monitor in monitors)
        {
            var device = Encoding.UTF8.GetBytes(monitor.DeviceName);
            var display = Encoding.UTF8.GetBytes(monitor.DisplayName);
            encoded.Add((device, display));
            size += 4 + device.Length + 4 + display.Length + 2;
        }

        var buffer = new byte[size];
        buffer[0] = (byte)PacketType.MonitorList;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1, 4), monitors.Count);
        var offset = 5;
        for (var i = 0; i < monitors.Count; i++)
        {
            offset += WriteLengthPrefixed(buffer.AsSpan(offset), encoded[i].Device);
            offset += WriteLengthPrefixed(buffer.AsSpan(offset), encoded[i].Display);
            buffer[offset++] = monitors[i].IsPrimary ? (byte)1 : (byte)0;
            buffer[offset++] = monitors[i].IsSelected ? (byte)1 : (byte)0;
        }

        return stream.WriteAsync(buffer, cancellationToken);
    }

    public static ValueTask WritePingAsync(Stream stream, long timestamp, CancellationToken cancellationToken)
        => WriteTimestampPacketAsync(stream, PacketType.Ping, timestamp, cancellationToken);

    public static ValueTask WritePongAsync(Stream stream, long timestamp, CancellationToken cancellationToken)
        => WriteTimestampPacketAsync(stream, PacketType.Pong, timestamp, cancellationToken);

    public static ValueTask WriteQualitySelectAsync(Stream stream, StreamQuality quality, CancellationToken cancellationToken)
        => stream.WriteAsync(new[] { (byte)PacketType.QualitySelect, (byte)quality }, cancellationToken);

    public static ValueTask WriteSecureAttentionAsync(Stream stream, CancellationToken cancellationToken)
        => stream.WriteAsync(new[] { (byte)PacketType.SecureAttention }, cancellationToken);

    public static ValueTask WriteStreamControlAsync(Stream stream, bool active, CancellationToken cancellationToken)
        => stream.WriteAsync(new[] { (byte)PacketType.StreamControl, active ? (byte)1 : (byte)0 }, cancellationToken);

    private static ValueTask WriteTimestampPacketAsync(Stream stream, PacketType type, long timestamp, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 + 8];
        buffer[0] = (byte)type;
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(1, 8), timestamp);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    private static ValueTask WriteStringPacketAsync(Stream stream, PacketType type, string value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var buffer = new byte[1 + 4 + bytes.Length];
        buffer[0] = (byte)type;
        WriteLengthPrefixed(buffer.AsSpan(1), bytes);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>Writes [int32 length][bytes] and returns how many bytes were written.</summary>
    private static int WriteLengthPrefixed(Span<byte> destination, ReadOnlySpan<byte> payload)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[..4], payload.Length);
        payload.CopyTo(destination[4..]);
        return 4 + payload.Length;
    }
}
