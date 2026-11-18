# Sprites

A **sprite** is a figure that can move freely over the screen without you having to clear and redraw the background. The player, the enemies, the shots and the coins in a game are nearly always sprites. They are drawn by the graphics card, so you can have hundreds at once.

Every sprite has a number that you choose yourself. The number also decides the order: sprites with higher numbers are drawn on top of those with lower ones.

## Loading and showing a sprite

```basic
SPRITE LOAD 1, "Graphics/ForestHop/gem.png"
SPRITE POS 1, 300, 200
SPRITE ON 1
```

`SPRITE LOAD` loads an image into sprite 1, `SPRITE POS` sets the position (the sprite's top left corner) and `SPRITE ON` makes it visible. `SPRITE OFF` hides it again.

> **Tip:** Black pixels (`0,0,0`) in the image become transparent, just like transparent pixels in PNG files.

## Moving a sprite

Just like the ball in the graphics chapter, you change the position a little every frame:

```basic
SPRITE LOAD 1, "Graphics/ForestHop/beetle.png", 32, 32, 3
SPRITE ON 1
X = 0
Loop:
  X = X + 2
  IF X > 640 THEN X = -32
  SPRITE POS 1, X, 300
  WAIT VBL
GOTO Loop
```

`SPRITE MOVE no, dx, dy` moves a sprite some distance from where it is.

## Animation: sprite sheets

An animated figure is made of several pictures in a row, often placed next to each other in a single file, a so-called **sprite sheet**. Give the width, height and number of pictures when you load it:

```basic nocheck
SPRITE LOAD 1, "Graphics/ForestHop/pinecone.png", 32, 32, 6
```

`pinecone.png` holds six pictures of 32 × 32 dots: standing, three walking pictures, jumping and hurt. With `SPRITE FRAME no, picture` you choose which one is shown (the pictures count from 0):

```basic
SPRITE LOAD 1, "Graphics/ForestHop/pinecone.png", 32, 32, 6
SPRITE POS 1, 300, 220
SPRITE ZOOM 1, 300
SPRITE ON 1
T = 0
Loop:
  T = T + 1
  SPRITE FRAME 1, 1 + INT(T / 8) MOD 3
  WAIT VBL
GOTO Loop
```

`INT(T / 8) MOD 3` counts 0, 1, 2, 0, 1, 2 ... and changes picture every eighth frame. The `1 +` makes it the walking pictures 1, 2 and 3.

> **Tip:** If the file name contains the size, like `Anim_Beast_W32_H56_B11.png` (width 32, height 56, 11 pictures), `SPRITE LOAD 1, "Anim_Beast_W32_H56_B11.png"` is enough.

### ANIM: animation that runs itself

With `ANIM` you describe an animation once, and then it runs by itself at every `WAIT VBL`. The sequence is written as `(picture, time)` where the time is a number of frames:

```basic
SPRITE LOAD 1, "Graphics/ForestHop/pinecone.png", 32, 32, 6
SPRITE POS 1, 300, 220
SPRITE ZOOM 1, 300
SPRITE ON 1
ANIM DEF 1, "idle", (0,30), LOOP
ANIM DEF 1, "walk", (1,8)(2,8)(3,8)(2,8), LOOP
ANIM DEF 1, "jump", (4,20), ONCE, "idle"
ANIM SET 1, "walk"
T = 0
Loop:
  T = T + 1
  IF T = 180 THEN ANIM SET 1, "jump"
  WAIT VBL
GOTO Loop
```

`LOOP` means the animation starts again, `ONCE` that it runs once. After `ONCE` you can give the animation that takes over when it has finished, here `"idle"`. `ANIM STOP no` stops the animation.

## The sprite editor

The **SPRITES** button opens the sprite editor, where you draw figures pixel by pixel.

**Pictures in the program** lists every `SPRITE LOAD`, `IMAGE LOAD` and `TILE LOAD` in your program. Choose one and it opens with the frame size the program uses, ready to change. **Save** writes it back to the same file, in the same layout, so the program finds the frames where it expects them.

**Load Spritesheet...** opens any picture. The editor shows it with a grid of frames on top and plays the frames as an animation. It guesses the frame size from the gaps between the figures; change the numbers until the grid and the animation look right.

The tools are on the left:

| Tool | Key | What it does |
|---|---|---|
| ✎ Pencil | P | draws dot by dot |
| ╱ Line | L | a straight line from where you press to where you let go |
| ▭ Rectangle | R | a box; with **Fill** ticked, a filled one |
| ◯ Ellipse | E | a circle or an ellipse; also with **Fill** |
| ▧ Fill | F | fills an area of one colour |
| ⌖ Picker | I | takes a colour from the picture; a right-click does that with any tool |

**In this sprite** shows the colours the sprite uses most, so you can pick up the exact colours again. **Palette** has the C64's 16 colours and more, and **T** is transparent. **Mirror draw** draws everything mirrored left-right too, which makes faces and ships symmetric. The buttons above the picture flip the frame, move it one pixel (it wraps round) and undo with **Ctrl+Z** (or **Cmd+Z** on a Mac). **Copy Frame** makes a copy of the frame to change into the next step of an animation. `,` and `.` step between the frames.

## Size, rotation and mirroring

| Command | Does |
|---|---|
| `SPRITE ZOOM no, percent` | changes the size, 100 = normal, 200 = twice as big |
| `SPRITE ZOOM no, x, y` | different in width and height |
| `SPRITE ROTATE no, degrees` | turns the sprite around its centre |
| `SPRITE ALPHA no, value` | transparency, 0 – 255 |
| `SPRITE FADE no, target, frames` | fades smoothly to the target value |

A negative zoom mirrors the sprite. That is how you make a figure that can walk both ways with the same pictures:

```basic
SPRITE LOAD 1, "Graphics/ForestHop/pinecone.png", 32, 32, 6
SPRITE ZOOM 1, 200
SPRITE ON 1
X = 100 : DX = 3
Loop:
  X = X + DX
  IF X > 560 THEN DX = -3 : SPRITE ZOOM 1, -200, 200
  IF X < 80 THEN DX = 3 : SPRITE ZOOM 1, 200, 200
  SPRITE POS 1, X, 300
  WAIT VBL
GOTO Loop
```

## Image banks

If many sprites use the same pictures, for example twenty coins, it is wasteful to load the file twenty times. Instead load it once into an **image bank** with `IMAGE LOAD`, and let the sprites take pictures from there with `SPRITE IMAGE no, bank, picture`:

```basic
IMAGE LOAD 1, "Graphics/ForestHop/gem.png", 20, 20, 4
FOR I = 0 TO 9
  SPRITE IMAGE 10 + I, 1, 0
  SPRITE POS 10 + I, 60 + I * 55, 240
  SPRITE ON 10 + I
NEXT I
T = 0
Loop:
  T = T + 1
  IF T MOD 6 = 0 THEN
    FOR I = 0 TO 9
      SPRITE IMAGE 10 + I, 1, (T / 6) MOD 4
    NEXT I
  ENDIF
  WAIT VBL
GOTO Loop
```

Ten gems spinning in step. A sprite that uses an image bank changes picture with `SPRITE IMAGE`, not with `SPRITE FRAME`.

## Sprites in layers

A sprite is drawn in layer 0 unless you say otherwise. `SPRITE LAYER no, layer` moves it to another layer. The sprite's position then counts in that layer's coordinates, so if the layer scrolls, the sprite follows. In a platform game you therefore put enemies and coins in the same layer as the level: they stay in their places on the level while it scrolls past.

## Collisions

To find out whether the player touches an enemy or a coin there are collision functions. They give 1 if the sprites overlap and 0 otherwise:

| Function | Tests |
|---|---|
| `HITBOX(a, b)` | whether the sprites' rectangles overlap (fastest) |
| `HITCIRCLE(a, b)` | whether two circles overlap (good for round things) |
| `HIT(a, b)` | whether any visible pixels overlap (most exact) |

```basic
SPRITE LOAD 1, "Graphics/ForestHop/pinecone.png", 32, 32, 6
SPRITE LOAD 2, "Graphics/ForestHop/gem.png", 20, 20, 4
SPRITE POS 2, 400, 300
SPRITE ON 1 : SPRITE ON 2
X = 100
Loop:
  X = X + 2
  SPRITE POS 1, X, 296
  IF HITBOX(1, 2) = 1 THEN SPRITE OFF 2 : PLAY COIN
  WAIT VBL
GOTO Loop
```

### Groups

With many enemies it is convenient to gather them in a **group**. `HITGROUP(no, "group")` gives the number of the first sprite in the group that `no` collides with, or 0:

```basic nocheck
FOR I = 10 TO 19
  SPRITE GROUP I, "enemies"
NEXT I
TOUCHED = HITBOXGROUP(1, "enemies")
IF TOUCHED > 0 THEN SPRITE OFF TOUCHED
```

`HITBOXGROUP` and `HITCIRCLEGROUP` work the same way with the rectangle and circle tests. `SPRITE UNGROUP no, "group"` takes a sprite out of a group.

## Effects

Sprites can get effects that are worked out by the graphics card:

| Command | Effect |
|---|---|
| `SPRITE EFFECT ADD no, BLUR, radius` | makes the sprite blurry |
| `SPRITE EFFECT ADD no, WAVE, ampX, freqX, ampY, freqY` | makes the sprite ripple |
| `SPRITE EFFECT ADD no, COLOR, brightness, contrast, saturation` | changes the colours (1 = unchanged) |
| `SPRITE EFFECT CLEAR no` | removes all effects |

```basic
SPRITE LOAD 1, "Graphics/ForestHop/beetle.png", 32, 32, 3
SPRITE POS 1, 300, 220
SPRITE ZOOM 1, 400
SPRITE ON 1
SPRITE EFFECT ADD 1, WAVE, 6, 0.3, 0, 0
Loop:
  WAIT VBL
GOTO Loop
```

The wave moves by itself. With `SPRITE EFFECT UPDATE no, index, parameter, value` you change an effect while the program runs, for example `SPRITE EFFECT UPDATE 1, 0, AMPLITUDEX, 10`. The effects are numbered from 0 in the order they were added.

> **Try it:** Make a sprite that you steer with the arrow keys (see the chapter on input) and that mirrors itself in the direction it walks.

> **Try it:** Scatter ten gems at random places and let the player pick them up. Count how many are left.
