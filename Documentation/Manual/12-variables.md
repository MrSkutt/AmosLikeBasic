# Variables

A **variable** is a box with a name, where the computer can keep a value. You put something in the box with an equals sign:

```basic
SCORE = 100
PRINT SCORE
```

The line `SCORE = 100` means "put the number 100 in the box called SCORE". Then `PRINT SCORE` prints what is in the box: `100`.

The nice thing about variables is that the value can change while the program runs:

```basic
SCORE = 100
PRINT "You have " & SCORE & " points"
SCORE = SCORE + 50
PRINT "Now you have " & SCORE & " points"
```

The line `SCORE = SCORE + 50` may look odd if you think of maths, but in BASIC it means: "take the value in SCORE, add 50 and put the result back in SCORE". The program first prints `You have 100 points` and then `Now you have 150 points`.

## Variable names

A variable name may contain letters, digits and underscores (`_`), but must start with a letter. Good names tell you what is in the variable:

```basic
LIVES = 3
PLAYER_X = 320
JUMP_POWER = 12.5
```

Upper and lower case count as the same, so `Lives`, `lives` and `LIVES` are the same variable.

> **Note:** Do not give a variable the same name as a command, for example `COLOR`, `HIT` or `WAIT`. The program can then mix them up.

A variable that you have not given a value contains `0`:

```basic
PRINT COUNTER
```

## Number variables and text variables

A variable can hold a number or a text. In programming, text is called a **string** (a row of characters after each other). It is common to name string variables with a dollar sign (`$`) at the end, so that you can see that they hold text:

```basic
NAME$ = "Pinecone"
AGE = 7
PRINT NAME$ & " is " & AGE & " years old"
```

## More ways to calculate

Besides the four kinds of arithmetic there are three more:

| Sign | Means | Example | Answer |
|---|---|---|---|
| `MOD` | the remainder after division | `17 MOD 5` | `2` |
| `DIV` | whole-number division | `17 DIV 5` | `3` |
| `^` | to the power of | `2 ^ 10` | `1024` |

`MOD` is useful for making something repeat. `T MOD 4` goes 0, 1, 2, 3, 0, 1, 2, 3 ... as `T` counts up, which is perfect for switching between the pictures of an animation.

```basic
FOR T = 0 TO 9
  PRINT T & " MOD 4 = " & (T MOD 4)
NEXT T
```

(`FOR` and `NEXT` repeat the lines in between; that comes in the chapter on loops.)

## INC and DEC

Adding one to a variable is so common that there are commands for it:

```basic
LIVES = 3
INC LIVES          ; the same as LIVES = LIVES + 1
PRINT LIVES
DEC LIVES          ; the same as LIVES = LIVES - 1
DEC LIVES
PRINT LIVES
```

## Maths functions

A **function** works something out and gives back an answer. You write the name of the function and then the value in brackets:

```basic
PRINT ABS(-7)          ; absolute value: 7
PRINT INT(3.8)         ; the whole-number part: 3
PRINT SQR(16)          ; the square root: 4
PRINT MIN(4, 9)        ; the smallest: 4
PRINT MAX(4, 9)        ; the largest: 9
PRINT SGN(-12)         ; the sign: -1, 0 or 1
```

`INT` always rounds down, so `INT(-3.2)` is `-4`.

### Random numbers

`RND(n)` gives a random number from 0 up to (but not including) `n`. It is a decimal number, so to get a whole number you take `INT` of it. This is how you roll a die:

```basic
FOR ROLL = 1 TO 5
  DIE = INT(RND(6)) + 1
  PRINT "Roll " & ROLL & ": " & DIE
NEXT ROLL
```

`RND(6)` gives something between 0 and 5.999..., `INT` turns it into 0 – 5, and `+ 1` gives 1 – 6.

### SIN and COS

`SIN` and `COS` work in **degrees** (one turn is 360). They give values between -1 and 1 that go in a smooth wave, and in games they are used for anything that sways, floats or goes round in circles:

```basic
FOR ANGLE = 0 TO 360 STEP 45
  PRINT ANGLE & " degrees: SIN = " & INT(SIN(ANGLE) * 100) / 100
NEXT ANGLE
```

> **Try it:** Make a program that rolls two dice and prints the total.

> **Try it:** Store the year you were born in a variable and let the program work out how old you will be this year.
