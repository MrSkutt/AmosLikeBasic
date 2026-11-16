# Sound and music

A game without sound feels empty. AMOS Like Basic has a built-in **synth** that makes sound effects and music in the style of old games consoles, and it can also play sound files and music modules.

## Ready-made sound effects

The simplest sounds are ready to use. Write `PLAY` and the name:

```basic
PLAY COIN
WAIT 400
PLAY JUMP
WAIT 400
PLAY LASER
WAIT 400
PLAY HIT
WAIT 400
PLAY POWERUP
WAIT 600
PLAY EXPLOSION
```

| Effect | Good for |
|---|---|
| `PLAY COIN` | picking something up |
| `PLAY JUMP` | jumping |
| `PLAY LASER` | shooting |
| `PLAY HIT` | a hit |
| `PLAY BLIP` | menu choices, button presses |
| `PLAY POWERUP` | a bonus, a new level |
| `PLAY EXPLOSION` | an explosion, a lost life |

If you want sounds of your own, make them in the tracker and play them with `TRACK SFX`; see *Your own sound effects* further on.

## Music with the synth

The synth has eight channels that play at the same time and stay exactly in step with each other. A C64 had three and a NES four, so that goes a long way. Each channel gets a sound type, a volume and a melody:

```basic
PLAY BPM 132
PLAY WAVE 1, "Square"
PLAY WAVE 2, "Triangle"
PLAY WAVE 4, "Drums"
PLAY VOLUME 1, 0.4
PLAY MUSIC 1, "C5:8 E5:8 G5:8 E5:8 F5:8 A5:8 G5:4"
PLAY MUSIC 2, "C3:4 G2:4 F2:4 G2:4"
PLAY MUSIC 4, "BD:8 HH:8 SD:8 HH:8 BD:8 HH:8 SD:8 HH:8"
WAIT MUSIC
```

| Command | Does |
|---|---|
| `PLAY BPM tempo` | tempo in beats per minute |
| `PLAY WAVE channel, "type"` | sound type: `Square`, `Pulse`, `Triangle`, `Sawtooth`, `Sine`, `Noise` or `Drums` |
| `PLAY VOLUME channel, volume` | volume from 0 to 1 |
| `PLAY MUSIC channel, notes` | plays the notes on the channel once |
| `PLAY LOOP channel, notes` | plays the notes over and over |
| `PLAY STOP [channel]` | stops a channel, or all synth music |
| `WAIT MUSIC` | waits until all music has finished |
| `WAIT MUSIC channel` | waits for one channel |

### Notes

A note is written as **name**, **octave** and **length**: `C5:8` is a C in the fifth octave, an eighth note.

- The names are `C D E F G A B`, with `#` for sharp (`C#`, `F#`) or `b` for flat (`Db`, `Bb`).
- Octave 4 is the middle. `A4` is 440 Hz, the note orchestras tune to.
- The length is 1 (whole note), 2 (half note), 4 (quarter), 8 (eighth), 16 (sixteenth) or 32. If you leave out the length, the same as the previous note is used. A dot after the length makes the note half as long again: `C5:4.` is a dotted quarter.
- If you leave out the octave, the same as the previous note is used: `C5:8 E G C6` is C5, E5, G5 and C6.
- `R` is a rest: `R:4` is a quarter rest.

On a channel with `Drums` you play drum sounds instead of notes: `BD` (bass drum), `SD` (snare), `HH` (hi-hat), `OH` (open hi-hat), `CY` (cymbal) and `TM` (tom).

The notes can also be in a text variable: `PLAY MUSIC 1, MELODY$`.

### Music that repeats

`PLAYING(channel)` is 1 as long as the channel is playing, and `ANYPLAYING()` if any channel is playing. This is how you restart the music when it has finished, in the main loop of a game:

```basic nocheck
IF PLAYING(4) = 0 THEN StartMusic
```

If the song should just repeat, `PLAY LOOP` is simpler. The project `Projects/forestsong.amosproj` starts all four channels with `PLAY LOOP`, and since the channels are exactly the same length they stay in step lap after lap.

## Shaping the sound

The sound is worked out while it plays, so everything in this section can be changed at any time, even in the middle of a note. That is how the lively sounds of the C64 and NES were made: a bass that opens up, a melody that wobbles, a chord played so fast that it sounds like three notes at once.

### Envelope: ADSR

`PLAY ADSR channel, attack, decay, sustain, release` decides how each note starts and fades:

