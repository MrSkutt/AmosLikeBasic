# Keyboard and gamepad

A game has to be controllable. There are two ways of reading the keyboard: ask whether a certain key is down right now, or get the last key pressed.

## KEYSTATE: is the key down?

`KEYSTATE("name")` gives 1 as long as the key is held down and 0 otherwise. It is the right way to steer a figure, since it keeps moving while you hold the key:

```basic
SPRITE LOAD 1, "Graphics/ForestHop/pinecone.png", 32, 32, 6
SPRITE ON 1
X = 300 : Y = 220
Loop:
  IF KEYSTATE("Left") = 1 THEN X = X - 4
  IF KEYSTATE("Right") = 1 THEN X = X + 4
  IF KEYSTATE("Up") = 1 THEN Y = Y - 4
  IF KEYSTATE("Down") = 1 THEN Y = Y + 4
  SPRITE POS 1, X, Y
  WAIT VBL
GOTO Loop
```

Click in the game window first, so that it gets the keys.

### Key names

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
| function keys | `F1` to `F11` (`F8` turns the CRT effect on and off and `F12` toggles full screen) |

### A press, not a hold

`KEYSTATE` only says whether the key is down. For a jump you want to react to the *press* itself, not jump again and again while the key is held. You solve that by remembering how it was last time:

```basic nocheck
NOW = KEYSTATE("Space")
IF NOW = 1 AND BEFORE = 0 THEN PLAY JUMP
BEFORE = NOW
```

## INKEY$: the last key pressed

`INKEY$` gives the name of a key that is down, or an empty string if none is. It suits menus and "press any key":

```basic nocheck
PRINT "Press a key..."
REPEAT
  K$ = INKEY$
  WAIT VBL
UNTIL K$ <> ""
PRINT "You pressed " & K$
```

## Typing text in the graphics: INPUTG

`INPUT` (from part 1) asks in the text console. `INPUTG` does the same but writes in the graphics, with the font and colour you have chosen for `PRINTG`:

```basic nocheck
INPUTG "Your name: ", NAME$
PRINTG "Welcome " & NAME$
```

## Gamepad

If a gamepad is connected (for example Xbox, PlayStation or Switch Pro) you can read it in the same way. Up to four gamepads are supported, numbered 1 – 4.

`JOYSTATE(pad, "button")` gives 1 while the button is pressed:

| Button | Name |
|---|---|
| A, B, X, Y | `A`, `B`, `X`, `Y` |
| shoulder buttons | `LB`, `RB` |
| start and select | `Start`, `Select` |
| d-pad | `Up`, `Down`, `Left`, `Right` |

`JOYAXIS(pad, "axis")` gives how far a stick is pushed, from –100 to 100 (the triggers 0 to 100):

| Axis | Name |
|---|---|
| left stick | `LeftX`, `LeftY` |
| right stick | `RightX`, `RightY` |
| triggers | `LT`, `RT` |

It is common to support both keyboard and gamepad:

```basic nocheck
IF KEYSTATE("Right") = 1 OR JOYSTATE(1, "Right") = 1 OR JOYAXIS(1, "LeftX") > 30 THEN X = X + 4
```

> **Try it:** Build on the `KEYSTATE` example so that the figure cannot leave the screen. Tip: `MIN` and `MAX`.
