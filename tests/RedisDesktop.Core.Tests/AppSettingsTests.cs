using RedisDesktop.Core;

namespace RedisDesktop.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void ResetUserData_keeps_pending_update()
    {
        var settings = new AppSettings
        {
            Language = "en-US",
            MuteUpdatePrompt = true,
            PendingUpdateVersion = "1.0.5",
            PendingUpdatePackagePath = @"C:\cache\pending-1.0.5",
            PendingUpdateTargetExe = @"D:\Program Files\RedisDesktop\RedisDesktop-win-x64.exe"
        };

        var cleared = settings.ResetUserData(300);

        Assert.Equal(300, cleared.SideBarWidth);
        Assert.Equal("zh-CN", cleared.Language);
        Assert.False(cleared.MuteUpdatePrompt);
        Assert.Equal("1.0.5", cleared.PendingUpdateVersion);
        Assert.Equal(@"C:\cache\pending-1.0.5", cleared.PendingUpdatePackagePath);
        Assert.Equal(
            @"D:\Program Files\RedisDesktop\RedisDesktop-win-x64.exe",
            cleared.PendingUpdateTargetExe);
    }
}
