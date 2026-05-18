namespace BanditDuels.Stats;

/// <summary>JSON shape on disk: match log + a name index for offline player lookups.</summary>
public sealed class StatsDatabase
{
    public List<MatchRecord> Matches { get; set; } = new();
    /// <summary>uuid string -> latest known display name (so /duel stats &lt;name&gt; works for offline players).</summary>
    public Dictionary<string, string> PlayerNames { get; set; } = new();
}

/// <summary>Aggregated stats for a single player, computed from the match log.</summary>
public sealed class PlayerStats
{
    public string Uuid { get; init; } = "";
    public string Name { get; init; } = "";

    public int TotalMatches { get; set; }
    public int Wins   { get; set; }
    public int Losses { get; set; }
    public int Draws  { get; set; }

    public double WinRate => TotalMatches == 0 ? 0.0 : (double)Wins / TotalMatches;

    /// <summary>kit id -> (wins, losses, draws)</summary>
    public Dictionary<string, (int Wins, int Losses, int Draws)> ByKit { get; } = new();

    public void recordWin (string kitId) { Wins++;   TotalMatches++; var v = ByKit.GetValueOrDefault(kitId); ByKit[kitId] = (v.Wins + 1, v.Losses, v.Draws); }
    public void recordLoss(string kitId) { Losses++; TotalMatches++; var v = ByKit.GetValueOrDefault(kitId); ByKit[kitId] = (v.Wins, v.Losses + 1, v.Draws); }
    public void recordDraw(string kitId) { Draws++;  TotalMatches++; var v = ByKit.GetValueOrDefault(kitId); ByKit[kitId] = (v.Wins, v.Losses, v.Draws + 1); }
}

/// <summary>Leaderboard row used for /duel top.</summary>
public sealed class LeaderboardEntry
{
    public string Uuid { get; init; } = "";
    public string Name { get; init; } = "";
    public int Wins { get; init; }
    public int TotalMatches { get; init; }
    public double WinRate => TotalMatches == 0 ? 0.0 : (double)Wins / TotalMatches;
}
