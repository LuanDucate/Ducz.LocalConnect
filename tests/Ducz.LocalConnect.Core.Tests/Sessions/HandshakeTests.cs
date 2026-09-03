using System.Net;
using System.Net.Sockets;
using Ducz.LocalConnect.Core.Protocol;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.Core.Tests.Sessions;

public class HandshakeTests
{
    private static readonly CancellationToken Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private static int FreePort() => Support.TestPorts.FreePair();

    private static HostServer StartServer(int port, string pin)
    {
        var server = new HostServer();
        server.Start(new HostOptions(port, pin, Path.GetTempPath()));
        return server;
    }

    [Fact]
    public async Task Wrong_pin_is_rejected_with_a_clear_message()
    {
        var port = FreePort();
        await using var server = StartServer(port, "1234");
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, Timeout);
        var stream = client.GetStream();

        await PacketWriter.WriteHandshakeAsync(stream, "9999", Timeout);
        var result = Assert.IsType<HandshakeResultPacket>(await PacketReader.ReadAsync(stream, Timeout));

        Assert.False(result.Success);
        Assert.Equal("Invalid PIN.", result.Message);
    }

    [Fact]
    public async Task Correct_pin_is_accepted_and_monitor_list_follows()
    {
        var port = FreePort();
        await using var server = StartServer(port, "1234");
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, Timeout);
        var stream = client.GetStream();

        await PacketWriter.WriteHandshakeAsync(stream, "1234", Timeout);
        var result = Assert.IsType<HandshakeResultPacket>(await PacketReader.ReadAsync(stream, Timeout));
        Assert.True(result.Success);

        var monitors = Assert.IsType<MonitorListPacket>(await PacketReader.ReadAsync(stream, Timeout));
        Assert.NotEmpty(monitors.Monitors);
        Assert.Single(monitors.Monitors, monitor => monitor.IsSelected);
    }

    [Fact]
    public async Task Non_localconnect_client_is_told_so()
    {
        var port = FreePort();
        await using var server = StartServer(port, "1234");
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, Timeout);
        var stream = client.GetStream();

        var garbage = new byte[] { (byte)PacketType.Handshake, (byte)'H', (byte)'T', (byte)'T', (byte)'P', 2, 0, 0, 0, 0, 0 };
        await stream.WriteAsync(garbage, Timeout);

        var result = Assert.IsType<HandshakeResultPacket>(await PacketReader.ReadAsync(stream, Timeout));
        Assert.False(result.Success);
        Assert.Contains("not a Ducz LocalConnect", result.Message);
    }

    [Fact]
    public async Task ClientSession_surfaces_a_rejected_pin_as_an_exception()
    {
        var port = FreePort();
        await using var server = StartServer(port, "1234");
        await using var session = new ClientSession();

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => session.ConnectAsync(new ClientOptions("127.0.0.1", port, "0000"), Timeout));

        Assert.Equal("Invalid PIN.", exception.Message);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task ClientSession_times_out_when_nothing_listens()
    {
        var port = FreePort();
        await using var session = new ClientSession();

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => session.ConnectAsync(new ClientOptions("127.0.0.1", port, "1234"), Timeout));

        Assert.True(exception is TimeoutException or SocketException, exception.GetType().Name);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public void Options_are_validated_up_front()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostOptions(80, "1234", Path.GetTempPath()).Validate());
        Assert.Throws<ArgumentException>(() => new HostOptions(5050, "  ", Path.GetTempPath()).Validate());
        Assert.Throws<ArgumentException>(() => new ClientOptions("", 5050, "1234").Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientOptions("host", 65535, "1234").Validate());
    }
}
