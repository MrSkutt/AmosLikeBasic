using Avalonia.Platform.Storage;

namespace AmosLikeBasic.Tests;

// The save dialog's name, with its extension once: some dialogs add it again to a name that has it
public class SaveDialogTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "songs");

    private static string Fix(string name, params string[] patterns) =>
        Path.GetFileName(SaveDialog.FixName(Path.Combine(Folder, name), SaveDialog.Extensions(new FilePickerSaveOptions
        {
            FileTypeChoices = new[] { new FilePickerFileType("files") { Patterns = patterns } }
        })));

    [Theory]
    [InlineData("SONG.DHS.dhs", "SONG.DHS")]
    [InlineData("SONG.dhs.DHS", "SONG.dhs")]
    [InlineData("SONG.DHS", "SONG.DHS")]
    [InlineData("SONG", "SONG.DHS")]
    public void A_DHS_file_has_its_extension_once(string picked, string saved) =>
        Assert.Equal(saved, Fix(picked, "*.DHS", "*.dhs"));

    [Theory]
    [InlineData("new_song.amostrk.amostrk", "new_song.amostrk")]
    [InlineData("new_song.amostrk.amostrk.amostrk", "new_song.amostrk")]
    [InlineData("new_song.amostrk", "new_song.amostrk")]
    [InlineData("my.song", "my.song.amostrk")]
    public void A_song_has_its_extension_once(string picked, string saved) =>
        Assert.Equal(saved, Fix(picked, "*.amostrk"));

    [Fact]
    public void An_SNDH_file_keeps_either_of_its_extensions()
    {
        Assert.Equal("tune.snd", Fix("tune.snd", "*.sndh", "*.snd"));
        Assert.Equal("tune.sndh", Fix("tune.sndh.sndh", "*.sndh", "*.snd"));
        Assert.Equal("tune.snd", Fix("tune.snd.sndh", "*.sndh", "*.snd"));
    }
}
