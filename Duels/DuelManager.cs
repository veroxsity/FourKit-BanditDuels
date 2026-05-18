using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Entity;
using Minecraft.Server.FourKit.Scheduler;

using BanditDuels.Arenas;
using BanditDuels.Kits;
using BanditDuels.Stats;
using BanditDuels.Ui;

namespace BanditDuels.Duels;

/// <summary>
/// Single source of truth for active duels and pending requests.
/// All methods must be called on the main server thread (driven by event
/// handlers and the scheduler).
/// </summary>
public sealed class DuelManager
{
    public static readonly TimeSpan RequestTtl = TimeSpan.FromSeconds(60);
    public static readonly int MatchTimeCapSeconds = 300;   // 5 min
    public static readonly int CountdownSeconds   = 3;

    private readonly BanditDuels _plugin;
    private readonly ArenaRegistry _arenas;
    private readonly KitRegistry _kits;
    private readonly DuelHud _hud;

    private readonly Dictionary<Guid, Match>          _matchByPlayer = new();
    private readonly Dictionary<string, DuelRequest>  _requests      = new(StringComparer.OrdinalIgnoreCase);

    public DuelManager(BanditDuels plugin, ArenaRegistry arenas, KitRegistry kits, DuelHud hud)
    {
        _plugin = plugin;
        _arenas = arenas;
        _kits = kits;
        _hud = hud;
    }

    // ---------- query ----------

    public bool isInMatch(Guid id) => _matchByPlayer.ContainsKey(id);
    public Match? getMatch(Guid id) => _matchByPlayer.TryGetValue(id, out var m) ? m : null;

    public IEnumerable<Match> activeMatches()
    {
        var seen = new HashSet<Match>();
        foreach (var m in _matchByPlayer.Values)
            if (seen.Add(m)) yield return m;
    }

    // ---------- requests ----------

    public string createRequest(Player challenger, Player target, string kitId)
    {
        if (challenger.getUniqueId() == target.getUniqueId())
            return "§cYou cannot duel yourself.";

        if (isInMatch(challenger.getUniqueId())) return "You are already in a duel.";
        if (isInMatch(target.getUniqueId()))     return "§cThat player is already in a duel.";

        if (_kits.get(kitId) == null)
            return "§cUnknown kit '" + kitId + "'. Available: " + string.Join(", ", kitIds());

        var req = new DuelRequest(
            challenger.getUniqueId(), challenger.getName(),
            target.getUniqueId(),     target.getName(),
            kitId, RequestTtl);

        _requests[req.key()] = req;

        target.sendMessage("§6[Duel] §e" + challenger.getName() + " §7challenged you to a §b" + _kits.get(kitId)!.DisplayName + " §7duel.");
        target.sendMessage("§7Type §f/duel accept " + challenger.getName() + " §7to accept, §f/duel deny " + challenger.getName() + " §7to decline.");
        return "§6[Duel] §7Challenge sent to §e" + target.getName() + " (" + _kits.get(kitId)!.DisplayName + "). Expires in " + (int)RequestTtl.TotalSeconds + "s.";
    }

    public string acceptRequest(Player acceptor, Player challenger)
    {
        var key = DuelRequest.keyFor(challenger.getName(), acceptor.getName());
        if (!_requests.TryGetValue(key, out var req))
            return "No duel request from " + challenger.getName() + ".";

        _requests.Remove(key);
        if (req.isExpired()) return "§cThat duel request has expired.";

        if (isInMatch(challenger.getUniqueId())) return "" + challenger.getName() + " is already in a duel.";
        if (isInMatch(acceptor.getUniqueId()))   return "You are already in a duel.";

        var kit = _kits.get(req.KitId);
        if (kit == null) return "§cKit no longer available.";

        var arena = _arenas.acquireFree();
        if (arena == null) return "No arenas are free right now.";

        startMatch(challenger, acceptor, arena, kit);
        return "§6[Duel] §7Match started in §b" + arena.Name + ".";
    }

    public string denyRequest(Player acceptor, Player challenger)
    {
        var key = DuelRequest.keyFor(challenger.getName(), acceptor.getName());
        if (!_requests.Remove(key))
            return "No duel request from " + challenger.getName() + ".";
        challenger.sendMessage("§6[Duel] §e" + acceptor.getName() + " §7declined your challenge.");
        return "§6[Duel] §7Declined challenge from §e" + challenger.getName() + ".";
    }

