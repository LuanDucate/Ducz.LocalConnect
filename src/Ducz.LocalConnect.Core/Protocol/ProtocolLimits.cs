namespace Ducz.LocalConnect.Core.Protocol;

public static class ProtocolLimits
{
    /// <summary>ASCII "DLC2" sent at the start of the handshake.</summary>
    public static ReadOnlySpan<byte> Magic => "DLC2"u8;

    public const ushort Version = 2;

    public const int MaxStringBytes = 64 * 1024;
    public const int MaxFileChunkBytes = 1024 * 1024;
    public const int MaxFrameBytes = 32 * 1024 * 1024;
    public const int MaxMonitors = 32;

    /// <summary>Largest side length we accept for a remote desktop; guards the frame header.</summary>
    public const int MaxDesktopDimension = 16_384;

    /// <summary>Audio port is always the video port plus this offset.</summary>
    public const int AudioPortOffset = 1;

    public const int DefaultPort = 5050;
    public const int MinPort = 1024;
    public const int MaxPort = 65534; // leaves room for port + AudioPortOffset
}
