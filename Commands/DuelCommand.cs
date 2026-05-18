using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Command;
using Minecraft.Server.FourKit.Entity;

using BanditDuels.Arenas;
using BanditDuels.Duels;
using BanditDuels.Kits;
using BanditDuels.Permissions;

namespace BanditDuels.Commands;

public sealed class DuelCommand : CommandExecutor
{
    private readonly DuelManager _duels;
    private readonly KitRegistry _kits;
    private readonly ArenaRegistry _arenas;

    public DuelCommand(DuelManager duels, KitRegistry kits, ArenaRegistry arenas)
    {
        _duels = duels;
        _kits = kits;
        _arenas = arenas;
    }

    public bool onCommand(CommandSender sender, Command command, string label, string[] args)
    {
        if (args.Length == 0)
        {
            if (sender is Player p) sendUsage(p);
            else sendConsoleUsage(sender);
            return true;
        }

        var sub = args[0].ToLowerInvariant();

        // /duel admin is the one entry point that accepts both players and the
        // console - the subcommands fork inside handleAdmin based on sender.
        if (sub == "admin")
            return handleAdmin(sender, args);

        // Everything else needs an actual player.
        if (sender is not Player player)
        {
            sender.sendMessage("Only players can run /duel " + sub + ". From console, try /duel admin add|remove|list <name>.");
            return true;
        }

        switch (sub)
        {
            case "accept":
                return handleAccept(player, args);

            case "deny":
            case "decline":
                return handleDeny(player, args);

            case "kits":
                listKits(player);
                return true;

            case "arenas":
                listArenas(player);
                return true;

            case "queue":
            case "q":
                return handleQueue(player, args);

            case "leave":
                return handleLeave(player);

            case "queues":
                listQueues(player);
                return true;

            case "stats":
                return handleStats(player, args);

            case "top":
                return handleTop(player, args);

            default:
                return handleChallenge(player, args);
        }
    }

    private bool handleChallenge(Player p, string[] args)
    {
        if (args.Length < 2)
        {
            p.sendMessage("§cUsage: /duel <player> <kit>");
            return true;
        }
        var targetName = args[0];
        var kitId = args[1];

        if (string.Equals(targetName, p.getName(), StringComparison.OrdinalIgnoreCase))
        {
            p.sendMessage("§cYou cannot duel yourself.");
            return true;
        }

        var target = FourKit.getPlayer(targetName);
        if (target == null)
        {
            p.sendMessage("Player '" + targetName + "' is not online.");
            return true;
        }

        p.sendMessage(_duels.createRequest(p, target, kitId));
        return true;
    }

    private bool handleAccept(Player p, string[] args)
    {
        if (args.Length < 2)
        {
            p.sendMessage("§cUsage: /duel accept <player>");
            return true;
        }
        var challenger = FourKit.getPlayer(args[1]);
        if (challenger == null)
        {
            p.sendMessage("§cPlayer '" + args[1] + "' is not online.");
            return true;
        }
        p.sendMessage(_duels.acceptRequest(p, challenger));
        return true;
    }

    private bool handleDeny(Player p, string[] args)
    {
        if (args.Length < 2)
        {
            p.sendMessage("§cUsage: /duel deny <player>");
            return true;
        }
        var challenger = FourKit.getPlayer(args[1]);
        if (challenger == null)
        {
            p.sendMessage("§cPlayer '" + args[1] + "' is not online.");
            return true;
        }
        p.sendMessage(_duels.denyRequest(p, challenger));
        return true;
    }

    private void listKits(Player p)
    {
        p.sendMessage("§6Available kits:");
        foreach (var k in _kits.all())
            p.sendMessage("- " + k.Id + " (" + k.DisplayName + ")");
    }

    private void listArenas(Player p)
    {
        p.sendMessage("§6Registered arenas (" + _arenas.count() + "):");
        foreach (var a in _arenas.all())
            p.sendMessage("- " + a.Name + " at " + a.BoundsMin + " to " + a.BoundsMax);
    }

    private bool handleQueue(Player p, string[] args)
    {
        if (args.Length < 2)
        {
            p.sendMessage("§cUsage: /duel queue <kit>");
            return true;
        }
        var queues = BanditDuels.Instance.Queues;
        if (queues == null)
        {
            p.sendMessage("§cQueue system is not available.");
            return true;
        }
        p.sendMessage(queues.joinQueue(p, args[1]));
        return true;
    }

