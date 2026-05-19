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
        if (BanditDuels.Instance.Parties?.isInParty(challenger.getUniqueId()) == true)
            return "§cLeave your party before starting a 1v1 duel.";
        if (BanditDuels.Instance.Parties?.isInParty(target.getUniqueId()) == true)
            return "§cThat player is in a party.";

        if (_kits.get(kitId) == null)
            return "§cUnknown kit '" + kitId + "'. Available: " + string.Join(", ", kitIds());

        if (!_arenas.all().Any(a => a.supportsTeamSize(1)))
            return "§cNo 1v1 arenas are configured.";

        var req = new DuelRequest(
            challenger.getUniqueId(), challenger.getName(),
            target.getUniqueId(),     target.getName(),
            kitId, RequestTtl);

        _requests[req.key()] = req;

        target.sendMessage("§6[Duel] §e" + challenger.getName() + " §7challenged you to a §b" + _kits.get(kitId)!.DisplayName + " §71v1 duel.");
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
        if (BanditDuels.Instance.Parties?.isInParty(challenger.getUniqueId()) == true)
            return challenger.getName() + " is in a party.";
        if (BanditDuels.Instance.Parties?.isInParty(acceptor.getUniqueId()) == true)
            return "§cLeave your party before accepting a 1v1 duel.";

        var kit = _kits.get(req.KitId);
        if (kit == null) return "§cKit no longer available.";

        var arena = _arenas.acquireFree(teamSize: 1);
        if (arena == null) return "No 1v1 arenas are free right now.";

        startMatch(new[] { challenger }, new[] { acceptor }, arena, kit);
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

    // ---------- queue entry points ----------

    public bool tryStartMatchFromQueue(Player a, Player b, string kitId, out string error) =>
        tryStartMatchFromQueue(new[] { a }, new[] { b }, kitId, out error);

    public bool tryStartMatchFromQueue(IReadOnlyList<Player> teamA, IReadOnlyList<Player> teamB, string kitId, out string error)
    {
        if (teamA.Count == 0 || teamA.Count != teamB.Count)
        {
            error = "Teams must be the same size.";
            return false;
        }

        var all = teamA.Concat(teamB).ToList();
        if (all.Select(p => p.getUniqueId()).Distinct().Count() != all.Count)
        {
            error = "A player cannot be on both teams.";
            return false;
        }

        foreach (var p in all)
        {
            if (isInMatch(p.getUniqueId()))
            {
                error = p.getName() + " is already in a duel.";
                return false;
            }
        }

        var kit = _kits.get(kitId);
        if (kit == null) { error = "Kit '" + kitId + "' no longer exists."; return false; }

        int teamSize = teamA.Count;
        var arena = _arenas.acquireFree(teamSize);
        if (arena == null) { error = "No " + teamSize + "v" + teamSize + " arenas are free right now."; return false; }

        startMatch(teamA, teamB, arena, kit);
        error = "";
        return true;
    }

    // ---------- match lifecycle ----------

    private void startMatch(IReadOnlyList<Player> teamA, IReadOnlyList<Player> teamB, Arena arena, Kit kit)
    {
        foreach (var p in teamA.Concat(teamB))
            BanditDuels.Instance.Queues?.removeFromQueue(p.getUniqueId());

        var match = new Match(
            teamA.Select(p => new MatchPlayer(p.getUniqueId(), p.getName(), PlayerSnapshot.capture(p), isTeamA: true)).ToList(),
            teamB.Select(p => new MatchPlayer(p.getUniqueId(), p.getName(), PlayerSnapshot.capture(p), isTeamA: false)).ToList(),
            arena,
            kit);

        foreach (var mp in match.Players)
            _matchByPlayer[mp.Id] = match;

        for (int i = 0; i < teamA.Count; i++)
            prepareAndSend(teamA[i], kit, arena.getTeamSpawn(teamA: true, i));

        for (int i = 0; i < teamB.Count; i++)
            prepareAndSend(teamB[i], kit, arena.getTeamSpawn(teamA: false, i));

        // Snapshot the arena's pristine state on first use. Teleport above
        // has loaded the relevant chunks, so getBlockAt returns real data.
        BanditDuels.Instance.ArenaResetter?.snapshotIfNeeded(arena);

        foreach (var mp in match.Players)
            FourKit.getPlayer(mp.Name)?.sendMessage("§6[Duel] §7Match starting against §e"
                + match.opponentsLabelOf(mp.Id) + " §7on §b" + arena.Name + "§7.");

        var mode = match.TeamSize + "v" + match.TeamSize;
        FourKit.broadcastMessage("§6[Duel] §e" + Match.teamLabel(match.TeamA) + " §7vs §e"
            + Match.teamLabel(match.TeamB) + " §7on §b" + arena.Name + " §7(§f"
            + kit.DisplayName + " " + mode + "§7)");

        scheduleCountdownTick(match);
    }

    private void prepareAndSend(Player p, Kit kit, Location spawn)
    {
        prepareForFight(p);
        kit.apply(p);
        p.teleport(spawn);
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
        FourKitTask? task = null;
        task = FourKit.getScheduler().runTaskTimer(_plugin, () => countdownStep(match, task!), 20, 20);
    }

    private void countdownStep(Match match, FourKitTask task)
    {
        if (match.State != MatchState.Countdown) { task.cancel(); return; }

        var offline = match.Players.FirstOrDefault(mp => FourKit.getPlayer(mp.Name) == null);
        if (offline != null)
        {
            task.cancel();
            cancelMatch(match, "§6[Duel] §7Match cancelled because §c" + offline.Name + " §7went offline.");
            return;
        }

        if (match.CountdownSecondsRemaining > 0)
        {
            foreach (var mp in match.Players)
                FourKit.getPlayer(mp.Name)?.sendMessage("§e" + match.CountdownSecondsRemaining + "...");
            match.CountdownSecondsRemaining--;
        }
        else
        {
            foreach (var mp in match.Players)
                FourKit.getPlayer(mp.Name)?.sendMessage("§aFIGHT!");
            match.State = MatchState.Active;
            match.ActiveAt = DateTime.UtcNow;
            task.cancel();
        }
    }

    /// <summary>Called once per second by DuelHud while the match is active.</summary>
    public void tickMatch(Match match)
    {
        if (match.State != MatchState.Active) return;
        if (match.ActiveAt is not { } active) return;

        foreach (var mp in match.Players)
        {
            if (mp.Eliminated) continue;
            var p = FourKit.getPlayer(mp.Name);
            if (p != null && isOutsideArena(p, match.Arena))
            {
                endMatchByEscape(match, mp.Id);
                return;
            }
        }

        var elapsed = (DateTime.UtcNow - active).TotalSeconds;
        if (elapsed >= MatchTimeCapSeconds)
            endMatchDraw(match);
    }

    private static bool isOutsideArena(Player p, Arena arena)
    {
        var loc = p.getLocation();
        var world = loc.getWorld();
        if (world == null) return false;
        return !arena.contains(loc.getBlockX(), loc.getBlockY(), loc.getBlockZ(), world.getName());
    }

    public void endMatchByDeath(Match match, Guid loserId)
    {
        if (match.State == MatchState.Ending) return;

        var loser = match.player(loserId);
        if (loser == null || loser.Eliminated) return;
        match.eliminate(loserId);

        var winningTeam = match.winnerTeam();
        if (winningTeam.HasValue)
        {
            match.State = MatchState.Ending;
            FourKit.broadcastMessage("§6[Duel] §a" + Match.teamLabel(match.team(winningTeam.Value))
                + " §7defeated §c" + Match.teamLabel(match.opponents(winningTeam.Value))
                + " §7on §b" + match.Arena.Name + "§7.");
            recordResult(match, match.team(winningTeam.Value)[0].Id, MatchEndReason.Death);
            finalizeMatch(match);
            return;
        }

        FourKit.broadcastMessage("§6[Duel] §c" + loser.Name + " §7was eliminated from §b" + match.Arena.Name + "§7.");
        moveEliminatedOut(match, loser);
    }

    public void endMatchByForfeit(Match match, Player? maybeA, Player? maybeB)
    {
        if (match.State == MatchState.Ending) return;
        if (maybeA == null && maybeB != null) { endMatchByQuit(match, match.PlayerA); return; }
        if (maybeB == null && maybeA != null) { endMatchByQuit(match, match.PlayerB); return; }
        cancelMatch(match, "§6[Duel] §7Match cancelled on §b" + match.Arena.Name + ".");
    }

    public void endMatchByQuit(Match match, Guid leaverId)
    {
        if (match.State == MatchState.Ending) return;

        var leaver = match.player(leaverId);
        if (leaver == null || leaver.Eliminated) return;
        match.eliminate(leaverId);

        var winningTeam = match.winnerTeam();
        if (winningTeam.HasValue)
        {
            match.State = MatchState.Ending;
            FourKit.broadcastMessage("§6[Duel] §a" + Match.teamLabel(match.team(winningTeam.Value))
                + " §7wins by forfeit against §c" + Match.teamLabel(match.opponents(winningTeam.Value)) + "§7.");
            recordResult(match, match.team(winningTeam.Value)[0].Id, MatchEndReason.Quit);
            finalizeMatch(match);
            return;
        }

        FourKit.broadcastMessage("§6[Duel] §c" + leaver.Name + " §7left and was eliminated.");
    }

    public void endMatchByEscape(Match match, Guid escaperId)
    {
        if (match.State == MatchState.Ending) return;

        var escaper = match.player(escaperId);
        if (escaper == null || escaper.Eliminated) return;
        match.eliminate(escaperId);

        var winningTeam = match.winnerTeam();
        if (winningTeam.HasValue)
        {
            match.State = MatchState.Ending;
            FourKit.broadcastMessage("§6[Duel] §a" + Match.teamLabel(match.team(winningTeam.Value))
                + " §7wins - §c" + Match.teamLabel(match.opponents(winningTeam.Value)) + " §7left the arena.");
            recordResult(match, match.team(winningTeam.Value)[0].Id, MatchEndReason.Escape);
            finalizeMatch(match);
            return;
        }

        FourKit.broadcastMessage("§6[Duel] §c" + escaper.Name + " §7left the arena and was eliminated.");
        FourKit.getPlayer(escaper.Name)?.sendMessage("§cYou were eliminated by leaving the arena.");
        moveEliminatedOut(match, escaper);
    }

    public void endMatchDraw(Match match)
    {
        if (match.State == MatchState.Ending) return;
        match.State = MatchState.Ending;
        FourKit.broadcastMessage("§6[Duel] §7Match between §e" + Match.teamLabel(match.TeamA)
            + " §7and §e" + Match.teamLabel(match.TeamB) + " §7ended in a draw (time limit).");
        recordResult(match, Guid.Empty, MatchEndReason.Draw);
        finalizeMatch(match);
    }

    private void moveEliminatedOut(Match match, MatchPlayer eliminated)
    {
        FourKit.getScheduler().runTaskLater(_plugin, () =>
        {
            if (match.State == MatchState.Ending) return;
            if (!eliminated.Eliminated) return;

            var p = FourKit.getPlayer(eliminated.Name);
            if (p == null) return;

            p.getInventory().clear();
            p.setGameMode(GameMode.ADVENTURE);
            p.setHealth(20.0);
            p.setFoodLevel(20);
            p.setSaturation(20f);
            p.setExhaustion(0f);

            var spawn = BanditDuels.Instance.Lobby.getSafeSpawn();
            if (spawn != null) p.teleport(spawn);
        }, 20);
    }

    private void cancelMatch(Match match, string message)
    {
        if (match.State == MatchState.Ending) return;
        match.State = MatchState.Ending;
        FourKit.broadcastMessage(message);
        finalizeMatch(match);
    }

    /// <summary>Persist a row in stats. Team matches are skipped until the DB schema supports rosters.</summary>
    private void recordResult(Match match, Guid winnerId, string endReason)
    {
        if (match.IsTeamMatch) return;

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
        foreach (var mp in match.Players)
            _matchByPlayer.Remove(mp.Id);

        foreach (var mp in match.Players)
        {
            var p = FourKit.getPlayer(mp.Name);
            if (p != null)
            {
                p.getInventory().clear();
                mp.Snapshot.restore(p);
            }
            else
            {
                BanditDuels.Instance.PendingResets?.markPending(mp.Id);
            }
        }

        var arena = match.Arena;
        var resetter = BanditDuels.Instance.ArenaResetter;
        if (resetter != null && resetter.hasSnapshot(arena.Name))
            resetter.scheduleRestore(arena, onComplete: () => _arenas.release(arena));
        else
            _arenas.release(arena);
    }

    public void shutdown()
    {
        foreach (var match in activeMatches().ToList())
        {
            match.State = MatchState.Ending;
            finalizeMatch(match);
        }
        _requests.Clear();
    }

    private IEnumerable<string> kitIds() => _kits.all().Select(k => k.Id);
}
