namespace HousecarlCore;

/// <summary>Is an IO failure another process holding the file — the transient to retry — rather than a real fault?
/// Windows says ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33); .NET elsewhere emulates the same sharing
/// between .NET processes with advisory locks and says EWOULDBLOCK (11 on Linux, 35 on macOS). See
/// docs/linux-and-amethyst.md: on Linux only a .NET process's hold is seen, and a held file can still be deleted.</summary>
public static class FileInUse
{
    public static bool Is(IOException ex) =>
        ex is not (FileNotFoundException or DirectoryNotFoundException)
        && (OperatingSystem.IsWindows() ? (ex.HResult & 0xFFFF) is 32 or 33 : ex.HResult is 11 or 35);
}
