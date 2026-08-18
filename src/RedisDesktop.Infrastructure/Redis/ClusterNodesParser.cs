using System.Globalization;
using System.Net;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

internal static class ClusterNodesParser
{
    public static IReadOnlyList<(string Host, int Port)> ParseMasters(string clusterNodes)
    {
        var masters = new List<(string Host, int Port)>();
        if (string.IsNullOrWhiteSpace(clusterNodes))
        {
            return masters;
        }

        foreach (var raw in clusterNodes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var flags = parts[2];
            if (flags.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || flags.Contains("handshake", StringComparison.OrdinalIgnoreCase)
                || flags.Contains("noaddr", StringComparison.OrdinalIgnoreCase)
                || !flags.Contains("master", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var endpoint = parts[1];
            var at = endpoint.IndexOf('@');
            if (at >= 0)
            {
                endpoint = endpoint[..at];
            }

            var comma = endpoint.IndexOf(',');
            if (comma >= 0)
            {
                endpoint = endpoint[..comma];
            }

            var colon = endpoint.LastIndexOf(':');
            if (colon <= 0 || colon == endpoint.Length - 1)
            {
                continue;
            }

            var host = endpoint[..colon].Trim();
            if (!int.TryParse(endpoint[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            {
                continue;
            }

            if (string.IsNullOrEmpty(host) || host == ":0")
            {
                continue;
            }

            masters.Add((host, port));
        }

        return masters;
    }

    public static string FormatEndpoint(EndPoint endpoint) => endpoint switch
    {
        DnsEndPoint dns => $"{dns.Host}:{dns.Port}",
        IPEndPoint ip => $"{ip.Address}:{ip.Port}",
        _ => endpoint.ToString() ?? string.Empty
    };
}
