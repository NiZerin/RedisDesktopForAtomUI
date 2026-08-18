namespace RedisDesktop.Core;

public sealed record CollectionMember(
    string Name,
    string Value,
    double? Score = null,
    long? Index = null,
    TimeSpan? FieldTtl = null,
    bool IsNew = false);

public sealed record CollectionPage(
    IReadOnlyList<CollectionMember> Items,
    long Cursor,
    bool Exhausted,
    int LoadedCount,
    long? TotalCount,
    bool SupportsFieldTtl = false);

public sealed record StreamField(string Name, string Value);

public sealed record StreamEntry(string Id, IReadOnlyList<StreamField> Fields);

public sealed record StreamPage(
    IReadOnlyList<StreamEntry> Items,
    string NextStartId,
    bool Exhausted,
    int LoadedCount,
    long? TotalCount);

public sealed record PubSubMessage(
    DateTimeOffset Timestamp,
    string Channel,
    string Payload);

public sealed record CliResult(
    string Output,
    bool Truncated,
    bool Success,
    string? Error,
    string? FullOutput = null);

public sealed class ConnectionSecrets
{
    public string? RedisPassword { get; init; }

    public string? SentinelPassword { get; init; }

    public string? SshPassword { get; init; }

    public string? SshPassphrase { get; init; }

    public static ConnectionSecrets From(ConnectionConfig config, ISecretProtector protector)
    {
        return new ConnectionSecrets
        {
            RedisPassword = Unprotect(protector, config.PasswordProtected),
            SentinelPassword = Unprotect(protector, config.Sentinel.SentinelPasswordProtected),
            SshPassword = Unprotect(protector, config.Ssh.PasswordProtected),
            SshPassphrase = Unprotect(protector, config.Ssh.PassphraseProtected)
        };
    }

    private static string? Unprotect(ISecretProtector protector, string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(protectedText);
        }
        catch
        {
            return null;
        }
    }
}

public static class CliCommandParser
{
    public const int MaxOutputBytes = 256 * 1024;

    public static IReadOnlyList<string> Parse(string commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return args;
        }

        var current = new System.Text.StringBuilder();
        var inSingle = false;
        var inDouble = false;
        foreach (var ch in commandLine.Trim())
        {
            if (ch == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inSingle && !inDouble)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    public static string Truncate(string output)
    {
        if (string.IsNullOrEmpty(output) || output.Length <= MaxOutputBytes)
        {
            return output;
        }

        return output[..MaxOutputBytes] + $"\n...[已截断，原文 {output.Length} 字符]";
    }
}

public static class ClusterScanCursor
{
    public static long Encode(int serverIndex, long redisCursor)
        => ((long)serverIndex << 48) | (redisCursor & 0xFFFFFFFFFFFF);

    public static void Decode(long cursor, out int serverIndex, out long redisCursor)
    {
        serverIndex = (int)((ulong)cursor >> 48);
        redisCursor = cursor & 0xFFFFFFFFFFFF;
    }
}
