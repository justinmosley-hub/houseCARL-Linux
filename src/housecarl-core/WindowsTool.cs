using System.Diagnostics;

namespace HousecarlCore;

/// <summary>Starting a Windows program houseCARL drives (the Creation Kit's PapyrusCompiler.exe, BSArch.exe): directly on
/// Windows, through Wine elsewhere, with each path it is handed in the form it reads. Contract in docs/linux-and-amethyst.md.</summary>
public static class WindowsTool
{
    /// <summary>The Wine that runs them off Windows: <c>HOUSECARL_WINE</c> if set (Proton's <c>files/bin/wine</c>, say),
    /// else <c>wine</c> on PATH.</summary>
    public static string Wine => Environment.GetEnvironmentVariable("HOUSECARL_WINE") is { Length: > 0 } set ? set : "wine";

    /// <summary>The Wine prefix they run in: <c>HOUSECARL_WINEPREFIX</c> if set, else Wine's own choice
    /// (<c>WINEPREFIX</c>, or <c>~/.wine</c>).</summary>
    public static string? Prefix => Environment.GetEnvironmentVariable("HOUSECARL_WINEPREFIX") is { Length: > 0 } set ? set : null;

    /// <summary>A start for <paramref name="exe"/> with output captured: the exe itself on Windows; elsewhere
    /// <paramref name="wine"/> (default <see cref="Wine"/>) with the exe as its first argument, in <paramref name="prefix"/>
    /// (default <see cref="Prefix"/>), and Wine's own log lines kept off the program's stderr.</summary>
    public static ProcessStartInfo StartInfo(string exe, string? wine = null, string? prefix = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? exe : wine ?? Wine,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows()) return psi;
        psi.ArgumentList.Add(exe);
        if ((prefix ?? Prefix) is { } p) psi.Environment["WINEPREFIX"] = p;
        if (!psi.Environment.ContainsKey("WINEDEBUG")) psi.Environment["WINEDEBUG"] = "-all";
        return psi;
    }

    /// <summary>A path as the program reads it: unchanged on Windows; elsewhere on Wine's <c>Z:</c> drive, which a prefix
    /// maps to <c>/</c> (<c>/home/me/x</c> is <c>Z:\home\me\x</c>).</summary>
    public static string PathArg(string path) =>
        OperatingSystem.IsWindows() ? path : "Z:" + Path.GetFullPath(path).Replace('/', '\\');

    /// <summary>What a failed start of <paramref name="psi"/> says: the program on Windows; elsewhere the Wine it was
    /// started through too, and how to point houseCARL at another.</summary>
    public static string CouldNotStart(string what, string exe, ProcessStartInfo psi, Exception ex) => OperatingSystem.IsWindows()
        ? $"could not run {what} at '{exe}': {ex.Message}"
        : $"could not run {what} at '{exe}' through Wine ('{psi.FileName}'): {ex.Message} — install Wine, or set HOUSECARL_WINE " +
          "to another Wine (Proton's files/bin/wine works)";
}
