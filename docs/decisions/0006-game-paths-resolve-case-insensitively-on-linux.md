# ADR 0006 — Game paths resolve case-insensitively on Linux, in code

**Class:** ARCHIVE (ADR). Immutable once merged; superseded by a later ADR, never edited.
**Date:** 2026-10-09 · **Status:** proposed (Linux / Amethyst fork)

## Context

houseCARL was written for Windows, where NTFS ignores case and accepts `\` and `/` as separators.
Game paths are spelled the way the game and the Creation Kit spell them — `Meshes\Armor\Foo.NIF` — and
reached the disk through `Path.Combine(root, rel)`. On Linux that breaks in two ways at once: `\` is an
ordinary filename character, so the join names one file called `Meshes\Armor\Foo.NIF`; and with the
separators fixed, `Meshes` and `meshes` are different folders. Mod archives are authored on Windows, so
on-disk casing is whatever the mod author typed and rarely matches the record.

The first Linux test run showed the scale: 98 `Path.Combine` and about 280 `Path.GetFileName` /
`GetDirectoryName` calls in the server, the asset ones among them failing, and a hundred tests passing
only because their fixtures made the same flat backslash-named files the code looked for.

Options considered:
1. **Case-insensitive folders on the file system** (ext4/f2fs `casefold`, `chattr +F`). No code, but it
   needs a file system formatted with the feature and an empty folder to switch on, so it cannot be a
   requirement.
2. **A library.** Neither Mutagen (`DataRelativePath` compares strings, never the disk) nor Noggog
   (`FilePath`, `DirectoryPath`) resolves casing on disk, and no maintained package does.
3. **One small helper in core** that every game path goes through, built on .NET's own
   `EnumerationOptions.MatchCasing`.

## Decision

Option 3. `DiskPath.Combine(root, rel)` joins a game path onto a real folder: on Windows it is exactly
`Path.Combine`; elsewhere each segment takes the casing found on disk. `GamePath` splits a game path on
`\` and `/` on every OS. Game paths keep `\` inside houseCARL everywhere. Contract:
[`architecture/linux-paths.md`](../architecture/linux-paths.md).

## Consequences

- Windows behaviour does not change: the helper is `Path.Combine` there, and every test that passed on
  Windows still means the same thing.
- A new call site that joins a game path with `Path.Combine` works on Windows and fails on Linux. The
  Linux CI job is what catches it.
- Test fixtures that write game paths go through `DiskPath.Combine` too; on Linux, a fixture that uses
  `Path.Combine` writes a flat file and the test fails, which is the signal.
- Linux allows `Meshes` and `meshes` side by side, which Windows cannot hold. The helper picks one
  deterministically (exact case, else ordinal-first) rather than merging them; Amethyst's own folder-case
  normalisation is the user-side fix.
