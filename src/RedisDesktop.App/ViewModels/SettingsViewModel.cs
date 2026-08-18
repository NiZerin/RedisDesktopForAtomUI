using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.App.ViewModels;

public sealed record SettingChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record HotkeyTip(string Key, string Description);

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _original;
    private bool _ready;

    public SettingsViewModel(MainWindowViewModel owner)
    {
        Owner = owner;
        Loc = owner.Loc;
        _original = Clone(owner.Settings);
        ThemeOptions =
        [
            new SettingChoice("system", Loc.ThemeSystem),
            new SettingChoice("light", Loc.ThemeLight),
            new SettingChoice("dark", Loc.ThemeDark)
        ];
        LanguageOptions =
        [
            new SettingChoice("zh-CN", "简体中文"),
            new SettingChoice("en-US", "English")
        ];
        FontOptions.Add(Loc.FontDefault);
        LoadFrom(owner.Settings);
        AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        _ready = true;
    }

    public MainWindowViewModel Owner { get; }

    public UiStrings Loc { get; }

    public string AppVersion { get; }

    public IReadOnlyList<SettingChoice> ThemeOptions { get; }

    public IReadOnlyList<SettingChoice> LanguageOptions { get; }

    public ObservableCollection<string> FontOptions { get; } = [];

    public IReadOnlyList<HotkeyTip> Hotkeys =>
    [
        new("Ctrl + N / ⌘ + N", Loc.NewConnection),
        new("Ctrl + , / ⌘ + ,", Loc.Settings),
        new("Ctrl + G / ⌘ + G", Loc.CommandLog),
        new("Ctrl + R / ⌘ + R / F5", Loc.HotkeyRefreshTab),
        new("Ctrl + D / ⌘ + D", Loc.HotkeyDeleteKey),
        new("Ctrl + S / ⌘ + S", Loc.HotkeySaveKey),
        new("Ctrl + L / ⌘ + L", Loc.HotkeyClearCli),
        new("Ctrl / ⌘ + click Key", Loc.HotkeyOpenNewTab),
        new("Ctrl + / / ⌘ + /", Loc.HotkeyTips)
    ];

    [ObservableProperty]
    private SettingChoice? _selectedTheme;

    [ObservableProperty]
    private SettingChoice? _selectedLanguage;

    [ObservableProperty]
    private decimal _zoomFactor = 1.0m;

    [ObservableProperty]
    private string _selectedFont = string.Empty;

    [ObservableProperty]
    private decimal _scanCount = 200;

    public void ApplyTo(AppSettings settings)
    {
        var theme = SelectedTheme?.Value ?? "system";
        settings.ThemeMode = theme;
        settings.IsDarkTheme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        settings.Language = SelectedLanguage?.Value ?? "zh-CN";
        settings.ZoomFactor = (double)ZoomFactor;
        settings.ScanCount = (int)ScanCount;
        settings.FontFamilies = IsDefaultFont(SelectedFont)
            ? []
            : [SelectedFont];
    }

    public void RevertPreview()
    {
        Owner.ApplyTheme(_original.ThemeMode, _original.IsDarkTheme);
        Owner.PreviewZoom(_original.ZoomFactor);
    }

    public void EnsureFonts()
    {
        if (FontOptions.Count > 1)
        {
            return;
        }

        try
        {
            var names = FontManager.Current.SystemFonts
                .Select(font => font.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                FontOptions.Add(name);
            }
        }
        catch
        {
            // System font enumeration can fail on some platforms; Default still works.
        }
    }

    [RelayCommand]
    private Task ExportConnectionsAsync() => Owner.ExportConnectionsCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task ImportConnectionsAsync() => Owner.ImportConnectionsCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task ShowHotkeysAsync() => Owner.Prompt.ShowHotkeysAsync(this);

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (!await Owner.ClearCacheAsync())
        {
            return;
        }

        LoadFrom(Owner.Settings);
        Owner.ApplyTheme(SelectedTheme?.Value ?? "system", false);
        Owner.PreviewZoom((double)ZoomFactor);
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.Root,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    partial void OnSelectedThemeChanged(SettingChoice? value)
    {
        if (!_ready || value is null)
        {
            return;
        }

        Owner.ApplyTheme(value.Value, string.Equals(value.Value, "dark", StringComparison.OrdinalIgnoreCase));
    }

    partial void OnZoomFactorChanged(decimal value)
    {
        if (!_ready)
        {
            return;
        }

        Owner.PreviewZoom((double)value);
    }

    private void LoadFrom(AppSettings settings)
    {
        var mode = string.IsNullOrWhiteSpace(settings.ThemeMode)
            ? (settings.IsDarkTheme ? "dark" : "light")
            : settings.ThemeMode;
        SelectedTheme = ThemeOptions.FirstOrDefault(x => string.Equals(x.Value, mode, StringComparison.OrdinalIgnoreCase))
                        ?? ThemeOptions[0];
        var language = settings.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
        SelectedLanguage = LanguageOptions.First(x => x.Value == language);
        ZoomFactor = (decimal)Math.Clamp(settings.ZoomFactor <= 0 ? 1 : settings.ZoomFactor, 0.5, 2.0);
        ScanCount = Math.Clamp(settings.ScanCount <= 0 ? 200 : settings.ScanCount, 10, 20000);
        var font = settings.FontFamilies?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        SelectedFont = string.IsNullOrWhiteSpace(font) ? Loc.FontDefault : font;
        if (!IsDefaultFont(SelectedFont) && !FontOptions.Contains(SelectedFont))
        {
            FontOptions.Add(SelectedFont);
        }
    }

    private bool IsDefaultFont(string? font)
        => string.IsNullOrWhiteSpace(font)
           || string.Equals(font, Loc.FontDefault, StringComparison.OrdinalIgnoreCase)
           || string.Equals(font, "Default Initial", StringComparison.OrdinalIgnoreCase);

    private static AppSettings Clone(AppSettings settings)
        => new()
        {
            SideBarWidth = settings.SideBarWidth,
            IsDarkTheme = settings.IsDarkTheme,
            ThemeMode = settings.ThemeMode,
            ScanCount = settings.ScanCount,
            Language = settings.Language,
            ZoomFactor = settings.ZoomFactor,
            FontFamilies = [.. settings.FontFamilies ?? []]
        };
}
