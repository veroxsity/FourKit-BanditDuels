using BanditDuels.Arenas;
using BanditDuels.Kits;

namespace BanditDuels.Duels;

public sealed class MatchPlayer
{
    public Guid Id { get; }
    public string Name { get; }
    public PlayerSnapshot Snapshot { get; }
    public bool IsTeamA { get; }
    public bool Eliminated { get; set; }

    public MatchPlayer(Guid id, string name, PlayerSnapshot snapshot, bool isTeamA)
    {
        Id = id;
        Name = name;
        Snapshot = snapshot;
        IsTeamA = isTeamA;
    }
}

public sealed class Match
{
    public IReadOnlyList<MatchPlayer> TeamA { get; }
    public IReadOnlyList<MatchPlayer> TeamB { get; }
    public IReadOnlyList<MatchPlayer> Players { get; }
    public int TeamSize { get; }
    public Arena Arena { get; }
    public Kit Kit { get; }
    public DateTime StartedAt { get; }
    /// <summary>Time the match transitioned out of Countdown into Active. Null until then.</summary>
    public DateTime? ActiveAt { get; set; }
    /// <summary>Index into DuelHud.Milestones for the next message to send.</summary>
    public int NextMilestoneIndex { get; set; }
    public MatchState State { get; set; }

    /// <summary>Tick of countdown counter remaining, -1 when active.</summary>
    public int CountdownSecondsRemaining { get; set; }

    public Match(
        IReadOnlyList<MatchPlayer> teamA,
        IReadOnlyList<MatchPlayer> teamB,
        Arena arena,
        Kit kit)
    {
        TeamA = teamA;
        TeamB = teamB;
        Players = teamA.Concat(teamB).ToList();
        TeamSize = Math.Min(teamA.Count, teamB.Count);
        Arena = arena;
        Kit = kit;
        StartedAt = DateTime.UtcNow;
        State = MatchState.Countdown;
        CountdownSecondsRemaining = 3;
    }

    public Guid PlayerA => TeamA[0].Id;
    public string PlayerAName => TeamA[0].Name;
    public Guid PlayerB => TeamB[0].Id;
    public string PlayerBName => TeamB[0].Name;
    public PlayerSnapshot SnapshotA => TeamA[0].Snapshot;
    public PlayerSnapshot SnapshotB => TeamB[0].Snapshot;

    public bool IsTeamMatch => TeamSize > 1;

    public bool involves(Guid playerId) => Players.Any(p => p.Id == playerId);

    public MatchPlayer? player(Guid playerId) => Players.FirstOrDefault(p => p.Id == playerId);
    public string nameOf(Guid playerId) => player(playerId)?.Name ?? "?";

    public Guid otherOf(Guid playerId)
    {
        var mp = player(playerId);
        var opponents = mp != null && mp.IsTeamA ? TeamB : TeamA;
        return opponents[0].Id;
    }

    public string otherNameOf(Guid playerId)
    {
        var mp = player(playerId);
        var opponents = mp != null && mp.IsTeamA ? TeamB : TeamA;
        return opponents[0].Name;
    }

    public string opponentsLabelOf(Guid playerId)
    {
        var mp = player(playerId);
        return teamLabel(mp != null && mp.IsTeamA ? TeamB : TeamA);
    }

    public string teamLabelOf(Guid playerId)
    {
        var mp = player(playerId);
        return teamLabel(mp != null && mp.IsTeamA ? TeamA : TeamB);
    }

    public bool areOpponents(Guid a, Guid b)
    {
        var pa = player(a);
        var pb = player(b);
        return pa != null && pb != null && pa.IsTeamA != pb.IsTeamA && !pa.Eliminated && !pb.Eliminated;
    }

    public void eliminate(Guid playerId)
    {
        var p = player(playerId);
        if (p != null) p.Eliminated = true;
    }

    public bool isEliminated(Guid playerId) => player(playerId)?.Eliminated == true;

    public bool isTeamEliminated(bool teamA)
    {
        var team = teamA ? TeamA : TeamB;
        return team.All(p => p.Eliminated);
    }

    public bool? winnerTeam()
    {
        if (isTeamEliminated(true) && !isTeamEliminated(false)) return false;
        if (isTeamEliminated(false) && !isTeamEliminated(true)) return true;
        return null;
    }

    public IReadOnlyList<MatchPlayer> team(bool teamA) => teamA ? TeamA : TeamB;
    public IReadOnlyList<MatchPlayer> opponents(bool teamA) => teamA ? TeamB : TeamA;

    public static string teamLabel(IEnumerable<MatchPlayer> players) =>
        string.Join(", ", players.Select(p => p.Name));
}
