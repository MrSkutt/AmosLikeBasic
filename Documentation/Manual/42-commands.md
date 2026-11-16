# Commands

All commands in alphabetical order. Commands that belong together, such as `SPRITE POS` and `SPRITE ZOOM`, are listed under their first word.

## A

### ANIM

Animates a sprite automatically. The animation is stepped at every `WAIT VBL`.

```text
ANIM DEF sprite, "name", (picture, time)(picture, time)..., LOOP
ANIM DEF sprite, "name", (picture, time)(picture, time)..., ONCE [, "next"]
ANIM SET sprite, "name"
ANIM STOP sprite
ANIM GROUP "group", sprite, sprite, ...
ANIM GROUP SET "group", "name"
```

`ANIM DEF` describes an animation. Each `(picture, time)` is a picture in the sprite's sprite sheet (from 0) and how many frames it is shown. `LOOP` starts again when the animation ends, `ONCE` stops on the last picture or moves on to the animation `"next"`. `ANIM SET` starts an animation and `ANIM STOP` stops it. `ANIM GROUP` gathers several sprites, and `ANIM GROUP SET` starts the same animation on all of them.

```basic nocheck
ANIM DEF 1, "walk", (1,8)(2,8)(3,8), LOOP
ANIM DEF 1, "jump", (4,20), ONCE, "walk"
ANIM SET 1, "walk"
```

## B

### BAR

```text
BAR [#layer,] x1, y1, x2, y2
```

Draws a filled rectangle with the corners x1, y1 and x2, y2 in the `INKG` colour.

### BOX

```text
BOX [#layer,] x1, y1, x2, y2
```

Draws the edge of a rectangle.

## C

### CIRCLE

```text
CIRCLE [#layer,] x, y, r
```

Draws a circle with its centre at x, y and radius r.

### CIRCLEF

```text
CIRCLEF [#layer,] x, y, rx, ry
```

Draws a filled ellipse with its centre at x, y and the radii rx (width) and ry (height). Equal radii give a filled circle.

### CLOSE

```text
CLOSE channel
```

Closes a file opened with `OPEN`. Nothing happens if the channel is not open.

### CLS

Clears the text console and moves the cursor to the top left corner.

### CLSG

```text
CLSG [#layer]
```

Clears everything drawn in the layer (the selected one, or the one given). The layer becomes transparent, not black. Sprites and font text are not affected.

### CLSGA

Clears all layers.

## D

### DATA

```text
DATA value, value, ...
```

Values that are read with `READ`. `DATA` lines must be under a label, and the store is chosen with `RESTORE label` before the first `READ`.

```basic
RESTORE Colours
READ A$, B$, C$
PRINT A$ & " " & B$ & " " & C$
END
Colours:
DATA "red", "green", "blue"
```

### DEC

```text
DEC variable
```

Takes 1 from a number variable. The same as `variable = variable - 1`.

### DIM

```text
DIM name(size)
DIM name(size, size, ...)
DIM name(size) = [value, value, ...]
```

Creates an array. `DIM A(10)` gives ten places, `A(0)` to `A(9)`. Text arrays get names ending in `$`. With `= [...]` the array gets starting values; for several dimensions they are written in nested square brackets.

### DO ... LOOP

```text
DO
  commands
LOOP
```

Repeats forever. Leave it with `EXIT DO`.

## E

### ELLIPSE

```text
ELLIPSE [#layer,] x, y, rx, ry
```

Draws the edge of an ellipse.

### END

Ends the program. `END` followed by another word ends a block: `END IF`, `END PROC`, `END FUNCTION` and `END SELECT`.

### EXIT

```text
EXIT DO
EXIT WHILE
EXIT REPEAT
EXIT PROC
```

Leaves the loop or procedure straight away and carries on after it. You leave a `FOR` loop with `GOTO` to a label after `NEXT`.

## F

### FILL

```text
FILL [#layer,] x, y
```

Fills the area around the point x, y with the `INKG` colour, like a paint bucket. The fill stops at pixels of another colour.

