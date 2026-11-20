using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AmosLikeBasic;

// An SNDH file: music for the Atari ST as its original player routine, 68000 code with the notes,
// rather than recorded registers. The file starts with three jumps (init, exit, play), then "SNDH"
// and tags until "HDNS": the title, the composer, the year, how many tunes it has and how often the
// play routine is called. Most SNDH files are packed with Pack-Ice, which is unpacked here.
public sealed class SndhFile
{
    public byte[] Data = Array.Empty<byte>();   // the unpacked file, as it is loaded into the Atari's memory
    public string Title = "", Composer = "", Ripper = "", Converter = "", Year = "";
    public int Tunes = 1, DefaultTune = 1;
    public int TimerHz = 50;                      // how often play is called
    public string Timer = "VBL";                  // which timer calls it: VBL, A, B, C or D
    public int[] Seconds = Array.Empty<int>();    // each tune's length, where the file says (0: not known)

    public static bool IsSndh(byte[] b) =>
        Ice.IsPacked(b) || (b.Length > 16 && Encoding.ASCII.GetString(b, 12, 4) == "SNDH");

    public static SndhFile Read(byte[] bytes)
    {
        if (Ice.IsPacked(bytes)) bytes = Ice.Unpack(bytes);
        if (bytes.Length < 16 || Encoding.ASCII.GetString(bytes, 12, 4) != "SNDH")
            throw new InvalidDataException("not an SNDH file");
        var sndh = new SndhFile { Data = bytes };
        int p = 16, end = Math.Min(bytes.Length, 4096);
        while (p + 4 <= end)
        {
            string tag = Encoding.ASCII.GetString(bytes, p, 4);
            if (tag == "HDNS") break;
            switch (tag)
            {
                case "TITL": sndh.Title = Text(bytes, ref p, 4); continue;
                case "COMM": sndh.Composer = Text(bytes, ref p, 4); continue;
                case "RIPP": sndh.Ripper = Text(bytes, ref p, 4); continue;
                case "CONV": sndh.Converter = Text(bytes, ref p, 4); continue;
                case "YEAR": sndh.Year = Text(bytes, ref p, 4); continue;
                case "TIME":
                {
                    // A word of seconds for each tune
                    p += 4;
                    sndh.Seconds = new int[sndh.Tunes];
                    for (int t = 0; t < sndh.Tunes && p + 2 <= end; t++, p += 2) sndh.Seconds[t] = bytes[p] << 8 | bytes[p + 1];
                    continue;
                }
            }
            if (tag.StartsWith("##") && char.IsDigit(tag[2]))
            {
                // ##nn: how many tunes
                if (int.TryParse(tag[2..], out int n) && n > 0) sndh.Tunes = n;
                p += 4;
                SkipToText(bytes, ref p, end);
                continue;
            }
            if (tag.StartsWith("!#") && char.IsDigit(tag[2]))
            {
                // !#nn: the tune to start with
                if (int.TryParse(tag[2..], out int n) && n > 0) sndh.DefaultTune = n;
                p += 4;
                SkipToText(bytes, ref p, end);
                continue;
            }
            if (tag[0] == 'T' && tag[1] is 'A' or 'B' or 'C' or 'D' && char.IsDigit(tag[2]))
            {
                // TAnnn ... TDnnn: called by that timer, nnn times a second
                sndh.Timer = tag[1].ToString();
                string hz = Text(bytes, ref p, 2);
                if (int.TryParse(hz, out int n) && n > 0) sndh.TimerHz = n;
                continue;
            }
            if (tag.StartsWith("!V"))
            {
                // !Vnn: called every vertical blank, nn times a second
                sndh.Timer = "VBL";
                string hz = Text(bytes, ref p, 2);
                if (int.TryParse(hz, out int n) && n > 0) sndh.TimerHz = n;
                continue;
            }
            // Anything else (names of the tunes, flags): step on to the next tag
            p += 2;
        }
        sndh.DefaultTune = Math.Clamp(sndh.DefaultTune, 1, sndh.Tunes);
        return sndh;
    }

    // A text after a tag of `skip` bytes, up to its 0, and on to an even place for the next tag
    private static string Text(byte[] b, ref int p, int skip)
    {
        p += skip;
        int start = p;
        while (p < b.Length && b[p] != 0) p++;
        string s = Encoding.Latin1.GetString(b, start, p - start).Trim();
        p++;
        if ((p & 1) != 0 && p < b.Length && b[p] == 0) p++;
        return s;
    }

    private static void SkipToText(byte[] b, ref int p, int end)
    {
        while (p < end && b[p] == 0) p++;
    }
}

// Unpacks Pack-Ice (by Axe of Delight), as used for SNDH files and many Atari programs. The packed data
// is read backwards from its end, bit by bit, and written from the end of the result towards its start.
//
// After IceDecompressor in "ancient" by Teemu Suutari:
// Copyright (c) 2017-2026, Teemu Suutari. All rights reserved.
// Redistribution and use in source and binary forms, with or without modification, are permitted
// provided that the following conditions are met: 1. Redistributions of source code must retain the
// above copyright notice, this list of conditions and the following disclaimer. 2. Redistributions in
// binary form must reproduce the above copyright notice, this list of conditions and the following
// disclaimer in the documentation and/or other materials provided with the distribution.
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
// IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE
// USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
public static class Ice
{
    public static bool IsPacked(byte[] b) =>
        b.Length >= 12 && b[0] is (byte)'I' && b[1] is (byte)'C' or (byte)'c' && b[2] is (byte)'E' or (byte)'e' && b[3] == '!';

