using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Player;

using BanditDuels.Party;

namespace BanditDuels.Listeners;

public sealed class PartyListener : Listener
{
    private readonly PartyManager _parties;

    public PartyListener(PartyManager parties) { _parties = parties; }

    [EventHandler(Priority = EventPriority.Monitor)]
    public void onQuit(PlayerQuitEvent e)
    {
        _parties.removeOnQuit(e.getPlayer());
    }
}
