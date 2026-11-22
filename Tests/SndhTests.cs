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
    // (data and control: Timer A's; 61 and predivisor 4 by default, about 10 000 interrupts a second)
    private static byte[] TimerTune(int data = 61, int control = 1)
    {
        var code = new List<ushort>();
        int init = 0x40;
        code.AddRange(new ushort[]
        {
            0x21FC, 0x0000, 0x0000, 0x0134,     // MOVE.L #handler,$134.W (the address is filled in below)
            0x11FC, 0x0007, 0x8800,             // MOVE.B #7,$8800.W   (the mixer:
            0x11FC, 0x003F, 0x8802,             // MOVE.B #$3F,$8802.W  everything off)
            0x11FC, 0x0008, 0x8800,             // MOVE.B #8,$8800.W   (channel A's volume picked)
            0x11FC, (ushort)data, 0xFA1F,       // MOVE.B #data,$FA1F.W     (Timer A data)
            0x11FC, (ushort)control, 0xFA19,    // MOVE.B #control,$FA19.W  (Timer A control: predivisor 4 by default)
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

    [Fact]
    public void A_slow_SID_voice_is_recognised_from_the_times_of_its_writes()
    {
        // Predivisor 200, data 192: 64 interrupts a second, about one a frame (a bass at 32 Hz, made with
        // the tone off): too few in a frame to count, but they go on and off, from a timer
        var ym = SndhPlayer.Record(SndhFile.Read(TimerTune(192, 7)), seconds: 2);
        Assert.NotNull(ym.FrameEffects);
        var sids = ym.FrameEffects!.SelectMany(e => e).Where(e => e.Kind == YmPlayer.EffectKind.Sid).ToList();
        Assert.True(sids.Count > 50, "nearly every frame");
        Assert.All(sids, e => Assert.InRange(e.Frequency, 62, 66));
    }

    // ── The rest of the machine that players use ──

    [Fact]
    public void A_byte_on_an_odd_address_reaches_the_YM_but_a_word_only_its_upper_byte()
    {
        var st = new AtariSt();
        st.Write8(0xFF8801, 8);        // the 68000 puts a byte on both halves of the bus
        st.Write8(0xFF8803, 12);
        Assert.Equal(12, st.Ym[8]);
        st.Write16(0xFF8800, 0x0900);  // register 9 (the 00 below it is not seen)
        st.Write16(0xFF8802, 0x0700);
        Assert.Equal(7, st.Ym[9]);
        Assert.Equal(0x02, st.Read8(0xFFFC04));   // the MIDI ACIA: ready to send
    }

    [Fact]
    public void STe_DMA_sound_plays_a_frame_from_memory_round_and_round_or_once()
    {
        var st = new AtariSt();
        for (int i = 0; i < 100; i++) st.Ram[0x20000 + i] = 0x40;   // a frame of 100 samples at half level
        void Set(uint a, int v) => st.Write8(0xFF8900 | a, (byte)v);
        Set(0x03, 0x02); Set(0x05, 0x00); Set(0x07, 0x00);            // start $020000
        Set(0x0F, 0x02); Set(0x11, 0x00); Set(0x13, 0x64);            // end $020064
        Set(0x21, 0x81);                                               // mono, 12517 Hz
        // Timer A counts the ends of frames (event mode), and interrupts at the first
        st.Mfp.Write(15, 1); st.Mfp.Write(12, 8); st.Mfp.Write(3, 0x20); st.Mfp.Write(9, 0x20);

        Set(0x01, 3);                                                  // play, round and round
        double frame = 100 / 12517.0 * SndhMachine.CpuClock;
        st.Dma.Run(frame * 0.5);
        Assert.Equal(0.5, st.Dma.Sample(), 3);
        Assert.Null(st.Mfp.Acknowledge());
        st.Dma.Run(frame);
        Assert.Equal(0.5, st.Dma.Sample(), 3);                         // the second time round
        Assert.Equal(0x40 + 13, st.Mfp.Acknowledge());                 // timer A's interrupt

        Set(0x01, 1);                                                  // once: it stops at the end
        st.Dma.Run(frame * 1.1);
        Assert.Equal(0, st.Dma.Sample());
        Assert.Equal(0, st.Read8(0xFF8901) & 1);
    }

    [Fact]
    public void The_Microwire_shifts_for_16_microseconds_and_sets_the_volume()
    {
        var st = new AtariSt();
        st.Ram[0x20000] = 0x40;
        st.Write8(0xFF8903, 0x02); st.Write8(0xFF890F, 0x02); st.Write8(0xFF8913, 0x02);
        st.Write8(0xFF8921, 0x80);
        st.Write8(0xFF8901, 1);
        // Master volume 20 of 40: -40 dB. The command is 10 (the LMC1992), 011 (master), then the value
        st.Write16(0xFF8924, 0x07FF);
        st.Write16(0xFF8922, 2 << 9 | 3 << 6 | 20);
        Assert.NotEqual(0x07FF, st.Read16(0xFF8924));                  // turning round while it sends
        st.Dma.Run(16 * SndhMachine.CpuClock / 1_000_000 + 8);
        Assert.Equal(0x07FF, st.Read16(0xFF8924));                     // and back
        Assert.Equal(0.5 * 0.01, st.Dma.Sample(), 4);
    }

    [Fact]
    public void Dosound_plays_its_list_of_writes_fifty_times_a_second()
    {
        // init: Dosound(list); play does nothing. The list: tone A at 284, mixer, full volume, wait 5, volume 0, end
        var code = new List<ushort>();
        int init = 0x40, list = 0x80;
        code.AddRange(new ushort[] { 0x2F3C, 0x0001, (ushort)list, 0x3F3C, 32, 0x4E4E, 0x5C8F, 0x4E75 });   // MOVE.L #list,-(SP); MOVE.W #32,-(SP); TRAP #14; ADDQ.L #6,SP; RTS
        int play = init + code.Count * 2;
        code.Add(0x4E75);
        var b = new byte[0xA0];
        void Word(int at, int w) { b[at] = (byte)(w >> 8); b[at + 1] = (byte)w; }
        Word(0, 0x6000); Word(2, init - 2);
        Word(4, 0x4E75); Word(6, 0x4E71);
        Word(8, 0x6000); Word(10, play - 10);
        Encoding.ASCII.GetBytes("SNDHTITLDo\0\0TC50\0\0HDNS").CopyTo(b, 12);
        for (int i = 0; i < code.Count; i++) Word(init + i * 2, code[i]);
        new byte[] { 0x00, 0x1C, 0x01, 0x01, 0x07, 0x3E, 0x08, 0x0F, 0xFF, 0x05, 0x08, 0x00, 0xFF, 0x00 }.CopyTo(b, list);
        var ym = SndhPlayer.Record(SndhFile.Read(b), seconds: 1);
        Assert.Equal((0x1C, 0x3E, 15), (ym.Frames[2][0], ym.Frames[2][7], ym.Frames[2][8]));
        Assert.Equal(0, ym.Frames[12][8]);
    }
}