    public void purgeExpiredRequests()
    {
        if (_requests.Count == 0) return;
        var toRemove = _requests.Values.Where(r => r.isExpired()).Select(r => r.key()).ToList();
        foreach (var k in toRemove) _requests.Remove(k);
    }

    // ---------- queue entry point ----------

    /// <summary>
    /// Try to start a match between two players who came from the queue system.
    /// Returns false (with an error message) if the match couldn't start because of
    /// a missing kit, no free arenas, or one side already being in a match.
    /// </summary>
    public bool tryStartMatchFromQueue(Player a, Player b, string kitId, out string error)
    {
        if (a.getUniqueId() == b.getUniqueId())
        {
            error = "Cannot match a player with themselves.";
            return false;
        }
        if (isInMatch(a.getUniqueId())) { error = a.getName() + " is already in a duel."; return false; }
        if (isInMatch(b.getUniqueId())) { error = b.getName() + " is already in a duel."; return false; }

        var kit = _kits.get(kitId);
        if (kit == null) { error = "Kit '" + kitId + "' no longer exists."; return false; }

        var arena = _arenas.acquireFree();
        if (arena == null) { error = "No arenas are free right now."; return false; }

        startMatch(a, b, arena, kit);
        error = "";
        return true;
    }

    // ---------- match lifecycle ----------

    private void startMatch(Player a, Player b, Arena arena, Kit kit)
    {
        // If either player was waiting in a queue, drop them now so finishing
        // this match doesn't accidentally pair them with the next joiner.
        BanditDuels.Instance.Queues?.removeFromQueue(a.getUniqueId());
        BanditDuels.Instance.Queues?.removeFromQueue(b.getUniqueId());

        var snapA = PlayerSnapshot.capture(a);
        var snapB = PlayerSnapshot.capture(b);

        var match = new Match(
            a.getUniqueId(), a.getName(), snapA,
            b.getUniqueId(), b.getName(), snapB,
            arena, kit);

        _matchByPlayer[a.getUniqueId()] = match;
        _matchByPlayer[b.getUniqueId()] = match;

        prepareForFight(a);
        prepareForFight(b);

        kit.apply(a);
        kit.apply(b);

        a.teleport(arena.getSpawnA());
        b.teleport(arena.getSpawnB());

        // Snapshot the arena's pristine state on first use. Teleport above
        // has loaded the relevant chunks, so getBlockAt returns real data.
        // Subsequent matches in the same arena reuse the cached snapshot
        // and skip the capture cost.
        BanditDuels.Instance.ArenaResetter?.snapshotIfNeeded(arena);

        a.sendMessage("§6[Duel] §7Match starting against §e" + b.getName() + " §7on §b" + arena.Name + "§7.");
        b.sendMessage("§6[Duel] §7Match starting against §e" + a.getName() + " §7on §b" + arena.Name + "§7.");

        FourKit.broadcastMessage("§6[Duel] §e" + a.getName() + " §7vs §e" + b.getName() + " §7on §b" + arena.Name + " §7(§f" + kit.DisplayName + "§7)");

        scheduleCountdownTick(match);
    }

    private void prepareForFight(Player p)
    {
        p.getInventory().clear();
        p.setGameMode(GameMode.SURVIVAL);
        p.setMaxHealth(20.0);
        p.setHealth(20.0);
        p.setFoodLevel(20);
        p.setSaturation(20f);
        p.setExhaustion(0f);
        p.setLevel(0);
        p.setExp(0f);
    }

    private void scheduleCountdownTick(Match match)
    {
        // Single recurring task; cancel from inside when done. Avoids the
        // FourKit scheduler bug where adding new tasks (runTaskLater) during
        // its own iteration mutates the task dictionary and throws.
        FourKitTask? task = null;
        task = FourKit.getScheduler().runTaskTimer(_plugin, () => countdownStep(match, task!), 20, 20);
    }