- **attack**: how many milliseconds it takes for the note to reach full strength
- **decay**: how many milliseconds it then takes to sink to the sustain level
- **sustain**: the strength while the note is held, 0 – 100
- **release**: how many milliseconds it takes for the note to fade when it is let go

```basic
PLAY WAVE 1, "Square"
PLAY ADSR 1, 2, 150, 0, 50
PLAY MUSIC 1, "C5:8 E5 G5 C6 G5 E5 C5:4"
WAIT MUSIC
PLAY ADSR 1, 300, 100, 80, 600
PLAY MUSIC 1, "C4:2 G4:2"
WAIT MUSIC
```

First short, plucked notes like a harp, then soft notes like an organ. `PLAY GATE channel, percent` decides how much of the note length the note is held before it is let go, normally 90.

### Pulse width

The `Pulse` wave is a square wave where you choose how much of each cycle is "up". A narrow pulse sounds thin and nasal, like NES melodies; 50 % is an ordinary square wave. With two more values the pulse width swings back and forth by itself, the classic C64 sound:

```text
PLAY PULSE channel, width [, depth, rate]
```

```basic
PLAY WAVE 1, "Pulse"
PLAY PULSE 1, 12
PLAY MUSIC 1, "A4:4 C5 E5 A5"
WAIT MUSIC
PLAY PULSE 1, 30, 20, 2
PLAY MUSIC 1, "A3:1"
WAIT MUSIC
```

### Filter

A filter takes away part of the sound. `PLAY FILTER channel, "type", frequency, resonance`:

| Type | Lets through |
|---|---|
| `"LOW"` | the dark part, below the frequency |
| `"HIGH"` | the bright part, above the frequency |
| `"BAND"` | only around the frequency |
| `"OFF"` | everything, the filter is off |

The frequency is in hertz, from 20 to 18 000. The resonance, 0 – 100, boosts the sound right at the frequency and gives the sharp, "squeaky" sound. The fun part is changing the frequency while the note plays:

```basic
PLAY WAVE 1, "Sawtooth"
PLAY ADSR 1, 5, 0, 100, 200
PLAY LOOP 1, "A2:8 A2 A3 A2 G2 G2 G3 G2"
F = 0
Loop:
  F = F + 2
  PLAY FILTER 1, "LOW", 600 + SIN(F) * 500, 75
  WAIT VBL
GOTO Loop
```

The bass opens and closes in a smooth wave, like in an old synth game.

### Filter envelope

If the filter should open and close by itself for every note instead, use `PLAY FILTERENV channel, depth, attack, decay`. On every new note the filter opens by up to `depth` hertz above the frequency in `PLAY FILTER`, over `attack` milliseconds, and then closes again over about `decay` milliseconds. That gives the classic "wah" sound in the bass:

```basic
PLAY WAVE 1, "Sawtooth"
PLAY ADSR 1, 2, 0, 100, 60
PLAY FILTER 1, "LOW", 150, 65
PLAY FILTERENV 1, 2500, 2, 180
PLAY MUSIC 1, "A2:8 A2 A3 A2 F2 F2 F3 F2 C3 C3 C4 C3 G2 G2 G3 G2"
WAIT MUSIC
```

A negative depth closes the filter instead, and `PLAY FILTERENV 1, 0` turns it off. The filter must be on with `PLAY FILTER` for the envelope to be heard.

> **Tip:** The project `Projects/synthdemo.amosproj` is a short song that uses most of this section: PWM, vibrato, filter envelope, arpeggio and drums.

### Vibrato, glide and bend

| Command | Does |
|---|---|
| `PLAY VIBRATO channel, depth, rate` | the pitch wobbles; depth in hundredths of a semitone (100 = one semitone), rate in Hz |
| `PLAY GLIDE channel, milliseconds` | the note glides to the next one instead of jumping |
| `PLAY BEND channel, semitones` | moves the pitch straight away, decimals too; 0 is normal |

```basic
PLAY WAVE 1, "Triangle"
PLAY VIBRATO 1, 25, 6
PLAY GLIDE 1, 80
PLAY MUSIC 1, "E5:4 G5 A5:2 G5:8 E5 D5:2"
WAIT MUSIC
```

### Arpeggio

The C64 and NES had too few channels to play chords. Instead the notes of the chord were played one after the other so fast that the ear hears them as a chord. `PLAY ARP channel, "steps", rate` does that automatically with every note on the channel. The steps are semitones from the note, and the rate is steps per second (50 is like on a C64):

