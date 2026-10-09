using HousecarlMcp;
using Mutagen.Bethesda.Plugins;

namespace HousecarlMcpTests;

/// <summary>A bare Amethyst setup under one temp root, laid out the way Amethyst lays it out (docs/architecture/amethyst-instance.md):
/// the config folder's games/&lt;game&gt;/ with paths.json, deploy_state.json and game_settings.json; the staging folder
/// with mods/, overwrite/ and profiles/&lt;profile&gt;/; and the game folder with Data. <see cref="Deploy"/> does what
/// Amethyst's link deploy does to Data.</summary>
sealed class ScratchAmethyst
{
    public const string Game = "Skyrim Special Edition";

    public string Root { get; }
    public string ConfigDir => Path.Combine(Root, "config", "AmethystModManager");
    public string GameConfig => Path.Combine(ConfigDir, "games", Game);
    public string Staging => Path.Combine(Root, "staging", Game);
    public string ModsDir => Path.Combine(Staging, "mods");
    public string OverwriteDir => Path.Combine(Staging, "overwrite");
    public string GamePath => Path.Combine(Root, "game");
    public string DataDir => Path.Combine(GamePath, "Data");
    public string DataCore => Path.Combine(GamePath, "Data_Core");
    public string ProfileDir(string profile = "default") => Path.Combine(Staging, "profiles", profile);

    public ScratchAmethyst(string prefix)
    {
        _ = TestCorpus.Path;   // the service's rulebook reads CorpusRulebook.CorpusPath, which this sets
        Root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        foreach (var d in new[] { GameConfig, ModsDir, OverwriteDir, DataDir, ProfileDir() }) Directory.CreateDirectory(d);
        Paths();
        File.WriteAllText(Path.Combine(GameConfig, "deploy_state.json"), "{\"last_active_profile\": \"default\"}");
        File.WriteAllText(Path.Combine(GameConfig, "game_settings.json"), "{\"vfs_enabled\": false}");
        Profile("Skyrim.esm\n", "", "");
    }

    /// <summary>Write paths.json; a null staging path leaves staging_path empty, Amethyst's default location.</summary>
    public void Paths(string? stagingPath = "", string? gamePath = null) =>
        File.WriteAllText(Path.Combine(GameConfig, "paths.json"),
            "{\n  \"game_path\": " + Json(gamePath ?? GamePath) + ",\n  \"deploy_mode\": \"symlink\",\n  \"staging_path\": "
            + Json(stagingPath == "" ? Staging : stagingPath ?? "") + "\n}\n");

    /// <summary>The active profile's three text files, as Amethyst writes them for Skyrim SE: plugins.txt marks enabled
    /// plugins with '*' and leaves base-game plugins out; loadorder.txt lists them all.</summary>
    public void Profile(string loadorder, string plugins, string modlist, string profile = "default")
    {
        Directory.CreateDirectory(ProfileDir(profile));
        File.WriteAllText(Path.Combine(ProfileDir(profile), "loadorder.txt"), loadorder);
        File.WriteAllText(Path.Combine(ProfileDir(profile), "plugins.txt"), plugins);
        File.WriteAllText(Path.Combine(ProfileDir(profile), "modlist.txt"), modlist);
    }

    /// <summary>Name <paramref name="plugin"/> in the game folder's Skyrim.ccc, so the game loads it itself, as it does a
    /// Creation Club master: the way a test's own master in Data is loaded without being in plugins.txt.</summary>
    public void GameLoads(string plugin) => File.AppendAllText(Path.Combine(GamePath, "Skyrim.ccc"), plugin + "\n");

    /// <summary>Make <paramref name="profile"/> the active one, as Amethyst records a profile switch.</summary>
    public void Activate(string profile) =>
        File.WriteAllText(Path.Combine(GameConfig, "deploy_state.json"), "{\"last_active_profile\": " + Json(profile) + "}");

    /// <summary>Give a profile its own settings, as Amethyst stores them in profile_state.json.</summary>
    public void ProfileSettings(string settingsJson, string profile = "default") =>
        File.WriteAllText(Path.Combine(ProfileDir(profile), "profile_state.json"), "{\"profile_settings\": " + settingsJson + "}");

    /// <summary>How a deploy puts a file into Data, as Amethyst's deploy modes do.</summary>
    public enum Link { Symlink, Hardlink, Copy }

    /// <summary>What a link deploy does: Data moves to Data_Core, and a new Data holds symlinks — to every enabled
    /// mod's files and to the game's own — and deploy_state records it.</summary>
    public void Deploy(params string[] enabledMods) => Deploy(Link.Symlink, ModsDir, enabledMods);

    /// <summary>As <see cref="Deploy(string[])"/>, linking each file the way <paramref name="link"/> says, from the mods in
    /// <paramref name="modsDir"/>; a later mod wins a path an earlier one also has. Hardlinks are Linux-only here.</summary>
    public void Deploy(Link link, string modsDir, params string[] enabledMods)
    {
        Directory.Move(DataDir, DataCore);
        Directory.CreateDirectory(DataDir);
        foreach (var dir in new[] { DataCore }.Concat(enabledMods.Select(m => Path.Combine(modsDir, m))))
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "meta.ini") continue;   // a mod's own metadata is never deployed
                var placed = Path.Combine(DataDir, Path.GetRelativePath(dir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(placed)!);
                if (File.Exists(placed)) File.Delete(placed);
                switch (link)
                {
                    case Link.Symlink: File.CreateSymbolicLink(placed, file); break;
                    case Link.Copy: File.Copy(file, placed); break;
                    case Link.Hardlink:
                        if (HardLink(file, placed) != 0) throw new IOException($"link() failed for '{placed}'");
                        break;
                }
            }
        File.WriteAllText(Path.Combine(GameConfig, "deploy_state.json"),
            "{\"last_active_profile\": \"default\", \"last_deployed\": \"default\", \"deploy_active\": true, \"last_deploy_mode\": \"" +
            link.ToString().ToUpperInvariant() + "\"}");
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
    static extern int HardLink(string existing, string created);

    /// <summary>What a restore does: the deployed Data goes, Data_Core moves back, and deploy_state records it.</summary>
    public void Restore()
    {
        Directory.Delete(DataDir, recursive: true);
        Directory.Move(DataCore, DataDir);
        File.WriteAllText(Path.Combine(GameConfig, "deploy_state.json"),
            "{\"last_active_profile\": \"default\", \"last_deployed\": \"default\", \"deploy_active\": false}");
    }

    /// <summary>The path a plugin takes inside a mod folder, with the folder created.</summary>
    public string InMod(string folder, ModKey key)
    {
        var p = Path.Combine(ModsDir, folder, key.FileName.String);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        return p;
    }

    public LoadOrderService Open(string? location = null)
    {
        var svc = LoadOrderService.WithInstance(location ?? GameConfig, 0, new UserConfigStore(Path.Combine(Root, "houseCARL.user.json")));
        svc.Stats();
        return svc;
    }

    static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    public void Delete() { try { Directory.Delete(Root, true); } catch { /* temp cleanup best-effort */ } }
}
