# FourKit-BanditDuels

Full 1v1 duels system for FourKit servers running Minecraft Legacy Console Edition. Kits, arenas, queue, stats, lobby protection, private duel chat - the lot.

## Features

- **Multiple kits** - classic, fist, nodebuff, op, pot, soup, uhc (JSON-defined, drop a file in `DefaultKits/` to add more)
- **Random arena selection** - free arenas are chosen uniformly so a fresh server can give you forest or ice on match 1
- **Queue system** - players queue per-kit; the manager pairs them as arenas free up
- **Stats tracking** - wins/losses persisted in `stats.json`, queryable per kit and per player
- **Lobby protection** - block break/place/interact/drop suppressed for non-admins inside the lobby AABB, with a 10-block margin for reach attacks
- **Lobby comfort** - full HP/food refresh every half-second, fall damage cancelled, time/weather locked to noon clear
- **Private duel chat** - duelists only see chat from their opponent; lobby chat doesn't leak in
- **Admin chat override** - configurable `AdminsSeeAllChat` toggle lets staff monitor everything
- **Auto arena reset** - blocks broken or modified during a fight are restored from a snapshot when the match ends
- **Mob culling** - non-player mobs periodically teleported to the void to keep arenas clean
- **Match guards** - escape detection (pearling out), forfeit-on-quit, 5-minute time-cap draws

## Installation

1. Build (see below) or grab the latest `BanditDuels.dll` from Releases
2. Drop it into `<server>/plugins/`
3. Restart the server

## Configuration

`plugins/BanditDuels-data/config.json` is generated on first run:

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

Arenas live in `arenas.json` - templates with grid columns for repeating layouts. Admins are in `admins.json`.

Kits are JSON files under `plugins/BanditDuels-data/kits/` - one per kit. To add a new kit, drop a file in there and restart.

## Commands

| Command | Description |
|---|---|
| `/duel <player> <kit>` | Send a challenge |
| `/duel accept <player>` | Accept an incoming challenge |
| `/duel deny <player>` | Decline an incoming challenge |
| `/duel queue <kit>` | Join the auto-matching queue for a kit |
| `/duel leave` | Leave queue or forfeit your active match |
| `/duel stats [player]` | Show win/loss stats |
| `/duel kits` | List available kits |
| `/duel arenas` | List arenas and current busy state |
| `/duel admin ...` | Admin subcommands (lobby setup, region bounds, force-end, lobbyguard toggle) |
## Building from source

Requires .NET 10 SDK.

```powershell
.\build.ps1 -StopServer
```

The script auto-stops a running `Minecraft.Server.exe`, builds in Release mode, and copies the DLL to `..\..\Server\plugins\`. Or build manually:

```powershell
dotnet build -c Release
```

## License

MIT
