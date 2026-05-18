using Minecraft.Server.FourKit.Entity;

namespace BanditDuels.Kits;

public abstract class Kit
{
    /// <summary>Lowercase id used in commands (e.g. "uhc", "classic").</summary>
    public abstract string Id { get; }

    /// <summary>Display label shown to players.</summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Applies this kit to a player whose inventory has already been cleared.
    /// Caller is responsible for clearing first.
    /// </summary>
    public abstract void apply(Player player);
}
