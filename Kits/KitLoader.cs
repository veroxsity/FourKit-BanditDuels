using System.Reflection;
using System.Text.Json;

namespace BanditDuels.Kits;

/// <summary>
/// Reads kit JSON files from <c>plugins/BanditDuels-data/kits/</c>.
///
/// Two operations happen here:
///
/// 1. <see cref="loadAll"/> — runtime load. Reads every <c>*.json</c> in the
///    kits folder into <see cref="KitDefinition"/> objects. This is the only
///    place the plugin actually uses to know what kits exist; the source
///    has no opinion on kit contents.
///
/// 2. <see cref="writeDefaults"/> — first-run seeder. If the kits folder
///    doesn't exist yet, the kit JSON files embedded as resources in the
///    plugin DLL (one per file in DefaultKits\) get written out to disk so
///    a fresh install has something to duel with. After that, those embedded
///    copies are never touched again.
///
/// To add or modify a default kit:
///   - drop / edit a file in <c>Plugins/BanditDuels/DefaultKits/</c>
///   - rebuild the plugin (csproj globs that folder as EmbeddedResource)
/// Existing installs need the JSON dropped into their kits folder by hand
/// (or the kits folder deleted entirely so the seeder runs again).
/// </summary>
public static class KitLoader
{
    public const string DataFolder = "plugins/BanditDuels-data";
    public const string KitsFolder = "plugins/BanditDuels-data/kits";

    /// <summary>Embedded resource prefix that the csproj's LogicalName pattern uses.</summary>
    private const string DefaultsResourcePrefix = "BanditDuels.DefaultKits.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        IncludeFields = false,
    };

    public static List<KitDefinition> loadAll()
    {
        if (!Directory.Exists(KitsFolder))
        {
            Directory.CreateDirectory(KitsFolder);
            int written = writeDefaults();
            Console.WriteLine("[BanditDuels] wrote " + written + " default kits to " + KitsFolder);
        }

        var kits = new List<KitDefinition>();
        foreach (var path in Directory.EnumerateFiles(KitsFolder, "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var def = JsonSerializer.Deserialize<KitDefinition>(stream, JsonOpts);
                if (def == null) continue;
                if (string.IsNullOrWhiteSpace(def.Id))
                {
                    Console.WriteLine("[BanditDuels] kit file " + Path.GetFileName(path) + " has no Id; skipping.");
                    continue;
                }
                kits.Add(def);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BanditDuels] failed to load " + Path.GetFileName(path) + ": " + ex.Message);
            }
        }
        return kits;
    }

    /// <summary>
    /// Extract every embedded default-kit JSON into the kits folder. Returns
    /// how many were written. Existing files with the same name are
    /// overwritten — but this only ever runs when the kits folder was just
    /// created, so in practice nothing pre-existing gets clobbered.
    /// </summary>
    private static int writeDefaults()
    {
        var asm = typeof(KitLoader).Assembly;
        int count = 0;
        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(DefaultsResourcePrefix, StringComparison.Ordinal)) continue;
            // The LogicalName template ends in the filename ("uhc.json", etc.)
            var fileName = resourceName.Substring(DefaultsResourcePrefix.Length);
            try
            {
                using var stream = asm.GetManifestResourceStream(resourceName)
                    ?? throw new FileNotFoundException("Missing embedded resource " + resourceName);
                var outPath = Path.Combine(KitsFolder, fileName);
                using var outFile = File.Create(outPath);
                stream.CopyTo(outFile);
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BanditDuels] failed to write default kit '" + fileName + "': " + ex.Message);
            }
        }
        return count;
    }
}
