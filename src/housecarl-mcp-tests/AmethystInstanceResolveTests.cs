using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>Deriving the load-order roots and the active profile from one Amethyst game's config folder
/// (<see cref="AmethystInstance"/>) over synthetic setups laid out as Amethyst lays them out; each rule is Amethyst's
/// own and is in docs/architecture/amethyst-instance.md.</summary>
[Trait("tier", "integration")]
public sealed class AmethystInstanceResolveTests : IDisposable
{
    readonly ScratchAmethyst _a = new("hc-amethyst-resolve-");

    public void Dispose() => _a.Delete();

    static void SamePath(string want, string got) =>
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(want)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(got)));

    [Fact]
    public void TheRootsComeFromPathsJsonAndTheStagingFolder()
    {
        var p = AmethystInstance.Resolve(_a.GameConfig);

        Assert.Equal("default", p.ProfileName);
        SamePath(_a.ProfileDir(), p.ProfileDir);
        SamePath(_a.ModsDir, p.ModsDir);
        SamePath(_a.OverwriteDir, p.OverwriteDir);
        SamePath(_a.DataDir, p.DataDir);
        SamePath(_a.GamePath, p.GamePath);
    }

    [Fact]
    public void TheActiveProfileIsTheOneDeployStateRecords()
    {
        _a.Profile("Skyrim.esm\n", "", "", profile: "Testing");
        _a.Activate("Testing");

        var p = AmethystInstance.Resolve(_a.GameConfig);

        Assert.Equal("Testing", p.ProfileName);
        SamePath(_a.ProfileDir("Testing"), p.ProfileDir);
    }

    [Fact]
    public void WithNoRecordedProfileTheActiveOneIsDefault()
    {
        File.WriteAllText(Path.Combine(_a.GameConfig, "deploy_state.json"), "{}");

        Assert.Equal("default", AmethystInstance.Resolve(_a.GameConfig).ProfileName);
    }

    [Fact]
    public void AProfileWithItsOwnModsReadsTheModsAndOverwriteInsideIt()
    {
        _a.ProfileSettings("{\"profile_specific_mods\": true}");
        Directory.CreateDirectory(Path.Combine(_a.ProfileDir(), "mods"));

        var p = AmethystInstance.Resolve(_a.GameConfig);

        SamePath(Path.Combine(_a.ProfileDir(), "mods"), p.ModsDir);
        SamePath(Path.Combine(_a.ProfileDir(), "overwrite"), p.OverwriteDir);
    }

    [Fact]
    public void AProfilesOwnGamePathWins()
    {
        var other = Path.Combine(_a.Root, "other-game");
        Directory.CreateDirectory(Path.Combine(other, "Data"));
        _a.ProfileSettings("{\"game_path\": " + System.Text.Json.JsonSerializer.Serialize(other) + "}");

        SamePath(Path.Combine(other, "Data"), AmethystInstance.Resolve(_a.GameConfig).DataDir);
    }

    [LinuxFact("Amethyst's link deploy is Linux; Windows needs admin rights for symlinks")]
    public void WhileDeployedTheGameIsReadFromDataCoreNeverTheDeployedData()
    {
        _a.Deploy();

        SamePath(_a.DataCore, AmethystInstance.Resolve(_a.GameConfig).DataDir);
    }

    [Fact]
    public void DeployedWithNoDataCoreIsRefusedByName()
    {
        File.WriteAllText(Path.Combine(_a.GameConfig, "deploy_state.json"), "{\"last_active_profile\": \"default\", \"deploy_active\": true}");

        var ex = Assert.Throws<InvalidOperationException>(() => AmethystInstance.Resolve(_a.GameConfig));
        Assert.Contains("Data_Core", ex.Message);
    }

    [Fact]
    public void AVfsDeployLeavesDataAsTheGame()
    {
        File.WriteAllText(Path.Combine(_a.GameConfig, "deploy_state.json"), "{\"last_active_profile\": \"default\", \"deploy_active\": true}");
        File.WriteAllText(Path.Combine(_a.GameConfig, "game_settings.json"), "{\"vfs_enabled\": true}");

        SamePath(_a.DataDir, AmethystInstance.Resolve(_a.GameConfig).DataDir);
    }

    [Fact]
    public void AMalformedPathsJsonIsRefusedNamingTheFile()
    {
        File.WriteAllText(Path.Combine(_a.GameConfig, "paths.json"), "{ not json");

        var ex = Assert.Throws<InvalidOperationException>(() => AmethystInstance.Resolve(_a.GameConfig));
        Assert.Contains("paths.json", ex.Message);
    }

    [Fact]
    public void AMissingProfileFolderIsNamed()
    {
        _a.Activate("Nowhere");

        var (ok, _, problems) = AmethystInstance.Validate(_a.GameConfig);

        Assert.False(ok);
        Assert.Contains(problems, p => p.Contains("'Nowhere'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyStagingPathMeansAmethystsProfilesFolder()
    {
        var legacy = Path.Combine(_a.ConfigDir, "Profiles");
        Directory.CreateDirectory(legacy);

        Assert.Equal(Path.Combine(legacy, ScratchAmethyst.Game), AmethystInstance.StagingRoot(_a.ConfigDir, ScratchAmethyst.Game, ""));
        Assert.Equal(_a.Staging, AmethystInstance.StagingRoot(_a.ConfigDir, ScratchAmethyst.Game, _a.Staging));
    }

    [Fact]
    public void TheStagingFolderFindsItsGameConfig()
    {
        Assert.Equal(_a.GameConfig, AmethystInstance.GameConfigFor(_a.Staging, new[] { _a.ConfigDir }));
        Assert.Equal(_a.GameConfig, AmethystInstance.GameConfigFor(_a.GameConfig, new[] { _a.ConfigDir }));
        Assert.Null(AmethystInstance.GameConfigFor(_a.GamePath, new[] { _a.ConfigDir }));
    }

    [Fact]
    public void OpenTellsTheManagersApartByWhatTheFolderHolds()
    {
        var mo2 = new ScratchMo2("hc-amethyst-open-mo2-");
        try
        {
            Assert.Equal("Mod Organizer 2", ManagerInstance.Open(mo2.Instance).Manager);
            Assert.Equal("Amethyst", ManagerInstance.Open(_a.GameConfig).Manager);
            // Neither: MO2's resolve names what is missing, Amethyst included.
            var ex = Assert.Throws<InvalidOperationException>(() => ManagerInstance.Open(_a.GamePath).Resolve());
            Assert.Contains("Amethyst", ex.Message);
        }
        finally { mo2.Delete(); }
    }

    [LinuxFact("Amethyst's link deploy is Linux; Windows needs admin rights for symlinks")]
    public void TheFingerprintMovesOnAProfileSwitchADeployAndARestore()
    {
        var instance = new AmethystManagerInstance(_a.GameConfig);
        var before = instance.Fingerprint();
        Assert.Equal(before, instance.Fingerprint());

        _a.Deploy();
        var deployed = instance.Fingerprint();
        Assert.NotEqual(before, deployed);

        _a.Restore();
        Assert.NotEqual(deployed, instance.Fingerprint());

        _a.Profile("Skyrim.esm\n", "", "", profile: "Other");
        var restored = instance.Fingerprint();
        _a.Activate("Other");
        Assert.NotEqual(restored, instance.Fingerprint());
    }
}
