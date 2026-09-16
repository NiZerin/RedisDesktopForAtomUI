using AtomUI.Desktop.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class UpdateAvailableViewModel : ViewModelBase, IDialogAwareDataContext
{
    private IDialog? _dialog;

    public UpdateAvailableViewModel(UiStrings loc, AppReleaseInfo release, string currentVersion)
    {
        Loc = loc;
        Release = release;
        CurrentVersion = currentVersion;
        Message = loc.T(
            $"发现新版本 {release.Version}（当前 {currentVersion}）。是否更新？",
            $"Version {release.Version} is available (current {currentVersion}). Update now?");
    }

    public UiStrings Loc { get; }

    public AppReleaseInfo Release { get; }

    public string CurrentVersion { get; }

    public string Message { get; }

    public string Notes => string.IsNullOrWhiteSpace(Release.Notes)
        ? Loc.T("无发行说明。", "No release notes.")
        : Release.Notes.Trim();

    public void NotifyAttachedToDialog(IDialog dialog) => _dialog = dialog;

    [RelayCommand]
    private void Update() => _dialog?.Done(AppUpdatePromptResult.Update);

    [RelayCommand]
    private void Cancel() => _dialog?.Done(AppUpdatePromptResult.Cancel);

    [RelayCommand]
    private void Mute() => _dialog?.Done(AppUpdatePromptResult.Mute);
}

public partial class UpdateDownloadViewModel : ViewModelBase, IDialogAwareDataContext
{
    private IDialog? _dialog;

    public UpdateDownloadViewModel(UiStrings loc, AppReleaseInfo release)
    {
        Loc = loc;
        Release = release;
        StatusText = loc.T($"正在下载 {release.Asset.Name}…", $"Downloading {release.Asset.Name}…");
    }

    public UiStrings Loc { get; }

    public AppReleaseInfo Release { get; }

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canCancel = true;

    private object? _pendingResult;

    public event Action? CancelRequested;

    public void NotifyAttachedToDialog(IDialog dialog)
    {
        _dialog = dialog;
        if (_pendingResult is not null)
        {
            dialog.Done(_pendingResult);
        }
    }

    public void Report(AppUpdateProgress progress)
    {
        Percent = progress.Percent;
        if (progress.TotalBytes is > 0)
        {
            StatusText = Loc.T(
                $"正在下载 {FormatSize(progress.BytesReceived)} / {FormatSize(progress.TotalBytes.Value)}",
                $"Downloading {FormatSize(progress.BytesReceived)} / {FormatSize(progress.TotalBytes.Value)}");
        }
    }

    public void Complete()
    {
        CanCancel = false;
        Percent = 100;
        StatusText = Loc.T("下载完成，将在重启后替换应用。", "Download complete. The app will be replaced after restart.");
        Close(true);
    }

    public void Fail(string message)
    {
        CanCancel = false;
        StatusText = message;
        Close(false);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }

        CancelRequested?.Invoke();
        Close(false);
    }

    private void Close(object result)
    {
        if (_dialog is not null)
        {
            _dialog.Done(result);
            return;
        }

        _pendingResult = result;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.0} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }
}
