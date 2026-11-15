# Fonts and text in games

Text in games is often drawn with letters of their own: an image with all the characters next to each other, drawn in the style of the game. AMOS Like Basic calls this a **font**. The text can be zoomed, rotated and given effects, just like sprites.

## Loading a font

A font image is a grid where every cell is a character. You load it with the size of a cell, and then tell it which characters the cells hold, in order, with `FONT MAP`:

```basic
FONT LOAD 0, "Graphics/ForestHop/font.png", 24, 32
FONT MAP 0, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
FONT PRINT 0, 100, 200, "HELLO THERE!"
```

`FONT PRINT font, x, y, text` writes the text with its top left corner at x, y. Small letters are shown as capitals, and characters that are not in `FONT MAP` are skipped. To write in another layer, give it first: `FONT PRINT #2, 0, 100, 200, "HELLO"`.

## Text that changes

Font text stays on the screen until you remove it with `FONT CLEAR`. For text that changes, such as a score or a moving heading, you therefore do this every frame: first `FONT CLEAR`, then `FONT PRINT` with the new text.

```basic
FONT LOAD 0, "Graphics/ForestHop/font.png", 24, 32
FONT MAP 0, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
SCORE = 0
Loop:
  SCORE = SCORE + 7
  FONT CLEAR
  FONT PRINT 0, 20, 20, "SCORE " & SCORE
  WAIT VBL
GOTO Loop
```

## Zoom and rotation

`FONT ZOOM font, percent` changes the size and `FONT ROTATE font, degrees` turns the text. They apply to everything written with the font afterwards:

```basic
FONT LOAD 1, "Graphics/ForestHop/font.png", 24, 32
FONT MAP 1, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
V = 0
Loop:
  V = V + 2
  FONT CLEAR
  FONT ZOOM 1, 150 + SIN(V) * 50
  FONT ROTATE 1, SIN(V * 2) * 15
  FONT PRINT 1, 180, 200, "SPIN!"
  WAIT VBL
GOTO Loop
```

## Effects

Fonts can get the same effects as sprites. The effect belongs to the font and stays even after `FONT CLEAR`, so you add it once before the loop:

```basic
FONT LOAD 1, "Graphics/ForestHop/font.png", 24, 32
FONT MAP 1, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
FONT ZOOM 1, 200
FONT EFFECT ADD 1, WAVE, 0, 0, 16, 0.02
Loop:
  FONT CLEAR
  FONT PRINT 1, 80, 180, "FOREST HOP"
  WAIT VBL
GOTO Loop
```

`WAVE, ampX, freqX, ampY, freqY` makes the text ripple. Here it only ripples up and down. The wave moves by itself.

| Command | Does |
|---|---|
| `FONT EFFECT ADD font, WAVE, ampX, freqX, ampY, freqY` | rippling text |
| `FONT EFFECT ADD font, BLUR, radius` | blurry text |
| `FONT EFFECT ADD font, COLOR, brightness, contrast, saturation` | changed colours |
| `FONT EFFECT UPDATE font, index, parameter, value` | changes an effect |
| `FONT EFFECT CLEAR font` | removes all effects |

If you add an effect type that the font already has, the existing one is updated instead of a new one being added. So it does no harm if `FONT EFFECT ADD` happens to be inside the loop.

## Making your own font

Draw the characters in a paint program, in cells of the same size next to each other, and save it as a PNG with a transparent background. The Forest Hop font is made by a Python script, `Graphics/ForestHop/generate_assets.py`, which you can look at and change.

> **Try it:** Make a text that scrolls in from the right edge and stops in the middle of the screen.
