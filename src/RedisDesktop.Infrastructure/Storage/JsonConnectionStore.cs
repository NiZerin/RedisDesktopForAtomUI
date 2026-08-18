using System.Text.Json;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public sealed class JsonConnectionStore : IConnectionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public JsonConnectionStore(string? path = null)
    {
        _path = path ?? AppPaths.ConnectionsFile;
    }

    public async Task<IReadOnlyList<ConnectionConfig>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        var items = await JsonSerializer.DeserializeAsync<List<ConnectionConfig>>(stream, Options, cancellationToken);
        return items ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<ConnectionConfig> connections, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, connections, Options, cancellationToken);
        }

        File.Copy(temp, _path, overwrite: true);
        File.Delete(temp);
    }
}