    public static byte[] Unpack(byte[] packed)
    {
        int packedSize = Be32(packed, 4), rawSize = Be32(packed, 8);
        if (packedSize <= 12 || packedSize > packed.Length || rawSize <= 0 || rawSize > 64 << 20)
            throw new InvalidDataException("the Pack-Ice data is damaged");
        // "ICE!" (2.31 and later) reads the stream a byte at a time; the older "Ice!" a long at a time,
        // except for some files that use the newer stream under the old name
        bool newer = packed[1] == 'C';
        if (!newer)
        {
            try { return Run(packed, packedSize, rawSize, false); }
            catch (InvalidDataException) { }
        }
        return Run(packed, packedSize, rawSize, true);
    }

    private static int Be32(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];

    // The packed bytes from packedSize down to 12, and the bits of each word from its top
    private sealed class Reader
    {
        private readonly byte[] _b;
        private int _pos;
        private readonly int _start;
        private readonly bool _bytes;
        private uint _buf;
        private int _bits;

        public Reader(byte[] b, int start, int end, bool bytes) { _b = b; _start = start; _pos = end; _bytes = bytes; }

        public byte Byte()
        {
            if (_pos <= _start) throw new InvalidDataException("the Pack-Ice data is damaged");
            return _b[--_pos];
        }

        public uint Be32()
        {
            uint b0 = Byte(), b1 = Byte(), b2 = Byte(), b3 = Byte();
            return b3 << 24 | b2 << 16 | b1 << 8 | b0;
        }

        public void Reset(uint content, int length) { _buf = content; _bits = length; }

        public uint Bits(int count)
        {
            uint ret = 0;
            while (count > 0)
            {
                if (_bits == 0)
                {
                    if (_bytes) { _buf = Byte(); _bits = 8; }
                    else { _buf = Be32(); _bits = 32; }
                }
                int take = Math.Min(count, _bits);
                _bits -= take;
                ret = (ret << take) | ((_buf >> _bits) & (uint)((1L << take) - 1));
                count -= take;
            }
            return ret;
        }
    }

    // Codes of growing lengths: each length's values follow the ones before
    private sealed class Vlc
    {
        private readonly int[] _lengths;
        private readonly uint[] _offsets;

        public Vlc(params int[] lengths)
        {
            _lengths = lengths;
            _offsets = new uint[lengths.Length];
            uint sum = 0;
            for (int i = 0; i < lengths.Length; i++) { _offsets[i] = sum; sum += 1u << lengths[i]; }
        }

        public uint Decode(Reader r, uint which)
        {
            if (which >= _lengths.Length) throw new InvalidDataException("the Pack-Ice data is damaged");
            return _offsets[which] + r.Bits(_lengths[which]);
        }

        // Read a length's bits; all ones go on to the next length
        public uint Cascade(Reader r)
        {
            for (int i = 0; i < _lengths.Length; i++)
            {
                uint v = r.Bits(_lengths[i]);
                if (i == _lengths.Length - 1 || v != (1u << _lengths[i]) - 1) return _offsets[i] - (uint)i + v;
            }
            throw new InvalidDataException("the Pack-Ice data is damaged");
        }
    }

    private static byte[] Run(byte[] packed, int packedSize, int rawSize, bool bytes)
    {
        var r = new Reader(packed, 12, packedSize, bytes);
        // The first word holds the first bits, above a marker bit
        {
            uint value = bytes ? r.Byte() : r.Be32();
            uint tmp = value;
            int count = 0;
            while (tmp != 0) { tmp <<= 1; count++; }
            if (count > 0) count--;
            if (count > 0) r.Reset(value >> (32 - count), count - (bytes ? 24 : 0));
        }

        var output = new byte[rawSize];
        int o = rawSize;
        var literal = bytes ? new Vlc(1, 2, 2, 3, 8, 15) : new Vlc(1, 2, 2, 3, 10);
        var countBase = new Vlc(1, 1, 1, 1);
        var countVlc = new Vlc(0, 0, 1, 2, 10);
        var distanceBase = new Vlc(1, 1);
        var distanceVlc = new Vlc(5, 8, 12);
        while (true)
        {
            if (r.Bits(1) != 0)
            {
                uint length = literal.Cascade(r) + 1;
                for (uint i = 0; i < length; i++)
                {
                    if (o == 0) throw new InvalidDataException("the Pack-Ice data is damaged");
                    output[--o] = r.Byte();
                }
            }
            if (o == 0) break;

            uint count = countVlc.Decode(r, countBase.Cascade(r)) + 2;
            uint distance;
            if (count == 2)
            {
                distance = r.Bits(1) != 0 ? r.Bits(9) + 0x40 : r.Bits(6);
                distance += count - (bytes ? 1u : 0);
            }
            else
            {
                uint b = distanceBase.Cascade(r);
                if (b < 2) b ^= 1;
                distance = distanceVlc.Decode(r, b);
                if (bytes) distance = distance != 0 ? distance + count - 1 : 1;
                else distance += count;
            }
            // Copy from further on in the result (it is written backwards)
            if (distance == 0 || count > o || o + distance > rawSize) throw new InvalidDataException("the Pack-Ice data is damaged");
            for (uint i = 0; i < count; i++, o--) output[o - 1] = output[o - 1 + distance];
        }
        return output;
    }
}
