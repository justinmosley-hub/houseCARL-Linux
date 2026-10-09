using System.Text.Json;
using HousecarlCore;
using HousecarlMcp;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>The six json write renders — apply, create, write_seq, remove, forward and place — filled far past their
/// cap, with every block they carry (read-back, voice coverage, result-script coverage, cell shell), and read across
/// caps from their floor up. Each tested whether it was ALREADY over before writing the next row, so the row that
/// crossed was written whole and the tail after the rows was never charged (#546, #859, #1016).</summary>
[Trait("tier", "unit")]
public sealed class WriteRenderCapTests
{
    static readonly OrderStamp Stamp = new("deadbeefdeadbeef", Array.Empty<string>());

    /// <summary>A cap no answer here reaches; 0 would be the server default, which these outcomes exceed.</summary>
    const int NoCap = 10_000_000;

    static IReadOnlyList<WritePatchBuilder.FullReadback> ReadBack(int records, int fields) =>
        Enumerable.Range(0, records).Select(r => new WritePatchBuilder.FullReadback(default,
            new RecordFields("Weapon", $"{0x800 + r:X6}:HcCap.esp", $"HcCapWeapon{r:D3}",
                Enumerable.Range(0, fields).Select(f => new FieldValue($"Field{f:D2}", true, $"value {r}.{f}", null)).ToList()),
            null)).ToList();

    static IReadOnlyList<WritePatchBuilder.OpResult> Ops(int n) =>
        Enumerable.Range(0, n).Select(i => new WritePatchBuilder.OpResult(default, "Weapon", $"ops[{i}] Set Damage", true, null, $"{i}")).ToList();

    static readonly WritePatchBuilder.PatchOutcome Patch =
        new(true, null, @"C:\mods\HcCap\HcCap.esp", false, new[] { "Skyrim.esm" }, Ops(60), 4096)
        { Stamp = Stamp, ReadBack = ReadBack(10, 20) };

    static readonly WritePatchBuilder.CreateOutcome Create =
        new(true, null, @"C:\mods\HcCap\HcCap.esp", false,
            Enumerable.Range(0, 20).Select(i => new WritePatchBuilder.CreatedRecord(default, "Weapon", $"HcCapNew{i:D2}", Ops(3))).ToList(),
            new[] { "Skyrim.esm" }, 4096)
        {
            Stamp = Stamp,
            ReadBack = ReadBack(5, 20),
            Voice = new VoiceReport(Enumerable.Range(0, 40).Select(i => new VoiceLine(
                default, "HcCapTopic", i, $@"sound\voice\HcCap.esp\MaleNord\HcCapTopic_{i:D4}.fuz", false, null, false,
                $@"sound\voice\HcCap.esp\MaleNord\HcCapTopic_{i:D4}.lip", false, false)).ToList(), Array.Empty<VoiceUndetermined>()),
            ScriptBinding = new ScriptBindingReport(Enumerable.Range(0, 25).Select(i => new ScriptBindingFinding(
                default, "HcCapTopic", ScriptBindingStatus.BoundAndCompiled, new[] { $"HcCapScript{i:D2}" },
                Array.Empty<string>(), false, "bound and compiled")).ToList()),
            CellShell = new CellShellReport(Enumerable.Range(0, 30).Select(i => new CellShell(
                default, $"HcCapCell{i:D2}", i % 2 == 0, new[] { "lighting template", "navmesh" })).ToList()),
        };

    static readonly SeqOutcome Seq = new(true, null, @"C:\mods\HcCap\SEQ\HcCap.seq", "HcCap",
        Enumerable.Range(0, 80).Select(i => new SeqFile.SeqQuest(default, $"HcCapQuest{i:D2}", 0x01000800u + (uint)i)).ToList(),
        "HcCap.esp", false);

    static readonly WritePatchBuilder.RemovalOutcome Removal = new(true, null, @"C:\mods\HcCap\HcCap.esp",
        Enumerable.Range(0, 80).Select(i => new WritePatchBuilder.RemovedRecord(default, "Weapon", $"HcCapGone{i:D2}")).ToList(),
        new[] { "Skyrim.esm" }, 3, 2048) { Stamp = Stamp };

