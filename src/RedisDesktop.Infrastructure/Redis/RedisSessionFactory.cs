using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using RedisDesktop.Core;
using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

public sealed class RedisSessionFactory : IRedisSessionFactory
{
    private readonly ICommandLog _commandLog;

    public RedisSessionFactory(ICommandLog commandLog)
    {
        _commandLog = commandLog;
    }

    public Task<IRedisSession> ConnectAsync(ConnectionConfig config, string? password, CancellationToken cancellationToken = default)
        => ConnectAsync(config, new ConnectionSecrets { RedisPassword = password }, cancellationToken);

    public async Task<IRedisSession> ConnectAsync(ConnectionConfig config, ConnectionSecrets secrets, CancellationToken cancellationToken = default)
    {
        var (multiplexer, tunnel) = await ConnectMultiplexerAsync(config, secrets, cancellationToken).ConfigureAwait(false);
        return new RedisSession(config, multiplexer, _commandLog, tunnel);
    }

    public Task TestAsync(ConnectionConfig config, string? password, CancellationToken cancellationToken = default)
        => TestAsync(config, new ConnectionSecrets { RedisPassword = password }, cancellationToken);

    public async Task TestAsync(ConnectionConfig config, ConnectionSecrets secrets, CancellationToken cancellationToken = default)
    {
        var (multiplexer, tunnel) = await ConnectMultiplexerAsync(config, secrets, cancellationToken).ConfigureAwait(false);
        try
        {
            var db = multiplexer.GetDatabase(config.Kind == RedisDeploymentKind.Cluster ? 0 : config.Database);
            var pong = await db.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (pong < TimeSpan.Zero)
            {
                throw new ConnectException("测试连接失败：未收到 PONG。");
            }
        }
        finally
        {
            await multiplexer.CloseAsync().ConfigureAwait(false);
            multiplexer.Dispose();
            if (tunnel is not null)
            {
                await tunnel.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    internal async Task<BenchmarkWorkerPool> OpenWorkerPoolAsync(
        ConnectionConfig config,
        ConnectionSecrets secrets,
        int workers,
        CancellationToken cancellationToken = default)
    {
        workers = Math.Clamp(workers, 1, BenchmarkLimits.MaxConcurrency);
        var muxes = new List<ConnectionMultiplexer>(workers);
        SshTunnel? tunnel = null;
        try
        {
            var (first, openedTunnel) = await ConnectMultiplexerAsync(config, secrets, cancellationToken)
                .ConfigureAwait(false);
            muxes.Add(first);
            tunnel = openedTunnel;
            for (var i = 1; i < workers; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                muxes.Add(await ConnectAdditionalAsync(config, secrets, tunnel, cancellationToken).ConfigureAwait(false));
            }

            var database = config.Kind == RedisDeploymentKind.Cluster ? 0 : Math.Max(0, config.Database);
            return new BenchmarkWorkerPool(muxes, tunnel, database);
        }
        catch
        {
            foreach (var mux in muxes)
            {
                try
                {
                    await mux.CloseAsync().ConfigureAwait(false);
                    mux.Dispose();
                }
                catch
                {
                    // best-effort cleanup
                }
            }

            if (tunnel is not null)
            {
                await tunnel.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectAdditionalAsync(
        ConnectionConfig config,
        ConnectionSecrets secrets,
        SshTunnel? tunnel,
        CancellationToken cancellationToken)
    {
        if (config.Kind == RedisDeploymentKind.Cluster && tunnel is not null)
        {
            var clusterOptions = CreateBaseOptions(config, secrets);
            clusterOptions.EndPoints.Clear();
            foreach (var local in tunnel.AdvertisedToLocal.Values)
            {
                clusterOptions.EndPoints.Add(local.Host, local.Port);
            }

            AttachSshClusterEndpointMap(clusterOptions, tunnel);
            return await ConnectCoreAsync(clusterOptions, config, secrets, cancellationToken).ConfigureAwait(false);
        }

        var options = CreateOptions(config, secrets, tunnel);
        return await ConnectCoreAsync(options, config, secrets, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(ConnectionMultiplexer Mux, SshTunnel? Tunnel)> ConnectMultiplexerAsync(
        ConnectionConfig config,
        ConnectionSecrets secrets,
        CancellationToken cancellationToken)
    {
        SshTunnel? tunnel = null;
        try
        {
            if (config.Ssh.Enabled)
            {
                tunnel = SshTunnel.Open(config.Ssh, secrets, config.Host, config.Port);
            }

            if (config.Kind == RedisDeploymentKind.Cluster && tunnel is not null)
            {
                return await ConnectSshClusterAsync(config, secrets, tunnel, cancellationToken).ConfigureAwait(false);
            }

            var options = CreateOptions(config, secrets, tunnel);
            var mux = await ConnectCoreAsync(options, config, secrets, cancellationToken).ConfigureAwait(false);
            return (mux, tunnel);
        }
        catch
        {
            if (tunnel is not null)
            {
                await tunnel.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static async Task<(ConnectionMultiplexer Mux, SshTunnel Tunnel)> ConnectSshClusterAsync(
        ConnectionConfig config,
        ConnectionSecrets secrets,
        SshTunnel tunnel,
        CancellationToken cancellationToken)
    {
        string nodesText;
        try
        {
            var seedOptions = CreateBaseOptions(config, secrets);
            seedOptions.AbortOnConnectFail = false;
            seedOptions.EndPoints.Add(tunnel.LocalHost, tunnel.LocalPort);
            var seed = await ConnectCoreAsync(seedOptions, config, secrets, cancellationToken).ConfigureAwait(false);
            try
            {
                var raw = await seed.GetDatabase().ExecuteAsync("CLUSTER", "NODES").WaitAsync(cancellationToken).ConfigureAwait(false);
                nodesText = raw.ToString() ?? string.Empty;
            }
            finally
            {
                await seed.CloseAsync().ConfigureAwait(false);
                seed.Dispose();
            }
        }
        catch (Exception ex) when (ex is not ConnectException)
        {
            throw new ConnectException("SSH 隧道已建立，但无法从种子节点读取 CLUSTER NODES。", ex);
        }

        var masters = ClusterNodesParser.ParseMasters(nodesText);
        if (masters.Count == 0)
        {
            throw new ConnectException("CLUSTER NODES 未返回可用 master。Host 请填写节点互相通告的内网 IP。");
        }

        try
        {
            foreach (var master in masters)
            {
                tunnel.Forward(master.Host, master.Port);
            }
        }
        catch (Exception ex)
        {
            throw new ConnectException("SSH 隧道已建立，但为 Cluster master 建立转发失败。", ex);
        }

        var options = CreateBaseOptions(config, secrets);
        options.EndPoints.Clear();
        foreach (var local in tunnel.AdvertisedToLocal.Values)
        {
            options.EndPoints.Add(local.Host, local.Port);
        }

        AttachSshClusterEndpointMap(options, tunnel);

        try
        {
            var mux = await ConnectCoreAsync(options, config, secrets, cancellationToken).ConfigureAwait(false);
            return (mux, tunnel);
        }
        catch (Exception ex) when (ex is not ConnectException)
        {
            throw new ConnectException("SSH 与 CLUSTER NODES 已成功，但多节点 Cluster 连接失败。请确认每个 master 都能经 SSH 转发。", ex);
        }
    }

    private static void AttachSshClusterEndpointMap(ConfigurationOptions options, SshTunnel tunnel)
    {
        options.BeforeSocketConnect = (endpoint, _, _) =>
        {
            _ = tunnel.MapEndpoint(endpoint);
        };

        options.AbortOnConnectFail = false;
    }

    private static ConfigurationOptions CreateOptions(ConnectionConfig config, ConnectionSecrets secrets, SshTunnel? tunnel)
    {
        var options = CreateBaseOptions(config, secrets);
        if (tunnel is not null)
        {
            options.EndPoints.Add(tunnel.LocalHost, tunnel.LocalPort);
        }
        else if (config.Kind == RedisDeploymentKind.Sentinel)
        {
            ApplySentinelEndpoints(options, config);
        }
        else
        {
            options.EndPoints.Add(config.Host, config.Port);
        }

        return options;
    }

    private static ConfigurationOptions CreateBaseOptions(ConnectionConfig config, ConnectionSecrets secrets)
    {
        var database = config.Kind == RedisDeploymentKind.Cluster ? 0 : Math.Max(0, config.Database);
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = true,
            ConnectTimeout = 8000,
            AsyncTimeout = 15000,
            SyncTimeout = 15000,
            AllowAdmin = true,
            DefaultDatabase = database,
            User = string.IsNullOrWhiteSpace(config.Username) ? null : config.Username,
            Password = string.IsNullOrEmpty(secrets.RedisPassword) ? null : secrets.RedisPassword,
            ClientName = string.IsNullOrWhiteSpace(config.Name) ? "RedisDesktop" : config.Name
        };

        if (config.Kind == RedisDeploymentKind.Sentinel)
        {
            if (string.IsNullOrWhiteSpace(config.Sentinel.MasterName))
            {
                throw new ConnectException("Sentinel 模式需要填写 MasterName。");
            }

            options.ServiceName = config.Sentinel.MasterName.Trim();
            if (!string.IsNullOrEmpty(secrets.SentinelPassword)
                && !string.Equals(secrets.SentinelPassword, secrets.RedisPassword, StringComparison.Ordinal))
            {
                // 2.8.31 无独立 SentinelPassword：Sentinel AUTH 与节点密码不同时，先解析 master 再直连。
            }
        }

        ApplyTls(options, config);
        return options;
    }

    private static void ApplySentinelEndpoints(ConfigurationOptions options, ConnectionConfig config)
    {
        options.EndPoints.Add(config.Host, config.Port);
    }

    private static void ApplyTls(ConfigurationOptions options, ConnectionConfig config)
    {
        if (!config.Tls.Enabled)
        {
            return;
        }

        options.Ssl = true;
        options.SslHost = string.IsNullOrWhiteSpace(config.Tls.ServerName) ? config.Host : config.Tls.ServerName;

        if (config.Tls.SkipCertValidation)
        {
            options.CertificateValidation += (_, _, _, _) => true;
        }
        else if (!string.IsNullOrWhiteSpace(config.Tls.CaPath))
        {
            options.TrustIssuer(config.Tls.CaPath);
        }

        var clientCert = LoadClientCertificate(config.Tls);
        if (clientCert is not null)
        {
            options.CertificateSelection += (_, _, _, _, _) => clientCert;
        }
    }

    private static X509Certificate2? LoadClientCertificate(TlsOptions tls)
    {
        if (string.IsNullOrWhiteSpace(tls.CertPath))
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(tls.KeyPath))
            {
                return X509Certificate2.CreateFromPemFile(tls.CertPath, tls.KeyPath);
            }

            return X509Certificate2.CreateFromPemFile(tls.CertPath);
        }
        catch (Exception ex)
        {
            try
            {
                return new X509Certificate2(tls.CertPath);
            }
            catch
            {
                throw new ConnectException($"无法加载 TLS 客户端证书：{ex.Message}", ex);
            }
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectCoreAsync(
        ConfigurationOptions options,
        ConnectionConfig config,
        ConnectionSecrets secrets,
        CancellationToken cancellationToken)
    {
        try
        {
            if (config.Kind == RedisDeploymentKind.Sentinel)
            {
                return await ConnectSentinelAsync(options, config, secrets, cancellationToken).ConfigureAwait(false);
            }

            return await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex) when (ex.FailureType == ConnectionFailureType.AuthenticationFailure)
        {
            throw new AuthException("认证失败，请检查用户名或密码。", ex);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException)
        {
            var endpoint = options.EndPoints.Count > 0 ? options.EndPoints[0].ToString() : $"{config.Host}:{config.Port}";
            throw new ConnectException($"无法连接到 {endpoint}。", ex);
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectSentinelAsync(
        ConfigurationOptions options,
        ConnectionConfig config,
        ConnectionSecrets secrets,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception first) when (first is RedisConnectionException or RedisTimeoutException or TimeoutException)
        {
            try
            {
                return await ConnectSentinelViaMasterLookupAsync(options, config, secrets, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception second)
            {
                throw new ConnectException($"无法通过 Sentinel 连接 {config.Sentinel.MasterName}。", second.InnerException ?? first);
            }
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectSentinelViaMasterLookupAsync(
        ConfigurationOptions options,
        ConnectionConfig config,
        ConnectionSecrets secrets,
        CancellationToken cancellationToken)
    {
        var sentinelOptions = options.Clone();
        sentinelOptions.ServiceName = null;
        sentinelOptions.CommandMap = CommandMap.Sentinel;
        sentinelOptions.TieBreaker = "";
        sentinelOptions.DefaultDatabase = 0;
        sentinelOptions.Password = string.IsNullOrEmpty(secrets.SentinelPassword) ? secrets.RedisPassword : secrets.SentinelPassword;

        await using var sentinel = await ConnectionMultiplexer.ConnectAsync(sentinelOptions).WaitAsync(cancellationToken).ConfigureAwait(false);
        var server = sentinel.GetServer(config.Host, config.Port);
        var master = await server.SentinelGetMasterAddressByNameAsync(config.Sentinel.MasterName).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (master is null)
        {
            throw new ConnectException($"Sentinel 未找到 master「{config.Sentinel.MasterName}」。");
        }

        var masterOptions = options.Clone();
        masterOptions.ServiceName = null;
        masterOptions.EndPoints.Clear();
        masterOptions.EndPoints.Add(master);
        masterOptions.Password = secrets.RedisPassword;
        return await ConnectionMultiplexer.ConnectAsync(masterOptions).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class RedisSession : IRedisSession
{
    private readonly ConnectionMultiplexer _multiplexer;
    private readonly ICommandLog _commandLog;
    private readonly SshTunnel? _tunnel;
    private SessionState _state = SessionState.Connected;

    public RedisSession(
        ConnectionConfig config,
        ConnectionMultiplexer multiplexer,
        ICommandLog commandLog,
        SshTunnel? tunnel = null)
    {
        Config = config;
        _multiplexer = multiplexer;
        _commandLog = commandLog;
        _tunnel = tunnel;
        Keys = new RedisKeyBrowser(this);
        Values = new RedisValueService(this);
        Cli = new RedisCliExecutor(this);
        PubSub = new RedisPubSubService(this);
        Observability = new RedisObservabilityService(this);
    }

    public ConnectionConfig Config { get; }

    public SessionState State => _state;

    public IKeyBrowser Keys { get; }

    public IValueService Values { get; }

    public ICliExecutor Cli { get; }

    public IPubSubService PubSub { get; }

    public IObservability Observability { get; }

    public event EventHandler<CommandLogEntry>? CommandExecuted;

    public int CurrentDatabase => IsCluster ? 0 : Math.Max(0, Config.Database);

    public void SelectDatabase(int database)
    {
        if (IsCluster)
        {
            return;
        }

        Config.Database = Math.Max(0, database);
    }

    public Task<string> PingAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync("PING", null, async () =>
        {
            var elapsed = await Database.PingAsync().ConfigureAwait(false);
            return $"{elapsed.TotalMilliseconds:0} ms";
        }, cancellationToken);
    }

    public Task<string> GetInfoAsync(string? section = "server", CancellationToken cancellationToken = default)
    {
        return RunAsync("INFO", section, async () =>
        {
            var result = string.IsNullOrWhiteSpace(section)
                ? await Database.ExecuteAsync("INFO").ConfigureAwait(false)
                : await Database.ExecuteAsync("INFO", section).ConfigureAwait(false);
            return result.ToString() ?? string.Empty;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<RedisNodeSection>> GetMasterInfoAsync(string section, CancellationToken cancellationToken = default)
    {
        return RunAsync("INFO", section, async () =>
        {
            var masters = GetMasters();
            var list = new List<RedisNodeSection>(masters.Length);
            foreach (var server in masters)
            {
                try
                {
                    var result = string.IsNullOrWhiteSpace(section)
                        ? await server.ExecuteAsync("INFO").ConfigureAwait(false)
                        : await server.ExecuteAsync("INFO", section).ConfigureAwait(false);
                    list.Add(new RedisNodeSection(FormatNodeName(server), result.ToString() ?? string.Empty));
                }
                catch
                {
                    // A single unreachable master should not fail the whole Status page.
                }
            }

            return (IReadOnlyList<RedisNodeSection>)list;
        }, cancellationToken);
    }

    public Task<long> GetDbSizeAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync("DBSIZE", null, async () =>
        {
            var result = await Database.ExecuteAsync("DBSIZE").ConfigureAwait(false);
            return (long)result;
        }, cancellationToken);
    }

    public Task<int> GetDatabaseCountAsync(CancellationToken cancellationToken = default)
    {
        if (IsCluster)
        {
            return Task.FromResult(1);
        }

        return RunAsync("CONFIG", "GET databases", async () =>
        {
            try
            {
                var result = await Database.ExecuteAsync("CONFIG", "GET", "databases").ConfigureAwait(false);
                if (result.Resp2Type == ResultType.Array)
                {
                    var rows = (RedisResult[])result!;
                    if (rows.Length >= 2 && int.TryParse(rows[^1].ToString(), out var count) && count > 0)
                    {
                        return Math.Clamp(count, 1, 64);
                    }
                }
            }
            catch
            {
                // CONFIG may be disabled
            }

            return 16;
        }, cancellationToken);
    }

    public async Task<bool> TryHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Database.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal IDatabase Database => _multiplexer.GetDatabase(CurrentDatabase);

    internal ConnectionMultiplexer Multiplexer => _multiplexer;

    internal bool IsCluster
    {
        get
        {
            if (Config.Kind == RedisDeploymentKind.Cluster)
            {
                return true;
            }

            try
            {
                return Server.ServerType == ServerType.Cluster;
            }
            catch
            {
                return false;
            }
        }
    }

    internal IServer[] GetMasters()
    {
        var servers = _multiplexer.GetEndPoints()
            .Select(ep =>
            {
                try
                {
                    return _multiplexer.GetServer(ep);
                }
                catch
                {
                    return null;
                }
            })
            .Where(s => s is { IsConnected: true, IsReplica: false })
            .Cast<IServer>()
            .ToArray();

        return servers.Length > 0
            ? servers
            : _multiplexer.GetEndPoints().Select(ep => _multiplexer.GetServer(ep)).ToArray();
    }

    internal string FormatNodeName(IServer server)
    {
        var endpoint = ClusterNodesParser.FormatEndpoint(server.EndPoint);
        if (_tunnel is null)
        {
            return endpoint;
        }

        foreach (var pair in _tunnel.AdvertisedToLocal)
        {
            var local = $"{pair.Value.Host}:{pair.Value.Port}";
            if (endpoint.Equals(local, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        var port = server.EndPoint switch
        {
            IPEndPoint ip => ip.Port,
            DnsEndPoint dns => dns.Port,
            _ => -1
        };
        if (port > 0)
        {
            foreach (var pair in _tunnel.AdvertisedToLocal)
            {
                if (pair.Value.Port == port)
                {
                    return pair.Key;
                }
            }
        }

        return endpoint;
    }

    internal IServer Server
    {
        get
        {
            var endpoint = _multiplexer.GetEndPoints().FirstOrDefault()
                           ?? throw new ConnectException("连接没有可用的 Redis 节点。");
            return _multiplexer.GetServer(endpoint);
        }
    }

    internal void EnsureWritable(string command)
    {
        if (Config.ReadOnly && WriteCommands.IsWrite(command))
        {
            throw new ReadOnlyException(command);
        }
    }

    internal async Task<T> RunAsync<T>(
        string command,
        string? details,
        Func<Task<T>> action,
        CancellationToken cancellationToken,
        bool log = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await action().WaitAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            if (log)
            {
                RaiseLog(command, Mask(command, details), sw.Elapsed.TotalMilliseconds, success: true, error: null);
            }

            return result;
        }
        catch (ReadOnlyException)
        {
            sw.Stop();
            if (log)
            {
                RaiseLog(command, Mask(command, details), sw.Elapsed.TotalMilliseconds, success: false, error: "readonly");
            }

            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (log)
            {
                RaiseLog(command, Mask(command, details), sw.Elapsed.TotalMilliseconds, success: false, error: ex.Message);
            }

            throw new RedisDesktop.Core.RedisCommandException(ex.Message, ex);
        }
    }

    private static string? Mask(string command, string? details)
    {
        if (string.Equals(command, "AUTH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "HELLO", StringComparison.OrdinalIgnoreCase))
        {
            return "***";
        }

        if (string.IsNullOrEmpty(details))
        {
            return details;
        }

        return details.Contains("AUTH", StringComparison.OrdinalIgnoreCase) ? "***" : details;
    }

    private void RaiseLog(string command, string? details, double elapsed, bool success, string? error)
    {
        var entry = new CommandLogEntry(
            DateTimeOffset.Now,
            Config.Name,
            command,
            details,
            elapsed,
            success,
            error);
        _commandLog.Append(entry);
        CommandExecuted?.Invoke(this, entry);
    }

    public async ValueTask DisposeAsync()
    {
        if (_state == SessionState.Disposed)
        {
            return;
        }

        _state = SessionState.Disposed;
        if (PubSub is RedisPubSubService pubSub)
        {
            await pubSub.StopQuietlyAsync().ConfigureAwait(false);
        }
        await _multiplexer.CloseAsync().ConfigureAwait(false);
        _multiplexer.Dispose();
        if (_tunnel is not null)
        {
            await _tunnel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
