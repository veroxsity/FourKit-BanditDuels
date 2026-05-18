using Minecraft.Server.FourKit;

using BanditDuels.Duels;

namespace BanditDuels.Ui;

/// <summary>
/// Per-match chat updates fired at fixed time milestones rather than every second.
/// Also evicts expired duel requests and drives the match time cap check on each tick.
/// </summary>
public sealed class DuelHud
{
    private readonly BanditDuels _plugin;

    /// <summary>
    /// Milestones, listed by elapsed-seconds-since-Active. Each fires exactly once per match.
    /// Tuned for a 300-second match: minute warnings, then 30s/15s/10s, then last-5 countdown.
    /// </summary>
    public static readonly (int elapsed, string text)[] Milestones =
    {
        ( 60, "§e4 §7minutes remaining"),
        (120, "§e3 §7minutes remaining"),
        (180, "§e2 §7minutes remaining"),
        (240, "§e1 §7minute remaining"),
        (270, "§630 §7seconds left"),
        (285, "§615 §7seconds left"),
        (290, "§610 §7seconds left"),
        (295, "§c5..."),
        (296, "§c4..."),
        (297, "§c3..."),
        (298, "§c2..."),
        (299, "§c1..."),
    };

    public DuelHud(BanditDuels plugin) { _plugin = plugin; }

    public void start() => FourKit.getScheduler().runTaskTimer(_plugin, tick, 20, 20);
    public void stop() { /* scheduler stops on plugin disable */ }

    private void tick()
    {
        var mgr = _plugin.Manager;
        mgr.purgeExpiredRequests();

        foreach (var match in mgr.activeMatches())
        {
            mgr.tickMatch(match);
            if (match.State != MatchState.Active) continue;
            if (match.ActiveAt is not { } active) continue;

            var elapsed = (int)(DateTime.UtcNow - active).TotalSeconds;

            // Fire any not-yet-fired milestones that are now due.
            while (match.NextMilestoneIndex < Milestones.Length &&
                   Milestones[match.NextMilestoneIndex].elapsed <= elapsed)
            {
                var msg = "[Duel] " + Milestones[match.NextMilestoneIndex].text +
                          "  (vs " + "{opp}" + ")";
                match.NextMilestoneIndex++;
                FourKit.getPlayer(match.PlayerAName)?.sendMessage(msg.Replace("{opp}", match.PlayerBName));
                FourKit.getPlayer(match.PlayerBName)?.sendMessage(msg.Replace("{opp}", match.PlayerAName));
            }
        }
    }
}
