using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Entity;
using Minecraft.Server.FourKit.Event;
using Minecraft.Server.FourKit.Event.Block;
using Minecraft.Server.FourKit.Event.Entity;
using Minecraft.Server.FourKit.Event.Player;

using BanditDuels.Duels;
using BanditDuels.Lobby;

namespace BanditDuels.Listeners;

/// <summary>
/// Keeps non-duel players inside the lobby AABB and re-spawns them at the
/// lobby safe spawn on join/death. Duelists are skipped because they are
/// legitimately outside the lobby during a match.
///
/// Also maintains lobby-only player state: full health/food/saturation
/// (refreshed every periodic tick) and cancelled fall damage. This makes
/// the lobby a safe waiting area where players can mess around without
/// having to worry about chip damage or parkour falls.
///
/// Boundary enforcement runs on a periodic scheduler tick rather than
/// PlayerMoveEvent because reacting to every move (including knockback
/// during damage) was interrupting the damage->death flow and causing
/// LCE anti-cheat to disconnect players as "moved-too-quickly".
/// </summary>
public sealed class LobbyGuardListener : Listener
{
    private readonly LobbyManager _lobby;
    private readonly DuelManager  _duels;

    public LobbyGuardListener(LobbyManager lobby, DuelManager duels)
    {
        _lobby = lobby;
        _duels = duels;
    }

    /// <summary>If false, lobby block break/place/interact/drop is allowed.
    /// Toggleable at runtime via /duel admin lobbyguard on|off. Default true.
    /// Fall damage cancellation and health/food refresh are not affected by
    /// this flag; those are comfort features, not protection.</summary>
    public bool BlockProtectionEnabled { get; set; } = true;

    /// <summary>If false, the periodic tick stops yanking non-duelling
    /// non-admin players back to lobby spawn when they walk outside the
    /// lobby AABB. Set false when WorldGuard's exit=deny (or any other
    /// plugin) is handling that. Comfort features (health, food,
    /// saturation top-up, fall damage cancel) keep running. Default
    /// true.</summary>
    public bool BoundaryEnforcementEnabled { get; set; } = true;

    /// <summary>
    /// Buffer applied around the lobby AABB when deciding whether to cancel
    /// block break / place / interact. Player reach is ~4.5 blocks in
    /// survival, so a strict block-inside-AABB check leaves a ring of
    /// griefable terrain that a player standing at the lobby edge can pick
    /// off. 10 blocks gives a comfortable cushion in every direction.
    ///
    /// Only the block protection uses this expanded zone. Boundary teleport,
    /// fall damage cancel, health refresh, and drop suppression stay on the
    /// tight AABB - they're about player position, not block position, and
    /// "you can drop items 10 blocks outside the lobby" would be a weird
    /// affordance to grant.
    /// </summary>
    private const int BlockGuardMargin = 10;

    private bool isInsideBlockGuardZone(string worldName, int x, int y, int z)
    {
        if (!_lobby.HasBounds) return false;
        var c = _lobby.Config;
        if (!string.Equals(worldName, c.WorldName, StringComparison.Ordinal)) return false;
        return x >= c.BoundsMinX - BlockGuardMargin && x <= c.BoundsMaxX + BlockGuardMargin
            && y >= c.BoundsMinY - BlockGuardMargin && y <= c.BoundsMaxY + BlockGuardMargin
            && z >= c.BoundsMinZ - BlockGuardMargin && z <= c.BoundsMaxZ + BlockGuardMargin;
    }

    private bool isInsideLobbyFootprint(string worldName, int x, int z)
    {
        if (!_lobby.HasBounds) return false;
        var c = _lobby.Config;
        if (!string.Equals(worldName, c.WorldName, StringComparison.Ordinal)) return false;
        return x >= c.BoundsMinX && x <= c.BoundsMaxX
            && z >= c.BoundsMinZ && z <= c.BoundsMaxZ;
    }

    /// <summary>Start the periodic boundary check (called from plugin onEnable).</summary>
    public void start(BanditDuels plugin)
    {
        // 0.5s tick. Frequent enough to catch escapes quickly without spamming.
        FourKit.getScheduler().runTaskTimer(plugin, tick, 20, 10);
    }

