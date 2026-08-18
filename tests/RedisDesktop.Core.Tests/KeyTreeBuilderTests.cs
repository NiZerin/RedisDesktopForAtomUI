using System.Text;
using RedisDesktop.Core;

namespace RedisDesktop.Core.Tests;

public class KeyTreeBuilderTests
{
    [Fact]
    public void Builds_folders_and_keys_at_root()
    {
        var keys = new[]
        {
            RedisKeyBytes.FromUtf8("user:1001:profile"),
            RedisKeyBytes.FromUtf8("user:1002"),
            RedisKeyBytes.FromUtf8("config")
        };

        var nodes = KeyTreeBuilder.BuildChildren(keys, "", ":");

        Assert.Contains(nodes, n => n.Kind == KeyTreeNodeKind.Folder && n.Name == "user" && n.Path == "user" && n.ChildCount == 2);
        Assert.Contains(nodes, n => n.Kind == KeyTreeNodeKind.Key && n.Name == "config");
        Assert.DoesNotContain(nodes, n => n.Name == "1001");
    }

    [Fact]
    public void Same_path_can_be_both_folder_and_key()
    {
        var keys = new[]
        {
            RedisKeyBytes.FromUtf8("user:1001"),
            RedisKeyBytes.FromUtf8("user:1001:profile")
        };

        var nodes = KeyTreeBuilder.BuildChildren(keys, "user", ":");

        Assert.Contains(nodes, n => n.Kind == KeyTreeNodeKind.Key && n.Name == "1001" && n.Path == "user:1001");
        Assert.Contains(nodes, n => n.Kind == KeyTreeNodeKind.Folder && n.Name == "1001" && n.Path == "user:1001");
    }

    [Fact]
    public void Custom_separator_is_respected()
    {
        var keys = new[]
        {
            RedisKeyBytes.FromUtf8("cache::users::1"),
            RedisKeyBytes.FromUtf8("cache::settings")
        };

        var nodes = KeyTreeBuilder.BuildChildren(keys, "cache", "::");

        Assert.Contains(nodes, n => n.Kind == KeyTreeNodeKind.Folder && n.Name == "users");
        Assert.Contains(nodes, n => n.Kind == KeyTreeNodeKind.Key && n.Name == "settings");
    }

    [Fact]
    public void Binary_keys_at_root_use_hex_name()
    {
        var key = new RedisKeyBytes([0xff, 0x00, 0xfe]);
        var nodes = KeyTreeBuilder.BuildChildren([key], "", ":");

        var node = Assert.Single(nodes);
        Assert.Equal(KeyTreeNodeKind.Key, node.Kind);
        Assert.Equal("FF00FE", node.Name);
    }

    [Fact]
    public void BuildTree_nests_folders_and_uses_full_key_labels()
    {
        var keys = new[]
        {
            RedisKeyBytes.FromUtf8("user:1001:profile"),
            RedisKeyBytes.FromUtf8("user:1002"),
            RedisKeyBytes.FromUtf8("config")
        };

        var nodes = KeyTreeBuilder.BuildTree(keys, ":");

        Assert.Equal(KeyTreeNodeKind.Folder, nodes[0].Kind);
        Assert.Equal("user", nodes[0].Name);
        Assert.Equal("user:", nodes[0].Path);
        Assert.Equal(2, nodes[0].ChildCount);
        Assert.Contains(nodes[0].Children, n => n.Kind == KeyTreeNodeKind.Folder && n.Name == "1001");
        Assert.Contains(nodes[0].Children, n => n.Kind == KeyTreeNodeKind.Key && n.Name == "user:1002");
        var nested = nodes[0].Children.Single(n => n.Kind == KeyTreeNodeKind.Folder);
        Assert.Contains(nested.Children, n => n.Name == "user:1001:profile" && n.FullKey is not null);
        Assert.Equal(KeyTreeNodeKind.Key, nodes[1].Kind);
        Assert.Equal("config", nodes[1].Name);
    }

