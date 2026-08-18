namespace RedisDesktop.Core;

public static class KeyTreeBuilder
{
    public static IReadOnlyList<KeyTreeNode> BuildChildren(
        IEnumerable<RedisKeyBytes> keys,
        string prefix,
        string separator)
    {
        ArgumentNullException.ThrowIfNull(keys);
        separator ??= ":";
        prefix ??= string.Empty;

        var folders = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyNodes = new Dictionary<string, RedisKeyBytes>(StringComparer.Ordinal);
        var hasExactKey = false;
        RedisKeyBytes? exactKey = null;

        foreach (var key in keys)
        {
            if (!key.TryGetUtf8(out var text))
            {
                if (string.IsNullOrEmpty(prefix))
                {
                    keyNodes[key.ToHex()] = key;
                }

                continue;
            }

            if (!string.IsNullOrEmpty(prefix))
            {
                if (text == prefix)
                {
                    hasExactKey = true;
                    exactKey = key;
                    continue;
                }

                var expected = prefix + separator;
                if (!text.StartsWith(expected, StringComparison.Ordinal))
                {
                    continue;
                }

                text = text[expected.Length..];
            }

            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
            {
                var path = string.IsNullOrEmpty(prefix) ? text : prefix + separator + text;
                keyNodes[path] = key;
            }
            else
            {
                var segment = text[..index];
                var folderPath = string.IsNullOrEmpty(prefix) ? segment : prefix + separator + segment;
                folders[folderPath] = folders.TryGetValue(folderPath, out var n) ? n + 1 : 1;
            }
        }

        var nodes = new List<KeyTreeNode>(folders.Count + keyNodes.Count + (hasExactKey ? 1 : 0));

        foreach (var folder in folders.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var path = folder.Key;
            var name = path.Contains(separator, StringComparison.Ordinal)
                ? path[(path.LastIndexOf(separator, StringComparison.Ordinal) + separator.Length)..]
                : path;
            nodes.Add(new KeyTreeNode
            {
                Kind = KeyTreeNodeKind.Folder,
                Name = name,
                Path = path,
                HasChildren = true,
                ChildCount = folder.Value
            });
        }

        if (hasExactKey && exactKey is { } exact)
        {
            var name = string.IsNullOrEmpty(prefix)
                ? exact.ToDisplayString()
                : prefix.Contains(separator, StringComparison.Ordinal)
                    ? prefix[(prefix.LastIndexOf(separator, StringComparison.Ordinal) + separator.Length)..]
                    : prefix;
            nodes.Add(new KeyTreeNode
            {
                Kind = KeyTreeNodeKind.Key,
                Name = name,
                Path = prefix,
                FullKey = exact,
                HasChildren = false
            });
        }

        foreach (var pair in keyNodes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var name = pair.Key.Contains(separator, StringComparison.Ordinal)
                ? pair.Key[(pair.Key.LastIndexOf(separator, StringComparison.Ordinal) + separator.Length)..]
                : pair.Key;
            nodes.Add(new KeyTreeNode
            {
                Kind = KeyTreeNodeKind.Key,
                Name = name,
                Path = pair.Key,
                FullKey = pair.Value,
                HasChildren = false
            });
        }

        return nodes;
    }

    /// <summary>
    /// Builds a nested tree from already scanned keys, matching ARDM <c>keysToTree</c>.
    /// Folder labels are path segments; key labels are the full key name. Folders sort before keys.
    /// </summary>
    public static IReadOnlyList<KeyTreeNode> BuildTree(
        IEnumerable<RedisKeyBytes> keys,
        string separator,
        int forceCut = 200_000)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (string.IsNullOrEmpty(separator))
        {
            return keys.Select(key => new KeyTreeNode
            {
                Kind = KeyTreeNodeKind.Key,
                Name = key.ToDisplayString(),
                Path = key.ToDisplayString(),
                FullKey = key
            }).ToList();
        }

        var root = new Accumulator();
        var added = 0;
        foreach (var key in keys)
        {
            if (added >= forceCut)
            {
                break;
            }

            Insert(root, key, separator);
            added++;
        }

        return Format(root, previousKey: string.Empty, separator);
    }

    private static void Insert(Accumulator root, RedisKeyBytes key, string separator)
    {
        if (!key.TryGetUtf8(out var text))
        {
            root.Keys[key.ToHex()] = key;
            return;
        }

        var parts = text.Split(new[] { separator }, StringSplitOptions.None);
        var current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var segment = parts[i];
            if (!current.Folders.TryGetValue(segment, out var next))
            {
                next = new Accumulator();
                current.Folders[segment] = next;
            }

            current = next;
        }

        current.Keys[text] = key;
    }

    private static IReadOnlyList<KeyTreeNode> Format(Accumulator node, string previousKey, string separator)
    {
        var result = new List<KeyTreeNode>(node.Folders.Count + node.Keys.Count);
        foreach (var folder in node.Folders.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var name = string.IsNullOrEmpty(folder.Key) ? "[Empty]" : folder.Key;
            var fullName = previousKey + folder.Key + separator;
            var children = Format(folder.Value, fullName, separator);
            var keyCount = children.Sum(child => child.Kind == KeyTreeNodeKind.Folder ? child.ChildCount : 1);
            result.Add(new KeyTreeNode
            {
                Kind = KeyTreeNodeKind.Folder,
                Name = name,
                Path = fullName,
                HasChildren = children.Count > 0,
                ChildCount = keyCount,
                Children = children
            });
        }

        foreach (var pair in node.Keys.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            result.Add(new KeyTreeNode
            {
                Kind = KeyTreeNodeKind.Key,
                Name = pair.Key,
                Path = pair.Key,
                FullKey = pair.Value
            });
        }

        return result;
    }

    private sealed class Accumulator
    {
        public Dictionary<string, Accumulator> Folders { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, RedisKeyBytes> Keys { get; } = new(StringComparer.Ordinal);
    }
}
