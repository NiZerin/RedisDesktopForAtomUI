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
    private bool _fontsLoaded;

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
        LanguageOptions = UiLanguages.All
            .Select(language => new SettingChoice(language.Code, language.Label))
            .ToList();
        FontOptions.Add(FontChoice.CreateDefault(Loc.FontDefault));
        LoadFrom(owner.Settings);
        AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.3";
        _ready = true;
    }

    public MainWindowViewModel Owner { get; }

    public UiStrings Loc { get; }

    public string AppVersion { get; }

    public string GitHubUrl { get; } = "https://github.com/NiZerin/RedisDesktopForAtomUI";

    public IReadOnlyList<SettingChoice> ThemeOptions { get; }

    public IReadOnlyList<SettingChoice> LanguageOptions { get; }

    public ObservableCollection<FontChoice> FontOptions { get; } = [];

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
    private FontChoice? _selectedFont;

    [ObservableProperty]
    private decimal _scanCount = 200;

    public void ApplyTo(AppSettings settings)
    {
        var theme = SelectedTheme?.Value ?? "system";
        settings.ThemeMode = theme;
        settings.IsDarkTheme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        settings.Language = UiLanguages.Normalize(SelectedLanguage?.Value);
        settings.ZoomFactor = (double)ZoomFactor;
        settings.ScanCount = (int)ScanCount;
        settings.FontFamilies = SelectedFont is null || SelectedFont.IsDefault
            ? []
            : [SelectedFont.Name];
    }

    public void RevertPreview()
    {
        Owner.ApplyTheme(_original.ThemeMode, _original.IsDarkTheme);
        Owner.ApplyAppearance(_original);
    }

    public void EnsureFonts()
    {
        if (_fontsLoaded)
        {
            return;
        }

        _fontsLoaded = true;
        var selectedName = SelectedFont?.Name;
        var wasReady = _ready;
        _ready = false;
        try
        {
            for (var i = FontOptions.Count - 1; i >= 1; i--)
            {
                FontOptions.RemoveAt(i);
            }

            foreach (var font in UiFontCatalog.ListChoices(Loc.FontDefault, Loc.PreferLocalizedFontNames).Skip(1))
            {
                FontOptions.Add(font);
            }
        }
        catch
        {
            // System font enumeration can fail on some platforms; Default still works.
        }
        finally
        {
            SelectedFont = FindFont(selectedName);
            _ready = wasReady;
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
        Owner.ApplyAppearance(Owner.Settings);
    }

    [RelayCommand]
    private void OpenConfigFolder() => OpenPath(AppPaths.Root);

    [RelayCommand]
    private void OpenGitHub() => OpenPath(GitHubUrl);

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
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

    partial void OnSelectedFontChanged(FontChoice? value)
    {
        if (!_ready)
        {
            return;
        }

        Owner.PreviewFont(value?.Name, SelectedLanguage?.Value);
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
        var language = UiLanguages.Normalize(settings.Language);
        SelectedLanguage = LanguageOptions.FirstOrDefault(x => x.Value == language)
                           ?? LanguageOptions.First(x => x.Value == UiLanguages.DefaultCode);
        ZoomFactor = (decimal)Math.Clamp(settings.ZoomFactor <= 0 ? 1 : settings.ZoomFactor, 0.5, 2.0);
        ScanCount = Math.Clamp(settings.ScanCount <= 0 ? 200 : settings.ScanCount, 10, 20000);
        SelectedFont = FindFont(settings.FontFamilies?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
    }

    private FontChoice FindFont(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || string.Equals(name, Loc.FontDefault, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Default Initial", StringComparison.OrdinalIgnoreCase))
        {
            return FontOptions[0];
        }

        var match = FontOptions.FirstOrDefault(font => string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        var custom = new FontChoice(name, name, new FontFamily(UiFontCatalog.SanitizeFamilyName(name)));
        FontOptions.Add(custom);
        return custom;
    }

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
