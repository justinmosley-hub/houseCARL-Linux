using Xunit;

namespace HousecarlMcpTests;

/// <summary>A fact that reports as skipped, not passed, off Windows; <paramref name="because"/> says what Linux lacks.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string because = "")
    {
        if (!OperatingSystem.IsWindows()) Skip = because.Length == 0 ? "Windows only." : $"Windows only: {because}.";
    }
}
