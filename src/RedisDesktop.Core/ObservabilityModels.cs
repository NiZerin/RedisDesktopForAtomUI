using System.Globalization;

namespace RedisDesktop.Core;

public sealed record SlowLogEntry(
    long Id,
    DateTimeOffset Timestamp,
    double CostMilliseconds,
    string Command,
    string? Source,
    string? ClientName,
    string? Node)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string TimeOfDayText
    {
        get
        {
            var text = TimeText;
            return text.Length > 11 ? text[11..] : text;
        }
    }

    public string CostText => CostMilliseconds.ToString("0.000", CultureInfo.InvariantCulture);
}

public sealed record SlowLogConfig(string SlowerThan, string MaxLen);

public sealed record KeyMemoryUsage(
    RedisKeyBytes Key,
    string Display,
    long SizeBytes,
    string? Node)
{
    public string SizeText => ValueViewerPipeline.FormatByteSize(SizeBytes);
}

public static class SlowLogFormat
{
    public static double ToMilliseconds(long microseconds)
        => microseconds / 1000d;

    public static string JoinCommand(IEnumerable<string> args)
        => string.Join(" ", args.Where(arg => !string.IsNullOrWhiteSpace(arg)));
}

public static class MemoryScanMatch
{
    public const int DefaultPageSize = 2000;

    public const int MaxKeys = 200_000;

    public static string FromPrefix(string? prefix)
    {
        var text = prefix?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(text) ? "*" : text + "*";
    }
}
