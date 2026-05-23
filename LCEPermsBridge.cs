using System.Reflection;

using Minecraft.Server.FourKit.Command;

namespace BanditDuels;

/// <summary>
/// Soft-dep bridge to LCEPerms. Resolves the static
/// <c>LCEPerms.Api.LCEPermsApi.has(CommandSender, string)</c> method via
/// reflection so we don't take a hard reference on LCEPerms.dll.
///
/// <para><b>Permission policy when LCEPerms is absent:</b> permissive.
/// Every <see cref="has"/> call returns <c>true</c>. This means anyone can
/// run /we on a server that doesn't have LCEPerms installed - which is the
/// agreed soft-dep model. See the README warning.</para>
///
/// <para><b>Console is always permitted</b>, irrespective of LCEPerms state.</para>
///
/// <para><b>Plugin load order:</b> the lookup runs lazily on the first
/// <see cref="has"/> call and retries on every cache miss, so it's safe
/// even if SimpleEdit loads before LCEPerms.</para>
///
/// This file is duplicated across plugins (SimpleEdit, Schematics,
/// AntiCheat, etc.). Keep them in sync.
/// </summary>
internal static class LCEPermsBridge
{
    private static volatile MethodInfo? _hasMethod;

    public static bool has(CommandSender sender, string node)
    {
        if (sender is ConsoleCommandSender) return true;

        var method = _hasMethod ?? resolve();
        if (method == null) return true; // LCEPerms not installed -> permissive

        try
        {
            return (bool)method.Invoke(null, new object[] { sender, node })!;
        }
        catch
        {
            // Anything goes wrong with the invocation? Default to permissive.
            return true;
        }
    }

    /// <summary>
    /// Convenience wrapper: check the permission, and if it fails, send a
    /// standard "no permission" message to the sender. Returns true iff
    /// the caller should continue.
    /// </summary>
    public static bool requirePerm(CommandSender sender, string node)
    {
        if (has(sender, node)) return true;
        sender.sendMessage("\u00A7cYou don't have permission: \u00A7f" + node);
        return false;
    }

    private static MethodInfo? resolve()
    {
        try
        {
            var type = Type.GetType("LCEPerms.Api.LCEPermsApi, LCEPerms");
            if (type == null) return null;
            var m = type.GetMethod("has", new[] { typeof(CommandSender), typeof(string) });
            if (m != null) _hasMethod = m;
            return m;
        }
        catch
        {
            return null;
        }
    }
}
