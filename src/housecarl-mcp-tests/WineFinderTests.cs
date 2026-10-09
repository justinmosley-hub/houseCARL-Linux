using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>Finding the Wine to run Windows programs with (<see cref="WindowsTool.FindWine(bool)"/>), over a fake Steam
/// laid out as Steam lays it out: the override first, then the newest Proton in any library, then system Wine only where
/// it can run what it is asked to. Contract in docs/linux-and-amethyst.md and ADR 0008.</summary>
[Trait("tier", "unit")]
public sealed class WineFinderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-wine-finder-" + Guid.NewGuid().ToString("N"));
    string Steam => Path.Combine(_root, "steam");
    string OtherLibrary => Path.Combine(_root, "games-drive");
    string NoMono => Path.Combine(_root, "no-mono");

    public WineFinderTests()
    {
        Directory.CreateDirectory(Path.Combine(Steam, "steamapps"));
        File.WriteAllText(Path.Combine(Steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + Steam + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + OtherLibrary + "\"\n\t}\n}\n");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* temp scratch */ } }

    /// <summary>A Proton as Steam installs it: files/bin/wine, and its Mono unless <paramref name="mono"/> is false.</summary>
    string Proton(string parent, string name, bool mono = true)
    {
        var dir = Path.Combine(parent, name);
        Directory.CreateDirectory(Path.Combine(dir, "files", "bin"));
        File.WriteAllText(Path.Combine(dir, "files", "bin", "wine"), "");
        if (mono) Directory.CreateDirectory(Path.Combine(dir, "files", "share", "wine", "mono", "wine-mono-11.2.0"));
        return Path.Combine(dir, "files", "bin", "wine");
    }

    string Common(string library) => Path.Combine(library, "steamapps", "common");
    string Tools => Path.Combine(Steam, "compatibilitytools.d");

    WindowsTool.WineChoice? Find(bool needsDotNet, string? chosen = null, string? systemWine = null, string? systemMono = null) =>
        WindowsTool.FindWine(needsDotNet, chosen, new[] { Steam }, systemWine, new[] { systemMono ?? NoMono });

    [Fact]
    public void TheNewestNumberedProtonInAnyLibraryComesFirstThenExperimentalThenCustomBuilds()
    {
        Proton(Common(Steam), "Proton 9.0");
        var newest = Proton(Common(OtherLibrary), "Proton 11.0");
        Proton(Common(Steam), "Proton 10.0");
        Proton(Common(Steam), "Proton - Experimental");
        Proton(Tools, "GE-Proton9-27");
        Proton(Tools, "GE-Proton10-3");
        Directory.CreateDirectory(Path.Combine(Common(Steam), "Proton EasyAntiCheat Runtime"));   // not a Wine

        Assert.Equal(new[] { "Proton 11.0", "Proton 10.0", "Proton 9.0", "Proton - Experimental", "GE-Proton10-3", "GE-Proton9-27" },
                     WindowsTool.Protons(new[] { Steam }).Select(p => p.From["Steam's ".Length..]));
        Assert.Equal(new WindowsTool.WineChoice(newest, "Steam's Proton 11.0"), Find(needsDotNet: true));
    }

    [Fact]
    public void AProtonWithoutItsMonoIsPassedOver()
    {
        Proton(Common(Steam), "Proton 11.0", mono: false);
        var experimental = Proton(Common(Steam), "Proton - Experimental");

        Assert.Equal(experimental, Find(needsDotNet: true)!.Path);
    }

    [LinuxFact("Steam's own paths are links on Linux")]
    public void OneSteamReachedThroughALinkIsCountedOnce()
    {
        Proton(Common(Steam), "Proton 11.0");
        var link = Path.Combine(_root, "dot-steam-steam");
        Directory.CreateSymbolicLink(link, Steam);

        Assert.Single(WindowsTool.Protons(new[] { Steam, link }));
    }

    [Fact]
    public void TheOverrideWinsOverEverything()
    {
        Proton(Common(Steam), "Proton 11.0");

        Assert.Equal(new WindowsTool.WineChoice("/opt/my/wine", "HOUSECARL_WINE"), Find(needsDotNet: true, chosen: "/opt/my/wine"));
    }

    [Fact]
    public void SystemWineRunsWhatNeedsNoMonoButADotNetProgramOnlyWithWineMono()
    {
        Assert.Equal("/usr/bin/wine", Find(needsDotNet: false, systemWine: "/usr/bin/wine")!.Path);   // BSArch
        Assert.Null(Find(needsDotNet: true, systemWine: "/usr/bin/wine"));                              // the compiler: no wine-mono
        var mono = Path.Combine(_root, "usr-share-wine-mono");
        Directory.CreateDirectory(mono);
        Assert.Equal("/usr/bin/wine", Find(needsDotNet: true, systemWine: "/usr/bin/wine", systemMono: mono)!.Path);
    }

    [Fact]
    public void WithNoWineAtAllNothingIsChosenAndTheAnswerSaysWhereItLooked()
    {
        Assert.Null(Find(needsDotNet: true));
        Assert.Equal("no Wine that can run .NET programs was found to run PapyrusCompiler.exe: houseCARL looks for Steam's " +
                     "Proton in every Steam library (any Proton game installs it, Skyrim included), then for wine with the " +
                     "wine-mono package on PATH. Install one, or set HOUSECARL_WINE to a Wine.",
                     WindowsTool.NoWineFound("/ck/PapyrusCompiler.exe", needsDotNet: true));
    }
}
