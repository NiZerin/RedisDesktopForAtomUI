using System.Globalization;
using System.Text;
using RedisDesktop.Core;
using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

internal sealed class RedisCliExecutor : ICliExecutor
{
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "MONITOR", "SUBSCRIBE", "PSUBSCRIBE", "SSUBSCRIBE"
    };

    private readonly RedisSession _session;

    public RedisCliExecutor(RedisSession session)
    {
        _session = session;
    }

    public Task<CliResult> ExecuteAsync(string commandLine, CancellationToken cancellationToken = default)
    {
        var argv = CliCommandParser.Parse(commandLine);
        if (argv.Count == 0)
        {
            return Task.FromResult(new CliResult(string.Empty, false, false, "请输入命令。"));
        }

        var command = argv[0];
        if (Blocked.Contains(command))
        {
            return Task.FromResult(new CliResult(string.Empty, false, false, $"已拒绝命令 {command}。"));
        }

        var details = string.Join(' ', argv.Skip(1).Select(TruncateArg));
        return _session.RunAsync(command, details, async () =>
        {
            _session.EnsureWritable(command);
            object[] args = argv.Skip(1).Select(a => (object)a).ToArray();
            var result = args.Length == 0
                ? await _session.Database.ExecuteAsync(command).ConfigureAwait(false)
                : await _session.Database.ExecuteAsync(command, args).ConfigureAwait(false);
            var raw = Format(result, 0);
            var truncated = raw.Length > CliCommandParser.MaxOutputBytes;
            var output = CliCommandParser.Truncate(raw);
            return new CliResult(output, truncated, true, null, truncated ? raw : null);
        }, cancellationToken);
    }

    private static string Format(RedisResult result, int depth)
    {
        if (result.IsNull)
        {
            return "(nil)";
        }

        if (result.Resp2Type == ResultType.Array)
        {
            var rows = (RedisResult[])result!;
            if (rows.Length == 0)
            {
                return "(empty array)";
            }

            var sb = new StringBuilder();
            for (var i = 0; i < rows.Length; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(CultureInfo.InvariantCulture, $"{new string(' ', depth * 2)}{i + 1}) ");
                var child = Format(rows[i], depth + 1);
                if (child.Contains('\n'))
                {
                    sb.AppendLine();
                    sb.Append(child);
                }
                else
                {
                    sb.Append(child);
                }
            }

            return sb.ToString();
        }

        if (result.Resp2Type == ResultType.Integer)
        {
            return "(integer) " + result;
        }

        var text = result.ToString();
        return string.IsNullOrEmpty(text) ? "(empty)" : text!;
    }

    private static string TruncateArg(string arg)
        => arg.Length > 100 ? arg[..100] + "..." : arg;
}
