using BanditDuels.Config;

namespace BanditDuels.Arenas;

public sealed class ArenaRegistry
{
    private readonly List<Arena> _arenas = new();
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The templates that produced the current arena list. Used by /duel admin setup.</summary>
    public IReadOnlyList<ArenaTemplate> Templates { get; private set; } = Array.Empty<ArenaTemplate>();

    /// <summary>Build the arena list from a config. Each template generates
    /// <see cref="ArenaTemplate.GridColumns"/> arenas at increasing strides.</summary>
    public void loadFromConfig(string worldName, IReadOnlyList<ArenaTemplate> templates)
    {
        _arenas.Clear();
        Templates = templates;
        foreach (var t in templates)
        {
            int cols = Math.Max(1, t.GridColumns);
            for (int col = 0; col < cols; col++)
            {
                int dx = col * t.GridStrideX;
                int dy = col * t.GridStrideY;
                int dz = col * t.GridStrideZ;
                _arenas.Add(makeFromTemplate(t, col + 1, worldName, dx, dy, dz));
            }
        }
    }

    private static Arena makeFromTemplate(ArenaTemplate t, int n, string worldName, int dx, int dy, int dz) => new Arena(
        name: t.NamePrefix + "_" + n,
        worldName: worldName,
        spawnA: (
            x: t.SpawnA.X + dx, y: t.SpawnA.Y + dy, z: t.SpawnA.Z + dz,
            yaw: t.SpawnA.Yaw, pitch: t.SpawnA.Pitch),
        spawnB: (
            x: t.SpawnB.X + dx, y: t.SpawnB.Y + dy, z: t.SpawnB.Z + dz,
            yaw: t.SpawnB.Yaw, pitch: t.SpawnB.Pitch),
        boundsMin: (t.BoundsMin.X + dx, t.BoundsMin.Y + dy, t.BoundsMin.Z + dz),
        boundsMax: (t.BoundsMax.X + dx, t.BoundsMax.Y + dy, t.BoundsMax.Z + dz));

    /// <summary>
    /// Reserve any currently-free arena and return it, or null if all are
    /// busy. Selection is uniform-random across the free set so a player
    /// joining the queue could be sent to any unoccupied arena - not always
    /// the first-registered template. Without this, forest_1..5 would
    /// always fill before any ice_* arena saw use, because templates are
    /// expanded in declaration order and the previous first-fit walk
    /// picked the lowest-index free entry every time.
    ///
    /// Random.Shared is thread-safe (.NET 6+) but acquireFree() should
    /// only be called from the main FourKit thread anyway (event handlers
    /// and the scheduler) - the threading note is just future-proofing.
    /// </summary>
    public Arena? acquireFree()
    {
        // Collect the indices of every currently-free arena. Allocating a
        // small list here is fine: it lives for one stack frame and the
        // arena list is small (handful of templates x a few grid columns).
        var freeIndices = new List<int>(_arenas.Count);
        for (int i = 0; i < _arenas.Count; i++)
        {
            if (!_busy.Contains(_arenas[i].Name)) freeIndices.Add(i);
        }
        if (freeIndices.Count == 0) return null;

        int pick = freeIndices[Random.Shared.Next(freeIndices.Count)];
        var arena = _arenas[pick];
        _busy.Add(arena.Name);
        return arena;
    }

    public void release(Arena arena) => _busy.Remove(arena.Name);

    public Arena? findContaining(int x, int y, int z, string worldName)
    {
        foreach (var a in _arenas)
            if (a.contains(x, y, z, worldName)) return a;
        return null;
    }

    public Arena? findByName(string name)
    {
        foreach (var a in _arenas)
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                return a;
        return null;
    }

    public IReadOnlyList<Arena> all() => _arenas;
    public int count() => _arenas.Count;
}
