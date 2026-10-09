# houseCARL on Linux, with Amethyst Mod Manager

This fork runs houseCARL on Linux and reads a load order from
[Amethyst Mod Manager](https://github.com/ChrisDKN/Amethyst-Mod-Manager) as well as from MO2. It is
written for someone who knows houseCARL, C# and Windows, and has not used Linux or Amethyst. Nothing
here changes how houseCARL behaves on Windows.

Status: the server builds and the whole test suite passes on Linux. houseCARL **reads an Amethyst game** (roots,
active profile, load order, assets) and picks up a profile switch, deploy or restore without a restart; it has been
run against a real Amethyst 2.5.3 Skyrim SE install. Writing into Amethyst and Papyrus compile through Wine are next.

## What changed, and where

| Change | Why | Read |
|---|---|---|
| `DiskPath` / `GamePath` in core; game paths go through them | Linux file names are case-sensitive and `\` is not a separator there | [`architecture/linux-paths.md`](architecture/linux-paths.md), [ADR 0006](decisions/0006-game-paths-resolve-case-insensitively-on-linux.md) |
| `HarnessPaths.ServerExe` in the tests | The built server has no `.exe` on Linux | the property itself |
| `IManagerInstance`, `AmethystInstance`; the setup tool takes an MO2 or Amethyst folder; `ReadComposition` takes the mods and Data folders and the manager's `PartialLoadOrder` | houseCARL reads Amethyst as it reads MO2; Amethyst's `loadorder.txt` can be partial, so its order is completed the way the game loads | [`architecture/amethyst-instance.md`](architecture/amethyst-instance.md), [ADR 0007](decisions/0007-amethyst-is-read-from-its-files.md) |
| json written with `\n` on every OS (`JsonWire.Opts`) | .NET indents with the OS line break by default, so Windows and Linux returned different documents | [`architecture/json-wire.md`](architecture/json-wire.md) "One document, whatever the OS" |
| json caveats and asset rows measured before they are written | The item that crossed `max_chars` was written whole; it showed on Linux because line-break width moved where items end | [`architecture/json-wire.md`](architecture/json-wire.md), `AssetSelectTests.NoCaveatOrRowPastTheFirstCrossesTheCeilingAtAnyCap` |
| `WindowsTool` in core starts the Papyrus compiler and BSArch | They are Windows programs: on Linux they run through Wine, handed paths on its `Z:` drive | *Windows tools through Wine* below, [ADR 0008](decisions/0008-windows-tools-run-through-wine.md) |
| Every text and json write render bounded by `max_chars` (`BatchRender.AppendSections`, `RowRoom`) | Not a Linux issue: #1016's three text renders and the six json write renders answered past their cap whenever they cut | [`architecture/render-budget.md`](architecture/render-budget.md) |

## Amethyst: the rules the backend follows

Reading and writing are done ([`architecture/amethyst-instance.md`](architecture/amethyst-instance.md)); how each rule
is met is in *Status*. Most come from an earlier port,
[ni1by2/houseCARL-Amethyst](https://github.com/ni1by2/houseCARL-Amethyst) (GPL-3.0, July 2026, built on houseCARL
1.8). Their work is credited here and in the README. We adopt their findings, not their code: that fork predates
houseCARL 2.0.

1. **Never infer winners from the deployed game `Data/` folder.** Read the active profile and staging state from
   Amethyst. A deployed `Data/` holds Amethyst's links into staging, so it is never treated as vanilla.
2. **Fail loud on bad Amethyst state.** A missing, malformed or unknown-version input is refused by name. There is
   no silent fallback to scanning `Data/`.
3. **Writes go to staging, never to deployed `Data/`.** A new patch mod goes into Amethyst staging, and the user
   refreshes, enables and deploys it in Amethyst.
4. **In-place edits** keep upstream's per-plugin consent, also need an explicit redeploy confirmation, write to
   staging only, and stay pending until a later deploy is verified.
5. **Check the real deploy, not the requested mode.** Amethyst can fall back from hardlinks to symlinks or copies,
   so the backend checks each file's actual link and identity (inode).
6. **Hardlink deployment is the first supported mode.** Symlink and copy are smoke-tested; Amethyst's VFS mode
   comes later.
7. **Both staging layouts** are supported: shared staging and profile-specific staging. A `*`-locked mod is enabled.
8. **No Skyrim game data in CI.** Testing against a real game uses a disposable profile, with backups first.

**Status.** Rules 1, 2, 3, 7 and 8 hold as written. The others are met this way:
- **4:** upstream's per-plugin consent applies unchanged, the write lands in staging, and every in-place answer says
  whether the game now reads the edit (rule 5). There is no separate `confirm_amethyst_redeploy` parameter: it would
  add a parameter to four published tools for a fact the answer already states, and the consent prompt still stops a
  first touch. Nothing yet keeps an edit marked pending across calls; the answer is the record.
- **5:** the check reads the file actually deployed at the plugin's place in Data. A symlink to the written file, or a
  file with the same bytes, means the game reads the edit; anything else means it does not. Comparing bytes rather
  than inodes answers the question asked ("does the game read this edit") and needs no platform call.
- **6:** symlink, hardlink and copy deploys are each tested (`AmethystWriteTests`); the user's own install deploys by
  symlink. VFS mode is recognised and named, not tested against a real VFS.

**Amethyst's file database.** Current Amethyst tracks which mod provides each file in a versioned SQLite database,
the *filegraph* (`native/amethyst_filegraph/src/schema.rs` and `src/Utils/filegraph/service.py` in Amethyst), not
in `filemap.txt` as it used to. Its tables include `profiles` (with a `generation` counter), `winners` and
`deployed_entries`. This finding is also ni1by2's (their hand-off note of 2026-10-02). houseCARL does not need it:
reads use Amethyst's plain files ([ADR 0007](decisions/0007-amethyst-is-read-from-its-files.md)), and the deploy check
reads the deployed files themselves, which is what the game reads.

## Windows tools through Wine

houseCARL starts two Windows programs: the Creation Kit's `PapyrusCompiler.exe` (`housecarl_compile_script`) and
`BSArch.exe` (`housecarl_bsa_repack`). On Linux, `WindowsTool` starts them through Wine and hands each folder path over
on Wine's `Z:` drive (`/home/me/x` is `Z:\home\me\x`), which every prefix maps to `/`.

| Setting | What it does | Default |
|---|---|---|
| `HOUSECARL_WINE` | The Wine to run them with | `wine` on PATH |
| `HOUSECARL_WINEPREFIX` | The Wine prefix (its private "Windows install") they run in | Wine's own: `WINEPREFIX`, else `~/.wine` |

The compiler is a .NET Framework program, so its Wine needs Mono: install the `wine-mono` package, or point
`HOUSECARL_WINE` at Proton's Wine (`<Steam>/steamapps/common/Proton <version>/files/bin/wine`), which carries its own.
Give houseCARL its own prefix rather than a game's, so the compiler never changes the game's prefix. The first compile
in a new prefix takes a while as Wine sets it up. BSArch needs no Mono.

Getting the compiler: install **Skyrim Special Edition: Creation Kit** from Steam into the same game folder. It adds
`Papyrus Compiler/PapyrusCompiler.exe` and the vanilla script sources (`Data/Scripts.zip`, to extract to
`Data/Source/Scripts`). Then `housecarl_set_tool_path tool='papyrus_compiler'` with the `.exe`'s path, as on Windows.

## Windows → Linux, for this codebase

| On Windows | On Linux | What it means here |
|---|---|---|
| `housecarl-mcp.exe` | `housecarl-mcp` (no extension, marked executable) | .NET's apphost is named per OS. |
| NTFS ignores case: `Meshes` = `meshes` | ext4/btrfs do not; both can exist side by side | Every game path reaches the disk through `DiskPath.Combine`. |
| `\` and `/` both separate folders | Only `/` does; `\` is a normal filename character | Game paths keep `\` inside houseCARL; only `DiskPath` / `GamePath` split them. `Path.GetFileName(@"a\b.nif")` returns `a\b.nif` on Linux. |
| `C:\…` is an absolute path | It is not; absolute paths start with `/` | A test that hard-codes `C:\` needs a per-OS path. |
| `%APPDATA%`, `%LOCALAPPDATA%` | `~/.config`, `~/.local/share` (the XDG folders) | `Environment.GetFolderPath` already maps these. |
| Folder permissions are ACLs (`FileSystemAccessRule`) | Owner/group/other mode bits (`chmod`) | Tests that deny a folder need the Linux equivalent. |
| A file open in another process is locked | It is not; anyone can delete or replace it | Sharing-violation handling is Windows-only by nature. |
| Bethesda tools (`PapyrusCompiler.exe`, xEdit) run natively | They run under **Wine**, or **Proton** (Valve's Wine build that Steam uses) | Each game has a Proton *prefix*: a fake `C:` drive in a folder. |
| A shortcut or junction | A **symlink**; a **hardlink** is a second name for the same file | Amethyst deploys mods into the game folder with these. |

## Building and testing on Linux

```
# Arch / CachyOS (other distros: Microsoft's packages or dotnet-install.sh)
sudo pacman -S dotnet-sdk-9.0 aspnet-runtime-9.0

dotnet build housecarl.sln -c Release
dotnet test src/housecarl-mcp-tests -c Release --no-build
```

The server is an ASP.NET app, and Arch ships the ASP.NET runtime as its own package; without it every
test that starts the server aborts with "framework Microsoft.AspNetCore.App not found".

A test that cannot run on one OS is marked so it reports as **skipped**, never as passed: `[WindowsFact("what Linux
lacks")]` for Windows-only behaviour, `[CaseSensitiveDiskFact]` for one that needs a case-sensitive disk. Never `return`
early from a test body: that reports a pass for a test that did not run.

How the suite runs on Linux, for someone who wrote it on Windows:

| On Windows the test… | On Linux it… | Where |
|---|---|---|
| denies a folder with an ACL | sets the folder's mode bits (`000` for everything, `-wx` for listing only) | `DenyAce` |
| holds a file open so it cannot be replaced or deleted | also makes the file's folder unwritable, since an open file can be replaced on Linux | `HeldOpen.HoldAgainstReplace` |
| expects ERROR_SHARING_VIOLATION (32) for a file another process holds | gets EWOULDBLOCK (11); both mean "in use, retry" | `FileInUse` (core) |
| writes `C:\...` for an absolute path | uses `/C/...` | `HarnessPaths.Rooted` |
| copies `where.exe` as a failing BSArch | uses a shell script that exits 2 | `BsaPackContractTests` |
| runs BSArch or the compiler directly | runs them through a stand-in Wine that runs the program it is handed, so no test starts the real Wine | `NoRealWine`; `WindowsToolTests` record what Wine is handed |
| reads `System32\kernel32.dll` | reads Wine's `kernel32.dll`, a real PE; skipped without Wine | `[NativeDllFact]` |

Skipped on Linux, each with its reason: the five Papyrus compile tests (no `PapyrusCompiler.exe`, as on Windows CI),
three that carry a creation time across a replace, a memory-mapped file blocking a replace, the Windows installer's
held-DLL check, and upstream's own held-file dispose check.

## When a Linux user reports…

| Report | First place to look |
|---|---|
| "asset not found" for a file that is there | Casing or `\` in a join that does not go through `DiskPath.Combine` — [`linux-paths.md`](architecture/linux-paths.md). |
| A file written into a mod folder with `\` in its name | Same: a write built with `Path.Combine` on a game path. |
| A folder exists twice as `Meshes` and `meshes` | Expected on Linux; houseCARL reads one of them. Amethyst can normalise folder case. |