### FONT

Bitmap fonts: an image with all the characters in a grid. Font text is drawn on top of the layer and stays until `FONT CLEAR`.

```text
FONT LOAD font, "file", width, height
FONT MAP font, "characters"
FONT PRINT [#layer,] font, x, y, text
FONT CHAR font, x, y, character
FONT ZOOM font, percent [, percentY]
FONT ROTATE font, degrees
FONT CLEAR
FONT EFFECT ADD font, WAVE, ampX, freqX, ampY, freqY
FONT EFFECT ADD font, BLUR, radius
FONT EFFECT ADD font, COLOR, brightness, contrast, saturation
FONT EFFECT UPDATE font, index, parameter, value
FONT EFFECT CLEAR font
FONT SET width, height, "system font"
FONT STYLE "style"
```

| Command | Does |
|---|---|
| `FONT LOAD` | loads the font image; width and height are the size of a cell |
| `FONT MAP` | says which characters the cells hold, in order from left to right and top to bottom |
| `FONT PRINT` | writes text with its top left corner at x, y |
| `FONT CHAR` | writes a single character |
| `FONT ZOOM` | size, 100 = normal |
| `FONT ROTATE` | turns the text |
| `FONT CLEAR` | removes all font text (but not the effects) |
| `FONT EFFECT` | effects, see `SPRITE EFFECT` for the parameters |
| `FONT SET` | chooses the size and font for `TEXT` and `PRINTG`: one of the built-in retro fonts (see *Tables*) or a font installed on the computer |
| `FONT STYLE` | `"Bold"`, `"Italic"`, `"Bold Italic"` or `"Normal"` for `TEXT` and `PRINTG` |

### FOR ... NEXT

```text
FOR variable = start TO end [STEP step]
  commands
NEXT [variable]
```

Counts the variable from start to end and runs the commands for each value. `STEP` says how much the variable changes each lap, 1 if nothing is given. A negative step counts down. The loop always runs at least once.

```basic
FOR I = 10 TO 0 STEP -5
  PRINT I
NEXT I
```

### FUNCTION

```text
FUNCTION Name(parameter, parameter, ...)
  commands
  RETURN value
END FUNCTION
```

Defines a function of your own, called with `Name(value, ...)` in an expression. Only the simplest commands work in a function: assignment, `IF`, `FOR`, `WHILE`, `INC`, `DEC` and `RETURN`.

## G

### GOSUB ... RETURN

```text
GOSUB label
...
label:
  commands
RETURN
```

Jumps to the label and carries on after the `GOSUB` when a `RETURN` comes.

### GOTO

```text
GOTO label
```

Jumps to the label.

## I

### IF

```text
IF condition THEN command [: command] [ELSE command]

IF condition THEN
  commands
ELSE
  commands
END IF
```

Runs the commands if the condition is true. If nothing comes after `THEN` it is a block that ends with `END IF` (or `ENDIF`). `ELSE` is optional. A number as a condition counts as true if it is not 0.

### IMAGE LOAD

```text
IMAGE LOAD bank, "file"
IMAGE LOAD bank, "file", width, height, count
```

Loads an image or a sprite sheet into an image bank that many sprites can share; see `SPRITE IMAGE`.

### INC

```text
INC variable
```

Adds 1 to a number variable.

### INK

```text
INK colour
```

Chooses the text colour in the text console.

### INKG

```text
INKG colour
```

Chooses the colour for everything drawn in the graphics: `PLOT`, `LINE`, `BOX`, `BAR`, `CIRCLE`, `CIRCLEF`, `ELLIPSE`, `FILL`, `TEXT` and `PRINTG`.

### INPUT

```text
INPUT ["question",] variable
INPUT #channel, variable
```

Prints the question in the text console and waits until the user has typed something and pressed Enter. With `#channel` the next line of a file is read instead.

### INPUTG

```text
INPUTG ["question",] variable
```

Like `INPUT`, but writes in the graphics with the `TEXT` font and the `INKG` colour.

## L

### LET

