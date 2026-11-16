namespace AmosLikeBasic.Tests;

// Where programs count their file paths from, run from the source code or installed
public class WorkspaceTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "amos-ws-" + Guid.NewGuid().ToString("N"));
    private readonly string _oldDir = Environment.CurrentDirectory;

    public void Dispose()
    {
        Environment.CurrentDirectory = _oldDir;
        try { Directory.Delete(_temp, true); } catch { /* best effort */ }
    }

    [Fact]
    public void FromTheSourceCodeTheRepositoryIsTheWorkspace()
    {
        Workspace.Setup(Repo.Root, Repo.Root, _temp);
        Assert.False(Workspace.Installed);
        Assert.Equal(Repo.Root, Workspace.Root);
        Assert.False(Directory.Exists(Path.Combine(_temp, "AmosLikeBasic")));
    }

    [Fact]
    public void InstalledTheExamplesAreCopiedToDocuments()
    {
        string app = Path.Combine(_temp, "app");
        Directory.CreateDirectory(Path.Combine(app, "Projects"));
        File.WriteAllText(Path.Combine(app, "Projects", "game.amosproj"), "new");
        File.WriteAllText(Path.Combine(app, "Projects", "other.amosproj"), "new");

        // The user has already changed one of the examples
        string docs = Path.Combine(_temp, "Documents");
        Directory.CreateDirectory(Path.Combine(docs, "AmosLikeBasic", "Projects"));
        File.WriteAllText(Path.Combine(docs, "AmosLikeBasic", "Projects", "game.amosproj"), "mine");

        Workspace.Setup(null, app, docs);

        Assert.True(Workspace.Installed);
        Assert.Equal(Path.Combine(docs, "AmosLikeBasic"), Workspace.Root);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Workspace.Projects, "game.amosproj")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Workspace.Projects, "other.amosproj")));
        // The temp folder can be reached through a link (/var -> /private/var on macOS), so compare the end
        Assert.EndsWith(Path.Combine("Documents", "AmosLikeBasic"), Environment.CurrentDirectory);
    }
}
