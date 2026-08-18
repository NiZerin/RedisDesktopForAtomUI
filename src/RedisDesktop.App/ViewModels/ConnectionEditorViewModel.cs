using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class ConnectionEditorViewModel : ViewModelBase
{
    public bool IsNew { get; private init; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _host = "127.0.0.1";

    [ObservableProperty]
    private int _port = 6379;

    [ObservableProperty]
    private int _database;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _keySeparator = ":";

    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    private string _groupName = string.Empty;

    [ObservableProperty]
    private string _databaseAlias = string.Empty;

    [ObservableProperty]
    private string _colorTag = string.Empty;

    [ObservableProperty]
    private RedisDeploymentKind _kind = RedisDeploymentKind.Standalone;

    [ObservableProperty]
    private bool _tlsEnabled;

    [ObservableProperty]
    private string _tlsCaPath = string.Empty;

    [ObservableProperty]
    private string _tlsCertPath = string.Empty;

    [ObservableProperty]
    private string _tlsKeyPath = string.Empty;

    [ObservableProperty]
    private string _tlsServerName = string.Empty;

    [ObservableProperty]
    private bool _skipCertValidation;

    [ObservableProperty]
    private string _masterName = "mymaster";

    [ObservableProperty]
    private string _sentinelPassword = string.Empty;

    [ObservableProperty]
    private bool _sshEnabled;

    [ObservableProperty]
    private string _sshHost = string.Empty;

    [ObservableProperty]
    private int _sshPort = 22;

    [ObservableProperty]
    private string _sshUsername = string.Empty;

    [ObservableProperty]
    private string _sshPassword = string.Empty;

    [ObservableProperty]
    private string _sshPrivateKeyPath = string.Empty;

    [ObservableProperty]
    private string _sshPassphrase = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    public bool IsStandalone => Kind == RedisDeploymentKind.Standalone;

    public bool IsSentinel => Kind == RedisDeploymentKind.Sentinel;

    public bool IsNotSentinel => Kind != RedisDeploymentKind.Sentinel;

    public bool IsCluster => Kind == RedisDeploymentKind.Cluster;

    public bool ClusterChecked
    {
        get => Kind == RedisDeploymentKind.Cluster;
        set => Kind = value
            ? RedisDeploymentKind.Cluster
            : Kind == RedisDeploymentKind.Cluster
                ? RedisDeploymentKind.Standalone
                : Kind;
    }

    public bool SentinelChecked
    {
        get => Kind == RedisDeploymentKind.Sentinel;
        set => Kind = value
            ? RedisDeploymentKind.Sentinel
            : Kind == RedisDeploymentKind.Sentinel
                ? RedisDeploymentKind.Standalone
                : Kind;
    }

    public UiStrings Loc { get; set; } = new();

    public static ConnectionEditorViewModel CreateNew() => new() { IsNew = true };

    public static ConnectionEditorViewModel FromConfig(ConnectionConfig config, ISecretProtector protector)
    {
        var vm = new ConnectionEditorViewModel { IsNew = false };
        vm.Name = config.Name;
        vm.Host = config.Host;
        vm.Port = config.Port;
        vm.Database = config.Database;
        vm.Username = config.Username ?? string.Empty;
        vm.KeySeparator = config.KeySeparator ?? string.Empty;
        vm.ReadOnly = config.ReadOnly;
        vm.GroupName = config.GroupName ?? string.Empty;
        vm.DatabaseAlias = config.DatabaseAlias ?? string.Empty;
        vm.ColorTag = config.ColorTag ?? string.Empty;
        vm.Kind = config.Kind;
        vm.TlsEnabled = config.Tls.Enabled;
        vm.TlsCaPath = config.Tls.CaPath ?? string.Empty;
        vm.TlsCertPath = config.Tls.CertPath ?? string.Empty;
        vm.TlsKeyPath = config.Tls.KeyPath ?? string.Empty;
        vm.TlsServerName = config.Tls.ServerName ?? string.Empty;
        vm.SkipCertValidation = config.Tls.SkipCertValidation;
        vm.MasterName = string.IsNullOrEmpty(config.Sentinel.MasterName) ? "mymaster" : config.Sentinel.MasterName;
        vm.SshEnabled = config.Ssh.Enabled;
        vm.SshHost = config.Ssh.Host ?? string.Empty;
        vm.SshPort = config.Ssh.Port <= 0 ? 22 : config.Ssh.Port;
        vm.SshUsername = config.Ssh.Username ?? string.Empty;
        vm.SshPrivateKeyPath = config.Ssh.PrivateKeyPath ?? string.Empty;
        vm.Password = Unprotect(protector, config.PasswordProtected);
        vm.SentinelPassword = Unprotect(protector, config.Sentinel.SentinelPasswordProtected);
        vm.SshPassword = Unprotect(protector, config.Ssh.PasswordProtected);
        vm.SshPassphrase = Unprotect(protector, config.Ssh.PassphraseProtected);
        return vm;
    }

    public bool TryBuild(out ConnectionConfig config, out string? error)
    {
        config = new ConnectionConfig();
        error = null;
        if (string.IsNullOrWhiteSpace(Host))
        {
            Host = "127.0.0.1";
        }

        if (Port is <= 0 or > 65535)
        {
            Port = 6379;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = Host.Trim();
        }

        if (Kind == RedisDeploymentKind.Sentinel && string.IsNullOrWhiteSpace(MasterName))
        {
            error = "Sentinel 模式需要 MasterName。";
            return false;
        }

        if (SshEnabled)
        {
            if (string.IsNullOrWhiteSpace(SshHost) || string.IsNullOrWhiteSpace(SshUsername))
            {
                error = "SSH 需要 Host 与用户名。";
                return false;
            }

            if (SshPort is <= 0 or > 65535)
            {
                error = "SSH 端口无效。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(SshPrivateKeyPath) && string.IsNullOrEmpty(SshPassword))
            {
                error = "SSH 需要密码或私钥。";
                return false;
            }
        }

        config.Name = Name.Trim();
        config.Host = Host.Trim();
        config.Port = Port;
        config.Database = Kind == RedisDeploymentKind.Cluster ? 0 : Math.Max(0, Database);
        config.Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
        config.KeySeparator = KeySeparator ?? string.Empty;
        config.ReadOnly = ReadOnly;
        config.GroupName = string.IsNullOrWhiteSpace(GroupName) ? null : GroupName.Trim();
        config.DatabaseAlias = string.IsNullOrWhiteSpace(DatabaseAlias) ? null : DatabaseAlias.Trim();
        config.ColorTag = string.IsNullOrWhiteSpace(ColorTag) ? null : ColorTag.Trim().ToLowerInvariant();
        config.Kind = Kind;
        config.Tls = new TlsOptions
        {
            Enabled = TlsEnabled,
            CaPath = EmptyToNull(TlsCaPath),
            CertPath = EmptyToNull(TlsCertPath),
            KeyPath = EmptyToNull(TlsKeyPath),
            ServerName = EmptyToNull(TlsServerName),
            SkipCertValidation = SkipCertValidation
        };
        config.Sentinel = new SentinelOptions
        {
            MasterName = string.IsNullOrWhiteSpace(MasterName) ? "mymaster" : MasterName.Trim()
        };
        config.Ssh = new SshOptions
        {
            Enabled = SshEnabled,
            Host = SshHost.Trim(),
            Port = SshPort,
            Username = SshUsername.Trim(),
            PrivateKeyPath = EmptyToNull(SshPrivateKeyPath)
        };
        return true;
    }

    public ConnectionSecrets BuildSecrets() => new()
    {
        RedisPassword = EmptyToNull(Password),
        SentinelPassword = EmptyToNull(SentinelPassword),
        SshPassword = EmptyToNull(SshPassword),
        SshPassphrase = EmptyToNull(SshPassphrase)
    };

    public void ApplyProtectedSecrets(ISecretProtector protector, ConnectionConfig config, ConnectionConfig? existing)
    {
        config.PasswordProtected = ProtectOrKeep(protector, Password, existing?.PasswordProtected);
        config.Sentinel.SentinelPasswordProtected = ProtectOrKeep(protector, SentinelPassword, existing?.Sentinel.SentinelPasswordProtected);
        config.Ssh.PasswordProtected = ProtectOrKeep(protector, SshPassword, existing?.Ssh.PasswordProtected);
        config.Ssh.PassphraseProtected = ProtectOrKeep(protector, SshPassphrase, existing?.Ssh.PassphraseProtected);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTesting)
        {
            return;
        }

        if (!TryBuild(out var config, out var error))
        {
            await App.Services.GetRequiredService<IUserPrompt>().ErrorAsync(error ?? "参数无效");
            return;
        }

        IsTesting = true;
        try
        {
            await App.Services.GetRequiredService<IRedisSessionFactory>().TestAsync(config, BuildSecrets());
            App.Services.GetRequiredService<IUserPrompt>().Info(Loc.T("连接成功", "Connection successful"));
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IUserPrompt>().ErrorAsync(Loc.T("测试连接失败", "Test connection failed"), ex);
        }
        finally
        {
            IsTesting = false;
        }
    }

    partial void OnKindChanged(RedisDeploymentKind value)
    {
        OnPropertyChanged(nameof(IsStandalone));
        OnPropertyChanged(nameof(IsSentinel));
        OnPropertyChanged(nameof(IsNotSentinel));
        OnPropertyChanged(nameof(IsCluster));
        OnPropertyChanged(nameof(ClusterChecked));
        OnPropertyChanged(nameof(SentinelChecked));
    }

    private static string Unprotect(ISecretProtector protector, string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return string.Empty;
        }

        try
        {
            return protector.Unprotect(protectedText);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ProtectOrKeep(ISecretProtector protector, string plaintext, string? existing)
    {
        if (!string.IsNullOrEmpty(plaintext))
        {
            return protector.Protect(plaintext);
        }

        return existing;
    }
}
