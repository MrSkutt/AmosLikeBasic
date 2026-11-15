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

Ska låten bara upprepas är `PLAY LOOP` enklare. Skogshopp startar alla fyra kanalerna med `PLAY LOOP`, och eftersom kanalerna är exakt lika långa håller de takten varv efter varv.

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

### Filter-envelope

Ska filtret i stället öppnas och stängas av sig självt för varje not använder du `PLAY FILTERENV kanal, djup, attack, decay`. Vid varje ny not öppnas filtret med upp till `djup` hertz över frekvensen i `PLAY FILTER`, på `attack` millisekunder, och stängs sedan igen under ungefär `decay` millisekunder. Det ger det klassiska "wah"-ljudet i basen:

```basic
PLAY WAVE 1, "Sawtooth"
PLAY ADSR 1, 2, 0, 100, 60
PLAY FILTER 1, "LOW", 150, 65
PLAY FILTERENV 1, 2500, 2, 180
PLAY MUSIC 1, "A2:8 A2 A3 A2 F2 F2 F3 F2 C3 C3 C4 C3 G2 G2 G3 G2"
WAIT MUSIC
```

Ett negativt djup stänger filtret i stället, och `PLAY FILTERENV 1, 0` stänger av. Filtret måste vara på med `PLAY FILTER` för att envelopen ska höras.

> **Tips:** Projektet `Projects/synthdemo.amosproj` är en kort låt som använder det mesta i det här avsnittet: PWM, vibrato, filter-envelope, arpeggio och trummor.

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

### Effekter i noterna

Inställningarna ovan kan också stå *inne i* notsträngen, inom klamrar `{ }`. Då ändras ljudet precis där i melodin, utan att ditt program behöver göra något medan låten spelar. Effekterna tar ingen tid, så takten påverkas inte:

```basic
PLAY WAVE 1, "Sawtooth"
PLAY MUSIC 1, "{FILTER LOW 300 40} C3:8 C3 C4 C3 {CUT 3000} C3 C3 C4 C3 {SWEEP 300 1000} C3 C3 C4 C3 C3 C3 C4 C3"
WAIT MUSIC
```

Basen låter dov, blir skarp mitt i takten och glider sedan tillbaka under en sekund. Flera effekter på samma ställe skiljs åt med semikolon: `{WAVE PULSE; PULSE 12; VIBRATO 30}`.

Effekterna har samma namn och värden som `PLAY`-kommandona, fast utan kanalnummer och citattecken:

| Effekt | Gör |
|---|---|
| `{WAVE SAW}` | byter ljudtyp |
| `{VOLUME 0.3}` | volym 0 – 1 |
| `{FADE 0, 2000}` | volymen glider till ett nytt värde under ett antal millisekunder |
| `{ADSR 5, 100, 60, 200}` | envelope |
| `{GATE 50}` | hur stor del av notlängden tonen hålls |
| `{PULSE 30}` | pulsbredd; med två värden till även PWM: `{PULSE 30, 15, 2}` |
| `{FILTER LOW, 800, 50}` | filtertyp, och om du vill frekvens och resonans |
| `{CUT 800}` | bara filtrets frekvens |
| `{RES 70}` | bara filtrets resonans |
| `{SWEEP 4000, 1500}` | filtret glider till en ny frekvens under ett antal millisekunder |
| `{FILTERENV 2000, 5, 200}` | filter-envelope |
| `{VIBRATO 20, 6}` | vibrato; `{VIBRATO 0}` stänger av |
| `{ARP 0 4 7}` | arpeggio; `{ARP}` stänger av |
| `{ARPRATE 50}` | arpeggiots fart |
| `{GLIDE 50}` | glidning mellan noter |
| `{BEND 2}` | flyttar tonhöjden ett antal halvtoner |
| `{RING 2}`, `{SYNC 2}` | ringmodulering och synk mot en annan kanal |

Ett vanligt knep från C64-musik är att byta ackord med arpeggio för varje takt. Då räcker en enda kanal till ett helt ackordkomp:

```basic
PLAY WAVE 3, "Pulse"
PLAY VOLUME 3, 0.15
PLAY MUSIC 3, "{ARP 0 4 7} C4:2 {ARP 0 3 7} A3:2 {ARP 0 4 7} F3:2 G3:2"
WAIT MUSIC
```

Inställningar som ändras av en effekt ligger kvar när melodin är slut, precis som om du hade skrivit `PLAY`-kommandot. I en loopad melodi är det därför bra att sätta ljudet i början av strängen, så att varje varv börjar likadant. Skogshopps låt gör så: melodin byter till en tunnare pulsvåg med glidning i andra halvan och tillbaka när den börjar om, och basens filter öppnas sakta med `SWEEP`.

Alla inställningar nollställs när programmet startar om.

## Låtar från trackern

