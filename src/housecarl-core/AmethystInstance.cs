using System.Text.Json;

namespace HousecarlCore;

// Derive the four load-order roots from ONE Amethyst game's config folder (…/AmethystModManager/games/<game>): where
// the game and its mods are, the active profile, and which copy of Data is the untouched game. Every rule mirrors
// Amethyst's own code and is in docs/architecture/amethyst-instance.md.

public static class AmethystInstance
{
    public const string PathsFileName = "paths.json";
    public const string DeployStateFileName = "deploy_state.json";
    public const string GameSettingsFileName = "game_settings.json";
    public const string ProfileStateFileName = "profile_state.json";
    public const string ConfigHint = "~/.config/AmethystModManager";

    /// <summary>Amethyst's config folders, native first, then the Flatpak's.</summary>
    public static IEnumerable<string> ConfigDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        yield return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".config") : xdg, "AmethystModManager");
        yield return Path.Combine(home, ".var", "app", "io.github.Amethyst.ModManager", "config", "AmethystModManager");
    }

    /// <summary>The game config folder a location names: itself when it holds paths.json, else the Amethyst game whose
    /// staging folder it is; null when it is neither.</summary>
    public static string? GameConfigFor(string dir) => GameConfigFor(dir, ConfigDirs());

    /// <summary><see cref="GameConfigFor(string)"/> over the given Amethyst config folders.</summary>
    internal static string? GameConfigFor(string dir, IEnumerable<string> configDirs)
    {
        if (File.Exists(Path.Combine(dir, PathsFileName))) return dir;
        if (!Directory.Exists(Path.Combine(dir, "profiles"))) return null;
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)); } catch { return null; }
        foreach (var config in configDirs)
        {
            var games = Path.Combine(config, "games");
            if (!Directory.Exists(games)) continue;
            foreach (var game in Directory.EnumerateDirectories(games))
                if (ReadJson(Path.Combine(game, PathsFileName), null, PathsFileName) is { } paths
                    && PathEquals(StagingRoot(config, Path.GetFileName(game), Str(paths, "staging_path")), full))
                    return game;
        }
        return null;
    }

    /// <summary>Derive the roots from a game config folder, or THROW naming every missing piece.</summary>
    public static ManagerInstancePaths Resolve(string gameConfigDir)
    {
        var problems = new List<string>();
        return Derive(gameConfigDir, problems)
            ?? throw new InvalidOperationException(
                $"'{gameConfigDir}' is not a usable Amethyst game — {string.Join("; ", problems)}.");
    }

    /// <summary>Non-throwing derive for the cheap freshness re-check.</summary>
    public static bool TryResolve(string gameConfigDir, out ManagerInstancePaths? paths)
    {
        paths = Derive(gameConfigDir, null);
        return paths is not null;
    }

    /// <summary>Whether a game config folder is usable, the derived paths, and the problems to show the user.</summary>
    public static (bool ok, ManagerInstancePaths? paths, IReadOnlyList<string> problems) Validate(string gameConfigDir)
    {
        var problems = new List<string>();
        var paths = Derive(gameConfigDir, problems);
        return (paths is not null, paths, problems);
    }

    /// <summary>The active profile, as Amethyst's get_last_active_profile reads it: last_active_profile, else "default".</summary>
    public static string ActiveProfile(string gameConfigDir) =>
        ReadJson(Path.Combine(gameConfigDir, DeployStateFileName), null, DeployStateFileName) is { } state
        && Str(state, "last_active_profile") is { Length: > 0 } profile ? profile : "default";

    /// <summary>Where a game's mods/, overwrite/ and profiles/ live: staging_path when set, else Amethyst's profiles
    /// folder for the game ($MOD_MANAGER_PROFILES_DIR, an existing &lt;config&gt;/Profiles, else ~/Games/Amethyst/Profiles).</summary>
    public static string StagingRoot(string configDir, string game, string? stagingPath)
    {
        if (!string.IsNullOrWhiteSpace(stagingPath)) return Path.TrimEndingDirectorySeparator(stagingPath);
        var env = Environment.GetEnvironmentVariable("MOD_MANAGER_PROFILES_DIR");
        var legacy = Path.Combine(configDir, "Profiles");
        var profiles = !string.IsNullOrEmpty(env) ? env
                     : Directory.Exists(legacy) ? legacy
                     : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Amethyst", "Profiles");
        return Path.Combine(profiles, game);
    }

    /// <summary>The untouched game Data: a deploy moves Data to its sibling Data_Core and rebuilds Data from links, so
    /// Data_Core is the game while it exists and Data otherwise. Deployed Data is never read (it holds the mods' links).</summary>
    public static string VanillaDataDir(string gamePath)
    {
        var core = Path.Combine(gamePath, "Data_Core");
        return Directory.Exists(core) ? core : Path.Combine(gamePath, "Data");
    }

    static ManagerInstancePaths? Derive(string gameConfigDir, List<string>? problems)
    {
        if (string.IsNullOrWhiteSpace(gameConfigDir)) { problems?.Add("no path was given"); return null; }
        gameConfigDir = Path.TrimEndingDirectorySeparator(gameConfigDir.Trim());
        var game = Path.GetFileName(gameConfigDir);
        var configDir = Path.GetDirectoryName(Path.GetDirectoryName(gameConfigDir)) ?? "";

        if (ReadJson(Path.Combine(gameConfigDir, PathsFileName), problems, PathsFileName) is not { } paths) return null;

        var profile = ActiveProfile(gameConfigDir);
        var root = StagingRoot(configDir, game, Str(paths, "staging_path"));
        var profileDir = Path.Combine(root, "profiles", profile);
        if (!Directory.Exists(profileDir))
        {
            problems?.Add($"the active profile '{profile}' has no folder: '{profileDir}' (open it in Amethyst once)");
            return null;
        }

        // A profile's settings may give it its own mods/overwrite folders and its own game install (Amethyst's
        // profile_specific_mods and per-profile game_path).
        if (ProfileSettings(profileDir, problems) is not { } settings) return null;
        bool ownMods = OwnMods(settings);
        var gamePath = Str(settings, "game_path") is { Length: > 0 } profileGame ? profileGame : Str(paths, "game_path");
        var modsDir = ownMods ? Path.Combine(profileDir, "mods") : Path.Combine(root, "mods");
        var overwriteDir = ownMods ? Path.Combine(profileDir, "overwrite") : Path.Combine(root, "overwrite");

        if (string.IsNullOrWhiteSpace(gamePath))
            problems?.Add($"{PathsFileName} has no game_path (set the game folder in Amethyst)");
        else if (!Directory.Exists(gamePath))
            problems?.Add($"the game folder from {PathsFileName} does not exist: '{gamePath}'");
        if (!File.Exists(Path.Combine(profileDir, "loadorder.txt")))
            problems?.Add($"profile '{profile}' has no loadorder.txt yet (open the plugins tab in Amethyst once so it writes it)");
        if (!Directory.Exists(modsDir)) problems?.Add($"the mods folder is missing: '{modsDir}'");

        var dataDir = string.IsNullOrWhiteSpace(gamePath) ? "" : VanillaDataDir(gamePath!);
        if (dataDir.Length > 0 && !Directory.Exists(dataDir))
            problems?.Add($"the game Data folder is missing: '{dataDir}'");
        else if (dataDir.Length > 0 && DeployedWithoutBackup(gameConfigDir, gamePath!))
            problems?.Add("Amethyst records mods as deployed, but there is no Data_Core beside the game's Data, so houseCARL " +
                          "cannot tell the game's own files from deployed mod files — restore or redeploy in Amethyst, then retry");

        bool ok = !string.IsNullOrWhiteSpace(gamePath) && Directory.Exists(gamePath)
                  && File.Exists(Path.Combine(profileDir, "loadorder.txt")) && Directory.Exists(modsDir)
                  && Directory.Exists(dataDir) && !DeployedWithoutBackup(gameConfigDir, gamePath!);
        return ok ? new ManagerInstancePaths(gameConfigDir, profile, profileDir, modsDir, dataDir, gamePath!, overwriteDir) : null;
    }

    /// <summary>Where houseCARL's messages say MO2 they mean Amethyst; the steps that differ are Amethyst's own.</summary>
    public const string WorkflowNote =
        "messages that say MO2 mean Amethyst here. A new houseCARL mod folder shows at the top of Amethyst's mod list, " +
        "switched off, once Amethyst refreshes; enabling it adds its plugin at the end of the load order. The game sees a " +
        "new or newly enabled mod only after a Deploy in Amethyst.";

    /// <summary>A profile's mods folder: its own when its settings say profile_specific_mods, else the staging root's.</summary>
    public static string ModsDirFor(string profileDir)
    {
        profileDir = Path.TrimEndingDirectorySeparator(profileDir);
        if (ProfileSettings(profileDir, null) is { } settings && OwnMods(settings)) return Path.Combine(profileDir, "mods");
        var root = Path.GetDirectoryName(Path.GetDirectoryName(profileDir)) ?? "";
        return Path.Combine(root, "mods");
    }

    static bool OwnMods(JsonElement settings) =>
        settings.TryGetProperty("profile_specific_mods", out var own) && own.ValueKind == JsonValueKind.True;

    /// <summary>Deployed by links or copies (not VFS, which leaves Data alone on disk) with no Data_Core backup beside it.</summary>
    static bool DeployedWithoutBackup(string gameConfigDir, string gamePath)
    {
        var state = ReadJson(Path.Combine(gameConfigDir, DeployStateFileName), null, DeployStateFileName);
        bool deployed = state is { } s && s.TryGetProperty("deploy_active", out var a) && a.ValueKind == JsonValueKind.True;
        var settings = ReadJson(Path.Combine(gameConfigDir, GameSettingsFileName), null, GameSettingsFileName);
        bool vfs = settings is { } g && g.TryGetProperty("vfs_enabled", out var v) && v.ValueKind == JsonValueKind.True;
        return deployed && !vfs && !Directory.Exists(Path.Combine(gamePath, "Data_Core"));
    }

    /// <summary>A profile's settings as Amethyst's read_profile_settings finds them: profile_state.json's
    /// profile_settings, else the legacy profile_settings.json; an empty object when neither has any; null (named) when
    /// one is there but malformed.</summary>
    static JsonElement? ProfileSettings(string profileDir, List<string>? problems)
    {
        var statePath = Path.Combine(profileDir, ProfileStateFileName);
        if (File.Exists(statePath))
        {
            if (ReadJson(statePath, problems, ProfileStateFileName) is not { } state) return null;
            if (state.TryGetProperty("profile_settings", out var s) && s.ValueKind == JsonValueKind.Object) return s;
        }
        var legacy = Path.Combine(profileDir, "profile_settings.json");
        if (File.Exists(legacy)) return ReadJson(legacy, problems, "profile_settings.json");
        return EmptyObject;
    }

    /// <summary>One JSON object off disk; null when it is absent, unreadable, or not an object — named in
    /// <paramref name="problems"/> when the caller keeps one, since bad Amethyst state is refused, never guessed past.</summary>
    static JsonElement? ReadJson(string path, List<string>? problems, string name)
    {
        if (!File.Exists(path)) { problems?.Add($"no {name} at '{path}'"); return null; }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc.RootElement.Clone();
            problems?.Add($"{name} is not a JSON object: '{path}'");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problems?.Add($"cannot read {name} ({ex.Message}): '{path}'");
        }
        return null;
    }

    static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool PathEquals(string a, string b)
    {
        try { return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), b, StringComparison.Ordinal); }
        catch { return false; }
    }
}

