namespace RedisDesktop.Core;

public interface IRedisSessionFactory
{
    Task<IRedisSession> ConnectAsync(ConnectionConfig config, string? password, CancellationToken cancellationToken = default);

    Task<IRedisSession> ConnectAsync(ConnectionConfig config, ConnectionSecrets secrets, CancellationToken cancellationToken = default);

    Task TestAsync(ConnectionConfig config, string? password, CancellationToken cancellationToken = default);

    Task TestAsync(ConnectionConfig config, ConnectionSecrets secrets, CancellationToken cancellationToken = default);
}

public interface IRedisSession : IAsyncDisposable
{
    ConnectionConfig Config { get; }

    SessionState State { get; }

    int CurrentDatabase { get; }

    IKeyBrowser Keys { get; }

    IValueService Values { get; }

    ICliExecutor Cli { get; }

    IPubSubService PubSub { get; }

    event EventHandler<CommandLogEntry>? CommandExecuted;

    void SelectDatabase(int database);

    Task<string> PingAsync(CancellationToken cancellationToken = default);

    Task<string> GetInfoAsync(string? section = "server", CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RedisNodeSection>> GetMasterInfoAsync(string section, CancellationToken cancellationToken = default);

    Task<long> GetDbSizeAsync(CancellationToken cancellationToken = default);

    Task<int> GetDatabaseCountAsync(CancellationToken cancellationToken = default);

    Task<bool> TryHeartbeatAsync(CancellationToken cancellationToken = default);
}

public interface IKeyBrowser
{
    Task<ScanPage> ScanAsync(ScanRequest request, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(RedisKeyBytes key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeyTreeNode>> LoadChildrenAsync(string prefix, string separator, int count, CancellationToken cancellationToken = default);
}

public interface ICliExecutor
{
    Task<CliResult> ExecuteAsync(string commandLine, CancellationToken cancellationToken = default);
}

public interface IValueService
{
    Task<KeyMeta> GetMetaAsync(RedisKeyBytes key, CancellationToken cancellationToken = default);

    Task<StringSnapshot> GetStringAsync(RedisKeyBytes key, long previewLimit, long maxFullLoad, CancellationToken cancellationToken = default);

    Task SaveStringAsync(RedisKeyBytes key, byte[] value, TimeSpan? ttl, CancellationToken cancellationToken = default);

    Task SetTtlAsync(RedisKeyBytes key, TimeSpan? ttl, CancellationToken cancellationToken = default);

    Task RenameAsync(RedisKeyBytes key, RedisKeyBytes newKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(RedisKeyBytes key, CancellationToken cancellationToken = default);

    Task<CollectionPage> ScanHashAsync(RedisKeyBytes key, long cursor, int count, string? match, CancellationToken cancellationToken = default);

    Task HashSetAsync(RedisKeyBytes key, string field, string value, TimeSpan? fieldTtl, CancellationToken cancellationToken = default);

    Task HashDeleteAsync(RedisKeyBytes key, string field, CancellationToken cancellationToken = default);

    Task<CollectionPage> RangeListAsync(RedisKeyBytes key, long start, long stop, CancellationToken cancellationToken = default);

    Task ListSetAsync(RedisKeyBytes key, long index, string value, CancellationToken cancellationToken = default);

    Task ListPushAsync(RedisKeyBytes key, string value, bool left, CancellationToken cancellationToken = default);

    Task ListRemoveAtAsync(RedisKeyBytes key, long index, CancellationToken cancellationToken = default);

    Task<CollectionPage> ScanSetAsync(RedisKeyBytes key, long cursor, int count, string? match, CancellationToken cancellationToken = default);

    Task SetAddAsync(RedisKeyBytes key, string member, CancellationToken cancellationToken = default);

    Task SetRemoveAsync(RedisKeyBytes key, string member, CancellationToken cancellationToken = default);

    Task<CollectionPage> ScanZSetAsync(RedisKeyBytes key, long cursor, int count, string? match, CancellationToken cancellationToken = default);

    Task ZAddAsync(RedisKeyBytes key, string member, double score, CancellationToken cancellationToken = default);

    Task ZRemAsync(RedisKeyBytes key, string member, CancellationToken cancellationToken = default);

    Task<StreamPage> RangeStreamAsync(RedisKeyBytes key, string startId, int count, CancellationToken cancellationToken = default);

    Task StreamAddAsync(RedisKeyBytes key, IReadOnlyList<StreamField> fields, CancellationToken cancellationToken = default);

    Task StreamDeleteAsync(RedisKeyBytes key, string id, CancellationToken cancellationToken = default);
}

public interface IPubSubService
{
    bool IsSubscribed { get; }

    string? CurrentChannel { get; }

    bool IsPattern { get; }

    event EventHandler<PubSubMessage>? MessageReceived;

    Task SubscribeAsync(string channel, bool pattern, CancellationToken cancellationToken = default);

    Task UnsubscribeAsync(CancellationToken cancellationToken = default);

    Task PublishAsync(string channel, string message, CancellationToken cancellationToken = default);
}
