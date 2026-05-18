using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Enchantments;
using Minecraft.Server.FourKit.Entity;
using Minecraft.Server.FourKit.Inventory;

namespace BanditDuels.Kits;

/// <summary>A <see cref="Kit"/> backed by a JSON-loaded <see cref="KitDefinition"/>.</summary>
public sealed class JsonKit : Kit
{
    private readonly KitDefinition _def;

    public JsonKit(KitDefinition def) { _def = def; }

    public override string Id => _def.Id;
    public override string DisplayName => string.IsNullOrEmpty(_def.DisplayName) ? _def.Id : _def.DisplayName;

    public override void apply(Player player)
    {
        var inv = player.getInventory();
        if (_def.Helmet     != null) inv.setHelmet    (buildItem(_def.Helmet));
        if (_def.Chestplate != null) inv.setChestplate(buildItem(_def.Chestplate));
        if (_def.Leggings   != null) inv.setLeggings  (buildItem(_def.Leggings));
        if (_def.Boots      != null) inv.setBoots     (buildItem(_def.Boots));

        foreach (var entry in _def.Inventory)
        {
            try { inv.setItem(entry.Slot, buildItem(entry)); }
            catch (Exception ex)
            {
                Console.WriteLine("[BanditDuels] kit '" + Id + "': skipped slot " + entry.Slot + " (" + ex.Message + ")");
            }
        }
    }

    /// <summary>Construct an ItemStack from a spec. Throws if the material is unknown.</summary>
    public static ItemStack buildItem(ItemSpec spec)
    {
        if (!Enum.TryParse<Material>(spec.Material, ignoreCase: true, out var mat))
            throw new ArgumentException("unknown material '" + spec.Material + "'");

        var amount = Math.Max(1, spec.Amount);
        var item = new ItemStack(mat, amount, (short)spec.Durability);

        bool wantsMeta = spec.DisplayName != null
                      || (spec.Lore?.Count ?? 0) > 0
                      || (spec.Enchantments?.Count ?? 0) > 0;

        if (!wantsMeta) return item;

        var meta = item.getItemMeta();
        if (spec.DisplayName != null) meta.setDisplayName(spec.DisplayName);
        if (spec.Lore != null && spec.Lore.Count > 0) meta.setLore(new List<string>(spec.Lore));

        if (spec.Enchantments != null)
        {
            foreach (var e in spec.Enchantments)
            {
                if (Enum.TryParse<EnchantmentType>(e.Type, ignoreCase: true, out var et))
                    meta.addEnchant(et, e.Level, ignoreLevelRestriction: true);
                else
                    Console.WriteLine("[BanditDuels] unknown enchantment '" + e.Type + "' on " + spec.Material);
            }
        }

        item.setItemMeta(meta);
        return item;
    }
}
