using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

/// <summary><see cref="DiskPath.Combine"/> turns a game-relative path, written the Windows way, into the real path on
/// disk. On Windows that is plain <see cref="Path.Combine(string, string)"/>; on a case-sensitive file system each
/// folder takes the casing it has on disk (docs/architecture/linux-paths.md).</summary>
[Trait("tier", "integration")]
public sealed class DiskPathTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-diskpath-" + Guid.NewGuid().ToString("N"));

    public DiskPathTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "meshes", "armor"));
        File.WriteAllText(Path.Combine(_root, "meshes", "armor", "cuirass.nif"), "");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { /* temp cleanup best-effort */ } }

    [Fact]
    public void ABackslashPathWithOtherCasingFindsTheFileOnDisk()
    {
        var path = DiskPath.Combine(_root, @"Meshes\Armor\Cuirass.NIF");

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AForwardSlashPathFindsTheFileOnDisk()
    {
        Assert.True(File.Exists(DiskPath.Combine(_root, "MESHES/armor/cuirass.nif")));
    }

    [Fact]
    public void AMissingTailKeepsTheGivenSpellingUnderTheFoldersThatExist()
    {
        var path = DiskPath.Combine(_root, @"Meshes\Armor\New\Boots.nif");

        var expected = OperatingSystem.IsWindows()
            ? Path.Combine(_root, @"Meshes\Armor\New\Boots.nif")
            : Path.Combine(_root, "meshes", "armor", "New", "Boots.nif");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void ARootedSecondPathReplacesTheRootLikePathCombine()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "somewhere");

        Assert.Equal(Path.Combine(_root, elsewhere), DiskPath.Combine(_root, elsewhere));
    }

    [CaseSensitiveDiskFact]
    public void ACaseOnlyTwinPrefersTheExactSpelling()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Meshes"));

        Assert.Equal(Path.Combine(_root, "Meshes"), DiskPath.Combine(_root, "Meshes"));
        Assert.Equal(Path.Combine(_root, "meshes"), DiskPath.Combine(_root, "meshes"));
    }

    [Fact]
    public void ALeadingSlashGamePathIsNotADiskPathButARealAbsolutePathIs()
    {
        Assert.False(PathArguments.NamesDiskPath("meshes/armor/x.nif"));
        Assert.True(PathArguments.NamesDiskPath(Path.Combine(Path.GetTempPath(), "x.nif")));
        if (!OperatingSystem.IsWindows()) Assert.False(PathArguments.NamesDiskPath("/meshes/armor/x.nif"));
    }
}
