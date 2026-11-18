# A complete game: Forest Hop

Now you have all the pieces needed to make a real game. In this chapter we go through **Forest Hop**, a platform game that comes with AMOS Like Basic. Open `Projects/foresthop.amosproj` and run it while you read.

In Forest Hop you steer Pinecone through a forest. You collect gems, jump on beetles and must reach the flag at the end of the level. The game is about 550 lines long and uses nearly everything described in this part of the book.

## Planning the layers

The first decision is which layers the game should have. Forest Hop has six:

| Layer | Contents | Scrolls |
|---|---|---|
| 0 | sky (raster bars), sun and mountains | 1/10 of the level's speed |
| 1 | clouds | 1/6, and they drift by themselves |
| 2 | hills | 1/4 |
| 3 | forest | 1/2 |
| 4 | the level with tiles, Pinecone, enemies and gems | as fast as the camera |
| 5 | score and text | does not scroll |

The further away, the slower. That gives a feeling of depth.

## The background is drawn with code

None of the backgrounds is an image. They are drawn with `BAR` and `CIRCLEF` when the game starts. The mountains are the sum of a few sine waves, drawn as thin bars:

```basic nocheck
PROC DrawSky
  SCREEN SELECT 0
  RASTER 1,0,0,320,"#1d4aa0,#3f7fdc,#8cc6ff,#ffe6bf"
  RASTER 2,0,320,160,"#ffe6bf,#ffcf96"
  INKG "#8d93cf"
  FOR X=0 TO 638 STEP 2
    H=SIN(X*0.5625)*30+SIN(X*1.6875+40)*16+SIN(X*3.375)*6
    BAR X,INT(250-H),X+1,479
  NEXT X
END PROC
```

The numbers in `SIN` are chosen so that the waves fit exactly into 640 dots (`0.5625 × 640 = 360`). Then the right edge matches the left edge, and the layer can scroll forever without a seam.

Clouds and trees are drawn in the same way with `CIRCLEF`. A cloud or tree near the edge is drawn once more on the other side, so that it matches too:

```basic nocheck
IF CX<90 THEN Cloud CX+640,CY,CS
IF CX>550 THEN Cloud CX-640,CY,CS
```

## The level

The level is 120 cells wide and 15 high, and it is a map made for the map editor, `Graphics/ForestHop/level.amosmap`. `LoadLevel` loads it with `MAP LOAD`.

Gems, beetles, the start and the flag are not tiles but sprites. In the map they are **marker tiles**, tiles 12 to 15 in `tiles.png` with a dashed yellow frame. `LoadLevel` looks at every cell with `TILE(...)`, keeps the places of the gems and beetles in arrays, which then become sprites, and takes the markers off the map so that they are never drawn:

```basic nocheck
MAP LOAD "Graphics/ForestHop/level.amosmap"
FOR R=0 TO LH-1
  FOR C=0 TO LW-1
    TI=TILE(4,C*32,R*32)
    IF TI=GEMTILE THEN GX(NG)=C*32+6 : GY(NG)=R*32+6 : GA(NG)=1 : NG=NG+1
    IF TI=BUGTILE THEN EX(NE)=C*32 : EY(NE)=R*32 : ED(NE)=-1 : EA(NE)=1 : NE=NE+1
    IF TI=STARTTILE THEN STARTX=C*32 : STARTY=R*32
    IF TI=FLAGTILE THEN FLAGX=C*32 : FLAGY=R*32
    IF TI>=GEMTILE THEN TILE SET C,R,-1
  NEXT C
NEXT R
```

All sprites, Pinecone too, are in layer 4 together with the level, in the level's coordinates. When the layer scrolls they follow along by themselves.

## The main loop

The game has five modes: title screen, playing, dying, level complete and game over. The variable `MODE` keeps track of which, and the main loop calls the right procedure:

```basic nocheck
MainLoop:
  T=T+1
  IF MODE=0 THEN TitleFrame
  IF MODE=1 THEN PlayFrame
  IF MODE=2 THEN DeathFrame
  IF MODE=3 THEN WinFrame
  IF MODE=4 THEN GameOverFrame
  UpdateGems
  UpdateBugs
  UpdateSparkle
  UpdateCamera
  DrawText
  WAIT VBL
GOTO MainLoop
```

Notice how short and readable it is. All the detailed work happens in the procedures.

## Physics: speed, gravity and jumping

Pinecone has a position (`PX`, `PY`) and a speed (`VX`, `VY`). Every frame the speed is added to the position, and gravity increases the falling speed a little:

