# Ljud och musik

Ett spel utan ljud känns tomt. AMOS Like Basic har en inbyggd **synt** som gör ljudeffekter och musik i stil med gamla spelkonsoler, och kan dessutom spela ljudfiler och musikmoduler.

## Färdiga ljudeffekter

De enklaste ljuden är färdiga. Skriv `PLAY` och namnet:

```basic
PLAY COIN
WAIT 400
PLAY JUMP
WAIT 400
PLAY LASER
WAIT 400
PLAY HIT
WAIT 400
PLAY POWERUP
WAIT 600
PLAY EXPLOSION
```

| Effekt | Passar till |
|---|---|
| `PLAY COIN` | plocka upp något |
| `PLAY JUMP` | hopp |
| `PLAY LASER` | skott |
| `PLAY HIT` | träff |
| `PLAY BLIP` | menyval, knapptryck |
| `PLAY POWERUP` | bonus, ny nivå |
| `PLAY EXPLOSION` | explosion, förlorat liv |

## Musik med synten

Synten har åtta kanaler som spelar samtidigt och går exakt i takt med varandra. En C64 hade tre och en NES fyra, så det räcker långt. Varje kanal får en ljudtyp, en volym och en melodi:

```basic
PLAY BPM 132
PLAY WAVE 1, "Square"
PLAY WAVE 2, "Triangle"
PLAY WAVE 4, "Drums"
PLAY VOLUME 1, 0.4
PLAY MUSIC 1, "C5:8 E5:8 G5:8 E5:8 F5:8 A5:8 G5:4"
PLAY MUSIC 2, "C3:4 G2:4 F2:4 G2:4"
PLAY MUSIC 4, "BD:8 HH:8 SD:8 HH:8 BD:8 HH:8 SD:8 HH:8"
WAIT MUSIC
```

| Kommando | Gör |
|---|---|
| `PLAY BPM takt` | tempo i slag per minut |
| `PLAY WAVE kanal, "typ"` | ljudtyp: `Square`, `Pulse`, `Triangle`, `Sawtooth`, `Sine`, `Noise` eller `Drums` |
| `PLAY VOLUME kanal, volym` | volym från 0 till 1 |
| `PLAY MUSIC kanal, noter` | spelar noterna på kanalen en gång |
| `PLAY LOOP kanal, noter` | spelar noterna om och om igen |
| `PLAY STOP [kanal]` | stoppar en kanal, eller all syntmusik |
| `WAIT MUSIC` | väntar tills all musik har spelat klart |
| `WAIT MUSIC kanal` | väntar på en kanal |

### Noter

En not skrivs som **namn**, **oktav** och **längd**: `C5:8` är ett C i femte oktaven, en åttondelsnot.

- Namnen är `C D E F G A B`, med `#` för höjt (`C#`, `F#`) eller `b` för sänkt (`Db`, `Bb`).
- Oktav 4 är mellanläget. `A4` är 440 Hz, tonen orkestrar stämmer efter.
- Längden är 1 (helnot), 2 (halvnot), 4 (fjärdedel), 8 (åttondel), 16 (sextondel) eller 32. Utelämnar du längden används samma som förra noten. En punkt efter längden gör noten en halv gång längre: `C5:4.` är en punkterad fjärdedel.
- Utelämnar du oktaven används samma som förra noten: `C5:8 E G C6` är C5, E5, G5 och C6.
- `R` är en paus: `R:4` är en fjärdedelspaus.

På en kanal med `Drums` spelar du trumljud i stället för noter: `BD` (baskagge), `SD` (virvel), `HH` (hihat), `OH` (öppen hihat), `CY` (cymbal) och `TM` (puka).

Noterna kan också ligga i en textvariabel: `PLAY MUSIC 1, MELODI$`.

### Musik som spelas om

`PLAYING(kanal)` är 1 så länge kanalen spelar, och `ANYPLAYING()` om någon kanal spelar. Så här startar du om musiken när den är slut, i spelets huvudloop:

```basic nocheck
IF PLAYING(4) = 0 THEN StartaMusiken
```

Skogshopp gör så, med en procedur som innehåller alla `PLAY MUSIC`-rader. Ska en kanal bara upprepa samma sak, som ett trumkomp, är `PLAY LOOP` enklare.

## Forma ljudet

Ljudet räknas ut medan det spelar, så allt i det här avsnittet kan ändras när som helst, även mitt i en ton. Det är så man fick fram de levande ljuden på C64 och NES: en bas som öppnar sig, en melodi som vibrerar, ett ackord som spelas så snabbt att det låter som tre toner samtidigt.

### Envelope: ADSR

`PLAY ADSR kanal, attack, decay, sustain, release` bestämmer hur varje ton tänds och slocknar:

- **attack**: hur många millisekunder det tar för tonen att nå full styrka
- **decay**: hur många millisekunder det sedan tar att sjunka till sustain-nivån
- **sustain**: styrkan medan tonen hålls, 0 – 100
- **release**: hur många millisekunder det tar för tonen att tona ut när den släpps

```basic
PLAY WAVE 1, "Square"
PLAY ADSR 1, 2, 150, 0, 50
PLAY MUSIC 1, "C5:8 E5 G5 C6 G5 E5 C5:4"
WAIT MUSIC
PLAY ADSR 1, 300, 100, 80, 600
PLAY MUSIC 1, "C4:2 G4:2"
WAIT MUSIC
```

