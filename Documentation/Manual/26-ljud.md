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

Synten har fyra kanaler som spelar samtidigt. Varje kanal får en ljudtyp, en volym och en melodi:

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
| `PLAY WAVE kanal, "typ"` | ljudtyp: `Square`, `Triangle`, `Sawtooth`, `Pulse`, `Noise` eller `Drums` |
| `PLAY VOLUME kanal, volym` | volym från 0 till 1 |
| `PLAY MUSIC kanal, "noter"` | spelar noterna på kanalen |
| `PLAY STOP` | stoppar all syntmusik |
| `WAIT MUSIC` | väntar tills all musik har spelat klart |
| `WAIT MUSIC kanal` | väntar på en kanal |

### Noter

En not skrivs som **namn**, **oktav** och **längd**: `C5:8` är ett C i femte oktaven, en åttondelsnot.

- Namnen är `C D E F G A B`, med `#` för höjt (`C#`, `F#`) eller `b` för sänkt (`Db`, `Bb`).
- Oktav 4 är mellanläget. `A4` är 440 Hz, tonen orkestrar stämmer efter.
- Längden är 1 (helnot), 2 (halvnot), 4 (fjärdedel), 8 (åttondel) eller 16 (sextondel). Utelämnar du längden används samma som förra noten.
- `R` är en paus: `R:4` är en fjärdedelspaus.

På en kanal med `Drums` spelar du trumljud i stället för noter: `BD` (baskagge), `SD` (virvel), `HH` (hihat), `OH` (öppen hihat), `CY` (cymbal) och `TM` (puka).

### Musik som spelas om

`PLAYING(kanal)` är 1 så länge kanalen spelar, och `ANYPLAYING()` om någon kanal spelar. Så här startar du om musiken när den är slut, i spelets huvudloop:

```basic nocheck
IF PLAYING(4) = 0 THEN StartaMusiken
```

Skogshopp gör så, med en procedur som innehåller alla `PLAY MUSIC`-rader.

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
