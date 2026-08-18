namespace RedisDesktop.Core;

public sealed class ConnectionConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "localhost";

    public RedisDeploymentKind Kind { get; set; } = RedisDeploymentKind.Standalone;

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 6379;

    public int Database { get; set; }

    public string? Username { get; set; }

    /// <summary>
    /// DPAPI (or equivalent) ciphertext. Never store plaintext here.
    /// </summary>
    public string? PasswordProtected { get; set; }

    public string KeySeparator { get; set; } = ":";

    public bool ReadOnly { get; set; }

    public string? GroupName { get; set; }

    /// <summary>
    /// Optional label shown next to db index, e.g. "cache".
    /// </summary>
    public string? DatabaseAlias { get; set; }

    /// <summary>
    /// Optional sidebar color: red, blue, green, orange, purple.
    /// </summary>
    public string? ColorTag { get; set; }

    public TlsOptions Tls { get; set; } = new();

    public SentinelOptions Sentinel { get; set; } = new();

    public SshOptions Ssh { get; set; } = new();

    public ConnectionConfig Clone() => new()
    {
        Id = Guid.NewGuid(),
        Name = Name + " copy",
        Kind = Kind,
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        PasswordProtected = PasswordProtected,
        KeySeparator = KeySeparator,
        ReadOnly = ReadOnly,
        GroupName = GroupName,
        DatabaseAlias = DatabaseAlias,
        ColorTag = ColorTag,
        Tls = Tls.Clone(),
        Sentinel = Sentinel.Clone(),
        Ssh = Ssh.Clone()
    };
}

public sealed class TlsOptions
{
    public bool Enabled { get; set; }

    public string? CaPath { get; set; }

    public string? CertPath { get; set; }

    public string? KeyPath { get; set; }

    public string? ServerName { get; set; }

    public bool SkipCertValidation { get; set; }

    public TlsOptions Clone() => new()
    {
        Enabled = Enabled,
        CaPath = CaPath,
        CertPath = CertPath,
        KeyPath = KeyPath,
        ServerName = ServerName,
        SkipCertValidation = SkipCertValidation
    };
}

public sealed class SentinelOptions
{
    public string MasterName { get; set; } = "mymaster";

    public string? SentinelPasswordProtected { get; set; }

    public SentinelOptions Clone() => new()
    {
        MasterName = MasterName,
        SentinelPasswordProtected = SentinelPasswordProtected
    };
}

public sealed class SshOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string? PasswordProtected { get; set; }

    public string? PrivateKeyPath { get; set; }

    public string? PassphraseProtected { get; set; }

    public SshOptions Clone() => new()
    {
        Enabled = Enabled,
        Host = Host,
        Port = Port,
        Username = Username,
        PasswordProtected = PasswordProtected,
        PrivateKeyPath = PrivateKeyPath,
        PassphraseProtected = PassphraseProtected
    };
}
