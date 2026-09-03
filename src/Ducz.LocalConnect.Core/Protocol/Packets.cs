namespace Ducz.LocalConnect.Core.Protocol;

public enum RemoteMouseButton : byte
{
    Left = 1,
    Right = 2,
    Middle = 3,
}

public enum RemoteFrameKind : byte
{
    Full = 1,
    Delta = 2,
}

/// <summary>A JPEG-encoded region of the shared desktop.</summary>
public sealed record RemoteFrame(
    RemoteFrameKind Kind,
    int DesktopWidth,
    int DesktopHeight,
    int X,
    int Y,
    int Width,
    int Height,
    ReadOnlyMemory<byte> ImageBytes);

public sealed record RemoteMonitorInfo(string DeviceName, string DisplayName, bool IsPrimary, bool IsSelected);

/// <summary>One mouse or keyboard event. Which fields matter depends on <see cref="Type"/>.</summary>
public sealed record RemoteInputMessage(
    PacketType Type,
    int X = 0,
    int Y = 0,
    int Delta = 0,
    int KeyCode = 0,
    RemoteMouseButton Button = RemoteMouseButton.Left);

/// <summary>Base of every decoded packet. Both directions share one hierarchy; each side
/// simply rejects the types it never expects to receive.</summary>
public abstract record Packet(PacketType Type);

public sealed record HandshakePacket(ushort Version, string Pin) : Packet(PacketType.Handshake);
public sealed record HandshakeResultPacket(bool Success, string Message) : Packet(PacketType.HandshakeResult);
public sealed record FramePacket(RemoteFrame Frame) : Packet(PacketType.Frame);
public sealed record InputPacket(RemoteInputMessage Input) : Packet(Input.Type);
public sealed record ClipboardSetPacket(string Text) : Packet(PacketType.ClipboardSet);
public sealed record ClipboardRequestPacket() : Packet(PacketType.ClipboardRequest);
public sealed record ClipboardResponsePacket(string Text) : Packet(PacketType.ClipboardResponse);
public sealed record FileMetadataPacket(string FileName, long FileSize) : Packet(PacketType.FileMetadata);
public sealed record FileChunkPacket(ReadOnlyMemory<byte> Content) : Packet(PacketType.FileChunk);
public sealed record MonitorSelectPacket(string DeviceName) : Packet(PacketType.MonitorSelect);
public sealed record MonitorListPacket(IReadOnlyList<RemoteMonitorInfo> Monitors) : Packet(PacketType.MonitorList);

/// <summary><paramref name="Timestamp"/> is opaque to the receiver and echoed back in the Pong,
/// so the sender can measure round-trip time with its own clock.</summary>
public sealed record PingPacket(long Timestamp) : Packet(PacketType.Ping);
public sealed record PongPacket(long Timestamp) : Packet(PacketType.Pong);

/// <summary>Trade-off between bandwidth and picture fidelity, chosen by the client.</summary>
public enum StreamQuality : byte
{
    /// <summary>JPEG everywhere. Fine for Wi-Fi.</summary>
    Balanced = 1,

    /// <summary>Lossless (PNG) delta patches, high-quality JPEG full frames. Crisp text on a wired LAN.</summary>
    Sharp = 2,

    /// <summary>Everything lossless. Pixel-perfect, bandwidth-hungry.</summary>
    Lossless = 3,
}

public sealed record QualitySelectPacket(StreamQuality Quality) : Packet(PacketType.QualitySelect);

public sealed record SecureAttentionPacket() : Packet(PacketType.SecureAttention);

public sealed record StreamControlPacket(bool Active) : Packet(PacketType.StreamControl);
