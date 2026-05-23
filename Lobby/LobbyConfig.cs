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

    /// <summary>
    /// When true (default), LobbyGuardListener cancels block break / place /
    /// interact / drop events inside the lobby AABB for non-admin players.
    /// Persisted here so the toggle survives restarts. Set to false when
    /// delegating lobby block protection to another plugin (e.g. WorldGuard
    /// region flags). The boundary teleport-back and comfort features
    /// (health / food / fall-damage) stay on regardless of this flag.
    /// </summary>
    public bool BlockProtectionEnabled { get; set; } = true;

    /// <summary>
    /// When true (default), LobbyGuardListener's periodic tick teleports
    /// non-duelling non-admin players back to lobby spawn whenever it
    /// detects them outside the lobby AABB. Set to false when WorldGuard's
    /// exit=deny flag (or any other plugin) is handling boundary
    /// enforcement, to avoid duplicate teleport-backs.
    /// Comfort features (health / food / saturation top-up, fall damage
    /// cancel) keep running regardless.
    /// </summary>
    public bool BoundaryEnforcementEnabled { get; set; } = true;
}
