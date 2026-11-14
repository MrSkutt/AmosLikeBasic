# Text och strängar

Text i ett program kallas **strängar**. En sträng skrivs inom citattecken och kan läggas i en variabel, skrivas ut och sättas ihop med andra strängar.

## Sätta ihop strängar

Du sätter ihop strängar med `&` eller `+`:

```basic
FORNAMN$ = "Kotte"
EFTERNAMN$ = "Skogsson"
HELA$ = FORNAMN$ & " " & EFTERNAMN$
PRINT HELA$
```

Tal blir automatiskt text när de sätts ihop med en sträng:

```basic
NIVA = 3
PRINT "Du är på nivå " & NIVA
```

## Fråga användaren: INPUT

`INPUT` skriver en fråga och väntar på att användaren skriver något och trycker på Enter. Svaret hamnar i en variabel:

```basic
INPUT "Vad heter du? ", NAMN$
PRINT "Hej " & NAMN$ & "!"
```

Skriver användaren ett tal kan du göra om texten till ett tal med `VAL`:

```basic
INPUT "Hur gammal är du? ", SVAR$
ALDER = VAL(SVAR$)
PRINT "Om tio år är du " & (ALDER + 10)
```

## Längd och delar av en sträng

| Funktion | Ger | Exempel | Svar |
|---|---|---|---|
| `LEN(s$)` | antal tecken | `LEN("KOTTE")` | `5` |
| `LEFT$(s$, n)` | de första n tecknen | `LEFT$("KOTTE", 3)` | `KOT` |
| `RIGHT$(s$, n)` | de sista n tecknen | `RIGHT$("KOTTE", 2)` | `TE` |
| `MID$(s$, start, n)` | n tecken från position start | `MID$("KOTTE", 2, 3)` | `OTT` |
| `MID$(s$, start)` | resten från position start | `MID$("KOTTE", 3)` | `TTE` |

Positionerna räknas från 1, så första tecknet har position 1.

```basic
ORD$ = "PROGRAMMERING"
FOR I = 1 TO LEN(ORD$)
  PRINT LEFT$(ORD$, I)
NEXT I
```

## Söka i en sträng

`INSTR(s$, sök$)` letar efter en text i en annan och ger positionen där den börjar, eller `0` om den inte finns:

```basic
MENING$ = "Kotte bor i skogen"
PRINT INSTR(MENING$, "bor")
PRINT INSTR(MENING$, "havet")
```

Det skriver ut `7` och `0`. Ett tredje värde anger var sökningen ska börja: `INSTR(MENING$, "o", 3)`.

`INSTR` är praktisk för att översätta ett tecken till ett nummer. Skogshopp-spelet använder det för att göra om tecknen i en bana till tile-nummer:

```basic
TILES$ = "GDBQ"
PRINT INSTR(TILES$, "B") - 1
```

Det ger `2`, alltså tile nummer 2.

## Ändra strängar

| Funktion | Gör | Exempel | Svar |
|---|---|---|---|
| `UPPER$(s$)` | stora bokstäver | `UPPER$("hej")` | `HEJ` |
| `LOWER$(s$)` | små bokstäver | `LOWER$("HEJ")` | `hej` |
| `TRIM$(s$)` | tar bort mellanslag i början och slutet | `TRIM$("  hej  ")` | `hej` |
| `REPLACE$(s$, gammal$, ny$)` | byter ut text | `REPLACE$("katt", "k", "h")` | `hatt` |
| `REPEAT$(s$, n)` | upprepar | `REPEAT$("=", 5)` | `=====` |

```basic
TITEL$ = "skogshopp"
PRINT UPPER$(TITEL$)
PRINT REPEAT$("*", LEN(TITEL$))
```

## Ord i en mening

`WORD$(s$, n)` plockar ut ord nummer `n` ur en mening (orden räknas från 1):

```basic
KOMMANDO$ = "GÅ NORR SNABBT"
PRINT WORD$(KOMMANDO$, 1)
PRINT WORD$(KOMMANDO$, 2)
```

Det är användbart i textäventyr, där spelaren skriver kommandon som "TA NYCKEL".

## Tal och strängar

| Funktion | Gör | Exempel | Svar |
|---|---|---|---|
| `STR$(tal)` | tal till text | `STR$(42)` | `"42"` |
| `VAL(s$)` | text till tal | `VAL("3.5")` | `3.5` |
| `CHR$(kod)` | tecknet med en viss teckenkod | `CHR$(65)` | `A` |
| `ASC(s$)` | teckenkoden för första tecknet | `ASC("A")` | `65` |
| `HEX(tal)` | talet hexadecimalt | `HEX(255)` | `FF` |

`VAL` ger `0` om texten inte är ett tal.

> **Prova själv:** Fråga efter ett ord och skriv ut det baklänges. Tips: gå igenom tecknen med `MID$(ORD$, I, 1)` från det sista till det första.

> **Prova själv:** Fråga efter ett namn och skriv ut det med en ram av stjärnor runt, lika bred som namnet.
