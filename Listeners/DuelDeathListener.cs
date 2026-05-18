using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Entity;

using BanditDuels.Duels;

namespace BanditDuels.Listeners;

public sealed class DuelDeathListener : Listener
{
    private readonly DuelManager _duels;
    public DuelDeathListener(DuelManager duels) { _duels = duels; }

    [EventHandler(Priority = EventPriority.Highest)]
    public void onPlayerDeath(PlayerDeathEvent e)
    {
        var player = e.getEntity();
        var match = _duels.getMatch(player.getUniqueId());
        if (match == null) return;

        // Prevent kit items from spilling into the world.
        e.getDrops().Clear();
        e.setDroppedExp(0);
        e.setKeepInventory(true);     // we'll reset via snapshot anyway
        e.setKeepLevel(true);
        e.setDeathMessage("§c" + match.nameOf(player.getUniqueId()) + " §7was defeated by §e" + match.otherNameOf(player.getUniqueId()) + "§7.");

        _duels.endMatchByDeath(match, player.getUniqueId());
    }
}
