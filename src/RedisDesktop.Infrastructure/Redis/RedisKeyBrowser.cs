using RedisDesktop.Core;
using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

internal sealed class RedisKeyBrowser : IKeyBrowser
{
    private readonly RedisSession _session;

    public RedisKeyBrowser(RedisSession session)
    {
        _session = session;
    }

    public Task<ScanPage> ScanAsync(ScanRequest request, CancellationToken cancellationToken = default)
        => ScanAsync(request, log: true, cancellationToken);

    public Task<ScanPage> ScanAsync(ScanRequest request, bool log, CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("SCAN", request.Match, async () =>
        {
            if (_session.IsCluster)
            {
                return await ScanClusterAsync(request).ConfigureAwait(false);
            }

            var result = await _session.Database.ExecuteAsync(
                    "SCAN",
                    request.Cursor,
                    "MATCH",
                    NormalizeMatch(request.Match),
                    "COUNT",
                    Math.Max(1, request.Count))
                .ConfigureAwait(false);

            return ParseScan(result);
        }, cancellationToken, log);
    }

    public Task<bool> ExistsAsync(RedisKeyBytes key, CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("EXISTS", key.ToDisplayString(), async () =>
            await _session.Database.KeyExistsAsync((RedisKey)key.Value).ConfigureAwait(false), cancellationToken);
    }

    public async Task<IReadOnlyList<KeyTreeNode>> LoadChildrenAsync(
        string prefix,
        string separator,
        int count,
        CancellationToken cancellationToken = default)
    {
        separator = string.IsNullOrEmpty(separator) ? ":" : separator;
        var match = string.IsNullOrEmpty(prefix) ? "*" : prefix + separator + "*";
        var keys = new List<RedisKeyBytes>();
        long cursor = 0;
        var pages = 0;

        do
        {
            var page = await ScanAsync(new ScanRequest(_session.Config.Database, match, cursor, count), cancellationToken)
                .ConfigureAwait(false);
            keys.AddRange(page.Keys);
            cursor = page.Cursor;
            pages++;
        } while (cursor != 0 && keys.Count < 2000 && pages < 20 && !cancellationToken.IsCancellationRequested);

        if (!string.IsNullOrEmpty(prefix))
        {
            try
            {
                var exists = await _session.Database.KeyExistsAsync((RedisKey)prefix).WaitAsync(cancellationToken).ConfigureAwait(false);
                if (exists)
                {
                    keys.Add(RedisKeyBytes.FromUtf8(prefix));
                }
            }
            catch
            {
                // prefix may be binary; ignore exact-key probe
            }
        }

        return KeyTreeBuilder.BuildChildren(keys, prefix, separator);
    }

    private async Task<ScanPage> ScanClusterAsync(ScanRequest request)
    {
        var servers = _session.GetMasters();
        if (servers.Length == 0)
        {
            return new ScanPage([], 0, true);
        }

        ClusterScanCursor.Decode(request.Cursor, out var startIndex, out var nodeCursor);
        if (startIndex < 0 || startIndex >= servers.Length)
        {
            startIndex = 0;
            nodeCursor = 0;
        }

        var match = NormalizeMatch(request.Match);
        var count = Math.Max(1, request.Count);
        var keys = new List<RedisKeyBytes>();
        var seen = new HashSet<RedisKeyBytes>();

        for (var i = startIndex; i < servers.Length; i++)
        {
            var cursor = i == startIndex ? nodeCursor : 0L;
            var result = await servers[i].ExecuteAsync(
                    "SCAN",
                    cursor,
                    "MATCH",
                    match,
                    "COUNT",
                    count)
                .ConfigureAwait(false);
            var page = ParseScan(result);
            foreach (var key in page.Keys)
            {
                if (seen.Add(key))
                {
                    keys.Add(key);
                }
            }

            if (!page.Exhausted)
            {
                return new ScanPage(keys, ClusterScanCursor.Encode(i, page.Cursor), false);
            }

            if (keys.Count >= count && i < servers.Length - 1)
            {
                return new ScanPage(keys, ClusterScanCursor.Encode(i + 1, 0), false);
            }
        }

        return new ScanPage(keys, 0, true);
    }

    private static string NormalizeMatch(string match)
        => string.IsNullOrWhiteSpace(match) ? "*" : match;

    private static ScanPage ParseScan(RedisResult result)
    {
        if (result.IsNull || result.Resp2Type != ResultType.Array)
        {
            return new ScanPage([], 0, true);
        }

        var rows = (RedisResult[])result!;
        var cursor = rows.Length > 0 ? long.Parse(rows[0].ToString()!) : 0;
        var keys = new List<RedisKeyBytes>();
        if (rows.Length > 1 && rows[1].Resp2Type == ResultType.Array)
        {
            foreach (var item in (RedisResult[])rows[1]!)
            {
                if (item.IsNull)
                {
                    continue;
                }

                keys.Add(new RedisKeyBytes((byte[])item!));
            }
        }

        return new ScanPage(keys, cursor, cursor == 0);
    }
}
