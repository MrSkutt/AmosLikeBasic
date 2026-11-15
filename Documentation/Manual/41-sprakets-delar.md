# Språkets byggstenar

Den här delen är till för att slå upp saker i. Kapitlen räknar upp allt som AMOS Like Basic kan, med exakt syntax och korta exempel. Den förklarar inte grunderna; för det finns del 1 och 2.

I syntaxen gäller:

- Ord med **STORA BOKSTÄVER** skrivs precis som de står.
- Ord med *små bokstäver*, som `x`, `lager` eller `fil`, byter du mot ett värde: ett tal, en text, en variabel eller ett helt uttryck.
- Det som står inom hakparentes `[ ]` får utelämnas.

## Rader och kommandon

Ett program är en rad kommandon som körs uppifrån och ned. Flera kommandon kan stå på samma rad med kolon emellan:

```basic
X = 5 : Y = 10 : PRINT X + Y
```

Stora och små bokstäver spelar ingen roll i kommandon och variabelnamn, så `print`, `Print` och `PRINT` är samma sak.

## Kommentarer

| Skrivsätt | Betydelse |
|---|---|
| `; text` | resten av raden är en kommentar |
| `REM text` | hela raden är en kommentar |

## Etiketter

En **etikett** är ett namn följt av kolon, ensamt på en rad. `GOTO`, `GOSUB`, `RESTORE` och `ON ERROR GOTO` hoppar till etiketter.

```basic nocheck
Start:
  PRINT "HEJ"
GOTO Start
```

## Variabler

| Sort | Namn | Exempel |
|---|---|---|
| tal | bokstäver och siffror, börjar med bokstav | `X`, `POANG`, `LIV2` |
| text | som ovan, slutar med `$` | `NAMN$`, `RAD$` |
| array | skapas med `DIM` | `DIM A(10)`, `DIM N$(5)` |

Variabler behöver inte skapas i förväg. En talvariabel som inte har fått något värde är `0`, och en textvariabel är tom. Tal lagras med decimaler, så `7 / 2` blir `3.5`.

## Värden

| Sort | Skrivsätt | Exempel |
|---|---|---|
| heltal | siffror | `42`, `-7` |
| decimaltal | punkt som decimaltecken | `3.14`, `0.5` |
| text | inom citattecken | `"HEJ"` |
| array med startvärden | inom hakparentes | `[1, 2, 3]` |

## Operatorer

Från högst till lägst prioritet. Det som står högre räknas ut först, och parenteser går alltid före.

| Operator | Betyder | Exempel | Ger |
|---|---|---|---|
| `( )` | parentes | `(2 + 3) * 4` | `20` |
| `^` | upphöjt till | `2 ^ 10` | `1024` |
| `-` (framför) | minus | `-X` | |
| `*` `/` `MOD` `DIV` | gånger, delat med, rest, heltalsdivision | `17 MOD 5` | `2` |
| `+` `-` `&` | plus, minus, sätt ihop text | `"A" & 1` | `A1` |
| `=` `<>` `<` `>` `<=` `>=` | jämförelse | `10 > 3` | `1` |
| `NOT` | inte | `NOT 5 = 5` | `0` |
| `AND` | och | `1 > 0 AND 2 > 0` | `1` |
| `OR` | eller | `1 < 0 OR 2 < 0` | `0` |

Operatorer på samma nivå räknas från vänster till höger, utom `^` som räknas från höger: `2 ^ 3 ^ 2` är `2 ^ 9`. Minus framför räknas efter `^`, så `-2 ^ 2` är `-4`. Eftersom `&` och `+` är på samma nivå blir `"A" & 2 + 3` texten `A23`; skriv `"A" & (2 + 3)` för att få `A5`.

`+` sätter också ihop två texter, men `&` fungerar även mellan text och tal och är därför säkrare.

En jämförelse ger `1` om den är sann och `0` om den är falsk, så den kan användas i uttryck och sparas i variabler: `KLAR = X > 100`. `AND`, `OR` och `NOT` ger också `1` eller `0`, och räknar allt utom `0` som sant. Texter jämförs tecken för tecken, så `"abc" < "abd"` är `1`.

## Färger

Alla kommandon som tar en färg (`INK`, `INKG`, `PAPER`, `PAPERG` och färgerna i `RASTER`) förstår samma skrivsätt:

| Skrivsätt | Exempel |
|---|---|
| nummer 0 – 15 i paletten | `INKG 7` |
| engelskt färgnamn | `INKG "Orange"` |
| hexadecimalt | `INKG "#FF8800"` |
| röd, grön, blå 0 – 255 | `INKG "255,136,0"` |

Paletten och färgnamnen finns i kapitlet *Tabeller*.

## Lager med #

Ritkommandon ritar i det lager som är valt med `SCREEN SELECT`. Många av dem kan i stället få ett lagernummer med `#` först, som bara gäller just det kommandot:

```basic nocheck
BAR #2, 0, 0, 100, 100
TEXT #3, 10, 10, "POÄNG"
```

Det fungerar för `PLOT`, `LINE`, `BOX`, `BAR`, `CIRCLE`, `CIRCLEF`, `ELLIPSE`, `FILL`, `TEXT`, `PRINTG`, `LOCATEG`, `CLSG`, `LOAD`, `SPRITE`, `RASTER`, `FONT` och `TILE DRAW`.

## Filnamn

Filnamn skrivs inom citattecken. En relativ sökväg som `"Graphics/hero.png"` letas efter i projektets mapp, och när spelet är exporterat i appens egen mapp. Använd alltid snedstreck `/`, även på Windows.