    private void countdownStep(Match match, FourKitTask task)
    {
        if (match.State != MatchState.Countdown) { task.cancel(); return; }

        var a = FourKit.getPlayer(match.PlayerAName);
        var b = FourKit.getPlayer(match.PlayerBName);
        if (a == null || b == null)
        {
            task.cancel();
            endMatchByForfeit(match, a, b);
            return;
        }

        if (match.CountdownSecondsRemaining > 0)
        {
            a.sendMessage("§e" + match.CountdownSecondsRemaining + "...");
            b.sendMessage("§e" + match.CountdownSecondsRemaining + "...");
            match.CountdownSecondsRemaining--;
        }
        else
        {
            a.sendMessage("§aFIGHT!");
            b.sendMessage("§aFIGHT!");
            match.State = MatchState.Active;
            match.ActiveAt = DateTime.UtcNow;
            task.cancel();
        }
    }

    /// <summary>Called once per second by DuelHud while the match is active. Handles the time-cap draw
    /// and the arena-escape forfeit check (ender pearls, parkour out, etc.).</summary>
    public void tickMatch(Match match)
    {
        if (match.State != MatchState.Active) return;
        if (match.ActiveAt is not { } active) return;

        // Escape check before time cap: if a player pearled out in the last
        // second of a 5min match they shouldn't get saved by the time-cap
        // draw. Offline players are handled by the quit path; we only
        // forfeit-on-escape players who are demonstrably still connected
        // and just happen to be outside the AABB.
        var a = FourKit.getPlayer(match.PlayerAName);
        if (a != null && isOutsideArena(a, match.Arena))
        {
            endMatchByEscape(match, match.PlayerA);
            return;
        }
        var b = FourKit.getPlayer(match.PlayerBName);
        if (b != null && isOutsideArena(b, match.Arena))
        {
            endMatchByEscape(match, match.PlayerB);
            return;
        }

        var elapsed = (DateTime.UtcNow - active).TotalSeconds;
        if (elapsed >= MatchTimeCapSeconds)
            endMatchDraw(match);
    }

    private static bool isOutsideArena(Player p, Arena arena)
    {
        var loc = p.getLocation();
        var world = loc.getWorld();
        if (world == null) return false;  // unknown world; don't penalize
        return !arena.contains(loc.getBlockX(), loc.getBlockY(), loc.getBlockZ(), world.getName());
    }

    public void endMatchByDeath(Match match, Guid loserId)
    {
        if (match.State == MatchState.Ending) return;
        match.State = MatchState.Ending;

        var loserName = match.nameOf(loserId);
        var winnerId  = match.otherOf(loserId);
        var winnerName = match.nameOf(winnerId);

        FourKit.broadcastMessage("§6[Duel] §a" + winnerName + " §7defeated §c" + loserName + " §7on §b" + match.Arena.Name + "§7.");

        recordResult(match, winnerId, MatchEndReason.Death);
        finalizeMatch(match);
    }

    public void endMatchByForfeit(Match match, Player? maybeA, Player? maybeB)
    {
        if (match.State == MatchState.Ending) return;
        match.State = MatchState.Ending;

        string? winnerName = null;
        string? loserName = null;
        Guid? winnerId = null;
        if (maybeA == null && maybeB != null) { winnerName = match.PlayerBName; loserName = match.PlayerAName; winnerId = match.PlayerB; }
        else if (maybeB == null && maybeA != null) { winnerName = match.PlayerAName; loserName = match.PlayerBName; winnerId = match.PlayerA; }

        if (winnerName != null && loserName != null)
            FourKit.broadcastMessage("§6[Duel] §a" + winnerName + " §7wins by forfeit (§c" + loserName + " §7left).");
        else
            FourKit.broadcastMessage("§6[Duel] §7Match cancelled on §b" + match.Arena.Name + ".");

        if (winnerId.HasValue)
            recordResult(match, winnerId.Value, MatchEndReason.Forfeit);
        // If both gone (winnerId null), skip stats; nobody to credit.

        finalizeMatch(match);
    }

    public void endMatchByQuit(Match match, Guid leaverId)
    {
        if (match.State == MatchState.Ending) return;
        match.State = MatchState.Ending;

        var winnerName = match.otherNameOf(leaverId);
        var loserName = match.nameOf(leaverId);
        FourKit.broadcastMessage("§6[Duel] §a" + winnerName + " §7wins by forfeit (§c" + loserName + " §7left).");

        recordResult(match, match.otherOf(leaverId), MatchEndReason.Quit);
        finalizeMatch(match);
    }

