# Sprites

En **sprite** är en figur som kan flyttas fritt över skärmen utan att du behöver sudda och rita om bakgrunden. Spelaren, fienderna, skotten och mynten i ett spel är nästan alltid sprites. De ritas av grafikkortet, så du kan ha hundratals samtidigt.

Varje sprite har ett nummer som du själv väljer. Numret avgör också ordningen: sprites med högre nummer ritas ovanpå dem med lägre.

## Ladda och visa en sprite

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/gem.png"
SPRITE POS 1, 300, 200
SPRITE ON 1
```

`SPRITE LOAD` laddar en bild till sprite 1, `SPRITE POS` sätter positionen (spritens övre vänstra hörn) och `SPRITE ON` gör den synlig. `SPRITE OFF` döljer den igen.

> **Tips:** Svarta pixlar (`0,0,0`) i bilden blir genomskinliga, liksom genomskinliga pixlar i PNG-filer.

## Flytta en sprite

Precis som med bollen i grafikkapitlet ändrar du positionen lite i varje bild:

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/bagge.png", 32, 32, 3
SPRITE ON 1
X = 0
Loop:
  X = X + 2
  IF X > 640 THEN X = -32
  SPRITE POS 1, X, 300
  WAIT VBL
GOTO Loop
```

`SPRITE MOVE nr, dx, dy` flyttar en sprite ett stycke från där den är.

## Animation: bildark

En animerad figur består av flera bilder efter varandra, ofta lagda bredvid varandra i en enda fil, ett så kallat **bildark** (*sprite sheet*). Ange bredd, höjd och antal bilder när du laddar:

```basic nocheck
SPRITE LOAD 1, "Graphics/Skogshopp/kotte.png", 32, 32, 6
```

`kotte.png` innehåller sex bilder på 32 × 32 punkter: stå, tre gångbilder, hopp och skadad. Med `SPRITE FRAME nr, bild` väljer du vilken som visas (bilderna räknas från 0):

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/kotte.png", 32, 32, 6
SPRITE POS 1, 300, 220
SPRITE ZOOM 1, 300
SPRITE ON 1
T = 0
Loop:
  T = T + 1
  SPRITE FRAME 1, 1 + INT(T / 8) MOD 3
  WAIT VBL
GOTO Loop
```

`INT(T / 8) MOD 3` räknar 0, 1, 2, 0, 1, 2 ... och byter bild var åttonde skärmbild. `1 +` gör att det blir gångbilderna 1, 2 och 3.

> **Tips:** Innehåller filnamnet storleken, som `Anim_Beast_W32_H56_B11.png` (bredd 32, höjd 56, 11 bilder), räcker `SPRITE LOAD 1, "Anim_Beast_W32_H56_B11.png"`.

### ANIM: animation som sköter sig själv

Med `ANIM` beskriver du en animation en gång, och sedan sköts den automatiskt vid varje `WAIT VBL`. Sekvensen skrivs som `(bild, tid)` där tiden är antal skärmbilder:

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/kotte.png", 32, 32, 6
SPRITE POS 1, 300, 220
SPRITE ZOOM 1, 300
SPRITE ON 1
ANIM DEF 1, "stå", (0,30), LOOP
ANIM DEF 1, "gå", (1,8)(2,8)(3,8)(2,8), LOOP
ANIM DEF 1, "hopp", (4,20), ONCE, "stå"
ANIM SET 1, "gå"
T = 0
Loop:
  T = T + 1
  IF T = 180 THEN ANIM SET 1, "hopp"
  WAIT VBL
GOTO Loop
```

`LOOP` betyder att animationen börjar om, `ONCE` att den körs en gång. Efter `ONCE` kan du ange vilken animation som ska ta vid när den är klar, här `"stå"`. `ANIM STOP nr` stoppar animationen.

## Storlek, vridning och spegling

| Kommando | Gör |
|---|---|
| `SPRITE ZOOM nr, procent` | ändrar storleken, 100 = normal, 200 = dubbelt så stor |
| `SPRITE ZOOM nr, x, y` | olika i bredd och höjd |
| `SPRITE ROTATE nr, grader` | vrider spriten runt sin mittpunkt |
| `SPRITE ALPHA nr, värde` | genomskinlighet, 0 – 255 |
| `SPRITE FADE nr, mål, bilder` | tonar mjukt till målvärdet |

En negativ zoom speglar spriten. Det är så man gör en figur som kan gå åt båda hållen med samma bilder:

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/kotte.png", 32, 32, 6
SPRITE ZOOM 1, 200
SPRITE ON 1
X = 100 : DX = 3
Loop:
  X = X + DX
  IF X > 560 THEN DX = -3 : SPRITE ZOOM 1, -200, 200
  IF X < 80 THEN DX = 3 : SPRITE ZOOM 1, 200, 200
  SPRITE POS 1, X, 300
  WAIT VBL