    private void tick()
    {
        if (!_lobby.HasBounds || !_lobby.HasSpawn) return;

        foreach (var p in FourKit.getOnlinePlayers())
        {
            if (_duels.isInMatch(p.getUniqueId())) continue;

            var loc = p.getLocation();
            var world = loc.getWorld();
            if (world == null) continue;

            var inLobby = _lobby.isInside(world.getName(), loc.getBlockX(), loc.getBlockY(), loc.getBlockZ());

            if (!inLobby)
            {
                // Boundary enforcement off (WorldGuard handles it via
                // exit=deny, or it's been disabled for some other reason):
                // skip the teleport-back but still process comfort features
                // for in-lobby players elsewhere in the loop.
                if (!BoundaryEnforcementEnabled) continue;

                // Admins can roam freely outside the lobby (to set bounds, fix
                // arenas, etc.) without being yanked back every 0.5s.
                if (isAdmin(p)) continue;

                var spawn = _lobby.getSafeSpawn();
                if (spawn != null) p.teleport(spawn);
                continue;
            }

            // In lobby and not duelling: top up health, food, saturation each tick.
            // Cheap idempotent calls; this keeps players permanently full so the
            // lobby acts as a safe waiting area between matches.
            var maxHp = p.getMaxHealth();
            if (p.getHealth() < maxHp) p.setHealth(maxHp);
            if (p.getFoodLevel() < 20) p.setFoodLevel(20);
            p.setSaturation(20f);
            p.setExhaustion(0f);
        }
    }

    [EventHandler(Priority = EventPriority.Normal)]
    public void onJoin(PlayerJoinEvent e)
    {
        var p = e.getPlayer();

        // If this player quit during a duel, their kit items never got cleared
        // (finalizeMatch couldn't restore an offline player). Reset them now,
        // before any other join logic runs.
        var pending = BanditDuels.Instance.PendingResets;
        if (pending != null && pending.isPending(p.getUniqueId()))
        {
            p.getInventory().clear();
            p.setMaxHealth(20.0);
            p.setHealth(20.0);
            p.setFoodLevel(20);
            p.setSaturation(20f);
            p.setExhaustion(0f);
            p.setLevel(0);
            p.setExp(0f);
            p.setGameMode(GameMode.SURVIVAL);
            pending.clearPending(p.getUniqueId());
            p.sendMessage("§7Your previous duel was forfeited. Inventory reset.");
        }

        var spawn = _lobby.getSafeSpawn();
        if (spawn == null) return;
        p.teleport(spawn);
    }

    [EventHandler(Priority = EventPriority.Monitor)]
    public void onDeath(PlayerDeathEvent e)
    {
        var player = e.getEntity();
        // Duelists are restored to their snapshot by DuelDeathListener; don't override.
        if (_duels.isInMatch(player.getUniqueId())) return;
        if (!_lobby.HasSpawn) return;

        var name = player.getName();
        // Delay so the respawn completes first; then yank to lobby.
        FourKit.getScheduler().runTaskLater(BanditDuels.Instance, () =>
        {
            var p = FourKit.getPlayer(name);
            var spawn = _lobby.getSafeSpawn();
            if (p != null && spawn != null) p.teleport(spawn);
        }, 20);
    }

    [EventHandler(Priority = EventPriority.High)]
    public void onBlockBreak(BlockBreakEvent e)
    {
        if (!BlockProtectionEnabled) return;
        if (!_lobby.HasBounds) return;
        // Admins can edit the lobby with protection on - the protection is for
        // random players, not server staff who need to fix lobby builds in place.
        if (isAdmin(e.getPlayer())) return;
        var loc = e.getBlock().getLocation();
        var world = loc.getWorld();
        if (world == null) return;
        if (!isInsideBlockGuardZone(world.getName(), loc.getBlockX(), loc.getBlockY(), loc.getBlockZ())) return;
        e.setCancelled(true);
    }

    [EventHandler(Priority = EventPriority.High)]
    public void onBlockPlace(BlockPlaceEvent e)
    {
        if (!BlockProtectionEnabled) return;
        if (!_lobby.HasBounds) return;
        if (isAdmin(e.getPlayer())) return;
        var loc = e.getBlockPlaced().getLocation();
        var world = loc.getWorld();
        if (world == null) return;
        if (!isInsideBlockGuardZone(world.getName(), loc.getBlockX(), loc.getBlockY(), loc.getBlockZ())) return;
        e.setCancelled(true);
    }

    [EventHandler(Priority = EventPriority.High)]
    public void onDrop(PlayerDropItemEvent e)
    {
        if (!BlockProtectionEnabled) return;
        if (!_lobby.HasBounds) return;
        var p = e.getPlayer();
        // Duelists' drops are already handled by ArenaGuardListener; this is just lobby coverage.
        if (_duels.isInMatch(p.getUniqueId())) return;
        if (isAdmin(p)) return;

        var loc = p.getLocation();
        var world = loc.getWorld();
        if (world == null) return;
        if (!_lobby.isInside(world.getName(), loc.getBlockX(), loc.getBlockY(), loc.getBlockZ())) return;
        e.setCancelled(true);
    }

    /// <summary>
    /// True if the player has the banditduels.bypass.lobby permission.
    /// Centralized so all four lobby-protection handlers share the same
    /// check. Routes through LCEPermsBridge so the lookup is consistent
    /// with the rest of the plugin.
    /// </summary>
    private static bool isAdmin(Player? p)
    {
        if (p == null) return false;
        return LCEPermsBridge.has(p, "banditduels.bypass.lobby");
    }