/// <summary>An Amethyst game as the service's <see cref="IManagerInstance"/>. Its roots move when paths.json,
/// game_settings.json or deploy_state.json is rewritten (a profile switch, a deploy or restore), when the active
/// profile's own settings change, or when a deploy or restore makes or removes Data_Core.</summary>
public sealed class AmethystManagerInstance(string gameConfigDir) : IManagerInstance
{
    public string Manager => "Amethyst";
    public string Location { get; } = gameConfigDir;
    public bool PartialLoadOrder => true;
    public string? WorkflowNote => AmethystInstance.WorkflowNote;
    public string ModsDirFor(string profileDir, string activeModsDir) => AmethystInstance.ModsDirFor(profileDir);

    public ManagerInstancePaths Resolve() => AmethystInstance.Resolve(Location);
    public bool TryResolve(out ManagerInstancePaths? paths) => AmethystInstance.TryResolve(Location, out paths);

    public string Fingerprint()
    {
        var parts = new List<string>
        {
            FileStamp.Of(Path.Combine(Location, AmethystInstance.PathsFileName)).Key,
            FileStamp.Of(Path.Combine(Location, AmethystInstance.GameSettingsFileName)).Key,
            FileStamp.Of(Path.Combine(Location, AmethystInstance.DeployStateFileName)).Key,
        };
        if (TryResolve(out var p) && p is not null)
        {
            parts.Add(FileStamp.Of(Path.Combine(p.ProfileDir, AmethystInstance.ProfileStateFileName)).Key);
            parts.Add(p.DataDir);   // names Data_Core while deployed, Data otherwise
        }
        return string.Join("|", parts);
    }
}
