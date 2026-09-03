using System.Buffers.Binary;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Tests.Protocol;

/// <summary>Malformed input must fail fast with <see cref="ProtocolException"/>, before any large allocation.</summary>
public class PacketLimitTests
{
    private static Task<ProtocolException> ReadExpectingFailure(params byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return Assert.ThrowsAsync<ProtocolException>(() => PacketReader.ReadAsync(stream, CancellationToken.None).AsTask());
    }

    private static byte[] Int32(int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        return buffer;
    }

    [Fact]
    public async Task Unknown_packet_type_is_rejected()
    {
        var exception = await ReadExpectingFailure(0xFF);
        Assert.Contains("Unknown packet type", exception.Message);
    }

    [Fact]
    public async Task Wrong_magic_is_rejected()
    {
        var bytes = new List<byte> { (byte)PacketType.Handshake };
        bytes.AddRange("NOPE"u8.ToArray());
        bytes.AddRange(new byte[] { 2, 0 });
        bytes.AddRange(Int32(0));

        var exception = await ReadExpectingFailure(bytes.ToArray());
        Assert.Contains("not a Ducz LocalConnect", exception.Message);
    }

    [Fact]
    public async Task Oversized_string_is_rejected_before_allocation()
    {
        var bytes = new List<byte> { (byte)PacketType.ClipboardSet };
        bytes.AddRange(Int32(ProtocolLimits.MaxStringBytes + 1));

        var exception = await ReadExpectingFailure(bytes.ToArray());
        Assert.Contains("exceeds the limit", exception.Message);
    }

    [Fact]
    public async Task Negative_length_is_rejected()
    {
        var bytes = new List<byte> { (byte)PacketType.FileChunk };
        bytes.AddRange(Int32(-1));

        await ReadExpectingFailure(bytes.ToArray());
    }

    [Fact]
    public async Task Oversized_frame_is_rejected()
    {
        var bytes = new List<byte> { (byte)PacketType.Frame, (byte)RemoteFrameKind.Full };
        bytes.AddRange(Int32(1920));
        bytes.AddRange(Int32(1080));
        bytes.AddRange(Int32(0));
        bytes.AddRange(Int32(0));
        bytes.AddRange(Int32(1920));
        bytes.AddRange(Int32(1080));
        bytes.AddRange(Int32(ProtocolLimits.MaxFrameBytes + 1));

        await ReadExpectingFailure(bytes.ToArray());
    }

    [Theory]
    [InlineData(1920, 1080, 1900, 0, 100, 100)]  // patch runs past the right edge
    [InlineData(1920, 1080, 0, 0, 0, 100)]       // zero width
    [InlineData(1920, 1080, -1, 0, 10, 10)]      // negative origin
    [InlineData(100_000, 1080, 0, 0, 10, 10)]    // absurd desktop size
    public async Task Frame_geometry_out_of_range_is_rejected(int desktopWidth, int desktopHeight, int x, int y, int width, int height)
    {
        var bytes = new List<byte> { (byte)PacketType.Frame, (byte)RemoteFrameKind.Delta };
        bytes.AddRange(Int32(desktopWidth));
        bytes.AddRange(Int32(desktopHeight));
        bytes.AddRange(Int32(x));
        bytes.AddRange(Int32(y));
        bytes.AddRange(Int32(width));
        bytes.AddRange(Int32(height));
        bytes.AddRange(Int32(0));

        var exception = await ReadExpectingFailure(bytes.ToArray());
        Assert.Contains("geometry", exception.Message);
    }

    [Fact]
    public async Task Unknown_mouse_button_is_rejected()
    {
        var bytes = new List<byte> { (byte)PacketType.MouseDown, 9 };
        bytes.AddRange(new byte[8]);

        await ReadExpectingFailure(bytes.ToArray());
    }

    [Fact]
    public async Task Too_many_monitors_is_rejected()
    {
        var bytes = new List<byte> { (byte)PacketType.MonitorList };
        bytes.AddRange(Int32(ProtocolLimits.MaxMonitors + 1));

        await ReadExpectingFailure(bytes.ToArray());
    }

    [Fact]
    public async Task Unknown_quality_preset_is_rejected()
    {
        await ReadExpectingFailure((byte)PacketType.QualitySelect, 42);
    }

    [Fact]
    public async Task Truncated_packet_reports_end_of_stream()
    {
        var stream = new MemoryStream(new byte[] { (byte)PacketType.MouseMove, 1, 2 });
        await Assert.ThrowsAsync<EndOfStreamException>(() => PacketReader.ReadAsync(stream, CancellationToken.None).AsTask());
    }
}
