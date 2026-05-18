using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Scheduler;
using FKWorld = Minecraft.Server.FourKit.World;

namespace BanditDuels.Arenas;

/// <summary>
/// Batched block copy from one AABB to another. Processes blocks in
/// chunks per scheduler tick to keep the server responsive.
/// </summary>
public static class ArenaCloner
{
    public sealed class Job
    {
        public string Label = "";
        public string WorldName = "";
        public (int x, int y, int z) SrcMin;
        public (int x, int y, int z) SrcMax;
        public (int dx, int dy, int dz) Offset;

        internal int CursorX, CursorY, CursorZ;
        internal bool Started;
        internal long Total;
        internal long Done;

        public void initCursor()
        {
            CursorX = SrcMin.x;
            CursorY = SrcMin.y;
            CursorZ = SrcMin.z;
            long w = SrcMax.x - SrcMin.x + 1;
            long h = SrcMax.y - SrcMin.y + 1;
            long d = SrcMax.z - SrcMin.z + 1;
            Total = w * h * d;
            Done = 0;
            Started = true;
        }

        public bool isComplete() => Started && CursorY > SrcMax.y;
    }

    /// <summary>
    /// Run all jobs in sequence on the main thread. Each tick copies up to
    /// <paramref name="blocksPerTick"/> blocks. Calls <paramref name="onJobComplete"/>
    /// when each job finishes, and <paramref name="onAllComplete"/> when the
    /// whole queue is done.
    /// </summary>
    public static void runJobs(
        BanditDuels plugin,
        IList<Job> jobs,
        int blocksPerTick,
        Action<Job>? onJobStart = null,
        Action<Job>? onJobComplete = null,
        Action? onAllComplete = null)
    {
        if (jobs.Count == 0) { onAllComplete?.Invoke(); return; }

        int currentJob = 0;
        FourKitTask? task = null;
        task = FourKit.getScheduler().runTaskTimer(plugin, () =>
        {
            // Advance past any finished jobs.
            while (currentJob < jobs.Count && jobs[currentJob].isComplete())
            {
                onJobComplete?.Invoke(jobs[currentJob]);
                currentJob++;
            }
            if (currentJob >= jobs.Count)
            {
                task!.cancel();
                onAllComplete?.Invoke();
                return;
            }

            var job = jobs[currentJob];
            var world = FourKit.getWorld(job.WorldName);
            if (world == null)
            {
                Console.WriteLine("[ArenaCloner] world '" + job.WorldName + "' not found, aborting.");
                task!.cancel();
                return;
            }

            if (!job.Started)
            {
                job.initCursor();
                preloadChunks(world, job);
                onJobStart?.Invoke(job);
            }

            processBatch(world, job, blocksPerTick);
        }, 1, 1);
    }

    private static void preloadChunks(FKWorld world, Job job)
    {
        int minCx = (job.SrcMin.x + job.Offset.dx) >> 4;
        int maxCx = (job.SrcMax.x + job.Offset.dx) >> 4;
        int minCz = (job.SrcMin.z + job.Offset.dz) >> 4;
        int maxCz = (job.SrcMax.z + job.Offset.dz) >> 4;
        for (int cx = minCx; cx <= maxCx; cx++)
        for (int cz = minCz; cz <= maxCz; cz++)
            world.loadChunk(cx, cz, generate: true);
    }

    private static void processBatch(FKWorld world, Job job, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (job.CursorY > job.SrcMax.y) return;

            var src = world.getBlockAt(job.CursorX, job.CursorY, job.CursorZ);
            int typeId = src.getTypeId();
            byte data = src.getData();
            var dst = world.getBlockAt(
                job.CursorX + job.Offset.dx,
                job.CursorY + job.Offset.dy,
                job.CursorZ + job.Offset.dz);
            dst.setTypeIdAndData(typeId, data, false);
            job.Done++;

            // Advance cursor: Z first, then X, then Y.
            job.CursorZ++;
            if (job.CursorZ > job.SrcMax.z)
            {
                job.CursorZ = job.SrcMin.z;
                job.CursorX++;
                if (job.CursorX > job.SrcMax.x)
                {
                    job.CursorX = job.SrcMin.x;
                    job.CursorY++;
                }
            }
        }
    }
}
