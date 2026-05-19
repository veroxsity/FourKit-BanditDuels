using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Block;
using Minecraft.Server.FourKit.Event.Entity;
using Minecraft.Server.FourKit.Event.Player;
using Minecraft.Server.FourKit.Entity;

using BanditDuels.Duels;

namespace BanditDuels.Listeners;

/// <summary>
/// Enforces arena rules:
///   - no block break / place by anyone inside an arena AABB
///   - matched players cannot drop items
///   - damage between non-opponents (where at least one side is in a match) is blocked
/// </summary>
public sealed class ArenaGuardListener : Listener
{
    private readonly DuelManager _duels;
    public ArenaGuardListener(DuelManager duels) { _duels = duels; }

    /// <summary>If false, block break/place inside arena bounds is allowed.
    /// Toggleable at runtime via /duel admin guard on|off. Default true.</summary>
    public bool BlockProtectionEnabled { get; set; } = true;

    [EventHandler(Priority = EventPriority.High)]
    public void onBlockBreak(BlockBreakEvent e)
    {
        if (!BlockProtectionEnabled) return;
        if (insideAnyArena(e.getBlock().getLocation())) e.setCancelled(true);
    }

    [EventHandler(Priority = EventPriority.High)]
    public void onBlockPlace(BlockPlaceEvent e)
    {
        if (!BlockProtectionEnabled) return;
        if (insideAnyArena(e.getBlockPlaced().getLocation())) e.setCancelled(true);
    }

    [EventHandler(Priority = EventPriority.High)]
    public void onDrop(PlayerDropItemEvent e)
    {
        if (_duels.isInMatch(e.getPlayer().getUniqueId())) e.setCancelled(true);
    }

    [EventHandler(Priority = EventPriority.High)]
    public void onDamage(EntityDamageByEntityEvent e)
    {
        if (e.getEntity() is not Player victim) return;
        if (e.getDamager() is not Player attacker) return;

        var vMatch = _duels.getMatch(victim.getUniqueId());
        var aMatch = _duels.getMatch(attacker.getUniqueId());

        // The only legal PvP is between non-eliminated opponents in the same match.
        if (vMatch != null
            && ReferenceEquals(vMatch, aMatch)
            && vMatch.areOpponents(attacker.getUniqueId(), victim.getUniqueId()))
            return;

        // Everything else (lobby PvP, cross-match attacks, matched-vs-unmatched) -> deny.
        e.setCancelled(true);
    }

    private bool insideAnyArena(Minecraft.Server.FourKit.Location loc)
    {
        var world = loc.getWorld();
        if (world == null) return false;
        return BanditDuels.Instance.Arenas.findContaining(
            loc.getBlockX(), loc.getBlockY(), loc.getBlockZ(), world.getName()) != null;
    }
}