En **tracker** är ett musikprogram där låten skrivs uppifrån och ned i rader, som ett kalkylark. Varje kolumn är en kanal, och varje rad kan ha en not, ett instrument och en effekt. ProTracker på Amiga fungerade så, och nästan all spelmusik på C64 och NES gjordes på samma sätt.

AMOS Like Basic spelar trackerlåtar med synten. Låten ligger i en egen fil som slutar på `.amostrk`:

```basic
TRACK LOAD "Sound/Tracker/skogsvisa.amostrk"
TRACK PLAY
Loop:
  WAIT VBL
GOTO Loop
```

| Kommando | Gör |
|---|---|
| `TRACK LOAD "fil"` | laddar en låt |
| `TRACK PLAY [mönster]` | spelar låten, från början eller från en plats i ordningslistan |
| `TRACK STOP` | stoppar låten (`PLAY STOP` gör det också) |
| `TRACK VOLUME volym` | låtens volym, 0 – 1 |
| `TRACK LOOP 0` | låten spelas en gång i stället för att börja om (`TRACK LOOP 1` slår på igen) |

| Funktion | Ger |
|---|---|
| `TRACKPLAYING()` | 1 medan låten spelar |
| `TRACKPOS()` | platsen i ordningslistan som spelas, från 0 |
| `TRACKROW()` | raden i mönstret som spelas, från 0 |

En trackerlåt använder syntens kanal 1 – 4. Kanal 5 – 8 är lediga för `PLAY MUSIC` medan låten spelar, och ljudeffekterna som `PLAY COIN` påverkar den inte.

Projektet `Projects/trackerdemo.amosproj` spelar Skogsvisa, samma låt som i Skogshopp men skriven för trackern.

### Trackern i utvecklingsmiljön

Knappen **TRACKER** öppnar ett fönster där du skriver låtarna. I mitten är mönstret med de fyra kanalerna, till vänster ordningslistan och till höger instrumenten.

**Skriva noter.** Tangentbordet fungerar som ett piano, som i ProTracker. Den nedre bokstavsraden (`Z S X D C V G B H N J M`) är en oktav och den övre (`Q 2 W 3 E R 5 T 6 Y 7 U I`) nästa. Noten får instrumentet som är valt i listan, och du hör den medan du håller in tangenten. `1` släpper tonen (`===`). Efter varje not flyttas markören ned så många rader som **Step** säger. **Octave** väljer vilken oktav pianot börjar på, och `F1` och `F2` byter oktav.

**Instrument och effekter.** Med pilarna går markören till instrument- och effektkolumnerna. Där skriver du siffror och bokstäver direkt: `0`–`9` och `A`–`F` för hexsiffror, och i effektens första kolumn även `G`, `H` och `I`. `Delete` eller `Backspace` tömmer det markören står på.

**Spela.** `F5` spelar låten från platsen som är vald i ordningslistan. `F6` spelar mönstret om och om igen, `F7` spelar mönstret från markörens rad och `Esc` stoppar. Med **Follow** följer mönstret med när låten spelar. Klicka på en kanalrubrik för att tysta kanalen, och högerklicka för att bara höra den kanalen.

**Edit.** `Space` slår av och på edit-läget. När det är av kan du spela på pianotangenterna utan att något skrivs i mönstret, till exempel för att prova ett instrument. Markörraden är röd när edit är på. Spelar låten stoppar `Space` den i stället.

**Markera och ändra.** Håll in `Shift` och flytta markören, eller dra med musen, för att markera ett block. `Cmd+C`, `Cmd+X` och `Cmd+V` kopierar, klipper ut och klistrar in (`Ctrl` på Windows). `Cmd+↑` och `Cmd+↓` flyttar noterna en halvton, med `Shift` en oktav. `Cmd+I` (eller `Insert`) skjuter in en tom rad och `Cmd+Backspace` tar bort raden, i markörens kanal eller med `Shift` i alla kanaler. `Cmd+Z` ångrar och `Cmd+Shift+Z` gör om.

**Ordningslistan.** **INS** lägger till en plats efter den valda, **DEL** tar bort den. **PAT -** och **PAT +** byter vilket mönster platsen spelar; efter det sista mönstret gör **PAT +** ett nytt tomt. **CLONE PATTERN** kopierar mönstret till ett nytt, så att du kan göra en variant utan att ändra originalet. **Rows** är hur många rader mönstret har.

**Instrumentpanelen.** Allt du ändrar hörs direkt, även medan låten spelar. Tabellen skrivs med ett steg per rad, och rutan blir röd om ett steg är fel. En ny låt har sex färdiga instrument att börja med: melodi, bas, ackord, bastrumma, virvel och hihat.

**Spara.** **SAVE** sparar låten som en `.amostrk`-fil, helst i `Sound/Tracker` i projektmappen. Statusraden visar sedan raden som laddar låten, till exempel `TRACK LOAD "Sound/Tracker/minlat.amostrk"`. Exporterar du spelet följer filen med, eftersom namnet står inom citattecken i programmet.

### Hur en låt är uppbyggd

En låt består av tre delar:

