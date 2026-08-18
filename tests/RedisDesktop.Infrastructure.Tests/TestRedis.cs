namespace RedisDesktop.Infrastructure.Tests;

internal static class TestRedis
{
    public static string Host =>
        Environment.GetEnvironmentVariable("REDIS_TEST_HOST") ?? "192.168.227.5";

    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("REDIS_TEST_PORT"), out var port) && port > 0
            ? port
            : 6379;
}
