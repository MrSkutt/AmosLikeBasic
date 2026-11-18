namespace AmosLikeBasic.Tests;

// Where programs count their file paths from, run from the source code or installed
public class WorkspaceTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "amos-ws-" + Guid.NewGuid().ToString("N"));
    private readonly string _oldDir = Environment.CurrentDirectory;

    public void Dispose()
    {
        // Back to the repository, without moved folders, so later tests find their files
        Workspace.Setup(Repo.Root, Repo.Root, _temp);
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
        Directory.CreateDirectory(Path.Combine(app, "Graphics"));
        File.WriteAllText(Path.Combine(app, "Graphics", "hero.png"), "png");
        Directory.CreateDirectory(Path.Combine(app, "Sound", "Tracker"));
        File.WriteAllText(Path.Combine(app, "Sound", "Tracker", "song.amostrk"), "song");
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
        // Graphics and Sound are copied too, and an Export folder is made
        Assert.True(File.Exists(Path.Combine(Workspace.Graphics, "hero.png")));
        Assert.True(File.Exists(Path.Combine(Workspace.Sound, "Tracker", "song.amostrk")));
        Assert.True(Directory.Exists(Workspace.Export));
        // The temp folder can be reached through a link (/var -> /private/var on macOS), so compare the end
        Assert.EndsWith(Path.Combine("Documents", "AmosLikeBasic"), Environment.CurrentDirectory);
    }

    [Fact]
    public void MovedFoldersAreKeptAndUsed()
    {
        string settings = Path.Combine(_temp, "config", "folders.json");
        string art = Path.Combine(_temp, "MyArt");
        Directory.CreateDirectory(art);
        File.WriteAllText(Path.Combine(art, "ship.png"), "mine");

        Workspace.Setup(Repo.Root, Repo.Root, _temp, settings);
        Workspace.Choose(WorkspaceFolder.Graphics, art);
        Assert.Equal(Path.GetFullPath(art), Workspace.Graphics);

        // A new start reads the choice back; the default is not written down
        Workspace.Setup(Repo.Root, Repo.Root, _temp, settings);
        Assert.Equal(Path.GetFullPath(art), Workspace.Graphics);
        Assert.Equal(Path.Combine(Repo.Root, "Sound"), Workspace.Sound);

        // "Graphics/..." in a program is found in the moved folder, other files as before
        Assert.Equal(Path.Combine(Path.GetFullPath(art), "ship.png"), ResourceLoader.GetPath("Graphics/ship.png"));
        Assert.EndsWith(Path.Combine("Sound", "Tracker"), Path.GetDirectoryName(ResourceLoader.GetPath("Sound/Tracker/neon_horizon.amostrk")));

        // Back to the default
        Workspace.Choose(WorkspaceFolder.Graphics, Workspace.DefaultOf(WorkspaceFolder.Graphics));
        Workspace.Setup(Repo.Root, Repo.Root, _temp, settings);
        Assert.Equal(Path.Combine(Repo.Root, "Graphics"), Workspace.Graphics);
    }

    [Fact]
    public void YourOwnCopyOfAFileIsFoundBeforeTheAppsOwn()
    {
        // The working folder (Documents/AmosLikeBasic when installed) comes before the app's files
        string work = Path.Combine(_temp, "work");
        Directory.CreateDirectory(Path.Combine(work, "Graphics"));
        File.WriteAllText(Path.Combine(work, "Graphics", "hero.png"), "changed");
        Environment.CurrentDirectory = work;
        Assert.Equal("changed", File.ReadAllText(ResourceLoader.GetPath("Graphics/hero.png")));
    }

    [Fact]
    public void ANewVersionUpdatesTheExamplesYouHaveNotChanged()
    {
        string app = Path.Combine(_temp, "app");
        string docs = Path.Combine(_temp, "Documents");
        Directory.CreateDirectory(Path.Combine(app, "Projects"));
        File.WriteAllText(Path.Combine(app, "Projects", "kept.amosproj"), "v1");
        File.WriteAllText(Path.Combine(app, "Projects", "mine.amosproj"), "v1");
        Workspace.Setup(null, app, docs);

        // You change one of them; then a new version of the app changes both
        File.WriteAllText(Path.Combine(Workspace.Projects, "mine.amosproj"), "my change");
        File.WriteAllText(Path.Combine(app, "Projects", "kept.amosproj"), "v2");
        File.WriteAllText(Path.Combine(app, "Projects", "mine.amosproj"), "v2");
        Workspace.Setup(null, app, docs);

        Assert.Equal("v2", File.ReadAllText(Path.Combine(Workspace.Projects, "kept.amosproj")));
        Assert.Equal("my change", File.ReadAllText(Path.Combine(Workspace.Projects, "mine.amosproj")));
    }
}
