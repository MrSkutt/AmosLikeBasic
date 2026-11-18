using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AmosLikeBasic
{
    // A tracker song rendered to a sound file, faster than it plays, for using the music outside
    // the games. While it renders the synth belongs to the export: what is playing goes quiet.
    public partial class ChiptuneSynth
    {
        // The song once through (or at most maxSeconds), and then tailSeconds for the last notes'
        // release and echoes; silence at the end is trimmed. progress gets the seconds done.
        public static float[] RenderSong(TrackerSong song, double maxSeconds = 600, double tailSeconds = 3,
                                         Action<double>? progress = null, CancellationToken token = default)
        {
            bool was = ManualRendering;
            ManualRendering = true;
            Thread.Sleep(30);   // the sound card's last buffer finishes
            var chunk = new float[SampleRate / 10];
            var all = new List<float>(SampleRate * 60);
            try
            {
                Reset();
                Render(chunk);
                TrackPlay(song, loop: false);
                do
                {
                    token.ThrowIfCancellationRequested();
                    Render(chunk);
                    all.AddRange(chunk);
                    if (all.Count % SampleRate < chunk.Length) progress?.Invoke(all.Count / (double)SampleRate);
                } while (IsTrackPlaying() && all.Count < maxSeconds * SampleRate);

                TrackStop();
                for (int i = 0; i < tailSeconds * 10; i++)
                {
                    Render(chunk);
                    all.AddRange(chunk);
                }
            }
            finally
            {
                Reset();
                Render(chunk);
                ManualRendering = was;
            }

            int end = all.Count;
            while (end > 0 && Math.Abs(all[end - 1]) < 0.0005f) end--;
            end = Math.Min(all.Count, end + SampleRate / 20);   // a little silence after the last sound
            return all.GetRange(0, end).ToArray();
        }

        // A WAV file: 16-bit mono at the synth's sample rate
        public static void WriteWav(string path, float[] samples)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = samples.Length * 2;
            w.Write("RIFF"u8);
            w.Write(36 + bytes);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1);              // PCM
            w.Write((short)1);              // mono
            w.Write(SampleRate);
            w.Write(SampleRate * 2);        // bytes a second
            w.Write((short)2);              // bytes a sample
            w.Write((short)16);             // bits
            w.Write("data"u8);
            w.Write(bytes);
            foreach (var s in samples)
                w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * 32767));
        }
    }
}
