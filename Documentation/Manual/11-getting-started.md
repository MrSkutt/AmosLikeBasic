# Getting started

A program is a list of instructions that the computer follows, one at a time, from top to bottom. Each instruction is on its own line and starts with a **command**: a word that tells the computer what to do.

## Your first program

Type this into the editor and press **RUN**:

```basic
PRINT "HELLO THERE!"
```

The game window opens and the text `HELLO THERE!` appears in the top left corner. The command `PRINT` prints whatever comes after it. The text is inside quotation marks (`"`) so that the computer understands that it is text and not a command.

Now let's try several lines:

```basic
PRINT "This is line one"
PRINT "This is line two"
PRINT "And here comes line three"
```

Each `PRINT` writes on a new line. The program runs the lines in order and stops when there are no more lines.

## Calculating with PRINT

`PRINT` can also calculate. If you write a sum without quotation marks, the computer works out the answer:

```basic
PRINT 2 + 3
PRINT 10 * 4
PRINT 100 / 8
PRINT 7 - 10
```

This prints `5`, `40`, `12.5` and `-3`. The signs for the four kinds of arithmetic are:

| Sign | Means | Example | Answer |
|---|---|---|---|
| `+` | plus | `2 + 3` | `5` |
| `-` | minus | `7 - 10` | `-3` |
| `*` | times | `10 * 4` | `40` |
| `/` | divided by | `100 / 8` | `12.5` |

The computer does times and divide before plus and minus, just like in maths. If you want to change the order, use brackets:

```basic
PRINT 2 + 3 * 4
PRINT (2 + 3) * 4
```

The first gives `14` and the second `20`.

## Text and numbers together

To print text and a calculated number on the same line, join them with `&`:

```basic
PRINT "Two plus three is " & (2 + 3)
PRINT "A week has " & 7 * 24 & " hours"
```

Notice the brackets around `2 + 3`. Without them the computer works from the left: first `"Two plus three is "` is joined with `2`, and then `3` is added to the text. The result is `Two plus three is 23`! Times and divide are always done first, so `7 * 24` needs no brackets.

> **Tip:** If you are unsure, put brackets around the calculation. It never hurts.

## Comments

Everything after a semicolon (`;`) is a **comment**. The computer skips comments; they are for you and others who read the code:

```basic
; This program says hello
PRINT "HELLO!"     ; prints a greeting
```

You can also write `REM` (short for *remark*) at the start of a line:

```basic
REM This is a comment too
PRINT "HELLO AGAIN!"
```

## Several commands on one line

With a colon (`:`) you can write several commands on the same line:

```basic
PRINT "ONE" : PRINT "TWO" : PRINT "THREE"
```

It works exactly like three lines. Use it sparingly; code is easiest to read when every line does one thing.

## Clearing the screen and placing text

`CLS` (*clear screen*) wipes all the text. `LOCATE` moves the place where the next `PRINT` writes. It takes two numbers: **row** and **column**, where the top left corner is row 1, column 1. The text screen has 30 rows and 80 columns.

```basic
CLS
LOCATE 10, 30
PRINT "IN THE MIDDLE OF THE SCREEN"
LOCATE 1, 1
PRINT "UP IN THE CORNER"
```

The same thing can be written more briefly with `PRINT AT`:

```basic
PRINT AT 15, 35, "HERE!"
```

## Pausing the program

`WAIT` waits a number of milliseconds (thousandths of a second) before the program goes on. So `WAIT 1000` waits one second:

```basic
PRINT "Three..."
WAIT 1000
PRINT "Two..."
WAIT 1000
PRINT "One..."
WAIT 1000
PRINT "GO!"
```

## Upper and lower case

It does not matter whether you write commands in upper or lower case: `PRINT`, `print` and `Print` are the same. In this book commands are written in capitals so that they stand out.

> **Try it:** Write a program that prints your name in the middle of the screen, waits two seconds, clears the screen and prints "GOODBYE" in the top left corner.

> **Try it:** How many seconds are there in a day? Let the computer work it out with `PRINT`.
