# Screens, layers and scrolling

One of the great strengths of AMOS was that you could have several screens on top of each other. AMOS Like Basic has the same idea: the game window is made of **layers**, like transparent sheets of plastic on top of each other. Whatever is transparent in a layer lets the layers below show through.

With layers you can for example have a sky at the back, mountains in front of it, a level in front of the mountains and the score at the very front, and scroll each layer on its own.

## Choosing a layer

The layers are numbered from 0. Layer 0 is at the back, and the higher the number, the further forward. `SCREEN SELECT` chooses which layer you draw in, and creates it if it does not exist:

```basic
SCREEN SELECT 0
INKG "SkyBlue"
BAR 0, 0, 639, 479

SCREEN SELECT 1
INKG "ForestGreen"
CIRCLEF 320, 480, 400, 150

SCREEN SELECT 2
INKG "White"
TEXT 260, 40, "THREE LAYERS"
```

To draw a single thing in another layer without switching layer, write `#number` first among the arguments:

```basic
SCREEN SELECT 0
BAR #3, 10, 10, 100, 50
```

This works for most drawing commands: `PLOT`, `LINE`, `BOX`, `BAR`, `CIRCLE`, `CIRCLEF`, `ELLIPSE`, `FILL`, `TEXT`, `PRINTG`, `LOAD`, `FONT PRINT`, `SPRITE` and `RASTER`.

## Images as background

`LOAD` loads an image (PNG or JPG) into the selected layer, with the image's top left corner at the screen's top left corner:

```basic nocheck
LOAD "Graphics/Layer/Background.png"
LOAD #1, "Graphics/Backgrounds/berg.png"
```

The file name counts from the project folder. The image is not scaled, so make it the same size as the screen.

## Scrolling

`SCROLL layer, x, y` shifts the whole content of a layer. Whatever disappears off one side comes in on the other, so a layer drawn to match at its edges can scroll forever.

If the layers at the back scroll slowly and the ones in front quickly, it looks like depth, so-called **parallax**. Here three layers with mountains, hills and trees are drawn and scrolled at different speeds:

```basic
SCREEN SELECT 0
INKG "#7D86C9"
FOR X = 0 TO 638 STEP 2
  H = SIN(X * 0.5625) * 40 + 200
  BAR X, 480 - H, X + 1, 479
NEXT X

SCREEN SELECT 1
INKG "#4E9A5C"
FOR X = 0 TO 638 STEP 2
  H = SIN(X * 1.125 + 60) * 25 + 120
  BAR X, 480 - H, X + 1, 479
NEXT X

SCREEN SELECT 2
INKG "#245C33"
FOR T = 0 TO 7
  CIRCLEF T * 80 + 40, 400, 30, 45
NEXT T

S = 0
Loop:
  S = S + 2
  SCROLL 0, S / 4, 0
  SCROLL 1, S / 2, 0
  SCROLL 2, S, 0
  WAIT VBL
GOTO Loop
```

Notice the numbers in `SIN`. A whole sine wave is 360 degrees, and the screen is 640 dots wide. `X * 0.5625` is exactly 360 when `X` is 640, so the wave matches up when the layer wraps around. The same goes for `X * 1.125`, which gives two whole waves.

## Making a layer transparent

`SCREEN ALPHA layer, value` makes a whole layer more or less transparent, from 0 (invisible) to 255 (fully visible). `SCREEN FADE layer, target, frames` fades the layer smoothly to the target value over a number of frames. The function `LAYERFADING(layer)` is 1 while the fade is going on:

```basic
INKG "Orange"
CIRCLEF 320, 240, 150, 150
SCREEN FADE 0, 0, 120
WHILE LAYERFADING(0) = 1
  WAIT VBL
WEND
SCREEN FADE 0, 255, 60
```

This fades the circle out over two seconds (120 frames) and back in over one.

`SCREEN OFF layer` hides a layer completely and `SCREEN ON layer` shows it again.

## Raster bars

On the Amiga people made colourful bands across the screen by changing the background colour for every line while the picture was being drawn, so-called **raster bars** or **copper bars**. In AMOS Like Basic you make them with `RASTER`:

```text
RASTER number, x, y, height, "colour1, colour2, ..."
```

The band gets a number (1 – 100) so that you can move it later. It starts at line `y`, is `height` dots high and blends smoothly between the colours you give. (`x` is not used yet but must be there.)

```basic
RASTER 1, 0, 0, 480, "#1D4AA0, #3F7FDC, #8CC6FF, #FFE6BF"
```

That gives a sky that fades from dark blue to light. Raster bars lie *behind* what is drawn in the layer, so you can draw mountains and clouds on top.

Raster bars belong to a layer: the one chosen with `SCREEN SELECT`, or the one after `#`. They fill the parts of their layer where nothing is drawn, so they also cover every layer below. A background therefore goes in the bottom layer, layer 0, and text and figures in the layers above:

```basic
RASTER #0, 1, 0, 0, 480, "Navy, Purple, Black"
SCREEN SELECT 1
INKG "Yellow"
CIRCLEF 320, 240, 100, 100
Loop:
  WAIT VBL
GOTO Loop
```

If a band ends up in the wrong layer, everything below it disappears behind the colours.

The bands can be moved with `RASTER MOVE number, y`. Here three bands bounce up and down:

```basic
RASTER 1, 0, 0, 40, "Red, Yellow, Red"
RASTER 2, 0, 0, 40, "Blue, Cyan, Blue"
RASTER 3, 0, 0, 40, "Green, Lime, Green"
V = 0
Loop:
  V = V + 3
  RASTER MOVE 1, 220 + SIN(V) * 200
  RASTER MOVE 2, 220 + SIN(V + 40) * 200
  RASTER MOVE 3, 220 + SIN(V + 80) * 200
  WAIT VBL
GOTO Loop
```