- **Instrument** är ljud: vågform, envelope, filter och så vidare, med samma värden som `PLAY`-kommandona. Instrumenten är numrerade från 01.
- **Mönster** (patterns) är block av rader, oftast 64. En rad har en cell för varje kanal.
- **Ordningslistan** säger i vilken ordning mönstren spelas. Ett mönster kan spelas många gånger, så en refräng behöver bara skrivas en gång.

Tempot styrs av två tal. **Speed** är hur många *tick* varje rad varar, och **tempo** hur snabbt tick kommer: 125 ger 50 tick i sekunden. Med speed 6 och fyra rader per taktslag blir tempot samma tal som slag per minut, så speed 6 och tempo 136 är 136 BPM.

En cell skrivs som not, instrument och effekt:

```text
C-4 01 A04
```

Det betyder C i fjärde oktaven, instrument 01, och effekt `A04` (volymen sjunker). `---` är ingen ny not, `===` släpper tonen, och `..` och `...` är tomt. En ton fortsätter att låta tills en ny not eller `===` kommer.

### Effekter

Effekten är en bokstav eller siffra och två hexsiffror. De flesta gäller bara på sin rad, så en glidning eller ett vibrato som ska fortsätta skrivs på varje rad. Nummer som i ProTracker:

| Effekt | Gör |
|---|---|
| `0xy` | arpeggio: tonen växlar mellan noten, x halvtoner upp och y halvtoner upp, ett steg per tick |
| `1xx` | glider uppåt, xx sextondels halvton per tick (`110` är en halvton per tick) |
| `2xx` | glider nedåt |
| `3xx` | glider till noten på raden i stället för att spela den; `300` fortsätter med samma fart |
| `4xy` | vibrato: x är fart, y djup; `400` fortsätter |
| `5xy` | fortsätter `3` och ändrar volymen som `A` |
| `6xy` | fortsätter `4` och ändrar volymen som `A` |
| `Axy` | volymen ökar med x eller minskar med y per tick |
| `Bxx` | hoppar till plats xx i ordningslistan |
| `Cxx` | volym 00 – 40 (hex, 40 är full volym) |
| `Dxy` | går vidare till rad xy (decimalt) i nästa mönster |
| `ECx` | tystnar på tick x |
| `EDx` | noten börjar på tick x i stället för direkt |
| `Fxx` | 01 – 1F sätter speed, 20 – FF tempo |
| `Gxx` | filterfrekvens: 00 är 20 Hz, 80 ungefär 640 Hz och FF 18 000 Hz; slår på lågpass om filtret är av |
| `Hxx` | filterresonans, 00 – FF |
| `Ixx` | pulsbredd, 80 är 50 % |

`G`, `H` och `I` ligger kvar på kanalen tills den byter instrument, så ett filter kan öppnas sakta över många toner.

### Instrument

Ett instrument har samma inställningar som `PLAY`-kommandona: `Wave`, `Volume`, `PulseWidth`, `PwmDepth`, `PwmRate`, `Attack`, `Decay`, `Sustain`, `Release`, `Filter`, `Cutoff`, `Resonance`, `FilterEnvDepth`, `FilterEnvAttack`, `FilterEnvDecay`, `Vibrato`, `VibratoRate`, `Ring` och `Sync`. De som inte anges får standardvärden.

Dessutom kan ett instrument ha en **tabell** som går ett steg per tick från att noten börjar. Varje steg kan byta vågform, flytta tonen ett antal halvtoner och ändra pulsbredden (`P30` är 30 %). Det är så C64-musiker gjorde trummor och ackord:

```text
"Table": ["SQUARE +24", "TRIANGLE +12", "+6", "+2", "-2", "-5", "-8", "-10", "-12"]
```

Den tabellen är en bastrumma: ett kort klick av fyrkantsvåg och sedan en triangelvåg som snabbt sjunker i tonhöjd. `TableLoop` är steget tabellen börjar om från när den är slut. Utan den stannar tabellen på sista steget. Ett durackord som arpeggio är `["0", "+4", "+7"]` med `"TableLoop": 0`.

### Filen

Filen är JSON, så den går att öppna i en vanlig texteditor. Varje rad i ett mönster är en textrad, med kanalerna åtskilda av `|`:

```text
{
  "Format": "AmosTracker",
  "Version": 1,
  "Title": "Skogsvisa",
  "Speed": 6,
  "Tempo": 136,
  "Instruments": [
    { "Name": "Bas", "Wave": "Sawtooth", "Volume": 0.25, "Filter": "Low", "Cutoff": 250, ... },
    ...
  ],
  "Order": [0, 1, 2, 3],
  "Patterns": [
    [
      "G-5 01 ...|G-2 03 G5D|G-3 04 047|C-7 0B ...",
      "--- .. ...|=== .. ...|--- .. 047|--- .. ...",
      ...
    ]
  ]
}
```

Ett fel i filen ger ett felmeddelande som talar om mönster, rad och kanal.

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
