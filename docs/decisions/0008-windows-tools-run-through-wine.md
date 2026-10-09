# ADR 0008 — Windows tools run through Wine, handed Z: paths, chosen by environment

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
3. **Which Wine, and which prefix, by environment.** `HOUSECARL_WINE` names the Wine (default `wine` on PATH;
   Proton's `files/bin/wine` carries its own Mono), and `HOUSECARL_WINEPREFIX` the prefix (default: Wine's own). This
   follows `HOUSECARL_DATA_DIR`'s precedent and adds nothing to the published tool surface; the compiler's own path
   stays the existing `housecarl_set_tool_path tool='papyrus_compiler'`.
4. **Tests never start the real Wine.** The test project points `HOUSECARL_WINE` at a stand-in that runs the program
   it is handed (`NoRealWine`), so the existing script stand-ins for BSArch behave as before and no prefix is ever
   created. The real-compiler tests name the real Wine explicitly and run only when the compiler is configured.

## Consequences

- Windows behaviour is unchanged: `WindowsTool` returns the plain start there and leaves every path as it was.
- A Linux user needs a Wine that can run .NET programs for compiling (the `wine-mono` package, or Proton's Wine), and
  a prefix; the first compile in a new prefix is slow while Wine sets it up. BSArch needs no Mono.
- A prefix whose `Z:` drive was removed would hand the tools paths they cannot open; the compiler then fails to
  find its sources and says so in its own diagnostics.
