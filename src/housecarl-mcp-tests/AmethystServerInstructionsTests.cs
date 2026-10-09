using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>What a server started on an Amethyst game says over the wire: its standing instructions and its tool answers
/// in Amethyst's words where MO2's tell the user what to do, the instructions inside Claude Code's cut. The default
/// server's instructions are pinned unchanged by <see cref="ServerInstructionsTests"/>.
/// Contract in docs/architecture/amethyst-instance.md.</summary>
[Trait("tier", "integration")]
public sealed class AmethystServerInstructionsTests
{
    [Fact]
    public void AServerOnAnAmethystGameNamesAmethystAndTheDeployInsideTheCut()
    {
        var a = new ScratchAmethyst("hc-amethyst-instructions-");
        try
        {
            using var server = new ServerFixture(new Dictionary<string, string> { ["HouseCarl__Mo2InstanceDir"] = a.GameConfig });
            var text = server.PublishedInstructions;

            Assert.Contains("reads and writes a Skyrim SE load order via an Amethyst game.", text);
            Assert.Contains("waits until the user enables its mod and Deploys.", text);
            Assert.DoesNotContain("MO2", text);
            Assert.True(text.Length <= PublishedDescriptionBoundTests.Bound, $"{text.Length} characters");
        }
        finally { a.Delete(); }
    }

    [Fact]
    public void AServerOnAnAmethystGameAnswersInAmethystsWords()
    {
        var a = new ScratchAmethyst("hc-amethyst-wire-");
        try
        {
            // A master in the game's Data, and a houseCARL patch over it that Amethyst has not listed yet.
            var masterKey = new ModKey("HcWireMaster", ModType.Master);
            var master = new SkyrimMod(masterKey, SkyrimRelease.SkyrimSE);
            master.Weapons.Add(new Weapon(new FormKey(masterKey, 0x800), SkyrimRelease.SkyrimSE) { EditorID = "HcWireWeapon" });
            var masterPath = Path.Combine(a.DataDir, masterKey.FileName.String);
            master.BeginWrite.ToPath(masterPath).WithLoadOrder(Array.Empty<ISkyrimModGetter>()).Write();
            using (var ov = SkyrimMod.CreateFromBinaryOverlay(masterPath, SkyrimRelease.SkyrimSE))
            {
                var patch = new SkyrimMod(new ModKey("HcWire", ModType.Plugin), SkyrimRelease.SkyrimSE);
                WriteEngine.GenericGetOrAddAsOverride(patch, ov.Weapons.First(), ov.ToImmutableLinkCache());
                patch.BeginWrite.ToPath(a.InMod("houseCARL - HcWire", patch.ModKey)).WithLoadOrder(new[] { ov }).NoNextFormIDProcessing().Write();
            }
            a.Profile("HcWireMaster.esm\n", "", "");
            File.WriteAllText(Path.Combine(a.ProfileDir(), "Skyrim.ini"), "[Archive]\nsResourceArchiveList=\n");

            using var server = new ServerFixture(new Dictionary<string, string> { ["HouseCarl__Mo2InstanceDir"] = a.GameConfig });
            var read = server.Call("housecarl_records", "{\"formids\":[\"000800:HcWireMaster.esm\"],\"source\":\"HcWire.esp\"}");

            Assert.Contains("Amethyst has not listed that mod folder yet — refresh Amethyst, enable the mod, then Deploy", read.Text);
            Assert.DoesNotContain("MO2", read.Text);
        }
        finally { a.Delete(); }
    }
}
