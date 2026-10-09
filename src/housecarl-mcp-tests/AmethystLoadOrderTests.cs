using System.Text.Json;
using HousecarlCore;
using HousecarlMcp;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>The service over an Amethyst game end to end, with real plugins: the winner of a record comes from the mod
/// Amethyst ranks highest, a deploy's symlinks in Data are never read as a second copy (Data_Core is the game), and a
/// deploy, restore or profile switch in Amethyst is picked up on the next call without a restart.</summary>
[Trait("tier", "integration")]
public sealed class AmethystLoadOrderTests : IDisposable
{
    readonly ScratchAmethyst _a = new("hc-amethyst-order-");
    readonly ModKey _master = new("HcAmMaster", ModType.Master);
    readonly ModKey _patch = new("HcAmPatch", ModType.Plugin);
    readonly FormKey _weapon;

    public AmethystLoadOrderTests()
    {
        _weapon = new FormKey(_master, 0x800);
        // The game's own master in Data; a mod's override of its weapon in staging.
        var master = new SkyrimMod(_master, SkyrimRelease.SkyrimSE);
        master.Weapons.Add(new Weapon(_weapon, SkyrimRelease.SkyrimSE) { EditorID = "HcAmWeapon", BasicStats = new WeaponBasicStats { Damage = 5 } });
        var masterPath = Path.Combine(_a.DataDir, _master.FileName.String);
        master.BeginWrite.ToPath(masterPath).WithLoadOrder(Array.Empty<ISkyrimModGetter>()).Write();
        _a.GameLoads(_master.FileName.String);
        WriteOverride(_patch, _a.InMod("PatchMod", _patch));
        // As Amethyst writes them for Skyrim SE: every plugin in loadorder.txt, base-game ones left out of plugins.txt.
        _a.Profile("HcAmMaster.esm\nHcAmPatch.esp\n", "*HcAmPatch.esp\n", "+PatchMod\n");
        File.WriteAllText(Path.Combine(_a.ProfileDir(), "Skyrim.ini"), "[Archive]\nsResourceArchiveList=\n");
    }

    public void Dispose() => _a.Delete();

