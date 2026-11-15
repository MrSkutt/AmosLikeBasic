# Decisions with IF

So far the programs have run every line, one after the other. With `IF` the program can choose: do something *if* something is true, and perhaps something else otherwise.

## IF ... THEN

```basic
SCORE = 120
IF SCORE > 100 THEN PRINT "Well done!"
```

After `IF` comes a **condition**, something that is either true or false. If the condition is true, whatever comes after `THEN` runs. If it is false, the program skips it.

## Comparisons

| Sign | Means |
|---|---|
| `=` | equal to |
| `<>` | not equal to |
| `<` | less than |
| `>` | greater than |
| `<=` | less than or equal to |
| `>=` | greater than or equal to |

Strings are compared in the same way:

```basic
INPUT "Password: ", ANSWER$
IF ANSWER$ = "SESAME" THEN PRINT "Come on in!"
IF ANSWER$ <> "SESAME" THEN PRINT "Wrong password."
```

`<` and `>` compare strings by character code, which for capital letters is alphabetical order: `"CAT" < "DOG"` is true. Capital letters come before small ones, so `"Zebra" < "apple"` is also true.

## ELSE

With `ELSE` you say what should happen when the condition is false:

```basic
LIVES = 0
IF LIVES > 0 THEN PRINT "The game goes on" ELSE PRINT "GAME OVER"
```

## Several commands

After `THEN` (and `ELSE`) you can write several commands with colons in between:

```basic
SCORE = 950
IF SCORE >= 1000 THEN PRINT "Extra life!" : LIVES = LIVES + 1 ELSE PRINT "Keep collecting"
```

When there is a lot of code it is clearer to write `IF` as a **block**. Then `THEN` comes last on the line, the code goes on the lines after, and the block ends with `ENDIF`:

```basic
SCORE = 1200
LIVES = 3
IF SCORE >= 1000 THEN
  PRINT "Extra life!"
  LIVES = LIVES + 1
  SCORE = SCORE - 1000
ELSE
  PRINT "Collect " & (1000 - SCORE) & " more points"
ENDIF
PRINT "Lives: " & LIVES
```

You can write `END IF` with a space if you find that easier to read. The lines inside the block are usually indented two steps, so that you can see what belongs to the `IF`. It is not required, but it makes the code much easier to read.

## AND, OR and NOT

Sometimes several things must be true at the same time. You write that with `AND`:

```basic
X = 150
IF X > 100 AND X < 200 THEN PRINT "X is between 100 and 200"
```

`OR` means that at least one of the conditions must be true:

```basic
ANSWER$ = "Y"
IF ANSWER$ = "Y" OR ANSWER$ = "y" THEN PRINT "You answered yes"
```

`NOT` turns a condition around:

```basic
DONE = 0
IF NOT DONE = 1 THEN PRINT "Not done yet"
```

If you mix `AND` and `OR`, `AND` is worked out first, just as times comes before plus. Use brackets to show what you mean:

```basic
A = 1 : B = 0 : C = 1
IF (A = 1 OR B = 1) AND C = 1 THEN PRINT "True!"
```

## Comparisons give 1 or 0

A comparison is really a small calculation: true becomes `1` and false becomes `0`. So you can print it or keep it in a variable:

```basic
PRINT 10 > 3
PRINT 10 < 3
AGE = 15
ADULT = AGE >= 18
PRINT ADULT
```

The program prints `1`, `0` and `0`.

## A number as a condition

If the condition is just a number or a variable, everything except `0` counts as true. This is handy for variables that are only 0 or 1, so-called **flags**:

```basic
HAS_KEY = 1
IF HAS_KEY THEN PRINT "You unlock the door."
```

## Choosing among many: SELECT CASE

When the same variable is compared with many different values, lots of `IF`s get messy. Then `SELECT CASE` is clearer:

```basic
DIE = INT(RND(6)) + 1
SELECT DIE
CASE 1
  PRINT "A one, bad luck!"
CASE 6
  PRINT "A six, roll again!"
DEFAULT
  PRINT "You rolled " & DIE
END SELECT
```

`SELECT` takes a value. The program finds the `CASE` with the same value and runs the lines under it, up to the next `CASE`. If none matches, the lines under `DEFAULT` run (if there is one). It works just as well with strings:

```basic
DIRECTION$ = "NORTH"
SELECT DIRECTION$
CASE "NORTH"
  PRINT "You walk towards the mountains."
CASE "SOUTH"
  PRINT "You walk towards the lake."
DEFAULT
  PRINT "You cannot go that way."
END SELECT
```

> **Try it:** Ask for an age and print "child" if it is under 13, "teenager" if it is 13 – 19 and "adult" otherwise.

> **Try it:** Make a small guessing game: the computer picks a number between 1 and 10, you guess once, and the program says whether you were right, too high or too low.
