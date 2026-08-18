using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace RedisDesktop.Core;

public enum ValueViewKind
{
    Text,
    Json,
    Hex,
    Binary,
    Gzip,
    Deflate,
    DeflateRaw,
    Brotli,
    OverSize
}

public sealed record ValueViewResult(
    ValueViewKind Kind,
    string Text,
    bool CanEncode,
    string? Warning);

public static class ValueViewerPipeline
{
    public const int OverSizePreviewChars = 20_000;

    private static readonly UTF8Encoding Utf8 = new(false, false);
    private static readonly UTF8Encoding Utf8Strict = new(false, true);

    public static IReadOnlyList<ValueViewKind> ManualKinds { get; } =
    [
        ValueViewKind.Text,
        ValueViewKind.Json,
        ValueViewKind.Hex,
        ValueViewKind.Binary,
        ValueViewKind.Gzip,
        ValueViewKind.Deflate,
        ValueViewKind.DeflateRaw,
        ValueViewKind.Brotli
    ];

    public static string DisplayName(ValueViewKind kind) => kind switch
    {
        ValueViewKind.Json => "Json",
        ValueViewKind.Hex => "Hex",
        ValueViewKind.Binary => "Binary",
        ValueViewKind.Gzip => "Gzip",
        ValueViewKind.Deflate => "Deflate",
        ValueViewKind.DeflateRaw => "DeflateRaw",
        ValueViewKind.Brotli => "Brotli",
        ValueViewKind.OverSize => "OverSize",
        _ => "Text"
    };

    public static bool IsUtf8(byte[] data)
    {
        if (data.Length == 0)
        {
            return true;
        }

        try
        {
            _ = Utf8Strict.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    public static string FormatByteSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.##} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB";
    }

    public static ValueViewKind Detect(byte[] data)
    {
        if (data.Length == 0)
        {
            return ValueViewKind.Text;
        }

        if (LooksLikeJson(data))
        {
            return ValueViewKind.Json;
        }

        if (HasGzipMagic(data) && TryDecompress(data, DecompressGzip, out _))
        {
            return ValueViewKind.Gzip;
        }

        if (HasZlibMagic(data) && TryDecompress(data, DecompressZlib, out _))
        {
            return ValueViewKind.Deflate;
        }

        if (!IsUtf8(data) && TryDecompress(data, DecompressBrotli, out _))
        {
            return ValueViewKind.Brotli;
        }

        if (!IsUtf8(data) && TryDecompress(data, DecompressDeflateRaw, out _))
        {
            return ValueViewKind.DeflateRaw;
        }

        return IsUtf8(data) ? ValueViewKind.Text : ValueViewKind.Hex;
    }

    public static ValueViewResult Decode(byte[] data, ValueViewKind kind)
    {
        try
        {
            return kind switch
            {
                ValueViewKind.OverSize => DecodeOverSize(data),
                ValueViewKind.Hex => new ValueViewResult(kind, ToEscapedHex(data), true, null),
                ValueViewKind.Binary => new ValueViewResult(kind, ToBinaryBits(data), true, null),
                ValueViewKind.Json => DecodeJson(data),
                ValueViewKind.Gzip => DecodeCompressed(data, ValueViewKind.Gzip, DecompressGzip),
                ValueViewKind.Deflate => DecodeCompressed(data, ValueViewKind.Deflate, DecompressZlib),
                ValueViewKind.DeflateRaw => DecodeCompressed(data, ValueViewKind.DeflateRaw, DecompressDeflateRaw),
                ValueViewKind.Brotli => DecodeCompressed(data, ValueViewKind.Brotli, DecompressBrotli),
                _ => DecodeText(data)
            };
        }
        catch (Exception ex)
        {
            var fallback = DecodeText(data);
            return fallback with { Warning = $"Viewer {DisplayName(kind)} 失败，已回退 Text：{ex.Message}" };
        }
    }

    public static byte[] Encode(string text, ValueViewKind kind)
    {
        return kind switch
        {
            ValueViewKind.OverSize => throw new InvalidOperationException("值过大，禁止整包保存。"),
            ValueViewKind.Hex => FromEscapedHex(text),
            ValueViewKind.Binary => FromBinaryBits(text),
            ValueViewKind.Json => EncodeJson(text),
            ValueViewKind.Gzip => CompressGzip(Utf8.GetBytes(text)),
            ValueViewKind.Deflate => CompressZlib(Utf8.GetBytes(text)),
            ValueViewKind.DeflateRaw => CompressDeflateRaw(Utf8.GetBytes(text)),
            ValueViewKind.Brotli => CompressBrotli(Utf8.GetBytes(text)),
            _ => Utf8.GetBytes(text)
        };
    }

    public static string QuoteRedisArg(byte[] data)
    {
        var text = ToEscapedHex(data).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{text}\"";
    }

    public static string TypeLabel(RedisKeyType type) => type switch
    {
        RedisKeyType.String => "string",
        RedisKeyType.Hash => "hash",
        RedisKeyType.List => "list",
        RedisKeyType.Set => "set",
        RedisKeyType.SortedSet => "zset",
        RedisKeyType.Stream => "stream",
        RedisKeyType.ReJson => "ReJSON-RL",
        RedisKeyType.TimeSeries => "TSDB-TYPE",
        RedisKeyType.Vector => "vectorset",
        RedisKeyType.Module => "module",
        RedisKeyType.None => "none",
        _ => "unknown"
    };

    private static ValueViewResult DecodeOverSize(byte[] data)
    {
        var preview = IsUtf8(data) ? Utf8.GetString(data) : ToEscapedHex(data);
        if (preview.Length > OverSizePreviewChars)
        {
            preview = preview[..OverSizePreviewChars];
        }

        return new ValueViewResult(
            ValueViewKind.OverSize,
            $"{preview}\n\n...Show only the first {OverSizePreviewChars} characters, the rest has been hidden...",
            false,
            $"Size too large, show only the first {OverSizePreviewChars} characters and you cannot edit it.");
    }

    private static ValueViewResult DecodeText(byte[] data)
    {
        if (IsUtf8(data))
        {
            return new ValueViewResult(ValueViewKind.Text, Utf8.GetString(data), true, null);
        }

        return new ValueViewResult(ValueViewKind.Hex, ToEscapedHex(data), true, "非 UTF-8，已按 Hex 显示。");
    }

    private static ValueViewResult DecodeJson(byte[] data)
    {
        var text = Utf8.GetString(data);
        using var doc = JsonDocument.Parse(text);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            doc.WriteTo(writer);
        }

        return new ValueViewResult(ValueViewKind.Json, Utf8.GetString(stream.ToArray()), true, null);
    }