| Command | Does |
|---|---|
| `RASTER MOVE no, y` | moves the band |
| `RASTER WRAP no, 1` | the band continues at the top when it leaves the bottom (0 turns it off) |
| `RASTER DEL no` | removes the band |
| `RASTER CLEAR` | removes all bands in the layer |
| `RASTER GFXMODE layer, 1` | the bands colour what is *drawn* instead of the background |
| `RASTER COLOUR layer, "colour" [, tolerance]` | the bands only show where the layer has that colour (see below) |
| `RASTER SPACE layer, mode` | changes the whole layer's background: 0 = normal, 1 = space with stars, 2 = clouds |

A layer can have 24 colour transitions in total. A band with three colours uses two transitions. `RASTER INFO` prints how many are used.

### Rasters on one colour: RASTER COLOUR

On the Amiga and the Atari ST the screen had a palette of colours, and a favourite demo trick was to change one palette colour on every line. Everything drawn in that colour then got a rainbow, while the rest of the picture stayed the same. `RASTER COLOUR` does the same: the layer's raster bars only show where the layer has the colour you give.

```basic
SCREEN SELECT 1
INKG "Red"
BAR 100, 150, 540, 330
INKG "White"
BAR 200, 200, 440, 280
RASTER 1, 0, 0, 480, "Red, Orange, Yellow, Green, Blue, Violet"
RASTER COLOUR 1, "Red"
Loop:
  WAIT VBL
GOTO Loop
```

The red frame gets the rainbow, the white box in the middle stays white. It works on everything in the layer: drawings, images, sprites and text with `FONT PRINT`. The project `raster_bars` uses it on the red colour in the middle of a bitmap font.

Put the graphics that should get the rasters in a layer of their own, as above, since all the layer's bands follow the colour. A third value says how far from the colour a dot may be and still get the raster, in percent: `RASTER COLOUR 1, "Red", 25`. The default is 10, enough for the soft edges of scaled-up graphics. `RASTER GFXMODE 1, 0` turns it off again.

> **Note:** Give the colour exactly as it is in the picture. Some programs show the colours of an image slightly changed by the computer's colour settings; a value from the program that made the image, or `RASTER COLOUR` with a larger tolerance, is safer.

## CRT effect: SCREEN CRT

Games used to be played on TVs and monitors with picture tubes. They had visible lines, slightly blurry pixels and a curved glass front. `SCREEN CRT` makes the whole game window look like that:

```basic
SCREEN CRT 1
INKG "Orange"
CIRCLEF 320, 240, 150, 150
INKG "White"
TEXT 250, 230, "OLD TV"
Loop:
  WAIT VBL
GOTO Loop
```

`SCREEN CRT 0` turns the effect off. You can also give how strong the lines and how curved the screen should be, both from 0 to 100:

```basic nocheck
SCREEN CRT 1, 80, 20     ; strong lines, almost flat screen
```

Four more values give the whole look: the red, green and blue stripes of the tube (**mask**), how much darker the edges are (**vignette**), the **brightness** in percent and the **softness** of the pixels (0 sharp, 100 soft as on a TV). The standard look is:

```basic nocheck
SCREEN CRT 1, 55, 50, 50, 50, 122, 100
```

The easiest way to find a look is **View → CRT Settings...** in the editor. Move the sliders, or choose a ready-made look such as *Arcade monitor* or *Old TV*, and the game window changes while you watch. At the bottom is the same setting as a `SCREEN CRT` line; press **Copy** and paste it at the start of your program, so that the game always looks like that, also when it is exported.

The effect is drawn by the graphics card in the last step, when the picture is scaled up to the window, so it costs almost nothing. It looks best in a large window or in full screen. **F8** turns it on and off while the game runs, and in the editor it is also under **View → CRT Effect**. The text from `PRINT` gets the effect too, just like the graphics.

### A monitor round the picture: SCREEN FRAME

`SCREEN FRAME "file"` shows the game in a monitor: a picture of one, with a see-through glass in its middle. The game picture fills the glass, and the monitor is drawn on top of it. When the glass has another shape than the game (4:3 for 640 x 480), the plain parts of the monitor's case at the sides are drawn wider and its middle a little narrower, so that the glass fits the picture and the monitor still fills the window. The CRT effect only falls on the glass, as on a real monitor; the monitor itself stays sharp. The game still draws at its own resolution, so nothing in the program changes.

```basic nocheck
SCREEN FRAME "Graphics/Frames/C64Monitor.png"
SCREEN CRT 1
```

`SCREEN FRAME OFF` takes the monitor away. **View → CRT Settings...** has the same as *Monitor frame*, with the pictures in `Graphics/Frames` to choose from. The editor remembers what you choose there, the CRT settings too, until the next time you start it; a program that should always look the same has its own `SCREEN CRT` and `SCREEN FRAME` lines. Draw your own in a paint program: a monitor of the size of the window (640 x 480) with the glass made transparent; the glass is found by itself.

## Snow, rain and stars

`PARTICLE type, density` adds weather to the selected layer: type 1 is snow, 2 rain and 3 twinkling stars, and 0 turns it off. The density decides how big the particles and the gaps between them are:

```basic
RASTER 1, 0, 0, 480, "#0B1A4D, #2A3F8F"
PARTICLE 1, 20
Loop:
  WAIT VBL
GOTO Loop
```

The weather is drawn by the graphics card, so it costs nothing for your BASIC code.

> **Try it:** Make a sky with raster bars in layer 0, draw a crescent moon in layer 1 and put snow in layer 2.

> **Try it:** Build on the parallax example so that the layers scroll left or right depending on whether you hold the left or right arrow key. You read the keys with `KEYSTATE("Left")`, see the chapter on input.