```basic
PLAY WAVE 1, "Pulse"
PLAY PULSE 1, 25
PLAY ARP 1, "0 4 7", 50
PLAY MUSIC 1, "C5:2 A4:2 F4:2 G4:2"
WAIT MUSIC
```

`"0 4 7"` is a major chord and `"0 3 7"` a minor chord. `PLAY ARP 1, ""` turns it off.

### Ring modulation and sync

Two special effects from the SID chip in the C64. Both link one channel to another:

| Command | Does |
|---|---|
| `PLAY RING channel, source` | the channel's sound flips every half cycle of the source channel; gives metallic, bell-like sounds |
| `PLAY SYNC channel, source` | the channel's wave restarts every time the source channel's does; gives sharp, screaming sounds |

`PLAY RING channel, 0` and `PLAY SYNC channel, 0` turn them off. The source channel must be playing a note for anything to happen.

### Effects in the notes

The settings above can also go *inside* the note string, in braces `{ }`. Then the sound changes right there in the melody, without your program having to do anything while the song plays. The effects take no time, so the rhythm is not affected:

```basic
PLAY WAVE 1, "Sawtooth"
PLAY MUSIC 1, "{FILTER LOW 300 40} C3:8 C3 C4 C3 {CUT 3000} C3 C3 C4 C3 {SWEEP 300 1000} C3 C3 C4 C3 C3 C3 C4 C3"
WAIT MUSIC
```

The bass sounds dull, turns sharp in the middle of the bar and then glides back over one second. Several effects at the same place are separated with semicolons: `{WAVE PULSE; PULSE 12; VIBRATO 30}`.

The effects have the same names and values as the `PLAY` commands, but without the channel number and the quotation marks:

| Effect | Does |
|---|---|
| `{WAVE SAW}` | changes the sound type |
| `{VOLUME 0.3}` | volume 0 – 1 |
| `{FADE 0, 2000}` | the volume glides to a new value over a number of milliseconds |
| `{ADSR 5, 100, 60, 200}` | envelope |
| `{GATE 50}` | how much of the note length the note is held |
| `{PULSE 30}` | pulse width; with two more values PWM as well: `{PULSE 30, 15, 2}` |
| `{FILTER LOW, 800, 50}` | filter type, and if you like frequency and resonance |
| `{CUT 800}` | just the filter frequency |
| `{RES 70}` | just the filter resonance |
| `{SWEEP 4000, 1500}` | the filter glides to a new frequency over a number of milliseconds |
| `{FILTERENV 2000, 5, 200}` | filter envelope |
| `{VIBRATO 20, 6}` | vibrato; `{VIBRATO 0}` turns it off |
| `{ARP 0 4 7}` | arpeggio; `{ARP}` turns it off |
| `{ARPRATE 50}` | the arpeggio rate |
| `{GLIDE 50}` | gliding between notes |
| `{BEND 2}` | moves the pitch a number of semitones |
| `{RING 2}`, `{SYNC 2}` | ring modulation and sync with another channel |

A common trick in C64 music is to change chords with arpeggio every bar. Then a single channel is enough for a whole chord accompaniment:

```basic
PLAY WAVE 3, "Pulse"
PLAY VOLUME 3, 0.15
PLAY MUSIC 3, "{ARP 0 4 7} C4:2 {ARP 0 3 7} A3:2 {ARP 0 4 7} F3:2 G3:2"
WAIT MUSIC
```

Settings changed by an effect stay when the melody has ended, just as if you had written the `PLAY` command. In a looping melody it is therefore a good idea to set the sound at the start of the string, so that every lap starts the same. The song in `Projects/forestsong.amosproj` does that: the melody switches to a thinner, gliding pulse wave in the second half and back when it starts again, and the bass filter opens slowly with `SWEEP`.

All settings are reset when the program starts again.

## Songs from the tracker

A **tracker** is a music program where the song is written from top to bottom in rows, like a spreadsheet. Each column is a channel, and each row can have a note, an instrument and an effect. ProTracker on the Amiga worked that way, and nearly all game music on the C64 and NES was made the same way.

AMOS Like Basic plays tracker songs with the synth. The song is in a file of its own ending in `.amostrk`:

```basic
TRACK LOAD "Sound/Tracker/forest_song.amostrk"
TRACK PLAY
Loop:
  WAIT VBL
GOTO Loop
```

