using RedisDesktop.Core;
using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

internal sealed class RedisObservabilityService : IObservability
{
    private readonly RedisSession _session;

    public RedisObservabilityService(RedisSession session)
    {
        _session = session;
    }

    public Task<IReadOnlyList<SlowLogEntry>> GetSlowLogAsync(int count = 1000, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, 20_000);
        return _session.RunAsync("SLOWLOG", $"GET {take}", async () =>
        {
            var masters = _session.GetMasters();
            if (masters.Length == 0)
            {
                return (IReadOnlyList<SlowLogEntry>)[];
            }

            var list = new List<SlowLogEntry>();
            var multi = masters.Length > 1;
            foreach (var server in masters)
            {
                var node = multi ? _session.FormatNodeName(server) : null;
                try
                {
                    var result = await server.ExecuteAsync("SLOWLOG", "GET", take).ConfigureAwait(false);
                    list.AddRange(ParseSlowLog(result, node));
                }
                catch
                {
                    // one unreachable master should not fail the tab
                }
            }

            return (IReadOnlyList<SlowLogEntry>)list;
        }, cancellationToken);
    }

    public Task<SlowLogConfig> GetSlowLogConfigAsync(CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("CONFIG", "GET slowlog-*", async () =>
        {
            string slower = "-";
            string maxLen = "-";
            try
            {
                var slowerReply = await _session.Database.ExecuteAsync("CONFIG", "GET", "slowlog-log-slower-than")
                    .ConfigureAwait(false);
                slower = ReadConfigValue(slowerReply) ?? "-";
            }
            catch
            {
                slower = "-";
            }

            try
            {
                var maxReply = await _session.Database.ExecuteAsync("CONFIG", "GET", "slowlog-max-len")
                    .ConfigureAwait(false);
                maxLen = ReadConfigValue(maxReply) ?? "-";
            }
            catch
            {
                maxLen = "-";
            }

            return new SlowLogConfig(slower, maxLen);
        }, cancellationToken, log: false);
    }

    public Task<IReadOnlyList<KeyMemoryUsage>> GetMemoryUsageAsync(
        IReadOnlyList<RedisKeyBytes> keys,
        long minSizeBytes,
        CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("MEMORY", $"USAGE {keys.Count}", async () =>
        {
            if (keys.Count == 0)
            {
                return (IReadOnlyList<KeyMemoryUsage>)[];
            }

            var db = _session.Database;
            var batch = db.CreateBatch();
            var tasks = new Task<RedisResult>[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                tasks[i] = batch.ExecuteAsync("MEMORY", "USAGE", (RedisKey)keys[i].Value);
            }

            batch.Execute();
            RedisResult[] replies;
            try
            {
                replies = await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                var fallback = new List<KeyMemoryUsage>(keys.Count);
                foreach (var key in keys)
                {
                    fallback.Add(new KeyMemoryUsage(key, key.ToDisplayString(), 0, null));
                }

                return (IReadOnlyList<KeyMemoryUsage>)fallback;
            }

            var rows = new List<KeyMemoryUsage>(keys.Count);
            for (var i = 0; i < keys.Count; i++)
            {
                long size = 0;
                try
                {
                    var reply = replies[i];
                    if (!reply.IsNull)
                    {
                        size = (long)reply;
                    }
                }
                catch
                {
                    size = 0;
                }

                if (minSizeBytes > 0 && size < minSizeBytes)
                {
                    continue;
                }

                rows.Add(new KeyMemoryUsage(keys[i], keys[i].ToDisplayString(), size, null));
            }

            return (IReadOnlyList<KeyMemoryUsage>)rows;
        }, cancellationToken, log: false);
    }

    private static IEnumerable<SlowLogEntry> ParseSlowLog(RedisResult result, string? node)
    {
        if (result.IsNull || result.Resp2Type != ResultType.Array)
        {
            yield break;
        }

        foreach (var item in (RedisResult[])result!)
        {
            if (item.IsNull || item.Resp2Type != ResultType.Array)
            {
                continue;
            }

            var fields = (RedisResult[])item!;
            if (fields.Length < 4)
            {
                continue;
            }

            var id = ToInt64(fields[0]);
            var unix = ToInt64(fields[1]);
            var micros = ToInt64(fields[2]);
            var command = ReadCommand(fields[3]);
            var source = fields.Length > 4 ? fields[4].ToString() : null;
            var name = fields.Length > 5 ? fields[5].ToString() : null;
            yield return new SlowLogEntry(
                id,
                DateTimeOffset.FromUnixTimeSeconds(unix),
                SlowLogFormat.ToMilliseconds(micros),
                command,
                string.IsNullOrWhiteSpace(source) ? null : source,
                string.IsNullOrWhiteSpace(name) ? null : name,
                node);
        }
    }

    private static string ReadCommand(RedisResult result)
    {
        if (result.IsNull)
        {
            return string.Empty;
        }

        if (result.Resp2Type == ResultType.Array)
        {
            var parts = new List<string>();
            foreach (var item in (RedisResult[])result!)
            {
                if (!item.IsNull)
                {
                    parts.Add(item.ToString() ?? string.Empty);
                }
            }

            return SlowLogFormat.JoinCommand(parts);
        }

        return result.ToString() ?? string.Empty;
    }

    private static long ToInt64(RedisResult result)
    {
        try
        {
            return (long)result;
        }
        catch
        {
            return long.TryParse(result.ToString(), out var value) ? value : 0;
        }
    }

    private static string? ReadConfigValue(RedisResult result)
    {
        if (result.IsNull || result.Resp2Type != ResultType.Array)
        {
            return result.IsNull ? null : result.ToString();
        }

        var rows = (RedisResult[])result!;
        if (rows.Length >= 2)
        {
            return rows[1].ToString();
        }

        return rows.Length == 1 ? rows[0].ToString() : null;
    }
}
