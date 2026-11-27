# Tiles and levels

A level in a platform game is often thousands of dots wide. Drawing it as a single image would be impractical. Instead it is built from **tiles**: small square pieces, such as grass, stone and crates, laid out in a grid. The grid is called a **map**.

A tileset is an image with all the pieces next to each other, just like a font. The pieces are numbered from 0, row by row from the left.

## Building a level

```basic
SCREEN SELECT 1
TILE MAP 40, 15
TILE LOAD "Graphics/ForestHop/tiles.png", 32, 32
FOR X = 0 TO 39
  TILE SET X, 13, 0
  TILE SET X, 14, 1
NEXT X
TILE SET 10, 9, 3
TILE SET 11, 9, 2
TILE SET 12, 9, 3
TILE DRAW 0, 0
```

| Command | Does |
|---|---|
| `TILE MAP width, height` | makes an empty map, measured in cells, in the selected layer |
| `TILE LOAD "file", width, height` | loads the tileset with the cell size in dots |
| `TILE SET x, y, number` | puts tile `number` in cell x, y (–1 = empty) |
| `TILE DRAW x, y` | draws the whole map into the layer |

`TILE MAP` makes the layer as big as the whole map, here 40 × 32 = 1280 dots wide. That is wider than the screen, so you scroll the layer to see the rest:

```basic nocheck
SCROLL 1, CAMERA_X, 0
```

> **Note:** Use tiles of 32 × 32 dots. The `TILE()` function below always assumes that size.

`TILE DRAW` only draws the cells that have a tile and does not paint over empty cells. If you change one tile for another, as when a bonus block becomes empty in Forest Hop, set the new one and draw again: `TILE SET X, Y, 4` and then `TILE DRAW 0, 0`.

## What is at a place?

`TILE(layer, x, y)` gives the number of the tile at the point x, y in the level, or –1 if the cell is empty. That is the basis of collisions in a platform game: if there is stone under the feet, the figure should not fall.

```basic nocheck
IF TILE(1, PLAYER_X + 16, PLAYER_Y + 32) >= 0 THEN ON_GROUND = 1
```

(The first value, the layer, is not used yet but must be there.)

## The map editor

The **MAP** button in the editor opens the map editor, where you draw the level with the mouse instead of with `TILE SET`.

**From the program** lists the `TILE LOAD` and `MAP LOAD` lines in your program: choose the tileset and then the map, and you edit the level just as the game shows it. **Save** writes the map back to its file. **Load Tileset...** opens another picture and shows the tile grid on it, so you can check the tile size.

Choose a tile in the palette and draw with it. Besides the pencil there are **line**, **rectangle** (filled or not) and **fill**, and the **picker** takes a tile from the map. The right button erases, and **Ctrl+Z** undoes. The level is then loaded in your program with `MAP LOAD`:

```basic nocheck
SCREEN SELECT 1
TILE LOAD "Graphics/Tiles/tileset.png", 32, 32
MAP LOAD "Graphics/test.amosmap"
TILE DRAW 0, 0
```

## Figures on the map

A level has more than walls and floors: coins, enemies, the start and the goal. They are sprites, but it is handy to put them out in the map editor too. Draw a few **marker tiles** in your tileset, for example a coin and an enemy with a frame round them, and place them on the map like any tile. `MAP OBJECTS` then finds them:

```basic nocheck
MAP LOAD "Graphics/level.amosmap"
MAP OBJECTS 12, CX(), CY(), COINS     ; tile 12 marks a coin
FOR I = 0 TO COINS - 1
  SPRITE IMAGE 10 + I, 1, 0
  SPRITE POS 10 + I, CX(I), CY(I)
  SPRITE ON 10 + I
NEXT I
TILE DRAW 0, 0
```

The places are in dots, so they go straight into `SPRITE POS`. The marker tiles are taken off the map, so call `MAP OBJECTS` before `TILE DRAW`. Forest Hop does this with tiles 12 to 15 for gems, beetles, the start and the flag.

In the map editor you can see the figures themselves instead of the marker tiles: choose the marker tile in the palette and pick its picture under **Show this tile as a figure**. The list holds the pictures your program loads with `SPRITE LOAD` and `IMAGE LOAD`, with their frame size. The map then shows the first frame of the picture on every such cell, at its own size, and the palette shows it too. The choice is saved in the map file; the game is not changed by it, since `MAP LOAD` only reads the tiles. Open Forest Hop's map to see it: the gems, beetles, Pinecone and the flag stand where the game puts them.

## Levels in DATA

Another way is to draw the level with letters in `DATA` lines, right in the program. Each letter stands for a tile:

```basic
SCREEN SELECT 1
TILE MAP 20, 5
TILE LOAD "Graphics/ForestHop/tiles.png", 32, 32
CHARS$ = "GDBQ"
RESTORE Level
FOR Y = 0 TO 4
  READ ROW$
  FOR X = 0 TO 19
    TILE SET X, Y, INSTR(CHARS$, MID$(ROW$, X + 1, 1)) - 1
  NEXT X
NEXT Y
TILE DRAW 0, 0
END

Level:
DATA "...................."
DATA "......QBQ..........."
DATA "...................."
DATA "GGGGGGGGGG...GGGGGGG"
DATA "DDDDDDDDDD...DDDDDDD"
```

`INSTR` gives the position of the character in `CHARS$`, and `- 1` turns it into a tile number: `G` becomes 0 (grass), `D` 1 (dirt), `B` 2 (stone), `Q` 3 (bonus block), and `.`, which is not there, becomes –1, so empty. That makes it easy to see and change the level right in the code.

> **Try it:** Add a row of `B` as a ceiling at the top and a pit in the middle of the level.
