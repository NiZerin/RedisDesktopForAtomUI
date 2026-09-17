using RedisDesktop.Infrastructure;

namespace RedisDesktop.Infrastructure.Tests;

public class AppLogTests : IDisposable
{
    public AppLogTests()
    {
        AppLog.ConsoleOverride = false;
        AppLog.DirectoryOverride = Path.Combine(Path.GetTempPath(), "RedisDesktop-log-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        AppLog.ConsoleOverride = null;
        var dir = AppLog.DirectoryOverride;
        AppLog.DirectoryOverride = null;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LogFilePath_uses_date_stamp()
    {
        var path = AppLog.LogFilePath(new DateTime(2026, 9, 17));
        Assert.Equal("2026-09-17.log", Path.GetFileName(path));
    }

    [Fact]
    public void Error_writes_exception_to_dated_file()
    {
        AppLog.Error("UI", new InvalidOperationException("boom"));

        var file = AppLog.LogFilePath(DateTime.Now);
        var text = File.ReadAllText(file);
        Assert.Contains("ERROR UI", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }
}
