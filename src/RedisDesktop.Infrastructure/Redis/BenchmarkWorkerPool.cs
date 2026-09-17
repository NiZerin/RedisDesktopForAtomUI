using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

internal sealed class BenchmarkWorkerPool : IAsyncDisposable
{
    private readonly IReadOnlyList<ConnectionMultiplexer> _muxes;
    private readonly SshTunnel? _tunnel;
    private bool _disposed;

    public BenchmarkWorkerPool(
        IReadOnlyList<ConnectionMultiplexer> muxes,
        SshTunnel? tunnel,
        int database)
    {
        if (muxes.Count == 0)
        {
            throw new ArgumentException("压测连接池不能为空。", nameof(muxes));
        }

        _muxes = muxes;
        _tunnel = tunnel;
        Primary = muxes[0];
        Databases = muxes.Select(mux => mux.GetDatabase(database)).ToArray();
    }

    public ConnectionMultiplexer Primary { get; }

    public IReadOnlyList<IDatabase> Databases { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var mux in _muxes)
        {
            try
            {
                await mux.CloseAsync().ConfigureAwait(false);
                mux.Dispose();
            }
            catch
            {
                // best-effort
            }
        }

        if (_tunnel is not null)
        {
            await _tunnel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
