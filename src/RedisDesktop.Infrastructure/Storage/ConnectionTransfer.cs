using System.Text.Json;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public static class ConnectionTransfer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToJson(IReadOnlyList<ConnectionConfig> connections)
        => JsonSerializer.Serialize(connections, Options);

    public static IReadOnlyList<ConnectionConfig> FromJson(string json)
    {
        var items = JsonSerializer.Deserialize<List<ConnectionConfig>>(json, Options) ?? [];
        foreach (var item in items)
        {
            item.Id = Guid.NewGuid();
        }

        return items;
    }
}
