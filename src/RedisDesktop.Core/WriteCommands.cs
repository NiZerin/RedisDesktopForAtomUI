namespace RedisDesktop.Core;

public static class WriteCommands
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "SET", "SETEX", "SETNX", "SETRANGE", "MSET", "MSETNX", "GETSET", "APPEND",
        "INCR", "INCRBY", "INCRBYFLOAT", "DECR", "DECRBY",
        "DEL", "UNLINK", "UNLINK", "FLUSHDB", "FLUSHALL",
        "EXPIRE", "EXPIREAT", "PEXPIRE", "PEXPIREAT", "PERSIST",
        "RENAME", "RENAMENX", "MOVE", "RESTORE",
        "HSET", "HSETNX", "HMSET", "HDEL", "HINCRBY", "HINCRBYFLOAT",
        "HEXPIRE", "HPEXPIRE", "HEXPIREAT", "HPEXPIREAT", "HPERSIST",
        "LPUSH", "LPUSHX", "RPUSH", "RPUSHX", "LPOP", "RPOP", "LSET", "LINSERT", "LTRIM", "LREM", "LMOVE",
        "SADD", "SREM", "SPOP", "SMOVE",
        "ZADD", "ZREM", "ZINCRBY", "ZREMRANGEBYRANK", "ZREMRANGEBYSCORE", "ZREMRANGEBYLEX",
        "XADD", "XDEL", "XTRIM", "XGROUP", "XACK",
        "PUBLISH", "SPUBLISH", "SELECT"
    };

    public static bool IsWrite(string command) => Commands.Contains(command);
}
