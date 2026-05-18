using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Scheduler;
using FKWorld = Minecraft.Server.FourKit.World;

namespace BanditDuels.Arenas;

/// <summary>
/// Holds one <see cref="ArenaSnapshot"/> per arena and provides hooks the
/// DuelManager calls at match start (snapshot if needed) and match end
/// (schedule batched restore). The snapshot is taken lazily on the FIRST
/// match in each arena: the arena's blocks at that moment are treated as
/// the pristine "canonical" state to restore to after every subsequent
/// match.
///
/// Snapshots are persisted to disk under
/// <c>plugins/BanditDuels-data/arena-snapshots/&lt;arenaName&gt;.bin</c>
/// so they survive server restarts (including crash mid-match). Format
/// is a small binary header followed by the typeId/data arrays:
///
/// <code>
/// offset  size  field
/// 0       4     magic "BDA1"
/// 4       1     version (currently 1)
/// 5       1     worldNameLen (UTF-8 byte count, max 255)
/// 6       N     worldName (UTF-8)
/// 6+N     4     originX (Int32 LE)
/// ...     4     originY
/// ...     4     originZ
/// ...     4     width
/// ...     4     height
/// ...     4     depth
/// ...     w*h*d*4  typeIds (Int32 LE each)
/// ...     w*h*d    datas (byte each)
/// </code>
///
/// Why lazily and not at startup:
/// - Plugin enable runs before /duel admin setup has cloned arenas, so a
///   cold-start snapshot would capture empty grids for forest_2..N.
/// - Arenas added later still get proper snapshots without an explicit
///   refresh command.
///
/// Restore is batched at 4000 blocks/tick so a ~100k-block arena reset
/// takes ~25 ticks (~1.25s) instead of stalling the main thread for ~100ms.
/// The arena stays busy in <see cref="ArenaRegistry"/> until the restore
/// completes - we run the release as an <c>onComplete</c> callback - so
/// the next match can't grab it mid-reset.
///
/// IMPORTANT: scheduling new <c>runTaskTimer</c> calls from inside a
/// scheduled callback crashes FourKit's scheduler (it iterates its task
/// dict directly and chokes on mid-iteration mutation). <c>finalizeMatch</c>
/// runs from inside <c>DuelHud.tick</c> when matches end by time-cap draw
/// or escape forfeit, which IS scheduled-callback context. So instead of
/// scheduling a fresh task per restore, we register ONE persistent worker
/// at plugin enable (via <see cref="start"/>) and <see cref="scheduleRestore"/>
/// just appends jobs to a list the worker drains each tick. Same throughput,
/// no mid-iteration mutation.
/// </summary>
public sealed class ArenaResetManager
{
    private const int BlocksPerTick = 4000;

    public const string SnapshotsFolder = "plugins/BanditDuels-data/arena-snapshots";
    private static readonly byte[] FileMagic = new byte[] { (byte)'B', (byte)'D', (byte)'A', (byte)'1' };
    private const byte FileVersion = 1;

    private readonly Dictionary<string, ArenaSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One in-flight restore. Iteration state lives here so the
    /// worker tick can pick up where the previous tick left off.</summary>
    private sealed class RestoreJob
    {
        public Arena         Arena         = null!;
        public ArenaSnapshot Snap          = null!;
        public int           Progress;
        public int           Changed;
        public bool          ChunksPreloaded;
        public Action?       OnComplete;
    }

    private readonly List<RestoreJob> _pendingRestores = new();

    /// <summary>
    /// Register the per-tick worker that drains <see cref="_pendingRestores"/>.
    /// MUST be called once from <see cref="BanditDuels.onEnable"/> - that's
    /// event-handler context, which is safe to schedule from. NEVER call
    /// this from inside any scheduled callback.
    /// </summary>
    public void start(BanditDuels plugin)
    {
        FourKit.getScheduler().runTaskTimer(plugin, processPendingTick, 1, 1);
    }

    public bool hasSnapshot(string arenaName) => _snapshots.ContainsKey(arenaName);

