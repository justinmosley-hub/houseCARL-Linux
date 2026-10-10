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
| `build-plugin.ps1 -Runtime linux-x64`; `packaging/START-HERE-linux.txt`; the plugin's folder prompt names Amethyst | A Linux package from the same build script, installed through Claude Code's plugin commands | *Building and testing on Linux* below |
| `WindowsTool` in core starts the Papyrus compiler and BSArch | They are Windows programs: on Linux they run through Wine, handed paths on its `Z:` drive | *Windows tools through Wine* below, [ADR 0008](decisions/0008-windows-tools-run-through-wine.md) |
| A spill's reservation deletes the file it made when the lock is lost (`ResultsStore.Reserve`) | Off Windows, .NET creates a `FileShare.None` file and then locks it; a reader opening the new file in between wins the lock, and the empty file was left beside the real spill | [`architecture/output-and-artifacts.md`](architecture/output-and-artifacts.md) "The reservation is the file" |
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

**Nothing to set up.** Every Linux Skyrim player has Steam's Proton, so houseCARL uses it: on each start it finds the
newest Proton in any Steam library (Proton carries the Mono the compiler, a .NET program, needs), so a Proton update or
a new version is followed by itself. It runs them in its own prefix (a prefix is Wine's private "Windows install"),
never a game's; Wine creates it on first use in a few seconds, and it takes about 630 MB (a Windows system with Proton's
Mono), which deleting the folder gives back. Without Proton it falls back to `wine` on PATH where
that can run the program, and otherwise says where it looked. [ADR 0008](decisions/0008-windows-tools-run-through-wine.md)
has the order and why houseCARL does not download Wine itself.

| Setting (optional) | What it does | Default |
|---|---|---|
| `HOUSECARL_WINE` | Use this Wine instead | the newest Steam Proton, then `wine` on PATH |
| `HOUSECARL_WINEPREFIX` | Run in this prefix instead | `~/.local/share/houseCARL/wineprefix` |

Getting the compiler: install **Skyrim Special Edition: Creation Kit** from Steam into the same game folder. It adds
`Papyrus Compiler/PapyrusCompiler.exe` and the vanilla script sources (`Data/Scripts.zip`, to extract to
`Data/Source/Scripts`). Then `housecarl_set_tool_path tool='papyrus_compiler'` with the `.exe`'s path, as on Windows.

### The game's log folders

