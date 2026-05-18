using Minecraft.Server.FourKit;
using FKEntity = Minecraft.Server.FourKit.Entity;

namespace BanditDuels.World;

/// <summary>
/// Best-effort mob cull. FourKit has no spawn event or entity-remove API,
/// so we periodically teleport every non-player living entity into the void.
/// </summary>
public sealed class MobCuller
{
    private readonly BanditDuels _plugin;
    private readonly string _worldName;
    private readonly int _periodTicks;
    private readonly double _voidY;
    private long _lastCullCount;

    public MobCuller(BanditDuels plugin, string worldName, int periodTicks, double voidY)
    {
        _plugin = plugin;
        _worldName = worldName;
        _periodTicks = Math.Max(20, periodTicks);
        _voidY = voidY;
    }

    public long LastCullCount => _lastCullCount;

    public void start()
    {
        FourKit.getScheduler().runTaskTimer(_plugin, cull, _periodTicks, _periodTicks);
    }

    private void cull()
    {
        var world = FourKit.getWorld(_worldName);
        if (world == null) return;

        long count = 0;
        foreach (var ent in world.getLivingEntities())
        {
            if (ent is FKEntity.Player) continue;
            var loc = ent.getLocation();
            ent.teleport(new Location(world, loc.getX(), _voidY, loc.getZ()));
            count++;
        }
        _lastCullCount = count;
    }
}
