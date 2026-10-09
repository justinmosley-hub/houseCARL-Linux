using Xunit;

namespace HousecarlMcpTests;

/// <summary>A fact that reports as skipped, not passed, off Linux; <paramref name="because"/> says what it needs from Linux.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute(string because = "")
    {
        if (!OperatingSystem.IsLinux()) Skip = because.Length == 0 ? "Linux only." : $"Linux only: {because}.";
    }
}