Först korta, plingande toner som en harpa, sedan mjuka toner som en orgel. `PLAY GATE kanal, procent` bestämmer hur stor del av notlängden tonen hålls innan den släpps, normalt 90.

### Pulsbredd

Vågformen `Pulse` är en fyrkantsvåg där du själv väljer hur stor del av varje svängning som är "uppe". Smal puls låter tunt och nasalt, som NES-melodier; 50 % är en vanlig fyrkantsvåg. Med två värden till svänger pulsbredden fram och tillbaka av sig själv, det klassiska C64-ljudet:

```text
PLAY PULSE kanal, bredd [, djup, fart]
```

```basic
PLAY WAVE 1, "Pulse"
PLAY PULSE 1, 12
PLAY MUSIC 1, "A4:4 C5 E5 A5"
WAIT MUSIC
PLAY PULSE 1, 30, 20, 2
PLAY MUSIC 1, "A3:1"
WAIT MUSIC
```

### Filter

Ett filter tar bort en del av ljudet. `PLAY FILTER kanal, "typ", frekvens, resonans`:

| Typ | Släpper igenom |
|---|---|
| `"LOW"` | det mörka, under frekvensen |
| `"HIGH"` | det ljusa, över frekvensen |
| `"BAND"` | bara runt frekvensen |
| `"OFF"` | allt, filtret är av |

Frekvensen är i hertz, från 20 till 18 000. Resonansen, 0 – 100, förstärker tonen precis vid frekvensen och ger det skarpa, "pipiga" ljudet. Det roliga är att ändra frekvensen medan tonen spelar:

```basic
PLAY WAVE 1, "Sawtooth"
PLAY ADSR 1, 5, 0, 100, 200
PLAY LOOP 1, "A2:8 A2 A3 A2 G2 G2 G3 G2"
F = 0
Loop:
  F = F + 2
  PLAY FILTER 1, "LOW", 600 + SIN(F) * 500, 75
  WAIT VBL
GOTO Loop
```

Basen öppnar och stänger sig i en jämn våg, som i ett gammalt syntspel.

### Vibrato, glidning och bend

| Kommando | Gör |
|---|---|
| `PLAY VIBRATO kanal, djup, fart` | tonhöjden vibrerar; djup i hundradels halvton (100 = en halvton), fart i Hz |
| `PLAY GLIDE kanal, millisekunder` | tonen glider till nästa not i stället för att hoppa |
| `PLAY BEND kanal, halvtoner` | flyttar tonhöjden direkt, även med decimaler; 0 är normalt |

```basic
PLAY WAVE 1, "Triangle"
PLAY VIBRATO 1, 25, 6
PLAY GLIDE 1, 80
PLAY MUSIC 1, "E5:4 G5 A5:2 G5:8 E5 D5:2"
WAIT MUSIC
```

### Arpeggio

På C64 och NES fanns för få kanaler för att spela ackord. I stället spelade man ackordets toner efter varandra så snabbt att örat hör dem som ett ackord. `PLAY ARP kanal, "steg", fart` gör det automatiskt med varje not på kanalen. Stegen är halvtoner från noten, och farten är steg per sekund (50 är som på en C64):

```basic
PLAY WAVE 1, "Pulse"
PLAY PULSE 1, 25
PLAY ARP 1, "0 4 7", 50
PLAY MUSIC 1, "C5:2 A4:2 F4:2 G4:2"
WAIT MUSIC
```

`"0 4 7"` är ett durackord och `"0 3 7"` ett mollackord. `PLAY ARP 1, ""` stänger av.

### Ringmodulering och synk

Två specialeffekter från SID-kretsen i C64. Båda kopplar ihop en kanal med en annan:

| Kommando | Gör |
|---|---|
| `PLAY RING kanal, källa` | kanalens ljud vänds varje halv svängning hos källkanalen; ger metalliska, klockliknande ljud |
| `PLAY SYNC kanal, källa` | kanalens våg börjar om varje gång källkanalen gör det; ger skarpa, skrikande ljud |

`PLAY RING kanal, 0` och `PLAY SYNC kanal, 0` stänger av. Källkanalen måste spela en ton för att något ska hända.

Alla inställningar nollställs när programmet startar om.

## Ljudfiler och musikmoduler

Du kan också spela inspelade ljud och färdig musik:

| Kommando | Gör |
|---|---|
| `SAM PLAY "fil"` | spelar en ljudfil (WAV, MP3, OGG) en gång |
| `SAM LOOP "fil"` | spelar en ljudfil om och om igen |
| `SAM STOP "fil"` | stoppar ljudet (`SAM STOP ""` stoppar alla) |
| `MUSIC PLAY "fil"` | spelar en musikmodul (MOD, XM, S3M, IT) |
| `MUSIC STOP` | stoppar musiken |

```basic nocheck
MUSIC PLAY "Sound/mus2.mod"
SAM PLAY "Sound/ding.mp3"
```

Musikmoduler är musikformatet från Amiga-tiden, och det finns tusentals fritt på nätet. Vill du spela musik som ligger som MP3, använd `SAM LOOP`. Tänk på att kontrollera att du får använda musiken om du ska dela ditt spel.

> **Prova själv:** Komponera en egen trudelutt på två kanaler: melodi på kanal 1 och bas på kanal 2.
