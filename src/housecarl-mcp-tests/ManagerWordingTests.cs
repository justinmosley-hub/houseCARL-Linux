using HousecarlMcp;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>The Amethyst wording table (<see cref="ManagerWording"/>): each MO2 phrase it swaps is still the server's own
/// words, and MO2's text passes through untouched. Contract in docs/architecture/amethyst-instance.md.</summary>
public sealed class ManagerWordingTests
{
    static readonly string Source = string.Concat(
        Directory.EnumerateFiles(Path.Combine(HarnessPaths.RepoRoot, "src", "housecarl-mcp"), "*.cs").Select(File.ReadAllText));

    [Fact]
    public void EveryMo2PhraseIsVerbatimInTheServersSource()
    {
        var gone = ManagerWording.AmethystPhrases.Select(p => p.Mo2).Where(m => !Source.Contains(m, StringComparison.Ordinal)).ToList();
        Assert.True(gone.Count == 0, "reworded upstream, so no longer swapped: " + string.Join(" | ", gone));
    }

    [Fact]
    public void Mo2TextPassesThroughUntouched()
    {
        var all = string.Join("\n", ManagerWording.AmethystPhrases.Select(p => p.Mo2));
        Assert.Equal(all, ManagerWording.For("Mod Organizer 2", all));
        Assert.Equal(all, ManagerWording.For(null, all));
    }

    [Fact]
    public void AnAmethystAnswerLosesEveryPhraseInBothSpellings()
    {
        var all = string.Join("\n", ManagerWording.AmethystPhrases.Select(p => p.Mo2));
        var json = all.Replace("'", "\\u0027");

        Assert.DoesNotContain("MO2", ManagerWording.For("Amethyst", all));
        Assert.DoesNotContain("MO2", ManagerWording.For("Amethyst", json));
    }
}
