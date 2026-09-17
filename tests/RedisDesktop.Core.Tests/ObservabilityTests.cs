using RedisDesktop.Core;

namespace RedisDesktop.Core.Tests;

public class ObservabilityTests
{
    [Theory]
    [InlineData(null, "*")]
    [InlineData("", "*")]
    [InlineData("user:", "user:*")]
    [InlineData("cache", "cache*")]
    public void MemoryScanMatch_appends_wildcard(string? prefix, string expected)
        => Assert.Equal(expected, MemoryScanMatch.FromPrefix(prefix));

    [Fact]
    public void SlowLogFormat_converts_microseconds_to_milliseconds()
        => Assert.Equal(15.5d, SlowLogFormat.ToMilliseconds(15500), 3);

    [Fact]
    public void SlowLogFormat_joins_command_args()
        => Assert.Equal("SET foo bar", SlowLogFormat.JoinCommand(["SET", "foo", "bar"]));

    [Fact]
    public void SlowLogEntry_formats_cost_and_time_of_day()
    {
        var entry = new SlowLogEntry(
            7,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            SlowLogFormat.ToMilliseconds(12345),
            "GET key",
            "127.0.0.1:6379",
            "app",
            "10.0.0.1:7000");

        Assert.Equal("12.345", entry.CostText);
        Assert.Equal(8, entry.TimeOfDayText.Length);
        Assert.Contains(':', entry.TimeOfDayText);
    }
}
