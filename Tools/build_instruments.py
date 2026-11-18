#!/usr/bin/env python3
"""Writes the tracker's instrument library: Sound/Instruments/<Category>/<Name>.amosinst

    python3 Tools/build_instruments.py

Each file is one tracker instrument as JSON, the same as in a song file. The tracker's LIBRARY
window lists the folders as categories; your own instruments can be saved there too.
Units: ms for times, % for sustain, pulse width, resonance and mix, Hz for cutoff and rates,
cents for vibrato and detune. An LFO's depth is cents (pitch), semitones (cutoff) or %.
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Sound", "Instruments")


def lfo(target, shape="Sine", depth=0, rate=4, sync=0, delay=0, retrigger=True):
    return {"Target": target, "Shape": shape, "Depth": depth, "Rate": rate,
            "SyncRows": sync, "Delay": delay, "Retrigger": retrigger}


def inst(name, **kw):
    d = {"Name": name}
    d.update(kw)
    return d


LIBRARY = {
    "Bass": [
        inst("Acid Bass", Wave="Sawtooth", Volume=0.26, Attack=1, Decay=0, Sustain=100, Release=40,
             Filter="Low", Cutoff=260, Resonance=78, FilterEnvDepth=2600, FilterEnvAttack=1, FilterEnvDecay=180),
        inst("Fat Saw Bass", Wave="Sawtooth", Volume=0.24, Attack=2, Decay=300, Sustain=80, Release=60,
             Filter="Low", Cutoff=700, Resonance=30, FilterEnvDepth=1200, FilterEnvAttack=2, FilterEnvDecay=250,
             Osc2Wave="Sawtooth", Osc2Semitones=-12, Osc2Detune=6, Osc2Mix=45),
        inst("Sub Bass", Wave="Sine", Volume=0.38, Attack=4, Decay=0, Sustain=100, Release=80,
             Osc2Wave="Triangle", Osc2Semitones=12, Osc2Mix=20),
        inst("Pulse Bass", Wave="Pulse", Volume=0.24, PulseWidth=22, Attack=1, Decay=200, Sustain=70, Release=50,
             Filter="Low", Cutoff=1400, Resonance=20),
        inst("Wobble Bass", Wave="Sawtooth", Volume=0.25, Attack=2, Decay=0, Sustain=100, Release=60,
             Filter="Low", Cutoff=500, Resonance=65, Osc2Wave="Square", Osc2Semitones=-12, Osc2Mix=40,
             Lfo1=lfo("Cutoff", "Sine", depth=30, sync=2)),
        inst("C64 Bass", Wave="Pulse", Volume=0.24, PulseWidth=40, PwmDepth=20, PwmRate=3, Attack=1, Decay=120,
             Sustain=60, Release=40),
        inst("Pluck Bass", Wave="Triangle", Volume=0.5, Attack=0, Decay=220, Sustain=0, Release=60,
             Osc2Wave="Square", Osc2Mix=25, Filter="Low", Cutoff=900, FilterEnvDepth=2200, FilterEnvDecay=90),
        inst("Reese Bass", Wave="Sawtooth", Volume=0.23, Attack=10, Decay=0, Sustain=100, Release=120,
             Filter="Low", Cutoff=900, Resonance=15, Osc2Wave="Sawtooth", Osc2Detune=14, Osc2Mix=50,
             Lfo1=lfo("Osc 2 pitch", "Sine", depth=10, rate=0.3, retrigger=False)),
    ],
    "Lead": [
        inst("Supersaw Lead", Wave="Sawtooth", Volume=0.2, Attack=5, Decay=200, Sustain=80, Release=180,
             Osc2Wave="Sawtooth", Osc2Detune=12, Osc2Mix=50, Filter="Low", Cutoff=5000, Resonance=10,
             Lfo1=lfo("Pitch", "Sine", depth=14, rate=5.5, delay=350)),
        inst("Chip Lead", Wave="Pulse", Volume=0.2, PulseWidth=30, PwmDepth=12, PwmRate=1.5, Attack=5, Decay=150,
             Sustain=60, Release=120, Lfo1=lfo("Pitch", "Sine", depth=18, rate=6, delay=250)),
        inst("Square Lead", Wave="Square", Volume=0.18, Attack=3, Decay=100, Sustain=80, Release=120,
             Osc2Wave="Square", Osc2Semitones=12, Osc2Detune=4, Osc2Mix=30,
             Lfo1=lfo("Pitch", "Sine", depth=12, rate=5, delay=300)),
        inst("Sync Lead", Wave="Sawtooth", Volume=0.2, Attack=2, Decay=250, Sustain=70, Release=150,
             Osc2Wave="Sawtooth", Osc2Semitones=7, Osc2Mix=40, Filter="Low", Cutoff=3000, Resonance=35,
             Lfo1=lfo("Osc 2 pitch", "Triangle", depth=700, rate=0.8), Lfo2=lfo("Pitch", depth=10, rate=6, delay=300)),
        inst("Sine Whistle", Wave="Sine", Volume=0.3, Attack=40, Decay=0, Sustain=100, Release=200,
             Lfo1=lfo("Pitch", "Sine", depth=20, rate=5, delay=200)),
        inst("Screamer", Wave="Pulse", Volume=0.18, PulseWidth=15, Attack=2, Decay=0, Sustain=100, Release=100,
             Filter="Band", Cutoff=1800, Resonance=75, Lfo1=lfo("Cutoff", "Triangle", depth=18, rate=3),
             Lfo2=lfo("Pitch", depth=25, rate=6.5, delay=150)),
        inst("Fifth Lead", Wave="Sawtooth", Volume=0.18, Attack=5, Decay=200, Sustain=75, Release=150,
             Osc2Wave="Sawtooth", Osc2Semitones=7, Osc2Detune=3, Osc2Mix=45, Filter="Low", Cutoff=4000),
    ],
    "Pad": [
        inst("Warm Pad", Wave="Sawtooth", Volume=0.2, Attack=800, Decay=600, Sustain=80, Release=1200,
             Filter="Low", Cutoff=1200, Resonance=15, Osc2Wave="Sawtooth", Osc2Detune=9, Osc2Mix=50,
             Lfo1=lfo("Cutoff", "Sine", depth=8, rate=0.25, retrigger=False)),
        inst("Glass Pad", Wave="Triangle", Volume=0.16, Attack=600, Decay=0, Sustain=100, Release=1500,
             Osc2Wave="Sine", Osc2Semitones=12, Osc2Detune=5, Osc2Mix=40,
             Lfo1=lfo("Osc 2 mix", "Sine", depth=30, rate=0.4, retrigger=False)),
        inst("PWM Pad", Wave="Pulse", Volume=0.14, PulseWidth=50, Attack=700, Decay=0, Sustain=100, Release=1200,
             Filter="Low", Cutoff=2500, Lfo1=lfo("Pulse width", "Triangle", depth=35, rate=0.6, retrigger=False)),
        inst("Dark Pad", Wave="Sawtooth", Volume=0.13, Attack=1200, Decay=0, Sustain=100, Release=2000,
             Filter="Low", Cutoff=500, Resonance=30, Osc2Wave="Square", Osc2Semitones=-12, Osc2Detune=7, Osc2Mix=40,
             Lfo1=lfo("Cutoff", "Triangle", depth=12, sync=16, retrigger=False)),
        inst("Choir Pad", Wave="Pulse", Volume=0.2, PulseWidth=35, Attack=500, Decay=0, Sustain=100, Release=1000,
             Filter="Band", Cutoff=900, Resonance=40, Osc2Wave="Pulse", Osc2Detune=10, Osc2Mix=50,
             Lfo1=lfo("Pitch", "Sine", depth=8, rate=4.5, delay=400)),
        inst("Space Drone", Wave="Sawtooth", Volume=0.12, Attack=2000, Decay=0, Sustain=100, Release=3000,
             Filter="Low", Cutoff=800, Resonance=60, Osc2Wave="Sawtooth", Osc2Semitones=7, Osc2Detune=6, Osc2Mix=35,
             Lfo1=lfo("Cutoff", "Sine", depth=20, rate=0.12, retrigger=False),
             Lfo2=lfo("Resonance", "Triangle", depth=25, rate=0.07, retrigger=False)),
    ],
    "Keys": [
        inst("E-Piano", Wave="Sine", Volume=0.3, Attack=1, Decay=900, Sustain=20, Release=300,
             Osc2Wave="Triangle", Osc2Semitones=12, Osc2Mix=25, Lfo1=lfo("Volume", "Sine", depth=15, rate=4.5)),
        inst("Organ", Wave="Sine", Volume=0.24, Attack=5, Decay=0, Sustain=100, Release=60,
             Osc2Wave="Sine", Osc2Semitones=12, Osc2Mix=40, Lfo1=lfo("Pitch", "Sine", depth=6, rate=6.5)),
        inst("Rock Organ", Wave="Square", Volume=0.16, Attack=3, Decay=0, Sustain=100, Release=60,
             Osc2Wave="Square", Osc2Semitones=19, Osc2Mix=30, Filter="Low", Cutoff=3000,
             Lfo1=lfo("Volume", "Sine", depth=25, rate=6)),
        inst("Clavinet", Wave="Pulse", Volume=0.2, PulseWidth=12, Attack=0, Decay=350, Sustain=10, Release=80,
             Filter="High", Cutoff=300, Resonance=20),
        inst("Toy Piano", Wave="Triangle", Volume=0.3, Attack=0, Decay=500, Sustain=0, Release=200,
             Osc2Wave="Sine", Osc2Semitones=24, Osc2Detune=10, Osc2Mix=30),
    ],
    "Pluck": [
        inst("Harp", Wave="Triangle", Volume=0.32, Attack=0, Decay=700, Sustain=0, Release=400,
             Osc2Wave="Sine", Osc2Semitones=12, Osc2Mix=20),
        inst("Saw Pluck", Wave="Sawtooth", Volume=0.22, Attack=0, Decay=300, Sustain=0, Release=200,
             Filter="Low", Cutoff=600, Resonance=35, FilterEnvDepth=4000, FilterEnvAttack=0, FilterEnvDecay=120),
        inst("Guitar Pick", Wave="Pulse", Volume=0.22, PulseWidth=25, Attack=0, Decay=600, Sustain=10, Release=150,
             Filter="Low", Cutoff=1200, FilterEnvDepth=2500, FilterEnvDecay=150, Osc2Wave="Sawtooth", Osc2Detune=4, Osc2Mix=30),
        inst("Marimba", Wave="Sine", Volume=0.36, Attack=0, Decay=350, Sustain=0, Release=150,
             Osc2Wave="Sine", Osc2Semitones=24, Osc2Mix=15),
        inst("Music Box", Wave="Sine", Volume=0.3, Attack=0, Decay=1000, Sustain=0, Release=600,
             Osc2Wave="Triangle", Osc2Semitones=24, Osc2Detune=6, Osc2Mix=35),
        inst("Chip Arp", Wave="Pulse", Volume=0.16, PulseWidth=25, Attack=0, Decay=400, Sustain=30, Release=150,
             Table=["0", "+4", "+7", "+12"], TableLoop=0),
    ],
    "Brass & Strings": [
        inst("Synth Brass", Wave="Sawtooth", Volume=0.2, Attack=60, Decay=300, Sustain=80, Release=150,
             Filter="Low", Cutoff=600, Resonance=20, FilterEnvDepth=2500, FilterEnvAttack=80, FilterEnvDecay=400,
             Osc2Wave="Sawtooth", Osc2Detune=7, Osc2Mix=45),
        inst("Brass Stab", Wave="Sawtooth", Volume=0.32, Attack=10, Decay=250, Sustain=0, Release=100,
             Filter="Low", Cutoff=800, FilterEnvDepth=3500, FilterEnvAttack=10, FilterEnvDecay=200,
             Osc2Wave="Sawtooth", Osc2Semitones=12, Osc2Detune=5, Osc2Mix=35),
        inst("Strings", Wave="Sawtooth", Volume=0.2, Attack=400, Decay=0, Sustain=100, Release=700,
             Filter="Low", Cutoff=2500, Osc2Wave="Sawtooth", Osc2Detune=11, Osc2Mix=50,
             Lfo1=lfo("Pitch", "Sine", depth=7, rate=5, delay=500)),
        inst("Pizzicato", Wave="Sawtooth", Volume=0.32, Attack=0, Decay=180, Sustain=0, Release=80,
             Filter="Low", Cutoff=1800, Osc2Wave="Triangle", Osc2Semitones=12, Osc2Mix=25),
        inst("Flute", Wave="Triangle", Volume=0.28, Attack=80, Decay=0, Sustain=100, Release=150,
             Osc2Wave="Noise", Osc2Mix=6, Lfo1=lfo("Pitch", "Sine", depth=10, rate=5, delay=300)),
    ],
    "Chip": [
        inst("NES Square", Wave="Pulse", Volume=0.18, PulseWidth=25, Attack=0, Decay=0, Sustain=100, Release=30),
        inst("NES Triangle", Wave="Triangle", Volume=0.34, Attack=0, Decay=0, Sustain=100, Release=20),
        inst("C64 Lead", Wave="Pulse", Volume=0.2, PulseWidth=20, Attack=2, Decay=200, Sustain=70, Release=150,
             Lfo1=lfo("Pulse width", "Triangle", depth=18, rate=2), Lfo2=lfo("Pitch", depth=20, rate=6, delay=200)),
        inst("Game Boy", Wave="Pulse", Volume=0.18, PulseWidth=12, Attack=0, Decay=250, Sustain=50, Release=50),
        inst("Major Chord", Wave="Pulse", Volume=0.12, PulseWidth=30, Attack=5, Decay=400, Sustain=40, Release=200,
             Table=["0", "+4", "+7"], TableLoop=0),
        inst("Minor Chord", Wave="Pulse", Volume=0.12, PulseWidth=30, Attack=5, Decay=400, Sustain=40, Release=200,
             Table=["0", "+3", "+7"], TableLoop=0),
        inst("Trill", Wave="Square", Volume=0.16, Attack=0, Decay=0, Sustain=100, Release=60,
             Lfo1=lfo("Pitch", "Square", depth=200, sync=1)),
    ],
    "Drums": [
        inst("Kick", Wave="Triangle", Volume=0.45, Attack=0, Decay=180, Sustain=0, Release=30,
             Table=["SQUARE +24", "TRIANGLE +12", "+6", "+2", "-2", "-5", "-8", "-10", "-12"]),
        inst("808 Kick", Wave="Sine", Volume=0.5, Attack=0, Decay=600, Sustain=0, Release=80,
             Table=["+24", "+14", "+8", "+4", "+2", "+1", "0"]),
        inst("Snare", Wave="Noise", Volume=0.3, Attack=0, Decay=160, Sustain=0, Release=40,
             Table=["NOISE +12", "TRIANGLE -5", "NOISE +10", "+8"]),
        inst("Clap", Wave="Noise", Volume=0.28, Attack=0, Decay=200, Sustain=0, Release=60,
             Filter="Band", Cutoff=1500, Resonance=30, Table=["NOISE +20", "+20", "+20", "+20", "+20", "+20"]),
        inst("Closed Hihat", Wave="Noise", Volume=0.16, Attack=0, Decay=40, Sustain=0, Release=20,
             Filter="High", Cutoff=7000),
        inst("Open Hihat", Wave="Noise", Volume=0.14, Attack=0, Decay=300, Sustain=0, Release=150,
             Filter="High", Cutoff=6000),
        inst("Tom", Wave="Triangle", Volume=0.4, Attack=0, Decay=300, Sustain=0, Release=60,
             Table=["NOISE +12", "TRIANGLE +7", "+5", "+3", "+2", "+1", "0", "-1", "-2"]),
        inst("Crash", Wave="Noise", Volume=0.15, Attack=0, Decay=1200, Sustain=0, Release=600,
             Filter="High", Cutoff=3500, Resonance=20),
    ],
    "Percussion": [
        inst("Cowbell", Wave="Square", Volume=0.15, Attack=0, Decay=250, Sustain=0, Release=60,
             Osc2Wave="Square", Osc2Semitones=7, Osc2Detune=30, Osc2Mix=50, Filter="Band", Cutoff=1200, Resonance=40),
        inst("Rim Shot", Wave="Pulse", Volume=0.32, PulseWidth=10, Attack=0, Decay=40, Sustain=0, Release=20,
             Filter="High", Cutoff=1500, Table=["NOISE +24", "PULSE +12"]),
        inst("Shaker", Wave="Noise", Volume=0.18, Attack=15, Decay=80, Sustain=0, Release=40,
             Filter="High", Cutoff=8000),
        inst("Wood Block", Wave="Sine", Volume=0.5, Attack=0, Decay=70, Sustain=0, Release=20,
             Osc2Wave="Triangle", Osc2Semitones=17, Osc2Mix=30),
        inst("Bongo", Wave="Sine", Volume=0.4, Attack=0, Decay=160, Sustain=0, Release=40,
             Table=["+7", "+4", "+2", "+1", "0"]),
    ],
    "FX": [
        inst("Laser", Wave="Sawtooth", Volume=0.2, Attack=0, Decay=300, Sustain=0, Release=50,
             Table=["+36", "+30", "+24", "+18", "+12", "+6", "0", "-6", "-12"]),
        inst("Sci-Fi Bleeps", Wave="Square", Volume=0.14, Attack=0, Decay=0, Sustain=100, Release=100,
             Lfo1=lfo("Pitch", "Random", depth=1200, rate=10)),
        inst("Siren", Wave="Triangle", Volume=0.3, Attack=50, Decay=0, Sustain=100, Release=300,
             Lfo1=lfo("Pitch", "Triangle", depth=700, rate=0.8)),
        inst("Wind", Wave="Noise", Volume=0.15, Attack=600, Decay=0, Sustain=100, Release=1200,
             Filter="Band", Cutoff=800, Resonance=60, Lfo1=lfo("Cutoff", "Sine", depth=18, rate=0.2, retrigger=False),
             Lfo2=lfo("Volume", "Sine", depth=40, rate=0.35, retrigger=False)),
        inst("Explosion", Wave="Noise", Volume=0.4, Attack=0, Decay=1200, Sustain=0, Release=400,
             Filter="Low", Cutoff=300, Resonance=20, FilterEnvDepth=5000, FilterEnvDecay=300,
             Table=["NOISE +12", "+6", "0", "-6", "-12"]),
        inst("Power Up", Wave="Square", Volume=0.15, Attack=0, Decay=0, Sustain=100, Release=80,
             Table=["0", "+4", "+7", "+12", "+16", "+19", "+24", "+28", "+31", "+36"]),
        inst("Ufo", Wave="Sine", Volume=0.3, Attack=100, Decay=0, Sustain=100, Release=400,
             Osc2Wave="Sine", Osc2Semitones=7, Osc2Mix=40, Lfo1=lfo("Pitch", "Sine", depth=300, rate=7),
             Lfo2=lfo("Osc 2 pitch", "Sine", depth=100, rate=0.5)),
    ],
}


def main():
    # Only these files are written (and overwritten); instruments you saved in the library yourself stay
    count = 0
    for category, instruments in LIBRARY.items():
        folder = os.path.join(OUT, category)
        os.makedirs(folder, exist_ok=True)
        for i in instruments:
            with open(os.path.join(folder, i["Name"] + ".amosinst"), "w", encoding="utf-8") as f:
                json.dump(i, f, indent=2)
            count += 1
    print(f"wrote {count} instruments in {len(LIBRARY)} categories to {OUT}")


if __name__ == "__main__":
    main()
