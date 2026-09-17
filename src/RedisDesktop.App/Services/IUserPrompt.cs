using RedisDesktop.App.ViewModels;
using RedisDesktop.Core;

namespace RedisDesktop.App;

public interface IUserPrompt
{
    void Info(string message);

    Task ErrorAsync(string message, Exception? exception = null);

    Task<bool> ConfirmAsync(string title, string message);

    Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel viewModel);

    Task<bool> ShowSettingsAsync(SettingsViewModel viewModel);

    Task ShowHotkeysAsync(SettingsViewModel viewModel);

    Task ShowCommandLogAsync(CommandLogViewModel viewModel);

    Task<bool> ShowNewKeyDialogAsync(NewKeyDialogViewModel viewModel);

    Task<string?> PickSaveFileAsync(string title, string suggestedName);

    Task<string?> PickOpenFileAsync(string title);

    Task<string?> PromptTextAsync(string title, string message, string? initial = null);

    Task<AppUpdatePromptResult> ShowUpdateAvailableAsync(UpdateAvailableViewModel viewModel);

    Task ShowUpdateDownloadAsync(UpdateDownloadViewModel viewModel);

    Task<bool> ShowUpdateRestartNoticeAsync(string title, string message);
}

public interface IThemeService
{
    void Apply(AppSettings settings);

    void ApplyTheme(string themeMode, bool isDark);
}
