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

### The plugin order is the game's, completed from what Amethyst saved
Amethyst's `loadorder.txt` is its plugin panel's memory, written when the panel saves. A new profile starts with only
the ten plugins Amethyst pins first, and a newly installed mod's plugin is added to `plugins.txt` alone. On deploy
Amethyst copies `plugins.txt` into the game's Proton prefix unchanged, so that file is what the game reads. houseCARL
therefore completes the saved order the way the game loads (`Mo2LoadOrder.CompleteOrder`):
1. `loadorder.txt` as listed;
2. every base master (`Skyrim.esm`, `Update.esm`, the three DLC) and every `Skyrim.ccc` entry whose file is in the game's
   Data folder but not listed, in the game's order, after the last such plugin listed;
3. every `plugins.txt` entry not listed, in that file's order.

A complete `loadorder.txt` is left as it is. A missing one is not rebuilt: it stays empty and is named, as for MO2.
Names compare without regard to case; Steam on Linux installs most Creation Club files in lower case while
`Skyrim.ccc` lists them in mixed case. Amethyst's panel can show unlisted Creation Club plugins sorted by type and
name; houseCARL follows the game's `Skyrim.ccc` order instead. Plugins Amethyst's panel would recover from a deploy
(a mod plugin missing from `plugins.txt`) are not loaded by the game until Amethyst writes them back, so houseCARL
leaves them out too.

The rule is the manager's: `IManagerInstance.PartialLoadOrder` is true for Amethyst and false for MO2, travels with
the roots (`Mo2Roots.PartialLoadOrder`, a required member so no place that builds roots can leave it out), and every
composition read takes it, including the re-reads after a profile switch or a profile-file change. MO2's
`loadorder.txt` lists everything, so MO2 profiles are read exactly as before.

### Profiles with their own mods, and profiles sharing them
A profile with `profile_specific_mods` reads mods and overwrite inside its own folder; every other profile of the game
shares the staging root's. A switch between the two kinds moves the mods folder, and the service re-resolves under the
new one on the next call, so one profile's mods are never seen from the other. Reading a profile other than the active
one (`profile=` on the status tool) uses that profile's own mods folder (`IManagerInstance.ModsDirFor`); MO2's profiles
all share the instance's.

### Locked mods
A `*Name` line in `modlist.txt` is Amethyst's enabled-and-locked mod, and counts as enabled when that mod folder
exists. MO2 writes `*` for DLC and unmanaged entries, which have no folder, so they stay out as before.

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
- `AmethystLoadOrderTests` — the service end to end with real plugins: the mod's override wins, a plugin listed only in
  `plugins.txt` loads, a plugin ticked mid-session loads, a deployed patch has one copy (never Data's link), a deploy,
  restore and profile switch are picked up without a restart, a switch between a profile with its own mods and one
  with the shared mods swaps both ways, and another profile is read from its own mods folder without switching.
- `AmethystPluginOrderTests` — the completed order (game plugins in `Skyrim.ccc` order, then `plugins.txt`), the same
  files read as MO2 unchanged, a complete order left alone, a missing one left empty and named, and locked mods.
- `StatusInstanceProfileTests` and the MO2 resolve tests hold that MO2 is unchanged.

## Where
`src/housecarl-core/AmethystInstance.cs` (`Resolve`, `TryResolve`, `Validate`, `GameConfigFor`, `StagingRoot`,
`VanillaDataDir`, `AmethystManagerInstance`); `Mo2LoadOrder.CompleteOrder` and `ParseModlist` in
`src/housecarl-core/Mo2LoadOrder.cs`; `src/housecarl-core/ManagerInstance.cs` (`ManagerInstancePaths`,
`IManagerInstance`, `ManagerInstance.Open`); the service's `_instance` in `src/housecarl-mcp/LoadOrderService.cs`.
Tool: `housecarl_set_mo2_instance`.

## Related
- [`mo2-instance.md`](mo2-instance.md) — the other manager, and the profile-file formats both share.
- [`../decisions/0007-amethyst-is-read-from-its-files.md`](../decisions/0007-amethyst-is-read-from-its-files.md) — why not Amethyst's database.
- [`../linux-and-amethyst.md`](../linux-and-amethyst.md) — the rules the Amethyst work follows, and their source.
