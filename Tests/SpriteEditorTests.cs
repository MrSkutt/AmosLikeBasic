using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

namespace AmosLikeBasic.Tests;

// The sprite editor's list of the pictures a program loads, and its guess of a sheet's frames
public class SpriteEditorTests
{
    [Fact]
    public void Finds_the_pictures_a_program_loads()
    {
        var assets = ProgramAssets.Find("""
            SPRITE LOAD PLAYER,"Graphics/ForestHop/pinecone.png",32,32,6
            IMAGE LOAD 2, "Graphics/ForestHop/beetle.png", 32, 32, 3   ; beetles
            TILE LOAD "Graphics/ForestHop/tiles.png",32,32
            SPRITE LOAD 1,"Graphics/Anim/Anim_Beast_W32_H56_B11.png"
            SPRITE LOAD 3, F$, W, H
            ; SPRITE LOAD 4,"Graphics/hero.png"
            SPRITE LOAD 5,"Graphics/hero.png", W, H
            MAP LOAD "Graphics/test.amosmap"
            """);

        Assert.Equal(6, assets.Count);
        Assert.Equal(("MAP", "Graphics/test.amosmap"), (assets[5].Kind, assets[5].File));
        Assert.Equal(("SPRITE", "PLAYER", 32, 32, 6), (assets[0].Kind, assets[0].Number, assets[0].FrameWidth, assets[0].FrameHeight, assets[0].Frames));
        Assert.Equal((32, 32, 3, 2), (assets[1].FrameWidth, assets[1].FrameHeight, assets[1].Frames, assets[1].Line));
        Assert.Equal(("TILE", "", 32, (int?)null), (assets[2].Kind, assets[2].Number, assets[2].FrameWidth, assets[2].Frames));
        // Sizes from the file name convention when the program gives none
        Assert.Equal((32, 56, 11), (assets[3].FrameWidth, assets[3].FrameHeight, assets[3].Frames));
        // Sizes written as variables are unknown
        Assert.False(assets[4].HasFrameSize);
    }

    [AvaloniaTheory]
    [InlineData("Graphics/ForestHop/beetle.png", 32, 32, 3)]       // gaps between the figures
    [InlineData("Graphics/ForestHop/pinecone.png", 32, 32, 6)]
    [InlineData("Graphics/ForestHop/gem.png", 20, 20, 4)]
    [InlineData("Graphics/Anim/Anim_Earth_W150_H150_B48.png", 150, 150, 48)]   // frames that touch
    [InlineData("Graphics/Anim/Anim_Beast_W32_H56_B11.png", 32, 56, 16)]
    public void Guesses_the_frames_of_a_sheet(string file, int w, int h, int count)
    {
        using var bitmap = new Bitmap(Repo.PathOf(file));
        var guess = SheetGuess.Guess(bitmap);
        Assert.Equal((w, h, count), (guess.FrameWidth, guess.FrameHeight, guess.FrameCount));
    }
}

// The drawing tools both editors share
public class GridToolsTests
{
    private static string Draw(int w, int h, Action<Action<int, int>> shape)
    {
        var cells = new char[w * h];
        Array.Fill(cells, '.');
        shape((x, y) => { if (x >= 0 && y >= 0 && x < w && y < h) cells[y * w + x] = '#'; });
        return string.Join("/", Enumerable.Range(0, h).Select(y => new string(cells, y * w, w)));
    }

    [Fact]
    public void Line_has_no_gaps() =>
        Assert.Equal("#..../.##../...##", Draw(5, 3, p => GridTools.Line(0, 0, 4, 2, p)));

    [Fact]
    public void Rectangle_outline_and_filled()
    {
        Assert.Equal("####/#..#/####", Draw(4, 3, p => GridTools.Rectangle(3, 2, 0, 0, false, p)));
        Assert.Equal("####/####/####", Draw(4, 3, p => GridTools.Rectangle(0, 0, 3, 2, true, p)));
    }

    [Fact]
    public void Ellipse_is_round_and_one_cell_thick() =>
        Assert.Equal(".###./#...#/#...#/#...#/.###.", Draw(5, 5, p => GridTools.Ellipse(0, 0, 4, 4, false, p)));

    [Fact]
    public void Fill_stops_at_other_cells()
    {
        // A wall down the middle keeps the right side as it was
        var cells = new[] { 0, 1, 0,
                            0, 1, 0,
                            0, 1, 0 };
        GridTools.Fill(cells, 3, 3, 0, 0, 7);
        Assert.Equal(new[] { 7, 1, 0, 7, 1, 0, 7, 1, 0 }, cells);
    }

    [Fact]
    public void Flips_and_shifts()
    {
        var cells = new[] { 1, 2, 3, 4, 5, 6 };   // 3 wide, 2 high
        GridTools.FlipHorizontal(cells, 3, 2);
        Assert.Equal(new[] { 3, 2, 1, 6, 5, 4 }, cells);
        GridTools.FlipVertical(cells, 3, 2);
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, cells);
        GridTools.Shift(cells, 3, 2, 1, 0);
        Assert.Equal(new[] { 4, 6, 5, 1, 3, 2 }, cells);
    }

    [Fact]
    public void Common_colours_most_used_first_without_transparent()
    {
        var frames = new[] { new uint[] { 5, 5, 9, 0 }, new uint[] { 9, 9, 0, 0 } };
        Assert.Equal(new uint[] { 9, 5 }, GridTools.CommonColours(frames, 10, c => c == 0));
    }
}
