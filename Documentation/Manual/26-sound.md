# Sound and music

A game without sound feels empty. AMOS Like Basic has a built-in **synth** that makes sound effects and music in the style of old games consoles, and it can also play sound files and music modules.

> **No sound?** Most sound cards play on channels 1 – 2, and that is where AMOS Like Basic sends its sound. A sound card or mixer with many channels may listen on other ones; the system's own setting for stereo channels is not used. Choose the card and the channels in **View → Sound Output...** (for example *Channels 17 – 18*) and start AMOS Like Basic again.

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

### A second oscillator, LFOs, echo and stereo

The same sound tools as the tracker's instruments work with `PLAY` too:

```basic
PLAY WAVE 1, "Sawtooth"
PLAY OSC2 1, "Sawtooth", 0, 10, 50          ; two saws 10 cents apart: a fat lead
PLAY FILTER 1, "LOW", 1500, 40
PLAY LFO 1, 1, "CUTOFF", "TRIANGLE", 18, 2  ; the filter opens and closes twice a second
PLAY ECHO 1, 30, 330, 45                    ; an echo a third of a second later
PLAY PAN 2, -60                             ; channel 2 to the left
PLAY MUSIC 1, "A3:4 C4 E4 A4"
WAIT MUSIC
```

`PLAY OSC2` mixes in a second oscillator, tuned in semitones and cents from the note. `PLAY LFO` lets LFO 1 or 2 move the pitch, volume, cutoff, resonance, pulse width, oscillator 2 or the pan, with depth in cents (pitch), semitones (cutoff) or percent. `PLAY ECHO` repeats the sound, and `PLAY PAN` places the channel from left (-100) to right (100). The LFOs start again on every note. In a melody they are written `{OSC2 SAW 0 10 50}`, `{LFO 1 CUTOFF TRIANGLE 18 2}`, `{ECHO 30 330 45}` and `{PAN -60}`; `{LFO 1 OFF}`, `{OSC2 OFF}` and `{ECHO 0}` turn them off.

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
| `{OSC2 SAW 0 10 50}` | the second oscillator: wave, semitones, cents and mix; `{OSC2 OFF}` turns it off |
| `{LFO 1 CUTOFF TRIANGLE 18 2}` | LFO 1 or 2: target, wave, depth, rate in Hz and delay in ms; `{LFO 1 OFF}` |
| `{ECHO 30 330 45}` | echo: mix, time and feedback; `{ECHO 0}` turns it off |
| `{PAN -60}` | where in stereo, -100 to 100 |

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

A tracker song has 4 to 8 channels and uses that many synth channels, from channel 1. A song with four channels uses channels 1 – 4, and channels 5 – 8 are free for `PLAY MUSIC` and sound effects while it plays. A song with more channels takes more of them: with six, channels 7 and 8 are left. Sound effects such as `PLAY COIN` do not affect the song.

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
| `neon_horizon.amostrk` | `oldschool_demo` | eight channels: a lead with a C64 echo, filter bass, arpeggios, two pad voices and drums; open it in the tracker and watch the oscilloscopes |
| `acid_sunrise.amostrk` | `synth_jukebox` | acid techno: an acid bass whose filter opens part by part, a supersaw lead (two detuned saws) with vibrato that grows in, a wobble bass in the breakdown and 808 drums |
| `deep_space.amostrk` | `synth_jukebox` | slow and spacey: a drone whose filter drifts with two LFOs, a glass pad, a music box and UFO sounds |
| `pixel_rush.amostrk` | `synth_jukebox` | fast chip music: a C64 lead whose pulse width moves, chord arpeggios, an NES bass and trills made with a square LFO |
| `steel_sprint.amostrk` | `synth_jukebox` | in the style of 1980s C64 game music, on four channels: a bass that bounces between octaves, fast chord arpeggios, a pulse-wave lead that glides into its notes, and one channel for all the drums |
| `glass_labyrinth.amostrk` | `synth_jukebox` | the same style, slower and mysterious: a square lead and arpeggios with echo on the beat |

