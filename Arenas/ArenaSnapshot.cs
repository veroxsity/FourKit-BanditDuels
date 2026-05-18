namespace BanditDuels.Arenas;

/// <summary>
/// Captured block state of an arena's AABB at a point in time. Flat arrays
/// indexed by relative position so we can iterate linearly during restore.
///
/// Storage layout: indexed as <c>(ry * Depth + rz) * Width + rx</c> where
/// rx/ry/rz are the offsets from <see cref="Origin"/>. Iterating by raw
/// flat index advances Z fastest, then X, then Y - matches the order used
/// during capture so we can also pull the (rx, ry, rz) from the flat index
/// when needed.
///
/// Memory: 5 bytes per block (int typeId + byte data). For a typical
/// 60x25x68 arena (~102k blocks) that's ~500 KB per snapshot.
/// </summary>
public sealed class ArenaSnapshot
{
    public string WorldName { get; }
    public (int x, int y, int z) Origin { get; }
    public int Width  { get; }   // x span
    public int Height { get; }   // y span
    public int Depth  { get; }   // z span

    public int[]  TypeIds { get; }
    public byte[] Datas   { get; }

    public int TotalBlocks => Width * Height * Depth;

    public ArenaSnapshot(string worldName, (int x, int y, int z) origin,
                         int width, int height, int depth)
    {
        WorldName = worldName;
        Origin    = origin;
        Width     = width;
        Height    = height;
        Depth     = depth;
        TypeIds   = new int[width * height * depth];
        Datas     = new byte[width * height * depth];
    }

    public int indexOf(int rx, int ry, int rz) =>
        (ry * Depth + rz) * Width + rx;
}
