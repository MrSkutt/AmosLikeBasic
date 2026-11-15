# Loops

A **loop** repeats code. Computers are good at doing the same thing many times without getting tired, and nearly every game is built around a loop that runs over and over, once for every picture on the screen.

## FOR ... NEXT

`FOR` counts a variable from one number to another and runs the code up to `NEXT` once for each value:

```basic
FOR I = 1 TO 5
  PRINT "Lap number " & I
NEXT I
```

`I` is first 1, then 2, 3, 4 and finally 5. After `NEXT I` the program continues with the line after the loop.

With `STEP` you decide how much the variable changes each lap. A negative step counts backwards:

```basic
FOR I = 0 TO 100 STEP 25
  PRINT I
NEXT I

FOR COUNTDOWN = 10 TO 1 STEP -1
  PRINT COUNTDOWN
NEXT COUNTDOWN
PRINT "LIFT OFF!"
```

> **Note:** `FOR` only counts in whole numbers. The start, end and step are rounded to the nearest whole number, so both `STEP 0.5` and `STEP 0.1` become step 0, which gives a loop that never ends. If you need decimals, count in whole numbers and divide: `FOR I = 0 TO 10` and use `I / 10`.

> **Note:** A `FOR` loop always runs at least once. `FOR K = 0 TO N - 1` runs one lap with `K = 0` even when `N` is 0. If the loop might have nothing to do, check first with `IF N > 0 THEN`.

Loops can be inside each other. Here a times table is printed:

```basic
FOR ROW = 1 TO 5
  OUT$ = ""
  FOR COL = 1 TO 5
    OUT$ = OUT$ & (ROW * COL) & "  "
  NEXT COL
  PRINT OUT$
NEXT ROW
```

## WHILE ... WEND

`WHILE` repeats as long as a condition is true. The condition is checked *before* every lap, so if it is false from the start the loop does not run at all:

```basic
MONEY = 100
DAYS = 0
WHILE MONEY > 0
  MONEY = MONEY - 15
  DAYS = DAYS + 1
WEND
PRINT "The money lasted " & DAYS & " days"
```

## REPEAT ... UNTIL

`REPEAT` repeats *until* a condition becomes true. The condition is checked *after* every lap, so the loop always runs at least once:

```basic
REPEAT
  DIE = INT(RND(6)) + 1
  PRINT "You rolled " & DIE
UNTIL DIE = 6
PRINT "Finally a six!"
```

## DO ... LOOP

`DO ... LOOP` repeats forever. To get out you write `EXIT DO`:

```basic
X = 1
DO
  X = X * 2
  IF X > 1000 THEN EXIT DO
LOOP
PRINT "The first power of two above 1000 is " & X
```

In the same way `EXIT WHILE` leaves a `WHILE` loop and `EXIT REPEAT` a `REPEAT` loop.

## Jumping with GOTO

You can put a **label** on a line: a name followed by a colon, alone on the line. With `GOTO` the program jumps there:

```basic
COUNTER = 0
Again:
  COUNTER = COUNTER + 1
  PRINT "Lap " & COUNTER
  IF COUNTER < 3 THEN GOTO Again
PRINT "Done"
```

A game nearly always has a main loop that looks like this. The label and `GOTO` make a loop that never ends, and `WAIT VBL` waits for the next picture (more about that in the graphics part):

```basic nocheck
MainLoop:
  ; read keys, move things, draw
  WAIT VBL
GOTO MainLoop
```

> **Tip:** Use `GOTO` sparingly. Programs with lots of jumps back and forth are hard to follow. `FOR`, `WHILE`, `REPEAT` and procedures (next chapter) are nearly always clearer. The main loop of a game is the classic exception.

## A loop that measures time

`MILLIS()` gives the number of milliseconds since the program started. With it you can measure how long something takes:

```basic
START = MILLIS()
TOTAL = 0
FOR I = 1 TO 100000
  TOTAL = TOTAL + I
NEXT I
PRINT "The total is " & TOTAL
PRINT "It took " & (MILLIS() - START) & " ms"
```

> **Try it:** Print all numbers between 1 and 100 that can be divided evenly by 7. Tip: `I MOD 7 = 0`.

> **Try it:** Change the guessing game from the last chapter so that you can keep guessing until you are right, and print how many guesses it took.
