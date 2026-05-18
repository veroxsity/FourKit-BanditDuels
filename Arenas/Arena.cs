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
    public (int x, int y, int z) BoundsMin { get; }
    public (int x, int y, int z) BoundsMax { get; }

    public Arena(
        string name,
        string worldName,
        (double x, double y, double z, float yaw, float pitch) spawnA,
        (double x, double y, double z, float yaw, float pitch) spawnB,
        (int x, int y, int z) boundsMin,
        (int x, int y, int z) boundsMax)
    {
        Name = name;
        WorldName = worldName;
        SpawnA = spawnA;
        SpawnB = spawnB;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    public Location getSpawnA() => loc(SpawnA);
    public Location getSpawnB() => loc(SpawnB);

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
