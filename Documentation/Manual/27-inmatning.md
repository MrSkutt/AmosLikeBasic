# Tangentbord och handkontroll

Ett spel måste kunna styras. Det finns två sätt att läsa av tangentbordet: fråga om en viss tangent är nedtryckt just nu, eller ta emot den senast tryckta.

## KEYSTATE: är tangenten nedtryckt?

`KEYSTATE("namn")` ger 1 så länge tangenten hålls nere och 0 annars. Det är rätt sätt att styra en figur, eftersom den rör sig så länge du håller in tangenten:

```basic
SPRITE LOAD 1, "Graphics/Skogshopp/kotte.png", 32, 32, 6
SPRITE ON 1
X = 300 : Y = 220
Loop:
  IF KEYSTATE("Left") = 1 THEN X = X - 4
  IF KEYSTATE("Right") = 1 THEN X = X + 4
  IF KEYSTATE("Up") = 1 THEN Y = Y - 4
  IF KEYSTATE("Down") = 1 THEN Y = Y + 4
  SPRITE POS 1, X, Y
  WAIT VBL
GOTO Loop
```

Klicka i spelfönstret först, så att det tar emot tangenterna.

### Tangentnamn

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
| funktionstangenter | `F1` till `F11` (`F12` växlar helskärm) |

### Ett tryck, inte ett håll

`KEYSTATE` säger bara om tangenten är nere. För ett hopp vill man reagera på själva *trycket*, inte hoppa om och om igen så länge tangenten hålls. Det löser man genom att komma ihåg hur det var förra gången:

```basic nocheck
NU = KEYSTATE("Space")
IF NU = 1 AND FORRA = 0 THEN PLAY JUMP
FORRA = NU
```

## INKEY$: senast tryckta tangent

`INKEY$` ger namnet på en tangent som är nedtryckt, eller en tom sträng om ingen är det. Den passar för menyer och "tryck på valfri tangent":

```basic nocheck
PRINT "Tryck på en tangent..."
REPEAT
  K$ = INKEY$
  WAIT VBL
UNTIL K$ <> ""
PRINT "Du tryckte på " & K$
```

## Skriva in text i grafiken: INPUTG

`INPUT` (från del 1) frågar i textkonsolen. `INPUTG` gör samma sak men skriver i grafiken, med den font och färg du har valt för `PRINTG`:

```basic nocheck
INPUTG "Ditt namn: ", NAMN$
PRINTG "Välkommen " & NAMN$
```

## Handkontroll

Har du en handkontroll (till exempel Xbox, PlayStation eller Switch Pro) inkopplad kan du läsa av den på samma sätt. Upp till fyra handkontroller stöds, numrerade 1 – 4.

`JOYSTATE(kontroll, "knapp")` ger 1 när knappen är nedtryckt:

| Knapp | Namn |
|---|---|
| A, B, X, Y | `A`, `B`, `X`, `Y` |
| axelknappar | `LB`, `RB` |
| start och select | `Start`, `Select` |
| styrkorset | `Up`, `Down`, `Left`, `Right` |

`JOYAXIS(kontroll, "axel")` ger hur långt en spak är tryckt, från –100 till 100 (avtryckarna 0 till 100):

| Axel | Namn |
|---|---|
| vänster spak | `LeftX`, `LeftY` |
| höger spak | `RightX`, `RightY` |
| avtryckare | `LT`, `RT` |

Det är vanligt att stödja både tangentbord och handkontroll:

```basic nocheck
IF KEYSTATE("Right") = 1 OR JOYSTATE(1, "Right") = 1 OR JOYAXIS(1, "LeftX") > 30 THEN X = X + 4
```

> **Prova själv:** Bygg vidare på exemplet med `KEYSTATE` så att figuren inte kan åka utanför skärmen. Tips: `MIN` och `MAX`.
