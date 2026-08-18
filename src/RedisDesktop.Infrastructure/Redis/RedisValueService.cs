using System.Text;
using RedisDesktop.Core;
using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

internal sealed class RedisValueService : IValueService
{
    private readonly RedisSession _session;
    private bool? _hashFieldTtlSupported;

    public RedisValueService(RedisSession session)
    {
        _session = session;
    }

    public Task<KeyMeta> GetMetaAsync(RedisKeyBytes key, CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("TYPE", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            var type = await _session.Database.KeyTypeAsync(redisKey).ConfigureAwait(false);
            var ttl = await _session.Database.KeyTimeToLiveAsync(redisKey).ConfigureAwait(false);
            long? memory = null;
            try
            {
                var usage = await _session.Database.ExecuteAsync("MEMORY", "USAGE", redisKey).ConfigureAwait(false);
                if (!usage.IsNull)
                {
                    memory = (long)usage;
                }
            }
            catch
            {
                // MEMORY USAGE is optional
            }

            return new KeyMeta(key, MapType(type), ttl, memory);
        }, cancellationToken);
    }

    public Task<StringSnapshot> GetStringAsync(
        RedisKeyBytes key,
        long previewLimit,
        long maxFullLoad,
        CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("STRLEN", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            var length = await _session.Database.StringLengthAsync(redisKey).ConfigureAwait(false);
            if (length > maxFullLoad)
            {
                var preview = await _session.Database.StringGetRangeAsync(redisKey, 0, previewLimit - 1).ConfigureAwait(false);
                var bytes = (byte[])preview!;
                return new StringSnapshot(key, bytes ?? [], IsUtf8(bytes), IsTruncated: true, TotalLength: length);
            }

            var value = await _session.Database.StringGetAsync(redisKey).ConfigureAwait(false);
            var full = (byte[]?)value ?? [];
            return new StringSnapshot(key, full, IsUtf8(full), IsTruncated: false, TotalLength: full.Length);
        }, cancellationToken);
    }

    public Task SaveStringAsync(RedisKeyBytes key, byte[] value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("SET");
        return _session.RunAsync("SET", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            if (ttl is { } expire && expire > TimeSpan.Zero)
            {
                await _session.Database.StringSetAsync(redisKey, value, expire).ConfigureAwait(false);
            }
            else
            {
                await _session.Database.StringSetAsync(redisKey, value).ConfigureAwait(false);
                if (ttl == TimeSpan.Zero)
                {
                    await _session.Database.KeyPersistAsync(redisKey).ConfigureAwait(false);
                }
            }

            return true;
        }, cancellationToken);
    }

    public Task SetTtlAsync(RedisKeyBytes key, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        var command = ttl is null || ttl <= TimeSpan.Zero ? "PERSIST" : "EXPIRE";
        _session.EnsureWritable(command);
        return _session.RunAsync(command, key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            if (ttl is null || ttl <= TimeSpan.Zero)
            {
                await _session.Database.KeyPersistAsync(redisKey).ConfigureAwait(false);
            }
            else
            {
                await _session.Database.KeyExpireAsync(redisKey, ttl).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken);
    }

    public Task RenameAsync(RedisKeyBytes key, RedisKeyBytes newKey, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("RENAME");
        return _session.RunAsync("RENAME", $"{key.ToDisplayString()} -> {newKey.ToDisplayString()}", async () =>
        {
            await _session.Database.KeyRenameAsync(ToRedisKey(key), ToRedisKey(newKey)).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task DeleteAsync(RedisKeyBytes key, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("DEL");
        return _session.RunAsync("DEL", key.ToDisplayString(), async () =>
        {
            await _session.Database.KeyDeleteAsync(ToRedisKey(key)).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<CollectionPage> ScanHashAsync(
        RedisKeyBytes key,
        long cursor,
        int count,
        string? match,
        CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("HSCAN", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            var total = (long)await _session.Database.HashLengthAsync(redisKey).ConfigureAwait(false);
            var result = await _session.Database.ExecuteAsync(
                    "HSCAN",
                    redisKey,
                    cursor,
                    "MATCH",
                    string.IsNullOrWhiteSpace(match) ? "*" : match,
                    "COUNT",
                    Math.Max(1, count))
                .ConfigureAwait(false);

            var (next, pairs) = ParseScanPairs(result);
            var supportsTtl = await ProbeHashFieldTtlAsync().ConfigureAwait(false);
            var members = new List<CollectionMember>(pairs.Count / 2);
            for (var i = 0; i + 1 < pairs.Count; i += 2)
            {
                TimeSpan? fieldTtl = null;
                if (supportsTtl)
                {
                    fieldTtl = await TryGetHashFieldTtlAsync(redisKey, pairs[i]).ConfigureAwait(false);
                }

                members.Add(new CollectionMember(pairs[i], pairs[i + 1], FieldTtl: fieldTtl));
            }

            return new CollectionPage(members, next, next == 0, members.Count, total, supportsTtl);
        }, cancellationToken);
    }

    public Task HashSetAsync(
        RedisKeyBytes key,
        string field,
        string value,
        TimeSpan? fieldTtl,
        CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("HSET");
        return _session.RunAsync("HSET", $"{key.ToDisplayString()} {field}", async () =>
        {
            var redisKey = ToRedisKey(key);
            await _session.Database.HashSetAsync(redisKey, field, value).ConfigureAwait(false);
            if (fieldTtl is { } ttl && ttl > TimeSpan.Zero && await ProbeHashFieldTtlAsync().ConfigureAwait(false))
            {
                _session.EnsureWritable("HEXPIRE");
                await _session.Database.ExecuteAsync(
                        "HEXPIRE",
                        redisKey,
                        (int)Math.Max(1, ttl.TotalSeconds),
                        "FIELDS",
                        1,
                        field)
                    .ConfigureAwait(false);
            }

            return true;
        }, cancellationToken);
    }

    public Task HashDeleteAsync(RedisKeyBytes key, string field, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("HDEL");
        return _session.RunAsync("HDEL", $"{key.ToDisplayString()} {field}", async () =>
        {
            await _session.Database.HashDeleteAsync(ToRedisKey(key), field).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<CollectionPage> RangeListAsync(
        RedisKeyBytes key,
        long start,
        long stop,
        CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("LRANGE", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            var total = await _session.Database.ListLengthAsync(redisKey).ConfigureAwait(false);
            var values = await _session.Database.ListRangeAsync(redisKey, start, stop).ConfigureAwait(false);
            var items = new List<CollectionMember>(values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                var index = start + i;
                items.Add(new CollectionMember(index.ToString(), ToDisplay(values[i]), Index: index));
            }

            var next = stop + 1;
            var exhausted = next >= total || values.Length == 0;
            return new CollectionPage(items, exhausted ? 0 : next, exhausted, items.Count, total);
        }, cancellationToken);
    }

    public Task ListSetAsync(RedisKeyBytes key, long index, string value, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("LSET");
        return _session.RunAsync("LSET", $"{key.ToDisplayString()} [{index}]", async () =>
        {
            await _session.Database.ListSetByIndexAsync(ToRedisKey(key), index, value).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task ListPushAsync(RedisKeyBytes key, string value, bool left, CancellationToken cancellationToken = default)
    {
        var command = left ? "LPUSH" : "RPUSH";
        _session.EnsureWritable(command);
        return _session.RunAsync(command, key.ToDisplayString(), async () =>
        {
            if (left)
            {
                await _session.Database.ListLeftPushAsync(ToRedisKey(key), value).ConfigureAwait(false);
            }
            else
            {
                await _session.Database.ListRightPushAsync(ToRedisKey(key), value).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken);
    }

    public Task ListRemoveAtAsync(RedisKeyBytes key, long index, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("LSET");
        return _session.RunAsync("LREM", $"{key.ToDisplayString()} [{index}]", async () =>
        {
            var redisKey = ToRedisKey(key);
            var marker = $"__rd_del_{Guid.NewGuid():N}__";
            await _session.Database.ListSetByIndexAsync(redisKey, index, marker).ConfigureAwait(false);
            await _session.Database.ListRemoveAsync(redisKey, marker, 1).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<CollectionPage> ScanSetAsync(
        RedisKeyBytes key,
        long cursor,
        int count,
        string? match,
        CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("SSCAN", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            var total = await _session.Database.SetLengthAsync(redisKey).ConfigureAwait(false);
            var result = await _session.Database.ExecuteAsync(
                    "SSCAN",
                    redisKey,
                    cursor,
                    "MATCH",
                    string.IsNullOrWhiteSpace(match) ? "*" : match,
                    "COUNT",
                    Math.Max(1, count))
                .ConfigureAwait(false);

            var (next, values) = ParseScanValues(result);
            var items = values.Select(v => new CollectionMember(v, v)).ToList();
            return new CollectionPage(items, next, next == 0, items.Count, total);
        }, cancellationToken);
    }

    public Task SetAddAsync(RedisKeyBytes key, string member, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("SADD");
        return _session.RunAsync("SADD", key.ToDisplayString(), async () =>
        {
            await _session.Database.SetAddAsync(ToRedisKey(key), member).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task SetRemoveAsync(RedisKeyBytes key, string member, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("SREM");
        return _session.RunAsync("SREM", key.ToDisplayString(), async () =>
        {
            await _session.Database.SetRemoveAsync(ToRedisKey(key), member).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<CollectionPage> ScanZSetAsync(
        RedisKeyBytes key,
        long cursor,
        int count,
        string? match,
        CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("ZSCAN", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            var total = await _session.Database.SortedSetLengthAsync(redisKey).ConfigureAwait(false);
            var result = await _session.Database.ExecuteAsync(
                    "ZSCAN",
                    redisKey,
                    cursor,
                    "MATCH",
                    string.IsNullOrWhiteSpace(match) ? "*" : match,
                    "COUNT",
                    Math.Max(1, count))
                .ConfigureAwait(false);

            var (next, pairs) = ParseScanPairs(result);
            var items = new List<CollectionMember>(pairs.Count / 2);
            for (var i = 0; i + 1 < pairs.Count; i += 2)
            {
                double? score = double.TryParse(pairs[i + 1], out var parsed) ? parsed : null;
                items.Add(new CollectionMember(pairs[i], pairs[i], score));
            }

            return new CollectionPage(items, next, next == 0, items.Count, total);
        }, cancellationToken);
    }

    public Task ZAddAsync(RedisKeyBytes key, string member, double score, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("ZADD");
        return _session.RunAsync("ZADD", key.ToDisplayString(), async () =>
        {
            await _session.Database.SortedSetAddAsync(ToRedisKey(key), member, score).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task ZRemAsync(RedisKeyBytes key, string member, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("ZREM");
        return _session.RunAsync("ZREM", key.ToDisplayString(), async () =>
        {
            await _session.Database.SortedSetRemoveAsync(ToRedisKey(key), member).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<StreamPage> RangeStreamAsync(
        RedisKeyBytes key,
        string startId,
        int count,
        CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("XRANGE", key.ToDisplayString(), async () =>
        {
            var redisKey = ToRedisKey(key);
            var total = await _session.Database.StreamLengthAsync(redisKey).ConfigureAwait(false);
            var start = string.IsNullOrWhiteSpace(startId) ? "-" : startId;
            var entries = await _session.Database
                .StreamRangeAsync(redisKey, start, "+", Math.Max(1, count))
                .ConfigureAwait(false);
            var items = entries.Select(entry => new Core.StreamEntry(
                    entry.Id.ToString(),
                    entry.Values.Select(v => new StreamField(ToDisplay(v.Name), ToDisplay(v.Value))).ToList()))
                .ToList();
            var exhausted = items.Count < Math.Max(1, count);
            var next = items.Count == 0 ? start : "(" + items[^1].Id;
            return new StreamPage(items, next, exhausted, items.Count, total);
        }, cancellationToken);
    }

    public Task StreamAddAsync(
        RedisKeyBytes key,
        IReadOnlyList<StreamField> fields,
        CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("XADD");
        return _session.RunAsync("XADD", key.ToDisplayString(), async () =>
        {
            var pairs = fields.Select(f => new NameValueEntry(f.Name, f.Value)).ToArray();
            await _session.Database.StreamAddAsync(ToRedisKey(key), pairs).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task StreamDeleteAsync(RedisKeyBytes key, string id, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("XDEL");
        return _session.RunAsync("XDEL", $"{key.ToDisplayString()} {id}", async () =>
        {
            await _session.Database.StreamDeleteAsync(ToRedisKey(key), [id]).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    private async Task<bool> ProbeHashFieldTtlAsync()
    {
        if (_hashFieldTtlSupported is { } cached)
        {
            return cached;
        }

        try
        {
            var info = await _session.Database.ExecuteAsync("COMMAND", "INFO", "HEXPIRE").ConfigureAwait(false);
            _hashFieldTtlSupported = !info.IsNull && info.Resp2Type != ResultType.Error;
        }
        catch
        {
            _hashFieldTtlSupported = false;
        }

        return _hashFieldTtlSupported == true;
    }

    private async Task<TimeSpan?> TryGetHashFieldTtlAsync(RedisKey key, string field)
    {
        try
        {
            var result = await _session.Database.ExecuteAsync("HTTL", key, "FIELDS", 1, field).ConfigureAwait(false);
            if (result.IsNull || result.Resp2Type != ResultType.Array)
            {
                return null;
            }

            var rows = (RedisResult[])result!;
            if (rows.Length == 0)
            {
                return null;
            }

            var seconds = (long)rows[0];
            return seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch
        {
            _hashFieldTtlSupported = false;
            return null;
        }
    }

    private static (long Cursor, List<string> Values) ParseScanValues(RedisResult result)
    {
        var values = new List<string>();
        if (result.IsNull || result.Resp2Type != ResultType.Array)
        {
            return (0, values);
        }

        var rows = (RedisResult[])result!;
        var cursor = rows.Length > 0 ? long.Parse(rows[0].ToString()!) : 0;
        if (rows.Length > 1 && rows[1].Resp2Type == ResultType.Array)
        {
            foreach (var item in (RedisResult[])rows[1]!)
            {
                if (!item.IsNull)
                {
                    values.Add(ToDisplay((RedisValue)item));
                }
            }
        }

        return (cursor, values);
    }

    private static (long Cursor, List<string> Pairs) ParseScanPairs(RedisResult result)
    {
        var (cursor, values) = ParseScanValues(result);
        return (cursor, values);
    }

    private static RedisKey ToRedisKey(RedisKeyBytes key) => (RedisKey)key.Value;

    private static RedisKeyType MapType(RedisType type) => type switch
    {
        RedisType.String => RedisKeyType.String,
        RedisType.Hash => RedisKeyType.Hash,
        RedisType.List => RedisKeyType.List,
        RedisType.Set => RedisKeyType.Set,
        RedisType.SortedSet => RedisKeyType.SortedSet,
        RedisType.Stream => RedisKeyType.Stream,
        RedisType.None => RedisKeyType.None,
        _ => RedisKeyType.Unknown
    };

    private static string ToDisplay(RedisValue value)
    {
        if (value.IsNull)
        {
            return string.Empty;
        }

        var bytes = (byte[]?)value;
        if (bytes is null)
        {
            return value.ToString();
        }

        return IsUtf8(bytes) ? Encoding.UTF8.GetString(bytes) : Convert.ToHexString(bytes);
    }

    private static bool IsUtf8(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return true;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