The game runs under Proton, so its `Documents\My Games\Skyrim Special Edition` is inside its Proton prefix, which Steam
keeps in the game's own library: `<library>/steamapps/compatdata/489830/pfx/drive_c/users/steamuser/Documents/My Games/
Skyrim Special Edition` (489830 is Skyrim SE's Steam app id). Off Windows, `ToolBridge` also looks there for the
Papyrus log and crash-log folders, from the load order's game folder (`ToolBridge.ProtonMyGames`), and the
where-to-find-it sentences name the prefix; `housecarl_load_order_status` shows them as auto-detected once the game has
written them. Pinned by `ProtonLogFolderTests`.

### The bundled skills

The skills' requirement line names an Amethyst game beside an MO2 instance, so a session on Amethyst does not pass
them over. What they still say for Windows or MO2, on purpose:
- log paths written as `Documents\My Games\…`: the Proton location is the one above, and the status tool shows it;
- `skse-plugin-authoring`'s toolchain notes build an SKSE plugin with the Windows toolchain and deploy it into an MO2
  mod; building SKSE plugins on Linux is out of this fork's scope;
- mentions of MO2 that only name the manager (an OAR `user.json` in its own mod, SPID reading `meta.ini`'s `version=`),
  which hold for Amethyst mods too;
- the Codex entry skill (`plugin/codex`) is upstream's, for Codex; this fork targets Claude Code.

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

**The Linux package** comes from upstream's own `scripts/build-plugin.ps1`, run with PowerShell 7 and `-Runtime
linux-x64` (`pwsh` is preinstalled on GitHub's Linux runners; locally, a distribution package or
`dotnet tool install PowerShell --version 7.5.4 --tool-path <dir>`):

```
pwsh scripts/build-plugin.ps1 -Runtime linux-x64
claude plugin validate dist/housecarl --strict
```

It runs the same twelve steps as the Windows build, with four differences: the server is published for Linux and
`.mcp.json` launches `housecarl-mcp` (no `.exe`); the START-HERE note is `packaging/START-HERE-linux.txt`, which installs
through `claude plugin marketplace add` / `claude plugin install`; there is no setup utility (it writes the Windows
desktop app's config); and the zip is `release/houseCARL-<ver>-linux-x64.zip`. The path leak-check also refuses the
builder's own home folder. The script's paths are written with `/`, which Windows PowerShell reads too, so the
Windows build is unchanged. The zip keeps the server's executable mark (checked by unzipping it).

**CI** (`.github/workflows/ci.yml`) runs one Linux job (`ubuntu-latest`), `build + probes (Linux)`: the build, the
suite with upstream's zero-tests guard, then the Linux package built, its tracked notices checked, the package validated
with `claude plugin validate --strict`, a check that the server is still executable after unzipping, and the zip kept
as the run's `linux-package` artifact for two weeks. The runner has no Wine, Steam or Creation Kit, so the tests that
need them skip. The fork does not run upstream's Windows job: the Windows package is upstream's, built by upstream's
CI, and the fork's merges of upstream bring its Windows results along. `amethyst-linux` requires the Linux job to
pass before a pull request merges.

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
| runs BSArch or the compiler directly | runs them through a stand-in Wine that runs the program it is handed, in a temp prefix, so no test starts the real Wine or writes under the user's home | `NoRealWine`; `WindowsToolTests` record what Wine is handed; `WineFinderTests` find Proton in a fake Steam |
| reads `System32\kernel32.dll` | reads Wine's `kernel32.dll`, a real PE; skipped without Wine | `[NativeDllFact]` |

Skipped on Linux, each with its reason: the five Papyrus compile tests (no `PapyrusCompiler.exe`, as on Windows CI),
three that carry a creation time across a replace, a memory-mapped file blocking a replace, the Windows installer's
held-DLL check, and upstream's own held-file dispose check.

## Keeping up with upstream

Upstream merges several pull requests a day, so the fork takes them daily, in small merges, and never straight into
`amethyst-linux`. `.github/workflows/upstream-sync.yml` runs at 06:17 UTC (and by hand from the Actions tab):

1. It merges upstream's `main` into a fresh `sync/upstream` branch off `amethyst-linux`. Nothing new: it stops.
2. A clean merge is pushed, gets a pull request into `amethyst-linux` with auto-merge on, and has CI started on it
   (a pull request made with the workflow's own token does not start CI by itself; a dispatch does, and its results
   attach to the commit the pull request shows).
3. `amethyst-linux` requires the CI job (`build + probes (Linux)`) and an up-to-date branch before a pull request
   merges, so the sync merges itself only once it passes. A red run leaves the pull request open for a look.
4. A merge that conflicts pushes nothing and opens (or comments on) an issue titled "Upstream sync conflicts" naming
   the files. Merge by hand keeping both sides: upstream's change and the fork's reason for its own (the "What
   changed, and where" table above says where each fork change lives and why).

**Settings the sync needs.** The workflows open pull requests and issues with the workflow's own token, which needs
two repository settings a fork starts without: Settings > Actions > General > "Allow GitHub Actions to create and
approve pull requests", and Settings > General > Features > Issues. Without the first, `gh pr create` fails with
"Resource not accessible by integration". Both workflows also set `GH_REPO` to this repository: they add a git remote
named `upstream`, and the `gh` CLI, given no repository, acts on the `upstream` remote ahead of `origin`, so every
`gh pr` and `gh issue` call would otherwise go to Avick3110/houseCARL and be refused with that same error.

The checks that catch a fork-specific break in a merge that is otherwise clean: the Linux job itself (casing, paths,
Wine), `ManagerWordingTests` (an upstream rewording of an MO2 remedy the Amethyst table swaps), and the Amethyst tests.
**Claude fixes a red or conflicting sync** (`.github/workflows/sync-fix.yml`), with Anthropic's
`anthropics/claude-code-action`, signed in with the repository secret `CLAUDE_CODE_OAUTH_TOKEN` (made with
`claude setup-token` on a Claude subscription; without the secret it does nothing):
- CI red on `sync/upstream`: auto-merge is turned off, Claude gets the failed log, fixes the cause on the branch
  following `CLAUDE.md` and this guide, and CI runs again.
- The sync's merge conflicted: Claude redoes the merge, resolves it keeping both sides, and the pull request opens
  with auto-merge off.
- A fix Claude made never merges by itself: a person reviews and merges it. One attempt per break: if the branch's
  newest commit is already Claude's, it comments that a person is needed and stops. While a sync pull request waits
  with auto-merge off, neither workflow touches its branch; the daily sync only notes that upstream moved on.
- Claude may only edit files and run `dotnet`, `git` and read-only `gh run view` / `gh pr view`; it is told never to
  delete, skip or weaken a test and never to change MO2 behaviour, and to commit nothing when unsure.
- Claude reads code merged from upstream, so anything in that code is input to it. It therefore runs in a job whose
  token can only read the repository: it commits locally, and a second job with no Claude in it pushes those commits
  to `sync/upstream` only, and only if the branch has not moved meanwhile. The action is pinned to a commit. A fix
  still reaches `amethyst-linux` only through a person's review.

## Releasing

Upstream tags its Windows releases `v<ver>`, and the fork takes those tags with its merges, so the fork's own tags carry
a suffix: `v<ver>-linux`, with `.2`, `.3` for another cut of the same version. `<ver>` is the version in
`plugin/.claude-plugin/plugin.json`, the one the zip is named and the server is stamped with; the release workflow
refuses a tag that says otherwise.

```
git tag v2.0.4-linux amethyst-linux
git push origin v2.0.4-linux
```

`.github/workflows/release.yml` then runs CI on the tagged commit and, once it is green, publishes
`houseCARL-<ver>-linux-x64.zip` from that run as a GitHub Release under the tag. A red run publishes nothing.

## When a Linux user reports…

| Report | First place to look |
|---|---|
| "asset not found" for a file that is there | Casing or `\` in a join that does not go through `DiskPath.Combine` — [`linux-paths.md`](architecture/linux-paths.md). |
| A file written into a mod folder with `\` in its name | Same: a write built with `Path.Combine` on a game path. |
| A folder exists twice as `Meshes` and `meshes` | Expected on Linux; houseCARL reads one of them. Amethyst can normalise folder case. |
