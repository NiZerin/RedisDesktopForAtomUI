using Avalonia.Controls;
using Avalonia.Platform;

namespace RedisDesktop.App;

internal static class AppAssets
{
    public const string LogoIcoUri = "avares://RedisDesktop/Assets/logo.ico";

    private static WindowIcon? _windowIcon;

    public static WindowIcon WindowIcon => _windowIcon ??= LoadWindowIcon();

    private static WindowIcon LoadWindowIcon()
    {
        using var stream = AssetLoader.Open(new Uri(LogoIcoUri));
        return new WindowIcon(stream);
    }
}
