using Minecraft.Server.FourKit;

namespace BanditDuels.Arenas;

/// <summary>
/// A duel arena. Coordinates are world-relative.
/// boundsMin/boundsMax form an inclusive AABB used to gate block events.
/// </summary>
public sealed class Arena
{
    public string Name { get; }
    public string WorldName { get; }
    public (double x, double y, double z, float yaw, float pitch) SpawnA { get; }
    public (double x, double y, double z, float yaw, float pitch) SpawnB { get; }
    public IReadOnlyList<(double x, double y, double z, float yaw, float pitch)> TeamASpawns { get; }
    public IReadOnlyList<(double x, double y, double z, float yaw, float pitch)> TeamBSpawns { get; }
    public (int x, int y, int z) BoundsMin { get; }
    public (int x, int y, int z) BoundsMax { get; }
    public int MinTeamSize { get; }
    public int MaxTeamSize { get; }

    public Arena(
        string name,
        string worldName,
        (double x, double y, double z, float yaw, float pitch) spawnA,
        (double x, double y, double z, float yaw, float pitch) spawnB,
        IReadOnlyList<(double x, double y, double z, float yaw, float pitch)> teamASpawns,
        IReadOnlyList<(double x, double y, double z, float yaw, float pitch)> teamBSpawns,
        (int x, int y, int z) boundsMin,
        (int x, int y, int z) boundsMax,
        int minTeamSize,
        int maxTeamSize)
    {
        Name = name;
        WorldName = worldName;
        SpawnA = spawnA;
        SpawnB = spawnB;
        TeamASpawns = teamASpawns;
        TeamBSpawns = teamBSpawns;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        MinTeamSize = Math.Max(1, minTeamSize);
        MaxTeamSize = Math.Max(MinTeamSize, maxTeamSize);
    }

    public Location getSpawnA() => loc(SpawnA);
    public Location getSpawnB() => loc(SpawnB);
    public Location getTeamSpawn(bool teamA, int index) => loc((teamA ? TeamASpawns : TeamBSpawns)[index]);

    public bool supportsTeamSize(int teamSize) =>
        teamSize >= MinTeamSize
        && teamSize <= MaxTeamSize
        && TeamASpawns.Count >= teamSize
        && TeamBSpawns.Count >= teamSize;

    public bool contains(int x, int y, int z, string worldName)
    {
        if (!string.Equals(worldName, WorldName, StringComparison.Ordinal)) return false;
        return x >= BoundsMin.x && x <= BoundsMax.x
            && y >= BoundsMin.y && y <= BoundsMax.y
            && z >= BoundsMin.z && z <= BoundsMax.z;
    }

    private Location loc((double x, double y, double z, float yaw, float pitch) p)
    {
        var world = FourKit.getWorld(WorldName);
        return new Location(world, p.x, p.y, p.z, p.yaw, p.pitch);
    }
}
