using System.Text.Json;

namespace BanditDuels.Permissions;

/// <summary>
/// Persists the list of player names allowed to run /duel admin subcommands
/// (other than add/remove/list, which are console-only and gate this list
/// itself). Storage is a flat JSON array at
/// <c>plugins/BanditDuels-data/admins.json</c>.
///
/// Names are matched case-insensitively. The match is by name rather than
/// UUID because the admin list is curated by a human typing names at the
/// server console; matching by UUID would mean either looking up offline
/// players (unreliable on LCE) or only allowing online players to be added.
/// </summary>
public sealed class AdminManager
{
    public const string DataFolder = "plugins/BanditDuels-data";
    public const string File       = "admins.json";

    private readonly HashSet<string> _admins = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> all() => _admins;

    public bool isAdmin(string playerName) =>
        !string.IsNullOrEmpty(playerName) && _admins.Contains(playerName);

    /// <summary>Returns true if the name was newly added; false if it was already present.</summary>
    public bool add(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return false;
        if (!_admins.Add(playerName)) return false;
        save();
        return true;
    }

    /// <summary>Returns true if the name was present and got removed; false if it wasn't an admin.</summary>
    public bool remove(string playerName)
    {
        if (!_admins.Remove(playerName)) return false;
        save();
        return true;
    }

    public void load()
    {
        var path = Path.Combine(DataFolder, File);
        if (!System.IO.File.Exists(path)) return;
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            var list = JsonSerializer.Deserialize<List<string>>(stream);
            if (list == null) return;
            _admins.Clear();
            foreach (var n in list)
                if (!string.IsNullOrWhiteSpace(n)) _admins.Add(n);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] failed to load admins.json: " + ex.Message
                + " (continuing with empty admin list)");
        }
    }

    private void save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var path = Path.Combine(DataFolder, File);
            var tmp  = path + ".tmp";
            using (var s = System.IO.File.Create(tmp))
                JsonSerializer.Serialize(s, _admins.ToList(), new JsonSerializerOptions { WriteIndented = true });
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            System.IO.File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] failed to save admins.json: " + ex.Message);
        }
    }
}
