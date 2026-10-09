namespace HousecarlMcpTests;

/// <summary>Where the repo is, and which build configuration this run belongs to — both derived from the running
/// assembly's own location. Every failure throws rather than falling back, so a test cannot measure the wrong
/// tree.</summary>
static class HarnessPaths
{
    /// <summary>The repo root — the directory holding housecarl.sln, found by walking up from this assembly.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Release or Debug — read off this assembly's own bin/&lt;config&gt;/net9.0 path.</summary>
    public static string Configuration { get; } = FindConfiguration();

    /// <summary>The built server beside these tests. The apphost is <c>housecarl-mcp.exe</c> on Windows and
    /// <c>housecarl-mcp</c> with no extension on Linux (see docs/linux-and-amethyst.md).</summary>
    public static string ServerExe { get; } = Path.Combine(RepoRoot, "src", "housecarl-mcp", "bin", Configuration,
                                                           "net9.0", OperatingSystem.IsWindows() ? "housecarl-mcp.exe" : "housecarl-mcp");

    /// <summary>A Windows absolute path written in a test (<c>E:\mo2\mods</c>), as an absolute path on this OS: unchanged
    /// on Windows, <c>/E/mo2/mods</c> elsewhere. For tests that need a fully qualified path, not a real file.</summary>
    public static string Rooted(string windowsPath) =>
        OperatingSystem.IsWindows() ? windowsPath : "/" + windowsPath.Replace(":", "").Replace('\\', '/').TrimEnd('/');

    static string FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "housecarl.sln"))) return d.FullName;

        throw new InvalidOperationException(
            $"No housecarl.sln above '{AppContext.BaseDirectory}'. The harness tests read the SOURCE tree " +
            "(source files, the built server), so a test run that cannot find the repo root " +
            "must fail loud rather than measure nothing.");
    }

    static string FindConfiguration()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d?.Parent != null; d = d.Parent)
            if (string.Equals(d.Parent.Name, "bin", StringComparison.OrdinalIgnoreCase)) return d.Name;

        throw new InvalidOperationException(
            $"Could not read a build configuration out of '{AppContext.BaseDirectory}' (expected " +
            "…/bin/<Configuration>/net9.0/). The stdio tests need it to find the server built alongside " +
            "these tests — guessing 'Release' would run a stale or absent binary.");
    }
}
