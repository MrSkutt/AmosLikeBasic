# The building blocks of the language

This part is for looking things up. The chapters list everything AMOS Like Basic can do, with exact syntax and short examples. They do not explain the basics; that is what parts 1 and 2 are for.

In the syntax:

- Words in **CAPITALS** are written exactly as they are.
- Words in *small letters*, such as `x`, `layer` or `file`, are replaced by a value: a number, a text, a variable or a whole expression.
- Anything in square brackets `[ ]` may be left out.

## Lines and commands

A program is a list of commands that run from top to bottom. Several commands can be on the same line with colons in between:

```basic
X = 5 : Y = 10 : PRINT X + Y
```

Upper and lower case make no difference in commands and variable names, so `print`, `Print` and `PRINT` are the same.

## Comments

| Written as | Meaning |
|---|---|
| `; text` | the rest of the line is a comment |
| `REM text` | the whole line is a comment |

## Labels

A **label** is a name followed by a colon, alone on a line. `GOTO`, `GOSUB`, `RESTORE` and `ON ERROR GOTO` jump to labels.

```basic nocheck
Start:
  PRINT "HELLO"
GOTO Start
```

## Variables

| Kind | Name | Example |
|---|---|---|
| number | letters and digits, starts with a letter | `X`, `SCORE`, `LIFE2` |
| text | as above, ends in `$` | `NAME$`, `ROW$` |
| whole number | as above, ends in `%` | `COUNT%` |
| constant | created with `CONST`, cannot change | `CONST SPEED = 4` |
| array | created with `DIM`; the number is the highest index | `DIM A(10)` (`A(0)` – `A(10)`), `DIM N$(5)` |

Variables do not need to be created in advance. A number variable that has not been given a value is `0`, and a text variable is empty. Numbers are stored with decimals, so `7 / 2` is `3.5`. A variable whose name ends in `%` only keeps whole numbers: `A% = 7 / 2` gives `3`. Upper and lower case make no difference: `score` and `SCORE` are the same variable.

## Values

| Kind | Written as | Example |
|---|---|---|
| whole number | digits | `42`, `-7` |
| decimal number | a dot as the decimal point | `3.14`, `0.5` |
| hexadecimal | `$` or `&H` in front | `$FF`, `&H10` (255 and 16) |
| binary | `%` in front | `%1010` (10) |
| text | in quotation marks | `"HELLO"` |
| array with starting values | in square brackets | `[1, 2, 3]` |

## Operators

From highest to lowest priority. Whatever is higher up is worked out first, and brackets always come first.

| Operator | Means | Example | Gives |
|---|---|---|---|
| `( )` | brackets | `(2 + 3) * 4` | `20` |
| `^` | to the power of | `2 ^ 10` | `1024` |
| `-` (in front) | minus | `-X` | |
| `*` `/` `MOD` `DIV` `\` | times, divided by, remainder, whole-number division (`\` is the same as `DIV`) | `17 MOD 5` | `2` |
| `+` `-` `&` | plus, minus, join text | `"A" & 1` | `A1` |
| `=` `<>` `<` `>` `<=` `>=` | comparison | `10 > 3` | `1` |
| `NOT` | not | `NOT 5 = 5` | `0` |
| `AND` | and | `1 > 0 AND 2 > 0` | `1` |
| `OR` `XOR` | or; either but not both | `1 < 0 OR 2 < 0` | `0` |

Operators on the same level are worked out from left to right, except `^`, which is worked out from the right: `2 ^ 3 ^ 2` is `2 ^ 9`. A minus in front is worked out after `^`, so `-2 ^ 2` is `-4`. Since `&` and `+` are on the same level, `"A" & 2 + 3` becomes the text `A23`; write `"A" & (2 + 3)` to get `A5`.

`+` also joins two texts, but `&` works between text and numbers as well and is therefore safer.

A comparison gives `1` if it is true and `0` if it is false, so it can be used in expressions and kept in variables: `DONE = X > 100`. `AND`, `OR`, `XOR` and `NOT` also give `1` or `0`, and count everything except `0` as true. Texts are compared character by character, so `"abc" < "abd"` is `1`.

## Colours

Every command that takes a colour (`INK`, `INKG`, `PAPER`, `PAPERG` and the colours in `RASTER`) understands the same ways of writing it:

| Written as | Example |
|---|---|
| number 0 – 15 in the palette | `INKG 7` |
| colour name | `INKG "Orange"` |
| hexadecimal | `INKG "#FF8800"` |
| red, green, blue 0 – 255 | `INKG "255,136,0"` |

The palette and the colour names are in the chapter *Tables*.

## Layers with #

Drawing commands draw in the layer chosen with `SCREEN SELECT`. Many of them can instead get a layer number with `#` first, which applies to that command only:

```basic nocheck
BAR #2, 0, 0, 100, 100
TEXT #3, 10, 10, "SCORE"
```

This works for `PLOT`, `LINE`, `BOX`, `BAR`, `CIRCLE`, `CIRCLEF`, `ELLIPSE`, `FILL`, `TEXT`, `PRINTG`, `LOCATEG`, `CLSG`, `LOAD`, `SPRITE`, `RASTER`, `FONT` and `TILE DRAW`.

## File names

File names are written in quotation marks. A relative path such as `"Graphics/Sprites/hero.png"` is looked for in the project folder, and in the app's own folder once the game is exported. Always use forward slashes `/`, even on Windows.