```text
[LET] variable = value
```

Gives a variable a value. The word `LET` is not needed.

### LINE

```text
LINE [#layer,] x1, y1, x2, y2
```

Draws a line.

### LOAD

```text
LOAD [#layer,] "file"
```

Loads an image (PNG or JPG) into the layer, with the image's top left corner at the layer's top left corner.

### LOCATE

```text
LOCATE row, column
```

Moves the cursor of the text console. Row and column count from 1. The console has 30 rows and 80 columns.

### LOCATEG

```text
LOCATEG [#layer,] column, row
```

Moves the cursor for `PRINTG`. Note the order: column first. Counts from 0, in character cells.

## M

### MAP LOAD

```text
MAP LOAD "file.amosmap"
```

Loads a map made in the map editor. Then draw it with `TILE DRAW`.

### MUSIC

```text
MUSIC PLAY "file"
MUSIC STOP
```

Plays a music module (MOD, XM, S3M or IT) or stops it. For the synth's music, see `PLAY`.

## O

### ON ERROR

```text
ON ERROR GOTO label
ON ERROR RESUME NEXT
ON ERROR BREAK
```

Decides what happens when a command goes wrong. `GOTO` jumps to the label, where the variable `ERR$` holds the error message. `RESUME NEXT` skips the command and carries on. `BREAK` stops the program with an error message, which is the default.

### OPEN

```text
OPEN IN channel, "file"
OPEN OUT channel, "file"
OPEN APPEND channel, "file"
```

Opens a text file for reading (`IN`), writing a new one (`OUT`) or writing at the end of an existing one (`APPEND`). The channel is a number that you use in `PRINT #`, `INPUT #`, `EOF` and `CLOSE`.

## P

### PAPER

```text
PAPER colour
```

Chooses the background colour in the text console.

### PAPERG

```text
PAPERG colour
```

Chooses the background colour behind the text from `PRINTG` and `INPUTG`. The default is transparent.

### PARTICLE

```text
PARTICLE type, density
```

Weather in the selected layer: type 1 is snow, 2 rain and 3 twinkling stars. Type 0 turns it off. The density decides how big the particles and the gaps are.

### PLAY

Sound effects and music from the built-in synth.

```text
PLAY COIN | JUMP | LASER | HIT | BLIP | POWERUP | EXPLOSION
PLAY BPM tempo
PLAY WAVE channel, "type"
PLAY VOLUME channel, volume
PLAY MUSIC channel, notes
PLAY LOOP channel, notes
PLAY STOP [channel]
PLAY ADSR channel, attack, decay, sustain, release
PLAY GATE channel, percent
PLAY PULSE channel, width [, depth, rate]
PLAY FILTER channel, "type", frequency, resonance
PLAY FILTERENV channel, depth [, attack, decay]
PLAY VIBRATO channel, depth, rate
PLAY GLIDE channel, milliseconds
PLAY BEND channel, semitones
PLAY ARP channel, "steps" [, rate]
PLAY RING channel, source
PLAY SYNC channel, source
```

The synth has eight channels, 1 – 8. Everything except `MUSIC`, `LOOP` and `STOP` is a setting that applies to the channel until it is changed, and it is heard straight away, even in the middle of a note.

