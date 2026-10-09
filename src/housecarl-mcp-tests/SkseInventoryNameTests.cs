using Xunit;

namespace HousecarlMcpTests;

/// <summary>The SKSE inventory lists each config by its file name, taken off the game path on every OS
/// (docs/architecture/linux-paths.md). Uses BlockedModWorld for its readable mod only; no blocked root is needed.</summary>
[Trait("tier", "integration")]
public sealed class SkseInventoryNameTests : IDisposable
{
    readonly BlockedModWorld _w = new();

    public void Dispose() => _w.Dispose();

    [Fact]
    public void AConfigIsListedByItsFileNameBesideItsGamePath()
    {
        var config = Assert.Single(_w.Svc.SkseInventory().Configs,
            c => c.RelPath.Equals(@"SKSE\Plugins\SkyPatcher\npc\readable.ini", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("readable.ini", config.FileName);
    }
}
