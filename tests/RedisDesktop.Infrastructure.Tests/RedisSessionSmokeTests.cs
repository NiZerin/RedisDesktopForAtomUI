using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.Infrastructure.Tests;

public class RedisSessionSmokeTests
{
    [Fact]
    public async Task Connect_set_get_scan()
    {
        var session = await TryConnectAsync();
        if (session is null)
        {
            return;
        }

        await using (session)
        {
            var key = RedisKeyBytes.FromUtf8($"rd-smoke:{Guid.NewGuid():N}");
            await session.Values.SaveStringAsync(key, "hello-atomui"u8.ToArray(), TimeSpan.FromMinutes(1));
            var snapshot = await session.Values.GetStringAsync(key, 64_000, 1_000_000);
            Assert.Equal("hello-atomui", System.Text.Encoding.UTF8.GetString(snapshot.Value));

            var page = await session.Keys.ScanAsync(new ScanRequest(15, "rd-smoke:*", 0, 50));
            Assert.Contains(page.Keys, k => k.Equals(key));

            await session.Values.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task Collection_hash_list_set_zset_scan_without_hgetall()
    {
        var session = await TryConnectAsync();
        if (session is null)
        {
            return;
        }

        await using (session)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var hashKey = RedisKeyBytes.FromUtf8($"rd-col:hash:{suffix}");
            var listKey = RedisKeyBytes.FromUtf8($"rd-col:list:{suffix}");
            var setKey = RedisKeyBytes.FromUtf8($"rd-col:set:{suffix}");
            var zsetKey = RedisKeyBytes.FromUtf8($"rd-col:zset:{suffix}");

            await session.Values.HashSetAsync(hashKey, "f1", "v1", null);
            await session.Values.HashSetAsync(hashKey, "f2", "v2", null);
            var hashPage = await session.Values.ScanHashAsync(hashKey, 0, 10, "*");
            Assert.True(hashPage.TotalCount >= 2);
            Assert.Contains(hashPage.Items, x => x.Name == "f1" && x.Value == "v1");

            await session.Values.ListPushAsync(listKey, "a", left: false);
            await session.Values.ListPushAsync(listKey, "b", left: false);
            var listPage = await session.Values.RangeListAsync(listKey, 0, 10);
            Assert.Equal(2, listPage.TotalCount);
            Assert.Equal("a", listPage.Items[0].Value);

            await session.Values.SetAddAsync(setKey, "m1");
            await session.Values.SetAddAsync(setKey, "m2");
            var setPage = await session.Values.ScanSetAsync(setKey, 0, 10, "*");
            Assert.Equal(2, setPage.TotalCount);
            Assert.Contains(setPage.Items, x => x.Name == "m1");

            await session.Values.ZAddAsync(zsetKey, "z1", 1.5);
            var zpage = await session.Values.ScanZSetAsync(zsetKey, 0, 10, "*");
            Assert.Equal(1, zpage.TotalCount);
            Assert.Equal(1.5, zpage.Items[0].Score);

            await session.Values.DeleteAsync(hashKey);
            await session.Values.DeleteAsync(listKey);
            await session.Values.DeleteAsync(setKey);
            await session.Values.DeleteAsync(zsetKey);
        }
    }

    [Fact]
    public async Task Stream_xadd_xrange_xdel()
    {
        var session = await TryConnectAsync();
        if (session is null)
        {
            return;
        }

        await using (session)
        {
            var key = RedisKeyBytes.FromUtf8($"rd-col:stream:{Guid.NewGuid():N}");
            await session.Values.StreamAddAsync(key, [new StreamField("f", "v1")]);
            await session.Values.StreamAddAsync(key, [new StreamField("f", "v2")]);
            var page = await session.Values.RangeStreamAsync(key, "-", 10);
            Assert.True(page.TotalCount >= 2);
            Assert.Equal(2, page.Items.Count);
            await session.Values.StreamDeleteAsync(key, page.Items[0].Id);
            var after = await session.Values.RangeStreamAsync(key, "-", 10);
            Assert.Equal(1, after.TotalCount);
            await session.Values.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task PubSub_publish_is_received()
    {
        var session = await TryConnectAsync();
        if (session is null)
        {
            return;
        }

        await using (session)
        {
            var channel = $"rd-pubsub:{Guid.NewGuid():N}";
            var received = new TaskCompletionSource<PubSubMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.PubSub.MessageReceived += (_, message) => received.TrySetResult(message);
            await session.PubSub.SubscribeAsync(channel, pattern: false);
            await session.PubSub.PublishAsync(channel, "hello-pubsub");
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("hello-pubsub", message.Payload);
            await session.PubSub.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task Cli_rejects_monitor_and_runs_ping()
    {
        var session = await TryConnectAsync();
        if (session is null)
        {
            return;
        }

        await using (session)
        {
            var blocked = await session.Cli.ExecuteAsync("MONITOR");
            Assert.False(blocked.Success);
            Assert.Contains("MONITOR", blocked.Error, StringComparison.OrdinalIgnoreCase);

            var ping = await session.Cli.ExecuteAsync("PING");
            Assert.True(ping.Success);
            Assert.Contains("PONG", ping.Output, StringComparison.OrdinalIgnoreCase);

            var latency = await session.PingAsync();
            Assert.False(string.IsNullOrWhiteSpace(latency));
            var size = await session.GetDbSizeAsync();
            Assert.True(size >= 0);
            var previous = session.CurrentDatabase;
            session.SelectDatabase(14);
            Assert.Equal(14, session.CurrentDatabase);
            session.SelectDatabase(previous);
        }
    }

    [Fact]
    public async Task Json_store_roundtrip_does_not_keep_plaintext_password()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rd-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "connections.json");
        var protector = new DpapiSecretProtector();
        var store = new JsonConnectionStore(path);
        var config = new ConnectionConfig
        {
            Name = "sec",
            Host = "127.0.0.1",
            PasswordProtected = protector.Protect("s3cret")
        };

        await store.SaveAsync([config]);
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("s3cret", json);

        var loaded = await store.LoadAsync();
        Assert.Equal("s3cret", protector.Unprotect(loaded[0].PasswordProtected!));
    }

    [Fact]
    public void Connection_transfer_assigns_new_ids()
    {
        var original = new ConnectionConfig { Name = "a", Host = "127.0.0.1", DatabaseAlias = "cache" };
        var json = ConnectionTransfer.ToJson([original]);
        Assert.Contains("cache", json);
        var imported = ConnectionTransfer.FromJson(json);
        Assert.NotEqual(original.Id, imported[0].Id);
        Assert.Equal("cache", imported[0].DatabaseAlias);
    }

    [Fact]
    public void Cluster_nodes_parser_extracts_masters()
    {
        const string text = """
id1 10.0.0.1:7000@17000 master - 0 0 1 connected 0-5460
id2 10.0.0.2:7001@17001 slave id1 0 0 1 connected
id3 10.0.0.3:7002@17002 master,fail - 0 0 2 disconnected
id4 10.0.0.4:7003@17003 myself,master - 0 0 3 connected 5461-10922
""";
        var masters = ClusterNodesParser.ParseMasters(text);
        Assert.Contains(masters, x => x.Host == "10.0.0.1" && x.Port == 7000);
        Assert.Contains(masters, x => x.Host == "10.0.0.4" && x.Port == 7003);
        Assert.DoesNotContain(masters, x => x.Port == 7001);
        Assert.DoesNotContain(masters, x => x.Port == 7002);
    }

    private static async Task<IRedisSession?> TryConnectAsync()
    {
        var config = new ConnectionConfig
        {
            Name = "smoke",
            Host = TestRedis.Host,
            Port = TestRedis.Port,
            Database = 15,
            KeySeparator = ":"
        };

        var log = new MemoryCommandLog();
        var factory = new RedisSessionFactory(log);
        try
        {
            return await factory.ConnectAsync(config, password: null);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
