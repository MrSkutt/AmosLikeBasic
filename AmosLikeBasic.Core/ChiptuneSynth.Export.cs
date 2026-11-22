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
        // Stereo: left and right in turn.
        public static float[] RenderSong(TrackerSong song, double maxSeconds = 600, double tailSeconds = 3,
                                         Action<double>? progress = null, CancellationToken token = default)
        {
            bool was = ManualRendering;
            ManualRendering = true;
            Thread.Sleep(30);   // the sound card's last buffer finishes
            var chunk = new float[SampleRate / 10 * 2];
            var all = new List<float>(SampleRate * 60);
            try
            {
                Reset();
                RenderStereo(chunk);
                TrackPlay(song, loop: false);
                do
                {
                    token.ThrowIfCancellationRequested();
                    RenderStereo(chunk);
                    all.AddRange(chunk);
                    if (all.Count % (SampleRate * 2) < chunk.Length) progress?.Invoke(all.Count / 2.0 / SampleRate);
                } while (IsTrackPlaying() && all.Count < maxSeconds * SampleRate * 2);

                TrackStop();
                for (int i = 0; i < tailSeconds * 10; i++)
                {
                    RenderStereo(chunk);
                    all.AddRange(chunk);
                }
            }
            finally
            {
                Reset();
                RenderStereo(chunk);
                ManualRendering = was;
            }

            int end = all.Count;
            while (end > 0 && Math.Abs(all[end - 1]) < 0.0005f) end--;
            end = Math.Min(all.Count, end + SampleRate / 10);   // a little silence after the last sound
            end -= end % 2;                                     // whole left-right pairs
            return all.GetRange(0, end).ToArray();
        }

        // A note to play into a sample: the instrument, the note, how many ticks it is held, and the
        // effects the song gives it while it plays (the tick from its start, the effect, its parameter)
        public sealed record NoteJob(TrackerInstrument Instrument, int Note, int Ticks,
                                     IReadOnlyList<(int Tick, char Effect, int Param)>? Changes = null);

        // Single notes of instruments, each held for its ticks at the tempo and cut there (mono, from the
        // middle; the instrument's pan, echo send and all else as it is). For turning the synth's
        // instruments into samples. While it renders the synth belongs to it, as for RenderSong.
        public static List<float[]> RenderNotes(IReadOnlyList<NoteJob> notes, int tempo, Action<double>? progress = null)
        {
            bool was = ManualRendering;
            ManualRendering = true;
            Thread.Sleep(30);
            var result = new List<float[]>();
            double perTick = SampleRate / (Math.Clamp(tempo, 32, 255) * 0.4);
            try
            {
                for (int i = 0; i < notes.Count; i++)
                {
                    var (inst, note, ticks, changes) = notes[i];
                    ticks = Math.Max(1, ticks);
                    // A song of one channel: the note on the first row, a tick a row, long enough to hold it
                    var song = new TrackerSong { Speed = 1, Tempo = tempo, Channels = TrackerSong.MinChannels };
                    song.Instruments.Add(inst);
                    song.Patterns.Clear();
                    song.Order.Clear();
                    for (int rows = ticks + 1; rows > 0; rows -= TrackerSong.MaxRows)
                    {
                        song.Order.Add(song.Patterns.Count);
                        song.Patterns.Add(new TrackerPattern(Math.Min(rows, TrackerSong.MaxRows)));
                    }
                    song.Patterns[0][0, 0] = new TrackerCell(note, 1, '\0', 0);
                    foreach (var (tick, effect, param) in changes ?? Array.Empty<(int, char, int)>())
                    {
                        if (tick > ticks) continue;
                        var cell = song.Patterns[tick / TrackerSong.MaxRows][tick % TrackerSong.MaxRows, 0];
                        song.Patterns[tick / TrackerSong.MaxRows][tick % TrackerSong.MaxRows, 0] = new TrackerCell(cell.Note, cell.Instrument, effect, param);
                    }
                    Reset();
                    Render(new float[64]);
                    TrackPlay(song, loop: false);
                    var data = new float[(int)Math.Round(ticks * perTick)];
                    Render(data);
                    TrackStop();
                    result.Add(data);
                    progress?.Invoke((i + 1) / (double)notes.Count);
                }
            }
            finally
            {
                Reset();
                Render(new float[64]);
                ManualRendering = was;
            }
            return result;
        }

        // A WAV file: 16-bit stereo (left and right in turn) at the synth's sample rate
        public static void WriteWav(string path, float[] samples)
        {
            const short channels = 2;
            using var w = new BinaryWriter(File.Create(path));
            int bytes = samples.Length * 2;
            w.Write("RIFF"u8);
            w.Write(36 + bytes);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1);              // PCM
            w.Write(channels);
            w.Write(SampleRate);
            w.Write(SampleRate * 2 * channels);   // bytes a second
            w.Write((short)(2 * channels));       // bytes a frame
            w.Write((short)16);             // bits
            w.Write("data"u8);
            w.Write(bytes);
            foreach (var s in samples)
                w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * 32767));
        }
    }
}
