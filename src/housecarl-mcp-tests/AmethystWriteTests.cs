using System.Text.Json;
using HousecarlCore;
using HousecarlMcp;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>Writing into an Amethyst game: a new patch lands in the active profile's mods folder, stays houseCARL's after
/// Amethyst rewrites its meta.ini in its own format, and the answers tell the caller what Amethyst does differently.
/// Contract in docs/architecture/amethyst-instance.md.</summary>
[Trait("tier", "integration")]
public sealed class AmethystWriteTests : IDisposable
{
    readonly ScratchAmethyst _a = new("hc-amethyst-write-");
    readonly ModKey _master = new("HcAwMaster", ModType.Master);
    readonly string _weapon;

    public AmethystWriteTests()
    {
        var master = new SkyrimMod(_master, SkyrimRelease.SkyrimSE);
        master.Weapons.Add(new Weapon(new FormKey(_master, 0x800), SkyrimRelease.SkyrimSE) { EditorID = "HcAwWeapon", BasicStats = new WeaponBasicStats { Damage = 5 } });
        master.BeginWrite.ToPath(Path.Combine(_a.DataDir, _master.FileName.String)).WithLoadOrder(Array.Empty<ISkyrimModGetter>()).Write();
        _weapon = $"000800:{_master.FileName}";
        // Your own setup's shape: the active profile keeps its own mods.
        _a.Profile("HcAwMaster.esm\n", "", "");
        _a.ProfileSettings("{\"profile_specific_mods\": true}");
        Directory.CreateDirectory(Path.Combine(_a.ProfileDir(), "mods"));
        File.WriteAllText(Path.Combine(_a.ProfileDir(), "Skyrim.ini"), "[Archive]\nsResourceArchiveList=\n");
    }

    public void Dispose() => _a.Delete();

    static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement.Clone();

    string SetDamage(LoadOrderService svc, int damage, string? patch = null, string? into = null) =>
        ApplyTools.Apply(svc, ops: Doc($"[{{\"formid\":\"{_weapon}\",\"field_path\":\"BasicStats.Damage\",\"value\":\"{damage}\"}}]"),
                         patch: patch, into: into);

    [Fact]
    public void ANewPatchLandsInTheActiveProfilesOwnModsFolder()
    {
        using var svc = _a.Open();

        var answer = SetDamage(svc, 50, patch: "HcAwFix");

        var folder = Path.Combine(_a.ProfileDir(), "mods", "houseCARL - HcAwFix");
        Assert.True(File.Exists(Path.Combine(folder, "HcAwFix.esp")), answer);
        Assert.Contains("mod folder: houseCARL - HcAwFix", answer);
        Assert.False(Directory.Exists(Path.Combine(_a.ModsDir, "houseCARL - HcAwFix")));   // never the shared folder
    }

    [Fact]
    public void APatchStaysHouseCarlsAfterAmethystRewritesItsMetaIni()
    {
        using var svc = _a.Open();
        SetDamage(svc, 50, patch: "HcAwFix");
        var meta = Path.Combine(_a.ProfileDir(), "mods", "houseCARL - HcAwFix", "meta.ini");
        // As Python's configparser writes it back: keys lower-cased, " = " between key and value.
        File.WriteAllText(meta, "[General]\ngamename = skyrimse\nmodid = 0\nversion = 1.0\n\n[houseCARL]\ngenerated = true\n" +
                                "plugin = HcAwFix.esp\n\n[installedFiles]\nsize = 0\n\n");

        var answer = SetDamage(svc, 60, into: "HcAwFix");

        Assert.Contains("extended HcAwFix.esp", answer);
    }

    string OwnMods => Path.Combine(_a.ProfileDir(), "mods");