    private bool handleLeave(Player p)
    {
        var queues = BanditDuels.Instance.Queues;
        if (queues == null)
        {
            p.sendMessage("§cQueue system is not available.");
            return true;
        }
        p.sendMessage(queues.leaveQueue(p));
        return true;
    }

    private void listQueues(Player p)
    {
        var queues = BanditDuels.Instance.Queues;
        if (queues == null)
        {
            p.sendMessage("§cQueue system is not available.");
            return;
        }
        var active = queues.all().Where(q => q.size() > 0).ToList();
        if (active.Count == 0)
        {
            p.sendMessage("§7No active queues.");
            return;
        }
        p.sendMessage("§6Active queues:");
        foreach (var q in active)
            p.sendMessage("- " + q.KitId + " (" + q.size() + " waiting)");
    }

    private bool handleStats(Player p, string[] args)
    {
        var stats = BanditDuels.Instance.Stats;
        if (stats == null)
        {
            p.sendMessage("§cStats not available.");
            return true;
        }

        string targetUuid;
        string targetName;

        if (args.Length >= 2)
        {
            // Lookup by name (online or offline)
            var online = FourKit.getPlayer(args[1]);
            if (online != null)
            {
                targetUuid = online.getUniqueId().ToString();
                targetName = online.getName();
            }
            else
            {
                var uuid = stats.findUuidByName(args[1]);
                if (uuid == null)
                {
                    p.sendMessage("§cNo player matching '" + args[1] + "' has played a duel yet.");
                    return true;
                }
                targetUuid = uuid;
                targetName = args[1];
            }
        }
        else
        {
            targetUuid = p.getUniqueId().ToString();
            targetName = p.getName();
        }

        var s = stats.getStats(targetUuid, targetName);
        if (s.TotalMatches == 0)
        {
            p.sendMessage("" + targetName + " has no recorded duels.");
            return true;
        }

        p.sendMessage("§6[Stats] §f" + s.Name);
        p.sendMessage("§7  Matches: §f" + s.TotalMatches
            + " (" + s.Wins + "W§7, §c" + s.Losses + "L§7, §e" + s.Draws + "D)");
        p.sendMessage("§7  Win rate: §f" + (s.WinRate * 100).ToString("0.0") + "%");
        if (s.ByKit.Count > 0)
        {
            p.sendMessage("§7  By kit:");
            foreach (var kv in s.ByKit.OrderByDescending(x => x.Value.Wins))
            {
                var total = kv.Value.Wins + kv.Value.Losses + kv.Value.Draws;
                var pct = total == 0 ? 0 : (double)kv.Value.Wins / total * 100;
                p.sendMessage("    " + kv.Key + ": " + kv.Value.Wins + "W " + kv.Value.Losses + "L"
                    + (kv.Value.Draws > 0 ? " " + kv.Value.Draws + "D" : "")
                    + " (" + pct.ToString("0.0") + "%)");
            }
        }
        return true;
    }

    private bool handleTop(Player p, string[] args)
    {
        var stats = BanditDuels.Instance.Stats;
        if (stats == null)
        {
            p.sendMessage("§cStats not available.");
            return true;
        }

        string? kitFilter = args.Length >= 2 ? args[1] : null;
        var rows = stats.top(10, kitFilter);
        if (rows.Count == 0)
        {
            p.sendMessage("§7No matches recorded yet" + (kitFilter != null ? " for kit '" + kitFilter + "'" : "") + ".");
            return true;
        }

        p.sendMessage("§6[Top 10 by wins" + (kitFilter != null ? " - " + kitFilter : "") + "]");
        int rank = 1;
        foreach (var row in rows)
        {
            p.sendMessage(" " + rank + ". " + row.Name
                + " - " + row.Wins + " wins §7(" + (row.WinRate * 100).ToString("0.0") + "%, "
                + row.TotalMatches + " played)");
            rank++;
        }
        return true;
    }

