# Drawing graphics

Now the fun starts. The game window is a canvas where you can draw dots, lines, rectangles and circles in any colour.

## Coordinates

By default the screen is 640 dots wide and 480 dots high. Every dot has a position given by two numbers: **x** (how far to the right) and **y** (how far down). The top left corner is `0, 0`, and the bottom right is `639, 479`.

```text
(0,0) ──────────────────────► x   (639,0)
  │
  │        (320,240) = the middle
  │
  ▼
  y
(0,479)                           (639,479)
```

Note that y grows *downwards*, the opposite of maths lessons.

If you want a different screen size, give it with `SCREEN`:

```basic
SCREEN 320, 240
```

A smaller screen gives bigger, more "retro" pixels, because the picture is scaled up to the window.

## Colour with INKG

Everything you draw gets the colour you have chosen with `INKG` (*ink graphics*). A colour can be given in several ways:

```basic
INKG 2                  ; a number 0 - 15 in the standard palette
INKG "Red"              ; a colour name
INKG "#FF8800"          ; red, green and blue in hexadecimal
INKG "255,136,0"        ; red, green and blue as numbers 0 - 255
```

The standard palette has 16 colours, much like on a Commodore 64:

| No | Colour | No | Colour |
|---|---|---|---|
| 0 | black | 8 | orange |
| 1 | white | 9 | brown |
| 2 | red | 10 | light red |
| 3 | cyan | 11 | dark grey |
| 4 | purple | 12 | grey |
| 5 | green | 13 | light green |
| 6 | blue | 14 | light blue |
| 7 | yellow | 15 | light grey |

The colour names are the usual web colour names: `"Red"`, `"Green"`, `"Blue"`, `"Yellow"`, `"White"`, `"Orange"`, `"Pink"`, `"Purple"`, `"Gray"`, `"SkyBlue"` and many more.

> **Note:** Pure black (`0,0,0`) counts as transparent when you draw with `PLOT` and in images that are loaded. If you want black, use an almost black colour, for example `"#101010"`.

## Dots and lines

`PLOT` draws a dot, `LINE` a line between two points:

```basic
INKG "Yellow"
PLOT 320, 240
LINE 0, 0, 639, 479
INKG "Cyan"
LINE 0, 479, 639, 0
```

With a loop a few lines can draw a lot:

```basic
FOR I = 0 TO 640 STEP 16
  INKG I MOD 16
  LINE I, 0, 640 - I, 479
NEXT I
```

## Rectangles and circles

| Command | Draws |
|---|---|
| `BOX x1, y1, x2, y2` | a rectangle (only the edge) |
| `BAR x1, y1, x2, y2` | a filled rectangle |
| `CIRCLE x, y, r` | a circle with its centre at x, y and radius r |
| `CIRCLEF x, y, rx, ry` | a filled ellipse (equal rx and ry give a filled circle) |
| `ELLIPSE x, y, rx, ry` | an ellipse (only the edge) |
| `FILL x, y` | fills the area around the point with colour, like a paint bucket |

```basic
INKG "SkyBlue"
BAR 0, 0, 639, 300
INKG "ForestGreen"
BAR 0, 300, 639, 479
INKG "Yellow"
CIRCLEF 500, 90, 50, 50
INKG "White"
CIRCLEF 150, 100, 60, 30
CIRCLEF 200, 90, 50, 30
INKG "SaddleBrown"
BAR 280, 220, 400, 320
INKG "DarkRed"
LINE 270, 220, 340, 160
LINE 340, 160, 410, 220
LINE 270, 220, 410, 220
FILL 340, 200
```

This draws a landscape with sky, grass, a sun, a cloud and a house with a roof.

## Text on the graphics screen

`PRINT` writes in the text console, which lies on top of everything. To write text *in* the graphics there are two commands:

- `TEXT x, y, "text"` writes the text with its top left corner at the point x, y, in the INKG colour. It uses the font chosen with `FONT SET`, or Arial at size 20 if you have not chosen one.
- `PRINTG "text"` writes on the next line, like `PRINT` does. With `LOCATEG column, row` you choose where. Here columns and rows count from 0, and a cell is 8 × 16 dots.

```basic
INKG "White"
TEXT 240, 220, "AMOS LIKE BASIC"
LOCATEG 2, 2
PRINTG "Column 2, row 2"
```

For nice game-style text, with your own letters that can wave and rotate, use `FONT`, which has its own chapter.

## Clearing

`CLSG` clears everything drawn on the screen. It is transparent afterwards, not black.

## Movement: WAIT VBL

To make something move you draw the picture again and again, moved a little each time. The screen is redrawn 60 times a second, and `WAIT VBL` waits until it is time for the next picture. (*VBL* stands for *vertical blank*, a short pause when old monitors started drawing the picture again.)

A bouncing ball:

```basic
X = 100 : Y = 100
DX = 4 : DY = 3
INKG "Orange"
Loop:
  CLSG
  CIRCLEF X, Y, 20, 20
  X = X + DX
  Y = Y + DY
  IF X < 20 OR X > 620 THEN DX = -DX
  IF Y < 20 OR Y > 460 THEN DY = -DY
  WAIT VBL
GOTO Loop
```

Every lap of the loop clears the screen, draws the ball, moves it and waits for the next picture. When the ball reaches an edge its direction turns around. That is the basic trick of every game!

> **Tip:** Clearing and redrawing the whole screen works for simple things, but for things that move there are **sprites**, which are drawn by the graphics card and do not need clearing. They come in the chapter after next. A game that redraws a lot every frame can use **double buffering** to avoid flicker; the chapter on Frog Hop shows how.

> **Try it:** Change the ball so that it changes colour every time it bounces. Tip: `INKG INT(RND(15)) + 1`.

> **Try it:** Draw a chessboard with two nested `FOR` loops and `BAR`.
