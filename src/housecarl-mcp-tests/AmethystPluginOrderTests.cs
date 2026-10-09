using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>The plugin order of a profile whose loadorder.txt is partial, as Amethyst's is: completed with what the game
/// loads (base masters and Skyrim.ccc plugins in Data, in the game's order) and with plugins.txt, the file the game
/// reads. The same files read as MO2 stay exactly as listed. Contract in docs/architecture/amethyst-instance.md.</summary>
[Trait("tier", "integration")]
public sealed class AmethystPluginOrderTests : IDisposable
{
    readonly ScratchAmethyst _a = new("hc-amethyst-plugin-order-");

    public AmethystPluginOrderTests()
    {
        foreach (var f in new[] { "Skyrim.esm", "Update.esm", "ccFirst.esm", "ccSecond.esl" })
            File.WriteAllText(Path.Combine(_a.DataDir, f), "");
        // Skyrim.ccc's order, not the alphabet, is the game's; a listed plugin with no file is not loaded.
        File.WriteAllText(Path.Combine(_a.GamePath, "Skyrim.ccc"), "ccSecond.esl\nccNotInstalled.esl\nccFirst.esm\n");
        // As Amethyst seeds a new profile: the fixed block it knows, then a mod plugin only in plugins.txt.
        _a.Profile("Skyrim.esm\nUpdate.esm\n", "*Mine.esp\nOff.esp\n", "+MineMod\n");
    }

    public void Dispose() => _a.Delete();

    Mo2Composition Read(bool partial) =>
        Mo2LoadOrder.ReadComposition(_a.ProfileDir(), _a.ModsDir, _a.DataDir, partial);

    [Fact]
    public void WhatTheGameLoadsUnlistedComesAfterTheListedMastersInTheGamesOrderThenPluginsTxt()
    {
        var comp = Read(partial: true);

        Assert.Equal(new[] { "Skyrim.esm", "Update.esm", "ccSecond.esl", "ccFirst.esm", "Mine.esp", "Off.esp" }, comp.OrderedPluginNames);
        Assert.Equal(new[] { "Skyrim.esm", "Update.esm", "ccSecond.esl", "ccFirst.esm" }, comp.ImplicitPluginNames);
        Assert.Contains("Mine.esp", comp.ActivePluginNames);
        Assert.Equal(new[] { "Off.esp" }, comp.InactivePluginNames);
    }

    [Fact]
    public void TheSameFilesReadAsMo2StayExactlyAsListed()
    {
        Assert.Equal(new[] { "Skyrim.esm", "Update.esm" }, Read(partial: false).OrderedPluginNames);
    }

    [Fact]
    public void ACompleteLoadOrderIsLeftAsItIs()
    {
        var full = "Skyrim.esm\nUpdate.esm\nccFirst.esm\nccSecond.esl\nOff.esp\nMine.esp\n";
        _a.Profile(full, "Off.esp\n*Mine.esp\n", "+MineMod\n");

        Assert.Equal(full.TrimEnd('\n').Split('\n'), Read(partial: true).OrderedPluginNames);
    }

    [Fact]
    public void AnEntryKeptOnlyAsAPositionIsNotLoaded()
    {
        // Amethyst keeps a disabled or removed mod's plugin in loadorder.txt to remember its slot.
        _a.Profile("Skyrim.esm\nUpdate.esm\nRemoved.esp\nMine.esp\n", "*Mine.esp\n", "+MineMod\n");

        var comp = Read(partial: true);

        Assert.DoesNotContain("Removed.esp", comp.OrderedPluginNames);
        Assert.DoesNotContain("Removed.esp", comp.ImplicitPluginNames);
        Assert.Contains("Removed.esp", Read(partial: false).ImplicitPluginNames);   // MO2's reading of the same files is unchanged
    }

    [Fact]
    public void AMissingLoadOrderStaysEmptyAndNamed()
    {
        File.Delete(Path.Combine(_a.ProfileDir(), "loadorder.txt"));
        var warnings = new List<string>();

        var comp = Mo2LoadOrder.ReadComposition(_a.ProfileDir(), _a.ModsDir, _a.DataDir, partialLoadOrder: true, warnings);

        Assert.Empty(comp.OrderedPluginNames);
        Assert.Contains(warnings, w => w.Contains("loadorder.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ALockedModIsEnabledAndAnEntryWithNoFolderIsNot()
    {
        Directory.CreateDirectory(Path.Combine(_a.ModsDir, "Locked Mod"));
        _a.Profile("Skyrim.esm\n", "", "*Locked Mod\n*DLC: Dawnguard\n+MineMod\n-OffMod\n");

        foreach (var partial in new[] { true, false })
        {
            var comp = Read(partial);
            Assert.Equal(new[] { "Locked Mod", "MineMod" }, comp.EnabledMods);
            Assert.Equal(new[] { "OffMod" }, comp.DisabledMods);
        }
    }
}
