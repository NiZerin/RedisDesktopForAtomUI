using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public sealed class MemoryCommandLog : ICommandLog
{
    private readonly object _gate = new();
    private readonly List<CommandLogEntry> _entries = new(capacity: 64);
    private const int Capacity = 5000;

    public IReadOnlyList<CommandLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public event EventHandler<CommandLogEntry>? EntryAdded;

    public void Append(CommandLogEntry entry)
    {
        if (string.Equals(entry.Command, "PING", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            if (_entries.Count >= Capacity)
            {
                _entries.RemoveAt(0);
            }

            _entries.Add(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
