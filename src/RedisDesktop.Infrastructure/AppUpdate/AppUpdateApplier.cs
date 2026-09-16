using System.Diagnostics;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public static class AppUpdateApplier
{
    private static int _replacerStarted;

    public static bool CanReplaceRunningApp
        => !IsDevelopmentHost() && File.Exists(ResolveAppExecutable());

    public static bool IsDevelopmentHost()
    {
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        return string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "testhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "ReSharperTestRunner", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryApplyPending(AppSettings settings)
    {
        if (IsDevelopmentHost())
        {
            return false;
        }

        var source = settings.PendingUpdatePackagePath;
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            return false;
        }

        var current = AppReleaseParser.GetLocalVersion();
        if (!AppReleaseParser.IsNewer(settings.PendingUpdateVersion, current))
        {
            return false;
        }

        var target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exe = ResolveAppExecutable();
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _replacerStarted, 1, 0) != 0)
        {
            return true;
        }

        if (!LaunchReplacer(Environment.ProcessId, source, target, exe))
        {
            Interlocked.Exchange(ref _replacerStarted, 0);
            return false;
        }

        return true;
    }

    public static void ClearPending(AppSettings settings)
    {
        var path = settings.PendingUpdatePackagePath;
        settings.PendingUpdateVersion = null;
        settings.PendingUpdatePackagePath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // leftover files are harmless
        }
    }

    public static bool TryApplyPendingFromDisk()
    {
        try
        {
            var store = new JsonAppSettingsStore();
            var settings = store.LoadAsync().GetAwaiter().GetResult();
            if (!TryApplyPending(settings))
            {
                return false;
            }

            Environment.Exit(0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LaunchReplacer(int processId, string source, string target, string exe)
    {
        try
        {
            var script = WriteScript(source);
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -ProcessId {processId} -Source \"{source}\" -Target \"{target}\" -Exe \"{exe}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
                : new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{script}\" {processId} \"{source}\" \"{target}\" \"{exe}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            using var process = Process.Start(start);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string WriteScript(string sourceDirectory)
    {
        var path = Path.Combine(AppPaths.UpdatesDirectory, OperatingSystem.IsWindows() ? "apply-update.ps1" : "apply-update.sh");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, WindowsScript);
        }
        else
        {
            File.WriteAllText(path, UnixScript);
        }

        _ = sourceDirectory;
        return path;
    }

    private static string ResolveAppExecutable()
    {
        var directory = AppContext.BaseDirectory;
        var name = OperatingSystem.IsWindows() ? "RedisDesktop.exe" : "RedisDesktop";
        var candidate = Path.Combine(directory, name);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return Environment.ProcessPath ?? candidate;
    }

    private const string WindowsScript =
        """
        param(
          [Parameter(Mandatory = $true)][int]$ProcessId,
          [Parameter(Mandatory = $true)][string]$Source,
          [Parameter(Mandatory = $true)][string]$Target,
          [Parameter(Mandatory = $true)][string]$Exe
        )
        $deadline = (Get-Date).AddMinutes(2)
        while ((Get-Date) -lt $deadline) {
          $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
          if ($null -eq $proc) { break }
          Start-Sleep -Milliseconds 400
        }
        Start-Sleep -Milliseconds 400
        Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force
        Start-Process -FilePath $Exe
        """;

    private const string UnixScript =
        """
        #!/bin/bash
        PID="$1"
        SRC="$2"
        DST="$3"
        EXE="$4"
        for _ in $(seq 1 300); do
          if kill -0 "$PID" 2>/dev/null; then
            sleep 0.4
          else
            break
          fi
        done
        sleep 0.4
        cp -a "$SRC"/. "$DST"/
        nohup "$EXE" >/dev/null 2>&1 &
        """;
}