Open them in the tracker to see how the sounds and effects are made. The last five are made only with instruments from the library, and are written by `Tools/build_songs.py`.

A game often changes music depending on what happens. Forest Hop has four songs in `Sound/Tracker`: calm music on the title screen, Forest Song while you play, and two short jingles when you win or lose. The jingles play once with `TRACK LOOP 0`:

```basic nocheck
PROC WinMusic
  TRACK LOAD "Sound/Tracker/victory.amostrk"
  TRACK LOOP 0
  TRACK PLAY
END PROC
```

### The tracker in the editor

The **TRACKER** button opens a window where you write the songs. In the middle is the pattern with the song's channels, on the left the order list and on the right the instruments. Above the pattern each channel has an oscilloscope, which shows the channel's sound wave as it plays, and a level meter. They move when you try notes on the keyboard too, so you can see what an instrument sounds like.

**Channels.** **Channels** in the toolbar sets how many channels the song has, from 4 to 8. Four is like ProTracker and leaves four synth channels for sound effects; eight gives room for fuller music. With many channels the text in the pattern gets smaller so that everything fits. If you choose fewer channels and the ones taken away have notes, the tracker asks first: the notes stay until you save, but are not saved.

**Entering notes.** The keyboard works like a piano, as in ProTracker. The bottom row of letters (`Z S X D C V G B H N J M`) is one octave and the top row (`Q 2 W 3 E R 5 T 6 Y 7 U I`) the next. The note gets the instrument selected in the list, and you hear it while you hold the key. `1` lets go of the note (`===`). After each note the cursor moves down as many rows as **Step** says. **Octave** chooses which octave the piano starts on, and `F1` and `F2` change the octave.

**Instruments and effects.** With the arrow keys the cursor goes to the instrument and effect columns. There you type digits and letters directly: `0`–`9` and `A`–`F` for hex digits, and in the first column of the effect also `G`, `H` and `I`. `Delete` or `Backspace` clears whatever the cursor is on.

**Playing.** `F5` plays the song from the place selected in the order list. `F6` plays the pattern over and over, `F7` plays the pattern from the cursor row and `Esc` stops. With **Follow** the pattern follows along while the song plays. Click a channel's oscilloscope (or its heading) to mute the channel, and right-click to hear only that channel (solo); right-click it again and all are heard. A muted channel's oscilloscope says MUTE, and the one heard alone says SOLO.

**Saving as a sound file.** **WAV** in the toolbar saves the song as a WAV file, to use the music in a video, send it to a friend or play it anywhere. It is made much faster than the song plays, and it is the song as you hear it: muted channels are left out, so you can also save the drums or the melody on their own.

**Edit.** `Space` (or **EDIT** in the toolbar) turns edit mode on and off. It is off when the tracker opens, so the keys only play until you turn it on. When it is off you can play on the piano keys without anything being written in the pattern, for example to try out an instrument. The cursor row is red while edit is on. If the song is playing, `Space` stops it instead.

**Selecting and changing.** Hold `Shift` and move the cursor, or drag with the mouse, to select a block. `Cmd+C`, `Cmd+X` and `Cmd+V` copy, cut and paste (`Ctrl` on Windows). `Cmd+↑` and `Cmd+↓` move the notes a semitone, with `Shift` an octave. `Cmd+I` (or `Insert`) inserts an empty row and `Cmd+Backspace` removes the row, in the cursor's channel or with `Shift` in all channels. `Cmd+Z` undoes and `Cmd+Shift+Z` redoes.

**The order list.** **INS** adds a place after the selected one, **DEL** removes it. **PAT -** and **PAT +** change which pattern the place plays; after the last pattern **PAT +** makes a new empty one. **CLONE PATTERN** copies the pattern to a new one, so that you can make a variation without changing the original. **Rows** is how many rows the pattern has.

**Choosing an instrument.** Click an instrument in the list to give new notes that instrument. The keyboard stays in the pattern, so you can go on typing notes straight away.

