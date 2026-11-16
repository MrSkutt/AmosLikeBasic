namespace AmosLikeBasic.Tests;

// The list behind Project → Examples must match the files in Projects/
public class ExamplesTests
{
    private static readonly string[] Listed =
        Examples.Groups.SelectMany(g => g.Items).Select(e => e.File).ToArray();

    private static readonly string[] OnDisk =
        Directory.GetFiles(Repo.PathOf("Projects"), "*.amosproj")
            .Select(Path.GetFileNameWithoutExtension).Select(f => f!).ToArray();

    [Fact]
    public void Every_listed_example_exists() =>
        Assert.Empty(Listed.Except(OnDisk));

    [Fact]
    public void Every_project_is_listed() =>
        Assert.Empty(OnDisk.Except(Listed));

    [Fact]
    public void No_example_is_listed_twice() =>
        Assert.Equal(Listed.Length, Listed.Distinct().Count());

    [Fact]
    public void Every_example_has_a_title_and_a_description()
    {
        foreach (var e in Examples.Groups.SelectMany(g => g.Items))
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title), e.File);
            Assert.False(string.IsNullOrWhiteSpace(e.Description), e.File);
        }
    }
}
