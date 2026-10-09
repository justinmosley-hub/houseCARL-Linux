using System.Runtime.CompilerServices;

namespace HousecarlMcpTests;

/// <summary>Tests never start the real Wine or touch a real prefix. Off Windows, houseCARL runs a Windows program through
/// Wine (<see cref="HousecarlCore.WindowsTool"/>), so before any test runs this points <c>HOUSECARL_WINE</c> at a stand-in
/// that runs the program it is handed directly, and <c>HOUSECARL_WINEPREFIX</c> into this run's temp folder: a test's
/// script standing in for BSArch or the compiler behaves as it did, and nothing is made under the user's home. A test
/// that means the real Wine passes it explicitly.</summary>
static class NoRealWine
{
    /// <summary>The stand-in, or null on Windows.</summary>
    internal static string? PassThrough { get; private set; }

    [ModuleInitializer]
    internal static void Install()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "hc-no-real-wine-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        PassThrough = Path.Combine(dir, "wine");
        File.WriteAllText(PassThrough, "#!/bin/sh\nexe=\"$1\"; shift\nexec \"$exe\" \"$@\"\n");
        File.SetUnixFileMode(PassThrough, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("HOUSECARL_WINE", PassThrough);
        Environment.SetEnvironmentVariable("HOUSECARL_WINEPREFIX", Path.Combine(dir, "wineprefix"));
    }
}
