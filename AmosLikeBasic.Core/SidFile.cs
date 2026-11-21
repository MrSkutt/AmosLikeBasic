using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AmosLikeBasic;

// A SID file (PSID or RSID, versions 1 – 4): Commodore 64 music as its player routine, the 6502 code
// and its data, with a header saying where it goes in memory, which routines start a tune and play
// it, how many tunes there are and how often play is called.
public sealed class SidFile
{
    public bool Rsid;                       // "real" SID: needs a whole C64, interrupts and all
    public int Version;
    public ushort LoadAddress, InitAddress, PlayAddress;
    public int Tunes = 1, StartTune = 1;
    public uint Speed;                      // a bit for each tune: 0 the screen's 50 Hz, 1 the CIA timer
    public string Title = "", Author = "", Released = "";
    public bool Ntsc;
    public bool Mos8580;                    // the newer SID chip (the 6581 otherwise)
    public bool BasicTune;                  // an RSID with a BASIC program: needs the C64's ROMs
    public ushort[] ExtraSids = Array.Empty<ushort>();   // second and third SID chips' addresses
    public byte[] Data = Array.Empty<byte>();
    public int[] Seconds = Array.Empty<int>();            // each tune's length, from Songlengths.md5

    // From its first four bytes, so that the start of a file is enough to tell
    public static bool IsSid(byte[] bytes) =>
        bytes.Length >= 4 && (Encoding.ASCII.GetString(bytes, 0, 4) is "PSID" or "RSID");

    public static SidFile Read(byte[] bytes)
    {
        if (!IsSid(bytes)) throw new InvalidDataException("Not a SID file");
        if (bytes.Length < 0x76) throw new InvalidDataException("The SID file is cut short");
        int U16(int at) => bytes[at] << 8 | bytes[at + 1];
        string Text(int at) => Encoding.Latin1.GetString(bytes, at, 32).Split('\0')[0].Trim();
        var sid = new SidFile
        {
            Rsid = bytes[0] == 'R',
            Version = U16(4),
            LoadAddress = (ushort)U16(8),
            InitAddress = (ushort)U16(10),
            PlayAddress = (ushort)U16(12),
            Tunes = Math.Max(1, U16(14)),
            StartTune = Math.Max(1, U16(16)),
            Speed = (uint)(U16(18) << 16 | U16(20)),
            Title = Text(0x16),
            Author = Text(0x36),
            Released = Text(0x56),
        };
        int dataOffset = U16(6);
        if (sid.Version >= 2 && bytes.Length >= 0x7C)
        {
            int flags = U16(0x76);
            sid.BasicTune = sid.Rsid && (flags & 2) != 0;
            sid.Ntsc = ((flags >> 2) & 3) == 2;
            sid.Mos8580 = ((flags >> 4) & 3) == 2;
            var extra = new List<ushort>();
            foreach (int at in new[] { 0x7A, 0x7B })
            {
                int page = bytes[at];
                // $D420 – $D7E0 or $DE00 – $DFE0, at even pages of 16
                if (page != 0 && (page & 1) == 0 && (page is >= 0x42 and <= 0x7E or >= 0xE0 and <= 0xFE)) extra.Add((ushort)(0xD000 | page << 4));
                if (sid.Version < 4) break;
            }
            sid.ExtraSids = extra.ToArray();
        }
        if (sid.StartTune > sid.Tunes) sid.StartTune = 1;

        // A load address of 0 means the data's first two bytes are the address (low byte first)
        var data = bytes.AsSpan(Math.Min(dataOffset, bytes.Length));
        if (sid.LoadAddress == 0 && data.Length >= 2)
        {
            sid.LoadAddress = (ushort)(data[0] | data[1] << 8);
            data = data[2..];
        }
        sid.Data = data.ToArray();
        if (sid.InitAddress == 0) sid.InitAddress = sid.LoadAddress;
        return sid;
    }

    // Reads a SID file and its tunes' lengths, if the High Voltage SID Collection's Songlengths.md5
    // is in a DOCUMENTS folder above it
    public static SidFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sid = Read(bytes);
        sid.Seconds = Lengths(path, bytes) ?? Array.Empty<int>();
        return sid;
    }

    private static readonly Dictionary<string, Dictionary<string, int[]>> _lengthFiles = new();

    private static int[]? Lengths(string path, byte[] bytes)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        while (folder != null)
        {
            string file = Path.Combine(folder, "DOCUMENTS", "Songlengths.md5");
            if (File.Exists(file))
            {
                Dictionary<string, int[]> table;
                lock (_lengthFiles)
                    if (!_lengthFiles.TryGetValue(file, out table!))
                        _lengthFiles[file] = table = ReadLengths(file);
                string md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
                return table.TryGetValue(md5, out var seconds) ? seconds : null;
            }
            folder = Path.GetDirectoryName(folder);
        }
        return null;
    }

    // Lines "md5=m:ss m:ss.sss ...", one length for each tune
    private static Dictionary<string, int[]> ReadLengths(string file)
    {
        var table = new Dictionary<string, int[]>();
        foreach (var line in File.ReadLines(file))
        {
            int eq = line.IndexOf('=');
            if (eq != 32 || line.StartsWith(';')) continue;
            table[line[..32]] = line[(eq + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t =>
                {
                    var mmss = t.Split('(')[0].Split(':');
                    return mmss.Length == 2 && int.TryParse(mmss[0], out int m) && double.TryParse(mmss[1],
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s)
                        ? m * 60 + (int)Math.Ceiling(s) : 0;
                }).ToArray();
        }
        return table;
    }
}