GOTO Loop
```

## Bildbanker

Ska många sprites använda samma bilder, till exempel tjugo mynt, är det slöseri att ladda filen tjugo gånger. Ladda den i stället en gång till en **bildbank** med `IMAGE LOAD`, och låt sprites hämta bilder därifrån med `SPRITE IMAGE nr, bank, bild`:

```basic
IMAGE LOAD 1, "Graphics/Skogshopp/gem.png", 20, 20, 4
FOR I = 0 TO 9
  SPRITE IMAGE 10 + I, 1, 0
  SPRITE POS 10 + I, 60 + I * 55, 240
  SPRITE ON 10 + I
NEXT I
T = 0
Loop:
  T = T + 1
  IF T MOD 6 = 0 THEN
    FOR I = 0 TO 9
      SPRITE IMAGE 10 + I, 1, (T / 6) MOD 4
    NEXT I
  ENDIF
  WAIT VBL
GOTO Loop
```

Tio ädelstenar som snurrar i takt. En sprite som använder en bildbank byter bild med `SPRITE IMAGE`, inte med `SPRITE FRAME`.

## Sprites i lager

En sprite ritas i lager 0 om du inte säger något annat. `SPRITE LAYER nr, lager` flyttar den till ett annat lager. Spritens position räknas då i det lagrets koordinater, så om lagret scrollas följer spriten med. I ett plattformsspel lägger man därför fiender och mynt i samma lager som banan: de stannar på sina platser på banan medan den scrollar förbi.

## Kollisioner

För att veta om spelaren nuddar en fiende eller ett mynt finns kollisionsfunktioner. De ger 1 om spritarna överlappar och 0 annars:

| Funktion | Testar |
|---|---|
| `HITBOX(a, b)` | om spritarnas rektanglar överlappar (snabbast) |
| `HITCIRCLE(a, b)` | om två cirklar överlappar (bra för runda saker) |
| `HIT(a, b)` | om några synliga pixlar överlappar (exaktast) |

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/kotte.png", 32, 32, 6
SPRITE LOAD 2, "Graphics/Skogshopp/gem.png", 20, 20, 4
SPRITE POS 2, 400, 300
SPRITE ON 1 : SPRITE ON 2
X = 100
Loop:
  X = X + 2
  SPRITE POS 1, X, 296
  IF HITBOX(1, 2) = 1 THEN SPRITE OFF 2 : PLAY COIN
  WAIT VBL
GOTO Loop
```

### Grupper

Har du många fiender är det bekvämt att samla dem i en **grupp**. `HITGROUP(nr, "grupp")` ger numret på den första spriten i gruppen som `nr` krockar med, eller 0:

```basic nocheck
FOR I = 10 TO 19
  SPRITE GROUP I, "fiender"
NEXT I
TRAFF = HITBOXGROUP(1, "fiender")
IF TRAFF > 0 THEN SPRITE OFF TRAFF
```

`HITBOXGROUP` och `HITCIRCLEGROUP` fungerar på samma sätt med rektangel- och cirkeltest. `SPRITE UNGROUP nr, "grupp"` tar bort en sprite ur en grupp.

## Effekter

Sprites kan få effekter som räknas ut av grafikkortet:

| Kommando | Effekt |
|---|---|
| `SPRITE EFFECT ADD nr, BLUR, radie` | gör spriten suddig |
| `SPRITE EFFECT ADD nr, WAVE, ampX, frekX, ampY, frekY` | får spriten att bölja |
| `SPRITE EFFECT ADD nr, COLOR, ljus, kontrast, mättnad` | ändrar färgerna (1 = oförändrat) |
| `SPRITE EFFECT CLEAR nr` | tar bort alla effekter |

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/bagge.png", 32, 32, 3
SPRITE POS 1, 300, 220
SPRITE ZOOM 1, 400
SPRITE ON 1
SPRITE EFFECT ADD 1, WAVE, 6, 0.3, 0, 0
Loop:
  WAIT VBL
GOTO Loop
```

Vågen rör sig av sig själv. Med `SPRITE EFFECT UPDATE nr, index, parameter, värde` ändrar du en effekt medan programmet kör, till exempel `SPRITE EFFECT UPDATE 1, 0, AMPLITUDEX, 10`. Effekterna numreras från 0 i den ordning de lades till.

> **Prova själv:** Gör en sprite som du styr med piltangenterna (se kapitlet om inmatning) och som speglas åt det håll den går.

> **Prova själv:** Strö ut tio ädelstenar på slumpmässiga platser och låt spelaren plocka upp dem. Räkna hur många som är kvar.
