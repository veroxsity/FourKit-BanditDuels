using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Entity;

using BanditDuels.Arenas;
using BanditDuels.Duels;
using BanditDuels.Kits;

namespace BanditDuels.Party;

public sealed class PartyManager
{
    private sealed class PartyInvite
    {
        public Guid TargetId = Guid.Empty;
        public string TargetName = "";
        public Guid LeaderId = Guid.Empty;
        public string LeaderName = "";
        public DateTime ExpiresAt;
    }

    private static readonly TimeSpan InviteTtl = TimeSpan.FromSeconds(60);

    private readonly DuelManager _duels;
    private readonly KitRegistry _kits;
    private readonly ArenaRegistry _arenas;
    private readonly Dictionary<Guid, DuelParty> _partyByPlayer = new();
    private readonly Dictionary<Guid, PartyInvite> _invitesByTarget = new();
    private readonly List<DuelParty> _waiting = new();

    public PartyManager(DuelManager duels, KitRegistry kits, ArenaRegistry arenas)
    {
        _duels = duels;
        _kits = kits;
        _arenas = arenas;
    }

    public DuelParty? getParty(Guid playerId) =>
        _partyByPlayer.TryGetValue(playerId, out var party) ? party : null;

    public bool isInParty(Guid playerId) => _partyByPlayer.ContainsKey(playerId);

    public string create(Player leader, string kitId, string mode)
    {
        if (_duels.isInMatch(leader.getUniqueId())) return "§cYou're already in a duel.";
        if (isInParty(leader.getUniqueId())) return "§cYou're already in a party. Use §f/duel party leave §cfirst.";
        if (BanditDuels.Instance.Queues?.isQueued(leader.getUniqueId()) == true)
            return "§cLeave your duel queue before creating a party.";

        var kit = _kits.get(kitId);
        if (kit == null) return "§cUnknown kit '" + kitId + "'. Try §f/duel kits§c.";

        if (!tryParseMode(mode, out var teamSize))
            return "§cMode must be §f2v2 §cor §f3v3§c.";

        if (!_arenas.all().Any(a => a.supportsTeamSize(teamSize)))
            return "§cNo " + teamSize + "v" + teamSize + " arenas are configured.";

        var party = new DuelParty(leader.getUniqueId(), leader.getName(), kit.Id, teamSize);
        _partyByPlayer[leader.getUniqueId()] = party;
        return "§6[Party] §7Created " + party.modeLabel() + " party for §f" + kit.Id
            + "§7. Invite players with §f/duel party invite <player>§7.";
    }

    public string invite(Player leader, string targetName)
    {
        var party = getParty(leader.getUniqueId());
        if (party == null) return "§cYou are not in a party. Use §f/duel party create <kit> <2v2|3v3>§c.";
        if (party.LeaderId != leader.getUniqueId()) return "§cOnly the party leader can invite players.";
        if (party.isFull()) return "§cYour party is already full.";

        var target = FourKit.getPlayer(targetName);
        if (target == null) return "§cPlayer '" + targetName + "' is not online.";
        if (target.getUniqueId() == leader.getUniqueId()) return "§cYou are already in your party.";
        if (_duels.isInMatch(target.getUniqueId())) return "§cThat player is already in a duel.";
        if (isInParty(target.getUniqueId())) return "§cThat player is already in a party.";
        if (BanditDuels.Instance.Queues?.isQueued(target.getUniqueId()) == true)
            return "§cThat player is already in a duel queue.";

        _invitesByTarget[target.getUniqueId()] = new PartyInvite
        {
            TargetId = target.getUniqueId(),
            TargetName = target.getName(),
            LeaderId = party.LeaderId,
            LeaderName = party.LeaderName,
            ExpiresAt = DateTime.UtcNow + InviteTtl,
        };

        target.sendMessage("§6[Party] §e" + leader.getName() + " §7invited you to a §f"
            + party.modeLabel() + " " + party.KitId + " §7party.");
        target.sendMessage("§7Use §f/duel party accept " + leader.getName()
            + " §7or §f/duel party decline " + leader.getName() + "§7.");

        return "§6[Party] §7Invite sent to §e" + target.getName() + "§7.";
    }

