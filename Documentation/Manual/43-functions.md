# Functions

A function works out a value. The arguments are in brackets after the name, and the result is used in an expression: `X = ABS(DX)`, `PRINT LEN(A$)`. Functions whose names end in `$` give text; the others give numbers.

Angles are given in **degrees**, not radians.

## Numbers

| Function | Gives | Example | Result |
|---|---|---|---|
| `ABS(x)` | x without a minus sign | `ABS(-7)` | `7` |
| `ATN(x)` | the angle whose tangent is x, in degrees | `ATN(1)` | `45` |
| `COS(a)` | the cosine of the angle a | `COS(60)` | `0.5` |
| `DEC(x)` | x minus 1 | `DEC(5)` | `4` |
| `EXP(x)` | e to the power of x | `EXP(0)` | `1` |
| `INC(x)` | x plus 1 | `INC(5)` | `6` |
| `INT(x)` | x rounded down to a whole number | `INT(-2.5)` | `-3` |
| `LOG(x)` | the natural logarithm | `LOG(1)` | `0` |
| `LOG10(x)` | the base-10 logarithm | `LOG10(1000)` | `3` |
| `LOG2(x)` | the base-2 logarithm | `LOG2(8)` | `3` |
| `MAX(a, b)` | the larger of two numbers | `MAX(3, 8)` | `8` |
| `MIN(a, b)` | the smaller of two numbers | `MIN(3, 8)` | `3` |
| `RND(x)` | a random number from 0 up to (but not including) x, with decimals | `INT(RND(6)) + 1` | 1 – 6 |
| `SGN(x)` | –1, 0 or 1 depending on the sign | `SGN(-3)` | `-1` |
| `SIN(a)` | the sine of the angle a | `SIN(90)` | `1` |
| `SQR(x)` | the square root | `SQR(16)` | `4` |
| `TAN(a)` | the tangent of the angle a | `TAN(45)` | `1` |

`INC` and `DEC` as functions do not change the variable; the commands `INC` and `DEC` do.

## Text

| Function | Gives | Example | Result |
|---|---|---|---|
| `ASC(s$)` | the character code of the first character | `ASC("A")` | `65` |
| `CHR$(code)` | the character with that code | `CHR$(65)` | `A` |
| `FIND(s$, find$ [, start])` | where find$ first appears, counted from **0**; –1 if it is not there | `FIND("PINECONE", "E")` | `3` |
| `FINDLAST(s$, find$)` | where find$ last appears, from 0; –1 if it is not there | `FINDLAST("PINECONE", "E")` | `7` |
| `HEX(x)` | the number in hexadecimal | `HEX(255)` | `FF` |
| `INSTR(s$, find$ [, start])` | where find$ first appears, counted from **1**; 0 if it is not there | `INSTR("PINECONE", "E")` | `4` |
| `LEFT$(s$, n)` | the first n characters | `LEFT$("PINECONE", 4)` | `PINE` |
| `LEN(s$)` | the number of characters | `LEN("PINECONE")` | `8` |
| `LOWER$(s$)` | small letters | `LOWER$("HELLO")` | `hello` |
| `MID$(s$, start [, n])` | n characters from position start (from 1), or the rest | `MID$("PINECONE", 2, 3)` | `INE` |
| `REPEAT$(s$, n)` | the text n times | `REPEAT$("=", 5)` | `=====` |
| `REPLACE$(s$, old$, new$)` | replaces every old$ with new$ | `REPLACE$("cat", "c", "h")` | `hat` |
| `RIGHT$(s$, n)` | the last n characters | `RIGHT$("PINECONE", 4)` | `CONE` |
| `STR$(x)` | the number as text | `STR$(42)` | `42` |
| `TRIM$(s$)` | without spaces at the start and end | `TRIM$("  hello  ")` | `hello` |
| `UPPER$(s$)` | capital letters | `UPPER$("hello")` | `HELLO` |
| `VAL(s$)` | the text as a number, 0 if it is not a number | `VAL("3.5")` | `3.5` |
| `WORD$(s$, n)` | word number n (from 1), separated by spaces | `WORD$("one two three", 2)` | `two` |

