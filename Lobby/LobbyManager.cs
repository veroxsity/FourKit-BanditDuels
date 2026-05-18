using System.Text.Json;

using Minecraft.Server.FourKit;

namespace BanditDuels.Lobby;

/// <summary>
/// In-memory lobby state. Holds the safe spawn point and bounding AABB.
/// Persists to <c>plugins/BanditDuels-data/lobby.json</c>.
/// </summary>
public sealed class LobbyManager
{
    public const string DataFolder = "plugins/BanditDuels-data";
    public const string ConfigFile = "lobby.json";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public LobbyConfig Config { get; private set; } = new();

    public bool HasSpawn  => Config.HasSpawn;
    public bool HasBounds => Config.HasBounds;
    public bool IsConfigured => HasSpawn && HasBounds;

    public void load()
    {
        var path = Path.Combine(DataFolder, ConfigFile);
        if (!File.Exists(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            var cfg = JsonSerializer.Deserialize<LobbyConfig>(stream, JsonOpts);
            if (cfg != null) Config = cfg;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] failed to load lobby config: " + ex.Message);
        }
    }

    public void save()
    {
        Directory.CreateDirectory(DataFolder);
        var path = Path.Combine(DataFolder, ConfigFile);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, Config, JsonOpts);
    }

    public void setSpawn(string worldName, double x, double y, double z, float yaw, float pitch)
    {
        Config.WorldName  = worldName;
        Config.SpawnX     = x;
        Config.SpawnY     = y;
        Config.SpawnZ     = z;
        Config.SpawnYaw   = yaw;
        Config.SpawnPitch = pitch;
        Config.HasSpawn   = true;
        save();
    }

    public void setBounds(string worldName, int x1, int y1, int z1, int x2, int y2, int z2)
    {
        Config.WorldName  = worldName;
        Config.BoundsMinX = Math.Min(x1, x2);
        Config.BoundsMinY = Math.Min(y1, y2);
        Config.BoundsMinZ = Math.Min(z1, z2);
        Config.BoundsMaxX = Math.Max(x1, x2);
        Config.BoundsMaxY = Math.Max(y1, y2);
        Config.BoundsMaxZ = Math.Max(z1, z2);
        Config.HasBounds  = true;
        save();
    }

    /// <summary>True if the given world+block coords are inside the lobby AABB.</summary>
    public bool isInside(string worldName, int x, int y, int z)
    {
        if (!HasBounds) return true; // no bounds set -> nothing to enforce
        if (!string.Equals(worldName, Config.WorldName, StringComparison.Ordinal)) return false;
        return x >= Config.BoundsMinX && x <= Config.BoundsMaxX
            && y >= Config.BoundsMinY && y <= Config.BoundsMaxY
            && z >= Config.BoundsMinZ && z <= Config.BoundsMaxZ;
    }

    /// <summary>Returns a Location representing the safe spawn, or null if no spawn is configured.</summary>
    public Location? getSafeSpawn()
    {
        if (!HasSpawn) return null;
        var world = FourKit.getWorld(Config.WorldName);
        if (world == null) return null;
        return new Location(world, Config.SpawnX, Config.SpawnY, Config.SpawnZ,
                            Config.SpawnYaw, Config.SpawnPitch);
    }
}
