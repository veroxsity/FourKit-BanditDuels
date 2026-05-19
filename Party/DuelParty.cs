namespace BanditDuels.Party;

public sealed record PartyMember(Guid PlayerId, string PlayerName);

public sealed class DuelParty
{
    public Guid LeaderId { get; private set; }
    public string LeaderName { get; private set; }
    public string KitId { get; }
    public int TeamSize { get; }
    public List<PartyMember> Members { get; } = new();
    public bool IsQueued { get; set; }

    public DuelParty(Guid leaderId, string leaderName, string kitId, int teamSize)
    {
        LeaderId = leaderId;
        LeaderName = leaderName;
        KitId = kitId;
        TeamSize = teamSize;
        Members.Add(new PartyMember(leaderId, leaderName));
    }

    public bool isFull() => Members.Count >= TeamSize;
    public bool contains(Guid playerId) => Members.Any(m => m.PlayerId == playerId);

    public void add(Guid playerId, string playerName) =>
        Members.Add(new PartyMember(playerId, playerName));

    public bool remove(Guid playerId)
    {
        var idx = Members.FindIndex(m => m.PlayerId == playerId);
        if (idx < 0) return false;

        Members.RemoveAt(idx);
        if (LeaderId == playerId && Members.Count > 0)
        {
            LeaderId = Members[0].PlayerId;
            LeaderName = Members[0].PlayerName;
        }
        return true;
    }

    public string modeLabel() => TeamSize + "v" + TeamSize;
    public string memberLabel() => string.Join(", ", Members.Select(m => m.PlayerName));
}
