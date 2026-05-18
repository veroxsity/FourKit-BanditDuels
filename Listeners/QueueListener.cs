using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Player;

using BanditDuels.Queue;

namespace BanditDuels.Listeners;

/// <summary>
/// Cleans up the queue manager when a player disconnects. Without this,
/// a queue can hold ghost entries that auto-pair with the next joiner
/// but immediately fail because the front-of-queue player isn't online.
/// </summary>
public sealed class QueueListener : Listener
{
    private readonly QueueManager _queues;
    public QueueListener(QueueManager queues) { _queues = queues; }

    [EventHandler(Priority = EventPriority.Monitor)]
    public void onQuit(PlayerQuitEvent e)
    {
        _queues.removeFromQueue(e.getPlayer().getUniqueId());
    }
}
