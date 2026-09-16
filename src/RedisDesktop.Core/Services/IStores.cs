namespace RedisDesktop.Core;

public interface IConnectionStore
{
    Task<IReadOnlyList<ConnectionConfig>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<ConnectionConfig> connections, CancellationToken cancellationToken = default);
}

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    void Save(AppSettings settings);
}

public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedText);
}

public interface IAppUpdateService
{
    string CurrentVersion { get; }

    bool CanReplaceRunningApp { get; }

    Task<AppReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadAndExtractAsync(
        AppReleaseInfo release,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    bool TryApplyPending(AppSettings settings);

    void ClearPending(AppSettings settings);
}

public interface ICommandLog
{
    IReadOnlyList<CommandLogEntry> Entries { get; }

    void Append(CommandLogEntry entry);

    void Clear();

    event EventHandler<CommandLogEntry>? EntryAdded;
}
