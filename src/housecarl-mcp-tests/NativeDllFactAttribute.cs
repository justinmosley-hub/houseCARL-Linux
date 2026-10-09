using Xunit;

namespace HousecarlMcpTests;

/// <summary>A fact that reads a real native Windows DLL: the system kernel32.dll on Windows, Wine's elsewhere (a
/// genuine PE32+ build), or <c>HOUSECARL_NATIVE_DLL</c> if set. Without one it reports as skipped, not passed.</summary>
public sealed class NativeDllFactAttribute : FactAttribute
{
    static readonly string[] WineKernel32 =
    {
        "/usr/lib/wine/x86_64-windows/kernel32.dll",
        "/usr/lib64/wine/x86_64-windows/kernel32.dll",
        "/usr/lib/x86_64-linux-gnu/wine/x86_64-windows/kernel32.dll",
    };

    /// <summary>The DLL these tests read, or null when this machine has none.</summary>
    public static string? Path { get; } = Find();

    static string? Find()
    {
        if (Environment.GetEnvironmentVariable("HOUSECARL_NATIVE_DLL") is { Length: > 0 } set) return set;
        if (OperatingSystem.IsWindows())
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        return WineKernel32.FirstOrDefault(File.Exists);
    }

    public NativeDllFactAttribute()
    {
        if (Path is null || !File.Exists(Path))
            Skip = "No native Windows DLL to read: install Wine (its kernel32.dll is a real PE), or set HOUSECARL_NATIVE_DLL.";
    }
}
