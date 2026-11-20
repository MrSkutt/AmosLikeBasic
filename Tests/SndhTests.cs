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

    // ── The 68000 running a player ──

    // A tiny SNDH tune in 68000 code: init turns on tone A in the mixer, play sets tone A to period 284
    // (440 Hz) at full volume. MOVE.B #n,($88xx).W is how ST code reaches the sound chip: $8800 picks a
    // register, $8802 writes it.
    private static byte[] TinyTune()
    {
        var code = new List<ushort>();
        void Ym(int register, int value) { code.AddRange(new ushort[] { 0x11FC, (ushort)register, 0x8800, 0x11FC, (ushort)value, 0x8802 }); }
        int init = 0x40;
        Ym(7, 0x3E);
        code.Add(0x4E75);                       // RTS
        int play = init + code.Count * 2;
        Ym(0, 0x1C); Ym(1, 0x01); Ym(8, 15);
        code.Add(0x4E75);
        var b = new byte[init + code.Count * 2];
        void Word(int at, int w) { b[at] = (byte)(w >> 8); b[at + 1] = (byte)w; }
        Word(0, 0x6000); Word(2, init - 2);      // BRA.W init
        Word(4, 0x4E75); Word(6, 0x4E71);        // exit: RTS
        Word(8, 0x6000); Word(10, play - 10);    // BRA.W play
        Encoding.ASCII.GetBytes("SNDHTITLTiny\0\0TC50\0\0HDNS").CopyTo(b, 12);
        for (int i = 0; i < code.Count; i++) Word(init + i * 2, code[i]);
        return b;
    }

    [Fact]
    public void An_SNDH_player_runs_and_its_sound_is_recorded()
    {
        var sndh = SndhFile.Read(TinyTune());
        Assert.Equal(("Tiny", 50), (sndh.Title, sndh.TimerHz));
        var ym = SndhPlayer.Record(sndh, seconds: 2);
        Assert.Equal((100, 50), (ym.Frames.Length, ym.FrameRate));
        var frame = ym.Frames[10];
        Assert.Equal((0x1C, 0x01, 0x3E, 15), (frame[0], frame[1], frame[7], frame[8]));

        var sound = new float[44100];
        new YmPlayer(ym, 44100).Render(sound);
        int crossings = 0;
        for (int i = 1; i < sound.Length; i++) if (sound[i - 1] < 0 && sound[i] >= 0) crossings++;
        Assert.InRange(crossings, 435, 445);
    }

    [Fact]
    public void A_68000_adds_branches_and_calls()
    {
        // D0 = 5; loop: D1 += D0; DBRA D0,loop  → D1 = 5+4+3+2+1+0 = 15; then BSR to a routine that sets D2
        ushort[] program =
        {
            0x7005,             // MOVEQ #5,D0
            0x7200,             // MOVEQ #0,D1
            0xD240,             // loop: ADD.W D0,D1
            0x51C8, 0xFFFC,     // DBRA D0,loop
            0x6100, 0x0008,     // BSR.W routine (8 bytes on from the extension word)
            0x4E71,             // NOP (where it returns)
            0x4E72, 0x2700,     // STOP
            0x7463,             // routine: MOVEQ #99,D2
            0x4E75              // RTS
        };
        var st = new AtariSt();
        for (int i = 0; i < program.Length; i++) st.Write16((uint)(0x1000 + i * 2), program[i]);
        var cpu = new M68000(st);
        cpu.SetState(0x2700, 0x7000, 0x8000);
        cpu.PC = 0x1000;
        for (int i = 0; i < 100 && !cpu.Stopped; i++) cpu.Step();
        Assert.Equal((15u, 99u), (cpu.D[1] & 0xFFFF, cpu.D[2]));
        Assert.True(cpu.Stopped);
    }

    // A SID voice made as Atari players make it: init sets Timer A of the MFP going (predivisor 4, count 61:
    // 2.4576 MHz / 244 = 10 072 Hz) with a routine at $134 that flips channel A's volume between 15 and 0
    // on every interrupt. The tone generators are off, so the flipping is the sound: 5036 Hz.
    private static byte[] TimerTune()
    {
        var code = new List<ushort>();
        int init = 0x40;
        code.AddRange(new ushort[]
        {
            0x21FC, 0x0000, 0x0000, 0x0134,     // MOVE.L #handler,$134.W (the address is filled in below)
            0x11FC, 0x0007, 0x8800,             // MOVE.B #7,$8800.W   (the mixer:
            0x11FC, 0x003F, 0x8802,             // MOVE.B #$3F,$8802.W  everything off)
            0x11FC, 0x0008, 0x8800,             // MOVE.B #8,$8800.W   (channel A's volume picked)
            0x11FC, 0x003D, 0xFA1F,             // MOVE.B #61,$FA1F.W  (Timer A data)
            0x11FC, 0x0001, 0xFA19,             // MOVE.B #1,$FA19.W   (Timer A control: predivisor 4)
            0x08F8, 0x0005, 0xFA07,             // BSET #5,$FA07.W     (enable Timer A's interrupt)
            0x08F8, 0x0005, 0xFA13,             // BSET #5,$FA13.W     (and unmask it)
            0x4E75                              // RTS
        });
        int play = init + code.Count * 2;
        code.Add(0x4E75);                       // play: RTS
        int handler = init + code.Count * 2;
        code.AddRange(new ushort[]
        {
            0x0A38, 0x000F, 0x2000,             // EORI.B #15,$2000.W
            0x11F8, 0x2000, 0x8802,             // MOVE.B $2000.W,$8802.W
            0x4E73                              // RTE
        });
        uint handlerAddress = 0x10000 + (uint)handler;   // where the file is loaded
        code[1] = (ushort)(handlerAddress >> 16);
        code[2] = (ushort)handlerAddress;
        var b = new byte[init + code.Count * 2];
        void Word(int at, int w) { b[at] = (byte)(w >> 8); b[at + 1] = (byte)w; }
        Word(0, 0x6000); Word(2, init - 2);
        Word(4, 0x4E75); Word(6, 0x4E71);
        Word(8, 0x6000); Word(10, play - 10);
        Encoding.ASCII.GetBytes("SNDHTITLTimer\0TC50\0\0HDNS").CopyTo(b, 12);
        for (int i = 0; i < code.Count; i++) Word(init + i * 2, code[i]);
        return b;
    }

    [Fact]
    public void The_MFP_timer_interrupts_make_a_SID_voice()
    {
        var sndh = SndhFile.Read(TimerTune());
        var sound = new float[44100];
        new SndhSound(sndh, 44100, seconds: 2).Render(sound);
        int crossings = 0;
        for (int i = 4410; i < sound.Length; i++) if (sound[i - 1] < 0 && sound[i] >= 0) crossings++;
        // 0.9 s of a 5036 Hz square wave
        Assert.InRange(crossings, 4400, 4660);
    }

    [Fact]
    public void A_SID_voice_made_by_a_timer_is_recognised_for_the_tracker()
    {
        // The volume flipped about 10 000 times a second: a SID voice on channel A at the timer's frequency
        var ym = SndhPlayer.Record(SndhFile.Read(TimerTune()), seconds: 2);
        Assert.NotNull(ym.FrameEffects);
        var sids = ym.FrameEffects!.SelectMany(e => e).Where(e => e.Kind == YmPlayer.EffectKind.Sid).ToList();
        Assert.True(sids.Count > 50, "nearly every frame");
        Assert.All(sids, e => Assert.Equal(0, e.Channel));
        Assert.InRange(sids.Average(e => e.Frequency), 9800, 10300);
        Assert.Empty(ym.Drums);
    }
}
