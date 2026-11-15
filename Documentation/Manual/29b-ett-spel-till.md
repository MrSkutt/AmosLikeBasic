# Ett spel till: Grodhopp

**Grodhopp** är ett mindre spel än Skogshopp, men det visar några knep som Skogshopp inte använder. Öppna `Projects/grodhopp.amosproj` och kör det.

Grodan Kvack står längst ned och ska upp till toppen. Det finns sju golv, och i varje golv finns hål som glider åt sidan. Hoppar Kvack när ett hål är ovanför kommer den upp en våning. Finns det inget hål slår den i taket och blir yr en stund. Står den där ett hål kommer faller den ned en våning. Från bana 2 flyger getingar längs golven, och den som nuddar en geting förlorar ett liv.

Spelet har inga bilder alls. Allt ritas med `BAR` och `CIRCLEF` i varje bild, och ljuden och musiken är gjorda i trackern.

## Golv med hål

Hålen sparas i arrayer, ett golv i taget:

```basic nocheck
DIM LH(8)         ; antal hål i varje golv
DIM LS(8)         ; hålens fart i varje golv (minus = åt vänster)
DIM LX(8,3)       ; hålens x-position, upp till tre hål per golv
```

Alla hål i ett golv har samma fart, så de går aldrig ihop. Varje bild flyttas de, och när ett hål åker ut på ena sidan kommer det in på den andra:

```basic nocheck
X=LX(L,K)+LS(L)
IF X<0 THEN X=X+640
IF X>=640 THEN X=X-640
LX(L,K)=X
```

Ett golv ritas som bitar mellan hålen. Det svåra är hålet som just nu ligger över kanten, till exempel från x 620 till 680. Det täcker då både 620 – 639 och 0 – 39. Proceduren `DrawFloor` sorterar hålen från vänster och ritar bitarna mellan dem, och sist biten från det sista hålet runt kanten till det första.

## Får grodan plats i hålet?

Proceduren `InHole` svarar på frågan om det finns ett hål i golvet `L` där grodan får plats:

```basic nocheck
PROC InHole(L, X, M)
  HIT=0
  FOR K=0 TO LH(L)-1
    D=X-LX(L,K)
    IF D<0 THEN D=D+640
    IF D>=M AND D<=HW-M THEN HIT=1
  NEXT K
END PROC
```

`D` är hur långt in i hålet grodan är räknat från hålets vänsterkant. Att 640 läggs till när `D` blir negativt gör att det fungerar även när hålet ligger över kanten. `M` är en marginal. När grodan hoppar räcker det att den är 4 punkter in i hålet, men för att falla igenom ett hål måste den vara 10 punkter in, annars blir spelet för elakt.

Samma procedur används på två ställen:

- När grodan hoppar och huvudet når golvet ovanför frågar spelet `InHole PF+1, PX, 4`. Finns det ett hål fortsätter hoppet upp, annars studsar grodan ned och blir yr.
- När grodan står still frågar spelet `InHole PF, PX, 10` om golvet den står på. Finns det ett hål där faller den.

## Dubbelbuffring

Grodhopp suddar och ritar om allt i varje bild. Om man ritar i lagret som syns kan skärmen visas mitt i, när golven är ritade men inte grodan. Det ser ut som flimmer.

Lösningen kallas **dubbelbuffring** och användes redan på Amiga. Spelet har två lager för grafiken, 1 och 2. Det ritar alltid i det som inte syns, och byter sedan:

```basic nocheck
BACK=2-T MOD 2      ; 1 och 2 varannan bild
SCREEN SELECT BACK
CLSG
; ... rita allt ...
SCREEN ON BACK
SCREEN OFF 3-BACK
```

Så ser man bara färdiga bilder. Spel som flyttar sprites i stället för att rita om behöver inte göra så, eftersom sprites alltid visas hela.

## Ljud från trackern

Hopp, fall, bonk och resten kommer från ljudbanken med `TRACK SFX`. Ljuden spelas på kanal 5 – 8, så musiken på kanal 1 – 4 fortsätter:

```basic nocheck
TRACK SFX LOAD "Sound/Tracker/ljud.amostrk"
...
TRACK SFX "Hopp", "C-4"
```

Musiken är `grodhopp.amostrk`. På titelskärmen spelas den lite tystare, med `TRACK VOLUME 0.6`.

> **Prova själv:** Gör spelet lättare med bredare hål (`HW`) eller svårare med fler hål per golv i `SetupLevel`.

> **Prova själv:** Gör ett eget hoppljud i trackern: öppna `ljud.amostrk`, ändra tabellen för `Hopp` och spara. Hur låter `0, +4, +7, +12`?
