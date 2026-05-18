namespace BanditDuels.Lobby;

/// <summary>JSON shape persisted to <c>plugins/BanditDuels-data/lobby.json</c>.</summary>
public sealed class LobbyConfig
{
    public string WorldName { get; set; } = "world";

    public double SpawnX { get; set; }
    public double SpawnY { get; set; }
    public double SpawnZ { get; set; }
    public float  SpawnYaw   { get; set; }
    public float  SpawnPitch { get; set; }

    public int BoundsMinX { get; set; }
    public int BoundsMinY { get; set; }
    public int BoundsMinZ { get; set; }
    public int BoundsMaxX { get; set; }
    public int BoundsMaxY { get; set; }
    public int BoundsMaxZ { get; set; }

    public bool HasSpawn  { get; set; }
    public bool HasBounds { get; set; }
}
