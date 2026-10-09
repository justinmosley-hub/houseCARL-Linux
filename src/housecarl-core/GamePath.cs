namespace HousecarlCore;

/// <summary>Name helpers for a game-relative path (<c>meshes\armor\cuirass.nif</c>), which splits on <c>\</c> on
/// every OS — <see cref="Path.GetFileName(string)"/> only does on Windows; contract in docs/architecture/linux-paths.md.</summary>
public static class GamePath
{
    static readonly char[] Separators = { '\\', '/' };

    /// <summary>The last segment: <c>cuirass.nif</c>.</summary>
    public static string FileName(string relPath)
    {
        int cut = relPath.LastIndexOfAny(Separators);
        return cut < 0 ? relPath : relPath[(cut + 1)..];
    }

    /// <summary>The last segment without its extension: <c>cuirass</c>.</summary>
    public static string FileNameWithoutExtension(string relPath) => Path.GetFileNameWithoutExtension(FileName(relPath));

    /// <summary>Does the path start with a drive letter (<c>C:</c>)? A game path never does, on any OS —
    /// <see cref="Path.IsPathRooted(string)"/> only knows drive letters on Windows.</summary>
    public static bool HasDriveLetter(string path) =>
        path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]);

    /// <summary>Everything before the last segment, separators as given: <c>meshes\armor</c>; empty at the top.</summary>
    public static string DirectoryName(string relPath)
    {
        int cut = relPath.LastIndexOfAny(Separators);
        return cut < 0 ? "" : relPath[..cut];
    }
}
