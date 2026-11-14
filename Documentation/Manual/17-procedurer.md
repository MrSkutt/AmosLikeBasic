# Procedurer och funktioner

När ett program växer blir det svårt att hålla ordning på allt i en lång lista. Lösningen är att dela upp koden i mindre bitar med egna namn. Varje bit gör en sak, och huvudprogrammet blir kort och lätt att läsa:

```basic nocheck
HuvudLoop:
  LasTangenter
  FlyttaSpelaren
  FlyttaFiender
  RitaPoang
  WAIT VBL
GOTO HuvudLoop
```

Det finns tre sätt att göra det: **procedurer**, **funktioner** och det gamla **GOSUB**.

## PROC: procedurer

En procedur är en namngiven bit kod mellan `PROC` och `END PROC`. Du kör den genom att skriva dess namn:

```basic
Halsa
Halsa
PRINT "Klart"
END

PROC Halsa
  PRINT "Hej hej!"
END PROC
```

När programmet kommer till `Halsa` hoppar det in i proceduren, kör raderna i den och fortsätter sedan på raden efter anropet. Programmet ovan skriver `Hej hej!` två gånger och sedan `Klart`.

Procedurerna kan stå var som helst i programmet. Kommer programmet fram till en `PROC` medan det kör uppifrån och ned hoppar det över den, så du kan samla dem i slutet eller i början, som du vill.

### Parametrar

En procedur kan ta emot värden, så kallade **parametrar**. De skrivs inom parentes efter namnet när proceduren definieras, och med komma efter namnet när den anropas:

```basic
Halsa "Kotte", 3
Halsa "Bagge", 1
END

PROC Halsa(NAMN$, GANGER)
  FOR I = 1 TO GANGER
    PRINT "Hej " & NAMN$ & "!"
  NEXT I
END PROC
```

> **Obs:** Vid anropet står argumenten efter ett mellanslag, utan parentes: `Halsa "Kotte", 3`. Parenteser används bara när proceduren definieras: `PROC Halsa(NAMN$, GANGER)`.

### Variabler i procedurer

Alla variabler är gemensamma för hela programmet, utom parametrarna. En procedur kan alltså läsa och ändra variabler som huvudprogrammet använder. Det är så spelets olika delar delar på positioner, poäng och liv:

```basic
POANG = 0
LaggTillPoang 50
LaggTillPoang 25
PRINT "Poäng: " & POANG
END

PROC LaggTillPoang(P)
  POANG = POANG + P
END PROC
```

Parametrarna, här `P`, är däremot lokala. Efter anropet får en variabel med samma namn tillbaka sitt gamla värde.

### Lämna en procedur i förtid

`EXIT PROC` lämnar proceduren direkt, utan att köra resten:

```basic
Dela 10, 2
Dela 10, 0
END

PROC Dela(A, B)
  IF B = 0 THEN PRINT "Kan inte dela med noll!" : EXIT PROC
  PRINT A & " / " & B & " = " & (A / B)
END PROC
```

## FUNCTION: funktioner

En funktion fungerar som en procedur, men räknar ut ett värde och ger tillbaka det med `RETURN`. Du använder den som de inbyggda funktionerna, till exempel i ett uttryck eller efter `PRINT`:

```basic
PRINT Kvadrat(5)
PRINT Kvadrat(3) + Kvadrat(4)
END

FUNCTION Kvadrat(N)
  RETURN N * N
END FUNCTION
```

När en funktion anropas används parenteser, precis som `SIN(X)` och `LEN(A$)`.

Funktioner passar bra för att svara på frågor. I Skogshopp finns en funktion som svarar på om det finns en fast vägg på en viss plats:

```basic nocheck
FUNCTION SolidAt(X, Y)
  IF X < 0 OR Y < 0 THEN RETURN 0
  ST = TILE(4, X, Y)
  IF ST >= 0 AND ST <= 6 THEN RETURN 1
  RETURN 0
END FUNCTION
```

Och används så här: `IF SolidAt(PX + 25, PY + 6) = 1 THEN ...`

> **Obs:** Inne i en funktion fungerar bara de vanligaste kommandona: tilldelning, `IF`, `FOR`, `WHILE`, `INC`, `DEC` och `RETURN`. Ska du rita eller flytta sprites, använd en procedur.

## GOSUB och RETURN

`GOSUB` är det klassiska BASIC-sättet att anropa en bit kod. Det hoppar till en etikett och kommer tillbaka när koden når `RETURN`:

```basic
GOSUB Rubrik
PRINT "Här kommer innehållet."
GOSUB Rubrik
END

Rubrik:
  PRINT "======================"
RETURN
```

`GOSUB` tar inga parametrar. Procedurer är tydligare, men `GOSUB` finns för att gamla program ska fungera.

## Avsluta programmet

`END` avslutar programmet direkt, var det än står.

> **Prova själv:** Skriv en procedur `Ruta(BREDD, HOJD)` som skriver ut en rektangel av tecknet `#` med den bredd och höjd du anger.

> **Prova själv:** Skriv en funktion `Storst(A, B, C)` som ger det största av tre tal.
