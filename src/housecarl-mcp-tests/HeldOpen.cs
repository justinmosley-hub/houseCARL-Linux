namespace HousecarlMcpTests;

/// <summary>Holds one file open with <see cref="FileShare.None"/> for the lifetime of the object — MO2 or xEdit
/// sitting on a plugin. Details: <c>docs/architecture/test-project-fixtures.md</c>.</summary>
public sealed class HeldOpen : IDisposable
{
    readonly FileStream _stream;

    HeldOpen(FileStream stream) => _stream = stream;

    /// <summary>Take an exclusive hold on <paramref name="path"/>. Throws — naming the path and the reason —
    /// if the hold cannot be taken, so no caller can proceed lockless past a swallowed failure.</summary>
    public static HeldOpen Hold(string path)
    {
        try
        {
            return new HeldOpen(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"could not take an exclusive hold on '{path}' to simulate a locked file — " +
                $"{ex.GetType().Name}: {ex.Message}. Nothing was locked, so a test continuing past this " +
                "point would assert against an ordinary readable file.", ex);
        }
    }

    public void Dispose()
    {
        if (_folder is not null) File.SetUnixFileMode(_folder, _folderMode);
        _stream.Dispose();
    }

    string? _folder;
    UnixFileMode _folderMode;

    /// <summary>Hold <paramref name="path"/> so that REPLACING or deleting it fails, as another program holding it does on
    /// Windows. Elsewhere an open file can still be replaced or deleted, so its folder is also made unwritable until
    /// disposed — which also refuses a new file beside it (docs/linux-and-amethyst.md).</summary>
    public static HeldOpen HoldAgainstReplace(string path, FileShare share = FileShare.None, FileAccess access = FileAccess.Read)
    {
        var held = new HeldOpen(new FileStream(path, FileMode.Open, access, share));
        if (!OperatingSystem.IsWindows())
        {
            held._folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
            held._folderMode = File.GetUnixFileMode(held._folder);
            File.SetUnixFileMode(held._folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        return held;
    }
}
