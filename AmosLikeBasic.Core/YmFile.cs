using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AmosLikeBasic;

// A YM file: the sound chip of the Atari ST (and the ZX Spectrum's AY) recorded as its registers,
// 14 (or 16) of them for every frame, usually 50 times a second. Most YM files are packed with LHA
// (-lh5-), which is unpacked here. YM2, YM3, YM3b, YM4, YM5 and YM6 are read, with the special
// effects of YM5 and YM6 (digidrums, SID voice, sync buzzer), which Ym2149 plays.
public sealed class YmFile
{
    public byte[][] Frames = Array.Empty<byte[]>();   // [frame][register 0 – 15]
    public int Clock = 2000000;                       // Hz: 2 MHz on the Atari ST, 1.7734 MHz on the Spectrum
    public int FrameRate = 50;
    public int LoopFrame;
    public string Title = "", Author = "", Comment = "";
    public bool UsesSpecialEffects;                    // digidrums or other YM5/YM6 effects
    public int Version;                                // 5 or 6 for the files with effects, else 0
    // The digidrums: short samples played through a channel's volume, as levels 0 – 1
    public List<float[]> Drums = new();

    public double Seconds => Frames.Length / (double)Math.Max(1, FrameRate);

    public static bool IsYm(byte[] b) =>
        (b.Length >= 4 && b[0] == 'Y' && b[1] == 'M' && b[3] == '!') || IsLha(b);

    private static bool IsLha(byte[] b) => b.Length > 22 && b[2] == '-' && b[3] == 'l' && b[4] == 'h' && b[6] == '-';

    public static YmFile Read(byte[] bytes)
    {
        if (IsLha(bytes)) bytes = Lha.Unpack(bytes);
        if (bytes.Length < 4) throw new InvalidDataException("the file is too short to be a YM file");
        string id = Encoding.ASCII.GetString(bytes, 0, 4);
        var ym = new YmFile();
        switch (id)
        {
            case "YM2!":   // as YM3, made by Mad Max's own player
            case "YM3!":
                ym.Frames = Interleaved(bytes, 4, (bytes.Length - 4) / 14, 14);
                return ym;
            case "YM3b":
            {
                int frames = (bytes.Length - 8) / 14;
                ym.Frames = Interleaved(bytes, 4, frames, 14);
                ym.LoopFrame = U32Le(bytes, bytes.Length - 4);
                return ym;
            }
            case "YM4!":
            case "YM5!":
            case "YM6!":
                return ReadNew(bytes, id, ym);
        }
        throw new InvalidDataException($"not a YM file the program can read (\"{new string(id.Select(c => c is >= ' ' and < (char)127 ? c : '?').ToArray())}\")");
    }

    // YM4 – YM6: a header after "LeOnArD!", the digidrums, three texts, then the registers
    private static YmFile ReadNew(byte[] b, string id, YmFile ym)
    {
        int frames = (int)U32(b, 12);
        uint attributes = U32(b, 16);
        ym.Version = id[2] - '0';
        bool drum4Bits = (attributes & 4) != 0, drumSigned = (attributes & 2) != 0;
        int drums = U16(b, 20);
        int p = 22;
        if (id != "YM4!")
        {
            ym.Clock = (int)U32(b, 22);
            ym.FrameRate = Math.Max(1, U16(b, 26));
            ym.LoopFrame = (int)U32(b, 28);
            p = 34 + U16(b, 32);   // skips the extra data
        }
        else
        {
            ym.LoopFrame = (int)U32(b, 22);
            p = 26;
        }
        // The digidrums: a size and the samples, as 4-bit volume steps or 8-bit levels
        for (int d = 0; d < drums; d++)
        {
            int size = (int)U32(b, p);
            p += 4;
            if (size < 0 || p + size > b.Length) throw new InvalidDataException("the YM file ends inside a digidrum");
            var drum = new float[size];
            for (int i = 0; i < size; i++)
                drum[i] = drum4Bits ? (float)Ym2149.VolumeLevel(b[p + i] & 15)
                    : drumSigned ? ((sbyte)b[p + i] + 128) / 255f : b[p + i] / 255f;
            ym.Drums.Add(drum);
            p += size;
        }
        if (drums > 0) ym.UsesSpecialEffects = true;
        ym.Title = Text(b, ref p);
        ym.Author = Text(b, ref p);
        ym.Comment = Text(b, ref p);
        bool interleaved = (attributes & 1) != 0;
        int regs = 16;
        if (p + frames * regs > b.Length) throw new InvalidDataException("the YM file ends too early");
        ym.Frames = interleaved ? Interleaved(b, p, frames, regs) : Sequential(b, p, frames, regs);
        // Effects are marked in the top bits of registers 1 and 3 (and 5, 6, 8, 10 in YM6)
        foreach (var f in ym.Frames)
            if ((f[1] & 0x30) != 0 || (f[3] & 0x30) != 0) { ym.UsesSpecialEffects = true; break; }
        return ym;
    }