    /// <summary>
    /// Called when a still-connected duelist is detected outside their arena
    /// AABB by the periodic <see cref="tickMatch"/> check. The escapee
    /// forfeits and the other player wins. Distinct from
    /// <see cref="endMatchByQuit"/>, which fires only on actual disconnect.
    /// </summary>
    public void endMatchByEscape(Match match, Guid escaperId)
    {
        if (match.State == MatchState.Ending) return;
        match.State = MatchState.Ending;

        var winnerId   = match.otherOf(escaperId);
        var winnerName = match.otherNameOf(escaperId);
        var loserName  = match.nameOf(escaperId);

        FourKit.broadcastMessage("§6[Duel] §a" + winnerName + " §7wins - §c" + loserName + " §7left the arena.");
        FourKit.getPlayer(loserName)?.sendMessage("§cYou forfeited by leaving the arena.");

        recordResult(match, winnerId, MatchEndReason.Escape);
        finalizeMatch(match);
    }

    public void endMatchDraw(Match match)
    {
        if (match.State == MatchState.Ending) return;
        match.State = MatchState.Ending;
        FourKit.broadcastMessage("§6[Duel] §7Match between §e" + match.PlayerAName + " §7and §e" + match.PlayerBName + " §7ended in a draw (time limit).");
        recordResult(match, Guid.Empty, MatchEndReason.Draw);
        finalizeMatch(match);
    }

    /// <summary>Persist a row in stats.json. Skipped silently if the stats repo isn't enabled.</summary>
    private void recordResult(Match match, Guid winnerId, string endReason)
    {
        var stats = BanditDuels.Instance.Stats;
        if (stats == null) return;

        var started = match.ActiveAt ?? match.StartedAt;
        var ended = DateTime.UtcNow;
        var record = new MatchRecord
        {
            Id          = Guid.NewGuid().ToString(),
            KitId       = match.Kit.Id,
            ArenaName   = match.Arena.Name,
            PlayerA     = match.PlayerA.ToString(),
            PlayerAName = match.PlayerAName,
            PlayerB     = match.PlayerB.ToString(),
            PlayerBName = match.PlayerBName,
            WinnerUuid  = winnerId == Guid.Empty ? "" : winnerId.ToString(),
            EndReason   = endReason,
            StartedAt   = started.ToString("O"),
            EndedAt     = ended.ToString("O"),
            DurationSeconds = Math.Max(0, (int)(ended - started).TotalSeconds),
        };
        stats.recordMatch(record);
    }

    private void finalizeMatch(Match match)
    {
        _matchByPlayer.Remove(match.PlayerA);
        _matchByPlayer.Remove(match.PlayerB);

        var a = FourKit.getPlayer(match.PlayerAName);
        var b = FourKit.getPlayer(match.PlayerBName);

        // Clear inventories first so the kit items aren't left behind when restoring.
        // The death drop list is cleared in DuelDeathListener; this covers the survivor.
        if (a != null) { a.getInventory().clear(); match.SnapshotA.restore(a); }
        else { BanditDuels.Instance.PendingResets?.markPending(match.PlayerA); }

        if (b != null) { b.getInventory().clear(); match.SnapshotB.restore(b); }
        else { BanditDuels.Instance.PendingResets?.markPending(match.PlayerB); }

        // Restore any blocks the fight modified (water buckets, broken blocks, etc.)
        // back to the arena's pristine snapshot. Keep the arena marked busy
        // until the batched restore completes so the next match doesn't try
        // to use a half-reset arena.
        var arena = match.Arena;
        var resetter = BanditDuels.Instance.ArenaResetter;
        if (resetter != null && resetter.hasSnapshot(arena.Name))
            resetter.scheduleRestore(arena, onComplete: () => _arenas.release(arena));
        else
            _arenas.release(arena);
    }

    public void shutdown()
    {
        // best-effort restore on plugin disable
        foreach (var match in activeMatches().ToList())
        {
            match.State = MatchState.Ending;
            finalizeMatch(match);
        }
        _requests.Clear();
    }

    private IEnumerable<string> kitIds() => _kits.all().Select(k => k.Id);
}
