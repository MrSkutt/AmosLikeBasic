using System.Text;

namespace AmosLikeBasic.Tests;

// SNDH files (Atari ST music as its player routine): the header, and Pack-Ice unpacking
public class SndhTests
{
    // Three jumps (init, exit, play), then the tags, padded to even places as in real files
    private static byte[] Header(params string[] tags)
    {
        var b = new List<byte>();
        for (int i = 0; i < 3; i++) b.AddRange(new byte[] { 0x60, 0x00, 0x00, 0x00 });   // BRA.W
        b.AddRange("SNDH"u8.ToArray());
        foreach (var t in tags)
        {
            b.AddRange(Encoding.Latin1.GetBytes(t));
            b.Add(0);
            if (b.Count % 2 != 0) b.Add(0);
        }
        b.AddRange("HDNS"u8.ToArray());
        b.AddRange(new byte[64]);
        return b.ToArray();
    }

    [Fact]
    public void The_header_tells_the_title_composer_tunes_and_timer()
    {
        var s = SndhFile.Read(Header("TITLCybernoid", "COMMJochen Hippel", "YEAR1988", "##03", "!#02", "TC50"));
        Assert.Equal(("Cybernoid", "Jochen Hippel", "1988"), (s.Title, s.Composer, s.Year));
        Assert.Equal((3, 2), (s.Tunes, s.DefaultTune));
        Assert.Equal(("C", 50), (s.Timer, s.TimerHz));
        Assert.True(SndhFile.IsSndh(s.Data));
    }

    [Fact]
    public void A_file_that_is_not_SNDH_says_so()
    {
        Assert.False(SndhFile.IsSndh(new byte[100]));
        Assert.Throws<InvalidDataException>(() => SndhFile.Read(new byte[100]));
    }

    // Pack-Ice: the Atari music in a folder on this computer, when there is one
    [Fact]
    public void Packed_SNDH_files_are_unpacked()
    {
        const string folder = "/Users/mattias/Projects/Atari/Common/Music";
        if (!Directory.Exists(folder)) Assert.Skip("needs the Atari music folder");
        var packed = Directory.GetFiles(folder, "*.sndh").Select(File.ReadAllBytes).Where(Ice.IsPacked).ToList();
        Assert.NotEmpty(packed);
        foreach (var bytes in packed)
        {
            var s = SndhFile.Read(bytes);
            Assert.Equal("SNDH", Encoding.ASCII.GetString(s.Data, 12, 4));
            Assert.True(s.Title.Length > 0);
        }
    }
}
