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
