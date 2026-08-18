using System.Globalization;
using System.Text.RegularExpressions;

namespace RedisDesktop.Core;

public static class RedisInfoParser
{
    private static readonly Regex DbKeyPattern = new(@"^db\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static Dictionary<string, string> Parse(string? content)
    {
        var lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(content))
        {
            return lines;
        }

        foreach (var raw in content.Split(['\r', '\n']))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            lines[key] = value;
        }

        return lines;
    }

    public static IReadOnlyList<RedisInfoKeyspaceRow> ParseKeyspace(
        IReadOnlyDictionary<string, string> status,
        string? node = null)
    {
        var rows = new List<RedisInfoKeyspaceRow>();
        foreach (var (key, value) in status)
        {
            if (!DbKeyPattern.IsMatch(key))
            {
                continue;
            }

            var parts = value.Split(',');
            rows.Add(new RedisInfoKeyspaceRow(
                key,
                ParseLong(FieldValue(parts, 0)),
                ParseLong(FieldValue(parts, 1)),
                ParseLong(FieldValue(parts, 2)),
                node));
        }

        return rows;
    }

    public static bool IsClusterEnabled(IReadOnlyDictionary<string, string> status)
        => status.TryGetValue("cluster_enabled", out var value) && value == "1";

    public static string Get(IReadOnlyDictionary<string, string> status, string key, string fallback = "-")
        => status.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public static string FormatBytes(string? raw)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
        {
            return ValueViewerPipeline.FormatByteSize(bytes);
        }

        return string.IsNullOrWhiteSpace(raw) ? "-" : raw.Trim();
    }

    public static string FormatNumber(string? raw)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString("N0");
        }

        return string.IsNullOrWhiteSpace(raw) ? "-" : raw.Trim();
    }

    public static string FormatNumber(long value) => value.ToString("N0");

    public static bool TryGetDatabaseIndex(string database, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(database)
            || !database.StartsWith("db", StringComparison.OrdinalIgnoreCase)
            || database.Length < 3)
        {
            return false;
        }

        return int.TryParse(database.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static string? FieldValue(string[] parts, int index)
    {
        if (index < 0 || index >= parts.Length)
        {
            return null;
        }

        var kv = parts[index].Split('=', 2);
        return kv.Length == 2 ? kv[1].Trim() : null;
    }

    private static long ParseLong(string? raw)
        => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
