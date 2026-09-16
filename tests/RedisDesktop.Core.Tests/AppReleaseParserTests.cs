using RedisDesktop.Core;

namespace RedisDesktop.Core.Tests;

public class AppReleaseParserTests
{
    private const string Sample = """
        {
          "tag_name": "v1.2.3",
          "html_url": "https://github.com/NiZerin/RedisDesktopForAtomUI/releases/tag/v1.2.3",
          "body": "bug fixes",
          "assets": [
            {
              "name": "RedisDesktop-linux-x64.zip",
              "browser_download_url": "https://example.com/linux.zip",
              "size": 10
            },
            {
              "name": "RedisDesktop-win-x64.zip",
              "browser_download_url": "https://example.com/win.zip",
              "size": 42
            }
          ]
        }
        """;

    [Theory]
    [InlineData("1.0.3", "1.0.2", true)]
    [InlineData("v1.0.3", "1.0.3", false)]
    [InlineData("1.0.2", "1.0.3", false)]
    [InlineData("1.0.3-beta", "1.0.2", true)]
    public void IsNewer_compares_semver(string latest, string current, bool expected)
        => Assert.Equal(expected, AppReleaseParser.IsNewer(latest, current));

    [Fact]
    public void ParseLatest_picks_runtime_zip()
    {
        var release = AppReleaseParser.ParseLatest(Sample, "win-x64");

        Assert.NotNull(release);
        Assert.Equal("1.2.3", release!.Version);
        Assert.Equal("v1.2.3", release.TagName);
        Assert.Equal("RedisDesktop-win-x64.zip", release.Asset.Name);
        Assert.Equal("https://example.com/win.zip", release.Asset.DownloadUrl);
        Assert.Equal(42, release.Asset.Size);
        Assert.Equal("bug fixes", release.Notes);
    }

    [Fact]
    public void ParseLatest_falls_back_when_rid_missing()
    {
        var release = AppReleaseParser.ParseLatest(Sample, "win-arm64");

        Assert.NotNull(release);
        Assert.Equal("RedisDesktop-linux-x64.zip", release!.Asset.Name);
    }

    [Fact]
    public void ParseLatest_returns_null_without_assets()
    {
        const string json = """{ "tag_name": "v1.0.0", "assets": [] }""";

        Assert.Null(AppReleaseParser.ParseLatest(json, "win-x64"));
    }

    [Fact]
    public void PreferredAssetName_matches_publish_layout()
        => Assert.Equal("RedisDesktop-win-x64.zip", AppReleaseParser.PreferredAssetName("win-x64"));
}
