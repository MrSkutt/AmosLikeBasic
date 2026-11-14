# Rita grafik

Nu börjar det roliga. Spelfönstret är en rityta där du kan rita punkter, linjer, rektanglar och cirklar i alla färger.

## Koordinater

Skärmen är som standard 640 punkter bred och 480 punkter hög. Varje punkt har en position som anges med två tal: **x** (hur långt åt höger) och **y** (hur långt ned). Övre vänstra hörnet är `0, 0`, och nedre högra är `639, 479`.

```text
(0,0) ──────────────────────► x   (639,0)
  │
  │        (320,240) = mitten
  │
  ▼
  y
(0,479)                           (639,479)
```

Observera att y växer *nedåt*, tvärtom mot matematiklektionerna.

Vill du ha en annan storlek på skärmen anger du den med `SCREEN`:

```basic
SCREEN 320, 240
```

En mindre skärm ger större, mer "retro" pixlar, eftersom bilden skalas upp till fönstret.

## Färg med INKG

Allt du ritar får färgen du har valt med `INKG` (*ink graphics*). En färg kan anges på flera sätt:

```basic
INKG 2                  ; ett nummer 0 - 15 i standardpaletten
INKG "Red"              ; ett färgnamn på engelska
INKG "#FF8800"          ; röd, grön och blå i hexadecimal form
INKG "255,136,0"        ; röd, grön och blå som tal 0 - 255
```

Standardpaletten har 16 färger, ungefär som på en Commodore 64:

| Nr | Färg | Nr | Färg |
|---|---|---|---|
| 0 | svart | 8 | orange |
| 1 | vit | 9 | brun |
| 2 | röd | 10 | ljusröd |
| 3 | cyan | 11 | mörkgrå |
| 4 | lila | 12 | grå |
| 5 | grön | 13 | ljusgrön |
| 6 | blå | 14 | ljusblå |
| 7 | gul | 15 | ljusgrå |

Färgnamnen är de vanliga på engelska: `"Red"`, `"Green"`, `"Blue"`, `"Yellow"`, `"White"`, `"Orange"`, `"Pink"`, `"Purple"`, `"Gray"`, `"SkyBlue"` och många fler.

> **Obs:** Helt svart (`0,0,0`) räknas som genomskinligt när du ritar med `PLOT` och i bilder som laddas in. Vill du ha svart, använd en nästan svart färg, till exempel `"#101010"`.

## Punkter och linjer

`PLOT` ritar en punkt, `LINE` en linje mellan två punkter:

```basic
INKG "Yellow"
PLOT 320, 240
LINE 0, 0, 639, 479
INKG "Cyan"
LINE 0, 479, 639, 0
```

Med en loop kan några få rader rita mycket:

```basic
FOR I = 0 TO 640 STEP 16
  INKG I MOD 16
  LINE I, 0, 640 - I, 479
NEXT I
```

## Rektanglar och cirklar

| Kommando | Ritar |
|---|---|
| `BOX x1, y1, x2, y2` | rektangel (bara kanten) |
| `BAR x1, y1, x2, y2` | fylld rektangel |
| `CIRCLE x, y, r` | cirkel med mittpunkt x, y och radie r |
| `CIRCLEF x, y, rx, ry` | fylld ellips (lika rx och ry ger en fylld cirkel) |
| `ELLIPSE x, y, rx, ry` | ellips (bara kanten) |
| `FILL x, y` | fyller området runt punkten med färg, som en färghink |

```basic
INKG "SkyBlue"
BAR 0, 0, 639, 300
INKG "ForestGreen"
BAR 0, 300, 639, 479
INKG "Yellow"
CIRCLEF 500, 90, 50, 50
INKG "White"
CIRCLEF 150, 100, 60, 30
CIRCLEF 200, 90, 50, 30
INKG "SaddleBrown"
BAR 280, 220, 400, 320
INKG "DarkRed"
LINE 270, 220, 340, 160
LINE 340, 160, 410, 220
LINE 270, 220, 410, 220
FILL 340, 200
```

Det ritar ett landskap med himmel, gräs, sol, ett moln och ett hus med tak.

## Text på grafikskärmen

`PRINT` skriver i textkonsolen, som ligger ovanpå allt. För att skriva text *i* grafiken finns två kommandon:

- `TEXT x, y, "text"` skriver texten med övre vänstra hörnet i punkten x, y, i INK-färgen.
- `PRINTG "text"` skriver på nästa rad, som `PRINT` gör. Med `LOCATEG kolumn, rad` väljer du var. Här räknas kolumner och rader från 0, och en ruta är 8 × 16 punkter.

```basic
INKG "White"
TEXT 240, 220, "AMOS LIKE BASIC"
LOCATEG 2, 2
PRINTG "Kolumn 2, rad 2"
```

Vill du skriva snygg text i spelstil, med egna bokstäver som kan vågas och roteras, använd `FONT`, som beskrivs i ett eget kapitel.

## Sudda

`CLSG` suddar allt som är ritat på skärmen. Det är genomskinligt efteråt, inte svart.

## Rörelse: WAIT VBL

För att få något att röra sig ritar man om bilden gång på gång, lite förflyttad varje gång. Skärmen ritas om 60 gånger i sekunden, och `WAIT VBL` väntar tills det är dags för nästa bild. (*VBL* står för *vertical blank*, en kort paus när bildskärmen förr i tiden ritade om bilden.)

En studsande boll:

```basic
X = 100 : Y = 100
DX = 4 : DY = 3
INKG "Orange"
Loop:
  CLSG
  CIRCLEF X, Y, 20, 20
  X = X + DX
  Y = Y + DY
  IF X < 20 OR X > 620 THEN DX = -DX
  IF Y < 20 OR Y > 460 THEN DY = -DY
  WAIT VBL
GOTO Loop
```

Varje varv i loopen suddar skärmen, ritar bollen, flyttar den och väntar på nästa bild. När bollen når en kant vänds riktningen. Det är grundtricket i alla spel!

> **Tips:** Att sudda och rita om hela skärmen fungerar för enkla saker, men för figurer som rör sig finns **sprites**, som ritas av grafikkortet och inte behöver suddas. De kommer i nästa kapitel men ett.

> **Prova själv:** Ändra bollen så att den byter färg varje gång den studsar. Tips: `INKG INT(RND(15)) + 1`.

> **Prova själv:** Rita ett schackbräde med två nästlade `FOR`-loopar och `BAR`.