    // All the frames of register 0, then all of register 1 ...
    private static byte[][] Interleaved(byte[] b, int start, int frames, int regs)
    {
        if (frames < 0 || start + frames * regs > b.Length) throw new InvalidDataException("the YM file ends too early");
        var f = new byte[frames][];
        for (int i = 0; i < frames; i++)
        {
            f[i] = new byte[16];
            for (int r = 0; r < regs; r++) f[i][r] = b[start + r * frames + i];
        }
        return f;
    }

    private static byte[][] Sequential(byte[] b, int start, int frames, int regs)
    {
        var f = new byte[frames][];
        for (int i = 0; i < frames; i++)
        {
            f[i] = new byte[16];
            Array.Copy(b, start + i * regs, f[i], 0, regs);
        }
        return f;
    }

    private static string Text(byte[] b, ref int p)
    {
        int start = p;
        while (p < b.Length && b[p] != 0) p++;
        string s = Encoding.Latin1.GetString(b, start, p - start).Trim();
        p++;
        return s;
    }

    private static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    private static int U16(byte[] b, int o) => b[o] << 8 | b[o + 1];
    private static int U32Le(byte[] b, int o) => b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24;
}

// Unpacks the first file of an LHA archive packed with -lh5- (or stored with -lh0-): LZ77 with an 8 kB
// window and Huffman codes that change every block, as in Haruhiko Okumura's LHarc.
public static class Lha
{
    public static byte[] Unpack(byte[] a)
    {
        string method = Encoding.ASCII.GetString(a, 2, 5);
        int level = a[20];
        int packed = a[7] | a[8] << 8 | a[9] << 16 | a[10] << 24;
        int size = a[11] | a[12] << 8 | a[13] << 16 | a[14] << 24;
        int data;
        if (level == 2) data = a[0] | a[1] << 8;
        else
        {
            data = 2 + a[0];
            if (level == 1)
            {
                // Extended headers, counted in the packed size
                int next = a[data - 2] | a[data - 1] << 8;
                while (next > 0)
                {
                    packed -= next;
                    data += next;
                    next = a[data - 2] | a[data - 1] << 8;
                }
            }
        }
        if (data + packed > a.Length) throw new InvalidDataException("the LHA archive ends too early");
        if (method == "-lh0-") return a.AsSpan(data, size).ToArray();
        if (method != "-lh5-") throw new InvalidDataException($"LHA files packed with {method} cannot be read (only -lh5-)");
        return new Lh5(a, data, data + packed).Decode(size);
    }

    private sealed class Lh5
    {
        private const int NC = 510, NP = 14, NT = 19, DicSize = 8192;
        private readonly byte[] _in;
        private int _pos;
        private readonly int _end;
        private uint _bits;      // the next bits, the first one at the top
        private int _count;      // how many bits are in _bits

        public Lh5(byte[] input, int start, int end) { _in = input; _pos = start; _end = end; }

