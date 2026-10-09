namespace HousecarlCore;

/// <summary>The one absolute-path rule for every path a CALLER names; contract in docs/architecture/output-and-artifacts.md.</summary>
public static class PathArguments
{
    /// <summary>Does a caller's path name a file ON DISK rather than a Data-relative game path? Fully qualified, as on
    /// Windows; on Linux a leading '/' is also how a game path is often written ('/meshes/x.nif'), so there it names
    /// disk only when its first folder is really at the filesystem root (docs/architecture/linux-paths.md).</summary>
    public static bool NamesDiskPath(string given)
    {
        if (!Path.IsPathFullyQualified(given)) return false;
        if (OperatingSystem.IsWindows()) return true;
        var first = given.TrimStart('/').Split('/', 2)[0];
        return first.Length > 0 && Directory.Exists("/" + first);
    }

    /// <summary>The refusal for a path that is not FULLY QUALIFIED, or null when it is fine, worded from the calling lane's own label, target and example.</summary>
    public static string? NotAbsolute(string given, string label, string what, string example)
        => Path.IsPathFullyQualified(given)
            ? null
            : $"{label} '{given}' is not an absolute path — pass the full path to {what} (e.g. '{example}'), " +
              "because the server resolves anything else against its OWN working directory, not yours.";
}