    /// <summary>
    /// Cancel any block interaction inside the lobby AABB: buttons, levers,
    /// doors, trapdoors, chests, pressure plates, redstone components, etc.
    /// Only triggers when the player actually clicked a block; air clicks
    /// (eating food, using items) still work fine.
    ///
    /// Doors and fence gates are a known LCE quirk: <c>setCancelled(true)</c>
    /// alone doesn't always stop them toggling, since the toggle is applied
    /// before our handler runs. We additionally snapshot the block state
    /// during the event and force-revert it one tick later if it changed.
    /// </summary>
    [EventHandler(Priority = EventPriority.High)]
    public void onInteract(PlayerInteractEvent e)
    {
        if (!BlockProtectionEnabled) return;
        if (!_lobby.HasBounds) return;
        if (isAdmin(e.getPlayer())) return;
        var block = e.getClickedBlock();
        if (block == null) return;  // air interaction; nothing to protect

        var loc = block.getLocation();
        var world = loc.getWorld();
        if (world == null) return;
        if (!isInsideBlockGuardZone(world.getName(), loc.getBlockX(), loc.getBlockY(), loc.getBlockZ())) return;

        e.setCancelled(true);

        if (isOpenableQuirky(block.getType()))
            scheduleRevert(block, world.getName());
    }

    /// <summary>
    /// Materials whose right-click toggle state isn't reliably stopped by
    /// PlayerInteractEvent.setCancelled() in LCE/FourKit. Trapdoors and
    /// buttons / levers / chests respect the cancel correctly and don't
    /// need this treatment.
    /// </summary>
    private static bool isOpenableQuirky(Material mat) =>
        mat == Material.WOODEN_DOOR
        || mat == Material.IRON_DOOR_BLOCK
        || mat == Material.FENCE_GATE;

    /// <summary>
    /// Capture the block's current type+data (during event dispatch this is
    /// the pre-toggle state in standard Bukkit semantics) and re-apply it one
    /// tick later. For tall doors we also fix up the other half so we don't
    /// leave a desynced top/bottom.
    /// </summary>
    private void scheduleRevert(Minecraft.Server.FourKit.Block.Block clicked, string worldName)
    {
        int x = clicked.getX();
        int y = clicked.getY();
        int z = clicked.getZ();
        int origId    = clicked.getTypeId();
        byte origData = clicked.getData();

        // Tall doors only: locate the other half. Bit 0x8 on data marks the
        // upper half in 1.6-era door encoding. Fence gates are single blocks
        // and skip this step.
        int? siblingY = null;
        int  siblingId = 0;
        byte siblingData = 0;

        if (origId != (int)Material.FENCE_GATE)
        {
            bool isUpper = (origData & 0x8) != 0;
            int otherY = isUpper ? y - 1 : y + 1;
            var w0 = FourKit.getWorld(worldName);
            if (w0 != null)
            {
                var s = w0.getBlockAt(x, otherY, z);
                if (s.getTypeId() == origId)
                {
                    siblingY    = otherY;
                    siblingId   = s.getTypeId();
                    siblingData = s.getData();
                }
            }
        }

        FourKit.getScheduler().runTaskLater(BanditDuels.Instance, () =>
        {
            var w = FourKit.getWorld(worldName);
            if (w == null) return;

            var b = w.getBlockAt(x, y, z);
            if (b.getTypeId() == origId && b.getData() != origData)
                b.setTypeIdAndData(origId, origData, false);

            if (siblingY.HasValue)
            {
                var sb = w.getBlockAt(x, siblingY.Value, z);
                if (sb.getTypeId() == siblingId && sb.getData() != siblingData)
                    sb.setTypeIdAndData(siblingId, siblingData, false);
            }
        }, 1);
    }

    /// <summary>
    /// Cancel fall damage for players inside the lobby footprint who aren't
    /// currently in a duel. Fall damage lands can be below the configured
    /// vertical AABB, so this uses X/Z bounds only while still requiring the
    /// lobby world.
    /// </summary>
    [EventHandler(Priority = EventPriority.High)]
    public void onDamage(EntityDamageEvent e)
    {
        if (!_lobby.HasBounds) return;
        if (e.getCause() != EntityDamageEvent.DamageCause.FALL) return;
        if (e.getEntity() is not Player p) return;
        if (_duels.isInMatch(p.getUniqueId())) return;

        var loc = p.getLocation();
        var world = loc.getWorld();
        if (world == null) return;
        if (!isInsideLobbyFootprint(world.getName(), loc.getBlockX(), loc.getBlockZ())) return;

        e.setCancelled(true);
    }
}
