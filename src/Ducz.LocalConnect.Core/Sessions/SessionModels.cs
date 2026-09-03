using Ducz.LocalConnect.Core.Capture;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Sessions;

public enum HostState
{
    Stopped,
    Listening,
    Connected,
}

public sealed record HostOptions(int Port, string Pin, string ReceivedFilesDirectory)
{
    public CaptureOptions Capture { get; init; } = CaptureOptions.Default;

    public static string DefaultReceivedFilesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Ducz LocalConnect Received Files");

    public void Validate()
    {
        if (Port is < ProtocolLimits.MinPort or > ProtocolLimits.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), $"Port must be between {ProtocolLimits.MinPort} and {ProtocolLimits.MaxPort}.");
        }

        if (string.IsNullOrWhiteSpace(Pin))
        {
            throw new ArgumentException("A PIN is required.", nameof(Pin));
        }
    }
}

public sealed record ClientOptions(string Host, int Port, string Pin)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Enter the host IP address or name.", nameof(Host));
        }

        if (Port is < ProtocolLimits.MinPort or > ProtocolLimits.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), $"Port must be between {ProtocolLimits.MinPort} and {ProtocolLimits.MaxPort}.");
        }
    }
}

public sealed record SessionStats(double FramesPerSecond, double KilobytesPerSecond, double LatencyMilliseconds);

public static class Liveness
{
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);
}
