# FourKit-BanditDuels

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