        private int Bits(int n)
        {
            if (n == 0) return 0;
            while (_count < n)
            {
                uint b = _pos < _end ? _in[_pos++] : 0u;
                _bits |= b << (24 - _count);
                _count += 8;
            }
            int v = (int)(_bits >> (32 - n));
            _bits <<= n;
            _count -= n;
            return v;
        }

        private int Peek(int n)
        {
            while (_count < n)
            {
                uint b = _pos < _end ? _in[_pos++] : 0u;
                _bits |= b << (24 - _count);
                _count += 8;
            }
            return (int)(_bits >> (32 - n));
        }

        // Canonical Huffman codes from their lengths: codes of one length follow each other in symbol order
        private sealed class Code
        {
            public readonly int[] Count = new int[17];
            public readonly int[] Symbols;
            public readonly int Single = -1;   // only one symbol: it takes no bits

            public Code(int[] lengths, int single = -1)
            {
                Single = single;
                var list = new List<int>();
                for (int len = 1; len <= 16; len++)
                    for (int s = 0; s < lengths.Length; s++)
                        if (lengths[s] == len) { list.Add(s); Count[len]++; }
                Symbols = list.ToArray();
            }
        }

        private int Decode(Code c)
        {
            if (c.Single >= 0) return c.Single;
            int code = 0, first = 0, index = 0;
            for (int len = 1; len <= 16; len++)
            {
                code |= Bits(1);
                int count = c.Count[len];
                if (code - first < count) return c.Symbols[index + code - first];
                index += count;
                first = (first + count) << 1;
                code <<= 1;
            }
            throw new InvalidDataException("the LHA data is damaged");
        }

        // The code lengths of the small trees (positions, and the tree for the big one)
        private Code ReadPtLen(int nn, int nbit, int special)
        {
            int n = Bits(nbit);
            if (n == 0) return new Code(new int[nn], Bits(nbit));
            var len = new int[nn];
            int i = 0;
            while (i < n && i < nn)
            {
                int c = Peek(3);
                if (c == 7)
                {
                    Bits(3);
                    while (Bits(1) == 1) c++;
                }
                else Bits(3);
                len[i++] = c;
                if (i == special)
                {
                    int zeros = Bits(2);
                    while (zeros-- > 0 && i < nn) len[i++] = 0;
                }
            }
            return new Code(len);
        }

        // The code lengths of the characters and match lengths, coded with the tree just read
        private Code ReadCLen(Code t)
        {
            int n = Bits(9);
            if (n == 0) return new Code(new int[NC], Bits(9));
            var len = new int[NC];
            int i = 0;
            while (i < n && i < NC)
            {
                int c = Decode(t);
                if (c <= 2)
                {
                    int zeros = c == 0 ? 1 : c == 1 ? Bits(4) + 3 : Bits(9) + 20;
                    while (zeros-- > 0 && i < NC) len[i++] = 0;
                }
                else len[i++] = c - 2;
            }
            return new Code(len);
        }

        public byte[] Decode(int size)
        {
            var output = new byte[size];
            int o = 0, left = 0;
            Code? c = null, p = null;
            while (o < size)
            {
                if (left == 0)
                {
                    left = Bits(16);
                    var t = ReadPtLen(NT, 5, 3);
                    c = ReadCLen(t);
                    p = ReadPtLen(NP, 4, -1);
                }
                left--;
                int sym = Decode(c!);
                if (sym < 256) { output[o++] = (byte)sym; continue; }
                int length = sym - 256 + 3;
                int dist = Decode(p!);
                if (dist > 0) dist = (1 << (dist - 1)) + Bits(dist - 1);
                int from = o - dist - 1;
                if (from < 0 || dist >= DicSize) throw new InvalidDataException("the LHA data is damaged");
                for (int k = 0; k < length && o < size; k++) output[o++] = output[from + k];
            }
            return output;
        }
    }
}
