using Ducz.LocalConnect.Core.Protocol;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.App.Services;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public sealed class PinnedConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = ProtocolLimits.DefaultPort;

    public string Pin { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    public const int MaxRecentHosts = 8;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public int HostPort { get; set; } = ProtocolLimits.DefaultPort;

    public string HostPin { get; set; } = GeneratePin();

    public bool StartHostOnLaunch { get; set; }

    public bool StartHostWithWindows { get; set; }

    public string ReceivedFilesDirectory { get; set; } = HostOptions.DefaultReceivedFilesDirectory;

    public string LastHost { get; set; } = string.Empty;

    public int LastPort { get; set; } = ProtocolLimits.DefaultPort;

    public List<string> RecentHosts { get; set; } = new();

    public List<PinnedConnection> Pins { get; set; } = new();

    public bool AudioMuted { get; set; }

    public StreamQuality StreamQuality { get; set; } = StreamQuality.Balanced;

    public bool MinimizeToTray { get; set; } = true;

    public void RememberHost(string host)
    {
        host = host.Trim();
        if (host.Length == 0)
        {
            return;
        }

        RecentHosts.RemoveAll(entry => string.Equals(entry, host, StringComparison.OrdinalIgnoreCase));
        RecentHosts.Insert(0, host);
        if (RecentHosts.Count > MaxRecentHosts)
        {
            RecentHosts.RemoveRange(MaxRecentHosts, RecentHosts.Count - MaxRecentHosts);
        }

        LastHost = host;
    }

    public static string GeneratePin() => Random.Shared.Next(0, 1_000_000).ToString("D6");
}
