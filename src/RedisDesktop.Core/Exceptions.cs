namespace RedisDesktop.Core;

public class RedisDesktopException : Exception
{
    public RedisDesktopException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class ConnectException : RedisDesktopException
{
    public ConnectException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class AuthException : RedisDesktopException
{
    public AuthException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class ReadOnlyException : RedisDesktopException
{
    public ReadOnlyException(string command)
        : base($"当前连接为只读模式，无法执行写命令 {command}。")
    {
        Command = command;
    }

    public string Command { get; }
}

public sealed class KeyTooLargeException : RedisDesktopException
{
    public KeyTooLargeException(long length, long limit)
        : base($"值过大（{length} 字节），超过 {limit} 字节限制，已禁止整包加载。")
    {
        Length = length;
        Limit = limit;
    }

    public long Length { get; }

    public long Limit { get; }
}

public sealed class RedisCommandException : RedisDesktopException
{
    public RedisCommandException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
