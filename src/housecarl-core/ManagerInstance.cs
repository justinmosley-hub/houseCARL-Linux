namespace HousecarlCore;

// One mod manager houseCARL reads a load order from — MO2 (Mo2Instance) or Amethyst (AmethystInstance) — chosen from
// what the configured folder holds; contract in docs/architecture/amethyst-instance.md.

/// <summary>The four load-order roots a mod manager resolves to, plus the active profile name, the game root and the
/// configured location; feed them straight to <see cref="Mo2LoadOrder.Build"/>, whose profile files both managers write.</summary>
public sealed record ManagerInstancePaths(
    string InstanceDir, string ProfileName, string ProfileDir, string ModsDir, string DataDir, string GamePath, string OverwriteDir);

/// <summary>A configured mod manager: resolves its roots on demand and fingerprints what could move them.</summary>
public interface IManagerInstance
{
    /// <summary>"Mod Organizer 2" or "Amethyst", for messages and status.</summary>
    string Manager { get; }

    /// <summary>What a caller must know about this manager that houseCARL's MO2-worded messages do not say; null for MO2.</summary>
    string? WorkflowNote { get; }

    /// <summary>The folder this instance was configured with.</summary>
    string Location { get; }

    /// <summary>True when the profile's loadorder.txt can leave out plugins the game loads, so the order is completed
    /// from Data and plugins.txt (<see cref="Mo2LoadOrder.ReadComposition"/>): Amethyst's lists only what its plugin
    /// panel last saved; MO2's lists everything.</summary>
    bool PartialLoadOrder { get; }

    /// <summary>The mods folder of the profile at <paramref name="profileDir"/>, for reading a profile other than the
    /// active one: MO2's profiles share <paramref name="activeModsDir"/>; an Amethyst profile can have its own.</summary>
    string ModsDirFor(string profileDir, string activeModsDir);

    /// <summary>Derive the roots, or THROW naming every missing piece; never half-derived paths.</summary>
    ManagerInstancePaths Resolve();

    /// <summary>The non-throwing derive for the freshness re-check: false keeps the last good set.</summary>
    bool TryResolve(out ManagerInstancePaths? paths);

    /// <summary>A cheap value that changes whenever anything this manager resolves from may have changed — the active
    /// profile, the game or mods folder, a deploy. Equal fingerprints mean nothing to re-derive.</summary>
    string Fingerprint();
}

public static class ManagerInstance
{
    /// <summary>The manager the folder belongs to: an MO2 instance (ModOrganizer.ini), else an Amethyst game's config
    /// folder (paths.json) or staging folder, else MO2 — whose resolve then names what is missing, Amethyst included.
    /// Never throws, so a bad configured folder surfaces on the first call, as it always has.</summary>
    public static IManagerInstance Open(string location)
    {
        var dir = Path.TrimEndingDirectorySeparator(location.Trim());
        if (File.Exists(Mo2Instance.IniPath(dir))) return new Mo2ManagerInstance(dir);
        return AmethystInstance.GameConfigFor(dir) is { } gameConfig ? new AmethystManagerInstance(gameConfig) : new Mo2ManagerInstance(dir);
    }
}
