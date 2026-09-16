using System.IO.Compression;
using System.Net.Http.Headers;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public sealed class GitHubAppUpdateService : IAppUpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public string CurrentVersion { get; } = AppReleaseParser.GetLocalVersion();

    public bool CanReplaceRunningApp => AppUpdateApplier.CanReplaceRunningApp;

    public async Task<AppReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppReleaseParser.LatestReleaseApiUrl);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return AppReleaseParser.ParseLatest(json);
    }

    public async Task<string> DownloadAndExtractAsync(
        AppReleaseInfo release,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var zipPath = Path.Combine(AppPaths.UpdatesDirectory, release.Asset.Name);
        var extractDir = Path.Combine(AppPaths.UpdatesDirectory, "pending-" + release.Version);
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        await DownloadAsync(release.Asset.DownloadUrl, zipPath, release.Asset.Size, progress, cancellationToken)
            .ConfigureAwait(false);
        ExtractZip(zipPath, extractDir);
        return extractDir;
    }

    public bool TryApplyPending(AppSettings settings)
        => AppUpdateApplier.TryApplyPending(settings);

    public void ClearPending(AppSettings settings) => AppUpdateApplier.ClearPending(settings);

    private static async Task DownloadAsync(
        string url,
        string destination,
        long expectedSize,
        IProgress<AppUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temp = destination + ".part";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? (expectedSize > 0 ? expectedSize : null);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long readTotal = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            var percent = total is > 0 ? Math.Clamp(readTotal * 100d / total.Value, 0, 100) : 0;
            progress?.Report(new AppUpdateProgress(percent, readTotal, total));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Close();
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        File.Move(temp, destination);
        progress?.Report(new AppUpdateProgress(100, readTotal, total ?? readTotal));
    }

    private static void ExtractZip(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(zipPath);
        var rootPrefix = CommonRootPrefix(archive);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var relative = entry.FullName.Replace('\\', '/');
            if (!string.IsNullOrEmpty(rootPrefix)
                && relative.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                relative = relative[rootPrefix.Length..];
            }

            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新包路径非法。");
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string? CommonRootPrefix(ZipArchive archive)
    {
        var names = archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (names.Count == 0)
        {
            return null;
        }

        var first = names[0];
        var slash = first.IndexOf('/');
        if (slash <= 0)
        {
            return null;
        }

        var prefix = first[..(slash + 1)];
        return names.All(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ? prefix : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RedisDesktop", AppReleaseParser.GetLocalVersion()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
