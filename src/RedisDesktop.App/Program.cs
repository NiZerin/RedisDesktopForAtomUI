using AtomUI;
using Avalonia;
using System;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.AttachGlobalHandlers();
        try
        {
            if (AppUpdateApplier.TryApplyPendingFromDisk())
            {
                return;
            }

            if (AppUpdateApplier.TryRelaunchNewerInstallCopy())
            {
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Error("Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UseAtomUIPlatformDetect()
            .WithAtomUIDefaultOptions();
#if DEBUG
        builder = builder.LogToTrace();
#endif
        return builder;
    }
}
