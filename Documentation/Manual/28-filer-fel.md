# Filer, tid och fel

## Spara och läsa filer

Ett spel vill ofta komma ihåg saker mellan gångerna, som en topplista. Det gör man genom att skriva till en fil och läsa från den nästa gång.

En fil öppnas med `OPEN` och ett **kanalnummer**, som du sedan använder för att skriva eller läsa:

| Kommando | Gör |
|---|---|
| `OPEN OUT kanal, "fil"` | öppnar en fil för att skriva (en gammal fil med samma namn ersätts) |
| `OPEN APPEND kanal, "fil"` | öppnar för att skriva i slutet av en befintlig fil |
| `OPEN IN kanal, "fil"` | öppnar för att läsa |
| `PRINT #kanal, värde` | skriver en rad |
| `INPUT #kanal, variabel` | läser en rad |
| `EOF(kanal)` | 1 när hela filen är läst |
| `CLOSE kanal` | stänger filen |

```basic
OPEN OUT 1, "topplista.txt"
PRINT #1, "Kotte"
PRINT #1, 1200
PRINT #1, "Bagge"
PRINT #1, 900
CLOSE 1

OPEN IN 1, "topplista.txt"
WHILE EOF(1) = 0
  INPUT #1, NAMN$
  INPUT #1, POANG
  PRINT NAMN$ & ": " & POANG
WEND
CLOSE 1
```

Läser du till en variabel vars namn slutar på `$` får du raden som text, annars görs den om till ett tal. Filen sparas i den mapp programmet körs från, om du inte anger en hel sökväg.

## Tid

| Funktion / kommando | Ger |
|---|---|
| `MILLIS()` | millisekunder sedan programmet startade |
| `MICROS()` | mikrosekunder sedan programmet startade |
| `TICKS` | samma som `MILLIS()` |

`MILLIS()` passar till allt som ska hända efter en viss tid, oberoende av hur fort datorn är:

```basic
START = MILLIS()
REPEAT
  WAIT VBL
UNTIL MILLIS() - START >= 2000
PRINT "Två sekunder har gått"
```

För att mäta hur lång tid en del av ditt program tar finns `PROFILE`:

```basic
PROFILE START "loop"
FOR I = 1 TO 50000
  X = SIN(I) * COS(I)
NEXT I
PROFILE END "loop"
PRINT "Loopen tog " & TIME_loop & " ms"
```

`PROFILE END` skriver tiden i loggfönstret och sparar den i variabeln `TIME_namn`.

## När något går fel

Försöker programmet göra något omöjligt, som att dela med noll eller läsa utanför en array, stannar det med ett felmeddelande i loggfönstret. Där står vad som hände och på vilken rad.

Ibland vill man att programmet ska klara sig själv i stället. Med `ON ERROR` bestämmer du vad som ska hända:

| Kommando | Gör vid fel |
|---|---|
| `ON ERROR GOTO etikett` | hoppar till etiketten |
| `ON ERROR RESUME NEXT` | hoppar över kommandot som gick fel och fortsätter |
| `ON ERROR BREAK` | stannar som vanligt (standard) |

Efter ett fel innehåller `ERR$` felmeddelandet och `ERL` radnumret:

```basic
ON ERROR GOTO Fel
OPEN IN 1, "finns-inte.txt"
PRINT "Den här raden körs inte"
END

Fel:
  PRINT "Något gick fel på rad " & ERL
  PRINT ERR$
```

> **Prova själv:** Gör ett spel där man ska trycka på mellanslag så nära exakt tre sekunder efter start som möjligt. Spara det bästa resultatet i en fil.
