# Ett helt spel: Skogshopp

Nu har du alla delar som behövs för att göra ett riktigt spel. I det här kapitlet går vi igenom **Skogshopp**, ett plattformsspel som följer med AMOS Like Basic. Öppna `Projects/skogshopp.amosproj` och kör det medan du läser.

I Skogshopp styr du Kotte genom en skog. Du samlar ädelstenar, hoppar på skalbaggar och ska nå flaggan i slutet av banan. Spelet är ungefär 550 rader och använder nästan allt som beskrivits i den här delen av boken.

## Planera lagren

Det första beslutet är vilka lager spelet ska ha. Skogshopp har sex:

| Lager | Innehåll | Scrollar |
|---|---|---|
| 0 | himmel (raster bars), sol och berg | 1/10 av banans fart |
| 1 | moln | 1/6, och driver av sig själva |
| 2 | kullar | 1/4 |
| 3 | skog | 1/2 |
| 4 | banan med tiles, Kotte, fiender och ädelstenar | lika fort som kameran |
| 5 | poäng och text | scrollar inte |

Ju längre bort, desto långsammare. Det ger en känsla av djup.

## Bakgrunden ritas med kod

Ingen av bakgrunderna är en bild. De ritas med `BAR` och `CIRCLEF` när spelet startar. Bergen är summan av några sinusvågor, ritade som smala staplar:

```basic nocheck
PROC DrawSky
  SCREEN SELECT 0
  RASTER 1,0,0,320,"#1d4aa0,#3f7fdc,#8cc6ff,#ffe6bf"
  RASTER 2,0,320,160,"#ffe6bf,#ffcf96"
  INKG "#8d93cf"
  FOR X=0 TO 638 STEP 2
    H=SIN(X*0.5625)*30+SIN(X*1.6875+40)*16+SIN(X*3.375)*6
    BAR X,INT(250-H),X+1,479
  NEXT X
END PROC
```

Talen i `SIN` är valda så att vågorna går jämnt upp på 640 punkter (`0.5625 × 640 = 360`). Då passar högerkanten ihop med vänsterkanten, och lagret kan scrollas i all oändlighet utan skarv.

Moln och träd ritas på samma sätt med `CIRCLEF`. Ett moln eller träd nära kanten ritas en gång till på andra sidan, så att det också passar ihop:

```basic nocheck
IF CX<90 THEN Cloud CX+640,CY,CS
IF CX>550 THEN Cloud CX-640,CY,CS
```

## Banan

Banan är 120 rutor bred och 15 hög, och lagras som bokstäver i `DATA`-rader, en rad per rad på banan. `G` är gräs, `B` sten, `Q` bonusblock, `o` en ädelsten, `e` en skalbagge, `S` start och `F` flagga:

```basic nocheck
LevelData:
DATA "........oo....QBQ..............................."
DATA "GGGGGGGGGGGGGGGGGGGGGG...GGGGGGGGGGGGGG.........."
```

`LoadLevel` går igenom varje tecken. Tile-bokstäverna blir `TILE SET`, och ädelstenar och skalbaggar sparas i arrayer, som sedan blir sprites:

```basic nocheck
CH$=MID$(ROW$,C+1,1)
TI=INSTR(TILES$,CH$)-1
TILE SET C,R,TI
IF CH$="o" THEN GX(NG)=C*32+6 : GY(NG)=R*32+6 : GA(NG)=1 : NG=NG+1
IF CH$="e" THEN EX(NE)=C*32 : EY(NE)=R*32 : ED(NE)=-1 : EA(NE)=1 : NE=NE+1
```

Alla sprites, även Kotte, ligger i lager 4 tillsammans med banan, i banans koordinater. När lagret scrollas följer de med av sig själva.

## Huvudloopen

Spelet har fem lägen: titelskärm, spel, död, banan klar och game over. Variabeln `MODE` håller reda på vilket, och huvudloopen anropar rätt procedur:

```basic nocheck
MainLoop:
  T=T+1
  IF MODE=0 THEN TitleFrame
  IF MODE=1 THEN PlayFrame
  IF MODE=2 THEN DeathFrame
  IF MODE=3 THEN WinFrame
  IF MODE=4 THEN GameOverFrame
  UpdateGems
  UpdateBugs
  UpdateSparkle
  UpdateCamera
  DrawText
  WAIT VBL
GOTO MainLoop
```

Lägg märke till hur kort och läsbar den är. Allt detaljarbete sker i procedurerna.

## Fysik: fart, gravitation och hopp

Kotte har en position (`PX`, `PY`) och en fart (`VX`, `VY`). Varje bild läggs farten till positionen, och gravitationen ökar fallfarten lite:

```basic nocheck
VY=VY+GRAV
IF VY>MAXFALL THEN VY=MAXFALL
PY=PY+VY
```

