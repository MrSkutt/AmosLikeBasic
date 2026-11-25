using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic
{
    // The tempo of a sound in parts (a sample sequence): how many beats a part is. A part is taken to be a
    // whole number of beats, so the beat is the part's length divided by 2, 3, 4 … and the one whose length
    // the sound repeats at the most (where its attacks come again: the autocorrelation of how much louder its
    // treble gets, 100 times a second) is the beat, between 70 and 180 BPM; whole bars of 4/4 are the
    // likelier. One part alone will do: a single sample, as a loop.
    public static class SampleTempo
    {
        public sealed record Result(double Bpm, int Beats, double Seconds);

        public static Result? Detect(IReadOnlyList<SampleData> parts, int maxParts = 8)
        {
            var use = parts.Where(p => p.Data.Length > 0).Take(maxParts).ToList();
            if (use.Count == 0) return null;
            double seconds = use[0].Seconds;
            // How much louder the treble (each sample less the one before: hats, snares, attacks) gets in each
            // 10 ms than in the one before, on a log scale, all parts after each other. (5 ms, or the whole
            // sound, would follow a bass's own waves.)
            const double hopSeconds = 0.01;
            var onset = new List<double>();
            foreach (var p in use)
            {
                int hop = Math.Max(2, (int)Math.Round(p.Rate * hopSeconds));
                double before = 0;
                for (int i = 1; i + hop <= p.Data.Length; i += hop)
                {
                    double energy = 0;
                    for (int j = i; j < i + hop; j++) { double d = p.Data[j] - p.Data[j - 1]; energy += d * d; }
                    double level = Math.Log(1 + 1000 * energy / hop);
                    onset.Add(Math.Max(0, level - before));
                    before = level;
                }
            }
            if (onset.Count < 8) return null;
            double mean = onset.Average();
            var x = onset.Select(v => v - mean).ToArray();
            double total = x.Sum(v => v * v);
            if (total <= 0) return null;
            double At(int lag)
            {
                if (lag <= 0 || lag >= x.Length) return 0;
                double sum = 0;
                for (int i = 0; i + lag < x.Length; i++) sum += x[i] * x[i + lag];
                return sum / (x.Length - lag) * x.Length / total;
            }
            // (between two whole lags, in proportion: a tempo finer than the 10 ms steps)
            double Correlation(double lagSeconds)
            {
                double lag = lagSeconds / hopSeconds;
                int low = (int)lag;
                double t = lag - low;
                return At(low) * (1 - t) + At(low + 1) * t;
            }

            Result? best = null;
            double bestScore = double.MinValue;
            // A long sound (a whole song, more than 64 beats) need not be a whole number of beats: every tempo
            // from 70 to 180 BPM in steps of 0.1 is tried, and the beats are as many as fit
            if (seconds * 70 / 60 > 64)
            {
                for (double bpm = 70; bpm <= 180; bpm += 0.1)
                {
                    double beat = 60 / bpm;
                    double score = Enumerable.Range(1, 4).Average(k => Correlation(k * beat)) + 0.5 * Correlation(beat / 2);
                    if (score > bestScore) { bestScore = score; best = new Result(Math.Round(bpm, 1), (int)Math.Round(seconds / beat), seconds); }
                }
                return best;
            }
            for (int beats = 1; beats <= 64; beats++)
            {
                double beat = seconds / beats, bpm = 60 / beat;
                if (bpm < 70 || bpm > 180) continue;
                // A real beat repeats at 1, 2, 3 and 4 beats, and has something between (the eighths). Parts are
                // most often whole bars of 4/4 (a part of 7 beats is rare), so 4, 8, 16 … beats come first.
                double likely = beats % 4 == 0 ? 1.0 : beats % 2 == 0 ? 0.8 : 0.6;
                var lags = Enumerable.Range(1, 4).Where(k => k * beat < seconds * 0.75).DefaultIfEmpty(1);
                double score = (lags.Average(k => Correlation(k * beat)) + 0.5 * Correlation(beat / 2)) * likely;
                if (score > bestScore) { bestScore = score; best = new Result(bpm, beats, seconds); }
            }
            return best;
        }
    }
}
