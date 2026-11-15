# Variabler

En **variabel** är en låda med ett namn, där datorn kan spara ett värde. Du lägger något i lådan med likhetstecken:

```basic
POANG = 100
PRINT POANG
```

Raden `POANG = 100` betyder "lägg talet 100 i lådan som heter POANG". Sedan skriver `PRINT POANG` ut det som ligger i lådan: `100`.

Det fina med variabler är att värdet kan ändras medan programmet kör:

```basic
POANG = 100
PRINT "Du har " & POANG & " poäng"
POANG = POANG + 50
PRINT "Nu har du " & POANG & " poäng"
```

Raden `POANG = POANG + 50` kan se konstig ut om man tänker matematik, men i BASIC betyder den: "ta värdet i POANG, lägg till 50 och lägg resultatet tillbaka i POANG". Programmet skriver först `Du har 100 poäng` och sedan `Nu har du 150 poäng`.

## Namn på variabler

Ett variabelnamn får innehålla bokstäver, siffror och understreck (`_`), men måste börja med en bokstav. Bra namn talar om vad som ligger i variabeln:

```basic
LIV = 3
SPELARE_X = 320
HOPPKRAFT = 12.5
```

Stora och små bokstäver räknas som samma, så `Liv`, `liv` och `LIV` är samma variabel.

> **Obs:** Ge inte en variabel samma namn som ett kommando, till exempel `COLOR`, `HIT` eller `WAIT`. Programmet kan då blanda ihop dem.

En variabel som du inte har gett något värde innehåller `0`:

```basic
PRINT ANTAL
```

## Talvariabler och textvariabler

En variabel kan innehålla ett tal eller en text. Text kallas i programmering för en **sträng** (en rad tecken efter varandra). Det är vanligt att namnge strängvariabler med ett dollartecken (`$`) på slutet, så att man ser att de innehåller text:

```basic
NAMN$ = "Kotte"
ALDER = 7
PRINT NAMN$ & " är " & ALDER & " år"
```

## Fler sätt att räkna

Förutom de fyra räknesätten finns tre till:

| Tecken | Betyder | Exempel | Svar |
|---|---|---|---|
| `MOD` | resten vid division | `17 MOD 5` | `2` |
| `DIV` | heltalsdivision | `17 DIV 5` | `3` |
| `^` | upphöjt till | `2 ^ 10` | `1024` |

`MOD` är användbar för att få något att upprepas. `T MOD 4` går 0, 1, 2, 3, 0, 1, 2, 3 ... när `T` räknas upp, vilket är perfekt för att växla mellan bilderna i en animation.

```basic
FOR T = 0 TO 9
  PRINT T & " MOD 4 = " & (T MOD 4)
NEXT T
```

(`FOR` och `NEXT` upprepar raderna emellan, det kommer i kapitlet om loopar.)

## INC och DEC

Att öka en variabel med ett är så vanligt att det finns egna kommandon för det:

```basic
LIV = 3
INC LIV          ; samma som LIV = LIV + 1
PRINT LIV
DEC LIV          ; samma som LIV = LIV - 1
DEC LIV
PRINT LIV
```

## Matematiska funktioner

En **funktion** räknar ut något och ger tillbaka ett svar. Du skriver funktionens namn och sedan värdet inom parentes:

```basic
PRINT ABS(-7)          ; absolutbelopp: 7
PRINT INT(3.8)         ; heltalsdelen: 3
PRINT SQR(16)          ; kvadratroten: 4
PRINT MIN(4, 9)        ; det minsta: 4
PRINT MAX(4, 9)        ; det största: 9
PRINT SGN(-12)         ; tecknet: -1, 0 eller 1
```

`INT` avrundar alltid nedåt, så `INT(-3.2)` blir `-4`.

### Slumptal

`RND(n)` ger ett slumpmässigt tal från 0 upp till (men inte med) `n`. Det blir ett decimaltal, så för att få ett heltal tar man `INT` av det. Så här slår du en tärning:

```basic
FOR KAST = 1 TO 5
  TARNING = INT(RND(6)) + 1
  PRINT "Kast " & KAST & ": " & TARNING
NEXT KAST
```

`RND(6)` ger något mellan 0 och 5.999..., `INT` gör det till 0 – 5, och `+ 1` ger 1 – 6.

### SIN och COS

`SIN` och `COS` räknar med **grader** (ett varv är 360). De ger värden mellan -1 och 1 som går i en mjuk våg, och används i spel till allt som gungar, svävar eller går runt i cirklar:

```basic
FOR VINKEL = 0 TO 360 STEP 45
  PRINT VINKEL & " grader: SIN = " & INT(SIN(VINKEL) * 100) / 100
NEXT VINKEL
```

> **Prova själv:** Gör ett program som slår två tärningar och skriver ut summan.

> **Prova själv:** Spara ditt födelseår i en variabel och låt programmet räkna ut hur gammal du blir i år.
