using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>Running the Creation Kit's compiler and BSArch off Windows (<see cref="WindowsTool"/>): through Wine, with each
/// path handed over on Wine's Z: drive, and the compiler's Windows-path diagnostics read back by file name. A recording
/// stand-in plays Wine, so no real Wine runs. Contract in docs/linux-and-amethyst.md.</summary>
[Trait("tier", "unit")]
public sealed class WindowsToolTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-windows-tool-" + Guid.NewGuid().ToString("N"));
    string Log => Path.Combine(_root, "wine-args.txt");

    public WindowsToolTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* temp scratch */ } }

    /// <summary>A Wine that writes each argument it gets on its own line to <see cref="Log"/>, then runs
    /// <paramref name="body"/>: as the Papyrus compiler, it writes the .pex named by -o=, or, for a script named HCBad,
    /// prints a diagnostic on a Windows path.</summary>
    string RecordingWine(string body = "")
    {
        var wine = Path.Combine(_root, "wine");
        File.WriteAllText(wine, "#!/bin/bash\nprintf '%s\\n' \"$@\" > '" + Log + "'\n" + body);
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return wine;
    }

    const string AsTheCompiler =
        "obj=\"$2\"\n" +
        "for a in \"$@\"; do case \"$a\" in -o=*) out=\"${a#-o=Z:}\";; esac; done\n" +
        "out=\"${out//\\\\//}\"\n" +
        "if [ \"$obj\" = HCBad ]; then echo 'Z:\\work\\HCBad.psc(4,5): no viable alternative at input' >&2; exit 0; fi\n" +
        "printf 'pex' > \"$out/$obj.pex\"\n";

    [LinuxFact("paths are handed to Windows programs on Wine's Z: drive off Windows")]
    public void APathIsHandedOverOnWinesZDrive() =>
        Assert.Equal(@"Z:\home\me\My Mods\x.psc", WindowsTool.PathArg("/home/me/My Mods/x.psc"));

    [LinuxFact("Windows programs run through Wine off Windows")]
    public void TheProgramRunsThroughWineInItsPrefixWithWinesOwnLogOff()
    {
        var psi = WindowsTool.StartInfo("/ck/PapyrusCompiler.exe", wine: "/opt/proton/bin/wine", prefix: "/home/me/.local/share/hc-prefix");

        Assert.Equal("/opt/proton/bin/wine", psi.FileName);
        Assert.Equal("/ck/PapyrusCompiler.exe", psi.ArgumentList[0]);
        Assert.Equal("/home/me/.local/share/hc-prefix", psi.Environment["WINEPREFIX"]);
        Assert.Equal("-all", psi.Environment["WINEDEBUG"]);
        Assert.Equal("/ck", psi.WorkingDirectory);
    }

    [LinuxFact("Windows programs run through Wine off Windows")]
    public void TheCompilerIsHandedWindowsPathsAndItsPexIsFoundOnLinux()
    {
        var src = Path.Combine(_root, "My Scripts");
        var vanilla = Path.Combine(_root, "Source", "Scripts");
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        var compiler = Path.Combine(_root, "Papyrus Compiler", "PapyrusCompiler.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(compiler)!);   // the compiler runs in its own folder

        var r = PapyrusCompile.CompileObject(compiler, "HCGood", new[] { src, vanilla }, output, wine: RecordingWine(AsTheCompiler));

        Assert.True(r.Success, r.RunError ?? r.Stderr);
        Assert.Equal(Path.Combine(output, "HCGood.pex"), r.PexPath);
        Assert.Equal(new[]
        {
            compiler, "HCGood", "-f=TESV_Papyrus_Flags.flg",
            "-i=" + WindowsTool.PathArg(src) + ";" + WindowsTool.PathArg(vanilla),
            "-o=" + WindowsTool.PathArg(output),
        }, File.ReadAllLines(Log));
    }

    [LinuxFact("Windows programs run through Wine off Windows")]
    public void ACompilerDiagnosticOnAWindowsPathIsReadByItsFileName()
    {
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);

        var r = PapyrusCompile.CompileObject(Path.Combine(_root, "PapyrusCompiler.exe"), "HCBad", new[] { _root }, output,
                                             wine: RecordingWine(AsTheCompiler));

        Assert.False(r.Success);
        Assert.Equal("HCBad.psc(4,5): no viable alternative at input", Assert.Single(r.Diagnostics).ToString());
    }

    [LinuxFact("Windows programs run through Wine off Windows")]
    public void BsArchIsHandedWindowsPaths()
    {
        var src = Path.Combine(_root, "pack me");
        Directory.CreateDirectory(src);
        var archive = Path.Combine(_root, "Out.bsa");

        BsaArchive.Pack(Path.Combine(_root, "BSArch.exe"), src, archive, "-sse", compress: false, timeoutMs: 30_000, wine: RecordingWine());

        var args = File.ReadAllLines(Log);
        Assert.Equal(new[] { "pack", WindowsTool.PathArg(src), WindowsTool.PathArg(Path.Combine(_root, "Out.houseCARL-tmp.bsa")), "-sse", "-mt" }, args[1..]);
    }

    [LinuxFact("Windows programs run through Wine off Windows")]
    public void AMissingWineIsNamedWithTheWayOut()
    {
        var r = PapyrusCompile.CompileObject(Path.Combine(_root, "PapyrusCompiler.exe"), "HCGood", new[] { _root }, _root,
                                             wine: Path.Combine(_root, "no-such-wine"));

        Assert.False(r.Ran);
        Assert.Contains($"through Wine ('{Path.Combine(_root, "no-such-wine")}')", r.RunError);
        Assert.Contains("HOUSECARL_WINE", r.RunError);
    }

    [LinuxFact("the test run's stand-in for Wine is installed off Windows")]
    public void TheTestRunNeverStartsTheRealWine() =>
        Assert.Equal(NoRealWine.PassThrough, WindowsTool.Wine);
}