    /// <summary>
    /// Load all .bin snapshots from disk and validate each against the
    /// current arena registry. Stale snapshots (different bounds/world
    /// than the configured arena) are skipped and will be re-snapshotted
    /// on first match. Orphan snapshots with no matching arena are left
    /// on disk but not loaded - removing an arena from config shouldn't
    /// destroy its persisted state.
    /// </summary>
    public void loadAllFromDisk(ArenaRegistry arenas)
    {
        if (!Directory.Exists(SnapshotsFolder)) return;

        int loaded = 0, stale = 0, orphan = 0, failed = 0;
        foreach (var path in Directory.EnumerateFiles(SnapshotsFolder, "*.bin"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var arena = arenas.findByName(name);
            if (arena == null) { orphan++; continue; }

            var snap = tryLoad(path);
            if (snap == null) { failed++; continue; }

            // Validate against current arena bounds. If they don't match,
            // the arena was redefined in arenas.json; treat snapshot as
            // stale and let the next match re-snapshot from the live world.
            var min = arena.BoundsMin;
            var max = arena.BoundsMax;
            int expectedW = max.x - min.x + 1;
            int expectedH = max.y - min.y + 1;
            int expectedD = max.z - min.z + 1;

            if (!string.Equals(snap.WorldName, arena.WorldName, StringComparison.Ordinal)
                || snap.Origin != min
                || snap.Width  != expectedW
                || snap.Height != expectedH
                || snap.Depth  != expectedD)
            {
                Console.WriteLine("[ArenaResetManager] stale snapshot for "
                    + name + " (bounds/world changed), will re-snapshot on next match.");
                stale++;
                continue;
            }

            _snapshots[arena.Name] = snap;
            loaded++;
        }

        if (loaded + stale + orphan + failed > 0)
            Console.WriteLine("[ArenaResetManager] loaded " + loaded + " snapshots from disk"
                + (stale  > 0 ? ", " + stale  + " stale"   : "")
                + (orphan > 0 ? ", " + orphan + " orphans" : "")
                + (failed > 0 ? ", " + failed + " failed"  : "")
                + ".");
    }

    /// <summary>
    /// Capture the arena's current block state if we haven't already. Safe
    /// to call every match start; will only do work on the first call per
    /// arena. Pre-loads all chunks within the arena AABB so getBlockAt
    /// returns real data rather than empty placeholders. Persists the
    /// resulting snapshot to disk synchronously so a crash before the
    /// next match doesn't lose the pristine state.
    /// </summary>
    public void snapshotIfNeeded(Arena arena)
    {
        if (_snapshots.ContainsKey(arena.Name)) return;

        var world = FourKit.getWorld(arena.WorldName);
        if (world == null)
        {
            Console.WriteLine("[ArenaResetManager] world '" + arena.WorldName
                + "' not found, skipping snapshot of " + arena.Name);
            return;
        }

        var min = arena.BoundsMin;
        var max = arena.BoundsMax;
        int width  = max.x - min.x + 1;
        int height = max.y - min.y + 1;
        int depth  = max.z - min.z + 1;

        preloadChunks(world, min, max);

        var snap = new ArenaSnapshot(arena.WorldName, min, width, height, depth);

        // Iterate in the same flat-index order used by indexOf() so we can
        // write directly to TypeIds[idx]/Datas[idx] without recomputing.
        int idx = 0;
        for (int ry = 0; ry < height; ry++)
        for (int rz = 0; rz < depth;  rz++)
        for (int rx = 0; rx < width;  rx++)
        {
            var block = world.getBlockAt(min.x + rx, min.y + ry, min.z + rz);
            snap.TypeIds[idx] = block.getTypeId();
            snap.Datas[idx]   = block.getData();
            idx++;
        }

        _snapshots[arena.Name] = snap;
        Console.WriteLine("[ArenaResetManager] snapshotted " + arena.Name
            + " (" + snap.TotalBlocks + " blocks).");

        saveToDisk(arena.Name, snap);
    }

    /// <summary>
    /// Queue a batched restore of the arena to its captured snapshot. The
    /// actual block writes happen on the persistent worker tick registered
    /// by <see cref="start"/>; this method just appends to that worker's
    /// queue. Safe to call from anywhere, including scheduled callbacks.
    ///
    /// If no snapshot exists (snapshotIfNeeded was never called or failed),
    /// the onComplete callback runs immediately so the caller can release
    /// the arena without waiting.
    /// </summary>
    public void scheduleRestore(Arena arena, Action? onComplete = null)
    {
        if (!_snapshots.TryGetValue(arena.Name, out var snap))
        {
            onComplete?.Invoke();
            return;
        }

        _pendingRestores.Add(new RestoreJob
        {
            Arena      = arena,
            Snap       = snap,
            Progress   = 0,
            Changed    = 0,
            OnComplete = onComplete,
        });
    }

    /// <summary>
    /// Per-tick worker that advances every pending restore by
    /// <see cref="BlocksPerTick"/> blocks. Multiple jobs run in parallel
    /// (one tick advances all of them) so back-to-back match endings don't
    /// queue up; the only contention is the per-tick total block-write
    /// budget.
    ///
    /// Iterating in reverse with <c>RemoveAt(i)</c> means a job completing
    /// and being removed mid-iteration doesn't break the index, and an
    /// onComplete callback that adds a new job (currently none do, but
    /// defensively) just lands at the end of the list and waits for next
    /// tick.
    /// </summary>
    private void processPendingTick()
    {
        if (_pendingRestores.Count == 0) return;

        for (int i = _pendingRestores.Count - 1; i >= 0; i--)
        {
            var job = _pendingRestores[i];
            var world = FourKit.getWorld(job.Snap.WorldName);
            if (world == null)
            {
                Console.WriteLine("[ArenaResetManager] world '" + job.Snap.WorldName
                    + "' vanished mid-restore for " + job.Arena.Name + ", aborting.");
                job.OnComplete?.Invoke();
                _pendingRestores.RemoveAt(i);
                continue;
            }

            // Chunks may be on the verge of unloading by the time the
            // worker reaches this job (players already left the arena);
            // force a reload so block writes actually persist.
            if (!job.ChunksPreloaded)
            {
                preloadChunks(world, job.Arena.BoundsMin, job.Arena.BoundsMax);
                job.ChunksPreloaded = true;
            }

            int total = job.Snap.TotalBlocks;
            int end = Math.Min(total, job.Progress + BlocksPerTick);
            for (; job.Progress < end; job.Progress++)
            {
                // Decompose flat index back into (rx, ry, rz). Must match
                // ArenaSnapshot.indexOf: ((ry*Depth + rz)*Width + rx).
                int rx = job.Progress % job.Snap.Width;
                int t  = job.Progress / job.Snap.Width;
                int rz = t % job.Snap.Depth;
                int ry = t / job.Snap.Depth;

                int  targetId   = job.Snap.TypeIds[job.Progress];
                byte targetData = job.Snap.Datas[job.Progress];

                var b = world.getBlockAt(
                    job.Snap.Origin.x + rx,
                    job.Snap.Origin.y + ry,
                    job.Snap.Origin.z + rz);
                // Skip writes when nothing changed - keeps physics quiet
                // and avoids unnecessary block-update packets to nearby
                // players (admins flying around, etc.).
                if (b.getTypeId() != targetId || b.getData() != targetData)
                {
                    b.setTypeIdAndData(targetId, targetData, false);
                    job.Changed++;
                }
            }

            if (job.Progress >= total)
            {
                if (job.Changed > 0)
                    Console.WriteLine("[ArenaResetManager] restored " + job.Arena.Name
                        + " (" + job.Changed + " blocks reverted).");
                job.OnComplete?.Invoke();
                _pendingRestores.RemoveAt(i);
            }
        }
    }

    private static void preloadChunks(FKWorld world, (int x, int y, int z) min, (int x, int y, int z) max)
    {
        int minCx = min.x >> 4;
        int maxCx = max.x >> 4;
        int minCz = min.z >> 4;
        int maxCz = max.z >> 4;
        for (int cx = minCx; cx <= maxCx; cx++)
        for (int cz = minCz; cz <= maxCz; cz++)
            world.loadChunk(cx, cz, generate: true);
    }

    private static ArenaSnapshot? tryLoad(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var magic = reader.ReadBytes(4);
            if (magic.Length != 4 || !magic.SequenceEqual(FileMagic))
            {
                Console.WriteLine("[ArenaResetManager] bad magic in " + path);
                return null;
            }

            var version = reader.ReadByte();
            if (version != FileVersion)
            {
                Console.WriteLine("[ArenaResetManager] unsupported version "
                    + version + " in " + path);
                return null;
            }

            var nameLen = reader.ReadByte();
            var nameBytes = reader.ReadBytes(nameLen);
            if (nameBytes.Length != nameLen)
            {
                Console.WriteLine("[ArenaResetManager] truncated world name in " + path);
                return null;
            }
            var worldName = System.Text.Encoding.UTF8.GetString(nameBytes);

            int ox = reader.ReadInt32();
            int oy = reader.ReadInt32();
            int oz = reader.ReadInt32();
            int w  = reader.ReadInt32();
            int h  = reader.ReadInt32();
            int d  = reader.ReadInt32();

            // Hard cap on snapshot size to prevent a corrupt file from
            // making us try to allocate gigabytes.
            if (w <= 0 || h <= 0 || d <= 0 || (long)w * h * d > 5_000_000)
            {
                Console.WriteLine("[ArenaResetManager] bad dimensions in "
                    + path + " (" + w + "x" + h + "x" + d + ")");
                return null;
            }

            var snap = new ArenaSnapshot(worldName, (ox, oy, oz), w, h, d);
            int total = w * h * d;

            // Read typeIds (Int32 array) as a single byte block; BlockCopy
            // is a direct memcpy and much faster than reader.ReadInt32() in
            // a loop for ~100k entries.
            var typeBuf = reader.ReadBytes(total * 4);
            if (typeBuf.Length != total * 4)
            {
                Console.WriteLine("[ArenaResetManager] truncated typeIds in " + path);
                return null;
            }
            Buffer.BlockCopy(typeBuf, 0, snap.TypeIds, 0, typeBuf.Length);

            var dataBuf = reader.ReadBytes(total);
            if (dataBuf.Length != total)
            {
                Console.WriteLine("[ArenaResetManager] truncated datas in " + path);
                return null;
            }
            Buffer.BlockCopy(dataBuf, 0, snap.Datas, 0, dataBuf.Length);

            return snap;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ArenaResetManager] failed to load " + path + ": " + ex.Message);
            return null;
        }
    }

    private static void saveToDisk(string arenaName, ArenaSnapshot snap)
    {
        try
        {
            Directory.CreateDirectory(SnapshotsFolder);
            var path = Path.Combine(SnapshotsFolder, arenaName + ".bin");
            var tmp  = path + ".tmp";

            using (var stream = File.Create(tmp))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(FileMagic);
                writer.Write(FileVersion);

                var nameBytes = System.Text.Encoding.UTF8.GetBytes(snap.WorldName);
                if (nameBytes.Length > 255)
                {
                    Console.WriteLine("[ArenaResetManager] world name longer than 255 bytes, truncating for snapshot header.");
                    Array.Resize(ref nameBytes, 255);
                }
                writer.Write((byte)nameBytes.Length);
                writer.Write(nameBytes);

                writer.Write(snap.Origin.x);
                writer.Write(snap.Origin.y);
                writer.Write(snap.Origin.z);
                writer.Write(snap.Width);
                writer.Write(snap.Height);
                writer.Write(snap.Depth);

                // typeIds (Int32[]) as a single byte block via BlockCopy.
                var typeBuf = new byte[snap.TypeIds.Length * 4];
                Buffer.BlockCopy(snap.TypeIds, 0, typeBuf, 0, typeBuf.Length);
                writer.Write(typeBuf);

                // datas (byte[]) written raw.
                writer.Write(snap.Datas);
            }

            // Atomic-ish replace: write to .tmp, delete old, rename. The
            // small window between Delete and Move is acceptable since the
            // snapshot file is rebuildable from the live arena anyway.
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ArenaResetManager] failed to save snapshot for "
                + arenaName + ": " + ex.Message);
        }
    }
}
