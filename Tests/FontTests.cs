using SkiaSharp;
using Xunit.Abstractions;

namespace AmosLikeBasic.Tests;

// The retro fonts come with the app, so FONT SET works without installing them
public class FontTests(ITestOutputHelper output)
{
    [Fact]
    public void BundledFontsHaveFamilyNames()
    {
        foreach (var file in Directory.GetFiles(Repo.PathOf("fonts"), "*.ttf"))
        {
            using var tf = SKTypeface.FromFile(file);
            Assert.NotNull(tf);
            output.WriteLine($"{Path.GetFileName(file)}: \"{tf.FamilyName}\"");
            Assert.False(string.IsNullOrWhiteSpace(tf.FamilyName));
        }
    }

    [Theory]
    [InlineData("C64 PRO MONO")]
    [InlineData("c64 pro mono")]
    [InlineData("Topaz")]
    [InlineData("Topaz A500")]
    [InlineData("TopazPlus")]
    [InlineData("Atari ST")]
    public void FontSetNamesFindTheBundledFont(string name)
    {
        Assert.NotNull(AmosGraphics.BundledFont(name));
    }
}
