using System.Text;

namespace RedisDesktop.Core;

public readonly struct RedisKeyBytes : IEquatable<RedisKeyBytes>
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public RedisKeyBytes(byte[] value)
    {
        Value = value ?? [];
    }

    public byte[] Value { get; }

    public bool IsEmpty => Value.Length == 0;

    public string ToDisplayString()
    {
        if (Value.Length == 0)
        {
            return string.Empty;
        }

        return TryGetUtf8(out var text) ? text : ToHex();
    }

    public bool TryGetUtf8(out string text)
    {
        try
        {
            text = Utf8Strict.GetString(Value);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = ToHex();
            return false;
        }
    }

    public string ToHex() => Convert.ToHexString(Value);

    public bool Equals(RedisKeyBytes other) => Value.AsSpan().SequenceEqual(other.Value);

    public override bool Equals(object? obj) => obj is RedisKeyBytes other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Value);
        return hash.ToHashCode();
    }

    public override string ToString() => ToDisplayString();

    public static RedisKeyBytes FromUtf8(string text) => new(Utf8Strict.GetBytes(text ?? string.Empty));
}
