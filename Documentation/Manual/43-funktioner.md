# Funktioner

En funktion räknar ut ett värde. Argumenten står inom parentes efter namnet, och resultatet används i ett uttryck: `X = ABS(DX)`, `PRINT LEN(A$)`. Funktioner vars namn slutar på `$` ger text, de andra ger tal.

Vinklar anges i **grader**, inte radianer.

## Tal

| Funktion | Ger | Exempel | Resultat |
|---|---|---|---|
| `ABS(x)` | x utan minustecken | `ABS(-7)` | `7` |
| `ATN(x)` | vinkeln vars tangens är x, i grader | `ATN(1)` | `45` |
| `COS(v)` | cosinus för vinkeln v | `COS(60)` | `0.5` |
| `DEC(x)` | x minus 1 | `DEC(5)` | `4` |
| `EXP(x)` | e upphöjt till x | `EXP(0)` | `1` |
| `INC(x)` | x plus 1 | `INC(5)` | `6` |
| `INT(x)` | x avrundat nedåt till heltal | `INT(-2.5)` | `-3` |
| `LOG(x)` | naturliga logaritmen | `LOG(1)` | `0` |
| `LOG10(x)` | tiologaritmen | `LOG10(1000)` | `3` |
| `LOG2(x)` | tvålogaritmen | `LOG2(8)` | `3` |
| `MAX(a, b)` | det största av två tal | `MAX(3, 8)` | `8` |
| `MIN(a, b)` | det minsta av två tal | `MIN(3, 8)` | `3` |
| `RND(x)` | ett slumptal från 0 upp till (men inte med) x, med decimaler | `INT(RND(6)) + 1` | 1 – 6 |
| `SGN(x)` | –1, 0 eller 1 beroende på tecknet | `SGN(-3)` | `-1` |
| `SIN(v)` | sinus för vinkeln v | `SIN(90)` | `1` |
| `SQR(x)` | kvadratroten | `SQR(16)` | `4` |
| `TAN(v)` | tangens för vinkeln v | `TAN(45)` | `1` |

`INC` och `DEC` som funktioner ändrar inte variabeln; det gör kommandona `INC` och `DEC`.

## Text

| Funktion | Ger | Exempel | Resultat |
|---|---|---|---|
| `ASC(s$)` | teckenkoden för första tecknet | `ASC("A")` | `65` |
| `CHR$(kod)` | tecknet med koden | `CHR$(65)` | `A` |
| `FIND(s$, sök$ [, start])` | var sök$ först finns, räknat från **0**; –1 om den inte finns | `FIND("KOTTE", "T")` | `2` |
| `FINDLAST(s$, sök$)` | var sök$ sist finns, från 0; –1 om den inte finns | `FINDLAST("KOTTE", "T")` | `3` |
| `HEX(x)` | talet hexadecimalt | `HEX(255)` | `FF` |
| `INSTR(s$, sök$ [, start])` | var sök$ först finns, räknat från **1**; 0 om den inte finns | `INSTR("KOTTE", "T")` | `3` |
| `LEFT$(s$, n)` | de första n tecknen | `LEFT$("KOTTE", 3)` | `KOT` |
| `LEN(s$)` | antal tecken | `LEN("KOTTE")` | `5` |
| `LOWER$(s$)` | små bokstäver | `LOWER$("HEJ")` | `hej` |
| `MID$(s$, start [, n])` | n tecken från position start (från 1), eller resten | `MID$("KOTTE", 2, 3)` | `OTT` |
| `REPEAT$(s$, n)` | texten n gånger | `REPEAT$("=", 5)` | `=====` |
| `REPLACE$(s$, gammal$, ny$)` | byter ut all gammal$ mot ny$ | `REPLACE$("katt", "k", "h")` | `hatt` |
| `RIGHT$(s$, n)` | de sista n tecknen | `RIGHT$("KOTTE", 2)` | `TE` |
| `STR$(x)` | talet som text | `STR$(42)` | `42` |
| `TRIM$(s$)` | utan mellanslag först och sist | `TRIM$("  hej  ")` | `hej` |
| `UPPER$(s$)` | stora bokstäver | `UPPER$("hej")` | `HEJ` |
| `VAL(s$)` | texten som tal, 0 om den inte är ett tal | `VAL("3.5")` | `3.5` |
| `WORD$(s$, n)` | ord nummer n (från 1), åtskilda av mellanslag | `WORD$("en två tre", 2)` | `två` |

