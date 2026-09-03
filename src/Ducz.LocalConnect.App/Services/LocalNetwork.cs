using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Ducz.LocalConnect.App.Services;

public static class LocalNetwork
{
    public sealed record Address(string Ip, string InterfaceName);

    public static IReadOnlyList<Address> GetIPv4Addresses()
    {
        var results = new List<Address>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var ip = unicast.Address.ToString();
                if (ip.StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue; 
                }

                results.Add(new Address(ip, nic.Name));
            }
        }

        return results
            .OrderBy(address => IsVirtualAdapterName(address.InterfaceName) ? 1 : 0)
            .ThenBy(address => address.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsVirtualAdapterName(string name) =>
        name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VMware", StringComparison.OrdinalIgnoreCase)
        || name.Contains("WSL", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VPN", StringComparison.OrdinalIgnoreCase);
}
