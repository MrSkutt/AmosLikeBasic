# Kommandon

Alla kommandon i bokstavsordning. Kommandon som hör ihop, som `SPRITE POS` och `SPRITE ZOOM`, står under sitt första ord.

## A

### ANIM

Animerar en sprite automatiskt. Animationen stegas fram vid varje `WAIT VBL`.

```text
ANIM DEF sprite, "namn", (bild, tid)(bild, tid)..., LOOP
ANIM DEF sprite, "namn", (bild, tid)(bild, tid)..., ONCE [, "nästa"]
ANIM SET sprite, "namn"
ANIM STOP sprite
ANIM GROUP "grupp", sprite, sprite, ...
ANIM GROUP SET "grupp", "namn"
```

`ANIM DEF` beskriver en animation. Varje `(bild, tid)` är en bild i spritens bildark (från 0) och hur många skärmbilder den ska visas. `LOOP` börjar om när animationen är slut, `ONCE` stannar på sista bilden eller går vidare till animationen `"nästa"`. `ANIM SET` startar en animation och `ANIM STOP` stoppar den. `ANIM GROUP` samlar flera sprites, och `ANIM GROUP SET` startar samma animation på alla i gruppen.

```basic nocheck
ANIM DEF 1, "gå", (1,8)(2,8)(3,8), LOOP
ANIM DEF 1, "hopp", (4,20), ONCE, "gå"
ANIM SET 1, "gå"
```

## B

### BAR

```text
BAR [#lager,] x1, y1, x2, y2
```

Ritar en fylld rektangel med hörnen x1, y1 och x2, y2 i `INKG`-färgen.

### BOX

```text
BOX [#lager,] x1, y1, x2, y2
```

Ritar kanten av en rektangel.

## C

### CIRCLE

```text
CIRCLE [#lager,] x, y, r
```

Ritar en cirkel med mittpunkt x, y och radie r.

### CIRCLEF

```text
CIRCLEF [#lager,] x, y, rx, ry
```

Ritar en fylld ellips med mittpunkt x, y och radierna rx (bredd) och ry (höjd). Lika radier ger en fylld cirkel.

### CLOSE

```text
CLOSE kanal
```

Stänger en fil som öppnats med `OPEN`. Det gör inget om kanalen inte är öppen.

### CLS

Suddar textkonsolen och flyttar markören till övre vänstra hörnet.

### CLSG

```text
CLSG [#lager]
```

Suddar allt som är ritat i lagret (det valda, eller det som anges). Lagret blir genomskinligt, inte svart. Sprites och fonttext påverkas inte.

### CLSGA

Suddar alla lager.

## D

### DATA

```text
DATA värde, värde, ...
```

Värden som läses med `READ`. `DATA`-rader måste stå under en etikett, och förrådet väljs med `RESTORE etikett` innan första `READ`.

```basic
RESTORE Farger
READ A$, B$, C$
PRINT A$ & " " & B$ & " " & C$
END
Farger:
DATA "röd", "grön", "blå"
```

### DEC

```text
DEC variabel
```

Minskar en talvariabel med 1. Samma som `variabel = variabel - 1`.

### DIM

```text
DIM namn(storlek)
DIM namn(storlek, storlek, ...)
DIM namn(storlek) = [värde, värde, ...]
```

Skapar en array. `DIM A(10)` ger tio platser, `A(0)` till `A(9)`. Textarrayer får namn som slutar på `$`. Med `= [...]` får arrayen startvärden; för flera dimensioner skrivs de i nästlade hakparenteser.

### DO ... LOOP

```text
DO
  kommandon
LOOP
```

Upprepar för evigt. Lämnas med `EXIT DO`.

## E

### ELLIPSE

```text
ELLIPSE [#lager,] x, y, rx, ry
```

Ritar kanten av en ellips.

### END

Avslutar programmet. `END` följt av ett annat ord avslutar ett block: `END IF`, `END PROC`, `END FUNCTION` och `END SELECT`.

### EXIT

```text
EXIT DO
EXIT WHILE
EXIT REPEAT
EXIT PROC
```

Lämnar loopen eller proceduren direkt och fortsätter efter den. En `FOR`-loop lämnar du med `GOTO` till en etikett efter `NEXT`.

## F

### FILL