    private bool handleAdmin(CommandSender sender, string[] args)
    {
        if (args.Length < 2)
        {
            if (sender is Player p) sendPlayerAdminHelp(p);
            else                    sendConsoleAdminHelp(sender);
            return true;
        }

        var sub = args[1].ToLowerInvariant();

        // Console-only: managing the admin list itself. These never run from
        // in-game so an admin's account being compromised can't escalate.
        if (sub == "add" || sub == "remove" || sub == "list")
        {
            if (sender is Player)
            {
                sender.sendMessage("/duel admin " + sub + " can only be run from the server console.");
                return true;
            }
            return sub switch
            {
                "add"    => handleAdminAdd(sender, args),
                "remove" => handleAdminRemove(sender, args),
                "list"   => handleAdminList(sender),
                _        => true,
            };
        }

        // Everything below is in-game admin tooling. Needs a player, and
        // that player must be on the admin list.
        if (sender is not Player player)
        {
            sender.sendMessage("/duel admin " + sub + " must be run in-game by a duel admin.");
            return true;
        }

        var admins = BanditDuels.Instance.Admins;
        if (admins == null || !admins.isAdmin(player.getName()))
        {
            player.sendMessage("§cYou aren't a duel admin. Ask the server operator to run §f/duel admin add " + player.getName() + " §cin the server console.");
            return true;
        }

        switch (sub)
        {
            case "setup":      return handleSetupGrid(player);
            case "guard":      return handleGuardToggle(player, args);
            case "lobbyguard": return handleLobbyGuardToggle(player, args);
            case "setspawn":   return handleSetSpawn(player);
            case "setbounds":  return handleSetBounds(player, args);
            case "lobbyinfo":  return handleLobbyInfo(player);
            default:
                player.sendMessage("§cUnknown admin subcommand. Try §f/duel admin §cwith no arguments for the list.");
                return true;
        }
    }

    private void sendPlayerAdminHelp(Player p)
    {
        p.sendMessage("§6Admin commands (you must be on the admin list):");
        p.sendMessage("§7- §f/duel admin setup §7- clone forest_1 / ice_1 to fill the 5x2 grid");
        p.sendMessage("§7- §f/duel admin guard [on|off] §7- toggle arena block protection");
        p.sendMessage("§7- §f/duel admin lobbyguard [on|off] §7- toggle lobby block protection");
        p.sendMessage("§7- §f/duel admin setspawn §7- set lobby spawn to where you're standing");
        p.sendMessage("§7- §f/duel admin setbounds <x1> <y1> <z1> <x2> <y2> <z2> §7- set lobby AABB");
        p.sendMessage("§7- §f/duel admin lobbyinfo §7- show current lobby config");
        p.sendMessage("§7(add/remove/list are server-console only.)");
    }

    private void sendConsoleAdminHelp(CommandSender sender)
    {
        sender.sendMessage("Server-console admin commands:");
        sender.sendMessage("  /duel admin add <name>    - grant a player the admin role");
        sender.sendMessage("  /duel admin remove <name> - revoke a player's admin role");
        sender.sendMessage("  /duel admin list          - show the current admin list");
        sender.sendMessage("Players with the admin role can use setup / guard / setspawn / setbounds / lobbyinfo in-game.");
    }

    private bool handleAdminAdd(CommandSender sender, string[] args)
    {
        if (args.Length < 3)
        {
            sender.sendMessage("Usage: /duel admin add <name>");
            return true;
        }
        var admins = BanditDuels.Instance.Admins;
        if (admins == null) { sender.sendMessage("Admin system unavailable."); return true; }

        var name = args[2];
        if (admins.add(name))
            sender.sendMessage("Added '" + name + "' as a duel admin. Total admins: " + admins.all().Count + ".");
        else
            sender.sendMessage("'" + name + "' is already a duel admin.");
        return true;
    }

    private bool handleAdminRemove(CommandSender sender, string[] args)
    {
        if (args.Length < 3)
        {
            sender.sendMessage("Usage: /duel admin remove <name>");
            return true;
        }
        var admins = BanditDuels.Instance.Admins;
        if (admins == null) { sender.sendMessage("Admin system unavailable."); return true; }

        var name = args[2];
        if (admins.remove(name))
            sender.sendMessage("Removed '" + name + "' from duel admins. Total admins: " + admins.all().Count + ".");
        else
            sender.sendMessage("'" + name + "' was not in the admin list.");
        return true;
    }

    private bool handleAdminList(CommandSender sender)
    {
        var admins = BanditDuels.Instance.Admins;
        if (admins == null) { sender.sendMessage("Admin system unavailable."); return true; }

        var all = admins.all();
        if (all.Count == 0)
        {
            sender.sendMessage("No duel admins configured. Use /duel admin add <name> to grant the role.");
            return true;
        }
        sender.sendMessage("Duel admins (" + all.Count + "):");
        foreach (var n in all) sender.sendMessage("  - " + n);
        return true;
    }

