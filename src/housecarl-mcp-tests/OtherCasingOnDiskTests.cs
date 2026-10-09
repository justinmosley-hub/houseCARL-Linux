using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>Game files and folders in a casing the code does not spell: mods authored on Windows ship <c>Mod.ESP</c>,
/// <c>strings\</c> and <c>skyrim.ini</c> as often as the spelling in the source, and a case-sensitive disk must still
/// find them (docs/architecture/linux-paths.md). On Windows every one of these holds by NTFS alone.</summary>
[Trait("tier", "integration")]
public sealed class OtherCasingOnDiskTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-other-casing-" + Guid.NewGuid().ToString("N"));
    readonly string _profile, _mods, _data, _mod;

    public OtherCasingOnDiskTests()
    {
        _profile = Path.Combine(_root, "profiles", "Default");
        _mods = Path.Combine(_root, "mods");
        _data = Path.Combine(_root, "game", "Data");
        _mod = Path.Combine(_mods, "UpMod");
        foreach (var d in new[] { _profile, _data, _mod }) Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(_profile, "modlist.txt"), "+UpMod\n");
        File.WriteAllText(Path.Combine(_profile, "loadorder.txt"), "UpMod.esp\n");
        File.WriteAllText(Path.Combine(_profile, "plugins.txt"), "*UpMod.esp\n");
        File.WriteAllText(Path.Combine(_mod, "UpMod.ESP"), "");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { /* temp cleanup best-effort */ } }

    [Fact]
    public void APluginShippedWithAnUpperCaseExtensionResolves()
    {
        var order = Mo2LoadOrder.Build(_profile, _mods, _data);

        Assert.Equal(Path.Combine(_mod, "UpMod.ESP"), Assert.Single(order.OrderedPaths));
    }

    [Fact]
    public void AnUpperCaseArchiveBindsToItsPlugin()
    {
        File.WriteAllText(Path.Combine(_mod, "UpMod.BSA"), "");

        var found = ArchiveDiscovery.Discover(_profile, _mods, _data, "", Path.GetDirectoryName(_data)!);

        Assert.Contains(found.Archives, a => a.OwningPlugin.Equals("UpMod.esp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ALowerCaseSkyrimIniIsRead()
    {
        File.WriteAllText(Path.Combine(_profile, "skyrim.ini"), "[Archive]\nsResourceArchiveList=Skyrim - Misc.bsa\n");

        var found = ArchiveDiscovery.Discover(_profile, _mods, _data, "", Path.GetDirectoryName(_data)!);

        Assert.DoesNotContain(found.Warnings, w => w.Contains("sResourceArchiveList", StringComparison.Ordinal));
    }

    [Fact]
    public void ALowerCaseStringsFolderIsThePluginsOwnStrings()
    {
        Directory.CreateDirectory(Path.Combine(_mod, "strings"));
        File.WriteAllText(Path.Combine(_mod, "strings", "UpMod_English.STRINGS"), "");

        Assert.True(LocalizedStrings.OwnFolderCarriesStringsFor(Path.Combine(_mod, "UpMod.ESP")));
    }

    [Fact]
    public void AnUpperCasePapyrusSourceCounts()
    {
        var src = Path.Combine(_mod, "source", "scripts");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "UpScript.PSC"), "Scriptname UpScript\n");

        Assert.True(PapyrusSourceRoots.HasSources(src));
    }
}
