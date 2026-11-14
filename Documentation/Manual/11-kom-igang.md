# Kom igång

Ett program är en lista med instruktioner som datorn följer, en i taget, uppifrån och ned. Varje instruktion står på en egen rad och börjar med ett **kommando**: ett ord som talar om vad datorn ska göra.

## Ditt första program

Skriv in det här i editorn och tryck på **RUN**:

```basic
PRINT "HEJ PÅ DIG!"
```

Spelfönstret öppnas och texten `HEJ PÅ DIG!` står överst till vänster. Kommandot `PRINT` skriver ut det som står efter det. Texten står inom citattecken (`"`) för att datorn ska förstå att det är text och inte ett kommando.

Nu provar vi flera rader:

```basic
PRINT "Det här är rad ett"
PRINT "Det här är rad två"
PRINT "Och här kommer rad tre"
```

Varje `PRINT` skriver på en ny rad. Programmet kör raderna i tur och ordning och slutar när det inte finns fler rader.

## Räkna med PRINT

`PRINT` kan också räkna. Skriver du ett räknestycke utan citattecken räknar datorn ut svaret:

```basic
PRINT 2 + 3
PRINT 10 * 4
PRINT 100 / 8
PRINT 7 - 10
```

Det skriver ut `5`, `40`, `12.5` och `-3`. Tecknen för de fyra räknesätten är:

| Tecken | Betyder | Exempel | Svar |
|---|---|---|---|
| `+` | plus | `2 + 3` | `5` |
| `-` | minus | `7 - 10` | `-3` |
| `*` | gånger | `10 * 4` | `40` |
| `/` | delat med | `100 / 8` | `12.5` |

Datorn räknar gånger och delat före plus och minus, precis som i matematiken. Vill du ändra ordningen använder du parenteser:

```basic
PRINT 2 + 3 * 4
PRINT (2 + 3) * 4
```

Det första blir `14` och det andra `20`.

## Text och tal tillsammans

Vill du skriva ut text och ett uträknat tal på samma rad sätter du ihop dem med `&`:

```basic
PRINT "Två plus tre är " & (2 + 3)
PRINT "En vecka har " & 7 * 24 & " timmar"
```

Lägg märke till parenteserna runt `2 + 3`. Utan dem räknar datorn från vänster: först sätts `"Två plus tre är "` ihop med `2`, och sedan läggs `3` till texten. Resultatet blir `Två plus tre är 23`! Gånger och delat räknas alltid först, så `7 * 24` behöver inga parenteser.

> **Tips:** Är du osäker, sätt parenteser runt räknestycket. Det skadar aldrig.

## Kommentarer

Allt som står efter ett semikolon (`;`) är en **kommentar**. Datorn hoppar över kommentarer, de är till för dig och andra som läser koden:

```basic
; Det här programmet hälsar
PRINT "HEJ!"     ; skriver ut en hälsning
```

Du kan också skriva `REM` (från engelskans *remark*) först på en rad:

```basic
REM Det här är också en kommentar
PRINT "HEJ IGEN!"
```

## Flera kommandon på samma rad

Med kolon (`:`) kan du skriva flera kommandon på samma rad:

```basic
PRINT "ETT" : PRINT "TVÅ" : PRINT "TRE"
```

Det fungerar exakt som tre rader. Använd det med måtta, kod är lättast att läsa när varje rad gör en sak.

## Rensa skärmen och placera texten

`CLS` (*clear screen*) suddar ut all text. `LOCATE` flyttar platsen där nästa `PRINT` skriver. Den tar två tal: **rad** och **kolumn**, där övre vänstra hörnet är rad 1, kolumn 1. Textskärmen har 30 rader och 80 kolumner.

```basic
CLS
LOCATE 10, 30
PRINT "MITT PÅ SKÄRMEN"
LOCATE 1, 1
PRINT "UPPE I HÖRNET"
```

Samma sak kan skrivas kortare med `PRINT AT`:

```basic
PRINT AT 15, 35, "HÄR!"
```

## Pausa programmet

`WAIT` väntar ett antal millisekunder (tusendels sekunder) innan programmet fortsätter. `WAIT 1000` väntar alltså en sekund:

```basic
PRINT "Tre..."
WAIT 1000
PRINT "Två..."
WAIT 1000
PRINT "Ett..."
WAIT 1000
PRINT "KÖR!"
```

## Stora och små bokstäver

Det spelar ingen roll om du skriver kommandon med stora eller små bokstäver: `PRINT`, `print` och `Print` är samma sak. I den här boken skrivs kommandon med stora bokstäver för att de ska synas tydligt.

> **Prova själv:** Skriv ett program som skriver ditt namn mitt på skärmen, väntar två sekunder, rensar skärmen och skriver "HEJ DÅ" i övre vänstra hörnet.

> **Prova själv:** Hur många sekunder finns det på ett dygn? Låt datorn räkna ut det med `PRINT`.