| Command | Does |
|---|---|
| `TRACK LOAD "file"` | loads a song |
| `TRACK PLAY [position]` | plays the song, from the start or from a place in the order list |
| `TRACK STOP` | stops the song (`PLAY STOP` does too) |
| `TRACK VOLUME volume` | the volume of the song, 0 – 1 |
| `TRACK LOOP 0` | the song plays once instead of starting again (`TRACK LOOP 1` turns looping back on) |

| Function | Gives |
|---|---|
| `TRACKPLAYING()` | 1 while the song plays |
| `TRACKPOS()` | the place in the order list being played, from 0 |
| `TRACKROW()` | the row in the pattern being played, from 0 |

A tracker song uses synth channels 1 – 4. Channels 5 – 8 are free for `PLAY MUSIC` while the song plays, and sound effects such as `PLAY COIN` do not affect it.

The project `Projects/trackerdemo.amosproj` plays Forest Song, the song from Forest Hop.

More songs in C64 style are in `Sound/Tracker` and are used by the example projects:

| Song | Project | Style |
|---|---|---|
| `raster_dream.amostrk` | `raster_bars` | a fast demo tune with arpeggio chords and a filter that opens in the intro |
| `solar_system.amostrk` | `solar_system` | spacey, with the C64 echo: the melody repeats more quietly on another channel |
| `shadow_play.amostrk` | `shadow_text` | funky, with a sawtooth melody through a resonant filter |
| `winter_evening.amostrk` | `weather` | slow and wistful, with bells |
| `snake.amostrk` | `snake` | bouncy game music with short, choppy notes |
| `frog_hop.amostrk` | `froghop` | cheerful game music for Frog Hop |

Open them in the tracker to see how the sounds and effects are made.

A game often changes music depending on what happens. Forest Hop has four songs in `Sound/Tracker`: calm music on the title screen, Forest Song while you play, and two short jingles when you win or lose. The jingles play once with `TRACK LOOP 0`:

```basic nocheck
PROC WinMusic
  TRACK LOAD "Sound/Tracker/victory.amostrk"
  TRACK LOOP 0
  TRACK PLAY
END PROC
```

### The tracker in the editor

The **TRACKER** button opens a window where you write the songs. In the middle is the pattern with the four channels, on the left the order list and on the right the instruments.

**Entering notes.** The keyboard works like a piano, as in ProTracker. The bottom row of letters (`Z S X D C V G B H N J M`) is one octave and the top row (`Q 2 W 3 E R 5 T 6 Y 7 U I`) the next. The note gets the instrument selected in the list, and you hear it while you hold the key. `1` lets go of the note (`===`). After each note the cursor moves down as many rows as **Step** says. **Octave** chooses which octave the piano starts on, and `F1` and `F2` change the octave.

**Instruments and effects.** With the arrow keys the cursor goes to the instrument and effect columns. There you type digits and letters directly: `0`–`9` and `A`–`F` for hex digits, and in the first column of the effect also `G`, `H` and `I`. `Delete` or `Backspace` clears whatever the cursor is on.

**Playing.** `F5` plays the song from the place selected in the order list. `F6` plays the pattern over and over, `F7` plays the pattern from the cursor row and `Esc` stops. With **Follow** the pattern follows along while the song plays. Click a channel heading to mute the channel, and right-click to hear only that channel.

**Edit.** `Space` turns edit mode on and off. When it is off you can play on the piano keys without anything being written in the pattern, for example to try out an instrument. The cursor row is red while edit is on. If the song is playing, `Space` stops it instead.

**Selecting and changing.** Hold `Shift` and move the cursor, or drag with the mouse, to select a block. `Cmd+C`, `Cmd+X` and `Cmd+V` copy, cut and paste (`Ctrl` on Windows). `Cmd+↑` and `Cmd+↓` move the notes a semitone, with `Shift` an octave. `Cmd+I` (or `Insert`) inserts an empty row and `Cmd+Backspace` removes the row, in the cursor's channel or with `Shift` in all channels. `Cmd+Z` undoes and `Cmd+Shift+Z` redoes.

**The order list.** **INS** adds a place after the selected one, **DEL** removes it. **PAT -** and **PAT +** change which pattern the place plays; after the last pattern **PAT +** makes a new empty one. **CLONE PATTERN** copies the pattern to a new one, so that you can make a variation without changing the original. **Rows** is how many rows the pattern has.

**The instrument panel.** Everything you change is heard straight away, even while the song plays. The table is written with one step per line, and the box turns red if a step is wrong. A new song has six ready-made instruments to start with: lead, bass, chord, kick, snare and hi-hat.