```text
FILL [#lager,] x, y
```

Fyller området runt punkten x, y med `INKG`-färgen, som en färghink. Fyllningen stannar vid pixlar med en annan färg.

### FONT

Bitmapsfonter: en bild med alla tecken i ett rutnät. Fonttexten ritas ovanpå lagret och ligger kvar tills `FONT CLEAR`.

```text
FONT LOAD font, "fil", bredd, höjd
FONT MAP font, "tecken"
FONT PRINT [#lager,] font, x, y, text
FONT CHAR font, x, y, tecken
FONT ZOOM font, procent [, procentY]
FONT ROTATE font, grader
FONT CLEAR
FONT EFFECT ADD font, WAVE, ampX, frekX, ampY, frekY
FONT EFFECT ADD font, BLUR, radie
FONT EFFECT ADD font, COLOR, ljus, kontrast, mättnad
FONT EFFECT UPDATE font, index, parameter, värde
FONT EFFECT CLEAR font
FONT SET bredd, höjd, "systemfont"
FONT STYLE "stil"
```

| Kommando | Gör |
|---|---|
| `FONT LOAD` | laddar fontbilden; bredd och höjd är storleken på en ruta |
| `FONT MAP` | talar om vilka tecken rutorna innehåller, i ordning från vänster till höger och uppifrån och ned |
| `FONT PRINT` | skriver text med övre vänstra hörnet i x, y |
| `FONT CHAR` | skriver ett enda tecken |
| `FONT ZOOM` | storlek, 100 = normal |
| `FONT ROTATE` | vrider texten |
| `FONT CLEAR` | tar bort all fonttext (men inte effekterna) |
| `FONT EFFECT` | effekter, se `SPRITE EFFECT` för parametrarna |
| `FONT SET` | väljer storlek och systemfont för `TEXT` och `PRINTG` |
| `FONT STYLE` | `"Bold"`, `"Italic"`, `"Bold Italic"` eller `"Normal"` för `TEXT` och `PRINTG` |

### FOR ... NEXT

```text
FOR variabel = start TO slut [STEP steg]
  kommandon
NEXT [variabel]
```

Räknar variabeln från start till slut och kör kommandona för varje värde. `STEP` anger hur mycket variabeln ändras varje varv, 1 om inget anges. Ett negativt steg räknar nedåt.

```basic
FOR I = 10 TO 0 STEP -5
  PRINT I
NEXT I
```

### FUNCTION

```text
FUNCTION Namn(parameter, parameter, ...)
  kommandon
  RETURN värde
END FUNCTION
```

Definierar en egen funktion som anropas med `Namn(värde, ...)` i ett uttryck. Bara de enklaste kommandona fungerar i en funktion: tilldelning, `IF`, `FOR`, `WHILE`, `INC`, `DEC` och `RETURN`.

## G

### GOSUB ... RETURN

```text
GOSUB etikett
...
etikett:
  kommandon
RETURN
```

Hoppar till etiketten och fortsätter efter `GOSUB` när det kommer ett `RETURN`.

### GOTO

```text
GOTO etikett
```

Hoppar till etiketten.

## I

### IF

```text
IF villkor THEN kommando [: kommando] [ELSE kommando]

IF villkor THEN
  kommandon
ELSE
  kommandon
END IF
```

Kör kommandona om villkoret är sant. Står inget efter `THEN` är det ett block som slutar med `END IF` (eller `ENDIF`). `ELSE` är frivilligt. Ett tal som villkor räknas som sant om det inte är 0.

### IMAGE LOAD

```text
IMAGE LOAD bank, "fil"
IMAGE LOAD bank, "fil", bredd, höjd, antal
```

Laddar en bild eller ett bildark till en bildbank som många sprites kan dela på, se `SPRITE IMAGE`.

### INC

```text
INC variabel
```

Ökar en talvariabel med 1.

### INK

```text
INK färg
```

Väljer textfärg i textkonsolen.

### INKG

```text
INKG färg
```

Väljer färg för allt som ritas i grafiken: `PLOT`, `LINE`, `BOX`, `BAR`, `CIRCLE`, `CIRCLEF`, `ELLIPSE`, `FILL`, `TEXT` och `PRINTG`.

### INPUT

```text
INPUT ["fråga",] variabel
INPUT #kanal, variabel
```

