# Another game: Frog Hop

**Frog Hop** is a smaller game than Forest Hop, but it shows some tricks that Forest Hop does not use. Open `Projects/froghop.amosproj` and run it.

Croak the frog stands at the bottom and must get to the top. There are seven floors, and every floor has holes that slide sideways. If Croak jumps when a hole is above, it gets up one floor. If there is no hole, it bumps its head and gets dizzy for a while. If it stands where a hole comes along, it falls down one floor. From level 2 wasps fly along the floors, and touching a wasp costs a life.

The game has no images at all. Everything is drawn with `BAR` and `CIRCLEF` every frame, and the sounds and music are made in the tracker.

## Floors with holes

The holes are kept in arrays, one floor at a time:

```basic nocheck
DIM LH(8)         ; number of holes in each floor
DIM LS(8)         ; speed of the holes in each floor (minus = left)
DIM LX(8,3)       ; x position of the holes, up to three per floor
```

All holes in a floor have the same speed, so they never merge. Every frame they move, and when a hole goes off one side it comes in on the other:

```basic nocheck
X=LX(L,K)+LS(L)
IF X<0 THEN X=X+640
IF X>=640 THEN X=X-640
LX(L,K)=X
```

A floor is drawn as pieces between the holes. The tricky part is a hole that lies across the edge, for example from x 620 to 680. It then covers both 620 – 639 and 0 – 39. The procedure `DrawFloor` sorts the holes from the left and draws the pieces between them, and finally the piece from the last hole, round the edge, to the first.

## Does the frog fit in the hole?

The procedure `InHole` answers whether floor `L` has a hole where the frog fits:

```basic nocheck
PROC InHole(L, X, M)
  HIT=0
  FOR K=0 TO LH(L)-1
    D=X-LX(L,K)
    IF D<0 THEN D=D+640
    IF D>=M AND D<=HW-M THEN HIT=1
  NEXT K
END PROC
```

`D` is how far into the hole the frog is, counted from the left edge of the hole. Adding 640 when `D` is negative makes it work even when the hole lies across the edge. `M` is a margin. When the frog jumps it is enough to be 4 dots into the hole, but to fall through a hole it must be 10 dots in; otherwise the game gets too mean.

The same procedure is used in two places:

- When the frog jumps and its head reaches the floor above, the game asks `InHole PF+1, PX, 4`. If there is a hole the jump continues up; otherwise the frog bounces down and gets dizzy.
- When the frog stands still, the game asks `InHole PF, PX, 10` about the floor it stands on. If there is a hole there, it falls.

## Double buffering

Frog Hop clears and redraws everything every frame. If you draw in the layer that is showing, the screen can be shown halfway through, when the floors are drawn but not the frog. That looks like flicker.

The solution is called **double buffering** and was used back on the Amiga. The game has two layers for the graphics, 1 and 2. It always draws in the one that is hidden, and then swaps:

```basic nocheck
BACK=2-T MOD 2      ; 1 and 2 every other frame
SCREEN SELECT BACK
CLSG
; ... draw everything ...
SCREEN ON BACK
SCREEN OFF 3-BACK
```

That way you only ever see finished pictures. Games that move sprites instead of redrawing do not need this, since sprites are always shown whole.

## Sounds from the tracker

Jump, fall, bonk and the rest come from the sound bank with `TRACK SFX`. The sounds play on channels 5 – 8, so the music on channels 1 – 4 carries on:

```basic nocheck
TRACK SFX LOAD "Sound/Tracker/sfx.amostrk"
...
TRACK SFX "Jump", "C-4"
```

The music is `frog_hop.amostrk`. On the title screen it plays a little quieter, with `TRACK VOLUME 0.6`.

> **Try it:** Make the game easier with wider holes (`HW`) or harder with more holes per floor in `SetupLevel`.

> **Try it:** Make your own jump sound in the tracker: open `sfx.amostrk`, change the table for `Jump` and save. How does `0, +4, +7, +12` sound?
