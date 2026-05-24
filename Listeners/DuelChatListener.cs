using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Entity;
using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Player;

using BanditDuels.Duels;

namespace BanditDuels.Listeners;

// Scopes chat by removing non-recipients from PlayerChatEvent.getRecipients().
// FourKit then delivers the message only to the players still in the set.
// Duel-originated chat is also flagged as not externally broadcastable so
// DiscordBridge does not relay private match chatter.
//
// Routing:
//   * Sender in a duel: only the same-match participants see the message.
//   * Sender NOT in a duel: only other lobby players (non-duelling) see it.
//   * Sender always sees their own echo.
//   * Admins (per AdminManager) optionally see everything when
//     FeatureConfig.AdminsSeeAllChat is true.
//   * Server console always logs the message.
public sealed class DuelChatListener : Listener
{
    private readonly DuelManager _duels;

    public DuelChatListener(DuelManager duels)
    {
        _duels = duels;
    }

    [EventHandler(Priority = EventPriority.Normal)]
    public void onChat(PlayerChatEvent e)
    {
        if (e.isCancelled()) return;

        var sender = e.getPlayer();
        if (sender == null) return;

        var message = e.getMessage() ?? "";

        // Sender's match (null if they're not in one). Match reference equality
        // works because DuelManager keeps one Match instance per active duel.
        var senderMatch = _duels.getMatch(sender.getUniqueId());
        if (senderMatch != null)
            e.setExternalBroadcastAllowed(false);

        // Cache feature config once per dispatch.
        var bd = BanditDuels.Instance;
        bool adminOverride  = bd?.Config?.Features?.AdminsSeeAllChat ?? false;
        bool consoleLogging = bd?.Config?.Features?.ChatConsoleLog   ?? false;

        // Optional console mirror. Off by default - BanditChat (or another
        // chat plugin) typically owns this. Set Features.ChatConsoleLog
        // to true in config.json if you want BanditDuels to log instead.
        if (consoleLogging)
            Console.WriteLine("[Chat] <" + sender.getName() + "> " + message);

        // Build the removal list - can't mutate the set while iterating it.
        var recipients = e.getRecipients();
        var toRemove = new List<Player>();

        foreach (var recipient in recipients)
        {
            // Sender always sees their own echo.
            if (recipient.getUniqueId() == sender.getUniqueId()) continue;

            // Admin/chat-bypass: holders of banditduels.bypass.chat see
            // everything regardless of duel scoping.
            if (adminOverride && LCEPermsBridge.has(recipient, "banditduels.bypass.chat")) continue;

            var recipientMatch = _duels.getMatch(recipient.getUniqueId());

            if (recipientMatch != null)
            {
                // Recipient is in a duel: keep only if it's the same match as
                // the sender. Other duels and lobby chat stay out.
                if (senderMatch == null || !ReferenceEquals(senderMatch, recipientMatch))
                    toRemove.Add(recipient);
            }
            else
            {
                // Recipient is in the lobby: keep only if the sender is also
                // in the lobby. Duel chat does not leak out.
                if (senderMatch != null)
                    toRemove.Add(recipient);
            }
        }

        foreach (var r in toRemove) recipients.Remove(r);
    }
}
