namespace RedisDesktop.Core;

public sealed class AppSettings
{
    public double SideBarWidth { get; set; } = 280;

    public bool IsDarkTheme { get; set; }

    /// <summary>
    /// light | dark | system
    /// </summary>
    public string ThemeMode { get; set; } = "system";

    public int ScanCount { get; set; } = 200;

    public string Language { get; set; } = "zh-CN";

    /// <summary>
    /// Page zoom factor, 0.5–2.0. Matches ARDM <c>zoomFactor</c>.
    /// </summary>
    public double ZoomFactor { get; set; } = 1.0;

    /// <summary>
    /// Preferred UI font names. Empty means the AtomUI default.
    /// </summary>
    public List<string> FontFamilies { get; set; } = [];

    /// <summary>
    /// When true, startup no longer shows the update dialog. Settings can still check and install.
    /// </summary>
    public bool MuteUpdatePrompt { get; set; }

    public string? PendingUpdateVersion { get; set; }

    public string? PendingUpdatePackagePath { get; set; }

    /// <summary>
    /// The executable the user actually launched. The updater must overwrite this path,
    /// not only <c>RedisDesktop.exe</c>, so a downloaded <c>RedisDesktop-win-x64.exe</c> stays current.
    /// </summary>
    public string? PendingUpdateTargetExe { get; set; }

    public AppSettings ResetUserData(double sideBarWidth)
        => new()
        {
            SideBarWidth = sideBarWidth < 220 ? 280 : sideBarWidth,
            PendingUpdateVersion = PendingUpdateVersion,
            PendingUpdatePackagePath = PendingUpdatePackagePath,
            PendingUpdateTargetExe = PendingUpdateTargetExe
        };
}
