# Skärmar, lager och scroll

En av de stora styrkorna med AMOS var att man kunde ha flera skärmar ovanpå varandra. AMOS Like Basic har samma idé: spelfönstret består av **lager**, som genomskinliga plastfilmer ovanpå varandra. Det som är genomskinligt i ett lager släpper igenom lagren under.

Med lager kan du till exempel ha en himmel längst bak, berg framför den, en spelbana framför bergen och poängräkningen överst, och scrolla varje lager för sig.

## Välja lager

Lagren är numrerade från 0. Lager 0 ligger längst bak, och ju högre nummer, desto längre fram. `SCREEN SELECT` väljer vilket lager du ritar i, och skapar det om det inte finns:

```basic
SCREEN SELECT 0
INKG "SkyBlue"
BAR 0, 0, 639, 479

SCREEN SELECT 1
INKG "ForestGreen"
CIRCLEF 320, 480, 400, 150

SCREEN SELECT 2
INKG "White"
TEXT 260, 40, "TRE LAGER"
```

Vill du rita en enda sak i ett annat lager utan att byta lager kan du skriva `#nummer` först bland argumenten:

```basic
SCREEN SELECT 0
BAR #3, 10, 10, 100, 50
```

Det fungerar för de flesta ritkommandon: `PLOT`, `LINE`, `BOX`, `BAR`, `CIRCLE`, `CIRCLEF`, `ELLIPSE`, `FILL`, `TEXT`, `PRINTG`, `LOAD`, `FONT PRINT`, `SPRITE` och `RASTER`.

## Bilder som bakgrund

`LOAD` laddar en bild (PNG eller JPG) till det valda lagret, med bildens övre vänstra hörn i skärmens övre vänstra hörn:

```basic nocheck
LOAD "Graphics/Layer/Background.png"
LOAD #1, "Graphics/berg.png"
```

Filnamnet räknas från projektmappen. Bilden skalas inte, så gör den lika stor som skärmen.

## Scrolla

`SCROLL lager, x, y` förskjuter hela innehållet i ett lager. Det som försvinner ut på ena sidan kommer in på den andra, så ett lager som ritats för att passa ihop i kanterna kan scrollas i all oändlighet.

Om lagren längst bak scrollas långsamt och de främre snabbt ser det ut som djup, så kallad **parallax**. Här ritas tre lager med berg, kullar och träd, och scrollas i olika fart:

```basic
SCREEN SELECT 0
INKG "#7D86C9"
FOR X = 0 TO 638 STEP 2
  H = SIN(X * 0.5625) * 40 + 200
  BAR X, 480 - H, X + 1, 479
NEXT X

SCREEN SELECT 1
INKG "#4E9A5C"
FOR X = 0 TO 638 STEP 2
  H = SIN(X * 1.125 + 60) * 25 + 120
  BAR X, 480 - H, X + 1, 479
NEXT X

SCREEN SELECT 2
INKG "#245C33"
FOR T = 0 TO 7
  CIRCLEF T * 80 + 40, 400, 30, 45
NEXT T

S = 0
Loop:
  S = S + 2
  SCROLL 0, S / 4, 0
  SCROLL 1, S / 2, 0
  SCROLL 2, S, 0
  WAIT VBL
GOTO Loop
```

Lägg märke till talen i `SIN`. En hel sinusvåg är 360 grader, och skärmen är 640 punkter bred. `X * 0.5625` blir exakt 360 när `X` är 640, så vågen passar ihop när lagret går runt. Samma sak med `X * 1.125`, som blir två hela vågor.

## Göra ett lager genomskinligt

`SCREEN ALPHA lager, värde` gör ett helt lager mer eller mindre genomskinligt, från 0 (osynligt) till 255 (helt synligt). `SCREEN FADE lager, mål, bilder` tonar lagret mjukt till målvärdet under ett antal bilder. Funktionen `LAYERFADING(lager)` är 1 så länge toningen pågår:

```basic
INKG "Orange"
CIRCLEF 320, 240, 150, 150
SCREEN FADE 0, 0, 120
WHILE LAYERFADING(0) = 1
  WAIT VBL
WEND
SCREEN FADE 0, 255, 60
```

Det tonar ut cirkeln under två sekunder (120 bilder) och tillbaka in under en.

`SCREEN OFF lager` döljer ett lager helt och `SCREEN ON lager` visar det igen.

## Raster bars

På Amiga gjorde man färgglada band över skärmen genom att byta bakgrundsfärg för varje rad medan bilden ritades, så kallade **raster bars** eller **copper bars**. I AMOS Like Basic gör du dem med `RASTER`:

```text
RASTER nummer, x, y, höjd, "färg1, färg2, ..."
```

Bandet får ett nummer (1 – 100) så att du kan flytta det senare. Det börjar på rad `y`, är `höjd` punkter högt och tonar mjukt mellan färgerna du anger. (`x` används inte än men måste finnas med.)

```basic
RASTER 1, 0, 0, 480, "#1D4AA0, #3F7FDC, #8CC6FF, #FFE6BF"
```

Det ger en himmel som tonar från mörkblått till ljust. Raster bars ligger *bakom* det som är ritat i lagret, så du kan rita berg och moln ovanpå.

Banden kan flyttas med `RASTER MOVE nummer, y`. Här studsar tre band upp och ned:

```basic
RASTER 1, 0, 0, 40, "Red, Yellow, Red"
RASTER 2, 0, 0, 40, "Blue, Cyan, Blue"
RASTER 3, 0, 0, 40, "Green, Lime, Green"
V = 0
Loop:
  V = V + 3
  RASTER MOVE 1, 220 + SIN(V) * 200
  RASTER MOVE 2, 220 + SIN(V + 40) * 200
  RASTER MOVE 3, 220 + SIN(V + 80) * 200
  WAIT VBL
GOTO Loop
```

| Kommando | Gör |
|---|---|
| `RASTER MOVE nr, y` | flyttar bandet |
| `RASTER WRAP nr, 1` | bandet fortsätter överst när det åker ut nederst (0 stänger av) |
| `RASTER DEL nr` | tar bort bandet |
| `RASTER CLEAR` | tar bort alla band i lagret |
| `RASTER GFXMODE lager, 1` | banden färgar det som är *ritat* i stället för bakgrunden |
| `RASTER SPACE lager, läge` | byter hela lagrets bakgrund: 0 = vanlig, 1 = rymd med stjärnor, 2 = moln |

Ett lager kan ha 24 färgövergångar sammanlagt. Ett band med tre färger använder två övergångar. `RASTER INFO` skriver ut hur många som används.

## Snö, regn och stjärnor

`PARTICLE typ, täthet` lägger till väder i det valda lagret: typ 1 är snö, 2 regn och 3 blinkande stjärnor, och 0 stänger av. Tätheten styr hur stora partiklarna och mellanrummen blir:

```basic
RASTER 1, 0, 0, 480, "#0B1A4D, #2A3F8F"
PARTICLE 1, 20
Loop:
  WAIT VBL
GOTO Loop
```

Vädret ritas av grafikkortet, så det kostar ingenting för din BASIC-kod.

> **Prova själv:** Gör en himmel med raster bars i lager 0, rita en månskära i lager 1 och lägg snö i lager 2.

> **Prova själv:** Bygg vidare på parallaxexemplet så att lagren scrollas åt vänster eller höger beroende på om du håller in vänster- eller högerpil. Tangenterna läser du med `KEYSTATE("Left")`, se kapitlet om inmatning.
