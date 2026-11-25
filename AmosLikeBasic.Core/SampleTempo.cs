using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic
{
    // The tempo of a sound in parts (a sample sequence): how many beats a part is. A part is taken to be a
    // whole number of beats, so the beat is the part's length divided by 2, 3, 4 … and the one whose length
    // the sound repeats at the most (where its attacks come again: the autocorrelation of how much louder
    // it gets, 200 times a second) is the beat, between 70 and 180 BPM; whole bars of 4/4 are the likelier.
    public static class SampleTempo
    {
        public sealed record Result(double Bpm, int Beats, double Seconds);

        public static Result? Detect(IReadOnlyList<SampleData> parts, int maxParts = 8)
        {
            var use = parts.Where(p => p.Data.Length > 0).Take(maxParts).ToList();
            if (use.Count == 0) return null;
            double seconds = use[0].Seconds;
            const double hopSeconds = 0.005;
            // How much louder each 5 ms is than the one before (the attacks), all parts after each other
            var onset = new List<double>();
            foreach (var p in use)
            {
                int hop = Math.Max(1, (int)Math.Round(p.Rate * hopSeconds));
                double before = 0;
                for (int i = 0; i + hop <= p.Data.Length; i += hop)
                {
                    double level = 0;
                    for (int j = i; j < i + hop; j++) level += Math.Abs(p.Data[j]);
                    level /= hop;
                    onset.Add(Math.Max(0, level - before));
                    before = level;
                }
            }
            double mean = onset.Average();
            var x = onset.Select(v => v - mean).ToArray();
            double energy = x.Sum(v => v * v);
            if (energy <= 0) return null;
            double Correlation(double lagSeconds)
            {
                int lag = (int)Math.Round(lagSeconds / hopSeconds);
                if (lag <= 0 || lag >= x.Length) return 0;
                double sum = 0;
                for (int i = 0; i + lag < x.Length; i++) sum += x[i] * x[i + lag];
                return sum / (x.Length - lag) * x.Length / energy;
            }

            Result? best = null;
            double bestScore = double.MinValue;
            for (int beats = 1; beats <= 64; beats++)
            {
                double beat = seconds / beats, bpm = 60 / beat;
                if (bpm < 70 || bpm > 180) continue;
                // The beat and its double: a real beat repeats at both. Parts are most often whole bars of 4/4
                // (a part of 7 beats is rare), so 4, 8, 16 … beats are taken before others
                double likely = beats % 4 == 0 ? 1.0 : beats % 2 == 0 ? 0.8 : 0.6;
                double score = (Correlation(beat) + 0.5 * Correlation(2 * beat)) * likely;
                if (score > bestScore) { bestScore = score; best = new Result(bpm, beats, seconds); }
            }
            return best;
        }
    }
}
