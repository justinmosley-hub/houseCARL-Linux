using HousecarlCore;
using HousecarlMcp;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>The game's log folders off Windows: the game runs under Proton, so its Documents\My Games is inside its Proton
/// prefix, which Steam keeps in the game's own library (steamapps/compatdata/489830/pfx). Auto-detect looks there, from
/// the load order's game folder. Contract in docs/linux-and-amethyst.md.</summary>
[Trait("tier", "unit")]
public sealed class ProtonLogFolderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-proton-logs-" + Guid.NewGuid().ToString("N"));
    string Library => Path.Combine(_root, "SteamLibrary");
    string GameDir => Path.Combine(Library, "steamapps", "common", "Skyrim Special Edition");
    string MyGames => Path.Combine(Library, "steamapps", "compatdata", "489830", "pfx", "drive_c", "users", "steamuser",
                                   "Documents", "My Games", "Skyrim Special Edition");

    public ProtonLogFolderTests() => Directory.CreateDirectory(Path.Combine(GameDir, "Data"));

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* temp scratch */ } }

    [Fact]
    public void TheProtonMyGamesFolderIsInTheGamesOwnLibrary()
    {
        Assert.Equal(MyGames, ToolBridge.ProtonMyGames(GameDir));
        Assert.Null(ToolBridge.ProtonMyGames(Path.Combine(_root, "Games", "Skyrim")));   // not <library>/steamapps/common/<game>
    }

    [LinuxFact("the game's Documents are inside its Proton prefix off Windows")]
    public void BothLogFoldersAreFoundInsideTheGamesProtonPrefix()
    {
        Directory.CreateDirectory(Path.Combine(MyGames, "Logs", "Script"));
        Directory.CreateDirectory(Path.Combine(MyGames, "SKSE", "Crashlogs"));

        Assert.Equal(Path.Combine(MyGames, "Logs", "Script"), ToolBridge.Probe(ToolDependency.PapyrusLogs, new[] { GameDir }));
        Assert.Equal(Path.Combine(MyGames, "SKSE", "Crashlogs"), ToolBridge.Probe(ToolDependency.CrashLogs, new[] { GameDir }));
    }

    [LinuxFact("the game's Documents are inside its Proton prefix off Windows")]
    public void StatusShowsTheLogFolderOfTheGameAmethystPointsAt()
    {
        Directory.CreateDirectory(Path.Combine(MyGames, "Logs", "Script"));
        var a = new ScratchAmethyst("hc-proton-logs-status-");
        try
        {
            a.Paths(gamePath: GameDir);
            a.Profile("", "", "");
            var store = new UserConfigStore(Path.Combine(a.Root, "u.json"));
            using var svc = LoadOrderService.WithInstance(a.GameConfig, 0, store);

            var logs = StatusWire.LogFolders(new ToolPathResolver(store), svc.GameDirOrNull() is { } g ? new[] { g } : null);

            var papyrus = Assert.Single(logs, l => l.Key == "papyrus_logs");
            Assert.Equal(Path.Combine(MyGames, "Logs", "Script"), papyrus.Path);
            Assert.Equal(ToolPathSource.AutoDetected, papyrus.Source);
        }
        finally { a.Delete(); }
    }

    [Fact]
    public void TheWhereToFindItSentenceNamesTheProtonPrefix() =>
        Assert.Contains("steamapps/compatdata/489830/pfx/drive_c/users/steamuser/Documents", ToolBridge.Info(ToolDependency.PapyrusLogs).WhereToGet);
}
