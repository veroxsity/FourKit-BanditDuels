namespace BanditDuels.Config;

/// <summary>JSON shape persisted to <c>plugins/BanditDuels-data/config.json</c>.</summary>
public sealed class PluginConfig
{
    /// <summary>Name of the world the plugin operates on (e.g. "world").</summary>
    public string WorldName { get; set; } = "world";

    public FeatureConfig Features { get; set; } = new();
}

public sealed class FeatureConfig
{
    /// <summary>If true, repeatedly reset world time to <see cref="NoonTicks"/>.</summary>
    public bool LockWorldTime { get; set; } = true;
    public long NoonTicks { get; set; } = 6000L;

    /// <summary>If true, periodically teleport non-player living entities to <see cref="MobCullVoidY"/>.</summary>
    public bool MobCullEnabled { get; set; } = true;
    public int MobCullPeriodTicks { get; set; } = 100;
    public double MobCullVoidY { get; set; } = -100.0;

    /// <summary>
    /// If true, admins (entries in BanditDuels-data/admins.json) receive every
    /// chat message regardless of duel-isolation rules: lobby chat while in a
    /// duel, duel chat from any match while in the lobby, the lot. Lets staff
    /// monitor PvP-room banter for rule-breaking without having to tail the
    /// console. Senders are unaware; they see the normal echo only.
    /// Default true on the assumption that anyone with admin status wants
    /// the visibility; flip to false if admins should be subject to the same
    /// chat scoping as regular players.
    /// </summary>
    public bool AdminsSeeAllChat { get; set; } = true;

    /// <summary>
    /// If true, the duel chat listener mirrors every chat message to the
    /// server console in <c>[Chat] &lt;name&gt; message</c> form. Disabled by
    /// default because BanditChat (the prefix/suffix plugin) typically owns
    /// console logging in BanditVault setups, and having both produces a
    /// duplicate per chat line. Enable this if you don't run BanditChat
    /// (or any other chat plugin that logs to console) and want chat in
    /// the server log.
    /// </summary>
    public bool ChatConsoleLog { get; set; } = false;
}
