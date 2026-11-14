# Tiles och banor

En spelbana i ett plattformsspel är ofta tusentals punkter bred. Att rita den som en enda bild vore opraktiskt. I stället byggs den av **tiles**: små fyrkantiga bitar, som gräs, sten och lådor, som läggs ut i ett rutnät. Rutnätet kallas en **karta** (*map*).

Ett tileset är en bild med alla bitar bredvid varandra, precis som en font. Bitarna numreras från 0, radvis från vänster.

## Bygga en bana

```basic
SCREEN SELECT 1
TILE MAP 40, 15
TILE LOAD "Graphics/Skogshopp/tiles.png", 32, 32
FOR X = 0 TO 39
  TILE SET X, 13, 0
  TILE SET X, 14, 1
NEXT X
TILE SET 10, 9, 3
TILE SET 11, 9, 2
TILE SET 12, 9, 3
TILE DRAW 0, 0
```

| Kommando | Gör |
|---|---|
| `TILE MAP bredd, höjd` | skapar en tom karta, mätt i rutor, i det valda lagret |
| `TILE LOAD "fil", bredd, höjd` | laddar tilesetet med rutornas storlek i punkter |
| `TILE SET x, y, nummer` | lägger tile `nummer` på ruta x, y (–1 = tomt) |
| `TILE DRAW x, y` | ritar ut hela kartan i lagret |

`TILE MAP` gör lagret lika stort som hela kartan, här 40 × 32 = 1280 punkter brett. Det är bredare än skärmen, så du scrollar lagret för att se resten:

```basic nocheck
SCROLL 1, KAMERA_X, 0
```

> **Obs:** Använd tiles på 32 × 32 punkter. Funktionen `TILE()` nedan räknar alltid med den storleken.

`TILE DRAW` ritar bara de rutor som har en tile och skriver inte över rutor som är tomma. Byter du en tile mot en annan, som när ett bonusblock blir tomt i Skogshopp, sätter du den nya och ritar om: `TILE SET X, Y, 4` och sedan `TILE DRAW 0, 0`.

## Vad finns på en plats?

`TILE(lager, x, y)` ger numret på den tile som finns på punkten x, y i banan, eller –1 om rutan är tom. Det är grunden för kollisioner i ett plattformsspel: står det en sten under fötterna, ska figuren inte falla.

```basic nocheck
IF TILE(1, SPELARE_X + 16, SPELARE_Y + 32) >= 0 THEN STAR_PA_MARKEN = 1
```

(Det första värdet, lagret, används inte än men måste finnas med.)

## Karteditorn

Knappen **MAP** i utvecklingsmiljön öppnar karteditorn, där du ritar banan med musen i stället för med `TILE SET`. Ladda ett tileset med **Load Tileset...**, välj tiles i paletten och klicka ut dem på kartan. Spara med **Save Map...**. Banan laddas sedan i ditt program med `MAP LOAD`:

```basic nocheck
SCREEN SELECT 1
TILE LOAD "Graphics/tileset.png", 32, 32
MAP LOAD "Graphics/test.amosmap"
TILE DRAW 0, 0
```

## Banor i DATA

Ett annat sätt, som Skogshopp använder, är att rita banan med bokstäver i `DATA`-rader. Varje bokstav står för en tile:

```basic
SCREEN SELECT 1
TILE MAP 20, 5
TILE LOAD "Graphics/Skogshopp/tiles.png", 32, 32
TECKEN$ = "GDBQ"
RESTORE Bana
FOR Y = 0 TO 4
  READ RAD$
  FOR X = 0 TO 19
    TILE SET X, Y, INSTR(TECKEN$, MID$(RAD$, X + 1, 1)) - 1
  NEXT X
NEXT Y
TILE DRAW 0, 0
END

Bana:
DATA "...................."
DATA "......QBQ..........."
DATA "...................."
DATA "GGGGGGGGGG...GGGGGGG"
DATA "DDDDDDDDDD...DDDDDDD"
```

`INSTR` ger positionen för tecknet i `TECKEN$`, och `- 1` gör om den till tile-nummer: `G` blir 0 (gräs), `D` 1 (jord), `B` 2 (sten), `Q` 3 (bonusblock), och `.` som inte finns blir –1, alltså tomt. Det gör det lätt att se och ändra banan direkt i koden.

> **Prova själv:** Lägg till en rad med `B` som ett tak högst upp och en grop mitt i banan.
