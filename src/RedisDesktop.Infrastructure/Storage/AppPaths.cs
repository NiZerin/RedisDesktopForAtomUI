using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public static class AppPaths
{
    public static string Root
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RedisDesktopForAtomUI");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConnectionsFile => Path.Combine(Root, "connections.json");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
}
