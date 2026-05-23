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
                if (!LCEPermsBridge.requirePerm(player, "banditduels.accept")) return true;
                return handleAccept(player, args);

            case "deny":
            case "decline":
                // Denying is harmless - leave open so a player can always
                // refuse a challenge regardless of perms.
                return handleDeny(player, args);

            case "kits":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.kits.list")) return true;
                listKits(player);
                return true;

            case "arenas":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.arenas.list")) return true;
                listArenas(player);
                return true;

            case "queue":
            case "q":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.queue.join")) return true;
                return handleQueue(player, args);

            case "party":
                // Per-subcommand party perms are checked inside handleParty
                // so each party action can be gated independently.
                return handleParty(player, args);

            case "leave":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.leave")) return true;
                return handleLeave(player);

            case "queues":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.queue.list")) return true;
                listQueues(player);
                return true;

            case "stats":
                // Self vs others split inside handleStats: stats.self for /duel
                // stats (no args), stats.others for /duel stats <name>.
                return handleStats(player, args);

            case "top":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.top")) return true;
                return handleTop(player, args);

            default:
                if (!LCEPermsBridge.requirePerm(player, "banditduels.challenge")) return true;
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

        // Per-kit gate: check banditduels.kit.<id> before forwarding to
        // the request manager. Done after the target check so a player
        // gets the more useful 'not online' error first if both apply.
        var kitNode = "banditduels.kit." + kitId.ToLowerInvariant();
        if (!LCEPermsBridge.requirePerm(p, kitNode)) return true;

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
        int shown = 0;
        foreach (var k in _kits.all())
        {
            // Per-kit gating: only show kits the player can actually use.
            // Avoids the "challenged with op kit, got 'no permission'" UX trap.
            if (!LCEPermsBridge.has(p, "banditduels.kit." + k.Id.ToLowerInvariant())) continue;
            p.sendMessage("- " + k.Id + " (" + k.DisplayName + ")");
            shown++;
        }
        if (shown == 0)
            p.sendMessage("§7(no kits available to you)");
    }

    private void listArenas(Player p)
    {
        p.sendMessage("§6Registered arenas (" + _arenas.count() + "):");
        foreach (var a in _arenas.all())
            p.sendMessage("- " + a.Name + " [" + a.MinTeamSize + "v" + a.MinTeamSize
                + (a.MaxTeamSize != a.MinTeamSize ? "-" + a.MaxTeamSize + "v" + a.MaxTeamSize : "")
                + "] at " + a.BoundsMin + " to " + a.BoundsMax);
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
        if (BanditDuels.Instance.Parties?.isInParty(p.getUniqueId()) == true)
        {
            p.sendMessage("§cLeave your party before joining the 1v1 queue.");
            return true;
        }

        // Per-kit gate. Matches handleChallenge so /duel queue <kit> can't
        // bypass kit perms by going through the queue instead of a direct
        // challenge.
        var kitNode = "banditduels.kit." + args[1].ToLowerInvariant();
        if (!LCEPermsBridge.requirePerm(p, kitNode)) return true;

        p.sendMessage(queues.joinQueue(p, args[1]));
        return true;
    }

    private bool handleParty(Player p, string[] args)
    {
        var parties = BanditDuels.Instance.Parties;
        if (parties == null)
        {
            p.sendMessage("§cParty system is not available.");
            return true;
        }

        if (args.Length < 2)
        {
            sendPartyHelp(p);
            return true;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "create":
                if (!LCEPermsBridge.requirePerm(p, "banditduels.party.create")) return true;
                if (args.Length < 4)
                {
                    p.sendMessage("§cUsage: /duel party create <kit> <2v2|3v3>");
                    return true;
                }
                p.sendMessage(parties.create(p, args[2], args[3]));
                return true;

            case "invite":
                if (!LCEPermsBridge.requirePerm(p, "banditduels.party.invite")) return true;
                if (args.Length < 3)
                {
                    p.sendMessage("§cUsage: /duel party invite <player>");
                    return true;
                }
                p.sendMessage(parties.invite(p, args[2]));
                return true;

            case "accept":
                if (!LCEPermsBridge.requirePerm(p, "banditduels.party.accept")) return true;
                p.sendMessage(parties.accept(p, args.Length >= 3 ? args[2] : null));
                return true;

            case "decline":
            case "deny":
                // Declining a party invite is harmless. Leave open.
                p.sendMessage(parties.decline(p, args.Length >= 3 ? args[2] : null));
                return true;

            case "disband":
                if (!LCEPermsBridge.requirePerm(p, "banditduels.party.disband")) return true;
                p.sendMessage(parties.disband(p));
                return true;

            case "leave":
                if (!LCEPermsBridge.requirePerm(p, "banditduels.party.leave")) return true;
                p.sendMessage(parties.leave(p));
                return true;

            case "remove":
                if (!LCEPermsBridge.requirePerm(p, "banditduels.party.remove")) return true;
                if (args.Length < 3)
                {
                    p.sendMessage("§cUsage: /duel party remove <player>");
                    return true;
                }
                p.sendMessage(parties.remove(p, args[2]));
                return true;

            case "info":
                if (!LCEPermsBridge.requirePerm(p, "banditduels.party.info")) return true;
                parties.sendInfo(p);
                return true;

            default:
                sendPartyHelp(p);
                return true;
        }
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

        // Gate self vs others separately so a server can let players
        // look up their own stats while restricting lookups of others.
        bool isOtherLookup = args.Length >= 2;
        if (isOtherLookup)
        {
            if (!LCEPermsBridge.requirePerm(p, "banditduels.stats.others")) return true;
        }
        else
        {
            if (!LCEPermsBridge.requirePerm(p, "banditduels.stats.self")) return true;
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

        // /duel admin add|remove|list are deprecated. The legacy admins.json
        // list is no longer consulted - permissions flow through LCEPerms.
        if (sub == "add" || sub == "remove" || sub == "list")
        {
            sender.sendMessage("[BanditDuels] /duel admin " + sub + " is deprecated. Use /lp instead:");
            sender.sendMessage("[BanditDuels]   lp group admin permission set banditduels.admin.* true");
            sender.sendMessage("[BanditDuels]   lp group admin permission set banditduels.bypass.lobby true");
            sender.sendMessage("[BanditDuels]   lp group admin permission set banditduels.bypass.chat true");
            sender.sendMessage("[BanditDuels]   lp user <name> parent add admin");
            return true;
        }

        if (sender is not Player player)
        {
            sender.sendMessage("/duel admin " + sub + " must be run in-game.");
            return true;
        }

        switch (sub)
        {
            case "setup":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.admin.setup")) return true;
                return handleSetupGrid(player);
            case "guard":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.admin.guard")) return true;
                return handleGuardToggle(player, args);
            case "lobbyguard":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.admin.lobbyguard")) return true;
                return handleLobbyGuardToggle(player, args);
            case "setspawn":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.admin.setspawn")) return true;
                return handleSetSpawn(player);
            case "setbounds":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.admin.setbounds")) return true;
                return handleSetBounds(player, args);
            case "lobbyinfo":
                if (!LCEPermsBridge.requirePerm(player, "banditduels.admin.lobbyinfo")) return true;
                return handleLobbyInfo(player);
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
                BanditDuels.Instance.Lobby.Config.BlockProtectionEnabled = true;
                BanditDuels.Instance.Lobby.save();
                p.sendMessage("§aLobby block protection ON§a. (persisted)");
                return true;
            case "off": case "false": case "0":
                guard.BlockProtectionEnabled = false;
                BanditDuels.Instance.Lobby.Config.BlockProtectionEnabled = false;
                BanditDuels.Instance.Lobby.save();
                p.sendMessage("§eLobby block protection OFF§e. (persisted) Delegating to another plugin? Make sure something else protects the lobby.");
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
        p.sendMessage("§7- §f/duel party create <kit> <2v2|3v3> §7- create a team party");
        p.sendMessage("§7- §f/duel leave §7- leave your current queue");
        p.sendMessage("§7- §f/duel queues §7- list active queues");
        p.sendMessage("§7- §f/duel stats [player] §7- show wins/losses (self or by name)");
        p.sendMessage("§7- §f/duel top [kit] §7- leaderboard, optional kit filter");
        p.sendMessage("§7- §f/duel kits §7- list available kits");
        p.sendMessage("§7- §f/duel arenas §7- list arenas");
    }

    private void sendPartyHelp(Player p)
    {
        p.sendMessage("§6[Party] §7Commands:");
        p.sendMessage("§7- §f/duel party create <kit> <2v2|3v3>");
        p.sendMessage("§7- §f/duel party invite <player>");
        p.sendMessage("§7- §f/duel party accept [leader]");
        p.sendMessage("§7- §f/duel party decline [leader]");
        p.sendMessage("§7- §f/duel party remove <player>");
        p.sendMessage("§7- §f/duel party leave");
        p.sendMessage("§7- §f/duel party disband");
        p.sendMessage("§7- §f/duel party info");
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
