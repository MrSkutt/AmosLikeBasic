# Tabeller

## Tangenter

Namnen används i `KEYSTATE` och ges av `INKEY$`. Stora och små bokstäver spelar ingen roll.

| Tangent | Namn |
|---|---|
| piltangenter | `Left`, `Right`, `Up`, `Down` |
| mellanslag | `Space` |
| Enter | `Return` |
| Escape | `Escape` |
| Tab, Backsteg | `Tab`, `Back` |
| bokstäver | `A` till `Z` |
| siffror ovanför bokstäverna | `D0` till `D9` |
| siffror på det numeriska tangentbordet | `NumPad0` till `NumPad9` |
| skift, ctrl, alt | `LeftShift`, `RightShift`, `LeftCtrl`, `RightCtrl`, `LeftAlt`, `RightAlt` |
| funktionstangenter | `F1` till `F11` |

`F8` och `F12` används av spelfönstret: `F8` slår på och av bildrörseffekten och `F12` växlar helskärm.

## Handkontroll

| Knapp | Namn i `JOYSTATE` |
|---|---|
| A, B, X, Y | `A`, `B`, `X`, `Y` |
| axelknappar | `LB`, `RB` |
| start och select | `Start`, `Select` |
| styrkorset | `Up`, `Down`, `Left`, `Right` |

| Axel | Namn i `JOYAXIS` | Värden |
|---|---|---|
| vänster spak | `LeftX`, `LeftY` | –100 till 100 |
| höger spak | `RightX`, `RightY` | –100 till 100 |
| avtryckare | `LT`, `RT` | 0 till 100 |

## Paletten

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

Färgnamnen är de engelska webbfärgerna, till exempel `Black`, `White`, `Red`, `Green`, `Lime`, `Blue`, `Yellow`, `Cyan`, `Magenta`, `Orange`, `Pink`, `Purple`, `Brown`, `Gray`, `Silver`, `Gold`, `Navy`, `Teal`, `SkyBlue`, `ForestGreen`, `SaddleBrown` och `DarkRed`.

## Noter i PLAY MUSIC

| Skrivsätt | Betyder |
|---|---|
| `C D E F G A B` | tonnamn |
| `C#`, `Db` | höjd eller sänkt ton |
| `C5` | oktav; `A4` är 440 Hz |
| `C5:8` | längd: 1, 2, 4, 8, 16 eller 32 (hel- till trettiotvåondelsnot) |
| `C5:4.` | punkterad: en halv gång längre |
| `E` | utan oktav: samma oktav som förra noten |
| `R:4` | paus |
| `E5` | utan längd: samma längd som förra noten |

Trumljud på en kanal med `PLAY WAVE kanal, "Drums"`:

| Namn | Ljud |
|---|---|
| `BD` | baskagge |
| `SD` | virvel |
| `HH` | hihat |
| `OH` | öppen hihat |
| `CY` | cymbal |
| `TM` | puka |

## Tangenter i utvecklingsmiljön

| Tangent | Gör |
|---|---|
| `F1` | visar eller döljer variabelrutan |
| `F8` | bildrörseffekten på eller av |
| `F12` | spelfönstret i helskärm eller fönster |
| `PgUp`, `PgDown` | bläddrar en sida i editorn |

## Vanliga fallgropar

**`&` och `+` är lika starka.** `"Summa: " & 2 + 3` blir `Summa: 23`. Sätt parentes: `"Summa: " & (2 + 3)`.

**Arrayer börjar på 0.** `DIM A(10)` ger `A(0)` till `A(9)`. `A(10)` ger ett fel.

**DATA behöver en etikett.** `DATA`-rader måste stå under en etikett, och `RESTORE etikett` måste köras före första `READ`.

**Svart är genomskinligt.** Pixlar med färgen `0,0,0` syns inte, varken när du ritar eller i bilder. Använd `"#101010"` för svart.

**Glöm inte WAIT VBL.** En spelloop utan `WAIT VBL` visar aldrig något, eftersom bilden visas först vid `WAIT VBL`.

**LOCATE och LOCATEG har olika ordning.** `LOCATE rad, kolumn` räknar från 1, `LOCATEG kolumn, rad` från 0.

**Procedurer anropas utan parentes.** `Halsa "Kotte", 3`, inte `Halsa("Kotte", 3)`. Egna funktioner anropas däremot med parentes.

**Funktioner kan inte rita.** I en `FUNCTION` fungerar bara tilldelning, `IF`, `FOR`, `WHILE`, `INC`, `DEC` och `RETURN`. Använd en `PROC` för allt annat.

**Fonttext ligger kvar.** Text från `FONT PRINT` ritas om varje bild tills den tas bort med `FONT CLEAR`. Text som ändras måste därför börja med `FONT CLEAR` varje varv.
