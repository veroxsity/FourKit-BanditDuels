using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Player;

using BanditDuels.Duels;

namespace BanditDuels.Listeners;

public sealed class DuelQuitListener : Listener
{
    private readonly DuelManager _duels;
    public DuelQuitListener(DuelManager duels) { _duels = duels; }

    [EventHandler(Priority = EventPriority.Monitor)]
    public void onQuit(PlayerQuitEvent e)
    {
        var p = e.getPlayer();
        var match = _duels.getMatch(p.getUniqueId());
        if (match == null) return;

        _duels.endMatchByQuit(match, p.getUniqueId());
    }
}
