# ADR 0007 — Amethyst is read from its files, behind one manager interface

**Class:** ARCHIVE (ADR). Immutable once merged; superseded by a later ADR, never edited.
**Date:** 2026-10-09 · **Status:** proposed (Linux / Amethyst fork)

## Context

houseCARL read one mod manager, MO2, through `Mo2Instance`: one folder in, four roots and a profile out, with
`ModOrganizer.ini` watched for a profile switch. The fork adds Amethyst Mod Manager, which copies MO2's profile files
on purpose but keeps its configuration elsewhere (`paths.json`, `deploy_state.json`, per-profile settings) and deploys
by moving the game's `Data` to `Data_Core` and rebuilding `Data` from links.

Amethyst also keeps a versioned SQLite file database (the *filegraph*: `winners`, `deployed_entries`, generations). An
earlier port (ni1by2/houseCARL-Amethyst) read winners from Amethyst's file index, and its 2026-10-02 hand-off notes that
index is now this database.

## Decision

1. **One interface, chosen by content.** `IManagerInstance` (resolve, try-resolve, fingerprint) has two
   implementations, `Mo2ManagerInstance` and `AmethystManagerInstance`. `ManagerInstance.Open` picks one from what the
   configured folder holds, so the existing setup tool, saved setting and environment variable take either. No tool is
   added; the published surface is unchanged.
2. **Reads use Amethyst's plain files, not the database.** houseCARL already decides each winner itself with MO2's rule,
   which Amethyst copies for Bethesda games, so it needs from Amethyst only the roots, the active profile, and which
   folder is the untouched game. All three are in plain files Amethyst writes, and `Data_Core` answers the last without
   reading deployed `Data`. The database is left for the write lanes, where a deploy has to be verified.
3. **Freshness is a fingerprint** of every file that can move the roots, plus which Data folder is the game, compared
   by exact ticks.

## Consequences

- MO2 behaviour does not change; its tests hold unchanged.
- houseCARL depends on Amethyst's file layout (`paths.json` keys, `deploy_state.json`, `profile_state.json`,
  `Data_Core`), not on the database schema, which Amethyst is still changing. A layout change shows as a named refusal,
  not a wrong answer.
- Amethyst's routing rules (files a mod sends somewhere other than `Data`) are not applied: houseCARL reads a mod folder
  as Data-relative, as MO2 does. For Skyrim SE that is Amethyst's default.
- The write lanes still have to be made Amethyst-aware: where a new patch lands and how a redeploy is confirmed.
