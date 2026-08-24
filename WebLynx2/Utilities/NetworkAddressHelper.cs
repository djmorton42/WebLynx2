using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WebLynx2.Utilities;

public static class NetworkAddressHelper
{
    /// <summary>
    /// Returns IPv4 addresses on operational adapters, including loopback for same-machine clients.
    /// </summary>
    public static IReadOnlyList<string> GetLocalIPv4Addresses()
    {
        var result = new List<(bool IsLoopback, string Display)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var address = unicast.Address;
                result.Add((System.Net.IPAddress.IsLoopback(address), $"{nic.Name}: {address}"));
            }
        }

        result.Sort(static (a, b) =>
        {
            if (a.IsLoopback != b.IsLoopback)
                return a.IsLoopback ? -1 : 1;

            return string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
        });

        return result.ConvertAll(static e => e.Display);
    }
}
