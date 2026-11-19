#!/usr/bin/env python3
"""Makes the samples for the tracker's sampled instruments: Sound/Samples/*.wav

    python3 Tools/build_samples.py

They are made from maths, so they are free to use:
    pluck.wav   a plucked string (Karplus-Strong), A-3 (note 57)
    bell.wav    a bell (FM synthesis), C-5 (note 72)
    choir.wav   a choir singing "aah" (harmonics shaped by formants), A-3, a perfect loop
16-bit mono, 44 100 Hz.
"""
import math
import os
import random
import struct

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Sound", "Samples")
RATE = 44100


def write_wav(name, samples):
    peak = max(1e-9, max(abs(s) for s in samples))
    data = b"".join(struct.pack("<h", int(max(-1, min(1, s / peak * 0.9)) * 32767)) for s in samples)
    header = (b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVE" +
              b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, RATE, RATE * 2, 2, 16) +
              b"data" + struct.pack("<I", len(data)))
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(header + data)
    print(f"wrote {name}: {len(samples)} frames, {len(samples) / RATE:.2f} s")


def pluck(freq=220.0, seconds=1.8):
    """Karplus-Strong: a burst of noise in a delay line, averaged a little each lap."""
    rnd = random.Random(5)
    n = int(round(RATE / freq))
    line = [rnd.uniform(-1, 1) for _ in range(n)]
    out = []
    for i in range(int(seconds * RATE)):
        a = line[i % n]
        b = line[(i + 1) % n]
        line[i % n] = 0.996 * 0.5 * (a + b)
        out.append(a)
    return out


def bell(freq=523.25, seconds=2.2):
    """FM: a carrier with a modulator at 1.4 × its pitch, the brightness dying away faster than the sound."""
    out = []
    for i in range(int(seconds * RATE)):
        t = i / RATE
        index = 4.0 * math.exp(-t * 2.2)
        amp = math.exp(-t * 1.6)
        out.append(amp * math.sin(2 * math.pi * freq * t + index * math.sin(2 * math.pi * freq * 1.4 * t)))
    return out


def choir(period=200, cycles=220):
    """A vowel "aah": harmonics of one pitch, loud near the formants 800, 1150 and 2900 Hz.
    A whole number of periods, so the end joins the start without a click (the loop)."""
    freq = RATE / period   # 220.5 Hz, A-3 a few cents sharp
    formants = [(800, 80, 1.0), (1150, 90, 0.5), (2900, 120, 0.25)]
    harmonics = []
    for h in range(1, int(5000 / freq)):
        f = h * freq
        gain = sum(a / (1 + ((f - fc) / bw) ** 2) for fc, bw, a in formants) + 0.02 / h
        harmonics.append((h, gain, random.Random(h).uniform(0, 2 * math.pi)))
    one = [sum(g * math.sin(2 * math.pi * h * i / period + ph) for h, g, ph in harmonics) for i in range(period)]
    return one * cycles


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    write_wav("pluck.wav", pluck())
    write_wav("bell.wav", bell())
    write_wav("choir.wav", choir())
