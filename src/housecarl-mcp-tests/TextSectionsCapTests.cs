using HousecarlGenerator;
using HousecarlCore;
using HousecarlMcp;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>The three text renders #1016 named — load_order_status, update_status and bsa_list — filled far past
/// their cap and read at every cap from their floor up: max_chars is a ceiling, not a point to stop at, so no answer
/// may come back longer than the cap it was given once that cap holds the parts every answer carries.</summary>
[Trait("tier", "unit")]
public sealed class TextSectionsCapTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-text-sections-" + Guid.NewGuid().ToString("N"));

    public TextSectionsCapTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { /* temp cleanup best-effort */ } }

    static IReadOnlyList<string> Names(string stem, int n) => Enumerable.Range(0, n).Select(i => $"{stem} {i:D3}").ToList();

    static string Status(int cap)
    {
        var c = new Mo2Composition(Names("Enabled Mod", 40), Names("Disabled Mod", 80), Names("Plugin", 120).Select(n => n + ".esp").ToList(),
                                   new HashSet<string>(), Names("Inactive", 80).Select(n => n + ".esp").ToList(), Names("Implicit", 30).Select(n => n + ".esm").ToList());
        var d = new LoadOrderStatusData(c, Names("a warning about something", 20), 120, 0, false, "", "", "", "", "Default", @"C:\MO2",
                                        Names("Excluded", 10).ToDictionary(n => n + ".esp", _ => "it would not open"));
        var logs = new[] { new LogFolderView("papyrus_logs", null, ToolPathSource.Unset), new LogFolderView("crash_logs", null, ToolPathSource.Unset) };
        var profiles = new NamedProfileResult(true, Names("Profile", 30), null, null, null, Array.Empty<string>());
        return StatusWire.Render(d, logs, profiles, null, null, cap);
    }

    static string Updates(int cap)
    {
        var rows = Enumerable.Range(0, 120).Select(i => new ModUpdateEntry(
            $"Some Mod {i:D3}", true, 1000 + i, "1.0", i % 4 == 0 ? "1.0" : "2.0", i % 3 == 0 ? "2.0" : null, null, new[] { 5000 + i })).ToList();
        return UpdateStatusWire.Render(new UpdateCacheData("", @"C:\MO2", rows, Array.Empty<string>(), 0), cap);
    }

    string Bsa(int cap)
    {
        var archive = Path.Combine(_root, "many.bsa");
        if (!File.Exists(archive))
            File.WriteAllBytes(archive, BsaBuilder.Build(105, BsaBuilder.HasFolderNames | BsaBuilder.HasFileNames,
                Enumerable.Range(0, 20).Select(f => ($@"meshes\folder{f:D2}",
                    Enumerable.Range(0, 10).Select(i => ($"mesh{i:D2}.nif", BsaBuilder.Bytes("NIF", 8))).ToArray())).ToArray()));
        return BsaTools.BsaList(archive, max_chars: cap);
    }

    /// <summary>The smallest cap the render answers inside, then every cap above it to well past the full answer.</summary>
    static void NeverOverTheCapFromItsFloorUp(Func<int, string> render)
    {
        int full = render(Wire.Cap(0)).Length;
        int floor = Enumerable.Range(1, full).First(cap => render(cap).Length <= cap);
        var over = new List<string>();
        for (int cap = floor; cap <= full + 50; cap++)
            if (render(cap) is var r && r.Length > cap) over.Add($"{r.Length} at max_chars={cap}");
        Assert.True(over.Count == 0, $"{over.Count} cap(s) answered past the cap, first: {over.FirstOrDefault()}");
        Assert.True(floor < full / 2, $"the floor {floor} is most of the full answer {full}: the lists were not what got cut");
    }

    [Fact]
    public void LoadOrderStatusNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(Status);

    [Fact]
    public void UpdateStatusNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(Updates);

    [Fact]
    public void BsaListNeverAnswersPastItsCap() => NeverOverTheCapFromItsFloorUp(Bsa);

    /// <summary>Under the floor the answer still ships, and says by how much it missed and what clears it.</summary>
    [Fact]
    public void ACapUnderTheFloorSaysSoAndNamesTheCapThatClearsIt()
    {
        var r = Status(60);
        Assert.Contains($"raise max_chars to at least {r.Length}", r);
    }

    /// <summary>At a cap that holds everything, nothing is cut and no notice is added.</summary>
    [Fact]
    public void AFullAnswerCarriesNoCutNotice()
    {
        var r = Status(Wire.Cap(0));
        Assert.DoesNotContain("omitted at max_chars", r);
        Assert.DoesNotContain("raise max_chars", r);
    }
}
