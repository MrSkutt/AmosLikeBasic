# Procedures and functions

As a program grows, it gets hard to keep track of everything in one long list. The answer is to split the code into smaller pieces with their own names. Each piece does one thing, and the main program becomes short and easy to read:

```basic nocheck
MainLoop:
  ReadKeys
  MovePlayer
  MoveEnemies
  DrawScore
  WAIT VBL
GOTO MainLoop
```

There are three ways to do this: **procedures**, **functions** and the old **GOSUB**.

## PROC: procedures

A procedure is a named piece of code between `PROC` and `END PROC`. You run it by writing its name:

```basic
SayHello
SayHello
PRINT "Done"
END

PROC SayHello
  PRINT "Hello there!"
END PROC
```

When the program reaches `SayHello` it jumps into the procedure, runs its lines and then carries on with the line after the call. The program above prints `Hello there!` twice and then `Done`.

Procedures can be anywhere in the program. If the program comes to a `PROC` while running from top to bottom it skips over it, so you can gather them at the end or at the start, as you like.

### Parameters

A procedure can take values, called **parameters**. They are written in brackets after the name when the procedure is defined, and with commas after the name when it is called:

```basic
SayHello "Pinecone", 3
SayHello "Beetle", 1
END

PROC SayHello(NAME$, TIMES)
  FOR I = 1 TO TIMES
    PRINT "Hello " & NAME$ & "!"
  NEXT I
END PROC
```

> **Note:** In the call the arguments come after a space, without brackets: `SayHello "Pinecone", 3`. Brackets are only used when the procedure is defined: `PROC SayHello(NAME$, TIMES)`.

### Variables in procedures

All variables are shared by the whole program, except the parameters and the local variables described below. So a procedure can read and change variables that the main program uses. That is how the different parts of a game share positions, scores and lives:

```basic
SCORE = 0
AddScore 50
AddScore 25
PRINT "Score: " & SCORE
END

PROC AddScore(P)
  SCORE = SCORE + P
END PROC
```

The parameters, here `P`, are local though. After the call, a variable with the same name gets its old value back.

### Local variables: LOCAL

Sharing everything has a catch. If a procedure uses a variable for something of its own, such as a loop counter, it changes the variable with the same name in the rest of the program. This program is meant to print one row of stars per lap, but `Stars` uses `I` too:

```basic nocheck
FOR I = 1 TO 3
  Stars I
NEXT I
END

PROC Stars(N)
  ROW$ = ""
  FOR I = 1 TO N
    ROW$ = ROW$ & "*"
  NEXT I
  PRINT ROW$
END PROC
```

After `Stars` the main program's `I` is no longer what it was, and the loop goes wrong. The answer is `LOCAL`, which gives the procedure variables of its own:

```basic
FOR I = 1 TO 3
  Stars I
NEXT I
END

PROC Stars(N)
  LOCAL I, ROW$
  FOR I = 1 TO N
    ROW$ = ROW$ & "*"
  NEXT I
  PRINT ROW$
END PROC
```

Now it prints `*`, `**` and `***`. Local variables start at 0, or as empty text if the name ends in `$`. When the procedure ends, variables with the same names get back the values they had before the call. Put `LOCAL` at the start of the procedure, and use it for everything the procedure only needs for itself.

> **Note:** A procedure that is called from inside another one sees the other one's local variables, as if they were shared.

### Leaving a procedure early

`EXIT PROC` leaves the procedure straight away, without running the rest:

```basic
Divide 10, 2
Divide 10, 0
END

PROC Divide(A, B)
  IF B = 0 THEN PRINT "Cannot divide by zero!" : EXIT PROC
  PRINT A & " / " & B & " = " & (A / B)
END PROC
```

## FUNCTION: functions

A function works like a procedure, but works out a value and gives it back with `RETURN`. You use it like the built-in functions, for example in an expression or after `PRINT`:

```basic
PRINT Square(5)
PRINT Square(3) + Square(4)
END

FUNCTION Square(N)
  RETURN N * N
END FUNCTION
```

When a function is called, brackets are used, just like `SIN(X)` and `LEN(A$)`.

Functions are good for answering questions. Forest Hop has a function that answers whether there is a solid wall at a certain place:

```basic nocheck
FUNCTION SolidAt(X, Y)
  IF X < 0 OR Y < 0 THEN RETURN 0
  ST = TILE(4, X, Y)
  IF ST >= 0 AND ST <= 6 THEN RETURN 1
  RETURN 0
END FUNCTION
```

And it is used like this: `IF SolidAt(PX + 25, PY + 6) = 1 THEN ...`

> **Note:** Inside a function only the most common commands work: assignment, `IF`, `FOR`, `WHILE`, `INC`, `DEC` and `RETURN`. If you want to draw or move sprites, use a procedure.

## GOSUB and RETURN

`GOSUB` is the classic BASIC way of calling a piece of code. It jumps to a label and comes back when the code reaches `RETURN`:

```basic
GOSUB Heading
PRINT "Here comes the content."
GOSUB Heading
END

Heading:
  PRINT "======================"
RETURN
```

`GOSUB` takes no parameters. Procedures are clearer, but `GOSUB` is there so that old programs work.

## Ending the program

`END` ends the program straight away, wherever it is.

> **Try it:** Write a procedure `Box(WIDTH, HEIGHT)` that prints a rectangle of the character `#` with the width and height you give.

> **Try it:** Write a function `Largest(A, B, C)` that gives the largest of three numbers.