**The instrument page.** **INSTRUMENT** above the pattern (or `F4`, or a double-click in the list) shows the selected instrument like a software synth, and **PATTERN** (or `F3`) goes back. At the top an **oscilloscope** shows the sound of the instrument chosen, as it is right now: play a note on the piano keys and turn a knob, and you see what it does. While the song plays it follows a channel where that instrument sounds, so choosing another instrument in the list shows that one. Below it the sections follow the way the sound is made:

- **OSCILLATORS**: the name, a picture of the wave (both oscillators mixed), and the buttons and knobs for oscillator 1 and 2.
- **FILTER**: the type (off, low pass, high pass, band), a picture of which frequencies come through, cutoff and resonance, and the filter envelope.
- **AMP ENVELOPE**: a picture of how the volume rises and falls, and four sliders for attack, decay, sustain and release.
- **EFFECTS**: echo and vibrato, ring modulation and sync.
- **LFO 1** and **LFO 2**: what they move, their wave and knobs, with a picture.
- **TABLE**: the steps of the instrument's table.

**Knobs.** Drag a knob up or down to turn it, hold `Shift` for small steps, or turn the mouse wheel. Double-click a knob to type the exact value and press `Enter`. The cutoff and the times turn evenly through short and long values; knobs that go both ways (detune, filter envelope depth, LFO depth) have 0 at the top. The knobs and buttons never take the keyboard, so the piano keys go on playing while you turn them.

Everything you change is heard straight away, even while the song plays, and the pictures change with it. A note you hold down on the piano keys changes too: hold a note and turn the cutoff, and you hear the filter open without playing the note again. When you have typed a value in a box, press `Enter` to go back to playing (`Esc` in the table, where `Enter` makes a new line), or click on an empty part of the page. The table is written with one step per line, and the box turns red if a step is wrong. A new song has six ready-made instruments to start with: lead, bass, chord, kick, snare and hi-hat.

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
| `7xy` | tremolo: the volume swings, x is the rate, y the depth; `700` carries on |
| `Axy` | the volume goes up by x or down by y per tick |
| `Bxx` | jumps to place xx in the order list |
| `Cxx` | volume 00 – 40 (hex, 40 is full volume) |
| `Dxy` | goes on to row xy (decimal) in the next pattern |
| `E1x`, `E2x` | slides up or down x sixteenths of a semitone once, on the row |
| `E6x` | pattern loop: `E60` marks a row, `E6x` goes back to it x times |
| `E9x` | strikes the note again every x ticks (drum rolls) |
| `EAx`, `EBx` | the volume up or down by x once, on the row |
| `ECx` | goes silent on tick x |
| `EDx` | the note starts on tick x instead of straight away |
| `EEx` | the row lasts x rows longer (its effects go on, no new notes) |
| `Fxx` | 01 – 1F sets the speed, 20 – FF the tempo |
| `Gxx` | filter frequency: 00 is 20 Hz, 80 about 640 Hz and FF 18 000 Hz; turns on low-pass if the filter is off |
| `Hxx` | filter resonance, 00 – FF |
| `Ixx` | pulse width, 80 is 50 % |
| `8xx` | pan: 00 left, 80 the middle, FF right |
| `9xx` | a sampled instrument starts xx × 256 frames into its sample |

`G`, `H`, `I` and `8` stay on the channel until it changes instrument, so a filter can open slowly over many notes.

### Instruments

An instrument has the same settings as the `PLAY` commands: `Wave`, `Volume`, `PulseWidth`, `PwmDepth`, `PwmRate`, `Attack`, `Decay`, `Sustain`, `Release`, `Filter`, `Cutoff`, `Resonance`, `FilterEnvDepth`, `FilterEnvAttack`, `FilterEnvDecay`, `Vibrato`, `VibratoRate`, `Ring` and `Sync`, and besides those a second oscillator and two LFOs (below). Those you leave out get default values.

