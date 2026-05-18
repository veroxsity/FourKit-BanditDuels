namespace BanditDuels.Queue;

/// <summary>One entry in a kit-specific queue.</summary>
public sealed record QueueEntry(Guid PlayerId, string PlayerName);

/// <summary>
/// FIFO queue of players waiting for a duel on a specific kit.
/// One <see cref="DuelQueue"/> per registered kit.
/// </summary>
public sealed class DuelQueue
{
    public string KitId { get; }
    private readonly LinkedList<QueueEntry> _entries = new();

    public DuelQueue(string kitId) { KitId = kitId; }

    public int size() => _entries.Count;
    public IEnumerable<QueueEntry> all() => _entries;

    public bool contains(Guid playerId)
    {
        foreach (var e in _entries) if (e.PlayerId == playerId) return true;
        return false;
    }

    /// <summary>1-indexed position in the queue, or 0 if not present.</summary>
    public int position(Guid playerId)
    {
        int i = 1;
        foreach (var e in _entries)
        {
            if (e.PlayerId == playerId) return i;
            i++;
        }
        return 0;
    }

    public void enqueue(QueueEntry entry) => _entries.AddLast(entry);

    public QueueEntry? dequeue()
    {
        if (_entries.Count == 0) return null;
        var first = _entries.First!.Value;
        _entries.RemoveFirst();
        return first;
    }

    public QueueEntry? peekAt(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;
        var node = _entries.First;
        for (int i = 0; i < index; i++) node = node!.Next;
        return node!.Value;
    }

    public bool remove(Guid playerId)
    {
        var node = _entries.First;
        while (node != null)
        {
            if (node.Value.PlayerId == playerId)
            {
                _entries.Remove(node);
                return true;
            }
            node = node.Next;
        }
        return false;
    }
}
