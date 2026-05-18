using Minecraft.Server.FourKit;

namespace BanditDuels.World;

/// <summary>
/// Locks the duel world to a fixed time-of-day and clear weather. FourKit
/// has no doDaylightCycle / doWeatherCycle gamerule API, so we reset both
/// on a periodic task. Drift between resets is invisible at normal playback
/// speed and a sudden storm has at most ~5 seconds to register before we
/// undo it.
/// </summary>
public sealed class WorldKeeper
{
    private readonly BanditDuels _plugin;
    private readonly string _worldName;
    private readonly long _lockedTicks;

    public WorldKeeper(BanditDuels plugin, string worldName, long lockedTicks)
    {
        _plugin = plugin;
        _worldName = worldName;
        _lockedTicks = lockedTicks;
    }

    public void start()
    {
        FourKit.getScheduler().runTaskTimer(_plugin, lockState, 1, 100);
    }

    private void lockState()
    {
        var world = FourKit.getWorld(_worldName);
        if (world == null) return;
        world.setTime(_lockedTicks);
        world.setStorm(false);
        world.setThundering(false);
    }
}
