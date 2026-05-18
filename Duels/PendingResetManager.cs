using System.Text.Json;

namespace BanditDuels.Duels;

/// <summary>
/// Tracks players who quit mid-match. On their next join we wipe the duel
/// kit so they don't reappear in the lobby wearing diamond armor.
///
/// Persisted to <c>plugins/BanditDuels-data/pending_resets.json</c> so a
/// server restart between quit and rejoin doesn't lose the marker.
/// </summary>
public sealed class PendingResetManager
{
    public const string DataFolder = "plugins/BanditDuels-data";
    public const string File       = "pending_resets.json";

    private readonly HashSet<Guid> _pending;

    public PendingResetManager()
    {
        _pending = load();
    }

    public bool isPending(Guid id) => _pending.Contains(id);

    public void markPending(Guid id)
    {
        if (_pending.Add(id)) save();
    }

    public void clearPending(Guid id)
    {
        if (_pending.Remove(id)) save();
    }

    private static HashSet<Guid> load()
    {
        var path = Path.Combine(DataFolder, File);
        if (!System.IO.File.Exists(path)) return new HashSet<Guid>();
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            var list = JsonSerializer.Deserialize<List<string>>(stream) ?? new List<string>();
            var result = new HashSet<Guid>();
            foreach (var s in list)
                if (Guid.TryParse(s, out var g)) result.Add(g);
            return result;
        }
        catch
        {
            return new HashSet<Guid>();
        }
    }

    private void save()
    {
        Directory.CreateDirectory(DataFolder);
        var path = Path.Combine(DataFolder, File);
        var list = _pending.Select(g => g.ToString()).ToList();
        using var stream = System.IO.File.Create(path);
        JsonSerializer.Serialize(stream, list);
    }
}
