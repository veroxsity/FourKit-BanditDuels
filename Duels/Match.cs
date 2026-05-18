using BanditDuels.Arenas;
using BanditDuels.Kits;

namespace BanditDuels.Duels;

public sealed class Match
{
    public Guid PlayerA { get; }
    public string PlayerAName { get; }
    public Guid PlayerB { get; }
    public string PlayerBName { get; }
    public Arena Arena { get; }
    public Kit Kit { get; }
    public PlayerSnapshot SnapshotA { get; }
    public PlayerSnapshot SnapshotB { get; }
    public DateTime StartedAt { get; }
    /// <summary>Time the match transitioned out of Countdown into Active. Null until then.</summary>
    public DateTime? ActiveAt { get; set; }
    /// <summary>Index into DuelHud.Milestones for the next message to send.</summary>
    public int NextMilestoneIndex { get; set; }
    public MatchState State { get; set; }

    /// <summary>Tick of countdown counter remaining, -1 when active.</summary>
    public int CountdownSecondsRemaining { get; set; }

    public Match(
        Guid a, string aName, PlayerSnapshot snapA,
        Guid b, string bName, PlayerSnapshot snapB,
        Arena arena, Kit kit)
    {
        PlayerA = a; PlayerAName = aName; SnapshotA = snapA;
        PlayerB = b; PlayerBName = bName; SnapshotB = snapB;
        Arena = arena; Kit = kit;
        StartedAt = DateTime.UtcNow;
        State = MatchState.Countdown;
        CountdownSecondsRemaining = 3;
    }

    public bool involves(Guid playerId) => playerId == PlayerA || playerId == PlayerB;

    public Guid otherOf(Guid playerId) => playerId == PlayerA ? PlayerB : PlayerA;
    public string otherNameOf(Guid playerId) => playerId == PlayerA ? PlayerBName : PlayerAName;

    public string nameOf(Guid playerId) => playerId == PlayerA ? PlayerAName : PlayerBName;
}
