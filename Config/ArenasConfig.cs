namespace BanditDuels.Config;

/// <summary>JSON shape persisted to <c>plugins/BanditDuels-data/arenas.json</c>.</summary>
public sealed class ArenasConfig
{
    public List<ArenaTemplate> Templates { get; set; } = new();
}

public sealed class ArenaTemplate
{
    /// <summary>Arenas are named "{NamePrefix}_{n}" where n is 1..GridColumns.</summary>
    public string NamePrefix { get; set; } = "arena";

    public SpawnPoint SpawnA { get; set; } = new();
    public SpawnPoint SpawnB { get; set; } = new();

    public IntPoint BoundsMin { get; set; } = new();
    public IntPoint BoundsMax { get; set; } = new();

    /// <summary>Number of arenas to generate from this template. Use 1 for a single arena.</summary>
    public int GridColumns { get; set; } = 1;

    /// <summary>Offset between consecutive arenas. Applied to spawns and bounds.</summary>
    public int GridStrideX { get; set; } = 0;
    public int GridStrideY { get; set; } = 0;
    public int GridStrideZ { get; set; } = 0;
}

public sealed class SpawnPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float  Yaw   { get; set; }
    public float  Pitch { get; set; }
}

public sealed class IntPoint
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}