    static readonly WritePatchBuilder.ForwardOutcome Forward = new(true, null, @"C:\mods\HcCap\HcCap.esp", false,
        Enumerable.Range(0, 60).Select(i => new WritePatchBuilder.ForwardedRecord(default, "Weapon", $"HcCapFwd{i:D2}", "Source.esp", "Winner.esp", false)).ToList(),
        new[] { "Skyrim.esm" }, 4096) { Stamp = Stamp, ReadBack = ReadBack(10, 20) };

    static readonly PlaceOutcome Place = new(
        Enumerable.Range(0, 60).Select(i => PlaceResult.Fail($@"meshes\hccap\mesh{i:D2}.nif", "no provider has this path")).ToList(),
        @"C:\mods\houseCARL - HcCap", Array.Empty<string>(), null, null);

    /// <summary>The smallest cap the render answers inside, then caps above it to past the full answer: none may come
    /// back longer than its cap, and every one is a json document.</summary>
    static void NeverOverTheCapFromItsFloorUp(Func<int, string> render)
    {
        int full = render(NoCap).Length;
        int floor = Enumerable.Range(1, full).First(cap => render(cap).Length <= cap);
        var over = new List<string>();
        for (int cap = floor; cap <= full + 50; cap += 7)
        {
            var r = render(cap);
            using var doc = JsonDocument.Parse(r);
            if (r.Length > cap) over.Add($"{r.Length} at max_chars={cap}");
        }
        Assert.True(over.Count == 0, $"{over.Count} cap(s) answered past the cap, first: {over.FirstOrDefault()}");
        Assert.True(floor < full / 2, $"the floor {floor} is most of the full answer {full}: the rows were not what got cut");
    }

    [Fact] public void ApplyNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(c => JsonWire.RenderPatchOutcome(Patch, c, true, "patch"));
    [Fact] public void CreateNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(c => JsonWire.RenderCreateOutcome(Create, c, true, "patch"));
    [Fact] public void WriteSeqNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(c => JsonWire.RenderSeqOutcome(Seq, c));
    [Fact] public void RemoveNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(c => JsonWire.RenderRemovalOutcome(Removal, c, "patch"));
    [Fact] public void ForwardNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(c => JsonWire.RenderForwardOutcome(Forward, c, true, "patch"));
    [Fact] public void PlaceNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(c => JsonWire.RenderPlaceOutcome(Place, c));

    /// <summary>A cut answer states what it rendered truthfully: the counts are what is in the arrays.</summary>
    [Fact]
    public void ACutAnswerCountsTheRowsItCarries()
    {
        foreach (var cap in new[] { 1_500, 3_000, 6_000 })
        {
            var d = JsonDocument.Parse(JsonWire.RenderCreateOutcome(Create, cap, true, "patch")).RootElement;
            Assert.True(d.GetProperty("truncated").GetBoolean(), $"nothing was cut at max_chars={cap}");
            Assert.Equal(d.GetProperty("created").GetArrayLength(), d.GetProperty("rendered_created").GetInt32());
            var voice = d.GetProperty("voice_coverage");
            Assert.Equal(voice.GetProperty("lines").GetArrayLength(), voice.GetProperty("rendered_lines").GetInt32());
        }
    }

    /// <summary>Under its floor an answer ships with no rows, says it overran, and still counts zero honestly.</summary>
    [Fact]
    public void UnderTheFloorTheAnswerSaysSoAndCountsNoRows()
    {
        var raw = JsonWire.RenderCreateOutcome(Create, 200, true, "patch");
        var d = JsonDocument.Parse(raw).RootElement;
        Assert.True(d.TryGetProperty("max_chars_overrun", out _));
        Assert.Equal(0, d.GetProperty("rendered_created").GetInt32());
        Assert.Equal(0, d.GetProperty("voice_coverage").GetProperty("rendered_lines").GetInt32());
    }

    /// <summary>A cap that holds the whole answer cuts nothing.</summary>
    [Fact]
    public void AFullAnswerIsUncut()
    {
        var d = JsonDocument.Parse(JsonWire.RenderCreateOutcome(Create, NoCap, true, "patch")).RootElement;
        Assert.False(d.GetProperty("truncated").GetBoolean());
        Assert.Equal(20, d.GetProperty("rendered_created").GetInt32());
    }
}
