namespace RedisDesktop.Core;

public sealed record ScanRequest(
    int Database,
    string Match,
    long Cursor,
    int Count);

public sealed record ScanPage(
    IReadOnlyList<RedisKeyBytes> Keys,
    long Cursor,
    bool Exhausted);

public sealed record KeyMeta(
    RedisKeyBytes Key,
    RedisKeyType Type,
    TimeSpan? TimeToLive,
    long? MemoryBytes);

public sealed record StringSnapshot(
    RedisKeyBytes Key,
    byte[] Value,
    bool IsUtf8,
    bool IsTruncated,
    long TotalLength);

public sealed record CommandLogEntry(
    DateTimeOffset Timestamp,
    string ConnectionName,
    string Command,
    string? Details,
    double ElapsedMilliseconds,
    bool Success,
    string? Error);

public sealed class KeyTreeNode
{
    public required KeyTreeNodeKind Kind { get; init; }

    public required string Name { get; init; }

    public required string Path { get; init; }

    public RedisKeyBytes? FullKey { get; init; }

    public bool HasChildren { get; init; }

    public int ChildCount { get; init; }

    public IReadOnlyList<KeyTreeNode> Children { get; init; } = [];
}

public sealed record RedisNodeSection(string Node, string Content);

public sealed record RedisInfoKeyspaceRow(
    string Database,
    long Keys,
    long Expires,
    long AvgTtl,
    string? Node);