Ett hopp är bara att sätta en stor fart uppåt: `VY=JUMPV`, där `JUMPV` är –13,5. Gravitationen bromsar sedan upp och drar ned Kotte igen, så att hoppet blir en båge. Släpper man hoppknappen tidigt minskas farten uppåt, vilket ger korta hopp:

```basic nocheck
IF JK=1 AND JHELD=0 AND ONG=1 THEN VY=JUMPV : ONG=0 : PLAY JUMP
IF JK=0 AND VY<-5 THEN VY=-5
JHELD=JK
```

`JHELD` är tricket från inmatningskapitlet: hoppet sker bara när knappen *trycks*, inte när den hålls.

## Kollisioner med banan

Funktionen `SolidAt` svarar på om det finns en fast tile på en punkt, med hjälp av `TILE()`:

```basic nocheck
FUNCTION SolidAt(X,Y)
  IF X<0 OR Y<0 THEN RETURN 0
  IF X>=WORLDW THEN RETURN 1
  ST=TILE(4,X,Y)
  IF ST>=0 AND ST<=6 THEN RETURN 1
  RETURN 0
END FUNCTION
```

Efter varje förflyttning testas några punkter runt Kotte. Faller han och fötterna hamnar i en fast tile, flyttas han upp så att han står precis på den, och farten nollas:

```basic nocheck
IF SolidAt(PX+8,PY+32)=1 OR SolidAt(PX+23,PY+32)=1 THEN PY=INT((PY+32)/32)*32-32 : VY=0 : ONG=1
```

`INT((PY+32)/32)*32` räknar ut var ovankanten på tilen finns. Samma idé används åt sidorna (väggar) och uppåt (taket och bonusblocken).

## Fiender

Skalbaggarna går fram och tillbaka och vänder när de möter en vägg eller en kant:

```basic nocheck
EX(B)=EX(B)+ED(B)*1.2
FX=EX(B)+16+ED(B)*14
IF SolidAt(FX,EY(B)+16)=1 OR SolidAt(FX,EY(B)+40)=0 THEN ED(B)=-ED(B) : SPRITE ZOOM BUGSPR+B,0-ED(B)*100,100
```

`ED` är riktningen, 1 eller –1. Punkten `FX` ligger strax framför baggen. Finns det en vägg där, eller *ingen* mark under den, vänder baggen, och spriten speglas med en negativ zoom så att den tittar åt rätt håll.

När Kotte nuddar en bagge avgör fallriktningen vad som händer. Faller han ovanifrån mosas baggen, annars förlorar Kotte ett liv.

## Kameran

Kameran försöker hålla Kotte en bit till vänster om mitten, men inte utanför banans kanter. Den följer mjukt efter genom att varje bild bara gå 18 % av vägen mot målet, och alla lager scrollas sedan i sin egen fart:

```basic nocheck
IF MODE=1 OR MODE=2 THEN CAMX=PX-280
IF CAMX<0 THEN CAMX=0
IF CAMX>WORLDW-640 THEN CAMX=WORLDW-640
SX=SX+(CAMX-SX)*0.18
SCROLL 0,SX/10,0
SCROLL 1,SX/6+T/4,0
SCROLL 2,SX/4,0
SCROLL 3,SX/2,0
SCROLL 4,SX,0
```

## Text och musik

Poängen skrivs om i varje bild med en egen font i lager 5, och titeln får en `WAVE`-effekt. Musiken är gjord i trackern och ligger i `Sound/Tracker`. Titelskärmen spelar den lugna `skogsmorgon.amostrk`, och när spelet börjar byter `NewGame` till `skogsvisa.amostrk`. Når Kotte flaggan spelas fanfaren `seger.amostrk`, och tar livet slut spelas `gameover.amostrk`. De två sista spelas en gång, med `TRACK LOOP 0`. Varje låt har en egen liten procedur, som `TitleMusic` och `GameMusic`. Öppna filerna med knappen **TRACKER** för att se hur de är gjorda. Samma Skogsvisa skriven med `PLAY`-kommandon finns i `Projects/skogsvisa.amosproj`.

## Grafiken

All grafik i Skogshopp är gjord med kod, i Python-skriptet `Graphics/Skogshopp/generate_assets.py`. Det ritar Kotte, skalbaggen, ädelstenarna, flaggan, alla tiles och fonten pixel för pixel. Banan och spelets kod finns i `Graphics/Skogshopp/build_game.py`, som skriver projektfilen. Vill du ändra banan, rita om den där och kör `python3 Graphics/Skogshopp/build_game.py`.

> **Prova själv:** Gör Kotte snabbare eller låt honom hoppa högre genom att ändra `MAXV` och `JUMPV` i början av programmet.

> **Prova själv:** Lägg till ett extra liv som belöning var tusende poäng.

> **Prova själv:** Gör en egen bana! Ändra bokstäverna i `DATA`-raderna. Tänk på att Kotte inte kan hoppa högre än ungefär fyra rutor eller längre än sex.
