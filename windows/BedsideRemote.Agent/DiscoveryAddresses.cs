using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BedsideRemote.Core;

namespace BedsideRemote.Agent;

internal static class DiscoveryAddresses
{
    public static string ReadLocal()
    {
        var addresses = new List<IPAddress>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || !adapter.SupportsMulticast) continue;
            try { addresses.AddRange(adapter.GetIPProperties().UnicastAddresses.Select(x => x.Address)); }
            catch (NetworkInformationException) { }
        }
        return Encode(addresses);
    }

    internal static string Encode(IEnumerable<IPAddress> addresses)
    {
        // One DNS-SD TXT string is at most 255 bytes, including "addresses=".
        var selected = new List<string>();
        foreach (var address in addresses.Where(x => x.AddressFamily == AddressFamily.InterNetwork && Protocol.IsLan(x) && !IPAddress.IsLoopback(x)).Distinct())
        {
            var text = address.ToString();
            if (selected.Count == 16 || string.Join(',', selected.Append(text)).Length > 245) break;
            selected.Add(text);
        }
        return string.Join(',', selected);
    }
}
