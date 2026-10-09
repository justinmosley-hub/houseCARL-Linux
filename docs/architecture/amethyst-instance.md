---
updated: 2026-10-09
covers: [src/housecarl-core/AmethystInstance.cs, src/housecarl-core/ManagerInstance.cs, src/housecarl-mcp/LoadOrderService.cs, src/housecarl-mcp/SetupTools.cs]
---
# The Amethyst instance

## What it is
houseCARL reads an Amethyst Mod Manager game off disk, the way it reads an MO2 instance: from the manager's own
files, never through Amethyst while it runs. Amethyst keeps MO2's profile layout, so once the roots are found the
load order is read by `Mo2LoadOrder` unchanged. This note is how the roots are found.

## Contracts

### Which manager a folder is
`ManagerInstance.Open` decides once, from what the configured folder holds: `ModOrganizer.ini` is MO2; `paths.json`
is an Amethyst game's config folder (`~/.config/AmethystModManager/games/<game>`, or the Flatpak's under
`~/.var/app/io.github.Amethyst.ModManager/config`); a folder holding `profiles/` that some game's `paths.json` names
as its staging folder is that game. Anything else is handed to MO2, whose resolve names what is missing, Amethyst
included. The one setup tool (`housecarl_set_mo2_instance`) and the one saved setting take either.

### The roots, from Amethyst's own rules
- **Staging root** (where `mods/`, `overwrite/` and `profiles/` live): `paths.json` `staging_path`; when empty,
  `$MOD_MANAGER_PROFILES_DIR/<game>`, else an existing `<config>/Profiles/<game>`, else `~/Games/Amethyst/Profiles/<game>`.
- **Active profile**: `deploy_state.json` `last_active_profile`, else `default` (Amethyst's `get_last_active_profile`).
- **Profile settings**: `profile_state.json`'s `profile_settings`, else the legacy `profile_settings.json`.
  `profile_specific_mods: true` puts `mods/` and `overwrite/` inside the profile folder; a `game_path` there wins over
  `paths.json`'s.
- **Game Data**: a link deploy moves `Data` to its sibling `Data_Core` and rebuilds `Data` from links to the game and
  the mods. So the untouched game is `Data_Core` while it exists and `Data` otherwise. Deployed `Data` is never read: it
  would show every mod plugin twice and is the merged result, not a source.
- The profile's `modlist.txt`, `loadorder.txt` and `plugins.txt` are MO2's formats for Skyrim SE (`*` marks an enabled
  plugin, a disabled one is listed bare, base-game plugins are left out of `plugins.txt`).

### Bad state is refused, never guessed past
A missing or malformed `paths.json` or profile settings file, a missing profile folder, mods folder, game folder or
`loadorder.txt` is named, and nothing is derived. Amethyst recording a deploy (`deploy_active`) with no `Data_Core`
beside `Data` is refused unless VFS is on (`game_settings.json` `vfs_enabled`), which leaves `Data` alone on disk.

### Freshness
The service re-derives when the manager's fingerprint changes: the stamps of `paths.json`, `game_settings.json`,
`deploy_state.json` and the active profile's `profile_state.json`, plus which Data folder is the game. A profile
switch, a deploy and a restore in Amethyst all rewrite `deploy_state.json`, so each is picked up on the next call.
Stamps compare by exact ticks (`FileStamp.Key`), never the second-rounded default text.

## Pinned by
- `AmethystInstanceResolveTests` — every rule above on a synthetic setup: the roots, the active profile and its
  default, profile-specific mods, a profile's game path, Data_Core while deployed, the refusals, the default staging
  root, finding a game from its staging folder, telling the managers apart, and the fingerprint moving.
- `AmethystLoadOrderTests` — the service end to end with real plugins: the mod's override wins, a deployed patch has
  one copy (never Data's link), and a deploy, restore and profile switch are picked up without a restart.
- `StatusInstanceProfileTests` and the MO2 resolve tests hold that MO2 is unchanged.

## Where
`src/housecarl-core/AmethystInstance.cs` (`Resolve`, `TryResolve`, `Validate`, `GameConfigFor`, `StagingRoot`,
`VanillaDataDir`, `AmethystManagerInstance`); `src/housecarl-core/ManagerInstance.cs` (`ManagerInstancePaths`,
`IManagerInstance`, `ManagerInstance.Open`); the service's `_instance` in `src/housecarl-mcp/LoadOrderService.cs`.
Tool: `housecarl_set_mo2_instance`.

## Related
- [`mo2-instance.md`](mo2-instance.md) — the other manager, and the profile-file formats both share.
- [`../decisions/0007-amethyst-is-read-from-its-files.md`](../decisions/0007-amethyst-is-read-from-its-files.md) — why not Amethyst's database.
- [`../linux-and-amethyst.md`](../linux-and-amethyst.md) — the rules the Amethyst work follows, and their source.