Skriver frågan i textkonsolen och väntar tills användaren har skrivit något och tryckt på Enter. Med `#kanal` läses i stället nästa rad ur en fil.

### INPUTG

```text
INPUTG ["fråga",] variabel
```

Som `INPUT`, men skriver i grafiken med `TEXT`-fonten och `INKG`-färgen.

## L

### LET

```text
[LET] variabel = värde
```

Ger en variabel ett värde. Ordet `LET` behövs inte.

### LINE

```text
LINE [#lager,] x1, y1, x2, y2
```

Ritar en linje.

### LOAD

```text
LOAD [#lager,] "fil"
```

Laddar en bild (PNG eller JPG) till lagret, med bildens övre vänstra hörn i lagrets övre vänstra hörn.

### LOCATE

```text
LOCATE rad, kolumn
```

Flyttar textkonsolens markör. Rad och kolumn räknas från 1. Konsolen har 30 rader och 80 kolumner.

### LOCATEG

```text
LOCATEG [#lager,] kolumn, rad
```

Flyttar markören för `PRINTG`. Observera ordningen: kolumn först. Räknas från 0, i teckenrutor.

## M

### MAP LOAD

```text
MAP LOAD "fil.amosmap"
```

Laddar en karta som gjorts i karteditorn. Rita sedan ut den med `TILE DRAW`.

### MUSIC

```text
MUSIC PLAY "fil"
MUSIC STOP
```

Spelar en musikmodul (MOD, XM, S3M eller IT) eller stoppar den. För syntens musik, se `PLAY`.

## O

### ON ERROR

```text
ON ERROR GOTO etikett
ON ERROR RESUME NEXT
ON ERROR BREAK
```

Bestämmer vad som händer när ett kommando går fel. `GOTO` hoppar till etiketten, där variabeln `ERR$` innehåller felmeddelandet. `RESUME NEXT` hoppar över kommandot och fortsätter. `BREAK` stoppar programmet med ett felmeddelande, vilket är standard.

### OPEN

```text
OPEN IN kanal, "fil"
OPEN OUT kanal, "fil"
OPEN APPEND kanal, "fil"
```

Öppnar en textfil för att läsa (`IN`), skriva en ny (`OUT`) eller skriva i slutet av en befintlig (`APPEND`). Kanalen är ett nummer som du använder i `PRINT #`, `INPUT #`, `EOF` och `CLOSE`.

## P

### PAPER

```text
PAPER färg
```

Väljer bakgrundsfärg i textkonsolen.

### PAPERG

```text
PAPERG färg
```

Väljer bakgrundsfärg bakom texten från `PRINTG` och `INPUTG`. Standard är genomskinlig.

### PARTICLE

```text
PARTICLE typ, täthet
```

Väder i det valda lagret: typ 1 är snö, 2 regn och 3 blinkande stjärnor. Typ 0 stänger av. Tätheten styr hur stora partiklarna och mellanrummen blir.

### PLAY

Ljudeffekter och musik från den inbyggda synten.

```text
PLAY COIN | JUMP | LASER | HIT | BLIP | POWERUP | EXPLOSION
PLAY BPM takt
PLAY WAVE kanal, "typ"
PLAY VOLUME kanal, volym
PLAY MUSIC kanal, noter
PLAY LOOP kanal, noter
PLAY STOP [kanal]
PLAY ADSR kanal, attack, decay, sustain, release
PLAY GATE kanal, procent
PLAY PULSE kanal, bredd [, djup, fart]
PLAY FILTER kanal, "typ", frekvens, resonans
PLAY FILTERENV kanal, djup [, attack, decay]
PLAY VIBRATO kanal, djup, fart
PLAY GLIDE kanal, millisekunder
PLAY BEND kanal, halvtoner
PLAY ARP kanal, "steg" [, fart]
PLAY RING kanal, källa
PLAY SYNC kanal, källa
```

Synten har åtta kanaler, 1 – 8. Allt utom `MUSIC`, `LOOP` och `STOP` är inställningar som gäller kanalen tills de ändras och hörs direkt, även mitt i en ton.

