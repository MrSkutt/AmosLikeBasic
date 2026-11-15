# Files, time and errors

## Saving and reading files

A game often wants to remember things between plays, such as a high-score list. You do that by writing to a file and reading from it next time.

A file is opened with `OPEN` and a **channel number**, which you then use to write or read:

| Command | Does |
|---|---|
| `OPEN OUT channel, "file"` | opens a file for writing (an old file with the same name is replaced) |
| `OPEN APPEND channel, "file"` | opens for writing at the end of an existing file |
| `OPEN IN channel, "file"` | opens for reading |
| `PRINT #channel, value` | writes a line |
| `INPUT #channel, variable` | reads a line |
| `EOF(channel)` | 1 when the whole file has been read |
| `CLOSE channel` | closes the file |

```basic
OPEN OUT 1, "highscores.txt"
PRINT #1, "Pinecone"
PRINT #1, 1200
PRINT #1, "Beetle"
PRINT #1, 900
CLOSE 1

OPEN IN 1, "highscores.txt"
WHILE EOF(1) = 0
  INPUT #1, NAME$
  INPUT #1, SCORE
  PRINT NAME$ & ": " & SCORE
WEND
CLOSE 1
```

If you read into a variable whose name ends in `$` you get the line as text; otherwise it is turned into a number. The file is saved in the folder the program runs from, unless you give a full path.

## Time

| Function / command | Gives |
|---|---|
| `MILLIS()` | milliseconds since the program started |
| `MICROS()` | microseconds since the program started |
| `TICKS` | the same as `MILLIS()` |

`MILLIS()` suits anything that should happen after a certain time, whatever the speed of the computer:

```basic
START = MILLIS()
REPEAT
  WAIT VBL
UNTIL MILLIS() - START >= 2000
PRINT "Two seconds have passed"
```

To measure how long a part of your program takes there is `PROFILE`:

```basic
PROFILE START "loop"
FOR I = 1 TO 50000
  X = SIN(I) * COS(I)
NEXT I
PROFILE END "loop"
PRINT "The loop took " & TIME_loop & " ms"
```

`PROFILE END` writes the time in the log window and keeps it in the variable `TIME_name`.

## When something goes wrong

If the program tries to do something impossible, such as dividing by zero or reading outside an array, it stops with an error message in the log window. It says what happened and on which line.

Sometimes you want the program to cope by itself instead. With `ON ERROR` you decide what should happen:

| Command | Does on an error |
|---|---|
| `ON ERROR GOTO label` | jumps to the label |
| `ON ERROR RESUME NEXT` | skips the command that went wrong and carries on |
| `ON ERROR BREAK` | stops as usual (the default) |

After an error `ERR$` holds the error message and `ERL` the line number:

```basic
ON ERROR GOTO Oops
OPEN IN 1, "does-not-exist.txt"
PRINT "This line does not run"
END

Oops:
  PRINT "Something went wrong on line " & ERL
  PRINT ERR$
```

> **Try it:** Make a game where you press space as close to exactly three seconds after the start as you can. Save the best result in a file.
