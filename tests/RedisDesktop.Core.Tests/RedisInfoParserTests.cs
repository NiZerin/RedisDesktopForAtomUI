using RedisDesktop.Core;

namespace RedisDesktop.Core.Tests;

public class RedisInfoParserTests
{
    private const string SampleInfo = """
        # Server
        redis_version:7.2.4
        os:Linux 5.15.0 x86_64
        process_id:123
        executable:/usr/bin/redis-server
        dbfilename:dump.rdb

        # Memory
        used_memory:2048
        used_memory_peak:4096
        used_memory_lua:1024

        # Stats
        connected_clients:8
        total_connections_received:1000
        total_commands_processed:2500
        cluster_enabled:0

        # Keyspace
        db0:keys=12,expires=3,avg_ttl=45000
        db1:keys=4,expires=0,avg_ttl=0
        """;

    [Fact]
    public void Parse_skips_comments_and_keeps_value_spaces()
    {
        var status = RedisInfoParser.Parse(SampleInfo);

        Assert.Equal("7.2.4", status["redis_version"]);
        Assert.Equal("Linux 5.15.0 x86_64", status["os"]);
        Assert.Equal("/usr/bin/redis-server", status["executable"]);
        Assert.False(status.ContainsKey("# Server"));
    }

    [Fact]
    public void ParseKeyspace_matches_db_digits_only()
    {
        var status = RedisInfoParser.Parse(SampleInfo);
        var rows = RedisInfoParser.ParseKeyspace(status);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Database == "db0" && r.Keys == 12 && r.Expires == 3 && r.AvgTtl == 45000);
        Assert.Contains(rows, r => r.Database == "db1" && r.Keys == 4 && r.Expires == 0);
        Assert.DoesNotContain(rows, r => r.Database.Contains("dbfilename", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cluster_enabled_and_node_keyspace()
    {
        var status = RedisInfoParser.Parse("cluster_enabled:1\ndb0:keys=2,expires=1,avg_ttl=9");
        Assert.True(RedisInfoParser.IsClusterEnabled(status));

        var rows = RedisInfoParser.ParseKeyspace(status, "10.0.0.8:6379");
        Assert.Single(rows);
        Assert.Equal("10.0.0.8:6379", rows[0].Node);
    }

    [Fact]
    public void Format_bytes_and_locale_numbers()
    {
        Assert.Equal("2 KB", RedisInfoParser.FormatBytes("2048"));
        Assert.Equal("-", RedisInfoParser.FormatBytes(null));
        Assert.Equal(1000.ToString("N0"), RedisInfoParser.FormatNumber("1000"));
        Assert.Equal("-", RedisInfoParser.FormatNumber(""));
    }

    [Fact]
    public void TryGetDatabaseIndex_parses_db_prefix()
    {
        Assert.True(RedisInfoParser.TryGetDatabaseIndex("db12", out var index));
        Assert.Equal(12, index);
        Assert.False(RedisInfoParser.TryGetDatabaseIndex("dbfilename", out _));
        Assert.False(RedisInfoParser.TryGetDatabaseIndex("db", out _));
    }
}
