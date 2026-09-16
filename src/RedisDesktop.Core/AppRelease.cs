using System.Runtime.InteropServices;
using System.Text.Json;

namespace RedisDesktop.Core;

public sealed record AppReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record AppReleaseInfo(
    string Version,
    string TagName,
    string HtmlUrl,
    string? Notes,
    AppReleaseAsset Asset);

public sealed record AppUpdateProgress(double Percent, long BytesReceived, long? TotalBytes);

public enum AppUpdatePromptResult
{
    Cancel = 0,
    Update = 1,
    Mute = 2
}

public static class AppReleaseParser
{
    public const string GitHubOwner = "NiZerin";
    public const string GitHubRepo = "RedisDesktopForAtomUI";
    public const string GitHubUrl = "https://github.com/NiZerin/RedisDesktopForAtomUI";
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/NiZerin/RedisDesktopForAtomUI/releases/latest";

    public static string CurrentRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }

        if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        }

        return "win-x64";
    }

    public static string PreferredAssetName(string? runtimeIdentifier = null)
        => $"RedisDesktop-{runtimeIdentifier ?? CurrentRuntimeIdentifier()}.zip";

    public static string GetLocalVersion()
    {
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version
                      ?? typeof(AppReleaseParser).Assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString(3);
    }

    public static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        if (!Version.TryParse(text, out var parsed) && !Version.TryParse(text + ".0", out parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    public static string NormalizeVersion(string? raw)
        => TryParseVersion(raw, out var version) ? version.ToString() : (raw ?? string.Empty).Trim();

    public static int Compare(string? left, string? right)
    {
        TryParseVersion(left, out var a);
        TryParseVersion(right, out var b);
        return a.CompareTo(b);
    }

    public static bool IsNewer(string? latest, string? current) => Compare(latest, current) > 0;

    public static AppReleaseInfo? ParseLatest(string json, string? runtimeIdentifier = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var version = NormalizeVersion(tag);
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var asset = SelectAsset(root, runtimeIdentifier ?? CurrentRuntimeIdentifier());
        if (asset is null)
        {
            return null;
        }

        return new AppReleaseInfo(
            version,
            tag,
            ReadString(root, "html_url") ?? GitHubUrl,
            ReadString(root, "body"),
            asset);
    }

    private static AppReleaseAsset? SelectAsset(JsonElement root, string runtimeIdentifier)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var preferred = PreferredAssetName(runtimeIdentifier);
        AppReleaseAsset? fallback = null;
        foreach (var item in assets.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var url = ReadString(item, "browser_download_url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || !name.Contains("RedisDesktop", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var size = item.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var bytes)
                ? bytes
                : 0;
            var asset = new AppReleaseAsset(name, url, size);
            if (string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }

            fallback ??= asset;
        }

        return fallback;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
}
