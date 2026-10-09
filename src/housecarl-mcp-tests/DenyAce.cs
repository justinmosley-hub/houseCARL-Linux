using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HousecarlMcpTests;

/// <summary>The deny machinery the unreadable-root worlds share: deny the current account one directory, verify the
/// deny actually bit on this host rather than trusting the call, and take it off again. One copy, so a host where a
/// deny stops biting is a single fix; the worlds that use it stage their own content.
/// On Windows a deny is an ACE; elsewhere it is the directory's mode bits: no permissions for everything, write and
/// execute without read for listing only, which is what a ListDirectory deny leaves (docs/linux-and-amethyst.md).
/// As root the mode bits do not bind, so the verify fails and the world says so instead of passing.</summary>
static class DenyAce
{
    static readonly ConcurrentDictionary<string, UnixFileMode> SavedModes = new(StringComparer.Ordinal);

    /// <summary>Deny the current user everything on one directory. False means the deny did not bite on this host, and
    /// it has already been taken back off — a fixture that did not build is a failure, never a quiet pass.</summary>
    public static bool TryDeny(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var di = new DirectoryInfo(dir);
                var sec = di.GetAccessControl();
                sec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Deny));
                di.SetAccessControl(sec);
            }
            else SetMode(dir, UnixFileMode.None);
            try { Directory.EnumerateFiles(dir, "*").ToList(); } catch (UnauthorizedAccessException) { return true; } catch { }
            Undeny(dir);
            return false;
        }
        catch { return false; }
    }

    /// <summary>Take <see cref="TryDeny"/>'s deny off again — left behind, it would block the temp tree's own cleanup.</summary>
    public static void Undeny(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var di = new DirectoryInfo(dir);
                var sec = di.GetAccessControl();
                sec.RemoveAccessRuleAll(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.FullControl, AccessControlType.Deny));
                di.SetAccessControl(sec);
            }
            else RestoreMode(dir);
        }
        catch { /* cleanup is best effort; the temp tree goes either way */ }
    }

    /// <summary>Deny only LISTING one directory, not inherited, so a path under it still opens by name while the
    /// directory's own enumeration fails. False means the deny did not bite and was taken back off.</summary>
    public static bool TryDenyListing(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var di = new DirectoryInfo(dir);
                var sec = di.GetAccessControl();
                sec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.ListDirectory,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
                di.SetAccessControl(sec);
            }
            else SetMode(dir, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (!Directory.Exists(dir)) { UndenyListing(dir); return false; }
            try { Directory.EnumerateFiles(dir).ToList(); }
            catch (UnauthorizedAccessException) { return true; }
            catch (IOException) { return true; }
            UndenyListing(dir);
            return false;
        }
        catch { return false; }
    }

    /// <summary>Lift <see cref="TryDenyListing"/>'s deny. Throws on failure, so a caller that must not leave a denied
    /// folder behind can say so.</summary>
    public static void UndenyListing(string dir)
    {
        if (OperatingSystem.IsWindows())
        {
            var di = new DirectoryInfo(dir);
            var sec = di.GetAccessControl();
            sec.RemoveAccessRuleAll(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.ListDirectory,
                AccessControlType.Deny));
            di.SetAccessControl(sec);
        }
        else RestoreMode(dir);
    }

    static void SetMode(string dir, UnixFileMode mode)
    {
        SavedModes.TryAdd(dir, File.GetUnixFileMode(dir));
        File.SetUnixFileMode(dir, mode);
    }

    static void RestoreMode(string dir)
    {
        if (SavedModes.TryRemove(dir, out var mode)) File.SetUnixFileMode(dir, mode);
    }
}