    private static byte[] EncodeJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        _ = doc.RootElement.ValueKind;
        return Utf8.GetBytes(text);
    }

    private static ValueViewResult DecodeCompressed(byte[] data, ValueViewKind kind, Func<byte[], byte[]> decompress)
    {
        var inner = decompress(data);
        if (LooksLikeJson(inner))
        {
            var json = DecodeJson(inner);
            return new ValueViewResult(kind, json.Text, true, $"已解压 {DisplayName(kind)}，内容为 JSON。");
        }

        if (IsUtf8(inner))
        {
            return new ValueViewResult(kind, Utf8.GetString(inner), true, $"已解压 {DisplayName(kind)}。");
        }

        return new ValueViewResult(
            kind,
            ToEscapedHex(inner),
            true,
            $"已解压 {DisplayName(kind)}，内层非 UTF-8，按 Hex 显示。保存将按当前文本重新压缩。");
    }

    private static byte[] DecompressGzip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressGzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] DecompressZlib(byte[] data)
    {
        if (data.Length < 2)
        {
            throw new InvalidDataException("Zlib 数据过短。");
        }

        using var input = new MemoryStream(data, 2, data.Length - 2);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9c);
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] DecompressDeflateRaw(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressDeflateRaw(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] DecompressBrotli(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressBrotli(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(data);
        }

        return output.ToArray();
    }

    internal static string ToEscapedHex(byte[] data)
    {
        var builder = new StringBuilder(data.Length * 2);
        foreach (var item in data)
        {
            if (item is >= 32 and <= 126)
            {
                builder.Append((char)item);
            }
            else
            {
                builder.Append("\\x");
                builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    internal static byte[] FromEscapedHex(string text)
    {
        using var output = new MemoryStream(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (i + 3 < text.Length
                && text[i] == '\\'
                && text[i + 1] == 'x'
                && Uri.IsHexDigit(text[i + 2])
                && Uri.IsHexDigit(text[i + 3]))
            {
                output.WriteByte(Convert.ToByte(text.Substring(i + 2, 2), 16));
                i += 4;
            }
            else
            {
                var encodedChar = Utf8.GetBytes(char.ToString(text[i]));
                output.Write(encodedChar, 0, encodedChar.Length);
                i++;
            }
        }

        return output.ToArray();
    }

    internal static string ToBinaryBits(byte[] data)
    {
        var builder = new StringBuilder(data.Length * 8);
        foreach (var item in data)
        {
            builder.Append(Convert.ToString(item, 2).PadLeft(8, '0'));
        }

        return builder.ToString();
    }

    internal static byte[] FromBinaryBits(string text)
    {
        var bits = new string(text.Where(ch => ch is '0' or '1').ToArray());
        if (bits.Length == 0)
        {
            return [];
        }

        if (bits.Length % 8 != 0)
        {
            throw new FormatException("Binary 长度必须是 8 的倍数。");
        }

        var result = new byte[bits.Length / 8];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Convert.ToByte(bits.Substring(i * 8, 8), 2);
        }

        return result;
    }

    private static bool LooksLikeJson(byte[] data)
    {
        if (!IsUtf8(data))
        {
            return false;
        }

        var text = Utf8.GetString(data).TrimStart();
        if (text.Length == 0 || (text[0] != '{' && text[0] != '['))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasGzipMagic(byte[] data) => data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b;

    private static bool HasZlibMagic(byte[] data)
        => data.Length >= 2 && data[0] == 0x78 && data[1] is 0x01 or 0x5e or 0x9c or 0xda;

    private static bool TryDecompress(byte[] data, Func<byte[], byte[]> decompress, out byte[] inner)
    {
        try
        {
            inner = decompress(data);
            return inner.Length > 0;
        }
        catch
        {
            inner = [];
            return false;
        }
    }
}