| Command | Does |
|---|---|
| `PLAY COIN` and the others | plays a ready-made sound effect |
| `PLAY BPM` | tempo in beats per minute |
| `PLAY WAVE` | sound type: `Square`, `Pulse`, `Triangle`, `Sawtooth`, `Sine`, `Noise` or `Drums` |
| `PLAY VOLUME` | volume 0 – 1 |
| `PLAY MUSIC` | plays notes on a channel once, see *Tables*; the notes can be a text variable and hold effects in `{ }` |
| `PLAY LOOP` | like `MUSIC`, but starts again when the notes run out |
| `PLAY STOP` | stops one channel, or all synth music |
| `PLAY ADSR` | envelope: attack, decay and release in milliseconds, sustain 0 – 100 (default 20, 50, 70, 100) |
| `PLAY GATE` | how much of the note length the note is held, 5 – 100 (default 90) |
| `PLAY PULSE` | pulse width in percent for `Pulse` (default 25), and pulse width modulation: depth in percent, rate in Hz |
| `PLAY FILTER` | `"LOW"`, `"HIGH"`, `"BAND"` or `"OFF"`; frequency 20 – 18 000 Hz, resonance 0 – 100 |
| `PLAY FILTERENV` | the filter opens by up to depth Hz on every note; attack and decay in milliseconds (default 5 and 200); depth 0 turns it off |
| `PLAY VIBRATO` | depth in hundredths of a semitone, rate in Hz; depth 0 turns it off |
| `PLAY GLIDE` | gliding between notes; 0 turns it off |
| `PLAY BEND` | moves the pitch a number of semitones; 0 is normal |
| `PLAY ARP` | arpeggio: semitone steps such as `"0 4 7"`, rate in steps per second (default 50); `""` turns it off |
| `PLAY RING` | ring modulation with another channel; 0 turns it off |
| `PLAY SYNC` | hard sync with another channel; 0 turns it off |

The settings are reset when the program starts.

```basic nocheck
PLAY BPM 140
PLAY WAVE 1, "Square"
PLAY MUSIC 1, "C5:8 E5 G5 C6:4 {CUT 500; ARP 0 4 7} C5:2"
```

### PLOT

```text
PLOT [#layer,] x, y
```

Draws a dot.

### PRINT

```text
PRINT value
PRINT AT row, column, value
PRINT #channel, value
```

Writes a value in the text console and moves to the next line. `PRINT` with no value writes an empty line. `AT` writes at a given place, like `LOCATE`. With `#channel` a line is written to a file.

### PRINTG

```text
PRINTG [#layer,] value
PRINTG [#layer,] AT column, row, value
```

Like `PRINT`, but writes in the graphics with the `TEXT` font and the `INKG` colour.

### PROC

```text
PROC Name[(parameter, parameter, ...)]
  commands
END PROC
```

Defines a procedure. It is called by its name, with the arguments after a space: `Name value, value`. The parameters only apply inside the procedure; all other variables are shared. `EXIT PROC` leaves the procedure straight away.

### PROFILE

```text
PROFILE START "name"
PROFILE END "name"
```

Measures how long the code between `START` and `END` takes. The result is written in the log and kept in the variable `TIME_name`, in milliseconds.

## R

### RASTER

Colour bands that blend between colours, behind what is drawn in the layer.

```text
RASTER [#layer,] number, x, y, height, "colour, colour, ..."
RASTER MOVE number, y
RASTER WRAP number, on
RASTER DEL number
RASTER CLEAR
RASTER GFXMODE layer, on
RASTER SPACE layer, mode
```

| Command | Does |
|---|---|
| `RASTER number, ...` | creates or changes band 1 – 100 starting at line y; `x` is not used yet |
| `RASTER MOVE` | moves the band |
| `RASTER WRAP` | 1: the band continues at the top when it goes off the bottom |
| `RASTER DEL` | removes the band |
| `RASTER CLEAR` | removes all bands in the layer |
| `RASTER GFXMODE` | 1: the bands colour what is drawn instead of the background |
| `RASTER SPACE` | the layer's background: 0 normal, 1 space with stars, 2 clouds |

A layer can have 24 colour transitions in total; a band with three colours uses two.

### READ

```text
READ variable, variable, ...
```

Reads the next values from `DATA` in the store chosen with `RESTORE`.

### REM

The rest of the line is a comment.

### REPEAT ... UNTIL

```text
REPEAT
  commands
UNTIL condition
```

Runs the commands and repeats until the condition is true. The commands always run at least once. Leave it with `EXIT REPEAT`.

### RESTORE

```text
RESTORE label
```

Chooses which `DATA` store `READ` reads from and starts again from its beginning.

### RETURN

Ends a `GOSUB` and jumps back, or ends a `FUNCTION` with a value: `RETURN value`.

## S

### SAM

