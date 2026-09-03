using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ducz.LocalConnect.Core.Capture;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.Core.Tests.Sessions;

public class SessionLifecycleTests
{
    private static readonly TimeSpan PromptShutdown = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Loopback_session_streams_frames_and_shuts_down_promptly()
    {
        try
        {
            DisplayInfo.GetAll();
        }
        catch (InvalidOperationException)
        {
            return; // headless environment: nothing to capture, nothing to assert
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var port = FreePort();
        var receivedFiles = Path.Combine(Path.GetTempPath(), "ducz-lifecycle-" + Guid.NewGuid().ToString("N"));

        await using var server = new HostServer();
        await using var client = new ClientSession();
        server.Start(new HostOptions(port, "1234", receivedFiles));

        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.FrameReceived += frame => firstFrame.TrySetResult();

        await client.ConnectAsync(new ClientOptions("127.0.0.1", port, "1234"), timeout.Token);
        Assert.True(client.IsConnected);
        Assert.NotEmpty(client.Monitors);

        await firstFrame.Task.WaitAsync(timeout.Token);

        var clock = Stopwatch.StartNew();
        await client.DisconnectAsync();
        var clientShutdown = clock.Elapsed;
        Assert.False(client.IsConnected);

        clock.Restart();
        await server.StopAsync();
        var serverShutdown = clock.Elapsed;

        Assert.True(clientShutdown < PromptShutdown, $"Client disconnect took {clientShutdown.TotalMilliseconds:0} ms.");
        Assert.True(serverShutdown < PromptShutdown, $"Host stop took {serverShutdown.TotalMilliseconds:0} ms.");
        Assert.Equal(HostState.Stopped, server.State);
    }

    [Fact]
    public async Task Host_returns_to_listening_after_the_client_drops()
    {
        try
        {
            DisplayInfo.GetAll();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var port = FreePort();
        await using var server = new HostServer();
        server.Start(new HostOptions(port, "1234", Path.GetTempPath()));

        var listeningAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientDisconnected += () => listeningAgain.TrySetResult();

        await using (var client = new ClientSession())
        {
            await client.ConnectAsync(new ClientOptions("127.0.0.1", port, "1234"), timeout.Token);
            Assert.Equal(HostState.Connected, server.State);
        } // disposed abruptly, like a crashed client

        await listeningAgain.Task.WaitAsync(timeout.Token);
        Assert.Equal(HostState.Listening, server.State);
    }

    [Fact]
    public async Task A_new_client_takes_over_from_the_previous_one()
    {
        try
        {
            DisplayInfo.GetAll();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var port = FreePort();
        await using var server = new HostServer();
        server.Start(new HostOptions(port, "1234", Path.GetTempPath()));

        await using var first = new ClientSession();
        var firstDropped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Disconnected += _ => firstDropped.TrySetResult();
        await first.ConnectAsync(new ClientOptions("127.0.0.1", port, "1234"), timeout.Token);
        Assert.True(first.IsConnected);

        // A second client reconnecting must be let in (takeover), not rejected as "already connected".
        await using var second = new ClientSession();
        await second.ConnectAsync(new ClientOptions("127.0.0.1", port, "1234"), timeout.Token);
        Assert.True(second.IsConnected);

        await firstDropped.Task.WaitAsync(timeout.Token);
        Assert.Equal(HostState.Connected, server.State);
    }

    private static int FreePort() => Support.TestPorts.FreePair();
}
