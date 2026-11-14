# Fonter och text i spel

Text i spel ritas ofta med egna bokstäver: en bild med alla tecken bredvid varandra, ritade i spelets stil. AMOS Like Basic kallar det en **font**. Texten kan zoomas, vridas och få effekter, precis som sprites.

## Ladda en font

En fontbild är ett rutnät där varje ruta är ett tecken. Du laddar den med storleken på en ruta, och talar sedan om vilka tecken rutorna innehåller, i ordning, med `FONT MAP`:

```basic
FONT LOAD 0, "Graphics/Skogshopp/font.png", 24, 32
FONT MAP 0, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
FONT PRINT 0, 100, 200, "HEJ PÅ DIG!"
```

`FONT PRINT font, x, y, text` skriver texten med övre vänstra hörnet i x, y. Gemener visas som versaler, och tecken som inte finns i `FONT MAP` hoppas över. Vill du skriva i ett annat lager anger du det först: `FONT PRINT #2, 0, 100, 200, "HEJ"`.

## Text som ändras

Fonttext ligger kvar på skärmen tills du tar bort den med `FONT CLEAR`. Text som ändras, som poäng eller en rörlig rubrik, gör du därför så här i varje bild: först `FONT CLEAR`, sedan `FONT PRINT` med det nya.

```basic
FONT LOAD 0, "Graphics/Skogshopp/font.png", 24, 32
FONT MAP 0, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
POANG = 0
Loop:
  POANG = POANG + 7
  FONT CLEAR
  FONT PRINT 0, 20, 20, "POÄNG " & POANG
  WAIT VBL
GOTO Loop
```

## Zoom och vridning

`FONT ZOOM font, procent` ändrar storleken och `FONT ROTATE font, grader` vrider texten. De gäller allt som skrivs med fonten efteråt:

```basic
FONT LOAD 1, "Graphics/Skogshopp/font.png", 24, 32
FONT MAP 1, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
V = 0
Loop:
  V = V + 2
  FONT CLEAR
  FONT ZOOM 1, 150 + SIN(V) * 50
  FONT ROTATE 1, SIN(V * 2) * 15
  FONT PRINT 1, 180, 200, "SNURRA!"
  WAIT VBL
GOTO Loop
```

## Effekter

Fonter kan få samma effekter som sprites. Effekten hör till fonten och ligger kvar även efter `FONT CLEAR`, så du lägger till den en gång innan loopen:

```basic
FONT LOAD 1, "Graphics/Skogshopp/font.png", 24, 32
FONT MAP 1, "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
FONT ZOOM 1, 200
FONT EFFECT ADD 1, WAVE, 0, 0, 16, 0.02
Loop:
  FONT CLEAR
  FONT PRINT 1, 100, 180, "SKOGSHOPP"
  WAIT VBL
GOTO Loop
```

`WAVE, ampX, frekX, ampY, frekY` gör att texten böljar. Här böljar den bara i höjdled. Vågen rör sig av sig själv.

| Kommando | Gör |
|---|---|
| `FONT EFFECT ADD font, WAVE, ampX, frekX, ampY, frekY` | böljande text |
| `FONT EFFECT ADD font, BLUR, radie` | suddig text |
| `FONT EFFECT ADD font, COLOR, ljus, kontrast, mättnad` | ändrade färger |
| `FONT EFFECT UPDATE font, index, parameter, värde` | ändrar en effekt |
| `FONT EFFECT CLEAR font` | tar bort alla effekter |

Lägger du till en effekttyp som fonten redan har uppdateras den befintliga i stället för att en ny läggs till. Det gör ingenting om `FONT EFFECT ADD` råkar ligga inne i loopen.

## Gör en egen font

Rita tecknen i ett ritprogram, lika stora rutor bredvid varandra, och spara som PNG med genomskinlig bakgrund. Skogshopp-fonten är gjord av ett Python-skript, `Graphics/Skogshopp/generate_assets.py`, som du kan titta på och ändra i.

> **Prova själv:** Gör en text som rullar in från höger kant och stannar mitt på skärmen.