| Kommando | Gör |
|---|---|
| `PLAY COIN` med flera | spelar en färdig ljudeffekt |
| `PLAY BPM` | tempo i slag per minut |
| `PLAY WAVE` | ljudtyp: `Square`, `Pulse`, `Triangle`, `Sawtooth`, `Sine`, `Noise` eller `Drums` |
| `PLAY VOLUME` | volym 0 – 1 |
| `PLAY MUSIC` | spelar noter på en kanal en gång, se *Tabeller*; noterna kan vara en textvariabel och innehålla effekter inom `{ }` |
| `PLAY LOOP` | som `MUSIC`, men börjar om när noterna är slut |
| `PLAY STOP` | stoppar en kanal, eller all syntmusik |
| `PLAY ADSR` | envelope: attack, decay och release i millisekunder, sustain 0 – 100 (standard 20, 50, 70, 100) |
| `PLAY GATE` | hur stor del av notlängden tonen hålls, 5 – 100 (standard 90) |
| `PLAY PULSE` | pulsbredd i procent för `Pulse` (standard 25), och pulsbreddsmodulering: djup i procent, fart i Hz |
| `PLAY FILTER` | `"LOW"`, `"HIGH"`, `"BAND"` eller `"OFF"`; frekvens 20 – 18 000 Hz, resonans 0 – 100 |
| `PLAY FILTERENV` | filtret öppnas med upp till djup Hz vid varje not; attack och decay i millisekunder (standard 5 och 200); djup 0 stänger av |
| `PLAY VIBRATO` | djup i hundradels halvton, fart i Hz; djup 0 stänger av |
| `PLAY GLIDE` | glidning mellan noter; 0 stänger av |
| `PLAY BEND` | flyttar tonhöjden ett antal halvtoner; 0 är normalt |
| `PLAY ARP` | arpeggio: halvtonssteg som `"0 4 7"`, fart i steg per sekund (standard 50); `""` stänger av |
| `PLAY RING` | ringmodulering med en annan kanal; 0 stänger av |
| `PLAY SYNC` | hård synk mot en annan kanal; 0 stänger av |

Inställningarna nollställs när programmet startar.

```basic nocheck
PLAY BPM 140
PLAY WAVE 1, "Square"
PLAY MUSIC 1, "C5:8 E5 G5 C6:4 {CUT 500; ARP 0 4 7} C5:2"
```

### PLOT

```text
PLOT [#lager,] x, y
```

Ritar en punkt.

### PRINT

```text
PRINT värde
PRINT AT rad, kolumn, värde
PRINT #kanal, värde
```

Skriver ett värde i textkonsolen och går till nästa rad. `PRINT` utan värde skriver en tom rad. `AT` skriver på en viss plats, som `LOCATE`. Med `#kanal` skrivs en rad i en fil.

### PRINTG

```text
PRINTG [#lager,] värde
PRINTG [#lager,] AT kolumn, rad, värde
```

Som `PRINT`, men skriver i grafiken med `TEXT`-fonten och `INKG`-färgen.

### PROC

```text
PROC Namn[(parameter, parameter, ...)]
  kommandon
END PROC
```

Definierar en procedur. Den anropas med sitt namn, och argumenten skrivs efter ett mellanslag: `Namn värde, värde`. Parametrarna gäller bara inne i proceduren; alla andra variabler är gemensamma. `EXIT PROC` lämnar proceduren direkt.

### PROFILE

```text
PROFILE START "namn"
PROFILE END "namn"
```

Mäter hur lång tid koden mellan `START` och `END` tar. Resultatet skrivs i loggen och sparas i variabeln `TIME_namn`, i millisekunder.

## R

### RASTER

Färgband som tonar mellan färger, bakom det som är ritat i lagret.

```text
RASTER [#lager,] nummer, x, y, höjd, "färg, färg, ..."
RASTER MOVE nummer, y
RASTER WRAP nummer, på
RASTER DEL nummer
RASTER CLEAR
RASTER GFXMODE lager, på
RASTER SPACE lager, läge
```

| Kommando | Gör |
|---|---|
| `RASTER nummer, ...` | skapar eller ändrar band 1 – 100 som börjar på rad y; `x` används inte än |
| `RASTER MOVE` | flyttar bandet |
| `RASTER WRAP` | 1: bandet fortsätter överst när det åker ut nederst |
| `RASTER DEL` | tar bort bandet |
| `RASTER CLEAR` | tar bort alla band i lagret |
| `RASTER GFXMODE` | 1: banden färgar det som är ritat i stället för bakgrunden |
| `RASTER SPACE` | lagrets bakgrund: 0 vanlig, 1 rymd med stjärnor, 2 moln |