An instrument can also have a **table** that moves one step per tick from the moment the note starts. Each step can change the waveform, move the note a number of semitones and change the pulse width (`P30` is 30 %). That is how C64 musicians made drums and chords:

```text
"Table": ["SQUARE +24", "TRIANGLE +12", "+6", "+2", "-2", "-5", "-8", "-10", "-12"]
```

That table is a kick drum: a short click of square wave and then a triangle wave that quickly drops in pitch. `TableLoop` is the step the table starts again from when it reaches the end. Without it the table stays on the last step. A major chord as an arpeggio is `["0", "+4", "+7"]` with `"TableLoop": 0`.

#### The instrument library

**LIBRARY...** under the instrument list opens seventy ready-made instruments, sorted like on a synth: **Bass, Lead, Pad, Keys, Pluck, Brass & Strings, Chip, Drums, Percussion, Samples** and **FX**. Choose one and you hear it straight away (on the channel the cursor is in, in the octave chosen). **Add to song** makes it a new instrument in the song (double-clicking does the same), and **Replace current** puts it in place of the instrument chosen in the tracker, so the notes already written get the new sound. Then you can change it on the instrument page like any other.

**Save current...** puts the tracker's current instrument in the library, in a category you choose or a new one you type. **Load file...** and **Save file...** read and write single instrument files (`.amosinst`) anywhere, for sharing a sound with a friend. The library is the folder `Sound/Instruments`, one folder per category, so you can also tidy it in Finder or Explorer.

#### Sampled instruments

An instrument can play a **sample**, a recorded sound from a WAV file, instead of oscillator 1, like the instruments of an Amiga module. Choose **SAMPLE** as the wave and **Load sample...** on the SAMPLE card (WAV files in 8, 16, 24 or 32 bits, mono or stereo). The sample is played at the note's pitch: **NOTE** says which note the file sounds at as it is recorded, so a sample of an A-3 played as A-4 sounds an octave higher. Slides, vibrato and the LFOs bend it like any other sound, and the envelope, filter, echo, pan and oscillator 2 work as usual.

A sample plays once and then is quiet. A **loop** keeps a held note sounding: **LOOP START** and **LOOP END** (in percent of the sample) mark the part that repeats, shown in the picture of the wave. With **LOOP END** at 0 there is no loop. Effect `9xx` starts the sample further in, as in ProTracker.

The library's **Samples** category has a plucked string, a bell and a choir, made by `Tools/build_samples.py`. In the file a sampled instrument is written `"Wave": "Sample", "Sample": "Sound/Samples/choir.wav", "SampleNote": 57, "LoopStart": 0, "LoopEnd": 44000`.

#### Oscillator 2

A tracker instrument can have a **second oscillator** that is mixed with the first. Choose its **Wave** (Off turns it off), how many **Semitones** above or below the note it plays, a **Detune** in cents and how much of it is heard (**Mix**). Two sawtooth waves a few cents apart (Detune 5 – 12) give the fat, swirling sound of a classic synth lead; +7 or +12 semitones gives a fifth or an octave on top of every note. In the file it is `"Osc2Wave": "Sawtooth", "Osc2Semitones": 0, "Osc2Detune": 8, "Osc2Mix": 50`.

#### Stereo

The synth plays in stereo. **PAN** under the amp envelope places an instrument from left (-100) to right (100); the middle (0) sounds the same on both sides, just as before. Effect `8xx` pans a channel in the song as in ProTracker, and an LFO with **Pan** as its target moves the sound from side to side. The WAV file is in stereo too. In the file it is `"Pan": -40`.

#### Echo

**ECHO** makes the sound come back after a while, a little quieter each time, like a voice in a cave or the classic tape echo. **Mix** is how loud the echo is (0 is off), **Time** how long it takes to come back (up to 2000 ms), and **Feedback** how much of each echo comes back again: 0 gives one echo, 70 a long trail. **Sync rows** above 0 sets the time in rows of the song instead, so the echo falls on the beat: 3 rows gives the dotted rhythm that so much synth music uses. The echo goes on after the note has ended. In the file it is `"EchoMix": 35, "EchoTime": 300, "EchoFeedback": 45, "EchoSyncRows": 3`.