    /// <summary>A plugin overriding the master's weapon, written to <paramref name="path"/>.</summary>
    void WriteOverride(ModKey key, string path)
    {
        using var ov = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_a.DataDir, _master.FileName.String), SkyrimRelease.SkyrimSE);
        var patch = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
        var w = (Weapon)WriteEngine.GenericGetOrAddAsOverride(patch, ov.Weapons.First(), ov.ToImmutableLinkCache());
        w.BasicStats!.Damage = 42;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        patch.BeginWrite.ToPath(path).WithLoadOrder(new[] { ov }).NoNextFormIDProcessing().Write();
    }

    /// <summary>A second profile, "Testing", with its own mods folder holding one mod whose plugin overrides the weapon;
    /// its loadorder.txt is as Amethyst seeds it, so the plugin is only in plugins.txt.</summary>
    void TestingProfileWithItsOwnMods()
    {
        _a.Profile("HcAmMaster.esm\n", "*HcAmOwn.esp\n", "*OwnMod\n", profile: "Testing");
        File.WriteAllText(Path.Combine(_a.ProfileDir("Testing"), "Skyrim.ini"), "[Archive]\nsResourceArchiveList=\n");
        _a.ProfileSettings("{\"profile_specific_mods\": true}", profile: "Testing");
        WriteOverride(new ModKey("HcAmOwn", ModType.Plugin), Path.Combine(_a.ProfileDir("Testing"), "mods", "OwnMod", "HcAmOwn.esp"));
    }

    string Winner(LoadOrderService svc) =>
        JsonDocument.Parse(RecordsTools.Records(svc, formids: new[] { $"{_weapon.ID:X6}:{_master.FileName}" }, format: "json"))
            .RootElement.GetProperty("records")[0].GetProperty("winner").GetString()!;

    [Fact]
    public void TheModsOverrideWinsAndTheMasterComesFromTheGame()
    {
        using var svc = _a.Open();

        Assert.Equal("HcAmPatch.esp", Winner(svc));
        var data = svc.StatusData();
        Assert.Equal("Amethyst", data.Manager);
        Assert.Contains("HcAmMaster.esm", data.Composition.ImplicitPluginNames);
    }

    [LinuxFact("Amethyst's link deploy is Linux; Windows needs admin rights for symlinks")]
    public void WhileDeployedThePatchHasOneCopyAndTheMasterIsReadFromDataCore()
    {
        _a.Deploy("PatchMod");
        using var svc = _a.Open();

        var roots = ((ILoadOrderHost)svc).CaptureRoots();
        Assert.Equal(_a.DataCore, roots.DataDir);
        var copies = Mo2LoadOrder.LocatePlugin(roots.ProfileDir, roots.ModsDir, roots.DataDir, roots.OverwriteDir, "HcAmPatch.esp");
        Assert.Single(copies);                                   // the mod's own file, never Data's symlink to it
        Assert.Equal("HcAmPatch.esp", Winner(svc));
    }

    [LinuxFact("Amethyst's link deploy is Linux; Windows needs admin rights for symlinks")]
    public void ADeployAndARestoreArePickedUpWithoutARestart()
    {
        using var svc = _a.Open();
        Assert.Equal(_a.DataDir, ((ILoadOrderHost)svc).CaptureRoots().DataDir);

        _a.Deploy("PatchMod");
        svc.Stats();
        Assert.Equal(_a.DataCore, ((ILoadOrderHost)svc).CaptureRoots().DataDir);

        _a.Restore();
        svc.Stats();
        Assert.Equal(_a.DataDir, ((ILoadOrderHost)svc).CaptureRoots().DataDir);
        Assert.Equal("HcAmPatch.esp", Winner(svc));
    }

    [Fact]
    public void AProfileSwitchInAmethystIsPickedUpWithoutARestart()
    {
        using var svc = _a.Open();
        Assert.Equal("HcAmPatch.esp", Winner(svc));

        // A second profile with the patch's mod switched off.
        _a.Profile("HcAmMaster.esm\nHcAmPatch.esp\n", "HcAmPatch.esp\n", "-PatchMod\n", profile: "Vanilla");
        File.WriteAllText(Path.Combine(_a.ProfileDir("Vanilla"), "Skyrim.ini"), "[Archive]\nsResourceArchiveList=\n");
        _a.Activate("Vanilla");

        Assert.Equal("HcAmMaster.esm", Winner(svc));
        Assert.Equal("Vanilla", svc.StatusData().ProfileName);
    }

    [Fact]
    public void APluginOnlyInPluginsTxtIsLoadedAsTheGameLoadsIt()
    {
        // Amethyst adds a new mod's plugin to plugins.txt; loadorder.txt waits for its plugin panel to save.
        _a.Profile("HcAmMaster.esm\n", "*HcAmPatch.esp\n", "+PatchMod\n");
        using var svc = _a.Open();

        Assert.Equal("HcAmPatch.esp", Winner(svc));
        Assert.Equal(2, svc.StatusData().ResolvedPluginCount);
    }

    [Fact]
    public void SwitchingBetweenAProfileWithItsOwnModsAndOneWithTheSharedModsSwapsBothWays()
    {
        TestingProfileWithItsOwnMods();
        using var svc = _a.Open();
        Assert.Equal("HcAmPatch.esp", Winner(svc));

        _a.Activate("Testing");
        Assert.Equal("HcAmOwn.esp", Winner(svc));                // read from Testing's own mods folder, order completed
        var testing = ((ILoadOrderHost)svc).CaptureRoots();
        Assert.Equal(Path.Combine(_a.ProfileDir("Testing"), "mods"), testing.ModsDir);
        Assert.Equal(2, svc.StatusData().ResolvedPluginCount);    // the shared folder's patch is not in this profile

        _a.Activate("default");
        Assert.Equal("HcAmPatch.esp", Winner(svc));
        Assert.Equal(_a.ModsDir, ((ILoadOrderHost)svc).CaptureRoots().ModsDir);
    }

    [Fact]
    public void APluginEnabledInAmethystMidSessionIsPickedUp()
    {
        _a.Profile("HcAmMaster.esm\n", "HcAmPatch.esp\n", "+PatchMod\n");
        using var svc = _a.Open();
        Assert.Equal("HcAmMaster.esm", Winner(svc));

        File.WriteAllText(Path.Combine(_a.ProfileDir(), "plugins.txt"), "*HcAmPatch.esp\n");   // ticked in Amethyst's plugin panel

        Assert.Equal("HcAmPatch.esp", Winner(svc));
    }

    [Fact]
    public void AnotherProfileIsReadFromItsOwnModsFolderWithoutSwitching()
    {
        TestingProfileWithItsOwnMods();
        using var svc = _a.Open();

        var other = svc.NamedProfileComposition("Testing");

        Assert.Equal(new[] { "OwnMod" }, other.Composition!.EnabledMods);   // a locked mod, found in Testing's own folder
        Assert.Contains("HcAmOwn.esp", other.Composition.ActivePluginNames);
        Assert.Equal("default", svc.StatusData().ProfileName);
    }

    [Fact]
    public void ARemovedModsRememberedSlotIsNeitherLoadedNorWarnedAbout()
    {
        // As after removing a mod in Amethyst: its plugin is gone from plugins.txt and from disk, its slot remembered.
        _a.Profile("HcAmMaster.esm\nHcAmGone.esp\nHcAmPatch.esp\n", "*HcAmPatch.esp\n", "+PatchMod\n");
        using var svc = _a.Open();

        var status = svc.StatusData();
        Assert.Equal(2, status.ResolvedPluginCount);
        Assert.DoesNotContain(status.Warnings, w => w.Contains("HcAmGone.esp", StringComparison.Ordinal));
        Assert.DoesNotContain("HcAmGone.esp", status.Composition.ImplicitPluginNames);
    }

    [Fact]
    public void TheSetupToolPointsHouseCarlAtAnAmethystGame()
    {
        using var svc = LoadOrderService.WithInstance(null, 0, new UserConfigStore(Path.Combine(_a.Root, "u.json")));
        var (paths, manager, persisted, _, _) = svc.SetInstance(_a.GameConfig);

        Assert.Equal("Amethyst", manager);
        Assert.True(persisted);
        Assert.Equal("default", paths.ProfileName);
        Assert.Equal("HcAmPatch.esp", Winner(svc));
    }
}