    private bool handleSetSpawn(Player p)
    {
        var loc = p.getLocation();
        var world = loc.getWorld();
        if (world == null)
        {
            p.sendMessage("§cCannot determine your current world.");
            return true;
        }
        BanditDuels.Instance.Lobby.setSpawn(
            world.getName(),
            loc.getX(), loc.getY(), loc.getZ(),
            loc.getYaw(), loc.getPitch());
        p.sendMessage("§a[Lobby] Safe spawn set to "
            + loc.getX().ToString("0.##") + ", "
            + loc.getY().ToString("0.##") + ", "
            + loc.getZ().ToString("0.##")
            + " in " + world.getName() + ".");
        return true;
    }

    private bool handleSetBounds(Player p, string[] args)
    {
        if (args.Length < 8)
        {
            p.sendMessage("§cUsage: /duel admin setbounds <x1> <y1> <z1> <x2> <y2> <z2>");
            return true;
        }
        if (!int.TryParse(args[2], out var x1) || !int.TryParse(args[3], out var y1) || !int.TryParse(args[4], out var z1) ||
            !int.TryParse(args[5], out var x2) || !int.TryParse(args[6], out var y2) || !int.TryParse(args[7], out var z2))
        {
            p.sendMessage("§cCoordinates must be integers.");
            return true;
        }
        var world = p.getLocation().getWorld();
        if (world == null)
        {
            p.sendMessage("§cCannot determine your current world.");
            return true;
        }
        BanditDuels.Instance.Lobby.setBounds(world.getName(), x1, y1, z1, x2, y2, z2);
        p.sendMessage("§a[Lobby] Bounds set to ("
            + Math.Min(x1, x2) + "," + Math.Min(y1, y2) + "," + Math.Min(z1, z2) + ") to ("
            + Math.Max(x1, x2) + "," + Math.Max(y1, y2) + "," + Math.Max(z1, z2) + ") in " + world.getName() + ".");
        return true;
    }

    private bool handleLobbyInfo(Player p)
    {
        var l = BanditDuels.Instance.Lobby;
        var c = l.Config;
        p.sendMessage("§6Lobby config:");
        p.sendMessage("§7  world:  §f" + c.WorldName);
        p.sendMessage("§7  spawn:  §f" + (l.HasSpawn
            ? c.SpawnX.ToString("0.##") + ", " + c.SpawnY.ToString("0.##") + ", " + c.SpawnZ.ToString("0.##")
              + " yaw=" + c.SpawnYaw + " pitch=" + c.SpawnPitch
            : "not set"));
        p.sendMessage("§7  bounds: §f" + (l.HasBounds
            ? "(" + c.BoundsMinX + "," + c.BoundsMinY + "," + c.BoundsMinZ + ") to ("
              + c.BoundsMaxX + "," + c.BoundsMaxY + "," + c.BoundsMaxZ + ")"
            : "not set"));
        return true;
    }

    private bool handleGuardToggle(Player p, string[] args)
    {
        var guard = BanditDuels.Instance.ArenaGuard;
        if (args.Length < 3)
        {
            p.sendMessage("§7Arena block protection is currently §f" + (guard.BlockProtectionEnabled ? "ON" : "OFF") + ".");
            p.sendMessage("§7Use §f/duel admin guard on §7or §foff§7.");
            return true;
        }
        switch (args[2].ToLowerInvariant())
        {
            case "on":  case "true":  case "1":
                guard.BlockProtectionEnabled = true;
                p.sendMessage("§aArena block protection ON§a.");
                return true;
            case "off": case "false": case "0":
                guard.BlockProtectionEnabled = false;
                p.sendMessage("§eArena block protection OFF§e. You can break/place inside arenas now.");
                return true;
            default:
                p.sendMessage("§cExpected §fon §cor §foff§c.");
                return true;
        }
    }

