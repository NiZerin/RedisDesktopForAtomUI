using System.Net;
using System.Net.Sockets;
using Renci.SshNet;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

internal sealed class SshTunnel : IAsyncDisposable
{
    private readonly SshClient _client;
    private readonly List<ForwardedPortLocal> _forwards = [];
    private bool _disposed;

    private SshTunnel(SshClient client)
    {
        _client = client;
    }

    public string LocalHost { get; private set; } = "127.0.0.1";

    public int LocalPort { get; private set; }

    public Dictionary<string, (string Host, int Port)> AdvertisedToLocal { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static SshTunnel Open(SshOptions ssh, ConnectionSecrets secrets, string remoteHost, int remotePort)
    {
        if (string.IsNullOrWhiteSpace(ssh.Host))
        {
            throw new ConnectException("SSH 已启用但未填写 SSH Host。");
        }

        if (string.IsNullOrWhiteSpace(ssh.Username))
        {
            throw new ConnectException("SSH 已启用但未填写用户名。");
        }

        SshClient client;
        try
        {
            client = CreateClient(ssh, secrets);
            client.Connect();
        }
        catch (Exception ex)
        {
            throw new ConnectException($"SSH 连接失败：{ex.Message}", ex);
        }

        var tunnel = new SshTunnel(client);
        try
        {
            var local = tunnel.Forward(remoteHost, remotePort);
            tunnel.LocalHost = local.Host;
            tunnel.LocalPort = local.Port;
            tunnel.AdvertisedToLocal[$"{remoteHost}:{remotePort}"] = local;
            return tunnel;
        }
        catch
        {
            client.Disconnect();
            client.Dispose();
            throw;
        }
    }

    public (string Host, int Port) Forward(string remoteHost, int remotePort)
    {
        var key = $"{remoteHost}:{remotePort}";
        if (AdvertisedToLocal.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var localPort = GetFreePort();
        var forward = new ForwardedPortLocal("127.0.0.1", (uint)localPort, remoteHost, (uint)remotePort);
        _client.AddForwardedPort(forward);
        try
        {
            forward.Start();
        }
        catch (Exception ex)
        {
            throw new ConnectException($"SSH 转发 {remoteHost}:{remotePort} 失败：{ex.Message}", ex);
        }

        _forwards.Add(forward);
        var mapped = ("127.0.0.1", localPort);
        AdvertisedToLocal[key] = mapped;
        return mapped;
    }

    public EndPoint MapEndpoint(EndPoint advertised)
    {
        var key = ClusterNodesParser.FormatEndpoint(advertised);
        if (AdvertisedToLocal.TryGetValue(key, out var mapped))
        {
            return new DnsEndPoint(mapped.Host, mapped.Port);
        }

        if (advertised is DnsEndPoint dns)
        {
            var created = Forward(dns.Host, dns.Port);
            return new DnsEndPoint(created.Host, created.Port);
        }

        if (advertised is IPEndPoint ip)
        {
            var created = Forward(ip.Address.ToString(), ip.Port);
            return new DnsEndPoint(created.Host, created.Port);
        }

        return advertised;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        foreach (var forward in _forwards)
        {
            try
            {
                if (forward.IsStarted)
                {
                    forward.Stop();
                }
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch
        {
            // ignore
        }

        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static SshClient CreateClient(SshOptions ssh, ConnectionSecrets secrets)
    {
        var port = ssh.Port <= 0 ? 22 : ssh.Port;
        if (!string.IsNullOrWhiteSpace(ssh.PrivateKeyPath))
        {
            PrivateKeyFile keyFile;
            try
            {
                keyFile = string.IsNullOrEmpty(secrets.SshPassphrase)
                    ? new PrivateKeyFile(ssh.PrivateKeyPath)
                    : new PrivateKeyFile(ssh.PrivateKeyPath, secrets.SshPassphrase);
            }
            catch (Exception ex)
            {
                throw new ConnectException($"无法读取 SSH 私钥：{ex.Message}", ex);
            }

            return new SshClient(ssh.Host, port, ssh.Username, keyFile);
        }

        if (string.IsNullOrEmpty(secrets.SshPassword))
        {
            throw new ConnectException("SSH 需要密码或私钥。");
        }

        return new SshClient(ssh.Host, port, ssh.Username, secrets.SshPassword);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