Ett lager kan ha 24 färgövergångar sammanlagt; ett band med tre färger använder två.

### READ

```text
READ variabel, variabel, ...
```

Läser nästa värden ur `DATA` i det förråd som valts med `RESTORE`.

### REM

Resten av raden är en kommentar.

### REPEAT ... UNTIL

```text
REPEAT
  kommandon
UNTIL villkor
```

Kör kommandona och upprepar tills villkoret är sant. Kommandona körs alltid minst en gång. Lämnas med `EXIT REPEAT`.

### RESTORE

```text
RESTORE etikett
```

Väljer vilket `DATA`-förråd `READ` läser från och börjar om från början av det.

### RETURN

Avslutar en `GOSUB` och hoppar tillbaka, eller avslutar en `FUNCTION` med ett värde: `RETURN värde`.

## S

### SAM

```text
SAM PLAY "fil"
SAM LOOP "fil"
SAM STOP "fil"
```

Spelar en ljudfil (WAV, MP3 eller OGG) en gång eller om och om igen, eller stoppar den. `SAM STOP ""` stoppar alla.

### SCREEN

```text
SCREEN bredd, höjd
SCREEN SELECT lager
SCREEN ON lager
SCREEN OFF lager
SCREEN ALPHA lager, värde
SCREEN FADE lager, mål, bilder
SCREEN CRT på [, linjer [, böjning]]
```

| Kommando | Gör |
|---|---|
| `SCREEN bredd, höjd` | spelets upplösning, normalt 640 × 480; bilden skalas upp till fönstret |
| `SCREEN SELECT` | väljer lagret som ritkommandona ritar i |
| `SCREEN ON` / `OFF` | visar eller döljer ett lager |
| `SCREEN ALPHA` | lagrets genomskinlighet, 0 (osynligt) – 255 |
| `SCREEN FADE` | tonar mjukt till målvärdet under så många skärmbilder |
| `SCREEN CRT` | 1 = bildrörseffekt, 0 = av; linjer och böjning 0 – 100 |

### SCROLL

```text
SCROLL lager, x, y
```

Flyttar hela lagret så att punkten x, y i lagret hamnar i skärmens övre vänstra hörn. Lagret upprepas, så det tar aldrig slut.

### SELECT ... CASE

```text
SELECT värde
  CASE värde
    kommandon
  CASE värde
    kommandon
  DEFAULT
    kommandon
END SELECT
```

Kör kommandona under den `CASE` som har samma värde, eller under `DEFAULT` om ingen passar.

### SPRITE

Figurer som ritas av grafikkortet ovanpå lagret, numrerade av dig. Högre nummer ritas ovanpå lägre.

```text
SPRITE LOAD nr, "fil" [, bredd, höjd, antal]
SPRITE IMAGE nr, bank [, bild]
SPRITE ADDFRAME nr, "fil"
SPRITE ON nr
SPRITE OFF nr
SPRITE POS nr, x, y
SPRITE MOVE nr, dx, dy
SPRITE FRAME nr, bild
SPRITE ZOOM nr, procent [, procentY]
SPRITE ROTATE nr, grader
SPRITE ALPHA nr, värde
SPRITE FADE nr, mål, bilder
SPRITE LAYER nr, lager
SPRITE HANDLE nr, x, y
SPRITE GROUP nr, "grupp"
SPRITE UNGROUP nr, "grupp"
SPRITE CLEARGROUP "grupp"
SPRITE EFFECT ADD nr, typ, parametrar...
SPRITE EFFECT UPDATE nr, index, parameter, värde
SPRITE EFFECT CLEAR nr
SPRITE [#lager,] nr, x, y, bank
```

