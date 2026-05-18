namespace BanditDuels.Stats;

/// <summary>Reason a match ended; used for filtering and display.</summary>
public static class MatchEndReason
{
    public const string Death   = "death";
    public const string Forfeit = "forfeit";    // ended because a player was offline / no players
    public const string Quit    = "quit";       // a duelist disconnected mid-fight
    public const string Draw    = "draw";       // time limit reached
    public const string Escape  = "escape";     // a duelist left the arena AABB mid-fight (pearl, parkour, etc.)
}

/// <summary>One completed match. Persisted as part of the stats database.</summary>
public sealed class MatchRecord
{
    public string Id { get; set; } = "";
    public string KitId { get; set; } = "";
    public string ArenaName { get; set; } = "";

    /// <summary>UUID strings (Guid.ToString()).</summary>
    public string PlayerA { get; set; } = "";
    public string PlayerAName { get; set; } = "";
    public string PlayerB { get; set; } = "";
    public string PlayerBName { get; set; } = "";

    /// <summary>UUID string of the winner; empty for a draw.</summary>
    public string WinnerUuid { get; set; } = "";

    public string EndReason { get; set; } = "";   // see MatchEndReason

    /// <summary>ISO 8601 timestamps in UTC.</summary>
    public string StartedAt { get; set; } = "";
    public string EndedAt { get; set; } = "";

    public int DurationSeconds { get; set; }
}