## Arrayer

| Funktion | Ger |
|---|---|
| `DIM(namn)` | antal platser i arrayen |
| `JOIN$(namn, skiljetecken$)` | alla platser ihopsatta till en text |
| `SPLIT$(s$, skiljetecken$)` | en textarray med delarna; tomt skiljetecken delar i enskilda tecken |

```basic
A$ = SPLIT$("a,b,c", ",")
PRINT DIM(A$)
PRINT JOIN$(A$, "-")
```

## Tangentbord och handkontroll

| Funktion | Ger |
|---|---|
| `INKEY$` | namnet på en nedtryckt tangent, eller tom text |
| `KEYSTATE("tangent")` | 1 om tangenten hålls nere, annars 0 |
| `JOYSTATE(kontroll, "knapp")` | 1 om knappen hålls nere; kontrollerna räknas från 1 |
| `JOYAXIS(kontroll, "axel")` | spakens läge, –100 till 100 (avtryckarna 0 till 100) |

Namnen på tangenter, knappar och axlar finns i kapitlet *Tabeller*.

## Sprites

Kollisionerna ger 1 eller 0. Gruppvarianterna ger numret på den första spriten i gruppen som träffas, eller 0.

| Funktion | Ger |
|---|---|
| `HIT(a, b)` | om synliga pixlar i sprite a och b överlappar |
| `HITBOX(a, b)` | om spritarnas rektanglar överlappar |
| `HITCIRCLE(a, b)` | om spritarnas cirklar överlappar |
| `HITGROUP(nr, "grupp")` | pixeltest mot en grupp |
| `HITBOXGROUP(nr, "grupp")` | rektangeltest mot en grupp |
| `HITCIRCLEGROUP(nr, "grupp")` | cirkeltest mot en grupp |
| `FADING(nr)` | 1 medan en `SPRITE FADE` pågår |
| `LAYERFADING(lager)` | 1 medan en `SCREEN FADE` pågår |
| `SPRITE#X(nr)`, `SPRITE#Y(nr)` | spritens position |
| `SPRITE#WIDTH(nr)`, `SPRITE#HEIGHT(nr)` | spritens storlek i punkter, utan zoom |
| `SPRITE#FRAME(nr)` | bilden som visas, från 0 |
| `SPRITE#FRAMECOUNT(nr)` | antal bilder |
| `SPRITE#ZOOMX(nr)`, `SPRITE#ZOOMY(nr)` | zoomen, där 1 är normal storlek |
| `SPRITE#ANGLE(nr)` | vridningen i grader |
| `SPRITE#VISIBLE(nr)` | 1 om spriten är på |

## Banor

| Funktion | Ger |
|---|---|
| `TILE(lager, x, y)` | tile-numret på punkten x, y på kartan, –1 om rutan är tom eller utanför kartan |

`x` och `y` är punkter, inte rutor: punkten delas med 32 för att hitta rutan. Lagret anges men används inte, eftersom det bara finns en karta.

## Ljud

| Funktion | Ger |
|---|---|
| `PLAYING(kanal)` | 1 om syntens kanal spelar (kanal 1 – 8) |
| `ANYPLAYING()` | 1 om någon kanal spelar |
| `TRACKPLAYING()` | 1 medan en trackerlåt spelar |
| `TRACKPOS()` | platsen i trackerlåtens ordningslista, från 0 |
| `TRACKROW()` | raden i trackerlåtens mönster, från 0 |

## Filer

| Funktion | Ger |
|---|---|
| `EOF(kanal)` | 1 när hela filen är läst, eller om kanalen inte är öppen |

## Tid

| Funktion | Ger |
|---|---|
| `MILLIS()` | millisekunder sedan programmet startade |
| `MICROS()` | mikrosekunder sedan programmet startade |
| `TICKS` | samma som `MILLIS()` |

## Variabler som sätts av systemet

| Variabel | Innehåller |
|---|---|
| `ERR$` | felmeddelandet efter ett fel, när `ON ERROR GOTO` är på |
| `TIMER` | klockan i millisekunder, efter kommandot `TIMER` |
| `TIME_namn` | tiden i millisekunder efter `PROFILE END "namn"` |