**Saving.** **SAVE** saves the song as an `.amostrk` file, ideally in `Sound/Tracker` in the project folder. The status bar then shows the line that loads the song, for example `TRACK LOAD "Sound/Tracker/mysong.amostrk"`. If you export the game the file comes along, since its name is in quotation marks in the program.

### How a song is built

A song has three parts:

- **Instruments** are sounds: waveform, envelope, filter and so on, with the same values as the `PLAY` commands. The instruments are numbered from 01.
- **Patterns** are blocks of rows, usually 64. A row has one cell for each channel.
- **The order list** says in which order the patterns play. A pattern can play many times, so a chorus only needs to be written once.

The tempo is set by two numbers. **Speed** is how many *ticks* each row lasts, and **tempo** how fast the ticks come: 125 gives 50 ticks a second. With speed 6 and four rows per beat, the tempo is the same number as the beats per minute, so speed 6 and tempo 136 is 136 BPM.

A cell is written as note, instrument and effect:

```text
C-4 01 A04
```

That means C in the fourth octave, instrument 01, and effect `A04` (the volume goes down). `---` is no new note, `===` lets go of the note, and `..` and `...` are empty. A note keeps sounding until a new note or `===` comes.

### Effects

The effect is a letter or digit and two hex digits. Most only apply on their own row, so a glide or vibrato that should continue is written on every row. The numbers are as in ProTracker:

| Effect | Does |
|---|---|
| `0xy` | arpeggio: the note switches between the note, x semitones up and y semitones up, one step per tick |
| `1xx` | slides up, xx sixteenths of a semitone per tick (`110` is one semitone per tick) |
| `2xx` | slides down |
| `3xx` | glides to the note on the row instead of playing it; `300` carries on at the same speed |
| `4xy` | vibrato: x is the rate, y the depth; `400` carries on |
| `5xy` | carries on `3` and changes the volume like `A` |
| `6xy` | carries on `4` and changes the volume like `A` |
| `Axy` | the volume goes up by x or down by y per tick |
| `Bxx` | jumps to place xx in the order list |
| `Cxx` | volume 00 – 40 (hex, 40 is full volume) |
| `Dxy` | goes on to row xy (decimal) in the next pattern |
| `ECx` | goes silent on tick x |
| `EDx` | the note starts on tick x instead of straight away |
| `Fxx` | 01 – 1F sets the speed, 20 – FF the tempo |
| `Gxx` | filter frequency: 00 is 20 Hz, 80 about 640 Hz and FF 18 000 Hz; turns on low-pass if the filter is off |
| `Hxx` | filter resonance, 00 – FF |
| `Ixx` | pulse width, 80 is 50 % |

`G`, `H` and `I` stay on the channel until it changes instrument, so a filter can open slowly over many notes.

### Instruments

An instrument has the same settings as the `PLAY` commands: `Wave`, `Volume`, `PulseWidth`, `PwmDepth`, `PwmRate`, `Attack`, `Decay`, `Sustain`, `Release`, `Filter`, `Cutoff`, `Resonance`, `FilterEnvDepth`, `FilterEnvAttack`, `FilterEnvDecay`, `Vibrato`, `VibratoRate`, `Ring` and `Sync`. Those you leave out get default values.

An instrument can also have a **table** that moves one step per tick from the moment the note starts. Each step can change the waveform, move the note a number of semitones and change the pulse width (`P30` is 30 %). That is how C64 musicians made drums and chords:

```text
"Table": ["SQUARE +24", "TRIANGLE +12", "+6", "+2", "-2", "-5", "-8", "-10", "-12"]
```

That table is a kick drum: a short click of square wave and then a triangle wave that quickly drops in pitch. `TableLoop` is the step the table starts again from when it reaches the end. Without it the table stays on the last step. A major chord as an arpeggio is `["0", "+4", "+7"]` with `"TableLoop": 0`.

### The file

The file is JSON, so it can be opened in an ordinary text editor. Each row of a pattern is a line of text, with the channels separated by `|`:

```text
{
  "Format": "AmosTracker",
  "Version": 1,
  "Title": "Forest Song",
  "Speed": 6,
  "Tempo": 136,
  "Instruments": [
    { "Name": "Bass", "Wave": "Sawtooth", "Volume": 0.25, "Filter": "Low", "Cutoff": 250, ... },
    ...
  ],
  "Order": [0, 1, 2, 3],
  "Patterns": [
    [
      "G-5 01 ...|G-2 03 G5D|G-3 04 047|C-7 0B ...",
      "--- .. ...|=== .. ...|--- .. 047|--- .. ...",
      ...
    ]
  ]
}
```

