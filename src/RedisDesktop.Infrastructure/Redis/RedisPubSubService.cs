using RedisDesktop.Core;
using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

internal sealed class RedisPubSubService : IPubSubService
{
    private readonly RedisSession _session;
    private RedisChannel? _channel;
    private Action<RedisChannel, RedisValue>? _handler;

    public RedisPubSubService(RedisSession session)
    {
        _session = session;
    }

    public bool IsSubscribed => _channel is not null;

    public string? CurrentChannel { get; private set; }

    public bool IsPattern { get; private set; }

    public event EventHandler<PubSubMessage>? MessageReceived;

    public Task SubscribeAsync(string channel, bool pattern, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new RedisDesktop.Core.RedisCommandException("请填写频道或模式。");
        }

        return _session.RunAsync(pattern ? "PSUBSCRIBE" : "SUBSCRIBE", channel, async () =>
        {
            await UnsubscribeCoreAsync().ConfigureAwait(false);
            var subscriber = _session.Multiplexer.GetSubscriber();
            var redisChannel = pattern
                ? RedisChannel.Pattern(channel.Trim())
                : RedisChannel.Literal(channel.Trim());
            _handler = (ch, value) =>
            {
                MessageReceived?.Invoke(this, new PubSubMessage(
                    DateTimeOffset.Now,
                    ch.ToString(),
                    value.ToString()));
            };
            await subscriber.SubscribeAsync(redisChannel, _handler).ConfigureAwait(false);
            _channel = redisChannel;
            CurrentChannel = channel.Trim();
            IsPattern = pattern;
            return true;
        }, cancellationToken);
    }

    public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        return _session.RunAsync("UNSUBSCRIBE", CurrentChannel, async () =>
        {
            await UnsubscribeCoreAsync().ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task PublishAsync(string channel, string message, CancellationToken cancellationToken = default)
    {
        _session.EnsureWritable("PUBLISH");
        return _session.RunAsync("PUBLISH", channel, async () =>
        {
            await _session.Multiplexer.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(channel), message)
                .ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    internal Task StopQuietlyAsync() => UnsubscribeCoreAsync();

    private async Task UnsubscribeCoreAsync()
    {
        if (_channel is null || _handler is null)
        {
            CurrentChannel = null;
            IsPattern = false;
            return;
        }

        try
        {
            await _session.Multiplexer.GetSubscriber()
                .UnsubscribeAsync(_channel.Value, _handler)
                .ConfigureAwait(false);
        }
        catch
        {
            // multiplexer may already be closing
        }

        _channel = null;
        _handler = null;
        CurrentChannel = null;
        IsPattern = false;
    }
}
