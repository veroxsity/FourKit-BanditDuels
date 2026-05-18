namespace BanditDuels.Kits;

public sealed class KitRegistry
{
    private readonly Dictionary<string, Kit> _byId = new(StringComparer.OrdinalIgnoreCase);

    public void register(Kit kit) => _byId[kit.Id] = kit;

    public Kit? get(string id) => _byId.TryGetValue(id, out var k) ? k : null;

    public IEnumerable<Kit> all() => _byId.Values;

    public int count() => _byId.Count;
}