    public string accept(Player target, string? leaderName)
    {
        if (!_invitesByTarget.TryGetValue(target.getUniqueId(), out var invite))
            return "§cYou do not have a pending party invite.";

        if (invite.ExpiresAt < DateTime.UtcNow)
        {
            _invitesByTarget.Remove(target.getUniqueId());
            return "§cThat party invite has expired.";
        }

        if (leaderName != null && !invite.LeaderName.Equals(leaderName, StringComparison.OrdinalIgnoreCase))
            return "§cYou do not have a party invite from '" + leaderName + "'.";

        var party = getParty(invite.LeaderId);
        if (party == null)
        {
            _invitesByTarget.Remove(target.getUniqueId());
            return "§cThat party no longer exists.";
        }

        if (party.isFull())
        {
            _invitesByTarget.Remove(target.getUniqueId());
            return "§cThat party is already full.";
        }

        if (_duels.isInMatch(target.getUniqueId())) return "§cYou're already in a duel.";
        if (isInParty(target.getUniqueId())) return "§cYou're already in a party.";
        if (BanditDuels.Instance.Queues?.isQueued(target.getUniqueId()) == true)
            return "§cLeave your duel queue before accepting a party invite.";

        dequeue(party);
        _invitesByTarget.Remove(target.getUniqueId());
        party.add(target.getUniqueId(), target.getName());
        _partyByPlayer[target.getUniqueId()] = party;

        removeInvitesFor(target.getUniqueId());

        broadcast(party, "§6[Party] §e" + target.getName() + " §7joined the party ("
            + party.Members.Count + "/" + party.TeamSize + ").");

        tryQueueOrMatch(party);
        return "§6[Party] §7Joined §e" + party.LeaderName + "§7's party.";
    }

    public string decline(Player target, string? leaderName)
    {
        if (!_invitesByTarget.TryGetValue(target.getUniqueId(), out var invite))
            return "§cYou do not have a pending party invite.";

        if (leaderName != null && !invite.LeaderName.Equals(leaderName, StringComparison.OrdinalIgnoreCase))
            return "§cYou do not have a party invite from '" + leaderName + "'.";

        _invitesByTarget.Remove(target.getUniqueId());
        FourKit.getPlayer(invite.LeaderName)?.sendMessage("§6[Party] §e" + target.getName() + " §7declined your party invite.");
        return "§7Declined party invite from §e" + invite.LeaderName + "§7.";
    }

    public string leave(Player player)
    {
        var party = getParty(player.getUniqueId());
        if (party == null) return "§cYou're not in a party.";

        if (party.LeaderId == player.getUniqueId())
            return disband(player);

        dequeue(party);
        party.remove(player.getUniqueId());
        _partyByPlayer.Remove(player.getUniqueId());
        removeInvitesFor(player.getUniqueId());

        broadcast(party, "§6[Party] §e" + player.getName() + " §7left the party.");
        return "§7Left your party.";
    }

    public string disband(Player player)
    {
        var party = getParty(player.getUniqueId());
        if (party == null) return "§cYou're not in a party.";
        if (party.LeaderId != player.getUniqueId()) return "§cOnly the party leader can disband the party.";

        dequeue(party);
        removeInvitesFor(party);
        foreach (var member in party.Members)
            _partyByPlayer.Remove(member.PlayerId);

        broadcast(party, "§6[Party] §7Party disbanded.");
        return "§7Disbanded your party.";
    }

