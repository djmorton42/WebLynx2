using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WebLynx2.Utilities;

public static class NetworkAddressHelper
{
    /// <summary>
    /// Returns display strings for IPv4 addresses on operational adapters, including loopback.
    /// </summary>
    public static IReadOnlyList<string> GetLocalIPv4Addresses()
    {
        var result = new List<(bool IsLoopback, string Display)>();

        foreach (var address in EnumerateLocalIPv4Addresses())
        {
            result.Add((IPAddress.IsLoopback(address.Address), $"{address.AdapterName}: {address.Address}"));
        }

        result.Sort(static (a, b) =>
        {
            if (a.IsLoopback != b.IsLoopback)
                return a.IsLoopback ? -1 : 1;

            return string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
        });

        return result.ConvertAll(static e => e.Display);
    }

    /// <summary>
    /// Builds a Kestrel listen URL for a port and optional bind address.
    /// Null/empty/"*" binds all IPv4 interfaces (0.0.0.0).
    /// </summary>
    public static string GetKestrelUrl(int port, string? listenAddress = null)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(listenAddress) || listenAddress is "*" or "+")
            return $"http://0.0.0.0:{port}";

        if (!IPAddress.TryParse(listenAddress.Trim(), out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                $"Listen address must be an IPv4 address, got '{listenAddress}'.",
                nameof(listenAddress));
        }

        return $"http://{ip}:{port}";
    }

    private static IEnumerable<(string AdapterName, IPAddress Address)> EnumerateLocalIPv4Addresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                yield return (nic.Name, unicast.Address);
            }
        }
    }
}
