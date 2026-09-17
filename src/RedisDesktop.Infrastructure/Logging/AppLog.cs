using System.Diagnostics;
using System.Text;

namespace RedisDesktop.Infrastructure;

public static class AppLog
{
    private static readonly object Gate = new();
    private static int _attached;

    internal static bool? ConsoleOverride { get; set; }

    internal static string? DirectoryOverride { get; set; }

    public static bool WriteToConsole
        => ConsoleOverride ?? (Debugger.IsAttached || AppUpdateApplier.IsDevelopmentHost() || IsDebugBuild);

    public static void AttachGlobalHandlers()
    {
        if (Interlocked.Exchange(ref _attached, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Error("AppDomain", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error("Task", e.Exception);
            e.SetObserved();
        };
    }

    public static void Error(string source, Exception? exception)
    {
        Write("ERROR", source, exception?.ToString() ?? "(null)");
    }

    public static void Error(string source, string message)
    {
        Write("ERROR", source, message);
    }

    private static void Write(string level, string source, string detail)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var text = $"[{stamp}] {level} {source}{Environment.NewLine}{detail}{Environment.NewLine}";
        if (WriteToConsole)
        {
            try
            {
                Console.Error.WriteLine(text);
            }
            catch
            {
                // WinExe without a console
            }

            return;
        }

        try
        {
            var path = LogFilePath(DateTime.Now);
            lock (Gate)
            {
                File.AppendAllText(path, text + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            try
            {
                Console.Error.WriteLine(text);
            }
            catch
            {
                // last resort
            }
        }
    }

    internal static string LogFilePath(DateTime date)
    {
        if (string.IsNullOrWhiteSpace(DirectoryOverride))
        {
            return AppPaths.LogFile(date);
        }

        Directory.CreateDirectory(DirectoryOverride);
        return Path.Combine(DirectoryOverride, date.ToString("yyyy-MM-dd") + ".log");
    }

#if DEBUG
    private static bool IsDebugBuild => true;
#else
    private static bool IsDebugBuild => false;
#endif
}
