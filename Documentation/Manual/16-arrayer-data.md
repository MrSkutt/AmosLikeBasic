# Arrayer och DATA

En variabel rymmer ett värde. Ofta behöver man många värden av samma sort: positionerna för tio fiender, poängen i en topplista eller rutorna på en spelplan. Då använder man en **array**, en rad med lådor som delar namn och har varsitt nummer.

## DIM

En array skapas med `DIM`. Talet inom parentes är hur många platser den har:

```basic
DIM POANG(5)
POANG(0) = 120
POANG(1) = 340
POANG(4) = 90
PRINT POANG(1)
```

Platserna numreras från 0, så `DIM POANG(5)` ger `POANG(0)` till `POANG(4)`. Numret kallas **index**. Platser du inte har gett något värde innehåller `0`.

> **Obs:** `DIM A(10)` ger index 0 – 9, alltså tio platser. Försöker du använda `A(10)` får du ett fel. (I den gamla AMOS fanns även `A(10)`; här är det som i de flesta moderna språk.)

Det fina är att indexet kan vara en variabel. Då kan en loop gå igenom hela arrayen:

```basic
DIM KVADRAT(10)
FOR I = 0 TO 9
  KVADRAT(I) = I * I
NEXT I
FOR I = 0 TO 9
  PRINT I & " i kvadrat är " & KVADRAT(I)
NEXT I
```

`DIM(namn)` ger antalet platser, så att loopen inte behöver veta storleken i förväg:

```basic
DIM X(8)
PRINT "Arrayen har " & DIM(X) & " platser"
FOR I = 0 TO DIM(X) - 1
  X(I) = RND(100)
NEXT I
```

## Startvärden

Du kan fylla en array direkt när du skapar den, med värdena inom hakparenteser:

```basic
DIM DAGAR(7) = [31, 28, 31, 30, 31, 30, 31]
DIM NAMN$(3) = ["Kotte", "Bagge", "Uggla"]
PRINT "Februari har " & DAGAR(1) & " dagar"
PRINT "Den andra figuren heter " & NAMN$(1)
```

Arrayer vars namn slutar på `$` innehåller text, andra innehåller tal.

## Arrayer med flera dimensioner

En array kan ha två (eller fler) index, som rader och kolumner i en tabell. Det passar perfekt för spelplaner:

```basic
DIM PLAN(8, 8)
PLAN(3, 4) = 1
PLAN(0, 7) = 2
FOR Y = 0 TO 7
  RAD$ = ""
  FOR X = 0 TO 7
    RAD$ = RAD$ & PLAN(X, Y)
  NEXT X
  PRINT RAD$
NEXT Y
```

Två dimensioner kan också fyllas direkt:

```basic
DIM M(3, 3) = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]
PRINT M(1, 2)
```

## DATA och READ

När ett program behöver mycket fast information, som en bana, en melodi eller frågorna i ett frågesport, är det smidigt att skriva den i `DATA`-rader. `READ` läser sedan värdena, ett i taget, i den ordning de står.

`DATA`-raderna samlas under en **etikett**, som blir namnet på förrådet. Innan du läser väljer du förråd med `RESTORE`:

```basic
RESTORE Djur
FOR I = 1 TO 3
  READ DJUR$, BEN
  PRINT DJUR$ & " har " & BEN & " ben"
NEXT I
END

Djur:
DATA "Katt", 4
DATA "Spindel", 8
DATA "Fågel", 2
```

`DATA`-raderna körs aldrig själva, de är bara förråd. `END` stoppar programmet innan det når dem, vilket gör det tydligt var programmet slutar.

> **Obs:** `DATA` måste stå efter en etikett, och du måste välja förråd med `RESTORE` innan första `READ`. `DATA`-rader utan etikett ignoreras.

### Flera förråd

Med flera etiketter kan du ha flera förråd och välja mellan dem. `RESTORE` börjar alltid om från början av förrådet:

```basic
RESTORE Frukter
READ A$, B$
PRINT A$ & " och " & B$

RESTORE Grönsaker
READ A$
PRINT A$
END

Frukter:
DATA "Äpple", "Päron"

Grönsaker:
DATA "Morot", "Gurka"
```

Skogshopp-spelet lagrar hela sin bana i `DATA`-rader, en sträng per rad på banan, och läser in den med `RESTORE LevelData` och `READ ROW$`.

## Dela upp och sätta ihop text

`SPLIT$` delar en sträng vid ett tecken och ger tillbaka en array. `JOIN$` gör tvärtom:

```basic
DELAR$ = SPLIT$("röd,grön,blå", ",")
FOR I = 0 TO DIM(DELAR$) - 1
  PRINT I & ": " & DELAR$(I)
NEXT I
PRINT JOIN$(DELAR$, " + ")
```

> **Prova själv:** Lagra fem namn och fem poäng i varsin array (eller i `DATA`) och skriv ut den med högst poäng.

> **Prova själv:** Gör ett frågesport med tre frågor och svar i `DATA`-rader. Räkna hur många rätt spelaren får.
