using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace BanditDuels;

internal static class BundleLoader
{
    private const string ResourcePrefix = "BanditDuels.Bundled.";

    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> ManagedResources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Data.Sqlite"] = ResourcePrefix + "Microsoft.Data.Sqlite.dll",
        ["SQLitePCLRaw.core"] = ResourcePrefix + "SQLitePCLRaw.core.dll",
        ["SQLitePCLRaw.batteries_v2"] = ResourcePrefix + "SQLitePCLRaw.batteries_v2.dll",
        ["SQLitePCLRaw.provider.e_sqlite3"] = ResourcePrefix + "SQLitePCLRaw.provider.e_sqlite3.dll",
    };

    private static bool _installed;
    private static string? _nativeSqlitePath;

    public static void Install()
    {
        lock (Sync)
        {
            if (_installed) return;

            AppDomain.CurrentDomain.AssemblyResolve += resolveBundledAssembly;

            // Load the SQLite stack before StatsRepo is touched, while we can
            // still attach the native import resolver before SQLitePCL initializes.
            loadBundledAssembly("SQLitePCLRaw.core");
            var provider = loadBundledAssembly("SQLitePCLRaw.provider.e_sqlite3");
            var batteries = loadBundledAssembly("SQLitePCLRaw.batteries_v2");
            loadBundledAssembly("Microsoft.Data.Sqlite");

            // Tolerate the resolver-already-set case: when another plugin
            // (LinkAuth, AntiCheat) also bundles SQLite and loads before us,
            // the CLR hands us back its already-loaded
            // SQLitePCLRaw.provider.e_sqlite3 assembly from GetAssemblies()
            // above. SetDllImportResolver is one-shot per assembly and
            // throws InvalidOperationException on the second call. Their
            // resolver does the same job ours would (extract e_sqlite3 to
            // %TEMP%, load it), so skipping is safe.
            try
            {
                NativeLibrary.SetDllImportResolver(provider, resolveNativeLibrary);
            }
            catch (InvalidOperationException)
            {
                // Another plugin's resolver is already attached and will
                // service our e_sqlite3 imports too. Carry on.
            }

            initializeBatteries(batteries);

            _installed = true;
        }
    }

    private static Assembly? resolveBundledAssembly(object? sender, ResolveEventArgs args)
    {
        try
        {
            var name = new AssemblyName(args.Name).Name;
            return string.IsNullOrEmpty(name) ? null : loadBundledAssembly(name);
        }
        catch
        {
            return null;
        }
    }

    private static Assembly loadBundledAssembly(string simpleName)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
        if (loaded != null) return loaded;

        if (!ManagedResources.TryGetValue(simpleName, out var resourceName))
            throw new FileNotFoundException("No bundled assembly resource is registered for " + simpleName);

        using var stream = openResource(resourceName);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Assembly.Load(bytes.ToArray());
    }

    private static IntPtr resolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "e_sqlite3", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(libraryName, "e_sqlite3.dll", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        return NativeLibrary.Load(extractNativeSqlite());
    }

    private static string extractNativeSqlite()
    {
        if (_nativeSqlitePath != null && File.Exists(_nativeSqlitePath))
            return _nativeSqlitePath;

        const string resourceName = ResourcePrefix + "e_sqlite3.dll";
        using var stream = openResource(resourceName);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        var data = bytes.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var dir = Path.Combine(Path.GetTempPath(), "BanditDuels-" + hash[..16]);
        var path = Path.Combine(dir, "e_sqlite3.dll");

        Directory.CreateDirectory(dir);
        if (!File.Exists(path) || new FileInfo(path).Length != data.Length)
            File.WriteAllBytes(path, data);

        _nativeSqlitePath = path;
        return path;
    }

    private static void initializeBatteries(Assembly batteries)
    {
        var type = batteries.GetType("SQLitePCL.Batteries_V2", throwOnError: true)!;
        var init = type.GetMethod("Init", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        init?.Invoke(null, null);
    }

    private static Stream openResource(string resourceName)
    {
        var assembly = typeof(BundleLoader).Assembly;
        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException("Missing bundled resource " + resourceName);
    }
}