    private bool handleLobbyGuardToggle(Player p, string[] args)
    {
        var guard = BanditDuels.Instance.LobbyGuard;
        if (args.Length < 3)
        {
            p.sendMessage("§7Lobby block protection is currently §f" + (guard.BlockProtectionEnabled ? "ON" : "OFF") + ".");
            p.sendMessage("§7Use §f/duel admin lobbyguard on §7or §foff§7.");
            return true;
        }
        switch (args[2].ToLowerInvariant())
        {
            case "on":  case "true":  case "1":
                guard.BlockProtectionEnabled = true;
                p.sendMessage("§aLobby block protection ON§a.");
                return true;
            case "off": case "false": case "0":
                guard.BlockProtectionEnabled = false;
                p.sendMessage("§eLobby block protection OFF§e. You can break/place/interact in the lobby now (don't forget to turn it back on).");
                return true;
            default:
                p.sendMessage("§cExpected §fon §cor §foff§c.");
                return true;
        }
    }

    private bool handleSetupGrid(Player p)
    {
        var templates = _arenas.Templates;
        if (templates.Count == 0)
        {
            p.sendMessage("§cNo arena templates configured. Edit plugins/BanditDuels-data/arenas.json first.");
            return true;
        }

        var jobs = new List<ArenaCloner.Job>();
        foreach (var t in templates)
        {
            int cols = Math.Max(1, t.GridColumns);
            if (cols <= 1) continue;  // nothing to clone for single-arena templates

            var sourceName = t.NamePrefix + "_1";
            var source = _arenas.findByName(sourceName);
            if (source == null)
            {
                p.sendMessage("§cSource arena " + sourceName + " not found; skipping template.");
                continue;
            }

            for (int col = 1; col < cols; col++)
            {
                jobs.Add(new ArenaCloner.Job
                {
                    Label     = t.NamePrefix + "_" + (col + 1),
                    WorldName = source.WorldName,
                    SrcMin    = source.BoundsMin,
                    SrcMax    = source.BoundsMax,
                    Offset    = (col * t.GridStrideX, col * t.GridStrideY, col * t.GridStrideZ),
                });
            }
        }

        if (jobs.Count == 0)
        {
            p.sendMessage("§7Nothing to clone. All templates have GridColumns <= 1.");
            return true;
        }

        long totalBlocks = 0;
        foreach (var j in jobs)
        {
            long w = j.SrcMax.x - j.SrcMin.x + 1;
            long h = j.SrcMax.y - j.SrcMin.y + 1;
            long d = j.SrcMax.z - j.SrcMin.z + 1;
            totalBlocks += w * h * d;
        }

        p.sendMessage("§6[Setup] §7Cloning " + jobs.Count + " arenas (" + totalBlocks + " blocks total).");
        p.sendMessage("§7         Estimated time: ~" + Math.Max(1, totalBlocks / 80000) + "s. Server may stutter briefly.");

        ArenaCloner.runJobs(
            BanditDuels.Instance,
            jobs,
            blocksPerTick: 4000,
            onJobStart:    j => p.sendMessage("§7[Setup] Building §f" + j.Label + " §7(+" + j.Offset.dx + " X)..."),
            onJobComplete: j => p.sendMessage("[Setup] " + j.Label + " §7done (" + j.Done + " blocks)."),
            onAllComplete: () => p.sendMessage("§a[Setup] All arenas built. Try §f/duel arenas §ato see them."));

        return true;
    }

    private void sendUsage(Player p)
    {
        p.sendMessage("§6[Duel] §7Commands:");
        p.sendMessage("§7- §f/duel <player> <kit> §7- challenge a player");
        p.sendMessage("§7- §f/duel accept <player>");
        p.sendMessage("§7- §f/duel deny <player>");
        p.sendMessage("§7- §f/duel queue <kit> §7- wait for an opponent on this kit");
        p.sendMessage("§7- §f/duel leave §7- leave your current queue");
        p.sendMessage("§7- §f/duel queues §7- list active queues");
        p.sendMessage("§7- §f/duel stats [player] §7- show wins/losses (self or by name)");
        p.sendMessage("§7- §f/duel top [kit] §7- leaderboard, optional kit filter");
        p.sendMessage("§7- §f/duel kits §7- list available kits");
        p.sendMessage("§7- §f/duel arenas §7- list arenas");
    }

    private void sendConsoleUsage(CommandSender sender)
    {
        sender.sendMessage("BanditDuels server-console commands:");
        sender.sendMessage("  /duel admin add <name>    - grant a player the admin role");
        sender.sendMessage("  /duel admin remove <name> - revoke a player's admin role");
        sender.sendMessage("  /duel admin list          - show the current admin list");
        sender.sendMessage("All other /duel commands must be run by a player in-game.");
    }
}
