using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Tests.Protocol;

public class PacketRoundTripTests
{
    private static async Task<Packet> RoundTripAsync(Func<Stream, CancellationToken, ValueTask> write)
    {
        using var stream = new MemoryStream();
        await write(stream, CancellationToken.None);
        stream.Position = 0;
        var packet = await PacketReader.ReadAsync(stream, CancellationToken.None);
        Assert.Equal(stream.Length, stream.Position); // nothing left unread
        return packet;
    }

    [Fact]
    public async Task Handshake_carries_version_and_pin()
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteHandshakeAsync(s, "1234", ct));
        var handshake = Assert.IsType<HandshakePacket>(packet);
        Assert.Equal(ProtocolLimits.Version, handshake.Version);
        Assert.Equal("1234", handshake.Pin);
    }

    [Theory]
    [InlineData(true, "Authenticated.")]
    [InlineData(false, "Invalid PIN.")]
    public async Task HandshakeResult_round_trips(bool success, string message)
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteHandshakeResultAsync(s, success, message, ct));
        var result = Assert.IsType<HandshakeResultPacket>(packet);
        Assert.Equal(success, result.Success);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task Frame_round_trips_geometry_and_bytes()
    {
        var image = new byte[] { 1, 2, 3, 4, 5 };
        var frame = new RemoteFrame(RemoteFrameKind.Delta, 1920, 1080, 100, 200, 300, 400, image);

        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteFrameAsync(s, frame, ct));
        var decoded = Assert.IsType<FramePacket>(packet).Frame;

        Assert.Equal(frame with { ImageBytes = default }, decoded with { ImageBytes = default });
        Assert.Equal(image, decoded.ImageBytes.ToArray());
    }

    [Fact]
    public async Task MouseMove_round_trips()
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteMouseMoveAsync(s, -5, 77, ct));
        var input = Assert.IsType<InputPacket>(packet).Input;
        Assert.Equal(new RemoteInputMessage(PacketType.MouseMove, X: -5, Y: 77), input);
    }

    [Theory]
    [InlineData(RemoteMouseButton.Left, true)]
    [InlineData(RemoteMouseButton.Right, false)]
    [InlineData(RemoteMouseButton.Middle, true)]
    public async Task MouseButton_round_trips(RemoteMouseButton button, bool isDown)
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteMouseButtonAsync(s, button, isDown, 10, 20, ct));
        var input = Assert.IsType<InputPacket>(packet).Input;
        Assert.Equal(isDown ? PacketType.MouseDown : PacketType.MouseUp, input.Type);
        Assert.Equal(button, input.Button);
        Assert.Equal((10, 20), (input.X, input.Y));
    }

    [Fact]
    public async Task MouseWheel_round_trips_negative_delta()
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteMouseWheelAsync(s, 1, 2, -120, ct));
        var input = Assert.IsType<InputPacket>(packet).Input;
        Assert.Equal(new RemoteInputMessage(PacketType.MouseWheel, X: 1, Y: 2, Delta: -120), input);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Key_round_trips(bool isDown)
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteKeyAsync(s, 0x41, isDown, ct));
        var input = Assert.IsType<InputPacket>(packet).Input;
        Assert.Equal(isDown ? PacketType.KeyDown : PacketType.KeyUp, input.Type);
        Assert.Equal(0x41, input.KeyCode);
    }

    [Fact]
    public async Task Clipboard_packets_round_trip_unicode()
    {
        const string text = "olá 🌎 - tabs\tand\nnewlines";

        var set = await RoundTripAsync((s, ct) => PacketWriter.WriteClipboardSetAsync(s, text, ct));
        Assert.Equal(text, Assert.IsType<ClipboardSetPacket>(set).Text);

        var response = await RoundTripAsync((s, ct) => PacketWriter.WriteClipboardResponseAsync(s, text, ct));
        Assert.Equal(text, Assert.IsType<ClipboardResponsePacket>(response).Text);

        var request = await RoundTripAsync(PacketWriter.WriteClipboardRequestAsync);
        Assert.IsType<ClipboardRequestPacket>(request);
    }

    [Fact]
    public async Task File_packets_round_trip()
    {
        var metadata = await RoundTripAsync((s, ct) => PacketWriter.WriteFileMetadataAsync(s, "report.pdf", 123_456_789_012L, ct));
        var decodedMetadata = Assert.IsType<FileMetadataPacket>(metadata);
        Assert.Equal("report.pdf", decodedMetadata.FileName);
        Assert.Equal(123_456_789_012L, decodedMetadata.FileSize);

        var content = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        var chunk = await RoundTripAsync((s, ct) => PacketWriter.WriteFileChunkAsync(s, content, ct));
        Assert.Equal(content, Assert.IsType<FileChunkPacket>(chunk).Content.ToArray());
    }

    [Fact]
    public async Task Monitor_packets_round_trip()
    {
        var select = await RoundTripAsync((s, ct) => PacketWriter.WriteMonitorSelectAsync(s, @"\\.\DISPLAY2", ct));
        Assert.Equal(@"\\.\DISPLAY2", Assert.IsType<MonitorSelectPacket>(select).DeviceName);

        var monitors = new[]
        {
            new RemoteMonitorInfo(@"\\.\DISPLAY1", "Monitor 1: 1920x1080 at 0,0 (Primary)", true, false),
            new RemoteMonitorInfo(@"\\.\DISPLAY2", "Monitor 2: 2560x1440 at 1920,0", false, true),
        };
        var list = await RoundTripAsync((s, ct) => PacketWriter.WriteMonitorListAsync(s, monitors, ct));
        Assert.Equal(monitors, Assert.IsType<MonitorListPacket>(list).Monitors);
    }

    [Fact]
    public async Task Ping_and_pong_echo_timestamp()
    {
        var ping = await RoundTripAsync((s, ct) => PacketWriter.WritePingAsync(s, long.MaxValue, ct));
        Assert.Equal(long.MaxValue, Assert.IsType<PingPacket>(ping).Timestamp);

        var pong = await RoundTripAsync((s, ct) => PacketWriter.WritePongAsync(s, -42, ct));
        Assert.Equal(-42, Assert.IsType<PongPacket>(pong).Timestamp);
    }

    [Theory]
    [InlineData(StreamQuality.Balanced)]
    [InlineData(StreamQuality.Sharp)]
    [InlineData(StreamQuality.Lossless)]
    public async Task QualitySelect_round_trips(StreamQuality quality)
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteQualitySelectAsync(s, quality, ct));
        Assert.Equal(quality, Assert.IsType<QualitySelectPacket>(packet).Quality);
    }

    [Fact]
    public async Task SecureAttention_round_trips()
    {
        var packet = await RoundTripAsync(PacketWriter.WriteSecureAttentionAsync);
        Assert.IsType<SecureAttentionPacket>(packet);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StreamControl_round_trips(bool active)
    {
        var packet = await RoundTripAsync((s, ct) => PacketWriter.WriteStreamControlAsync(s, active, ct));
        Assert.Equal(active, Assert.IsType<StreamControlPacket>(packet).Active);
    }

    [Fact]
    public async Task Several_packets_back_to_back_stay_framed()
    {
        using var stream = new MemoryStream();
        await PacketWriter.WriteMouseMoveAsync(stream, 1, 1, CancellationToken.None);
        await PacketWriter.WriteClipboardSetAsync(stream, "x", CancellationToken.None);
        await PacketWriter.WriteKeyAsync(stream, 13, true, CancellationToken.None);
        stream.Position = 0;

        Assert.IsType<InputPacket>(await PacketReader.ReadAsync(stream, CancellationToken.None));
        Assert.IsType<ClipboardSetPacket>(await PacketReader.ReadAsync(stream, CancellationToken.None));
        Assert.IsType<InputPacket>(await PacketReader.ReadAsync(stream, CancellationToken.None));
        await Assert.ThrowsAsync<EndOfStreamException>(() => PacketReader.ReadAsync(stream, CancellationToken.None).AsTask());
    }
}
