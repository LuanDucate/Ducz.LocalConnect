using System.Net;
using System.Net.Sockets;

namespace Ducz.LocalConnect.Core.Tests.Support;

public static class TestPorts
{
    private const int RangeStart = 20_000;
    private const int RangeEnd = 40_000;

    public static int FreePair()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var port = Random.Shared.Next(RangeStart, RangeEnd);
            if (CanBind(port) && CanBind(port + 1))
            {
                return port;
            }
        }

        throw new InvalidOperationException("Could not find two consecutive free ports.");
    }

    private static bool CanBind(int port)
    {
        try
        {
            var probe = new TcpListener(IPAddress.Any, port);
            probe.Start();
            probe.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
