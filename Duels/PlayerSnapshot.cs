using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Entity;
using Minecraft.Server.FourKit.Inventory;

namespace BanditDuels.Duels;

/// <summary>
/// Snapshot of a player's state before a match. Values are stored by value (copies of ItemStacks)
/// so subsequent inventory clears do not corrupt the snapshot.
/// </summary>
public sealed class PlayerSnapshot
{
    public Guid PlayerId { get; }
    public Location Location { get; }
    public GameMode GameMode { get; }
    public double Health { get; }
    public int FoodLevel { get; }
    public float Saturation { get; }
    public float Exhaustion { get; }
    public int Level { get; }
    public float Exp { get; }
    public ItemStack?[] MainContents { get; }
    public ItemStack?[] ArmorContents { get; }

    private PlayerSnapshot(
        Guid playerId, Location location, GameMode gameMode,
        double health, int food, float saturation, float exhaustion,
        int level, float exp,
        ItemStack?[] main, ItemStack?[] armor)
    {
        PlayerId = playerId;
        Location = location;
        GameMode = gameMode;
        Health = health;
        FoodLevel = food;
        Saturation = saturation;
        Exhaustion = exhaustion;
        Level = level;
        Exp = exp;
        MainContents = main;
        ArmorContents = armor;
    }

    public static PlayerSnapshot capture(Player p)
    {
        var inv = p.getInventory();
        return new PlayerSnapshot(
            playerId: p.getUniqueId(),
            location: p.getLocation(),
            gameMode: p.getGameMode(),
            health: p.getHealth(),
            food: p.getFoodLevel(),
            saturation: p.getSaturation(),
            exhaustion: p.getExhaustion(),
            level: p.getLevel(),
            exp: p.getExp(),
            main: copy(inv.getContents()),
            armor: copy(inv.getArmorContents()));
    }

    public void restore(Player p)
    {
        var inv = p.getInventory();
        inv.clear();
        inv.setContents(MainContents);
        inv.setArmorContents(ArmorContents);

        p.setGameMode(GameMode);
        p.setHealth(Math.Min(Health, p.getMaxHealth()));
        p.setFoodLevel(FoodLevel);
        p.setSaturation(Saturation);
        p.setExhaustion(Exhaustion);
        p.setLevel(Level);
        p.setExp(Exp);

        p.teleport(Location);
    }

    private static ItemStack?[] copy(ItemStack?[] src)
    {
        var dst = new ItemStack?[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var s = src[i];
            if (s == null || s.getAmount() <= 0) continue;
            dst[i] = new ItemStack(s.getType(), s.getAmount(), s.getDurability());
        }
        return dst;
    }
}
