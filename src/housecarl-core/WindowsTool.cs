using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HousecarlCore;

/// <summary>Starting a Windows program houseCARL drives (the Creation Kit's PapyrusCompiler.exe, BSArch.exe): directly on
/// Windows, through Wine elsewhere, with each path it is handed in the form it reads. The Wine is found afresh on every
/// start, so a Proton update or removal is followed without a setting. Contract in docs/linux-and-amethyst.md and ADR 0008.</summary>
public static class WindowsTool
{
    /// <summary>A Wine to start programs with, and where it was found, for messages.</summary>
    public sealed record WineChoice(string Path, string From);

    /// <summary>The Wine prefix programs run in: <c>HOUSECARL_WINEPREFIX</c> if set, else houseCARL's own under the user's
    /// data folder (<c>~/.local/share/houseCARL/wineprefix</c>), which Wine creates on first use.</summary>
    public static string Prefix => Environment.GetEnvironmentVariable("HOUSECARL_WINEPREFIX") is { Length: > 0 } set ? set : DefaultPrefix;

    internal static string DefaultPrefix =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "houseCARL", "wineprefix");

    /// <summary>The Wine to use, or null when none fits: <c>HOUSECARL_WINE</c> if set; else the newest Proton in any Steam
    /// library (a numbered release, then Experimental, then others, then a custom build in compatibilitytools.d), which
    /// carries the Mono a .NET program needs; else <c>wine</c> on PATH, when <paramref name="needsDotNet"/> is false or a
    /// wine-mono is installed, since a Wine without one stops to ask for it.</summary>
    public static WineChoice? FindWine(bool needsDotNet) =>
        FindWine(needsDotNet, Environment.GetEnvironmentVariable("HOUSECARL_WINE"), SteamRoots(), OnPath("wine"), SystemMonoDirs);

    internal static WineChoice? FindWine(bool needsDotNet, string? chosen, IEnumerable<string> steamRoots, string? systemWine,
                                         IEnumerable<string> systemMonoDirs)
    {
        if (chosen is { Length: > 0 }) return new WineChoice(chosen, "HOUSECARL_WINE");
        if (Protons(steamRoots).FirstOrDefault() is { } proton) return proton;
        if (systemWine is not null && (!needsDotNet || systemMonoDirs.Any(Directory.Exists))) return new WineChoice(systemWine, "wine on PATH");
        return null;
    }

    /// <summary>A start for <paramref name="exe"/> with output captured, or why there is none: the exe itself on Windows;
    /// elsewhere a Wine (<paramref name="wine"/>, else <see cref="FindWine(bool)"/>) with the exe as its first argument, in
    /// <paramref name="prefix"/> (else <see cref="Prefix"/>), and Wine's own log lines kept off the program's stderr.</summary>
    public static (ProcessStartInfo? Start, string? NoWine) Start(string exe, bool needsDotNet, string? wine = null, string? prefix = null)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows()) { psi.FileName = exe; return (psi, null); }

        var chosen = wine ?? FindWine(needsDotNet)?.Path;
        if (chosen is null) return (null, NoWineFound(exe, needsDotNet));
        psi.FileName = chosen;
        psi.ArgumentList.Add(exe);
        var pfx = prefix ?? Prefix;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(pfx))!);   // Wine creates the prefix itself
        psi.Environment["WINEPREFIX"] = pfx;
        if (!psi.Environment.ContainsKey("WINEDEBUG")) psi.Environment["WINEDEBUG"] = "-all";
        return (psi, null);
    }

    /// <summary>Why <paramref name="exe"/> cannot be started: where houseCARL looked, and what to install or set.</summary>
    internal static string NoWineFound(string exe, bool needsDotNet) =>
        $"no Wine{(needsDotNet ? " that can run .NET programs" : "")} was found to run {Path.GetFileName(exe)}: houseCARL " +
        "looks for Steam's Proton in every Steam library (any Proton game installs it, Skyrim included), then for wine " +
        (needsDotNet ? "with the wine-mono package " : "") + "on PATH. Install one, or set HOUSECARL_WINE to a Wine.";

    /// <summary>A path as the program reads it: unchanged on Windows; elsewhere on Wine's <c>Z:</c> drive, which a prefix
    /// maps to <c>/</c> (<c>/home/me/x</c> is <c>Z:\home\me\x</c>).</summary>
    public static string PathArg(string path) =>
        OperatingSystem.IsWindows() ? path : "Z:" + Path.GetFullPath(path).Replace('/', '\\');

    /// <summary>What a failed start of <paramref name="psi"/> says: the program on Windows; elsewhere the Wine it was
    /// started through too, and how to point houseCARL at another.</summary>
    public static string CouldNotStart(string what, string exe, ProcessStartInfo psi, Exception ex) => OperatingSystem.IsWindows()
        ? $"could not run {what} at '{exe}': {ex.Message}"
        : $"could not run {what} at '{exe}' through Wine ('{psi.FileName}'): {ex.Message} — set HOUSECARL_WINE to another Wine " +
          "(Proton's files/bin/wine works)";

    /// <summary>Every Proton under <paramref name="steamRoots"/>' libraries that has a Wine and its Mono, best first.</summary>
    internal static IEnumerable<WineChoice> Protons(IEnumerable<string> steamRoots)
    {
        var found = new List<(int Tier, string Name, WineChoice Choice)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            // One Steam is often reached through several paths (~/.steam/steam links to ~/.local/share/Steam).
            foreach (var library in Libraries(root).Select(RealDir).Distinct(StringComparer.Ordinal))
                foreach (var dir in Children(Path.Combine(library, "steamapps", "common")))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith("Proton", StringComparison.Ordinal))
                        Add(dir, name == "Proton - Experimental" ? 1 : NumberedProton.IsMatch(name) ? 0 : 2);
                }
            foreach (var dir in Children(Path.Combine(root, "compatibilitytools.d")))
                Add(dir, 3);
        }
        return found.OrderBy(f => f.Tier).ThenByDescending(f => f.Name, NaturalOrder.Instance).Select(f => f.Choice);

        void Add(string dir, int tier)
        {
            var wine = Path.Combine(dir, "files", "bin", "wine");
            if (!File.Exists(wine) || !Directory.Exists(Path.Combine(dir, "files", "share", "wine", "mono"))) return;
            if (!seen.Add(wine)) return;   // the same library through another path
            found.Add((tier, Path.GetFileName(dir), new WineChoice(wine, "Steam's " + Path.GetFileName(dir))));
        }
    }

    static readonly Regex NumberedProton = new(@"^Proton \d+(\.\d+)*$", RegexOptions.Compiled);
    static readonly Regex LibraryPath = new("\"path\"\\s+\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>A Steam root and every library its libraryfolders.vdf names.</summary>
    static IEnumerable<string> Libraries(string root)
    {
        yield return root;
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        string text;
        try { text = File.Exists(vdf) ? File.ReadAllText(vdf) : ""; } catch (IOException) { text = ""; }
        foreach (Match m in LibraryPath.Matches(text)) yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    /// <summary>Where Steam lives: the native install, its ~/.steam links, Flatpak and Snap.</summary>
    internal static IEnumerable<string> SteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"),
        };
    }

    static readonly string[] SystemMonoDirs = { "/usr/share/wine/mono", "/opt/wine/mono" };

    static string? OnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    static IEnumerable<string> Children(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>A folder by its own path, every link on the way followed.</summary>
    static string RealDir(string dir)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            var parent = Path.GetDirectoryName(full);
            var real = parent is null ? full : Path.Combine(RealDir(parent), Path.GetFileName(full));
            return new DirectoryInfo(real).ResolveLinkTarget(returnFinalTarget: true) is { } target ? RealDir(target.FullName) : real;
        }
        catch (IOException) { return Path.GetFullPath(dir); }
    }

    /// <summary>Names compared with their numbers as numbers: "Proton 11.0" after "Proton 9.0", "GE-Proton10-3" after "GE-Proton9-27".</summary>
    sealed class NaturalOrder : IComparer<string>
    {
        public static readonly NaturalOrder Instance = new();
        static readonly Regex Parts = new(@"\d+|\D+", RegexOptions.Compiled);

        public int Compare(string? x, string? y)
        {
            var a = Parts.Matches(x ?? "");
            var b = Parts.Matches(y ?? "");
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                int c = long.TryParse(a[i].Value, out var na) && long.TryParse(b[i].Value, out var nb)
                    ? na.CompareTo(nb) : string.CompareOrdinal(a[i].Value, b[i].Value);
                if (c != 0) return c;
            }
            return a.Count.CompareTo(b.Count);
        }
    }
}