## Arrays

| Function | Gives |
|---|---|
| `DIM(name)` | the number of places in the array |
| `JOIN$(name, separator$)` | all places joined into one text |
| `SPLIT$(s$, separator$)` | a text array with the parts; an empty separator splits into single characters |

```basic
A$ = SPLIT$("a,b,c", ",")
PRINT DIM(A$)
PRINT JOIN$(A$, "-")
```

## Keyboard and gamepad

| Function | Gives |
|---|---|
| `INKEY$` | the name of a key that is down, or empty text |
| `KEYSTATE("key")` | 1 if the key is held down, otherwise 0 |
| `JOYSTATE(pad, "button")` | 1 if the button is held down; the pads count from 1 |
| `JOYAXIS(pad, "axis")` | the position of the stick, –100 to 100 (the triggers 0 to 100) |

The names of keys, buttons and axes are in the chapter *Tables*.

## Sprites

The collisions give 1 or 0. The group versions give the number of the first sprite in the group that is hit, or 0.

| Function | Gives |
|---|---|
| `HIT(a, b)` | whether visible pixels of sprites a and b overlap |
| `HITBOX(a, b)` | whether the sprites' rectangles overlap |
| `HITCIRCLE(a, b)` | whether the sprites' circles overlap |
| `HITGROUP(no, "group")` | pixel test against a group |
| `HITBOXGROUP(no, "group")` | rectangle test against a group |
| `HITCIRCLEGROUP(no, "group")` | circle test against a group |
| `FADING(no)` | 1 while a `SPRITE FADE` is going on |
| `LAYERFADING(layer)` | 1 while a `SCREEN FADE` is going on |
| `SPRITE#X(no)`, `SPRITE#Y(no)` | the sprite's position |
| `SPRITE#WIDTH(no)`, `SPRITE#HEIGHT(no)` | the sprite's size in dots, without zoom |
| `SPRITE#FRAME(no)` | the picture shown, from 0 |
| `SPRITE#FRAMECOUNT(no)` | the number of pictures |
| `SPRITE#ZOOMX(no)`, `SPRITE#ZOOMY(no)` | the zoom, where 1 is normal size |
| `SPRITE#ANGLE(no)` | the rotation in degrees |
| `SPRITE#VISIBLE(no)` | 1 if the sprite is on |

## Levels

| Function | Gives |
|---|---|
| `TILE(layer, x, y)` | the tile number at the point x, y on the map, –1 if the cell is empty or outside the map |

`x` and `y` are dots, not cells: the point is divided by 32 to find the cell. The layer is given but not used, since there is only one map.

## Sound

| Function | Gives |
|---|---|
| `PLAYING(channel)` | 1 if the synth channel is playing (channel 1 – 8) |
| `ANYPLAYING()` | 1 if any channel is playing |
| `TRACKPLAYING()` | 1 while a tracker song is playing |
| `TRACKPOS()` | the place in the tracker song's order list, from 0 |
| `TRACKROW()` | the row in the tracker song's pattern, from 0 |

## Files

| Function | Gives |
|---|---|
| `EOF(channel)` | 1 when the whole file has been read, or if the channel is not open |

## Time

| Function | Gives |
|---|---|
| `MILLIS()` | milliseconds since the program started |
| `MICROS()` | microseconds since the program started |
| `TICKS` | the same as `MILLIS()` |

## Variables set by the system

| Variable | Holds |
|---|---|
| `ERR$` | the error message after an error, when `ON ERROR GOTO` is on |
| `TIMER` | the clock in milliseconds, after the command `TIMER` |
| `TIME_name` | the time in milliseconds after `PROFILE END "name"` |