```text
SAM PLAY "file"
SAM LOOP "file"
SAM STOP "file"
```

Plays a sound file (WAV, MP3 or OGG) once or over and over, or stops it. `SAM STOP ""` stops all.

### SCREEN

```text
SCREEN width, height
SCREEN SELECT layer
SCREEN ON layer
SCREEN OFF layer
SCREEN ALPHA layer, value
SCREEN FADE layer, target, frames
SCREEN CRT on [, lines [, curve]]
```

| Command | Does |
|---|---|
| `SCREEN width, height` | the game's resolution, normally 640 × 480; the picture is scaled up to the window |
| `SCREEN SELECT` | chooses the layer the drawing commands draw in |
| `SCREEN ON` / `OFF` | shows or hides a layer |
| `SCREEN ALPHA` | the layer's transparency, 0 (invisible) – 255 |
| `SCREEN FADE` | fades smoothly to the target value over that many frames |
| `SCREEN CRT` | 1 = CRT effect, 0 = off; lines and curve 0 – 100 |

### SCROLL

```text
SCROLL layer, x, y
```

Moves the whole layer so that the point x, y in the layer ends up at the screen's top left corner. The layer repeats, so it never ends.

### SELECT ... CASE

```text
SELECT value
  CASE value
    commands
  CASE value
    commands
  DEFAULT
    commands
END SELECT
```

Runs the commands under the `CASE` with the same value, or under `DEFAULT` if none matches.

### SPRITE

Figures drawn by the graphics card on top of the layer, numbered by you. Higher numbers are drawn on top of lower ones.

```text
SPRITE LOAD no, "file" [, width, height, count]
SPRITE IMAGE no, bank [, picture]
SPRITE ADDFRAME no, "file"
SPRITE ON no
SPRITE OFF no
SPRITE POS no, x, y
SPRITE MOVE no, dx, dy
SPRITE FRAME no, picture
SPRITE ZOOM no, percent [, percentY]
SPRITE ROTATE no, degrees
SPRITE ALPHA no, value
SPRITE FADE no, target, frames
SPRITE LAYER no, layer
SPRITE HANDLE no, x, y
SPRITE GROUP no, "group"
SPRITE UNGROUP no, "group"
SPRITE CLEARGROUP "group"
SPRITE EFFECT ADD no, type, parameters...
SPRITE EFFECT UPDATE no, index, parameter, value
SPRITE EFFECT CLEAR no
SPRITE [#layer,] no, x, y, bank
```

| Command | Does |
|---|---|
| `SPRITE LOAD` | loads an image, or a sprite sheet with width, height and number of pictures |
| `SPRITE IMAGE` | uses picture number `picture` (from 0) from an image bank made with `IMAGE LOAD` |
| `SPRITE ADDFRAME` | adds a picture from a file of its own at the end of the sprite's pictures |
| `SPRITE ON` / `OFF` | shows or hides the sprite |
| `SPRITE POS` | moves the sprite's top left corner to x, y |
| `SPRITE MOVE` | moves the sprite some distance from where it is |
| `SPRITE FRAME` | chooses the picture in the sprite sheet, from 0 |
| `SPRITE ZOOM` | size, 100 = normal; a negative value mirrors |
| `SPRITE ROTATE` | turns around the centre |
| `SPRITE ALPHA` | transparency, 0 – 255 |
| `SPRITE FADE` | fades smoothly to the target value over that many frames |
| `SPRITE LAYER` | moves the sprite to a layer; the position then counts in the layer's coordinates and follows when it scrolls |
| `SPRITE HANDLE` | moves the point the collision tests count from |
| `SPRITE GROUP` | puts the sprite in a group for `HITGROUP` and the like |
| `SPRITE UNGROUP` | takes the sprite out of the group |
| `SPRITE CLEARGROUP` | empties the group |
| `SPRITE no, x, y, bank` | short form: picture 1 from the bank, position and `SPRITE ON` in one command |

If the file name in `SPRITE LOAD` contains the size, like `hero_W32_H48_B8.png`, `SPRITE LOAD no, "file"` is enough.

