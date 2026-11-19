using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace AmosLikeBasic;

// A sound recorded as numbers, for the tracker's sampled instruments: one channel of -1 – 1 and
// the rate it was recorded at
public sealed record SampleData(float[] Data, int Rate)
{
    public double Seconds => Data.Length / (double)Rate;
}

// WAV files read once and kept: 8, 16, 24 or 32-bit PCM or 32-bit float, mono or stereo (stereo is
// mixed to mono). Paths count from the working folder like the program's other files.
public static class SampleBank
{
    private static readonly ConcurrentDictionary<string, SampleData?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // The sample, or null if the file is missing or not a WAV file it can read
    public static SampleData? Get(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        return Cache.GetOrAdd(file, f =>
        {
            try
            {
                string path;
                try { path = ResourceLoader.GetPath(f); }
                catch (FileNotFoundException) { path = f; }
                return File.Exists(path) ? ReadWav(File.ReadAllBytes(path)) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not read the sample {f}: {ex.Message}");
                return null;
            }
        });
    }

    // Forgets a file, so that a changed one is read again
    public static void Forget(string file) => Cache.TryRemove(file, out _);

    public static SampleData ReadWav(byte[] bytes)
    {
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            throw new InvalidDataException("not a WAV file");

        int format = 0, channels = 0, rate = 0, bits = 0;
        int pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            string id = Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;
            if (size < 0 || body + size > bytes.Length) size = bytes.Length - body;

            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(bytes, body);
                channels = BitConverter.ToUInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToUInt16(bytes, body + 14);
                if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(bytes, body + 24);   // extensible: the real format
            }
            else if (id == "data")
            {
                if (channels < 1 || rate < 1 || bits < 8) throw new InvalidDataException("the WAV file has no format chunk");
                return new SampleData(Decode(bytes, body, size, format, channels, bits), rate);
            }
            pos = body + size + (size & 1);   // chunks are padded to an even size
        }
        throw new InvalidDataException("the WAV file has no sound data");
    }

    private static float[] Decode(byte[] b, int start, int size, int format, int channels, int bits)
    {
        int bytesPer = bits / 8, frameBytes = bytesPer * channels;
        int frames = size / frameBytes;
        var data = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int o = start + f * frameBytes + c * bytesPer;
                sum += (format, bits) switch
                {
                    (3, 32) => BitConverter.ToSingle(b, o),
                    (3, 64) => BitConverter.ToDouble(b, o),
                    (_, 8) => (b[o] - 128) / 128.0,
                    (_, 16) => BitConverter.ToInt16(b, o) / 32768.0,
                    (_, 24) => ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16))) / 8388608.0,
                    (_, 32) => BitConverter.ToInt32(b, o) / 2147483648.0,
                    _ => throw new InvalidDataException($"{bits}-bit WAV files are not supported")
                };
            }
            data[f] = (float)(sum / channels);
        }
        return data;
    }
}
