# Loopar

En **loop** upprepar kod. Datorer är bra på att göra samma sak många gånger utan att tröttna, och nästan alla spel är byggda kring en loop som körs om och om igen, en gång per bild på skärmen.

## FOR ... NEXT

`FOR` räknar en variabel från ett tal till ett annat och kör koden fram till `NEXT` en gång för varje värde:

```basic
FOR I = 1 TO 5
  PRINT "Varv nummer " & I
NEXT I
```

`I` blir först 1, sedan 2, 3, 4 och till sist 5. Efter `NEXT I` fortsätter programmet med raden efter loopen.

Med `STEP` bestämmer du hur mycket variabeln ändras varje varv. Ett negativt steg räknar baklänges:

```basic
FOR I = 0 TO 100 STEP 25
  PRINT I
NEXT I

FOR NEDRAKNING = 10 TO 1 STEP -1
  PRINT NEDRAKNING
NEXT NEDRAKNING
PRINT "LYFT!"
```

> **Obs:** `FOR` räknar bara med heltal. Start, slut och steg avrundas till närmaste heltal, så både `STEP 0.5` och `STEP 0.1` blir steg 0, vilket ger en loop som aldrig tar slut. Behöver du decimaler, räkna med heltal och dela: `FOR I = 0 TO 10` och använd `I / 10`.

Loopar kan ligga i varandra. Här skrivs en multiplikationstabell ut:

```basic
FOR RAD = 1 TO 5
  UT$ = ""
  FOR KOL = 1 TO 5
    UT$ = UT$ & (RAD * KOL) & "  "
  NEXT KOL
  PRINT UT$
NEXT RAD
```

## WHILE ... WEND

`WHILE` upprepar så länge ett villkor är sant. Villkoret kontrolleras *innan* varje varv, så är det falskt från början körs loopen inte alls:

```basic
PENGAR = 100
DAGAR = 0
WHILE PENGAR > 0
  PENGAR = PENGAR - 15
  DAGAR = DAGAR + 1
WEND
PRINT "Pengarna räckte i " & DAGAR & " dagar"
```

## REPEAT ... UNTIL

`REPEAT` upprepar *tills* ett villkor blir sant. Villkoret kontrolleras *efter* varje varv, så loopen körs alltid minst en gång:

```basic
REPEAT
  TARNING = INT(RND(6)) + 1
  PRINT "Du slog " & TARNING
UNTIL TARNING = 6
PRINT "Äntligen en sexa!"
```

## DO ... LOOP

`DO ... LOOP` upprepar för evigt. För att komma ut skriver du `EXIT DO`:

```basic
X = 1
DO
  X = X * 2
  IF X > 1000 THEN EXIT DO
LOOP
PRINT "Första tvåpotensen över 1000 är " & X
```

På samma sätt lämnar `EXIT WHILE` en `WHILE`-loop och `EXIT REPEAT` en `REPEAT`-loop.

## Hopp med GOTO

Du kan sätta en **etikett** på en rad: ett namn följt av kolon, ensamt på raden. Med `GOTO` hoppar programmet dit:

```basic
RAKNARE = 0
Igen:
  RAKNARE = RAKNARE + 1
  PRINT "Varv " & RAKNARE
  IF RAKNARE < 3 THEN GOTO Igen
PRINT "Klart"
```

Ett spel har nästan alltid en huvudloop som ser ut så här. Etiketten och `GOTO` bildar en loop som aldrig tar slut, och `WAIT VBL` väntar på nästa bild (mer om det i grafikdelen):

```basic nocheck
HuvudLoop:
  ; läs tangenter, flytta figurer, rita
  WAIT VBL
GOTO HuvudLoop
```

> **Tips:** Använd `GOTO` sparsamt. Program med många hopp kors och tvärs blir svåra att följa. `FOR`, `WHILE`, `REPEAT` och procedurer (nästa kapitel) är nästan alltid tydligare. Huvudloopen i ett spel är det klassiska undantaget.

## En loop som räknar tiden

`MILLIS()` ger antalet millisekunder sedan programmet startade. Med den kan du mäta hur lång tid något tar:

```basic
START = MILLIS()
SUMMA = 0
FOR I = 1 TO 100000
  SUMMA = SUMMA + I
NEXT I
PRINT "Summan är " & SUMMA
PRINT "Det tog " & (MILLIS() - START) & " ms"
```

> **Prova själv:** Skriv ut alla tal mellan 1 och 100 som är jämnt delbara med 7. Tips: `I MOD 7 = 0`.

> **Prova själv:** Gör om gissningsspelet från förra kapitlet så att man får gissa tills man har rätt, och skriv ut hur många gissningar det tog.
