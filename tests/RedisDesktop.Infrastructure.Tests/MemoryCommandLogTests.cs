using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.Infrastructure.Tests;

public class MemoryCommandLogTests
{
    [Fact]
    public void Append_skips_ping()
    {
        var log = new MemoryCommandLog();

        log.Append(Entry("PING"));
        log.Append(Entry("GET"));

        Assert.Single(log.Entries);
        Assert.Equal("GET", log.Entries[0].Command);
    }

    [Fact]
    public void Append_raises_entry_added()
    {
        var log = new MemoryCommandLog();
        CommandLogEntry? received = null;
        log.EntryAdded += (_, entry) => received = entry;

        log.Append(Entry("SET"));

        Assert.NotNull(received);
        Assert.Equal("SET", received!.Command);
    }

    [Fact]
    public void Clear_removes_entries()
    {
        var log = new MemoryCommandLog();
        log.Append(Entry("GET"));

        log.Clear();

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Append_keeps_latest_5000()
    {
        var log = new MemoryCommandLog();
        for (var i = 0; i < 5002; i++)
        {
            log.Append(Entry($"CMD{i}"));
        }

        Assert.Equal(5000, log.Entries.Count);
        Assert.Equal("CMD2", log.Entries[0].Command);
        Assert.Equal("CMD5001", log.Entries[^1].Command);
    }

    private static CommandLogEntry Entry(string command)
        => new(DateTimeOffset.Now, "local", command, "arg", 1.2, true, null);
}
