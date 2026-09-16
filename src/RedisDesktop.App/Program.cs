using AtomUI;
using Avalonia;
using System;

namespace RedisDesktop.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (RedisDesktop.Infrastructure.AppUpdateApplier.TryApplyPendingFromDisk())
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseAtomUIPlatformDetect()
            .WithAtomUIDefaultOptions()
            .LogToTrace();
}
