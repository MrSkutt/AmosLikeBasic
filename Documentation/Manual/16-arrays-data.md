# Arrays and DATA

A variable holds one value. Often you need many values of the same kind: the positions of ten enemies, the scores in a high-score list or the squares of a game board. Then you use an **array**, a row of boxes that share a name and each have a number.

## DIM

An array is created with `DIM`. The number in brackets is how many places it has:

```basic
DIM SCORES(5)
SCORES(0) = 120
SCORES(1) = 340
SCORES(4) = 90
PRINT SCORES(1)
```

The places are numbered from 0, so `DIM SCORES(5)` gives `SCORES(0)` to `SCORES(4)`. The number is called the **index**. Places you have not given a value contain `0`.

> **Note:** `DIM A(10)` gives indexes 0 – 9, so ten places. If you try to use `A(10)` you get an error. (In the old AMOS `A(10)` existed too; here it is like in most modern languages.)

The nice thing is that the index can be a variable. Then a loop can go through the whole array:

```basic
DIM SQUARE(10)
FOR I = 0 TO 9
  SQUARE(I) = I * I
NEXT I
FOR I = 0 TO 9
  PRINT I & " squared is " & SQUARE(I)
NEXT I
```

`DIM(name)` gives the number of places, so the loop does not need to know the size in advance:

```basic
DIM X(8)
PRINT "The array has " & DIM(X) & " places"
FOR I = 0 TO DIM(X) - 1
  X(I) = RND(100)
NEXT I
```

## Starting values

You can fill an array straight away when you create it, with the values in square brackets:

```basic
DIM DAYS(7) = [31, 28, 31, 30, 31, 30, 31]
DIM NAMES$(3) = ["Pinecone", "Beetle", "Owl"]
PRINT "February has " & DAYS(1) & " days"
PRINT "The second character is called " & NAMES$(1)
```

Arrays whose names end in `$` hold text; the others hold numbers.

## Arrays with several dimensions

An array can have two (or more) indexes, like the rows and columns of a table. That is perfect for game boards:

```basic
DIM BOARD(8, 8)
BOARD(3, 4) = 1
BOARD(0, 7) = 2
FOR Y = 0 TO 7
  ROW$ = ""
  FOR X = 0 TO 7
    ROW$ = ROW$ & BOARD(X, Y)
  NEXT X
  PRINT ROW$
NEXT Y
```

Two dimensions can be filled straight away too:

```basic
DIM M(3, 3) = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]
PRINT M(1, 2)
```

## DATA and READ

When a program needs a lot of fixed information, such as a level, a tune or the questions in a quiz, it is handy to write it in `DATA` lines. `READ` then reads the values, one at a time, in the order they are written.

The `DATA` lines are gathered under a **label**, which becomes the name of the store. Before reading you choose the store with `RESTORE`:

```basic
RESTORE Animals
FOR I = 1 TO 3
  READ ANIMAL$, LEGS
  PRINT ANIMAL$ & " has " & LEGS & " legs"
NEXT I
END

Animals:
DATA "Cat", 4
DATA "Spider", 8
DATA "Bird", 2
```

The `DATA` lines never run themselves; they are only a store. `END` stops the program before it reaches them, which makes it clear where the program ends.

> **Note:** `DATA` must come after a label, and you must choose the store with `RESTORE` before the first `READ`. `DATA` lines without a label are ignored.

### Several stores

With several labels you can have several stores and choose between them. `RESTORE` always starts again from the beginning of the store:

```basic
RESTORE Fruit
READ A$, B$
PRINT A$ & " and " & B$

RESTORE Vegetables
READ A$
PRINT A$
END

Fruit:
DATA "Apple", "Pear"

Vegetables:
DATA "Carrot", "Cucumber"
```

The game Forest Hop keeps its whole level in `DATA` lines, one string per row of the level, and reads it with `RESTORE LevelData` and `READ ROW$`.

## Splitting and joining text

`SPLIT$` splits a string at a character and gives back an array. `JOIN$` does the opposite:

```basic
PARTS$ = SPLIT$("red,green,blue", ",")
FOR I = 0 TO DIM(PARTS$) - 1
  PRINT I & ": " & PARTS$(I)
NEXT I
PRINT JOIN$(PARTS$, " + ")
```

> **Try it:** Keep five names and five scores in two arrays (or in `DATA`) and print the one with the highest score.

> **Try it:** Make a quiz with three questions and answers in `DATA` lines. Count how many the player gets right.
