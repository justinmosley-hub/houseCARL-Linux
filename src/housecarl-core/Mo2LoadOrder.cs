namespace HousecarlCore;

// The active load order, read from an MO2 portable instance's profile files on disk, never from the USVFS; the priority model and the three profile files are in docs/architecture/mo2-instance.md.

/// <summary>The resolved active order plus any non-fatal problems — surfaced, not swallowed; <see cref="OrderedPaths"/> is in resolver winner order, highest priority last.</summary>
public sealed record Mo2OrderResult(
    IReadOnlyList<string> OrderedPaths, IReadOnlyList<string> Warnings, int ActiveCount)
{
    public int ResolvedCount => OrderedPaths.Count;
}

/// <summary>The MO2 profile's enabled/disabled composition, parsed from the three profile text files only, so it is cheap to re-read on demand; names are verbatim from the files.</summary>
public sealed record Mo2Composition(
    IReadOnlyList<string> EnabledMods,
    IReadOnlyList<string> DisabledMods,
    IReadOnlyList<string> OrderedPluginNames,
    IReadOnlySet<string> ActivePluginNames,
    IReadOnlyList<string> InactivePluginNames,
    IReadOnlyList<string> ImplicitPluginNames);

/// <summary>An MO2 profile text file is locked by another process right now — the sharing or lock violation only, which is a transient to retry; it derives from <see cref="IOException"/> so mid-write catches keep seeing it.</summary>
public sealed class ProfileUnreadableException : IOException
{
    /// <summary>The profile file that could not be read.</summary>
    public string ProfilePath { get; }

    public ProfileUnreadableException(string profilePath, Exception inner)
        : base($"the MO2 profile file '{System.IO.Path.GetFileName(profilePath)}' is held open by another process " +
               "right now, so the load order could not be read (MO2 holds these while it re-sorts). Nothing was " +
               "changed; run this again in a moment.", inner)
        => ProfilePath = profilePath;
}

/// <summary>One on-disk sighting of a plugin filename: its real path, a label for where it was found, and whether that source is enabled in the profile.</summary>
public sealed record PluginFileHit(string Path, string Where, bool Enabled);

public static class Mo2LoadOrder
{
    /// <summary>Test seam: raised with the profile folder on every <see cref="ReadComposition"/>, never for <see cref="Build"/>'s own parse; null in the product.</summary>
    internal static event Action<string>? CompositionRead;

    static readonly string[] PluginExts = PluginFile.Extensions;   // the one shared home (HousecarlCore.PluginFile) — no divergent copy

    /// <summary>Read the active order from <paramref name="profileDir"/>'s three profile files, resolving each active plugin to its winning real path; the returned paths are in load order, winner last.</summary>
    public static Mo2OrderResult Build(string profileDir, string modsDir, string dataDir, string overwriteDir = "", bool partialLoadOrder = false)
    {
        var warnings = new List<string>();

        // The enabled/disabled COMPOSITION (text files only — cheap). The diagnostic re-reads this same parse fresh.
        var comp = ParseComposition(profileDir, modsDir, dataDir, partialLoadOrder, warnings);

        // filename → WINNING real path: overwrite first, then highest-priority enabled mod (first-seen wins), data folder as base.
        var winningPath = BuildFilenameMap(comp.EnabledMods, modsDir, dataDir, overwriteDir);
        var inactive = new HashSet<string>(comp.InactivePluginNames, StringComparer.OrdinalIgnoreCase);

        // The can't-resolve warning names only the places actually searched: explicit-paths mode passes no overwrite dir.
        var searchedPlaces = string.IsNullOrWhiteSpace(overwriteDir)
            ? "no enabled mod or the game Data folder"
            : "no enabled mod, the overwrite folder, or the game Data folder";

        // loadorder.txt order → drop unchecked plugins; resolve the rest to their winning path (winner last).
        var orderedPaths = new List<string>(comp.OrderedPluginNames.Count);
        int active = 0;
        foreach (var name in comp.OrderedPluginNames)
        {
            if (inactive.Contains(name)) continue;                  // present-but-unchecked in MO2 → not loaded
            active++;
            if (winningPath.TryGetValue(name, out var path))
                orderedPaths.Add(path);
            else
                warnings.Add(
                    $"load order lists '{name}' but {searchedPlaces} provides it (stale loadorder.txt? " +
                    "trigger an MO2 refresh / re-sort so it re-writes the profile files).");
        }

        return new Mo2OrderResult(orderedPaths, warnings, active);
    }

