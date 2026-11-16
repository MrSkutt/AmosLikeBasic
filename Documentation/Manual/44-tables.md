# Tables

## Keys

The names are used in `KEYSTATE` and given by `INKEY$`. Upper and lower case make no difference.

| Key | Name |
|---|---|
| arrow keys | `Left`, `Right`, `Up`, `Down` |
| space bar | `Space` |
| Enter | `Return` |
| Escape | `Escape` |
| Tab, Backspace | `Tab`, `Back` |
| letters | `A` to `Z` |
| digits above the letters | `D0` to `D9` |
| digits on the number pad | `NumPad0` to `NumPad9` |
| shift, ctrl, alt | `LeftShift`, `RightShift`, `LeftCtrl`, `RightCtrl`, `LeftAlt`, `RightAlt` |
| function keys | `F1` to `F11` |

`F8` and `F12` are used by the game window: `F8` turns the CRT effect on and off and `F12` toggles full screen.

## Gamepad

| Button | Name in `JOYSTATE` |
|---|---|
| A, B, X, Y | `A`, `B`, `X`, `Y` |
| shoulder buttons | `LB`, `RB` |
| start and select | `Start`, `Select` |
| d-pad | `Up`, `Down`, `Left`, `Right` |

| Axis | Name in `JOYAXIS` | Values |
|---|---|---|
| left stick | `LeftX`, `LeftY` | –100 to 100 |
| right stick | `RightX`, `RightY` | –100 to 100 |
| triggers | `LT`, `RT` | 0 to 100 |

## The palette

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

The colour names are the web colour names, for example `Black`, `White`, `Red`, `Green`, `Lime`, `Blue`, `Yellow`, `Cyan`, `Magenta`, `Orange`, `Pink`, `Purple`, `Brown`, `Gray`, `Silver`, `Gold`, `Navy`, `Teal`, `SkyBlue`, `ForestGreen`, `SaddleBrown` and `DarkRed`.

## Fonts for FONT SET

These fonts come with AMOS Like Basic, so they look the same on every computer. Any font installed on the computer works too, by its name.

| Name in `FONT SET` | Font | Made by, licence |
|---|---|---|
| `"C64 Pro Mono"` | the Commodore 64 font, every character equally wide | Style (style64.org); free software only |
| `"C64 Pro"` | the Commodore 64 font, proportional | Style (style64.org); free software only |
| `"Topaz"` | the Amiga font from Workbench 2 and 3 (A1200) | dMG of Trueschool and Divine Stylers; GPL with font exception |
| `"Topaz A500"` | the Amiga font from Workbench 1 (A500) | the same |
| `"TopazPlus"` | a sharper version of the A500 font | the same |

The full licences are in `fonts/LICENSES.md`. The C64 fonts may only be part of software that is given away for free; see the chapter on exporting if you want to sell a game.

```basic
FONT SET 16, 16, "Topaz"
INKG "White"
PRINTG "READY."
```

## Notes in PLAY MUSIC

| Written as | Means |
|---|---|
| `C D E F G A B` | note names |
| `C#`, `Db` | sharp or flat |
| `C5` | octave; `A4` is 440 Hz |
| `C5:8` | length: 1, 2, 4, 8, 16 or 32 (whole note to thirty-second note) |
| `C5:4.` | dotted: half as long again |
| `E` | no octave: the same octave as the previous note |
| `R:4` | rest |
| `E5` | no length: the same length as the previous note |
| `{CUT 800}` | an effect that changes the sound right there; see the chapter on sound |
| `{CUT 800; ARP 0 4 7}` | several effects at the same place |

Drum sounds on a channel with `PLAY WAVE channel, "Drums"`:

| Name | Sound |
|---|---|
| `BD` | bass drum |
| `SD` | snare |
| `HH` | hi-hat |
| `OH` | open hi-hat |
| `CY` | cymbal |
| `TM` | tom |

## Keys in the editor

| Key | Does |
|---|---|
| `F1` | shows or hides the variable panel |
| `F8` | CRT effect on or off |
| `F12` | the game window in full screen or a window |
| `PgUp`, `PgDown` | scrolls a page in the editor |

## Keys in the tracker

| Key | Does |
|---|---|
| `Z S X D C V G B H N J M` | notes, one octave |
| `Q 2 W 3 E R 5 T 6 Y 7 U I` | notes, the octave above |
| `1` | let go of the note (`===`) |
| `0`–`9`, `A`–`F` | hex digits in the instrument and effect columns |
| `Delete`, `Backspace` | clears whatever the cursor is on, or the selection |
| `Space` | edit mode on or off; stops the song if it is playing |
| `F1`, `F2` | octave down or up |
| `F5` | play the song from the place selected in the order list |
| `F6`, `F7` | play the pattern from the start or from the cursor |
| `Esc` | stop |
| `Tab`, `Shift+Tab` | next or previous channel |
| `PgUp`, `PgDown`, `Home`, `End` | 16 rows up or down, the first or last row |
| `Shift` + arrows | select |
| `Cmd+C`, `Cmd+X`, `Cmd+V` | copy, cut, paste |
| `Cmd+A` | select the whole pattern |
| `Cmd+↑`, `Cmd+↓` | move the notes a semitone (with `Shift` an octave) |
| `Cmd+I`, `Cmd+Backspace` | insert or remove a row (with `Shift` in all channels); `Insert` works too |
| `Cmd+Z`, `Cmd+Shift+Z` | undo, redo |
| `Cmd+S` | save |

On Windows `Cmd` is `Ctrl` instead.

## Common pitfalls

**`&` and `+` are equally strong.** `"Total: " & 2 + 3` gives `Total: 23`. Add brackets: `"Total: " & (2 + 3)`.

**Arrays start at 0.** `DIM A(10)` gives `A(0)` to `A(9)`. `A(10)` gives an error.

**FOR runs at least once.** `FOR K=0 TO N-1` runs one lap even when `N` is 0. Check first: `IF N>0 THEN` before the loop.

**DATA needs a label.** `DATA` lines must be under a label, and `RESTORE label` must run before the first `READ`.

**Black is transparent.** Pixels with the colour `0,0,0` do not show, neither when you draw nor in images. Use `"#101010"` for black.

**Do not forget WAIT VBL.** A game loop without `WAIT VBL` never shows anything, since the picture is only shown at `WAIT VBL`.

**LOCATE and LOCATEG have different orders.** `LOCATE row, column` counts from 1, `LOCATEG column, row` from 0.

**Procedures are called without brackets.** `SayHello "Pinecone", 3`, not `SayHello("Pinecone", 3)`. Your own functions, on the other hand, are called with brackets.

**Functions cannot draw.** In a `FUNCTION` only assignment, `IF`, `FOR`, `WHILE`, `INC`, `DEC` and `RETURN` work. Use a `PROC` for everything else.

**The C64 font has no Å, Ä or Ö.** With `FONT SET 16,16,"C64 PRO MONO"` they become dashes, and so do other letters with accents. Choose words without them, or another font.

**Font text stays.** Text from `FONT PRINT` is redrawn every frame until it is removed with `FONT CLEAR`. Text that changes must therefore start with `FONT CLEAR` every lap.