    [Fact]
    public void BuildTree_keeps_same_path_as_folder_and_key()
    {
        var keys = new[]
        {
            RedisKeyBytes.FromUtf8("user:1001"),
            RedisKeyBytes.FromUtf8("user:1001:profile")
        };

        var nodes = KeyTreeBuilder.BuildTree(keys, ":");
        var user = Assert.Single(nodes);
        Assert.Contains(user.Children, n => n.Kind == KeyTreeNodeKind.Key && n.Name == "user:1001");
        Assert.Contains(user.Children, n => n.Kind == KeyTreeNodeKind.Folder && n.Name == "1001" && n.Path == "user:1001:");
    }

    [Fact]
    public void BuildTree_empty_separator_is_flat()
    {
        var keys = new[]
        {
            RedisKeyBytes.FromUtf8("user:1"),
            RedisKeyBytes.FromUtf8("user:2")
        };

        var nodes = KeyTreeBuilder.BuildTree(keys, "");
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.Equal(KeyTreeNodeKind.Key, n.Kind));
    }
}

public class WriteCommandsTests
{
    [Theory]
    [InlineData("SET", true)]
    [InlineData("GET", false)]
    [InlineData("del", true)]
    [InlineData("SCAN", false)]
    [InlineData("HEXPIRE", true)]
    [InlineData("HGET", false)]
    public void Classifies_write_commands(string command, bool expected)
    {
        Assert.Equal(expected, WriteCommands.IsWrite(command));
    }
}

public class CliCommandParserTests
{
    [Fact]
    public void Parses_quoted_arguments()
    {
        var args = CliCommandParser.Parse("SET mykey \"hello world\"");
        Assert.Equal(["SET", "mykey", "hello world"], args);
    }

    [Fact]
    public void Truncates_long_output()
    {
        var text = new string('a', CliCommandParser.MaxOutputBytes + 10);
        var truncated = CliCommandParser.Truncate(text);
        Assert.Contains("已截断", truncated);
        Assert.StartsWith(new string('a', 32), truncated);
    }
}

public class CollectionPageTests
{
    [Fact]
    public void Holds_paging_contract()
    {
        var page = new CollectionPage(
            [new CollectionMember("f", "v")],
            Cursor: 42,
            Exhausted: false,
            LoadedCount: 1,
            TotalCount: 10,
            SupportsFieldTtl: true);

        Assert.Single(page.Items);
        Assert.Equal(42, page.Cursor);
        Assert.Equal(10, page.TotalCount);
        Assert.True(page.SupportsFieldTtl);
    }
}

public class ClusterScanCursorTests
{
    [Fact]
    public void Roundtrips_server_index_and_cursor()
    {
        var encoded = ClusterScanCursor.Encode(3, 99);
        ClusterScanCursor.Decode(encoded, out var index, out var cursor);
        Assert.Equal(3, index);
        Assert.Equal(99, cursor);
    }
}

public class ValueViewerPipelineTests
{
    [Fact]
    public void Detects_json_and_roundtrips()
    {
        var bytes = """{"a":1}"""u8.ToArray();
        Assert.Equal(ValueViewKind.Json, ValueViewerPipeline.Detect(bytes));
        var decoded = ValueViewerPipeline.Decode(bytes, ValueViewKind.Json);
        Assert.Contains("\"a\"", decoded.Text);
        var encoded = ValueViewerPipeline.Encode(decoded.Text, ValueViewKind.Json);
        Assert.Equal(ValueViewKind.Json, ValueViewerPipeline.Detect(encoded));
    }

    [Fact]
    public void Json_pretty_print_keeps_large_integer()
    {
        var bytes = """{"id":9223372036854775807}"""u8.ToArray();
        var decoded = ValueViewerPipeline.Decode(bytes, ValueViewKind.Json);
        Assert.Contains("9223372036854775807", decoded.Text);
        var encoded = ValueViewerPipeline.Encode(decoded.Text, ValueViewKind.Json);
        Assert.Contains("9223372036854775807", Encoding.UTF8.GetString(encoded));
    }

