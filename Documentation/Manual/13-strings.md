# Text and strings

Text in a program is called **strings**. A string is written inside quotation marks and can be put in a variable, printed and joined with other strings.

## Joining strings

You join strings with `&` or `+`:

```basic
FIRST$ = "Pine"
LAST$ = "Cone"
FULL$ = FIRST$ & " " & LAST$
PRINT FULL$
```

Numbers turn into text automatically when they are joined with a string:

```basic
LEVEL = 3
PRINT "You are on level " & LEVEL
```

## Asking the user: INPUT

`INPUT` prints a question and waits for the user to type something and press Enter. The answer ends up in a variable:

```basic
INPUT "What is your name? ", NAME$
PRINT "Hello " & NAME$ & "!"
```

If the user types a number, you can turn the text into a number with `VAL`:

```basic
INPUT "How old are you? ", ANSWER$
AGE = VAL(ANSWER$)
PRINT "In ten years you will be " & (AGE + 10)
```

## Length and parts of a string

| Function | Gives | Example | Answer |
|---|---|---|---|
| `LEN(s$)` | the number of characters | `LEN("PINECONE")` | `8` |
| `LEFT$(s$, n)` | the first n characters | `LEFT$("PINECONE", 4)` | `PINE` |
| `RIGHT$(s$, n)` | the last n characters | `RIGHT$("PINECONE", 4)` | `CONE` |
| `MID$(s$, start, n)` | n characters from position start | `MID$("PINECONE", 2, 3)` | `INE` |
| `MID$(s$, start)` | the rest from position start | `MID$("PINECONE", 5)` | `CONE` |

Positions count from 1, so the first character is at position 1.

```basic
WORD$ = "PROGRAMMING"
FOR I = 1 TO LEN(WORD$)
  PRINT LEFT$(WORD$, I)
NEXT I
```

## Searching in a string

`INSTR(s$, find$)` looks for one text inside another and gives the position where it starts, or `0` if it is not there:

```basic
SENTENCE$ = "Pinecone lives in the forest"
PRINT INSTR(SENTENCE$, "lives")
PRINT INSTR(SENTENCE$, "sea")
```

This prints `10` and `0`. A third value says where the search should start: `INSTR(SENTENCE$, "e", 5)`.

`INSTR` is handy for turning a character into a number, for example the letters of a level drawn in `DATA` lines into tile numbers:

```basic
TILES$ = "GDBQ"
PRINT INSTR(TILES$, "B") - 1
```

That gives `2`, so tile number 2.

## Changing strings

| Function | Does | Example | Answer |
|---|---|---|---|
| `UPPER$(s$)` | capital letters | `UPPER$("hello")` | `HELLO` |
| `LOWER$(s$)` | small letters | `LOWER$("HELLO")` | `hello` |
| `TRIM$(s$)` | removes spaces at the start and end | `TRIM$("  hello  ")` | `hello` |
| `REPLACE$(s$, old$, new$)` | replaces text | `REPLACE$("cat", "c", "h")` | `hat` |
| `REPEAT$(s$, n)` | repeats | `REPEAT$("=", 5)` | `=====` |

```basic
TITLE$ = "forest hop"
PRINT UPPER$(TITLE$)
PRINT REPEAT$("*", LEN(TITLE$))
```

## Words in a sentence

`WORD$(s$, n)` picks out word number `n` from a sentence (the words count from 1):

```basic
COMMAND$ = "GO NORTH QUICKLY"
PRINT WORD$(COMMAND$, 1)
PRINT WORD$(COMMAND$, 2)
```

This is useful in text adventures, where the player types commands such as "TAKE KEY".

## Numbers and strings

| Function | Does | Example | Answer |
|---|---|---|---|
| `STR$(number)` | number to text | `STR$(42)` | `"42"` |
| `VAL(s$)` | text to number | `VAL("3.5")` | `3.5` |
| `CHR$(code)` | the character with a given character code | `CHR$(65)` | `A` |
| `ASC(s$)` | the character code of the first character | `ASC("A")` | `65` |
| `HEX(number)` | the number in hexadecimal | `HEX(255)` | `FF` |

`VAL` gives `0` if the text is not a number.

> **Try it:** Ask for a word and print it backwards. Tip: go through the characters with `MID$(WORD$, I, 1)` from the last to the first.

> **Try it:** Ask for a name and print it with a frame of stars around it, as wide as the name.
