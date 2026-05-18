using System.Text.Json;

namespace BanditDuels.Config;

/// <summary>
/// Loads <see cref="PluginConfig"/> and <see cref="ArenasConfig"/> from
/// <c>plugins/BanditDuels-data/</c>. Writes defaults on first run so admins
/// can edit JSON files without recompiling the plugin.
/// </summary>
public static class ConfigStore
{
    public const string DataFolder = "plugins/BanditDuels-data";
    public const string PluginFile = "config.json";
    public const string ArenasFile = "arenas.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        IncludeFields = false,
    };

    // ---- PluginConfig ----

    public static PluginConfig loadPluginConfig()
    {
        var path = Path.Combine(DataFolder, PluginFile);
        if (!File.Exists(path))
        {
            var defaults = new PluginConfig();
            savePluginConfig(defaults);
            Console.WriteLine("[BanditDuels] wrote default config.json");
            return defaults;
        }
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<PluginConfig>(stream, JsonOpts) ?? new PluginConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] failed to parse config.json: " + ex.Message + " (using defaults)");
            return new PluginConfig();
        }
    }

    public static void savePluginConfig(PluginConfig cfg)
    {
        Directory.CreateDirectory(DataFolder);
        var path = Path.Combine(DataFolder, PluginFile);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, cfg, JsonOpts);
    }

    // ---- ArenasConfig ----

    public static ArenasConfig loadArenasConfig()
    {
        var path = Path.Combine(DataFolder, ArenasFile);
        if (!File.Exists(path))
        {
            var defaults = makeExampleArenasConfig();
            saveArenasConfig(defaults);
            Console.WriteLine("[BanditDuels] wrote example arenas.json (edit it for your world)");
            return defaults;
        }
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<ArenasConfig>(stream, JsonOpts) ?? new ArenasConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] failed to parse arenas.json: " + ex.Message + " (using empty list)");
            return new ArenasConfig();
        }
    }

    public static void saveArenasConfig(ArenasConfig cfg)
    {
        Directory.CreateDirectory(DataFolder);
        var path = Path.Combine(DataFolder, ArenasFile);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, cfg, JsonOpts);
    }

    /// <summary>
    /// Example config used when arenas.json doesn't exist. Matches the
    /// BanditVault layout (forest + ice in a 5-column grid). Server admins
    /// are expected to replace these values with coords from their own world.
    /// </summary>
    private static ArenasConfig makeExampleArenasConfig() => new()
    {
        Templates = new List<ArenaTemplate>
        {
            new ArenaTemplate
            {
                NamePrefix = "forest",
                SpawnA    = new SpawnPoint { X = 64.5, Y = 5.6, Z = 11.5, Yaw =   0f, Pitch = 0f },
                SpawnB    = new SpawnPoint { X = 63.5, Y = 5.6, Z = 56.5, Yaw = 180f, Pitch = 0f },
                BoundsMin = new IntPoint { X = 33, Y = 2, Z =  0 },
                BoundsMax = new IntPoint { X = 92, Y = 26, Z = 67 },
                GridColumns = 5,
                GridStrideX = 68,
            },
            new ArenaTemplate
            {
                NamePrefix = "ice",
                SpawnA    = new SpawnPoint { X = 63.0, Y = 6.5, Z =  86.5, Yaw =   0f, Pitch = 0f },
                SpawnB    = new SpawnPoint { X = 63.0, Y = 6.5, Z = 123.5, Yaw = 180f, Pitch = 0f },
                BoundsMin = new IntPoint { X = 32, Y = 2, Z =  75 },
                BoundsMax = new IntPoint { X = 93, Y = 27, Z = 134 },
                GridColumns = 5,
                GridStrideX = 68,
            },
        },
    };
}
