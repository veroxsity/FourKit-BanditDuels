# FourKit-BanditDuels

> **WARNING**: This plugin's admin subcommands are open to everyone unless [LCEPerms](https://github.com/veroxsity/LCEPerms) is also installed. Without LCEPerms, any player can `/duel admin setup`, `/duel admin guard off`, etc. Install LCEPerms to gate access by group.
>
> Permission nodes:
>
> | Subcommand / behaviour | Node |
> |---|---|
> | `/duel admin setup` | `banditduels.admin.setup` |
> | `/duel admin guard` | `banditduels.admin.guard` |
> | `/duel admin lobbyguard` | `banditduels.admin.lobbyguard` |
> | `/duel admin setspawn` | `banditduels.admin.setspawn` |
> | `/duel admin setbounds` | `banditduels.admin.setbounds` |
> | `/duel admin lobbyinfo` | `banditduels.admin.lobbyinfo` |
> | LobbyGuardListener bypass (free roam, no block protection) | `banditduels.bypass.lobby` |
> | DuelChatListener bypass (see all duel chat) | `banditduels.bypass.chat` |
>
> One-time setup from console:
> ```
> lp group admin permission set banditduels.admin.* true
> lp group admin permission set banditduels.bypass.lobby true
> lp group admin permission set banditduels.bypass.chat true
> lp user <yourname> parent add admin
> ```
>
> ### Player-facing permission nodes
>
> All player-facing subcommands are gated. Default group should have these so the plugin works for everyone out of the box.
>
> | Subcommand | Node |
> |---|---|
> | `/duel <player> <kit>` (challenge) | `banditduels.challenge` |
> | `/duel accept <player>` | `banditduels.accept` |
> | `/duel deny <player>` / `decline` | (open - denying is harmless) |
> | `/duel leave` | `banditduels.leave` |
> | `/duel kits` | `banditduels.kits.list` |
> | `/duel arenas` | `banditduels.arenas.list` |
> | `/duel queue <kit>` / `/duel q` | `banditduels.queue.join` |
> | `/duel queues` | `banditduels.queue.list` |
> | `/duel stats` (self) | `banditduels.stats.self` |
> | `/duel stats <player>` (other) | `banditduels.stats.others` |
> | `/duel top [kit]` | `banditduels.top` |
> | `/duel party create` | `banditduels.party.create` |
> | `/duel party invite` | `banditduels.party.invite` |
> | `/duel party accept` | `banditduels.party.accept` |
> | `/duel party decline` / `deny` | (open) |
> | `/duel party disband` | `banditduels.party.disband` |
> | `/duel party leave` | `banditduels.party.leave` |
> | `/duel party remove` | `banditduels.party.remove` |
> | `/duel party info` | `banditduels.party.info` |
>
> ### Per-kit permission nodes
>
> Every kit requires `banditduels.kit.<kitid>` to be challenged with, queued for, or even listed via `/duel kits`. Players who lack a kit's perm won't see it in the kit list at all, removing the "look at op kit, get told no" UX trap.
>
> Default convention with the seeded kits:
>
> | Kit | Required node | Granted to (default install) |
> |---|---|---|
> | `classic` | `banditduels.kit.classic` | default |
> | `fist` | `banditduels.kit.fist` | default |
> | `nodebuff` | `banditduels.kit.nodebuff` | default |
> | `pot` | `banditduels.kit.pot` | default |
> | `soup` | `banditduels.kit.soup` | default |
> | `uhc` | `banditduels.kit.uhc` | default |
> | `op` | `banditduels.kit.op` | vip (donator tier) |
>
> Add a new kit by dropping JSON in `plugins/BanditDuels-data/kits/` and granting the corresponding `banditduels.kit.<id>` perm to whichever group(s) should have it. `banditduels.kit.*` works as an umbrella via LCEPerms wildcard matching (already granted to admin via `banditduels.*`).
>
> Recommended one-time setup for the default group:
> ```
> lp group default permission set banditduels.challenge true
> lp group default permission set banditduels.accept true
> lp group default permission set banditduels.leave true
> lp group default permission set banditduels.kits.list true
> lp group default permission set banditduels.arenas.list true
> lp group default permission set banditduels.queue.join true
> lp group default permission set banditduels.queue.list true
> lp group default permission set banditduels.stats.self true
> lp group default permission set banditduels.stats.others true
> lp group default permission set banditduels.top true
> lp group default permission set banditduels.party.* true
> ```
>
> ### Deprecated
>
> `/duel admin add|remove|list` (the JSON-list management subcommands) now emit a deprecation notice. Use `/lp user <name> parent add admin` instead. The legacy `admins.json` file is no longer consulted - if you have one, the plugin will log a migration notice on startup with the equivalent `/lp` commands.

Full-featured 1v1 duel plugin for FourKit servers running Minecraft Legacy Console Edition. Configurable kits, randomised arena selection, queue system, persistent stats, lobby protection, and isolated duel chat.

## Features

- **JSON-defined kits** loaded from `DefaultKits/*.json` and seeded to `plugins/BanditDuels-data/kits/` on first run. Drop a new JSON in, restart, and it's available.
- **Random arena selection** from configurable templates. Default setup ships 5 forest + 5 ice arenas; matches pick uniformly at random from the free pool.
- **Lobby** with safe spawn, fall damage cancel, hunger/health refresh every tick, periodic boundary enforcement.
- **Block protection** with admin bypass: turn protection on and admins can still edit while regular players are blocked.
- **Isolated duel chat**: duelists only see chat from their match opponent; lobby chat stays in the lobby. Optional `AdminsSeeAllChat` toggle for moderation.
- **Stats** tracked per kit (wins/losses, end reason, duration) in `stats.json`.
- **Match queue**: `/duel queue <kit>` pairs you with the next joiner. Auto-removed if you challenge someone or accept a request.
- **Arena reset**: block snapshots restored after every match so each fight starts on pristine terrain.
- **World keeper**: locks time to noon, optional periodic mob culling.

## Installation

```powershell
.\build.ps1 -StopServer
```

The plugin bundles SQLite via embedded resources. No external dependencies to install.

## Configuration

`plugins/BanditDuels-data/config.json`:

```json
{
  "WorldName": "world",
  "Features": {
    "LockWorldTime": true,
    "NoonTicks": 6000,
    "MobCullEnabled": true,
    "MobCullPeriodTicks": 100,
    "MobCullVoidY": -100.0,
    "AdminsSeeAllChat": true
  }
}
```

Other data files in `plugins/BanditDuels-data/`:

- `arenas.json` - arena templates (per kit grid expansion)
- `admins.json` - admin list (one name per line entry)
- `kits/*.json` - kit definitions
- `lobby.json` - lobby bounds and spawn
- `stats.json` - match history

## Commands

- `/duel <player> <kit>` - challenge a player
- `/duel accept <player>` / `/duel deny <player>`
- `/duel queue <kit>` - join the matchmaking queue for a kit
- `/duel queue leave` - leave the queue
- `/duel stats [player]` - view stats
- `/duel kits` - list available kits
- `/duel admin setup` - interactive setup for lobby bounds, spawn, arenas
- `/duel admin lobbyguard on|off` - toggle lobby block protection
- `/duel admin add <player>` - add an admin

## Adding a kit

Drop a JSON file in `Plugins/BanditDuels/DefaultKits/` matching the existing schema (see `classic.json` as reference). Rebuild and ship. Existing servers won't be affected unless they delete their `kits/` folder; new servers seed all defaults on first run.

## License

MIT
