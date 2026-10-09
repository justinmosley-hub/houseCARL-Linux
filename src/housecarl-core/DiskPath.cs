namespace HousecarlCore;

/// <summary>The one join of a root folder and a game-relative path (<c>meshes\foo.nif</c>) into a real path on disk;
/// contract in docs/architecture/linux-paths.md.</summary>
public static class DiskPath
{
    static readonly char[] Separators = { '\\', '/' };

    static readonly EnumerationOptions AnyCase = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>The options of <c>Directory.EnumerateFiles(dir, pattern)</c> (<see cref="EnumerationOptions"/>.Compatible),
    /// matching names without regard to case: game files ship as <c>Mod.ESP</c> as often as <c>mod.esp</c>, and Linux
    /// would otherwise list only the casing the pattern spells.</summary>
    public static readonly EnumerationOptions AnyCaseListing = new()
    {
        MatchType = MatchType.Win32,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    /// <summary><paramref name="root"/> joined with <paramref name="relPath"/>. Windows: exactly
    /// <see cref="Path.Combine(string, string)"/>. Elsewhere: each segment takes the casing found on disk, and the
    /// given spelling from the first segment that does not exist yet.</summary>
    public static string Combine(string root, string relPath)
    {
        if (OperatingSystem.IsWindows()) return Path.Combine(root, relPath);

        // Like Path.Combine, an already-rooted second argument replaces the root.
        var path = Path.IsPathRooted(relPath) ? Path.GetPathRoot(relPath)! : root;
        bool onDisk = true;
        foreach (var segment in relPath.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = onDisk ? NameOnDisk(path, segment) : null;
            onDisk = name is not null;
            path = Path.Combine(path, name ?? segment);
        }
        return path;
    }

    /// <summary>The entry in <paramref name="dir"/> spelled <paramref name="segment"/> in any casing, or null. An exact
    /// match wins; among case-only twins (Linux allows <c>Meshes</c> beside <c>meshes</c>) the ordinal-first name wins.</summary>
    static string? NameOnDisk(string dir, string segment)
    {
        if (Path.Exists(Path.Combine(dir, segment))) return segment;
        // '.' and '..' are not entries, and '*' or '?' would be read as a wildcard.
        if (segment is "." or ".." || segment.IndexOfAny(['*', '?']) >= 0) return null;
        try
        {
            string? best = null;
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir, segment, AnyCase))
            {
                var name = Path.GetFileName(entry);
                if (best is null || string.CompareOrdinal(name, best) < 0) best = name;
            }
            return best;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
