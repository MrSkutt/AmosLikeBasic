#!/usr/bin/env python3
"""Writes three tracker songs made with the instrument library (Sound/Instruments), to show off the
second oscillator and the LFOs:

    python3 Tools/build_songs.py

    Sound/Tracker/acid_sunrise.amostrk   acid techno: a filter-swept acid bass, supersaw lead, 808 drums
    Sound/Tracker/deep_space.amostrk     ambient: space drone, glass pad, music box, UFO sounds
    Sound/Tracker/pixel_rush.amostrk     fast chiptune: C64 lead, chord arpeggios, NES bass, trills
    Sound/Tracker/steel_sprint.amostrk   in the style of 1980s C64 game music: a bouncing octave bass,
                                         fast chord arpeggios, a gliding PWM lead, four channels
    Sound/Tracker/glass_labyrinth.amostrk  the same style, slower and mysterious, with echo

A row is a sixteenth note; 16 rows make a bar and a pattern is four bars (64 rows).
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
LIB = os.path.join(ROOT, "Sound", "Instruments")
OUT = os.path.join(ROOT, "Sound", "Tracker")

NAMES = ["C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-"]
ROWS = 64


def note(n):
    """A note number (MIDI: 60 is C-4 here, as the tracker counts) as text."""
    return f"{NAMES[n % 12]}{n // 12 - 1}"


def n(name):
    """'A-4' or 'C#5' as a note number."""
    return (int(name[2]) + 1) * 12 + NAMES.index(name[:2])


def load(category, name, **changes):
    with open(os.path.join(LIB, category, name + ".amosinst"), encoding="utf-8") as f:
        inst = json.load(f)
    inst.update(changes)
    return inst


class Pattern:
    def __init__(self, channels):
        self.cells = [["--- .. ..." for _ in range(channels)] for _ in range(ROWS)]

    def put(self, row, ch, text, inst=0, fx="..."):
        if 0 <= row < ROWS:
            self.cells[row][ch] = f"{text} {inst:02X} {fx}" if inst else f"{text} .. {fx}"

    def play(self, row, ch, number, inst, fx="..."):
        self.put(row, ch, note(number), inst, fx)

    def off(self, row, ch):
        if 0 <= row < ROWS and self.cells[row][ch].startswith("---"):
            self.cells[row][ch] = "=== .. " + self.cells[row][ch][7:]

    def fx(self, row, ch, fx):
        c = self.cells[row][ch]
        self.cells[row][ch] = c[:7] + fx

    def rows(self):
        return ["|".join(r) for r in self.cells]


def melody(p, ch, inst, notes, transpose=0, fx="..."):
    """notes: (row, 'A-4') pairs; each note lasts until the next one, '===' lets go."""
    for row, name in notes:
        if name == "===":
            p.off(row, ch)
        else:
            p.play(row, ch, n(name) + transpose, inst, fx)


def chords(p, chs, inst, progression, octave_rows=16, hold=True, vol="..."):
    """One chord a bar: each channel in chs gets one note of it, held for the bar."""
    for bar, chord in enumerate(progression):
        for ch, name in zip(chs, chord):
            p.play(bar * octave_rows, ch, n(name), inst, vol)
        if not hold:
            for ch in chs:
                p.off(bar * octave_rows + octave_rows - 2, ch)


def drums(p, ch, inst, steps, vol="...", bars=4):
    """steps: a 16 character string per bar, x = hit."""
    for bar in range(bars):
        for i, s in enumerate(steps):
            if s == "x":
                p.put(bar * 16 + i, ch, "C-4", inst, vol)


def song(title, speed, tempo, instruments, patterns, order, channels=8, level=1.0):
    """level scales every instrument, so the song is about as loud as the others (not clipping)."""
    for i in instruments:
        i["Volume"] = round(i.get("Volume", 0.3) * level, 3)
    return {
        "Format": "AmosTracker", "Version": 1, "Title": title, "Speed": speed, "Tempo": tempo,
        "Channels": channels, "Instruments": instruments,
        "Order": order, "Patterns": [p.rows() for p in patterns],
    }


# ════════════════════════════════════════════════════════════════════════
#  ACID SUNRISE  (A minor, Am F C G)
# ════════════════════════════════════════════════════════════════════════
def acid_sunrise():
    I = [
        load("Bass", "Acid Bass"),                                    # 1
        load("Lead", "Supersaw Lead", Volume=0.16, EchoMix=25, EchoSyncRows=3, EchoFeedback=35),   # 2
        load("Pad", "Stereo Pad", Volume=0.12),                       # 3  drifts between the sides
        load("Drums", "808 Kick", Volume=0.3),                        # 4
        load("Drums", "Clap", Pan=-20),                               # 5
        load("Drums", "Closed Hihat", Pan=35),                        # 6
        load("Drums", "Open Hihat", Pan=-35),                         # 7
        load("FX", "Sci-Fi Bleeps", Volume=0.08, Pan=60),             # 8
        load("Bass", "Wobble Bass"),                                  # 9
    ]
    roots = ["A-1", "F-1", "C-2", "G-1"]
    pad = [("A-3", "C-4", "E-4"), ("F-3", "A-3", "C-4"), ("E-3", "G-3", "C-4"), ("D-3", "G-3", "B-3")]
    bass_steps = "R.RO.RFR.RO.RFRO"   # R root, O octave up, F fifth

    def bass(p, inst=1, sweep=None):
        for bar, root in enumerate(roots):
            r = n(root)
            for i, s in enumerate(bass_steps):
                if s == ".":
                    continue
                num = r + {"R": 0, "O": 12, "F": 7}[s]
                row = bar * 16 + i
                fx = "..."
                if sweep is not None and i % 4 == 0:
                    # The filter opens bar by bar (G sets the cutoff and stays on the channel)
                    fx = f"G{min(0xF0, sweep + row * 2):02X}"
                p.play(row, 0, num, inst, fx)

    def beat(p, full=True):
        drums(p, 3, 4, "x...x...x...x...")
        if full:
            drums(p, 4, 5, "....x.......x...")
        drums(p, 5, 6, "..x...x...x...x." if full else "..x...x...x...x.")
        if full:
            drums(p, 6, 7, "..............x.")

    lead_a = [(0, "A-4"), (6, "C-5"), (8, "E-5"), (12, "D-5"), (16, "C-5"), (22, "A-4"), (24, "F-4"), (28, "A-4"),
              (32, "G-4"), (38, "C-5"), (40, "E-5"), (44, "G-5"), (48, "D-5"), (54, "B-4"), (56, "G-4"), (60, "B-4")]
    lead_b = [(0, "E-5"), (4, "A-5"), (8, "G-5"), (12, "E-5"), (16, "F-5"), (20, "E-5"), (24, "C-5"), (28, "A-4"),
              (32, "E-5"), (36, "G-5"), (40, "C-6"), (44, "B-5"), (48, "B-5"), (52, "A-5"), (56, "G-5"), (60, "===")]

    intro = Pattern(8)
    bass(intro, sweep=0x18)
    drums(intro, 3, 4, "x...x...x...x...")
    drums(intro, 5, 6, "..x...x...x...x.")

    groove = Pattern(8)
    bass(groove, sweep=0x60)
    beat(groove)
    chords(groove, [1, 2], 3, [(a, b) for a, b, _ in pad])

    verse = Pattern(8)
    bass(verse)
    beat(verse)
    melody(verse, 1, 2, lead_a)
    chords(verse, [2, 7], 3, [(a, c) for a, _, c in pad])

    chorus = Pattern(8)
    bass(chorus)
    beat(chorus)
    melody(chorus, 1, 2, lead_b)
    chords(chorus, [2, 7], 3, [(a, c) for a, _, c in pad])
    for row in range(2, 64, 8):
        chorus.put(row, 6, "C-6", 8) if row % 16 == 10 else None

    # Breakdown: no kick, the wobble bass (an LFO on its cutoff in step with the rows) and bleeps
    breakdown = Pattern(8)
    for bar, root in enumerate(roots):
        breakdown.play(bar * 16, 0, n(root) + 12, 9)
        breakdown.off(bar * 16 + 14, 0)
    chords(breakdown, [1, 2, 7], 3, pad)
    for row in range(0, 64, 16):
        breakdown.play(row + 8, 6, n("A-5"), 8)
        breakdown.off(row + 12, 6)
    drums(breakdown, 5, 6, "..x...x...x...x.")
    drums(breakdown, 4, 5, "................", bars=3)
    for i in range(48, 64, 2):
        breakdown.put(i, 4, "C-4", 5)   # a clap roll into the next part

    return song("Acid Sunrise", 6, 128, I, [intro, groove, verse, chorus, breakdown], [0, 1, 2, 3, 4, 2, 3, 1], level=0.72)


# ════════════════════════════════════════════════════════════════════════
#  DEEP SPACE  (D minor, Dm Bb F C) – slow, lets the pads and LFOs breathe
# ════════════════════════════════════════════════════════════════════════
def deep_space():
    I = [
        load("Pad", "Space Drone", Volume=0.11),                      # 1
        load("Pad", "Glass Pad", Volume=0.13),                        # 2
        load("Pluck", "Music Box", Volume=0.22, EchoMix=30, EchoSyncRows=6, EchoFeedback=40,
             Lfo2={"Target": "Pan", "Shape": "Sine", "Depth": 60, "Rate": 0.15, "SyncRows": 0, "Delay": 0, "Retrigger": False}),   # 3
        load("Bass", "Sub Bass", Volume=0.3),                         # 4
        load("FX", "Ufo", Volume=0.14),                               # 5
        load("FX", "Wind", Volume=0.12),                              # 6
        load("Percussion", "Shaker", Volume=0.12, Pan=40),            # 7
        load("Percussion", "Wood Block", Volume=0.25, Pan=-40),       # 8
        load("Lead", "Sine Whistle", Volume=0.2),                     # 9
    ]
    roots = ["D-2", "A#1", "F-2", "C-2"]
    pad = [("D-4", "F-4", "A-4"), ("D-4", "F-4", "A#4"), ("C-4", "F-4", "A-4"), ("C-4", "E-4", "G-4")]
    arps = [["D-5", "F-5", "A-5", "D-6"], ["D-5", "F-5", "A#5", "D-6"], ["C-5", "F-5", "A-5", "C-6"], ["C-5", "E-5", "G-5", "C-6"]]

    def drone(p):
        p.play(0, 0, n("D-3"), 1)

    def bass(p):
        for bar, root in enumerate(roots):
            p.play(bar * 16, 3, n(root), 4)
            p.off(bar * 16 + 13, 3)

    def music_box(p, every=2):
        for bar, notes in enumerate(arps):
            for i in range(0, 16, every):
                p.play(bar * 16 + i, 2, n(notes[(i // every) % 4]), 3)

    opening = Pattern(8)
    drone(opening)
    opening.play(0, 5, n("C-4"), 6)
    opening.play(24, 4, n("A-4"), 5)
    opening.off(40, 4)
    chords(opening, [1], 2, [(c[0],) for c in pad])

    rise = Pattern(8)
    chords(rise, [1, 6], 2, [(a, c) for a, _, c in pad])
    bass(rise)
    music_box(rise, every=4)

    float_ = Pattern(8)
    chords(float_, [1, 6], 2, [(a, c) for a, _, c in pad])
    bass(float_)
    music_box(float_)
    drums(float_, 7, 7, "x.x.x.x.x.x.x.x.")
    drums(float_, 4, 8, "....x..x......x.")

    theme = Pattern(8)
    chords(theme, [1, 6], 2, [(a, c) for a, _, c in pad])
    bass(theme)
    music_box(theme)
    drums(theme, 7, 7, "x.x.x.x.x.x.x.x.")
    melody(theme, 0, 9, [(0, "A-5"), (8, "F-5"), (12, "E-5"), (16, "D-5"), (28, "F-5"), (32, "A-5"), (40, "C-6"),
                         (44, "A-5"), (48, "G-5"), (60, "===")])
    theme.play(56, 4, n("D-5"), 5)

    drift = Pattern(8)
    drone(drift)
    drift.play(0, 5, n("C-4"), 6)
    chords(drift, [1], 2, [(c[2],) for c in pad])
    music_box(drift, every=8)
    drift.play(32, 4, n("F-4"), 5)
    drift.off(56, 4)

    return song("Deep Space", 6, 96, I, [opening, rise, float_, theme, drift], [0, 1, 2, 3, 2, 3, 4], level=0.85)


# ════════════════════════════════════════════════════════════════════════
#  PIXEL RUSH  (E minor, Em C G D) – fast chip music
# ════════════════════════════════════════════════════════════════════════
def pixel_rush():
    I = [
        load("Chip", "C64 Lead", Volume=0.17),                        # 1
        load("Chip", "Minor Chord", Volume=0.09, Pan=-30),            # 2
        load("Chip", "Major Chord", Volume=0.09, Pan=-30),            # 3
        load("Chip", "NES Triangle", Volume=0.3),                     # 4
        load("Drums", "Kick"),                                        # 5
        load("Drums", "Snare"),                                       # 6
        load("Drums", "Closed Hihat", Volume=0.12, Pan=30),           # 7
        load("Chip", "Trill", Volume=0.12, Pan=40),                   # 8
        load("FX", "Power Up", Volume=0.12),                          # 9
        load("Bass", "C64 Bass", Volume=0.2),                         # 10
    ]
    prog = [("E-4", 2), ("C-4", 3), ("G-4", 3), ("D-4", 3)]   # root and chord instrument (minor/major)
    bass_roots = ["E-2", "C-2", "G-2", "D-2"]

    def arp(p, ch=1):
        for bar, (root, inst) in enumerate(prog):
            p.play(bar * 16, ch, n(root), inst)
            p.play(bar * 16 + 8, ch, n(root), inst)

    def bass(p, inst=4):
        for bar, root in enumerate(bass_roots):
            r = n(root)
            for i, step in enumerate([0, 12, 0, 12, 7, 12, 0, 12]):
                p.play(bar * 16 + i * 2, 2, r + step, inst)

    def beat(p):
        drums(p, 3, 5, "x.....x...x.....")
        drums(p, 4, 6, "....x.......x...")
        drums(p, 5, 7, "x.x.x.x.x.x.x.x.")

    lead_a = [(0, "B-4"), (4, "E-5"), (6, "F#5"), (8, "G-5"), (12, "F#5"), (14, "E-5"), (16, "E-5"), (20, "G-5"),
              (24, "C-6"), (28, "B-5"), (32, "B-5"), (36, "D-5"), (40, "G-5"), (44, "A-5"), (48, "A-5"), (52, "F#5"),
              (56, "D-5"), (60, "F#5")]
    lead_b = [(0, "G-5"), (2, "F#5"), (4, "E-5"), (6, "B-4"), (8, "E-5"), (10, "G-5"), (12, "B-5"), (14, "A-5"),
              (16, "G-5"), (20, "E-5"), (24, "C-5"), (28, "E-5"), (32, "D-5"), (36, "G-5"), (40, "B-5"), (44, "D-6"),
              (48, "C#6"), (52, "A-5"), (56, "F#5"), (60, "A-5")]

    intro = Pattern(8)
    arp(intro)
    drums(intro, 5, 7, "x.x.x.x.x.x.x.x.")
    intro.play(48, 7, n("E-5"), 9)
    intro.off(58, 7)

    run = Pattern(8)
    arp(run)
    bass(run)
    beat(run)

    verse = Pattern(8)
    arp(verse)
    bass(verse)
    beat(verse)
    melody(verse, 0, 1, lead_a)

    chorus = Pattern(8)
    arp(chorus)
    bass(chorus, inst=10)
    beat(chorus)
    melody(chorus, 0, 1, lead_b)
    # A trill (a square LFO on the pitch, one period per row) on the long notes
    for row in (16, 32, 48):
        chorus.play(row, 6, n(dict(lead_b)[row]) - 12, 8)
        chorus.off(row + 6, 6)

    bridge = Pattern(8)
    arp(bridge)
    for bar, root in enumerate(bass_roots):
        bridge.play(bar * 16, 2, n(root), 10)
    drums(bridge, 3, 5, "x...............")
    drums(bridge, 5, 7, "..x...x...x...x.")
    for i in range(56, 64):
        bridge.put(i, 4, "C-4", 6)   # snare roll
    bridge.play(48, 7, n("E-5"), 9)
    bridge.off(58, 7)

    return song("Pixel Rush", 6, 150, I, [intro, run, verse, chorus, bridge], [0, 1, 2, 3, 2, 3, 4, 3], level=0.66)


# ════════════════════════════════════════════════════════════════════════
#  In the style of 1980s C64 game music (original tunes): four channels as close to the
#  SID's three voices as the tracker goes - lead, a bass that jumps octaves, chord arpeggios
#  and a drum channel. The lead glides into notes (effect 3) and gets vibrato on long ones.
# ════════════════════════════════════════════════════════════════════════
def c64_bass(p, ch, inst, roots, steps="R.ROR.ORR.RO.RFO"):
    """The bouncing bass: root, octave up and fifth in sixteenths."""
    for bar, root in enumerate(roots):
        r = n(root)
        for i, step in enumerate(steps):
            if step != ".":
                p.play(bar * 16 + i, ch, r + {"R": 0, "O": 12, "F": 7}[step], inst)


def c64_drums(p, ch, kick, snare, hat, steps):
    """One channel for all drums: K kick, S snare, H hi-hat, a 16 character bar."""
    which = {"K": kick, "S": snare, "H": hat}
    for bar in range(4):
        for i, step in enumerate(steps):
            if step in which:
                p.put(bar * 16 + i, ch, "C-4", which[step])


def chord_arps(p, ch, chords):
    """A chord per half bar as an arpeggio: (root, instrument) for each bar."""
    for bar, (root, inst) in enumerate(chords):
        p.play(bar * 16, ch, n(root), inst)
        p.play(bar * 16 + 8, ch, n(root), inst)


def steel_sprint():
    I = [
        load("Chip", "C64 Lead", Volume=0.2, EchoMix=15, EchoSyncRows=3, EchoFeedback=25),    # 1
        load("Bass", "C64 Bass", Volume=0.24, Decay=90, Sustain=40),                         # 2
        load("Chip", "Minor Chord", Volume=0.1, Pan=-25),                                   # 3
        load("Chip", "Major Chord", Volume=0.1, Pan=-25),                                   # 4
        load("Drums", "Kick", Volume=0.4),                                                  # 5
        load("Drums", "Snare", Volume=0.28),                                                # 6
        load("Drums", "Closed Hihat", Volume=0.13, Pan=25),                                 # 7
    ]
    verse_chords = [("A-4", 3), ("A-4", 3), ("F-4", 4), ("G-4", 4)]
    turn_chords = [("A-4", 3), ("A-4", 3), ("D-4", 3), ("E-4", 4)]   # E major pulls back to A minor
    beat = "K.H.S.HKK.HKS.HH"

    def base(p, chords, bass_roots):
        c64_bass(p, 1, 2, bass_roots)
        chord_arps(p, 2, chords)
        c64_drums(p, 3, 5, 6, 7, beat)

    tune_a = [(0, "A-4"), (3, "C-5"), (6, "E-5"), (8, "A-5"), (12, "G-5"), (14, "E-5"),
              (16, "D-5"), (18, "E-5"), (20, "C-5"), (24, "A-4"), (28, "B-4"), (30, "C-5"),
              (32, "C-5"), (35, "F-5"), (38, "A-5"), (40, "G-5"), (44, "F-5"), (46, "E-5"),
              (48, "D-5"), (52, "B-4"), (56, "G-4"), (60, "B-4"), (62, "D-5")]
    tune_b = [(0, "E-5"), (2, "A-5"), (4, "B-5"), (6, "C-6"), (8, "B-5"), (10, "A-5"), (12, "E-5"),
              (16, "A-5"), (20, "G-5"), (22, "F-5"), (24, "E-5"), (28, "C-5"),
              (32, "D-5"), (34, "F-5"), (36, "A-5"), (40, "D-6"), (44, "C-6"), (46, "A-5"),
              (48, "G#5"), (52, "B-5"), (56, "E-6"), (60, "D-6"), (62, "B-5")]

    def lead(p, tune, glides):
        melody(p, 0, 1, tune)
        for row in glides:   # glide into these notes instead of striking them
            p.fx(row, 0, "318")

    intro = Pattern(4)
    c64_bass(intro, 1, 2, ["A-2", "A-2", "F-2", "G-2"])
    c64_drums(intro, 3, 5, 6, 7, "K...S...K.K.S.HH")

    verse = Pattern(4)
    base(verse, verse_chords, ["A-2", "A-2", "F-2", "G-2"])
    lead(verse, tune_a, (8, 38))

    turn = Pattern(4)
    base(turn, turn_chords, ["A-2", "A-2", "D-2", "E-2"])
    lead(turn, tune_b, (8, 40, 56))

    # A break: the arpeggio and the bass alone, the drums only on the beat, the lead holding long notes
    bridge = Pattern(4)
    c64_bass(bridge, 1, 2, ["F-2", "G-2", "E-2", "E-2"], steps="R...O...R...O.F.")
    chord_arps(bridge, 2, [("F-4", 4), ("G-4", 4), ("E-4", 4), ("E-4", 4)])
    c64_drums(bridge, 3, 5, 6, 7, "K.......S.......")
    melody(bridge, 0, 1, [(0, "A-5"), (16, "B-5"), (32, "G#5"), (48, "B-5"), (56, "E-6"), (62, "===")])

    return song("Steel Sprint", 6, 150, I, [intro, verse, turn, bridge],
                [0, 1, 2, 1, 2, 3, 1, 2], channels=4, level=0.85)


def glass_labyrinth():
    I = [
        load("Lead", "Square Lead", Volume=0.18, EchoMix=35, EchoSyncRows=3, EchoFeedback=45),  # 1
        load("Bass", "Pulse Bass", Volume=0.22),                                               # 2
        load("Chip", "Minor Chord", Volume=0.09, EchoMix=25, EchoSyncRows=2, EchoFeedback=30, Pan=-30),   # 3
        load("Chip", "Major Chord", Volume=0.09, EchoMix=25, EchoSyncRows=2, EchoFeedback=30, Pan=-30),   # 4
        load("Drums", "Kick", Volume=0.36),                                                    # 5
        load("Drums", "Snare", Volume=0.24),                                                   # 6
        load("Percussion", "Shaker", Volume=0.14, Pan=30),                                     # 7
    ]
    chords = [("D-4", 3), ("A#3", 4), ("C-4", 4), ("A-3", 3)]
    roots = ["D-2", "A#1", "C-2", "A-1"]
    beat = "K.H.H.H.S.H.H.HH"

    def base(p, steps="R...O.R.R...O.F."):
        c64_bass(p, 1, 2, roots, steps)
        chord_arps(p, 2, chords)
        c64_drums(p, 3, 5, 6, 7, beat)

    tune_a = [(0, "A-4"), (4, "D-5"), (8, "F-5"), (12, "E-5"), (16, "D-5"), (22, "F-5"), (24, "A#4"), (28, "D-5"),
              (32, "C-5"), (36, "E-5"), (40, "G-5"), (44, "E-5"), (48, "C-5"), (52, "A-4"), (56, "E-5"), (60, "===")]
    tune_b = [(0, "F-5"), (2, "E-5"), (4, "D-5"), (8, "A-5"), (16, "A#5"), (20, "A-5"), (24, "F-5"), (28, "D-5"),
              (32, "E-5"), (34, "F-5"), (36, "G-5"), (40, "C-6"), (48, "A-5"), (52, "E-5"), (56, "C#5"), (60, "E-5")]

    intro = Pattern(4)
    chord_arps(intro, 2, chords)
    c64_drums(intro, 3, 5, 6, 7, "........H.......")

    walk = Pattern(4)
    base(walk)

    theme = Pattern(4)
    base(theme)
    melody(theme, 0, 1, tune_a)
    theme.fx(8, 0, "310")

    answer = Pattern(4)
    base(answer, steps="R.ROR.O.R.ROR.FO")
    melody(answer, 0, 1, tune_b)
    answer.fx(40, 0, "310")

    return song("Glass Labyrinth", 6, 118, I, [intro, walk, theme, answer],
                [0, 1, 2, 3, 2, 3, 1], channels=4, level=0.9)


def main():
    for name, build in [("acid_sunrise", acid_sunrise), ("deep_space", deep_space), ("pixel_rush", pixel_rush),
                        ("steel_sprint", steel_sprint), ("glass_labyrinth", glass_labyrinth)]:
        path = os.path.join(OUT, name + ".amostrk")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(build(), f, indent=2, ensure_ascii=False)
        print("wrote", path)


if __name__ == "__main__":
    main()
