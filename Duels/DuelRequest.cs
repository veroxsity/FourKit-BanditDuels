namespace BanditDuels.Duels;

public sealed class DuelRequest
{
    public Guid Challenger { get; }
    public string ChallengerName { get; }
    public Guid Target { get; }
    public string TargetName { get; }
    public string KitId { get; }
    public DateTime ExpiresAt { get; }

    public DuelRequest(Guid challenger, string challengerName, Guid target, string targetName, string kitId, TimeSpan ttl)
    {
        Challenger = challenger;
        ChallengerName = challengerName;
        Target = target;
        TargetName = targetName;
        KitId = kitId;
        ExpiresAt = DateTime.UtcNow + ttl;
    }

    public bool isExpired() => DateTime.UtcNow > ExpiresAt;

    /// <summary>Stable key used in the requests map, normalized on names.</summary>
    public static string keyFor(string challengerName, string targetName)
        => challengerName.ToLowerInvariant() + ":" + targetName.ToLowerInvariant();

    public string key() => keyFor(ChallengerName, TargetName);
}