| Kommando | Gör |
|---|---|
| `SPRITE LOAD` | laddar en bild, eller ett bildark med bredd, höjd och antal bilder |
| `SPRITE IMAGE` | använder bild nummer `bild` (från 0) ur en bildbank från `IMAGE LOAD` |
| `SPRITE ADDFRAME` | lägger till en bild från en egen fil sist i spritens bilder |
| `SPRITE ON` / `OFF` | visar eller döljer spriten |
| `SPRITE POS` | flyttar spritens övre vänstra hörn till x, y |
| `SPRITE MOVE` | flyttar spriten ett stycke från där den är |
| `SPRITE FRAME` | väljer bild i bildarket, från 0 |
| `SPRITE ZOOM` | storlek, 100 = normal; negativt värde speglar |
| `SPRITE ROTATE` | vrider runt mittpunkten |
| `SPRITE ALPHA` | genomskinlighet, 0 – 255 |
| `SPRITE FADE` | tonar mjukt till målvärdet under så många skärmbilder |
| `SPRITE LAYER` | flyttar spriten till ett lager; positionen räknas då i lagrets koordinater och följer med när det scrollar |
| `SPRITE HANDLE` | flyttar punkten som kollisionstesterna räknar från |
| `SPRITE GROUP` | lägger spriten i en grupp för `HITGROUP` och liknande |
| `SPRITE UNGROUP` | tar bort spriten ur gruppen |
| `SPRITE CLEARGROUP` | tömmer gruppen |
| `SPRITE nr, x, y, bank` | kortform: bild 1 ur banken, position och `SPRITE ON` i ett kommando |

Om filnamnet i `SPRITE LOAD` innehåller storleken, som `gubbe_W32_H48_B8.png`, räcker `SPRITE LOAD nr, "fil"`.

**Effekter**, för både sprites och fonter:

| Typ | Parametrar | Gör |
|---|---|---|
| `BLUR` | `radie` | suddig |
| `WAVE` | `ampX, frekX, ampY, frekY` | böljar; amplitud i punkter |
| `COLOR` | `ljus, kontrast, mättnad` | ändrar färgerna, 1 = oförändrat |

Effekterna numreras från 0 i den ordning de läggs till. Med `EFFECT UPDATE` kan parametrarna `RADIUS`, `AMPLITUDEX`, `FREQUENCYX`, `AMPLITUDEY`, `FREQUENCYY`, `PHASEX`, `PHASEY`, `TIME`, `BRIGHTNESS`, `CONTRAST` och `SATURATION` ändras.

### SWAP

```text
SWAP variabel, variabel
```

Byter plats på värdena i två variabler eller arrayplatser av samma sort.

```basic
A = 1 : B = 2
SWAP A, B
PRINT A & " " & B
DIM T(3) = [10, 20, 30]
SWAP T(0), T(2)
PRINT JOIN$(T, ",")
```

## T

### TEXT

```text
TEXT [#lager,] x, y, värde
```

Skriver text i grafiken med övre vänstra hörnet i x, y, med fonten från `FONT SET` och `INKG`-färgen.

### TILE

Banor byggda av rutor.

```text
TILE LOAD "fil", bredd, höjd
TILE MAP bredd, höjd
TILE SET x, y, nummer
TILE DRAW [#lager,] x, y
```

| Kommando | Gör |
|---|---|
| `TILE LOAD` | laddar tilesetet; bredd och höjd är storleken på en ruta i punkter |
| `TILE MAP` | skapar en tom karta, mätt i rutor |
| `TILE SET` | lägger tile `nummer` (från 0) på ruta x, y; –1 är tomt |
| `TILE DRAW` | ritar ut hela kartan i lagret med övre vänstra hörnet i x, y |

Det finns en karta åt gången. Funktionen `TILE()` läser den.

### TIMER

`TIMER` som kommando sätter variabeln `TIMER` till klockan i millisekunder. För att mäta tid är `MILLIS()` enklare.

## W

### WAIT

```text
WAIT VBL
WAIT millisekunder
WAIT MUSIC [kanal]
```

`WAIT VBL` visar det som ritats och väntar på nästa skärmbild, 60 gånger i sekunden. Det är hjärtat i varje spelloop: sprites, animationer och toningar uppdateras här. `WAIT` med ett tal väntar så många millisekunder. `WAIT MUSIC` väntar tills syntmusiken (eller en kanal) har spelat klart.

### WHILE ... WEND

```text
WHILE villkor
  kommandon
WEND
```

Upprepar kommandona så länge villkoret är sant. Är det falskt från början körs de aldrig. Lämnas med `EXIT WHILE`.
