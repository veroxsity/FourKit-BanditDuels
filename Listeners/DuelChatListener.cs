using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Player;

using BanditDuels.Duels;

namespace BanditDuels.Listeners;

/// <summary>
/// Routes chat so that players in a duel only see chat from their own match,
/// and players outside duels only see chat from other lobby players. This
/// prevents distracting lobby chatter from filling a duelist's screen mid-
/// fight, and keeps duel banter private to the participants.
///
/// FourKit's PlayerChatEvent does NOT expose getRecipients() like Bukkit's
/// AsyncPlayerChatEvent. The only handle we have is setCancelled(). So this
/// listener does the obvious thing: cancel the event entirely, then manually
/// sendMessage() to the subset of online players who should hear it.
///
/// Routing rules:
///   * Sender in a duel: deliver only to the other participant of the same
///     match (private duel chat).
///   * Sender NOT in a duel: deliver to all other online players who are
///     also not in a duel. Players in duels stay quiet.
///   * Sender always receives the echo so they see their own message
///     posted, matching normal chat behaviour.
///   * Server console always sees a log line so admins watching the console
///     can monitor everything.
///   * Admins (per the AdminManager admin list) are an exception when
///     FeatureConfig.AdminsSeeAllChat is true: they receive every message
///     regardless of duel state, for moderation purposes. Senders don't see
///     this - their own message echo is unchanged. The toggle defaults to
///     true; flip it off in config.json to make admins follow the same chat
///     scoping as regular players.
/// </summary>
public sealed class DuelChatListener : Listener
{
    private readonly DuelManager _duels;

    public DuelChatListener(DuelManager duels)
    {
        _duels = duels;
    }

    [EventHandler(Priority = EventPriority.Highest)]
    public void onChat(PlayerChatEvent e)
    {
        // Already cancelled by something else (e.g. anti-spam plugin). Don't
        // resurrect it.
        if (e.isCancelled()) return;

        var sender = e.getPlayer();
        if (sender == null) return;

        var message = e.getMessage() ?? "";
        // Simple chat format: <name> message. We ignore PlayerChatEvent.getFormat()
        // because FourKit may pass a Java-style %s format string that doesn't
        // translate cleanly to C#, and LCE chat has no displayName/prefix
        // system to preserve anyway.
        var formatted = "<" + sender.getName() + "> " + message;

        // Take over delivery. From here on the message reaches its audience
        // only via our explicit sendMessage calls below.
        e.setCancelled(true);

        // Always log to server console so server staff watching the terminal
        // see all chat, including private duel messages.
        Console.WriteLine("[Chat] " + formatted);

        // Cache the sender's match (null if they're not in one). Using the
        // Match reference for equality - both participants in a match share
        // the same Match object instance in the DuelManager dictionary.
        var senderMatch = _duels.getMatch(sender.getUniqueId());

        // Pull admin list + override toggle once per dispatch so we're not
        // hitting BanditDuels.Instance per recipient. Null-safe in case the
        // managers aren't initialized yet during plugin enable.
        var bd = BanditDuels.Instance;
        var admins = bd?.Admins;
        bool adminOverride = bd?.Config?.Features?.AdminsSeeAllChat ?? false;

        foreach (var recipient in FourKit.getOnlinePlayers())
        {
            try
            {
                // Admin override: deliver every message to admins regardless
                // of duel scoping. Sender is also covered by this branch if
                // they happen to be an admin (their echo goes through the
                // same code path).
                if (adminOverride && admins != null && admins.isAdmin(recipient.getName()))
                {
                    recipient.sendMessage(formatted);
                    continue;
                }

                var recipientMatch = _duels.getMatch(recipient.getUniqueId());

                if (recipientMatch != null)
                {
                    // Recipient is in a duel. Deliver only if the sender is
                    // in the SAME match (private duel chat) or if the
                    // recipient IS the sender (echo back to themselves so
                    // they see their own message).
                    if (recipient.getUniqueId() == sender.getUniqueId())
                    {
                        recipient.sendMessage(formatted);
                    }
                    else if (senderMatch != null && ReferenceEquals(senderMatch, recipientMatch))
                    {
                        recipient.sendMessage(formatted);
                    }
                    // else: skip - duelist doesn't see lobby chat or other-duel chat
                }
                else
                {
                    // Recipient is in the lobby. Deliver only if the sender
                    // is also in the lobby. Duel chat doesn't leak out.
                    if (senderMatch == null)
                    {
                        recipient.sendMessage(formatted);
                    }
                }
            }
            catch (Exception ex)
            {
                // Per-recipient send failures shouldn't stop the rest of the
                // broadcast. Log and keep going.
                Console.WriteLine("[BanditDuels/Chat] failed to deliver to "
                    + recipient.getName() + ": " + ex.Message);
            }
        }
    }
}