#### LFO 1 and LFO 2

An **LFO** (low frequency oscillator) is a slow wave that moves one setting up and down while the note sounds. Each instrument has two. Choose what it **moves**, its **wave** and its **depth**:

| Moves | Depth in | Sounds like |
|---|---|---|
| Pitch | cents | vibrato; with a square wave, a trill |
| Volume | % | tremolo, a pulsing sound |
| Cutoff | semitones | a wah that opens and closes the filter (the filter must be on) |
| Resonance | % | a whistling filter that comes and goes |
| Pulse width | % | the moving, chorus-like pulse sound of the C64 |
| Osc 2 pitch | cents | oscillator 2 swaying against oscillator 1 |
| Osc 2 mix | % | the sound shifting between the two oscillators |
| Pan | % | the sound moving between the left and right speaker (auto-pan) |

The waves are sine, triangle, square, saw up, saw down and **random**, which jumps to a new value every period, the bubbling computer sound of old science fiction. **Rate** is the speed in Hz. **Sync rows** above 0 ties the speed to the song instead: 4 means one period every four rows, so the effect pulses in time with the music even when the tempo changes. **Delay** lets the LFO grow in over that many milliseconds, so that a long note starts straight and then gets vibrato. **Restart on note** starts the wave from the beginning on every note; turned off, it runs on freely. The graph shows the wave, the delay and what it moves.

In the file an LFO is written `"Lfo1": { "Target": "Cutoff", "Shape": "Triangle", "Depth": 24, "SyncRows": 4 }`. The older settings `Vibrato` and `PwmDepth` still work as before.

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

### Reading MOD files

**LOAD** also opens ProTracker modules (`.mod`, with 4, 6 or 8 channels), so you can open a song from the Amiga and change it. The patterns, the order list and the effects are read as they are, since the tracker's effects are ProTracker's. Each sample becomes a sampled instrument with its loop, its volume and its finetune; the samples are written as WAV files in `Sound/Samples/<module name>/`, which the song refers to. The pitches are kept: ProTracker's C-2 is C-4 here.

The module becomes a new song that is not saved yet. **SAVE** keeps it as a tracker song (`.amostrk`), and from then on it is an ordinary song: you can change notes, swap a sample for a synth sound, or play it from your program with `TRACK PLAY`.

A few things differ from the Amiga. `E0x` (the Amiga's filter), `E3x`, `E4x`, `E5x`, `E7x`, `E8x` and `EFx` are left out, and the status line says if the module used any. A slide is turned into sixteenths of a semitone at the note it starts from, so a very long slide can end slightly off. XM, S3M and IT files cannot be read into the tracker, but `MUSIC PLAY` plays them.

A song played once (not looping) ends when it jumps back to a place it has already played, with `Bxx` or `Dxx`, as in ProTracker; that is how a song says it repeats.

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

The sounds play on synth channels 5 – 8, and each new sound takes the channel that becomes free first. A tracker song on channels 1 – 4 therefore carries on undisturbed. While a song with more channels plays, the sounds only use the channels above the song's; with six channels, 7 and 8. A song with all eight channels lends channel 8 to the sound while it plays, as in many C64 games, and that channel of the song comes back at its next note. If you use `PLAY MUSIC` on channels 5 – 8, the sound effect borrows the channel and gives it back with the same settings afterwards.

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
MUSIC PLAY "Sound/Tracker/MODs/mus2.mod"
SAM PLAY "Sound/ding.mp3"
```

Music modules are the music format of the Amiga days, and there are thousands of them freely available online. If your music is an MP3, use `SAM LOOP`. Remember to check that you are allowed to use the music if you are going to share your game.

> **Try it:** Compose a little tune of your own on two channels: melody on channel 1 and bass on channel 2.
