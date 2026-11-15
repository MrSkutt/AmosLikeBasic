# Beslut med IF

Hittills har programmen kört varje rad, en efter en. Med `IF` kan programmet välja: göra en sak *om* något är sant, och kanske något annat annars.

## IF ... THEN

```basic
POANG = 120
IF POANG > 100 THEN PRINT "Bra jobbat!"
```

Efter `IF` står ett **villkor**, något som antingen är sant eller falskt. Är villkoret sant körs det som står efter `THEN`. Är det falskt hoppar programmet över det.

## Jämförelser

| Tecken | Betyder |
|---|---|
| `=` | lika med |
| `<>` | inte lika med |
| `<` | mindre än |
| `>` | större än |
| `<=` | mindre än eller lika med |
| `>=` | större än eller lika med |

Strängar jämförs med `=` och `<>`:

```basic
INPUT "Lösenord: ", SVAR$
IF SVAR$ = "SESAM" THEN PRINT "Välkommen in!"
IF SVAR$ <> "SESAM" THEN PRINT "Fel lösenord."
```

> **Obs:** Strängar kan bara jämföras med `=` och `<>`. `<` och `>` fungerar bara för tal.

## ELSE

Med `ELSE` anger du vad som ska hända när villkoret är falskt:

```basic
LIV = 0
IF LIV > 0 THEN PRINT "Spelet fortsätter" ELSE PRINT "GAME OVER"
```

## Flera kommandon

Efter `THEN` (och `ELSE`) kan du skriva flera kommandon med kolon emellan:

```basic
POANG = 950
IF POANG >= 1000 THEN PRINT "Extraliv!" : LIV = LIV + 1 ELSE PRINT "Fortsätt samla"
```

Blir det mycket kod är det tydligare att skriva `IF` som ett **block**. Då står `THEN` sist på raden, koden kommer på raderna efter, och blocket avslutas med `ENDIF`:

```basic
POANG = 1200
LIV = 3
IF POANG >= 1000 THEN
  PRINT "Extraliv!"
  LIV = LIV + 1
  POANG = POANG - 1000
ELSE
  PRINT "Samla " & (1000 - POANG) & " poäng till"
ENDIF
PRINT "Liv: " & LIV
```

Du kan skriva `END IF` med mellanslag om du tycker det är lättare att läsa. Raderna inne i blocket brukar dras in två steg, så att man ser vad som hör till `IF`:en. Det är inget krav, men det gör koden mycket lättare att läsa.

## AND, OR och NOT

Ibland ska flera saker vara sanna samtidigt. Det skriver du med `AND`:

```basic
X = 150
IF X > 100 AND X < 200 THEN PRINT "X är mellan 100 och 200"
```

`OR` betyder att minst ett av villkoren ska vara sant:

```basic
SVAR$ = "J"
IF SVAR$ = "J" OR SVAR$ = "j" THEN PRINT "Du svarade ja"
```

`NOT` vänder på ett villkor:

```basic
KLAR = 0
IF NOT KLAR = 1 THEN PRINT "Inte klar än"
```

Blandar du `AND` och `OR` räknas `AND` först, precis som gånger går före plus. Använd parenteser för att visa vad du menar:

```basic
A = 1 : B = 0 : C = 1
IF (A = 1 OR B = 1) AND C = 1 THEN PRINT "Sant!"
```

## Jämförelser ger 1 eller 0

En jämförelse är i själva verket ett litet räknestycke: sant blir `1` och falskt blir `0`. Därför kan du skriva ut den eller spara den i en variabel:

```basic
PRINT 10 > 3
PRINT 10 < 3
ALDER = 15
VUXEN = ALDER >= 18
PRINT VUXEN
```

Programmet skriver `1`, `0` och `0`.

## Ett tal som villkor

Står det bara ett tal eller en variabel som villkor räknas allt utom `0` som sant. Det är praktiskt för variabler som bara är 0 eller 1, så kallade **flaggor**:

```basic
HAR_NYCKEL = 1
IF HAR_NYCKEL THEN PRINT "Du låser upp dörren."
```

## Välja bland många: SELECT CASE

När samma variabel ska jämföras med många olika värden blir det rörigt med många `IF`. Då är `SELECT CASE` tydligare:

```basic
TARNING = INT(RND(6)) + 1
SELECT TARNING
CASE 1
  PRINT "En etta, otur!"
CASE 6
  PRINT "En sexa, slå igen!"
DEFAULT
  PRINT "Du slog " & TARNING
END SELECT
```

`SELECT` tar ett värde. Programmet letar upp den `CASE` som har samma värde och kör raderna under den, fram till nästa `CASE`. Finns ingen som passar körs raderna under `DEFAULT` (om det finns någon). Det fungerar lika bra med strängar:

```basic
RIKTNING$ = "NORR"
SELECT RIKTNING$
CASE "NORR"
  PRINT "Du går mot bergen."
CASE "SÖDER"
  PRINT "Du går mot sjön."
DEFAULT
  PRINT "Dit kan du inte gå."
END SELECT
```

> **Prova själv:** Fråga efter en ålder och skriv ut "barn" om den är under 13, "tonåring" om den är 13 – 19 och "vuxen" annars.

> **Prova själv:** Gör ett litet gissningsspel: datorn väljer ett tal mellan 1 och 10, du gissar en gång, och programmet säger om du gissade rätt, för högt eller för lågt.