**Effects**, for both sprites and fonts:

| Type | Parameters | Does |
|---|---|---|
| `BLUR` | `radius` | blurry |
| `WAVE` | `ampX, freqX, ampY, freqY` | ripples; amplitude in dots |
| `COLOR` | `brightness, contrast, saturation` | changes the colours, 1 = unchanged |

The effects are numbered from 0 in the order they are added. With `EFFECT UPDATE` the parameters `RADIUS`, `AMPLITUDEX`, `FREQUENCYX`, `AMPLITUDEY`, `FREQUENCYY`, `PHASEX`, `PHASEY`, `TIME`, `BRIGHTNESS`, `CONTRAST` and `SATURATION` can be changed.

### SWAP

```text
SWAP variable, variable
```

Swaps the values of two variables or array places of the same kind.

```basic
A = 1 : B = 2
SWAP A, B
PRINT A & " " & B
DIM T(3) = [10, 20, 30]
SWAP T(0), T(2)
PRINT JOIN$(T, ",")
```

## T

### TEXT

```text
TEXT [#layer,] x, y, value
```

Writes text in the graphics with its top left corner at x, y, with the font from `FONT SET` and the `INKG` colour. Without `FONT SET` it is Arial at size 20.

### TILE

Levels built from cells.

```text
TILE LOAD "file", width, height
TILE MAP width, height
TILE SET x, y, number
TILE DRAW [#layer,] x, y
```

| Command | Does |
|---|---|
| `TILE LOAD` | loads the tileset; width and height are the size of a cell in dots |
| `TILE MAP` | creates an empty map, measured in cells |
| `TILE SET` | puts tile `number` (from 0) in cell x, y; –1 is empty |
| `TILE DRAW` | draws the whole map into the layer with its top left corner at x, y |

There is one map at a time. The function `TILE()` reads it.

### TIMER

`TIMER` as a command sets the variable `TIMER` to the clock in milliseconds. For measuring time `MILLIS()` is simpler.

### TRACK

Plays songs from the tracker on synth channels 1 – 4, and tracker instruments as sound effects on channels 5 – 8. See the chapter on sound.

```text
TRACK LOAD "file"
TRACK PLAY [position]
TRACK STOP
TRACK VOLUME volume
TRACK LOOP 0 | 1
TRACK SFX LOAD "file"
TRACK SFX instrument, note [, ms [, channel]]
TRACK SFX VOLUME volume
TRACK SFX STOP
```

| Command | Does |
|---|---|
| `TRACK LOAD` | loads a song (`.amostrk`) |
| `TRACK PLAY` | plays the song from the start, or from a place in the order list (from 0) |
| `TRACK STOP` | stops the song |
| `TRACK VOLUME` | the volume of the song, 0 – 1 |
| `TRACK LOOP` | 1 (the default) starts again when the song ends, 0 plays it once |
| `TRACK SFX LOAD` | loads a sound bank: a tracker file whose instruments are sound effects |
| `TRACK SFX` | plays an instrument from the sound bank (name or number) with a note (`"C-4"`, `"C4"` or a number) on channels 5 – 8 |
| `TRACK SFX VOLUME` | the volume of the sound effects, 0 – 1 |
| `TRACK SFX STOP` | silences the sound effects |

## W

### WAIT

```text
WAIT VBL
WAIT milliseconds
WAIT MUSIC [channel]
```

`WAIT VBL` shows what has been drawn and waits for the next frame, 60 times a second. It is the heart of every game loop: sprites, animations and fades are updated here. It follows the screen's own refreshes, so movement is smooth: on a 120 Hz screen it waits for every second refresh, so a game runs at the same speed everywhere. `WAIT` with a number waits that many milliseconds. `WAIT MUSIC` waits until the synth music (or one channel) has finished.

### WHILE ... WEND

```text
WHILE condition
  commands
WEND
```

Repeats the commands as long as the condition is true. If it is false from the start they never run. Leave it with `EXIT WHILE`.
