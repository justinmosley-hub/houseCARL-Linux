---
updated: 2026-10-09
covers: [src/housecarl-core/DiskPath.cs, src/housecarl-core/GamePath.cs, src/housecarl-core/AssetResolver.cs, src/housecarl-core/OffOrderAssetSource.cs, src/housecarl-core/AssetRenameService.cs, src/housecarl-core/PapyrusSourceRoots.cs, src/housecarl-core/Mo2ModMeta.cs, src/housecarl-mcp/PluginPaths.cs]
---
# Game paths on disk

## What it is
A path the game knows (`meshes\armor\cuirass.nif`) is written with `\` and matched without regard to case. Windows
gives both for free; Linux gives neither. `DiskPath` turns a game path into a real path under a root folder, and
`GamePath` takes one apart, so the rest of the code keeps writing game paths the Windows way on every OS.

## Contracts
- A game path inside houseCARL keeps `\` on every OS. It is never built with `Path.Combine` and never taken apart with `Path.GetFileName` or `Path.GetDirectoryName`, which split on `/` only on Linux.
- A game path meets the disk only through `DiskPath.Combine(root, rel)`. On Windows it is `Path.Combine(root, rel)`, unchanged.
- On Linux each segment takes the casing found on disk; an exact-case entry wins over a case-only twin, otherwise the ordinal-first twin wins. From the first segment that does not exist, the given spelling is kept, so a write creates the folders as asked.
- `DiskPath.Combine` never throws: a folder that will not list is treated as not there, and the read that follows reports it.
- `AssetResolver` descends through its own cached listings and takes each folder's spelling from the listing, so a warm lookup costs no extra system call.
- A root folder handed in from configuration (instance folder, mod folder) is a real disk path already and is joined with `Path.Combine`.
- A game path never starts with a drive letter, on any OS: `GamePath.HasDriveLetter` refuses `C:\…` where `Path.IsPathRooted` would only on Windows.
- "Is this disk path under that folder?" builds its prefix with `Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar`, never `root + "\\"`.
- Matching a disk path's tail against a game path is done segment by segment, ignoring case (`Mo2ModMeta.ModRootForLooseFile`), never with `EndsWith` on the two strings.

## Pinned by
- `DiskPathTests` — other casing and either separator reach the file; a missing tail keeps the given spelling; a rooted second argument replaces the root; an exact-case twin wins.
- `AssetResolverTests.PathFormsResolveToTheSameWinner` — upper case, a leading `\`, and `/` all reach one loose winner.
- `AssetLooseFreshnessTests` — a warmed subtree sees files appear, change and vanish through the listing-spelled descent.
- `CompactVoiceCarryTests`, `CompactFacegenCarryTests` — a renamed voice or FaceGen file lands at a game path built with `\`.
- `PapyrusSourceRootsDiscoverTests.ALayoutInOtherCasingIsFound` — `source/scripts` shipped in lower case is found.
- `AssetResolverTests.ADriveRootedOrEscapingPathIsRejected` — `C:\Windows\evil.nif` is refused on Linux too.
- `NativePairingProvenanceTests` — a file under the mods, overwrite or Data folder is placed in that layer.
- `Mo2ModMetaReadTests.TheModRootOfALooseFileIsFoundAcrossSeparatorsAndCasing`; `SkseInventoryNameTests` — a nested SKSE config is listed by its file name.

## Where
`src/housecarl-core/DiskPath.cs` (`Combine`), `src/housecarl-core/GamePath.cs` (`FileName`, `DirectoryName`,
`FileNameWithoutExtension`). Callers: `AssetResolver.ResolveProviders`, `EnumerateUnder`, `WarmSubtree`,
`TryDescend`; `OffOrderAssetSource`; `AssetRenameService`; the place and NIF write lanes in
`src/housecarl-mcp/AssetLayers.cs`.

## Related
- [`assets.md`](assets.md) — which copy of a file the game uses; this note is how that copy is found on disk.
- [`../linux-and-amethyst.md`](../linux-and-amethyst.md) — the fork's entry point for a Windows reader.
- [`../decisions/0006-game-paths-resolve-case-insensitively-on-linux.md`](../decisions/0006-game-paths-resolve-case-insensitively-on-linux.md) — why this is code and not a file-system setting.