    /// <summary>A mod in the profile's own mods folder whose plugin overrides the master's weapon, enabled.</summary>
    void OwnModWithItsPlugin()
    {
        var key = new ModKey("HcAwOwn", ModType.Plugin);
        var path = Path.Combine(OwnMods, "OwnMod", key.FileName.String);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var ov = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_a.DataDir, _master.FileName.String), SkyrimRelease.SkyrimSE))
        {
            var own = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
            WriteEngine.GenericGetOrAddAsOverride(own, ov.Weapons.First(), ov.ToImmutableLinkCache());
            own.BeginWrite.ToPath(path).WithLoadOrder(new[] { ov }).NoNextFormIDProcessing().Write();
        }
        _a.Profile("HcAwMaster.esm\nHcAwOwn.esp\n", "*HcAwOwn.esp\n", "+OwnMod\n");
    }

    string EditInPlace(LoadOrderService svc) =>
        ApplyTools.Apply(svc, ops: Doc($"[{{\"formid\":\"{_weapon}\",\"field_path\":\"BasicStats.Damage\",\"value\":\"77\"}}]"),
                         in_place: "HcAwOwn.esp", acknowledge: true);

    [Fact]
    public void AnInPlaceEditOfAnUndeployedProfileSaysTheGameSeesItAfterADeploy()
    {
        OwnModWithItsPlugin();
        using var svc = _a.Open();

        Assert.Contains("Amethyst has not deployed this profile, so the game sees this edit after a Deploy in Amethyst.", EditInPlace(svc));
    }

    [LinuxFact("Amethyst's link deploy is Linux; Windows needs admin rights for symlinks")]
    public void AnInPlaceEditBehindASymlinkIsAlreadyWhatTheGameSees()
    {
        OwnModWithItsPlugin();
        _a.Deploy(ScratchAmethyst.Link.Symlink, OwnMods, "OwnMod");
        using var svc = _a.Open();

        Assert.Contains("the game already sees this edit: Amethyst deployed 'HcAwOwn.esp' as a link to this file.", EditInPlace(svc));
    }

    [LinuxFact("Amethyst's hardlink deploy is Linux")]
    public void AnInPlaceEditBehindAHardlinkSaysTheGameStillReadsTheOldFile()
    {
        OwnModWithItsPlugin();
        _a.Deploy(ScratchAmethyst.Link.Hardlink, OwnMods, "OwnMod");
        using var svc = _a.Open();

        var answer = EditInPlace(svc);

        Assert.Contains("the game does NOT see this edit yet: the file Amethyst deployed at 'HcAwOwn.esp' is the one from before it", answer);
        Assert.Contains("Deploy in Amethyst to pick it up", answer);
    }

    [LinuxFact("Amethyst's link deploy is Linux")]
    public void AnInPlaceEditBehindACopySaysTheGameStillReadsTheOldFile()
    {
        OwnModWithItsPlugin();
        _a.Deploy(ScratchAmethyst.Link.Copy, OwnMods, "OwnMod");
        using var svc = _a.Open();

        Assert.Contains("the game does NOT see this edit yet", EditInPlace(svc));
    }

    [LinuxFact("Amethyst's link deploy is Linux; Windows needs admin rights for symlinks")]
    public void AnInPlaceEditOfAModDeployedWithoutItSaysTheGameSeesItOnlyAfterADeploy()
    {
        OwnModWithItsPlugin();
        _a.Deploy(ScratchAmethyst.Link.Symlink, OwnMods);   // deployed before the mod was enabled
        using var svc = _a.Open();

        Assert.Contains("the deployed Data has no 'HcAwOwn.esp', so the game sees this edit only after a Deploy in Amethyst.", EditInPlace(svc));
    }

    string OperationLock => Path.Combine(_a.Staging, AmethystInstance.OperationLockFileName);

    /// <summary>The lock as Amethyst takes it for a deploy or install: exclusive, and never waiting.</summary>
    FileStream LockAsAmethystDoes() =>
        new(OperationLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    [Fact]
    public void WhileAmethystDeploysAWriteIsRefusedAndWritesNothing()
    {
        using var svc = _a.Open();
        string answer;
        using (LockAsAmethystDoes())
            answer = SetDamage(svc, 50, patch: "HcAwBusy");

        Assert.Equal("error: Amethyst is deploying or installing into this game right now, so houseCARL wrote nothing; " +
                     "run this again when Amethyst finishes.", answer);
        Assert.False(Directory.Exists(Path.Combine(OwnMods, "houseCARL - HcAwBusy")));
        Assert.Contains("wrote HcAwBusy.esp", SetDamage(svc, 50, patch: "HcAwBusy"));   // and once it finishes, the same call lands
    }

    [Fact]
    public void WhileHouseCarlWritesAmethystCannotStartADeploy()
    {
        using (new AmethystManagerInstance(_a.GameConfig).HoldForWrite())
            Assert.ThrowsAny<IOException>(() => LockAsAmethystDoes());
        using (LockAsAmethystDoes()) { }   // released with the hold
    }

    [Fact]
    public void AWriteLeavesTheLockFree()
    {
        using var svc = _a.Open();
        SetDamage(svc, 50, patch: "HcAwFree");

        using (LockAsAmethystDoes()) { }
    }

    [Fact]
    public void StatusAndSetupSayWhatAmethystDoesDifferently()
    {
        using var svc = _a.Open();

        Assert.Equal(AmethystInstance.WorkflowNote, svc.StatusData().ManagerNote);
        var status = StatusTools.LoadOrderStatus(svc, new ToolPathResolver(new UserConfigStore(Path.Combine(_a.Root, "tools.json"))));
        Assert.Contains("manager:  Amethyst\n  note: messages that say MO2 mean Amethyst here.", status);
        Assert.Contains("only after a Deploy in Amethyst", status);
        Assert.Contains("note: messages that say MO2 mean Amethyst here.",
                        SetupTools.Render(AmethystInstance.Resolve(_a.GameConfig), "Amethyst", persisted: true, persistError: null, persistNote: null));
    }

    [Fact]
    public void Mo2CarriesNoNote()
    {
        var mo2 = new ScratchMo2("hc-amethyst-write-mo2-");
        try
        {
            mo2.Profile("Skyrim.esm\r\n", "", "");
            Assert.Null(new Mo2ManagerInstance(mo2.Instance).WorkflowNote);
            Assert.DoesNotContain("note:", SetupTools.Render(Mo2Instance.Resolve(mo2.Instance), "Mod Organizer 2", persisted: true, persistError: null, persistNote: null));
        }
        finally { mo2.Delete(); }
    }
}