An error in the file gives an error message that tells you the pattern, row and channel.

## Your own sound effects

On the C64 sound effects were made the same way as instruments: a table that changes the pitch and waveform every frame. A jump is a note that rises, an explosion noise that falls. In AMOS Like Basic you make them in the tracker and play them with `TRACK SFX`.

A **sound bank** is an ordinary tracker file, and its instruments are the sounds. `Sound/Tracker/sfx.amostrk` comes with eleven sounds to start with:

```basic
TRACK SFX LOAD "Sound/Tracker/sfx.amostrk"
TRACK SFX "Jump", "C-4"
WAIT 400
TRACK SFX "Coin", "B-5"
WAIT 400
TRACK SFX "Explosion", "C-4"
WAIT 800
```

| Command | Does |
|---|---|
| `TRACK SFX LOAD "file"` | loads a sound bank |
| `TRACK SFX instrument, note` | plays a sound; the instrument is a name or number, the note text such as `"C-4"` or `"C4"`, or a number (60 is C-4) |
| `TRACK SFX instrument, note, ms` | holds the note for that many milliseconds |
| `TRACK SFX instrument, note, ms, channel` | plays on a given channel |
| `TRACK SFX VOLUME volume` | the volume of the sound effects, 0 – 1 |
| `TRACK SFX STOP` | silences all sound effects |

| Sound in `sfx.amostrk` | Good for |
|---|---|
| `Jump` | jumping, a note that rises |
| `Bonk` | hitting the ceiling or a wall |
| `Fall` | falling, a whistle that drops |
| `Coin` | picking something up |
| `Explosion` | an explosion |
| `Laser` | shooting |
| `Powerup` | a bonus, a rising arpeggio |
| `Ouch` | getting hurt |
| `Blip` | menu choices |
| `Step` | footsteps |
| `Complete` | level complete |

The sounds play on synth channels 5 – 8, and each new sound takes the channel that becomes free first. A tracker song on channels 1 – 4 therefore carries on undisturbed. If you use `PLAY MUSIC` on channels 5 – 8, the sound effect borrows the channel and gives it back with the same settings afterwards.

Without a length the sound lasts as long as the instrument is made for: a sound with sustain 0 until it has faded, one with a table until the table ends, and otherwise a quarter of a second. The note decides the pitch, so the same sound can be played higher or lower, for example a coin that sounds brighter for each coin in a row.

### Making your own sounds

Open the sound bank in the tracker, or make a new song and remove the ready-made instruments. Each instrument is a sound. Give it a name that you use in `TRACK SFX`, and try it with the piano keys while you change it.

The table is the most important part. It moves one step per tick, 50 times a second. Some examples:

| Table | Sounds like |
|---|---|
| `0`, `+2`, `+4`, `+6`, `+8`, `+10`, `+12` | a jump: the note rises an octave |
| `NOISE +24`, `TRIANGLE 0`, `-3`, `-6`, `-9` | a thud: a click of noise followed by a falling note |
| `0`, `0`, `0`, `+5` | a coin: two notes after each other |
| `+24`, `+20`, `+16`, `+12`, `+8`, `+4`, `0` | a laser shot: the note falls fast |

Keep the tempo of the sound bank at 125. Then the tables run just as fast in the tracker as in the game.

## Sound files and music modules

You can also play recorded sounds and ready-made music:

| Command | Does |
|---|---|
| `SAM PLAY "file"` | plays a sound file (WAV, MP3, OGG) once |
| `SAM LOOP "file"` | plays a sound file over and over |
| `SAM STOP "file"` | stops the sound (`SAM STOP ""` stops all) |
| `MUSIC PLAY "file"` | plays a music module (MOD, XM, S3M, IT) |
| `MUSIC STOP` | stops the music |

```basic nocheck
MUSIC PLAY "Sound/mus2.mod"
SAM PLAY "Sound/ding.mp3"
```

Music modules are the music format of the Amiga days, and there are thousands of them freely available online. If your music is an MP3, use `SAM LOOP`. Remember to check that you are allowed to use the music if you are going to share your game.

> **Try it:** Compose a little tune of your own on two channels: melody on channel 1 and bass on channel 2.
