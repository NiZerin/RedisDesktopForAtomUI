using System.Diagnostics;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public static class AppUpdateApplier
{
    private static int _replacerStarted;

    public static string PublishedHostFileName
        => OperatingSystem.IsWindows() ? "RedisDesktop.exe" : "RedisDesktop";

    public static bool CanReplaceRunningApp
        => !IsDevelopmentHost() && File.Exists(ResolveTargetExecutable());

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

        var exe = ResolveTargetExecutable(settings);
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            return false;
        }

        var target = Path.GetDirectoryName(Path.GetFullPath(exe));
        if (string.IsNullOrWhiteSpace(target))
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
        settings.PendingUpdateTargetExe = null;
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

    public static bool TryRelaunchNewerInstallCopy()
    {
        if (IsDevelopmentHost())
        {
            return false;
        }

        try
        {
            var current = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(current) || !File.Exists(current))
            {
                return false;
            }

            var newer = FindNewerInstallCopy(current, ReadFileVersion(current) ?? AppReleaseParser.GetLocalVersion());
            if (newer is null)
            {
                return false;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = newer,
                WorkingDirectory = Path.GetDirectoryName(newer) ?? Environment.CurrentDirectory,
                UseShellExecute = false
            });
            if (process is null)
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

    public static void ReplaceOutdatedInstallCopies()
    {
        if (IsDevelopmentHost())
        {
            return;
        }

        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current) || !File.Exists(current))
        {
            return;
        }

        var version = ReadFileVersion(current) ?? AppReleaseParser.GetLocalVersion();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var failed = false;
            foreach (var sibling in EnumerateSiblingHosts(current))
            {
                if (!ShouldOverwriteInstallCopy(current, version, sibling, ReadFileVersion(sibling)))
                {
                    continue;
                }

                try
                {
                    File.Copy(current, sibling, overwrite: true);
                }
                catch
                {
                    failed = true;
                }
            }

            if (!failed)
            {
                return;
            }

            Thread.Sleep(250);
        }
    }

    public static string ResolveTargetExecutable(AppSettings? settings = null)
    {
        var stored = settings?.PendingUpdateTargetExe;
        if (!string.IsNullOrWhiteSpace(stored) && File.Exists(stored))
        {
            return Path.GetFullPath(stored);
        }

        if (!IsDevelopmentHost())
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            {
                return Path.GetFullPath(processPath);
            }
        }

        var directory = ResolveInstallDirectory(settings);
        var candidate = Path.Combine(directory, PublishedHostFileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return Environment.ProcessPath ?? candidate;
    }

    public static bool IsInstallHostName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);
        if (!string.IsNullOrEmpty(extension) && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return string.Equals(stem, "RedisDesktop", StringComparison.OrdinalIgnoreCase)
               || stem.StartsWith("RedisDesktop-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldOverwriteInstallCopy(
        string currentPath,
        string? currentVersion,
        string siblingPath,
        string? siblingVersion)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || string.IsNullOrWhiteSpace(siblingPath))
        {
            return false;
        }

        if (string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(siblingPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsInstallHostName(Path.GetFileName(siblingPath)))
        {
            return false;
        }

        return AppReleaseParser.IsNewer(currentVersion, siblingVersion);
    }

    public static string? FindNewerInstallCopy(string currentExe, string? currentVersion)
    {
        string? newest = null;
        var newestVersion = currentVersion;
        foreach (var sibling in EnumerateSiblingHosts(currentExe))
        {
            var version = ReadFileVersion(sibling);
            if (!AppReleaseParser.IsNewer(version, newestVersion))
            {
                continue;
            }

            newest = sibling;
            newestVersion = version;
        }

        return newest;
    }

    public static string? ReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var raw = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            return AppReleaseParser.TryParseVersion(raw, out var version) ? version.ToString(3) : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateSiblingHosts(string currentExe)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(currentExe));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        var current = Path.GetFullPath(currentExe);
        string[] files;
        try
        {
            files = OperatingSystem.IsWindows()
                ? Directory.GetFiles(directory, "RedisDesktop*.exe")
                : Directory.GetFiles(directory, "RedisDesktop*");
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (string.Equals(Path.GetFullPath(file), current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsInstallHostName(Path.GetFileName(file)))
            {
                yield return file;
            }
        }
    }

    private static string ResolveInstallDirectory(AppSettings? settings)
    {
        var stored = settings?.PendingUpdateTargetExe;
        if (!string.IsNullOrWhiteSpace(stored))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(stored));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                return dir;
            }
        }

        if (!IsDevelopmentHost())
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(processPath));
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return dir;
                }
            }
        }

        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool LaunchReplacer(int processId, string source, string target, string exe)
    {
        try
        {
            var script = WriteScript();
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -ProcessId {processId} -Source \"{source}\" -Target \"{target}\" -Exe \"{exe}\" -PublishedHost \"{PublishedHostFileName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
                : new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{script}\" {processId} \"{source}\" \"{target}\" \"{exe}\" \"{PublishedHostFileName}\"",
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

    private static string WriteScript()
    {
        var path = Path.Combine(AppPaths.UpdatesDirectory, OperatingSystem.IsWindows() ? "apply-update.ps1" : "apply-update.sh");
        File.WriteAllText(path, OperatingSystem.IsWindows() ? WindowsScript : UnixScript);
        return path;
    }

    private const string WindowsScript =
        """
        param(
          [Parameter(Mandatory = $true)][int]$ProcessId,
          [Parameter(Mandatory = $true)][string]$Source,
          [Parameter(Mandatory = $true)][string]$Target,
          [Parameter(Mandatory = $true)][string]$Exe,
          [Parameter(Mandatory = $true)][string]$PublishedHost
        )
        $ErrorActionPreference = 'Stop'
        $deadline = (Get-Date).AddMinutes(2)
        while ((Get-Date) -lt $deadline) {
          $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
          if ($null -eq $proc) { break }
          Start-Sleep -Milliseconds 400
        }
        Start-Sleep -Milliseconds 400
        if (-not (Test-Path $Target)) {
          New-Item -ItemType Directory -Path $Target -Force | Out-Null
        }
        $copied = $false
        for ($i = 0; $i -lt 40; $i++) {
          try {
            Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force
            $published = Join-Path $Target $PublishedHost
            if (Test-Path $published) {
              $publishedFull = [System.IO.Path]::GetFullPath($published)
              $exeFull = [System.IO.Path]::GetFullPath($Exe)
              if ($publishedFull -ne $exeFull) {
                Copy-Item -Path $publishedFull -Destination $exeFull -Force
              }
            }
            $copied = $true
            break
          } catch {
            Start-Sleep -Milliseconds 500
          }
        }
        if (-not $copied) {
          throw "Failed to replace $Exe"
        }
        Start-Process -FilePath $Exe
        """;

    private const string UnixScript =
        """
        #!/bin/bash
        set -e
        PID="$1"
        SRC="$2"
        DST="$3"
        EXE="$4"
        HOST="$5"
        for _ in $(seq 1 300); do
          if kill -0 "$PID" 2>/dev/null; then
            sleep 0.4
          else
            break
          fi
        done
        sleep 0.4
        mkdir -p "$DST"
        copied=0
        for _ in $(seq 1 40); do
          if cp -a "$SRC"/. "$DST"/; then
            if [ -f "$DST/$HOST" ]; then
              pub="$(cd "$(dirname "$DST/$HOST")" && pwd)/$(basename "$DST/$HOST")"
              exe="$(cd "$(dirname "$EXE")" && pwd)/$(basename "$EXE")"
              if [ "$pub" != "$exe" ]; then
                cp -f "$DST/$HOST" "$EXE"
              fi
            fi
            copied=1
            break
          fi
          sleep 0.5
        done
        if [ "$copied" -ne 1 ]; then
          exit 1
        fi
        nohup "$EXE" >/dev/null 2>&1 &
        """;
}