    public string remove(Player leader, string targetName)
    {
        var party = getParty(leader.getUniqueId());
        if (party == null) return "§cYou're not in a party.";
        if (party.LeaderId != leader.getUniqueId()) return "§cOnly the party leader can remove players.";

        var member = party.Members.FirstOrDefault(m => m.PlayerName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (member == null) return "§cThat player is not in your party.";
        if (member.PlayerId == leader.getUniqueId()) return "§cUse §f/duel party disband §cto close the party.";

        dequeue(party);
        party.remove(member.PlayerId);
        _partyByPlayer.Remove(member.PlayerId);
        removeInvitesFor(member.PlayerId);
        FourKit.getPlayer(member.PlayerName)?.sendMessage("§6[Party] §7You were removed from the party.");
        broadcast(party, "§6[Party] §e" + member.PlayerName + " §7was removed from the party.");
        return "§7Removed §e" + member.PlayerName + "§7.";
    }

    public void sendInfo(Player player)
    {
        var party = getParty(player.getUniqueId());
        if (party == null)
        {
            player.sendMessage("§cYou're not in a party.");
            return;
        }

        player.sendMessage("§6[Party]");
        player.sendMessage("§7Leader: " + party.LeaderName);
        player.sendMessage("§7Kit: " + party.KitId);
        player.sendMessage("§7Mode: " + party.modeLabel());
        player.sendMessage("§7Members (" + party.Members.Count + "/" + party.TeamSize + "):");
        foreach (var member in party.Members)
            player.sendMessage("§7- " + member.PlayerName);
        player.sendMessage("§7Status: " + (party.IsQueued ? "waiting for opponent party" : "forming"));
    }

    public void removeOnQuit(Player player)
    {
        var party = getParty(player.getUniqueId());
        if (party == null) return;

        if (party.LeaderId == player.getUniqueId())
        {
            dequeue(party);
            removeInvitesFor(party);
            foreach (var member in party.Members)
                _partyByPlayer.Remove(member.PlayerId);
            broadcast(party, "§6[Party] §7Party disbanded because the leader left.");
            return;
        }

        dequeue(party);
        party.remove(player.getUniqueId());
        _partyByPlayer.Remove(player.getUniqueId());
        removeInvitesFor(player.getUniqueId());
        broadcast(party, "§6[Party] §e" + player.getName() + " §7left the game and was removed from the party.");
    }

    private void tryQueueOrMatch(DuelParty party)
    {
        if (!party.isFull()) return;

        var opponent = _waiting.FirstOrDefault(p =>
            !ReferenceEquals(p, party)
            && p.TeamSize == party.TeamSize
            && p.KitId.Equals(party.KitId, StringComparison.OrdinalIgnoreCase)
            && p.isFull());

        if (opponent == null)
        {
            enqueue(party);
            broadcast(party, "§6[Party] §7Party full. Waiting for another §f" + party.modeLabel()
                + " " + party.KitId + " §7party.");
            return;
        }

        var teamA = onlinePlayers(party);
        var teamB = onlinePlayers(opponent);
        if (teamA.Count != party.TeamSize || teamB.Count != opponent.TeamSize)
        {
            cleanupOffline(party);
            cleanupOffline(opponent);
            return;
        }

        dequeue(party);
        dequeue(opponent);

        if (_duels.tryStartMatchFromQueue(teamA, teamB, party.KitId, out var error))
        {
            removeInvitesFor(party);
            removeInvitesFor(opponent);
            clearParty(party);
            clearParty(opponent);
            return;
        }

        enqueue(opponent);
        enqueue(party);
        broadcast(party, "§e[Party] §7Could not start yet: " + error);
        broadcast(opponent, "§e[Party] §7Could not start yet: " + error);
    }

    private void cleanupOffline(DuelParty party)
    {
        dequeue(party);
        foreach (var member in party.Members.ToList())
        {
            if (FourKit.getPlayer(member.PlayerName) != null) continue;
            party.remove(member.PlayerId);
            _partyByPlayer.Remove(member.PlayerId);
        }
    }

    private List<Player> onlinePlayers(DuelParty party) =>
        party.Members
            .Select(m => FourKit.getPlayer(m.PlayerName))
            .Where(p => p != null)
            .Cast<Player>()
            .ToList();

    private void enqueue(DuelParty party)
    {
        if (!_waiting.Contains(party)) _waiting.Add(party);
        party.IsQueued = true;
    }

    private void dequeue(DuelParty party)
    {
        _waiting.Remove(party);
        party.IsQueued = false;
    }

    private void clearParty(DuelParty party)
    {
        foreach (var member in party.Members)
            _partyByPlayer.Remove(member.PlayerId);
        party.IsQueued = false;
    }

    private void removeInvitesFor(DuelParty party)
    {
        foreach (var invite in _invitesByTarget.Values
                     .Where(i => i.LeaderId == party.LeaderId || party.contains(i.TargetId))
                     .ToList())
            _invitesByTarget.Remove(invite.TargetId);
    }

    private void removeInvitesFor(Guid playerId)
    {
        foreach (var invite in _invitesByTarget.Values
                     .Where(i => i.LeaderId == playerId || i.TargetId == playerId)
                     .ToList())
            _invitesByTarget.Remove(invite.TargetId);
    }

    private static bool tryParseMode(string mode, out int teamSize)
    {
        var normalized = mode.Trim().ToLowerInvariant();
        if (normalized == "2" || normalized == "2v2") { teamSize = 2; return true; }
        if (normalized == "3" || normalized == "3v3") { teamSize = 3; return true; }
        teamSize = 0;
        return false;
    }

    private static void broadcast(DuelParty party, string message)
    {
        foreach (var member in party.Members)
            FourKit.getPlayer(member.PlayerName)?.sendMessage(message);
    }
}
