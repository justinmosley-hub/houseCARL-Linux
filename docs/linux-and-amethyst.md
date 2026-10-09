# houseCARL on Linux, with Amethyst Mod Manager

This fork runs houseCARL on Linux and reads a load order from
[Amethyst Mod Manager](https://github.com/ChrisDKN/Amethyst-Mod-Manager) as well as from MO2. It is
written for someone who knows houseCARL, C# and Windows, and has not used Linux or Amethyst. Nothing
here changes how houseCARL behaves on Windows.

Status: the server builds and the test suite runs on Linux; game-path handling is done. The Amethyst
backend, writes into Amethyst, and Papyrus compile through Wine are next, in that order.

## What changed, and where

| Change | Why | Read |
|---|---|---|
| `DiskPath` / `GamePath` in core; game paths go through them | Linux file names are case-sensitive and `\` is not a separator there | [`architecture/linux-paths.md`](architecture/linux-paths.md), [ADR 0006](decisions/0006-game-paths-resolve-case-insensitively-on-linux.md) |
| `HarnessPaths.ServerExe` in the tests | The built server has no `.exe` on Linux | the property itself |

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

Not done yet: about 150 tests still assume Windows (`C:\` paths, ACL denial, `where.exe`, the Windows
installer). Each is being made per-OS, or skipped on Linux with a reason where Linux cannot reproduce it.

## When a Linux user reports…

| Report | First place to look |
|---|---|
| "asset not found" for a file that is there | Casing or `\` in a join that does not go through `DiskPath.Combine` — [`linux-paths.md`](architecture/linux-paths.md). |
| A file written into a mod folder with `\` in its name | Same: a write built with `Path.Combine` on a game path. |
| A folder exists twice as `Meshes` and `meshes` | Expected on Linux; houseCARL reads one of them. Amethyst can normalise folder case. |