    /// <summary>Parse the profile's enabled/disabled composition from the three profile text files; the diagnostic re-reads this fresh each call, and <see cref="Build"/> adds the physical-path resolution on top.
    /// <paramref name="partialLoadOrder"/> (<see cref="IManagerInstance.PartialLoadOrder"/>) completes the order from <paramref name="dataDir"/> and plugins.txt (<see cref="CompleteOrder"/>).</summary>
    public static Mo2Composition ReadComposition(string profileDir, string modsDir, string dataDir, bool partialLoadOrder, List<string>? warnings = null)
    {
        CompositionRead?.Invoke(profileDir);
        return ParseComposition(profileDir, modsDir, dataDir, partialLoadOrder, warnings);
    }

    /// <summary>The parse behind <see cref="ReadComposition"/>, which <see cref="Build"/> calls directly.</summary>
    static Mo2Composition ParseComposition(string profileDir, string modsDir, string dataDir, bool partialLoadOrder, List<string>? warnings)
    {
        var enabled = new List<string>();
        var disabled = new List<string>();
        ParseModlist(Path.Combine(profileDir, "modlist.txt"), modsDir, enabled, disabled, warnings);

        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inactive = new List<string>();
        var listed = new List<string>();
        ParsePlugins(Path.Combine(profileDir, "plugins.txt"), active, inactive, listed);
        var inactiveSet = new HashSet<string>(inactive, StringComparer.OrdinalIgnoreCase);

        var loadOrderPath = Path.Combine(profileDir, "loadorder.txt");
        var ordered = ReadLoadOrderNames(loadOrderPath, warnings);
        if (partialLoadOrder && File.Exists(loadOrderPath)) CompleteOrder(ordered, listed, dataDir);   // a missing order stays empty, named
        var implicitNames = new List<string>();
        foreach (var name in ordered)
            if (!active.Contains(name) && !inactiveSet.Contains(name))
                implicitNames.Add(name);                            // in the order, never in plugins.txt → force-loaded master/CC

        return new Mo2Composition(enabled, disabled, ordered, active, inactive, implicitNames);
    }