```basic nocheck
VY=VY+GRAV
IF VY>MAXFALL THEN VY=MAXFALL
PY=PY+VY
```

A jump is just setting a large upward speed: `VY=JUMPV`, where `JUMPV` is –13.5. Gravity then slows it down and pulls Pinecone back down, so that the jump becomes an arc. Letting go of the jump button early reduces the upward speed, which gives short jumps:

```basic nocheck
IF JK=1 AND JHELD=0 AND ONG=1 THEN VY=JUMPV : ONG=0 : PLAY JUMP
IF JK=0 AND VY<-5 THEN VY=-5
JHELD=JK
```

`JHELD` is the trick from the input chapter: the jump only happens when the button is *pressed*, not while it is held.

## Collisions with the level

The function `SolidAt` answers whether there is a solid tile at a point, with the help of `TILE()`:

```basic nocheck
FUNCTION SolidAt(X,Y)
  IF X<0 OR Y<0 THEN RETURN 0
  IF X>=WORLDW THEN RETURN 1
  ST=TILE(4,X,Y)
  IF ST>=0 AND ST<=6 THEN RETURN 1
  RETURN 0
END FUNCTION
```

After every move a few points around Pinecone are tested. If it is falling and the feet end up in a solid tile, it is moved up so that it stands right on top of it, and the speed is set to zero:

```basic nocheck
IF SolidAt(PX+8,PY+32)=1 OR SolidAt(PX+23,PY+32)=1 THEN PY=INT((PY+32)/32)*32-32 : VY=0 : ONG=1
```

`INT((PY+32)/32)*32` works out where the top edge of the tile is. The same idea is used to the sides (walls) and upwards (the ceiling and the bonus blocks).

## Enemies

The beetles walk back and forth and turn when they meet a wall or an edge:

```basic nocheck
EX(B)=EX(B)+ED(B)*1.2
FX=EX(B)+16+ED(B)*14
IF SolidAt(FX,EY(B)+16)=1 OR SolidAt(FX,EY(B)+40)=0 THEN ED(B)=-ED(B) : SPRITE ZOOM BUGSPR+B,0-ED(B)*100,100
```

`ED` is the direction, 1 or –1. The point `FX` is just in front of the beetle. If there is a wall there, or *no* ground under it, the beetle turns, and the sprite is mirrored with a negative zoom so that it faces the right way.

When Pinecone touches a beetle, the direction it is falling decides what happens. Falling from above squashes the beetle; otherwise Pinecone loses a life.

## The camera

The camera tries to keep Pinecone a bit to the left of the middle, but not outside the edges of the level. It follows smoothly by going only 18 % of the way to its target every frame, and then every layer scrolls at its own speed:

```basic nocheck
IF MODE=1 OR MODE=2 THEN CAMX=PX-280
IF CAMX<0 THEN CAMX=0
IF CAMX>WORLDW-640 THEN CAMX=WORLDW-640
SX=SX+(CAMX-SX)*0.18
SCROLL 0,SX/10,0
SCROLL 1,SX/6+T/4,0
SCROLL 2,SX/4,0
SCROLL 3,SX/2,0
SCROLL 4,SX,0
```

## Text and music

The score is redrawn every frame with a font of its own in layer 5, and the title gets a `WAVE` effect. The music is made in the tracker and lives in `Sound/Tracker`. The title screen plays the calm `forest_morning.amostrk`, and when the game starts `NewGame` switches to `forest_song.amostrk`. When Pinecone reaches the flag the fanfare `victory.amostrk` plays, and when the lives run out `game_over.amostrk` plays. The last two play once, with `TRACK LOOP 0`. Each song has a small procedure of its own, such as `TitleMusic` and `GameMusic`. Open the files with the **TRACKER** button to see how they are made. The same Forest Song written with `PLAY` commands is in `Projects/forestsong.amosproj`.

## The graphics

All the graphics in Forest Hop are made with code, in the Python script `Graphics/ForestHop/generate_assets.py`. It draws Pinecone, the beetle, the gems, the flag, all the tiles and the font pixel by pixel. The code of the game and the first version of the level are in `Graphics/ForestHop/build_game.py`, which writes the project file and the map.

> **Try it:** Make Pinecone faster or let it jump higher by changing `MAXV` and `JUMPV` at the start of the program.

> **Try it:** Add an extra life as a reward for every thousand points.

> **Try it:** Make a level of your own! Open Forest Hop, press **MAP**, and choose first the tileset and then `level.amosmap` under **From the program**. Draw, put out gems and beetles with the marker tiles, and press **Save**. Remember that Pinecone cannot jump higher than about four cells or further than six.
