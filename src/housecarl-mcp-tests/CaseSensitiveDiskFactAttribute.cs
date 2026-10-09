using Xunit;

namespace HousecarlMcpTests;

/// <summary>A fact that needs a file system able to hold <c>Meshes</c> beside <c>meshes</c>; it reports as skipped,
/// not passed, on Windows, where NTFS cannot.</summary>
public sealed class CaseSensitiveDiskFactAttribute : FactAttribute
{
    public CaseSensitiveDiskFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Needs a case-sensitive file system; NTFS cannot hold two names that differ only in case.";
    }
}
