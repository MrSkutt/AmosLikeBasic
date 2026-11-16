using Avalonia;
using Avalonia.Headless;
using AmosLikeBasic;
using AmosLikeBasic.Tests;

// The synth, the interpreter and the graphics share static state, so the tests run one at a time
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace AmosLikeBasic.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public static class Repo
{
    // The folder with AmosLikeBasic.csproj, found by walking up from the test binaries
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AmosLikeBasic.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Cannot find the repository root");
    }

    public static string PathOf(string relative) => Path.Combine(Root, relative);
}