    /// <summary>modlist.txt to enabled and disabled mod folder names in file order (top = highest priority); a <c>…_separator</c> entry is skipped from both lists.
    /// A <c>*</c> entry is enabled when its folder exists: Amethyst's locked mod; MO2's DLC and unmanaged entries have none.</summary>
    static void ParseModlist(string modlistPath, string modsDir, List<string> enabled, List<string> disabled, List<string>? warnings)
    {
        if (!File.Exists(modlistPath))
        {
            warnings?.Add($"modlist.txt not found at '{modlistPath}' — duplicate-name plugins cannot be priority-resolved.");
            return;
        }
        foreach (var raw in ReadProfileLines(modlistPath))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line[0] == '#') continue;
            char marker = line[0];
            if (marker != '+' && marker != '-' && marker != '*') continue;   // only +/-/* lines are mods
            var name = line[1..].Trim();
            if (name.Length == 0 || name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;
            if (marker == '*')
            {
                if (modsDir.Length > 0 && Directory.Exists(DiskPath.Combine(modsDir, name))) enabled.Add(name);
                continue;
            }
            (marker == '+' ? enabled : disabled).Add(name);
        }
    }

    /// <summary>Build filename to winning real path: overwrite, then enabled mods highest-priority first with the first sighting winning, then the game Data folder.</summary>
    static Dictionary<string, string> BuildFilenameMap(IReadOnlyList<string> enabledModsByPriority, string modsDir, string dataDir, string overwriteDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fn, full) in EnumeratePlugins(overwriteDir))  // MO2's overwrite layer — beats every mod
            map[fn] = full;                                         // (map is empty here; plain set keeps the rule obvious)

        foreach (var mod in enabledModsByPriority)                  // highest priority first
        {
            var modRoot = Path.Combine(modsDir, mod);
            foreach (var (fn, full) in EnumeratePlugins(modRoot))
                if (!map.ContainsKey(fn)) map[fn] = full;           // first (highest-priority) wins
        }

        foreach (var (fn, full) in EnumeratePlugins(dataDir))       // base game / vanilla masters — lowest priority
            if (!map.ContainsKey(fn)) map[fn] = full;

        return map;
    }

    /// <summary>Top-level *.esp/.esm/.esl in one folder, as (filename, full path); silent on a missing or inaccessible folder, since a modlist entry can lack a real folder.</summary>
    static IEnumerable<(string fn, string full)> EnumeratePlugins(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) yield break;
        var opts = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir, "*.es*", opts); }
        catch { yield break; }
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f);
            if (Array.Exists(PluginExts, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                yield return (Path.GetFileName(f), f);
        }
    }

    /// <summary>plugins.txt to the active set (the <c>*</c> stripped), the inactive list, and every name in file order; the implicit masters are not listed there at all and are classified in <see cref="ReadComposition"/>.</summary>
    static void ParsePlugins(string pluginsPath, HashSet<string> active, List<string> inactive, List<string> listed)
    {
        if (!File.Exists(pluginsPath)) return;
        foreach (var raw in ReadProfileLines(pluginsPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var name = line[0] == '*' ? line[1..].Trim() : line;
            if (line[0] == '*') active.Add(name);                   // checked/active
            else inactive.Add(name);                                // listed without `*` → unchecked
            listed.Add(name);
        }
    }

    /// <summary>The masters the game loads before reading plugins.txt, in its order, when their files are in Data.</summary>
    static readonly string[] BaseMasters = { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };

    /// <summary>Make the saved order the one the game loads. Amethyst's loadorder.txt is its plugin panel's memory of
    /// positions: it lists only what the panel last saved, and keeps a disabled or removed mod's plugin to remember its
    /// slot; the game reads plugins.txt. So an entry that is neither in plugins.txt nor one the game loads itself is
    /// dropped; a base master or Skyrim.ccc plugin present in Data that is missing goes after the last one listed, in the
    /// game's order; and a plugins.txt entry that is missing goes at the end, in that file's order.</summary>
    static void CompleteOrder(List<string> ordered, IReadOnlyList<string> pluginsTxt, string dataDir)
    {
        var gameLoaded = GameLoadedPlugins(dataDir);
        var gameLoadedSet = new HashSet<string>(gameLoaded, StringComparer.OrdinalIgnoreCase);
        var loaded = new HashSet<string>(gameLoadedSet.Concat(pluginsTxt), StringComparer.OrdinalIgnoreCase);
        ordered.RemoveAll(name => !loaded.Contains(name));
        var seen = new HashSet<string>(ordered, StringComparer.OrdinalIgnoreCase);
        int at = 0;
        for (int i = 0; i < ordered.Count; i++)
            if (gameLoadedSet.Contains(ordered[i])) at = i + 1;
        foreach (var name in gameLoaded)
            if (seen.Add(name)) ordered.Insert(at++, name);
        foreach (var name in pluginsTxt)
            if (seen.Add(name)) ordered.Add(name);
    }

    /// <summary>The base masters, then the game folder's Skyrim.ccc entries, each kept only when its file is in <paramref name="dataDir"/>.</summary>
    static List<string> GameLoadedPlugins(string dataDir)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir)) return names;
        var present = new HashSet<string>(
            Directory.EnumerateFiles(dataDir, "*", DiskPath.AnyCaseListing).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>(BaseMasters);
        var gameDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dataDir));
        var ccc = gameDir is null ? null : DiskPath.Combine(gameDir, "Skyrim.ccc");
        if (ccc is not null && File.Exists(ccc))
            candidates.AddRange(ReadProfileLines(ccc).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#'));
        foreach (var name in candidates)
            if (present.Contains(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        return names;
    }

    /// <summary>loadorder.txt to plugin filenames in load order, top to bottom being lowest to highest priority; a <c>#</c> header is skipped.</summary>
    static List<string> ReadLoadOrderNames(string loadOrderPath, List<string>? warnings)
    {
        var names = new List<string>();
        if (!File.Exists(loadOrderPath))
        {
            warnings?.Add($"loadorder.txt not found at '{loadOrderPath}' — cannot determine the active load order.");
            return names;
        }
        foreach (var raw in ReadProfileLines(loadOrderPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            names.Add(line);
        }
        return names;
    }

    /// <summary>Read one profile text file, naming a LOCKED file as the transient it is; only a file another process holds (<see cref="FileInUse"/>) becomes <see cref="ProfileUnreadableException"/>, and every other fault keeps its own error.</summary>
    static string[] ReadProfileLines(string path)
    {
        try { return File.ReadAllLines(path); }
        catch (IOException ex) when (FileInUse.Is(ex))
        {
            throw new ProfileUnreadableException(path, ex);
        }
    }

    /// <summary>Locate every on-disk copy of a plugin filename across the WHOLE install — overwrite, every mod folder enabled, disabled or unlisted, and game Data — returning ALL hits rather than the first; <paramref name="filename"/> is reduced to a bare name.</summary>
    public static IReadOnlyList<PluginFileHit> LocatePlugin(
        string profileDir, string modsDir, string dataDir, string overwriteDir, string filename)
        => LocatePlugin(ReadComposition(profileDir, modsDir, dataDir, partialLoadOrder: false), modsDir, dataDir, overwriteDir, filename);   // mods only; the order is unused

    /// <summary>As the profileDir overload, but reusing a <see cref="Mo2Composition"/> the caller already parsed, so a scan pays the modlist parse once.</summary>
    public static IReadOnlyList<PluginFileHit> LocatePlugin(
        Mo2Composition comp, string modsDir, string dataDir, string overwriteDir, string filename)
    {
        var hits = new List<PluginFileHit>();
        var fn = Path.GetFileName(filename?.Trim() ?? "");
        if (fn.Length == 0) return hits;

        foreach (var (dir, where, enabled) in CandidateFolders(comp, modsDir, dataDir, overwriteDir))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try { var p = Path.Combine(dir, fn); if (File.Exists(p)) hits.Add(new PluginFileHit(p, where, enabled)); }
            catch { /* an inaccessible candidate folder is simply not a hit — never a false 'found' */ }
        }
        return hits;
    }

    /// <summary>THE layer sequence a filename is searched across, in precedence order; written once because <see cref="LocatePlugin"/> and <see cref="AllPluginFileNames"/> must draw on the same set of places. The label identifies the layer and its state, never a remedy.</summary>
    static IEnumerable<(string dir, string where, bool enabled)> CandidateFolders(
        Mo2Composition comp, string modsDir, string dataDir, string overwriteDir)
    {
        yield return (overwriteDir, "overwrite", true);
        foreach (var mod in comp.EnabledMods) yield return (Path.Combine(modsDir, mod), $"mod '{mod}' (enabled)", true);
        foreach (var mod in comp.DisabledMods) yield return (Path.Combine(modsDir, mod), $"mod '{mod}' (DISABLED)", false);
        foreach (var dir in UnlistedModFolders(comp, modsDir))
            yield return (dir, $"mod '{Path.GetFileName(dir)}' (UNLISTED)", false);
        yield return (dataDir, "game Data", true);
    }

    /// <summary>Every plugin filename the install provides, walking <see cref="CandidateFolders"/> — the did-you-mean pool and the declared-master split's is-it-installed discriminant. It lists each folder, so it is read once per call and then asked many times.</summary>
    public static IReadOnlyCollection<string> AllPluginFileNames(
        Mo2Composition comp, string modsDir, string dataDir, string overwriteDir)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, _, _) in CandidateFolders(comp, modsDir, dataDir, overwriteDir))
            foreach (var (fn, _) in EnumeratePlugins(dir))
                names.Add(fn);
        return names;
    }

    /// <summary>Mod folders that exist under <paramref name="modsDir"/> but that modlist.txt mentions in NEITHER list; one directory listing, and a missing or inaccessible ModsDir yields nothing.</summary>
    static IEnumerable<string> UnlistedModFolders(Mo2Composition comp, string modsDir)
    {
        if (string.IsNullOrWhiteSpace(modsDir) || !Directory.Exists(modsDir)) yield break;
        var listed = new HashSet<string>(comp.EnabledMods, StringComparer.OrdinalIgnoreCase);
        listed.UnionWith(comp.DisabledMods);
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(modsDir); }
        catch { yield break; }
        foreach (var dir in dirs)
            if (!listed.Contains(Path.GetFileName(dir)))
                yield return dir;
    }

    /// <summary>Split declared masters the active order does not satisfy into the two cases whose REMEDIES differ — <c>NotInstalled</c> (install it) and <c>InstalledButInactive</c> (enable it) — the ONE home for the split, discriminated by presence in <paramref name="installedPluginFiles"/>, which is what <see cref="AllPluginFileNames"/> returns.</summary>
    public static (IReadOnlyList<string> NotInstalled, IReadOnlyList<string> InstalledButInactive) SplitUnsatisfiedMasters(
        IReadOnlyCollection<string> installedPluginFiles, IEnumerable<string> unsatisfied)
    {
        var installed = new HashSet<string>(installedPluginFiles, StringComparer.OrdinalIgnoreCase);
        var notInstalled = new List<string>();
        var inactive = new List<string>();
        foreach (var m in unsatisfied)
        {
            // A name is reduced to its FILENAME before it is looked up; a blank one is installed nowhere.
            if (installed.Contains(Path.GetFileName(m?.Trim() ?? ""))) inactive.Add(m);
            else notInstalled.Add(m);
        }
        return (notInstalled, inactive);
    }
}
