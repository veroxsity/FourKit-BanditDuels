namespace BanditDuels.Kits;

/// <summary>
/// JSON shape for one kit file at <c>plugins/BanditDuels-data/kits/&lt;id&gt;.json</c>.
/// Materials and enchantment types are case-insensitive enum names from FourKit
/// (e.g. "DIAMOND_SWORD", "DAMAGE_ALL"). See Material.cs / EnchantmentType for the
/// full list.
/// </summary>
public sealed class KitDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public ItemSpec? Helmet     { get; set; }
    public ItemSpec? Chestplate { get; set; }
    public ItemSpec? Leggings   { get; set; }
    public ItemSpec? Boots      { get; set; }

    /// <summary>One entry per inventory slot (0-8 = hotbar, 9-35 = main inventory).</summary>
    public List<InventoryItem> Inventory { get; set; } = new();
}

public class ItemSpec
{
    /// <summary>Enum name from FourKit's Material (case-insensitive).</summary>
    public string Material { get; set; } = "";
    public int    Amount { get; set; } = 1;
    /// <summary>Block data / item damage value. For potions, encodes type+splash+tier.</summary>
    public int    Durability { get; set; } = 0;
    public string? DisplayName { get; set; }
    public List<string>? Lore { get; set; }
    public List<EnchantmentSpec>? Enchantments { get; set; }
}

public sealed class InventoryItem : ItemSpec
{
    public int Slot { get; set; }
}

public sealed class EnchantmentSpec
{
    /// <summary>Enum name from FourKit's EnchantmentType, e.g. "DAMAGE_ALL" for Sharpness.</summary>
    public string Type { get; set; } = "";
    public int Level { get; set; } = 1;
}
