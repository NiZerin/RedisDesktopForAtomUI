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
}
