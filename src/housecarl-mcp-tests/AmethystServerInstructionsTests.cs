using Xunit;

namespace HousecarlMcpTests;

/// <summary>The standing instructions a server started on an Amethyst game publishes: Amethyst's words where MO2's
/// name it, inside Claude Code's cut. The default server's are pinned unchanged by <see cref="ServerInstructionsTests"/>.
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
}
