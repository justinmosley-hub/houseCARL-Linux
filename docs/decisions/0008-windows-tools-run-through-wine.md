# ADR 0008 — Windows tools run through Steam's Proton, found afresh, handed Z: paths

**Class:** ARCHIVE (ADR). Immutable once merged; superseded by a later ADR, never edited.
**Date:** 2026-10-09 · **Status:** proposed (Linux / Amethyst fork)

## Context

houseCARL drives two Windows programs as subprocesses: the Creation Kit's `PapyrusCompiler.exe` (compile a `.psc`)
and `BSArch.exe` (pack a `.bsa`). Neither has a Linux build. On Linux, Windows programs run under Wine, or under
Proton, Valve's Wine build that Steam ships. Each program also takes folder paths as arguments, and it reads them as
Windows paths. The compiler is a .NET Framework program, so the Wine that runs it needs Mono, which a distribution's
`wine` may not include and which a new prefix otherwise asks to install through a dialog.

## Decision

1. **One home for the launch.** `WindowsTool` (core) builds every start: the exe itself on Windows; elsewhere Wine,
   with the exe as its first argument and Wine's own log lines off (`WINEDEBUG=-all`), so the compiler's stderr holds
   only the compiler's diagnostics.
2. **Paths on Wine's `Z:` drive.** Each path argument is handed over as `Z:` plus the absolute path with `/` turned to
   `\`. Every Wine and Proton prefix maps `Z:` to `/`, so no `winepath` call (a second Wine start per path) is needed.
   A diagnostic the compiler prints on such a path is read back by its file name through `GamePath.FileName`, which
   splits on either separator.
3. **Steam's Proton, found on every start.** Skyrim is a Windows game, so every Linux player has Steam's Proton, and
   Steam keeps it updated; it also carries the Mono the compiler needs. `WindowsTool.FindWine` looks, each time a
   program is started: `HOUSECARL_WINE` if set; else the newest Proton in any Steam library (each library
   `libraryfolders.vdf` names, native, Flatpak and Snap Steam alike; a numbered release, then Experimental, then
   others, then a custom build in `compatibilitytools.d`), skipping one without its Mono; else `wine` on PATH, for a
   program that needs no Mono or where `wine-mono` is installed. With none, the start is refused in one sentence
   saying where it looked, before anything is launched, so a Wine never stops to ask for Mono. Finding it afresh
   means a Proton update, a new version or a removed one is followed with no setting. houseCARL does not download or
   update Wine itself: that would be a downloader and binaries to verify and maintain, for what Steam already does.
4. **houseCARL's own prefix.** Programs run in `~/.local/share/houseCARL/wineprefix` (the user's data folder), or
   `HOUSECARL_WINEPREFIX`, never a game's; Wine creates and upgrades it itself. The settings follow
   `HOUSECARL_DATA_DIR`'s precedent and add nothing to the published tool surface; the compiler's own path stays the
   existing `housecarl_set_tool_path tool='papyrus_compiler'`.
5. **Tests never start the real Wine or touch the user's home.** The test project points `HOUSECARL_WINE` at a
   stand-in that runs the program it is handed and `HOUSECARL_WINEPREFIX` into the run's temp folder (`NoRealWine`),
   so the existing script stand-ins for BSArch behave as before. The finder is tested over a fake Steam. The
   real-compiler tests find Proton as the product does and run only when the compiler is configured.

## Consequences

- Windows behaviour is unchanged: `WindowsTool` returns the plain start there and leaves every path as it was.
- A Linux user with Steam and Skyrim needs nothing more: compiling works out of the box (checked: Proton 11.0 was
  found and the real compiler ran with only its own path set). The first compile in a new prefix takes a few seconds
  while Wine sets it up. BSArch needs no Mono.
- The finder reads Steam's folder layout and `libraryfolders.vdf`; a change there shows as "no Wine was found", with
  `HOUSECARL_WINE` as the way out.
- A prefix whose `Z:` drive was removed would hand the tools paths they cannot open; the compiler then fails to
  find its sources and says so in its own diagnostics.
