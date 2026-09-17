using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.Infrastructure.Tests;

public class AppUpdateApplierTests
{
    [Theory]
    [InlineData("RedisDesktop.exe", true)]
    [InlineData("RedisDesktop-win-x64.exe", true)]
    [InlineData("RedisDesktop-linux-x64.exe", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("RedisDesktop.Core.dll", false)]
    [InlineData("apply-update.ps1", false)]
    public void IsInstallHostName_accepts_published_hosts(string fileName, bool expected)
        => Assert.Equal(expected, AppUpdateApplier.IsInstallHostName(fileName));

    [Fact]
    public void ShouldOverwriteInstallCopy_replaces_older_github_asset_name()
    {
        var current = Path.Combine(Path.GetTempPath(), "RedisDesktop.exe");
        var sibling = Path.Combine(Path.GetTempPath(), "RedisDesktop-win-x64.exe");

        Assert.True(AppUpdateApplier.ShouldOverwriteInstallCopy(current, "1.0.5", sibling, "1.0.4"));
        Assert.False(AppUpdateApplier.ShouldOverwriteInstallCopy(current, "1.0.5", sibling, "1.0.5"));
        Assert.False(AppUpdateApplier.ShouldOverwriteInstallCopy(current, "1.0.4", sibling, "1.0.5"));
        Assert.False(AppUpdateApplier.ShouldOverwriteInstallCopy(current, "1.0.5", current, "1.0.4"));
        Assert.False(AppUpdateApplier.ShouldOverwriteInstallCopy(current, "1.0.5", Path.Combine(Path.GetTempPath(), "notepad.exe"), "1.0.0"));
    }

    [Fact]
    public void ResolveTargetExecutable_prefers_pending_target()
    {
        var file = Path.GetTempFileName();
        try
        {
            var settings = new AppSettings { PendingUpdateTargetExe = file };
            Assert.Equal(Path.GetFullPath(file), AppUpdateApplier.ResolveTargetExecutable(settings));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
