using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Entity;

using BanditDuels.Duels;
using BanditDuels.Kits;

namespace BanditDuels.Queue;

/// <summary>
/// Holds one <see cref="DuelQueue"/> per kit and auto-pairs players as they join.
/// A player may be in at most one queue at a time; trying to join another while
/// queued is rejected (strict mode, see /duel queue).
/// </summary>
public sealed class QueueManager
{
    private readonly DuelManager _duels;
    private readonly KitRegistry _kits;
    private readonly Dictionary<string, DuelQueue> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _playerKit = new();

    public QueueManager(DuelManager duels, KitRegistry kits)
    {
        _duels = duels;
        _kits = kits;
    }

    public IEnumerable<DuelQueue> all() => _queues.Values;
    public bool isQueued(Guid playerId) => _playerKit.ContainsKey(playerId);
    public string? getQueueKit(Guid playerId) =>
        _playerKit.TryGetValue(playerId, out var k) ? k : null;

    /// <summary>
    /// Join a kit queue. Returns a chat-ready string describing the outcome.
    /// </summary>
    public string joinQueue(Player player, string kitId)
    {
        if (_duels.isInMatch(player.getUniqueId()))
            return "§cYou're already in a duel.";
        if (BanditDuels.Instance.Parties?.isInParty(player.getUniqueId()) == true)
            return "§cLeave your party before joining the 1v1 queue.";

        var kit = _kits.get(kitId);
        if (kit == null)
            return "Unknown kit '" + kitId + "'. Try §f/duel kits §cfor the list.";

        // Strict: already in another queue? Reject.
        if (_playerKit.TryGetValue(player.getUniqueId(), out var existingKit))
        {
            if (string.Equals(existingKit, kitId, StringComparison.OrdinalIgnoreCase))
            {
                var pos = getOrCreate(existingKit).position(player.getUniqueId());
                return "You're already in the " + existingKit + " queue (position " + pos + ").";
            }
            return "You're already in the " + existingKit + " §cqueue. Run §f/duel leave §cfirst.";
        }

        var queue = getOrCreate(kit.Id);  // use kit.Id to normalize casing
        var entry = new QueueEntry(player.getUniqueId(), player.getName());
        queue.enqueue(entry);
        _playerKit[player.getUniqueId()] = kit.Id;

        // Try to pair. Skip over offline entries; if a pairing fails we restore.
        while (queue.size() >= 2)
        {
            var aEntry = queue.peekAt(0)!;
            var bEntry = queue.peekAt(1)!;
            var pa = FourKit.getPlayer(aEntry.PlayerName);
            var pb = FourKit.getPlayer(bEntry.PlayerName);

            if (pa == null) { queue.remove(aEntry.PlayerId); _playerKit.Remove(aEntry.PlayerId); continue; }
            if (pb == null) { queue.remove(bEntry.PlayerId); _playerKit.Remove(bEntry.PlayerId); continue; }

            // Commit: dequeue both and try start
            queue.dequeue();
            queue.dequeue();
            _playerKit.Remove(aEntry.PlayerId);
            _playerKit.Remove(bEntry.PlayerId);

            if (_duels.tryStartMatchFromQueue(pa, pb, kit.Id, out var error))
            {
                // The just-joined player is one of pa/pb; DuelManager already messaged them.
                // Return a brief confirmation tailored to the caller.
                if (player.getUniqueId() == aEntry.PlayerId || player.getUniqueId() == bEntry.PlayerId)
                    return "§a[Duel] §7Matched! Match starting.";
                return "§a[Duel] §7Paired two players for §f" + kit.Id + ".";
            }

            // Start failed (no arena, etc). Put them back and stop trying.
            queue.enqueue(aEntry);
            queue.enqueue(bEntry);
            _playerKit[aEntry.PlayerId] = kit.Id;
            _playerKit[bEntry.PlayerId] = kit.Id;
            return "§e[Duel] §7Queued, but couldn't start a match yet: " + error;
        }

        return "§7Joined the §f" + kit.Id + " queue (position " + queue.position(player.getUniqueId())
             + "). Waiting for an opponent. Run §f/duel leave §7to cancel.";
    }

    public string leaveQueue(Player player)
    {
        if (!_playerKit.TryGetValue(player.getUniqueId(), out var kitId))
            return "§cYou're not in any queue.";
        _queues[kitId].remove(player.getUniqueId());
        _playerKit.Remove(player.getUniqueId());
        return "§7Left the §f" + kitId + " queue.";
    }

    /// <summary>Silently remove a player from any queue. Used on disconnect and match-start.</summary>
    public void removeFromQueue(Guid playerId)
    {
        if (!_playerKit.TryGetValue(playerId, out var kitId)) return;
        _queues[kitId].remove(playerId);
        _playerKit.Remove(playerId);
    }

    private DuelQueue getOrCreate(string kitId)
    {
        if (!_queues.TryGetValue(kitId, out var q))
        {
            q = new DuelQueue(kitId);
            _queues[kitId] = q;
        }
        return q;
    }
}