    [Fact]
    public void Gzip_roundtrip_and_fallback_on_bad_payload()
    {
        var gz = ValueViewerPipeline.Encode("hello-gzip", ValueViewKind.Gzip);
        Assert.Equal(ValueViewKind.Gzip, ValueViewerPipeline.Detect(gz));
        var decoded = ValueViewerPipeline.Decode(gz, ValueViewKind.Gzip);
        Assert.Equal("hello-gzip", decoded.Text);

        var fallback = ValueViewerPipeline.Decode("not-gzip"u8.ToArray(), ValueViewKind.Gzip);
        Assert.False(string.IsNullOrEmpty(fallback.Warning));
    }

    [Fact]
    public void Deflate_raw_and_brotli_roundtrip()
    {
        var raw = ValueViewerPipeline.Encode("hello-raw", ValueViewKind.DeflateRaw);
        var decodedRaw = ValueViewerPipeline.Decode(raw, ValueViewKind.DeflateRaw);
        Assert.Equal("hello-raw", decodedRaw.Text);

        var brotli = ValueViewerPipeline.Encode("hello-brotli", ValueViewKind.Brotli);
        var decodedBrotli = ValueViewerPipeline.Decode(brotli, ValueViewKind.Brotli);
        Assert.Equal("hello-brotli", decodedBrotli.Text);
        Assert.Equal(ValueViewKind.Brotli, ValueViewerPipeline.Detect(brotli));
    }

    [Fact]
    public void Hex_uses_escaped_non_printable_and_roundtrips()
    {
        var bytes = new byte[] { 0x00, (byte)'A', 0xff, (byte)'B' };
        var decoded = ValueViewerPipeline.Decode(bytes, ValueViewKind.Hex);
        Assert.Equal("\\x00A\\xffB", decoded.Text);
        Assert.Equal(bytes, ValueViewerPipeline.Encode(decoded.Text, ValueViewKind.Hex));
    }

    [Fact]
    public void Binary_roundtrip()
    {
        var bytes = new byte[] { 0b1010_0001, 0b0000_1111 };
        var decoded = ValueViewerPipeline.Decode(bytes, ValueViewKind.Binary);
        Assert.Equal("1010000100001111", decoded.Text);
        Assert.Equal(bytes, ValueViewerPipeline.Encode("10100001 00001111", ValueViewKind.Binary));
    }

    [Fact]
    public void Non_utf8_detects_as_hex()
    {
        var bytes = new byte[] { 0xff, 0xfe, 0x00, 0x01 };
        Assert.Equal(ValueViewKind.Hex, ValueViewerPipeline.Detect(bytes));
        var decoded = ValueViewerPipeline.Decode(bytes, ValueViewKind.Text);
        Assert.False(string.IsNullOrEmpty(decoded.Warning));
        Assert.Equal(ValueViewKind.Hex, decoded.Kind);
    }

    [Fact]
    public void Oversize_is_read_only()
    {
        var decoded = ValueViewerPipeline.Decode("hello-oversize"u8.ToArray(), ValueViewKind.OverSize);
        Assert.False(decoded.CanEncode);
        Assert.Contains("Show only the first", decoded.Text);
        Assert.Throws<InvalidOperationException>(() => ValueViewerPipeline.Encode(decoded.Text, ValueViewKind.OverSize));
    }

    [Fact]
    public void Type_label_matches_ardm()
    {
        Assert.Equal("string", ValueViewerPipeline.TypeLabel(RedisKeyType.String));
        Assert.Equal("zset", ValueViewerPipeline.TypeLabel(RedisKeyType.SortedSet));
        Assert.Equal("ReJSON-RL", ValueViewerPipeline.TypeLabel(RedisKeyType.ReJson));
    }
}

public class RedisKeyBytesTests
{
    [Fact]
    public void Utf8_roundtrip()
    {
        var key = RedisKeyBytes.FromUtf8("用户:1");
        Assert.True(key.TryGetUtf8(out var text));
        Assert.Equal("用户:1", text);
    }

    [Fact]
    public void Invalid_utf8_falls_back_to_hex()
    {
        var key = new RedisKeyBytes([0xff, 0xfe]);
        Assert.False(key.TryGetUtf8(out _));
        Assert.Equal("FFFE", key.ToDisplayString());
    }
}
