#!/usr/bin/env python3
"""Bygger Projects/skogshopp.amosproj av BASIC-koden och banan nedan.

    python3 Graphics/Skogshopp/build_game.py

Banan ritas som ASCII (120 x 15 rutor à 32x32 pixlar):

    .  tomt          G  gräs          D  jord          B  sten
    Q  bonusblock    L  stock         C  låda          ^  taggar
    b  buske         p  rosa blomma   v  blå blomma    m  svamp
    o  ädelsten      e  skalbagge     S  start         F  målflagga
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

W, H = 120, 15
GROUND = 13  # Översta raden av marken; man står på rad 12


def build_level():
    g = [["."] * W for _ in range(H)]

    def put(x, y, ch):
        g[y][x] = ch

    def ground(x0, x1):
        for x in range(x0, x1 + 1):
            put(x, GROUND, "G")
            put(x, GROUND + 1, "D")

    def row(x0, x1, y, ch):
        for x in range(x0, x1 + 1):
            put(x, y, ch)

    def column(x, y0, y1, ch):
        for y in range(y0, y1 + 1):
            put(x, y, ch)

    # ── Start: lugn början med blommor, ädelstenar och bonusblock ──
    ground(0, 21)
    put(3, 12, "S")
    for x, ch in ((6, "p"), (9, "v"), (12, "b"), (17, "m")):
        put(x, 12, ch)
    for x in range(7, 11):
        put(x, 9 if x in (8, 9) else 10, "o")
    row(14, 16, 9, "B")
    put(14, 9, "Q")
    put(16, 9, "Q")
    put(19, 12, "e")

    # ── Första gropen ──
    for x in range(22, 25):
        put(x, 10, "o")

    # ── Lådor, taggar och en stock ovanför ──
    ground(25, 38)
    put(28, 12, "C")
    column(29, 11, 12, "C")
    row(32, 33, 12, "^")
    for x, y in ((31, 10), (32, 9), (33, 9), (34, 10)):
        put(x, y, "o")
    put(37, 12, "e")
    put(26, 12, "v")

    # ── Svävande stenplattformar över en stor grop ──
    row(42, 44, 11, "B")
    row(47, 49, 9, "B")
    row(52, 54, 11, "B")
    for x in (43, 48, 53):
        put(x, 10 if x != 48 else 8, "o")
    put(48, 7, "o")
    put(48, 8, "e")

    # ── Bonusrad och en hög stock ──
    ground(55, 72)
    row(58, 62, 9, "B")
    for x in (59, 61):
        put(x, 9, "Q")
    row(60, 63, 5, "L")
    for x in range(60, 64):
        put(x, 4, "o")
    put(57, 12, "m")
    put(65, 12, "e")
    put(69, 12, "e")
    put(71, 12, "b")
    put(67, 12, "p")

    # ── Stentrappa upp och ner ──
    ground(73, 82)
    for i, x in enumerate(range(73, 77)):
        column(x, 12 - i, 12, "B")
    column(77, 9, 12, "B")
    column(78, 9, 12, "B")
    for i, x in enumerate(range(79, 82)):
        column(x, 10 + i, 12, "B")
    for x in range(74, 81):
        put(x, 6 if x in (77, 78) else 7, "o")

    # ── Lång stockbro över en djup grop ──
    row(85, 98, 10, "L")
    for x in range(87, 97, 2):
        put(x, 7, "o")
    put(92, 9, "e")

    # ── Slutet: lådtrappa och målflagga ──
    ground(99, 119)
    for i, x in enumerate(range(104, 108)):
        column(x, 12 - i, 12, "C")
    row(108, 110, 9, "C")
    for x in range(104, 111):
        put(x, 5 if x >= 108 else 8 - (x - 104), "o")
    put(102, 12, "e")
    put(100, 12, "b")
    put(116, 12, "p")
    put(117, 12, "v")
    put(113, 9, "F")
    column(119, 0, 12, "B")

    return ["".join(r) for r in g]


def check_level(rows):
    assert len(rows) == H
    for r in rows:
        assert len(r) == W, len(r)
    flat = "".join(rows)
    assert flat.count("S") == 1 and flat.count("F") == 1
    assert flat.count("e") <= 12, "max 12 skalbaggar"
    assert flat.count("o") <= 60, "max 60 ädelstenar"
    return flat.count("o"), flat.count("e")


PROGRAM = r'''
; ════════════════════════════════════════════════════════════════
;  SKOGSHOPP - ett plattformsspel i AMOS Like Basic
;
;  Hjälp Kotte genom skogen: samla ädelstenar, hoppa på
;  skalbaggarna och nå flaggan i slutet av banan.
;
;  Vänster/höger eller A/D: gå      Space, Upp eller W: hoppa
;  (Det går också bra med en handkontroll.)
;
;  Visar: sex lager med parallaxscroll, rasterhimmel, tilemap,
;  animerade och speglade sprites, fonteffekter, kollisioner
;  och chiptune-musik.
;
;  All grafik genereras av Graphics/Skogshopp/generate_assets.py
;  och banan nedan av Graphics/Skogshopp/build_game.py
; ════════════════════════════════════════════════════════════════

SCREEN 640,480

; ── Konstanter ──────────────────────────────────────────────────
LW=120 : LH=15 : WORLDW=LW*32
GRAV=0.6 : JUMPV=-13.5 : MAXFALL=11
ACC=0.45 : MAXV=4.2
TILES$="GDBQULC^bpvm"
GEMSPR=10 : BUGSPR=70 : FLAGSPR=2 : HUDGEM=3 : SPARKSPR=90 : PLAYER=99

DIM GX(60) : DIM GY(60) : DIM GA(60)
DIM EX(12) : DIM EY(12) : DIM ED(12) : DIM EA(12)
DIM SPK(6) : DIM SPX(6) : DIM SPY(6)

; ── Bygg världen ────────────────────────────────────────────────
DrawSky
DrawClouds
DrawHills
DrawForest
LoadGraphics
LoadLevel

MODE=0 : T=0 : SX=0 : HISCORE=0
SC=0 : GEMS=0 : LIVES=3
StartMusic

; ════════════════════════════════════════════════════════════════
;  Huvudloop
; ════════════════════════════════════════════════════════════════
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


; ════════════════════════════════════════════════════════════════
;  Lägen
; ════════════════════════════════════════════════════════════════
PROC TitleFrame
  ; Kameran glider fram och tillbaka över banan som demo
  CAMX=(1-COS(T*0.15))*(WORLDW-640)/2
  IF KEYSTATE("Space")=1 OR KEYSTATE("Return")=1 OR JOYSTATE(1,"A")=1 THEN NewGame
END PROC

PROC PlayFrame
  ReadInput
  MovePlayer
  AnimatePlayer
  ; Kontrollpunkt mitt på banan
  IF PX>56*32 THEN CPX=56*32 : CPY=11*32
  ; Målet
  IF PX+20>FLAGX AND PX<FLAGX+40 THEN Win
END PROC

PROC DeathFrame
  DT=DT+1
  VY=VY+GRAV
  PY=PY+VY
  SPRITE POS PLAYER,PX,PY
  IF DT>90 THEN AfterDeath
END PROC

PROC WinFrame
  WT=WT+1
  ; Kotte landar och skuttar av glädje
  TV=0 : JK=0
  IF WT MOD 40=20 THEN JK=1
  MovePlayer
  AnimatePlayer
  ; Fyrverkerier vid flaggan
  IF WT MOD 10=0 THEN Sparkle FLAGX-160+RND(320),90+RND(200)
  IF WT=60 THEN PLAY POWERUP
  IF WT>360 THEN EndRound
END PROC

PROC GameOverFrame
  WT=WT+1
  IF WT>240 THEN EndRound
END PROC


; ════════════════════════════════════════════════════════════════
;  Spelaren
; ════════════════════════════════════════════════════════════════
PROC ReadInput
  TV=0
  IF KEYSTATE("Left")=1 OR KEYSTATE("A")=1 OR JOYSTATE(1,"Left")=1 THEN TV=-MAXV
  IF KEYSTATE("Right")=1 OR KEYSTATE("D")=1 OR JOYSTATE(1,"Right")=1 THEN TV=MAXV
  JK=0
  IF KEYSTATE("Space")=1 OR KEYSTATE("Up")=1 OR KEYSTATE("W")=1 OR JOYSTATE(1,"A")=1 THEN JK=1
END PROC

PROC MovePlayer
  ; Acceleration mot önskad fart
  IF VX<TV THEN VX=MIN(VX+ACC,TV)
  IF VX>TV THEN VX=MAX(VX-ACC,TV)

  ; ── Vågrätt, med väggar ──
  PX=PX+VX
  IF PX<0 THEN PX=0 : VX=0
  IF VX>0 THEN
    IF SolidAt(PX+25,PY+6)=1 OR SolidAt(PX+25,PY+30)=1 THEN PX=INT((PX+25)/32)*32-26 : VX=0
  ENDIF
  IF VX<0 THEN
    IF SolidAt(PX+6,PY+6)=1 OR SolidAt(PX+6,PY+30)=1 THEN PX=INT((PX+6)/32)*32+26 : VX=0
  ENDIF

  ; ── Hopp: bara när knappen trycks ned, inte när den hålls ──
  IF JK=1 AND JHELD=0 AND ONG=1 THEN VY=JUMPV : ONG=0 : PLAY JUMP
  ; Släpp knappen tidigt = lägre hopp
  IF JK=0 AND VY<-5 THEN VY=-5
  JHELD=JK

  ; ── Lodrätt, med golv och tak ──
  VY=VY+GRAV
  IF VY>MAXFALL THEN VY=MAXFALL
  PY=PY+VY
  ONG=0
  IF VY>=0 THEN
    IF SolidAt(PX+8,PY+32)=1 OR SolidAt(PX+23,PY+32)=1 THEN PY=INT((PY+32)/32)*32-32 : VY=0 : ONG=1
  ELSE
    IF SolidAt(PX+8,PY+4)=1 OR SolidAt(PX+23,PY+4)=1 THEN HeadBump
  ENDIF

  ; ── Faror ──
  IF TILE(4,PX+16,PY+28)=7 THEN Die
  IF PY>480 THEN Die
END PROC

PROC HeadBump
  BCX=INT((PX+16)/32) : BCY=INT((PY+4)/32)
  PY=BCY*32+28 : VY=1
  IF TILE(4,BCX*32,BCY*32)=3 THEN
    ; Bonusblock: byt till använt block, ge poäng och en ädelsten
    TILE SET BCX,BCY,4
    TILE DRAW #4,0,0
    SC=SC+50 : GEMS=GEMS+1
    Sparkle BCX*32+4,BCY*32-28
    PLAY COIN
  ELSE
    PLAY BLIP
  ENDIF
END PROC

PROC AnimatePlayer
  FR=0
  IF ABS(VX)>0.5 THEN STEPS=STEPS+ABS(VX) : FR=1+INT(STEPS/14) MOD 3
  IF ONG=0 THEN FR=4
  SPRITE FRAME PLAYER,FR
  ; Spegla figuren när den går åt vänster
  NF=FACE
  IF VX>0.3 THEN NF=1
  IF VX<-0.3 THEN NF=-1
  IF NF<>FACE THEN FACE=NF : SPRITE ZOOM PLAYER,FACE*100,100
  ; Blinka en stund efter start
  IF SAFE>0 THEN SAFE=SAFE-1 : SPRITE ALPHA PLAYER,120+(SAFE MOD 8)*16
  IF SAFE=1 THEN SPRITE ALPHA PLAYER,255
  SPRITE POS PLAYER,PX,PY
END PROC

PROC Die
  MODE=2 : DT=0 : VY=-10
  SPRITE FRAME PLAYER,5
  PLAY EXPLOSION
END PROC

PROC AfterDeath
  LIVES=LIVES-1
  IF LIVES<=0 THEN
    MODE=4 : WT=0
    SPRITE OFF PLAYER
  ELSE
    Spawn
  ENDIF
END PROC

PROC Win
  MODE=3 : WT=0
  SC=SC+1000+GEMS*10
  SPRITE FRAME PLAYER,4
  PLAY COIN
END PROC

PROC EndRound
  IF SC>HISCORE THEN HISCORE=SC
  MODE=0
  SPRITE OFF PLAYER
END PROC

PROC NewGame
  SC=0 : GEMS=0 : LIVES=3
  LoadLevel
  CPX=STARTX : CPY=STARTY
  Spawn
END PROC

PROC Spawn
  PX=CPX : PY=CPY : VX=0 : VY=0 : ONG=0 : JHELD=1
  FACE=1 : SAFE=90
  SPRITE ZOOM PLAYER,100,100
  SPRITE FRAME PLAYER,0
  SPRITE POS PLAYER,PX,PY
  SPRITE ON PLAYER
  MODE=1
END PROC

FUNCTION SolidAt(X,Y)
  IF X<0 OR Y<0 THEN RETURN 0
  IF X>=WORLDW THEN RETURN 1
  ST=TILE(4,X,Y)
  IF ST>=0 AND ST<=6 THEN RETURN 1
  RETURN 0
END FUNCTION


; ════════════════════════════════════════════════════════════════
;  Ädelstenar, skalbaggar och glitter
; ════════════════════════════════════════════════════════════════
PROC UpdateGems
  ; Alla stenar snurrar i takt
  IF T MOD 6=0 THEN
    GF=(T/6) MOD 4
    FOR I=0 TO NG-1
      IF GA(I)=1 THEN SPRITE IMAGE GEMSPR+I,1,GF
    NEXT I
  ENDIF
  IF MODE<>1 THEN EXIT PROC
  FOR I=0 TO NG-1
    IF GA(I)=1 THEN
      IF ABS(GX(I)-PX-6)<20 AND ABS(GY(I)-PY-6)<24 THEN GA(I)=0 : SPRITE OFF GEMSPR+I : SC=SC+10 : GEMS=GEMS+1 : PLAY COIN
    ENDIF
  NEXT I
END PROC

PROC UpdateBugs
  BF=INT(T/8) MOD 2
  FOR I=0 TO NE-1
    ; Skalbaggar långt utanför bild står still
    IF EA(I)=1 AND ABS(EX(I)-SX-320)<480 THEN MoveBug I
    IF EA(I)<0 THEN
      ; Mosad: ligger kvar en stund och tonar bort
      EA(I)=EA(I)+1
      IF EA(I)=0 THEN SPRITE OFF BUGSPR+I
    ENDIF
  NEXT I
END PROC

PROC MoveBug(B)
  EX(B)=EX(B)+ED(B)*1.2
  ; Vänd vid väggar och kanter
  FX=EX(B)+16+ED(B)*14
  IF SolidAt(FX,EY(B)+16)=1 OR SolidAt(FX,EY(B)+40)=0 THEN ED(B)=-ED(B) : SPRITE ZOOM BUGSPR+B,0-ED(B)*100,100
  SPRITE IMAGE BUGSPR+B,2,BF
  SPRITE POS BUGSPR+B,EX(B),EY(B)
  IF MODE<>1 THEN EXIT PROC
  IF ABS(EX(B)-PX)<24 AND ABS(EY(B)-PY)<28 THEN
    ; Landar man ovanpå mosas baggen, annars blir man skadad
    IF VY>0 AND PY+20<EY(B) THEN
      EA(B)=-30 : VY=-9 : SC=SC+100
      SPRITE IMAGE BUGSPR+B,2,2
      SPRITE FADE BUGSPR+B,0,30
      PLAY HIT
    ELSE
      IF SAFE=0 THEN Die
    ENDIF
  ENDIF
END PROC

; Sex glittersprites som återanvänds i tur och ordning
PROC Sparkle(KX,KY)
  SPN=(SPN+1) MOD 6
  SPK(SPN)=18 : SPX(SPN)=KX : SPY(SPN)=KY
  SPRITE IMAGE SPARKSPR+SPN,4,0
  SPRITE LAYER SPARKSPR+SPN,4
  SPRITE ZOOM SPARKSPR+SPN,200,200
  SPRITE POS SPARKSPR+SPN,KX,KY
  SPRITE ON SPARKSPR+SPN
END PROC

PROC UpdateSparkle
  FOR I=0 TO 5
    IF SPK(I)>0 THEN
      SPK(I)=SPK(I)-1
      SPY(I)=SPY(I)-1
      SPRITE IMAGE SPARKSPR+I,4,MIN(2,INT((18-SPK(I))/5))
      SPRITE POS SPARKSPR+I,SPX(I),SPY(I)
      IF SPK(I)=0 THEN SPRITE OFF SPARKSPR+I
    ENDIF
  NEXT I
END PROC


; ════════════════════════════════════════════════════════════════
;  Kamera och parallax
; ════════════════════════════════════════════════════════════════
PROC UpdateCamera
  IF MODE=1 OR MODE=2 THEN CAMX=PX-280
  IF CAMX<0 THEN CAMX=0
  IF CAMX>WORLDW-640 THEN CAMX=WORLDW-640
  ; Mjuk följning
  SX=SX+(CAMX-SX)*0.18
  ; Längre bort = långsammare. Molnen driver dessutom av sig själva.
  SCROLL 0,SX/10,0
  SCROLL 1,SX/6+T/4,0
  SCROLL 2,SX/4,0
  SCROLL 3,SX/2,0
  SCROLL 4,SX,0
END PROC


; ════════════════════════════════════════════════════════════════
;  Text (ritas om varje frame på lager 5, som inte scrollar)
; ════════════════════════════════════════════════════════════════
PROC DrawText
  FONT CLEAR
  IF MODE=0 THEN
    FONT PRINT #5,1,96,70,"SKOGSHOPP"
    IF INT(T/30) MOD 2=0 THEN
      TX$="TRYCK SPACE FÖR ATT SPELA"
      FONT PRINT #5,0,8,300,TX$
    ENDIF
    TX$="REKORD " & HISCORE
    FONT PRINT #5,0,200,420,TX$
    SPRITE OFF HUDGEM
    EXIT PROC
  ENDIF
  TX$="POÄNG " & SC
  FONT PRINT #5,0,12,8,TX$
  SPRITE ON HUDGEM
  TX$="X" & GEMS
  FONT PRINT #5,0,322,8,TX$
  TX$="LIV " & LIVES
  FONT PRINT #5,0,500,8,TX$
  IF MODE=3 THEN FONT PRINT #5,1,64,150,"BANAN KLAR!"
  IF MODE=4 THEN FONT PRINT #5,1,64,150,"SPELET SLUT"
END PROC


; ════════════════════════════════════════════════════════════════
;  Musik: en egen liten trudelutt på tre kanaler
; ════════════════════════════════════════════════════════════════
PROC StartMusic
  ; "Skogsvisa": 16 takter i G-dur som loopar.
  ; Det inom {} i noterna är effekter som ändrar ljudet medan låten spelar.
  PLAY BPM 136
  ; Kanal 1: melodi med pulsvåg, vibrato och pulsbreddsmodulering
  PLAY WAVE 1,"Pulse"
  PLAY ADSR 1,5,150,60,120
  PLAY VOLUME 1,0.2
  ; Kanal 2: bas med sågtand genom ett filter som öppnas vid varje ton
  PLAY WAVE 2,"Sawtooth"
  PLAY FILTER 2,"LOW",250,60
  PLAY FILTERENV 2,1800,2,150
  PLAY ADSR 2,2,0,100,50
  PLAY GATE 2,70
  PLAY VOLUME 2,0.28
  ; Kanal 3: snabba arpeggion som ackord, som på C64
  PLAY WAVE 3,"Pulse"
  PLAY PULSE 3,25
  PLAY ADSR 3,10,400,35,200
  PLAY VOLUME 3,0.09
  ; Kanal 4: trummor
  PLAY WAVE 4,"Drums"
  PLAY VOLUME 4,0.35

  ; Del A
  LA$="{PULSE 30 12 1.5; GLIDE 0; VIBRATO 12 5} G5:8 D5 G5 A5 B5:4 A5:8 G5 F#5:4 A5:8 F#5 D5:4 R:4 E5:8 G5 B5 G5 E5 G5 B5 C6 C6:4."
  LA$=LA$+" B5:8 A5:4 G5:4 B5:8 A5 G5 D5 G5:4 B5:4 A5:8 B5 A5 F#5 D5:4 F#5:4 E5:8 G5 C6 E6 D6:4 C6:8 B5 {VIBRATO 35 6} A5:2. {VIBRATO 12 5} R:4"
  BA$="{SWEEP 250 300} G2:8 G2 G3 G2 G2 G3 G2 G3 D2:8 D2 D3 D2 D2 D3 D2 D3 E2:8 E2 E3 E2 E2 E3 E2 E3 C2:8 C2 C3 C2 C2 C3"
  BA$=BA$+" C2 C3 G2:8 G2 G3 G2 G2 G3 G2 G3 D2:8 D2 D3 D2 D2 D3 D2 D3 C2:8 C2 C3 C2 C2 C3 C2 C3 D2:8 D2 D3 D2 D2 D3 D2 D3"
  AA$="{ARP 0 4 7} G3:1 {ARP 0 4 7} D4:1 {ARP 0 3 7} E4:1 {ARP 0 4 7} C4:1"
  AA$=AA$+" {ARP 0 4 7} G3:1 {ARP 0 4 7} D4:1 {ARP 0 4 7} C4:1 {ARP 0 4 7} D4:1"
  DA$="CY:8 HH SD HH BD BD SD HH BD:8 HH SD HH BD BD SD HH BD:8 HH SD HH BD BD SD HH BD:8 HH SD HH BD BD SD HH"
  DA$=DA$+" BD:8 HH SD HH BD BD SD HH BD:8 HH SD HH BD BD SD HH BD:8 HH SD HH BD BD SD HH BD:8 HH SD HH BD BD SD HH"
  ; Del B: tunnare melodi som glider, och basens filter öppnas sakta
  LB$="{PULSE 50; GLIDE 40} E6:4 B5:8 G5 E6:4 F#6:8 G6 E6:4. D6:8 C6:4 B5:4 B5:8 C6 D6 B5 G5:4 D6:4 C6:8 B5 A5"
  LB$=LB$+" F#5 A5:2 E6:4 B5:8 G5 E6:4 G6:4 F#6:8 E6 D6 C6 E6:4 C6:4 D6:8 C6 B5 A5 F#5:4 A5:4 {GLIDE 120} D5:8 D6:4. R:2"
  BB$="{SWEEP 1400 6000} E2:8 E2 E3 E2 E2 E3 E2 E3 C2:8 C2 C3 C2 C2 C3 C2 C3 G2:8 G2 G3 G2 G2 G3 G2 G3 D2:8 D2 D3 D2 D2 D3"
  BB$=BB$+" D2 D3 E2:8 E2 E3 E2 E2 E3 E2 E3 C2:8 C2 C3 C2 C2 C3 C2 C3 D2:8 D2 D3 D2 D2 D3 D2 D3 D2:8 D2 D3 D2 D2 D3 D2 D3"
  AB$="{ARP 0 3 7 12} E4:1 {ARP 0 4 7 12} C4:1 {ARP 0 4 7 12} G3:1 {ARP 0 4 7 12} D4:1"
  AB$=AB$+" {ARP 0 3 7 12} E4:1 {ARP 0 4 7 12} C4:1 {ARP 0 4 7 12} D4:1 {ARP 0 4 7 12} D4:1"
  DB$="BD:8 HH SD HH BD HH SD OH BD:8 HH SD HH BD HH SD OH BD:8 HH SD HH BD HH SD OH BD:8 HH SD HH BD HH SD OH BD:8 HH SD"
  DB$=DB$+" HH BD HH SD OH BD:8 HH SD HH BD HH SD OH BD:8 HH SD HH BD HH SD OH BD:16 BD SD SD TM TM SD SD BD:8 HH SD:16 SD SD SD"

  PLAY LOOP 1,LA$+" "+LB$
  PLAY LOOP 2,BA$+" "+BB$
  PLAY LOOP 3,AA$+" "+AB$
  PLAY LOOP 4,DA$+" "+DB$
END PROC


; ════════════════════════════════════════════════════════════════
;  Bakgrundslagren ritas en gång med BASIC-kommandon.
;  Varje lager är 640 pixlar brett och upprepas när det scrollar,
;  så allt ritas så att högerkanten passar ihop med vänsterkanten.
; ════════════════════════════════════════════════════════════════
PROC DrawSky
  SCREEN SELECT 0
  ; Himlen är rasterfärger, som ligger kvar medan bergen scrollar
  RASTER 1,0,0,320,"#1d4aa0,#3f7fdc,#8cc6ff,#ffe6bf"
  RASTER 2,0,320,160,"#ffe6bf,#ffcf96"
  INKG "#fff4c4"
  CIRCLEF 470,110,44,44
  INKG "#fffbe6"
  CIRCLEF 470,110,34,34
  ; Blåa berg långt bort (summan av sinusvågor som går jämnt upp på 640 pixlar)
  INKG "#8d93cf"
  FOR X=0 TO 638 STEP 2
    H=SIN(X*0.5625)*30+SIN(X*1.6875+40)*16+SIN(X*3.375)*6
    BAR X,INT(250-H),X+1,479
  NEXT X
  INKG "#eef0ff"
  FOR X=0 TO 638 STEP 2
    H=SIN(X*0.5625)*30+SIN(X*1.6875+40)*16+SIN(X*3.375)*6
    IF H>26 THEN BAR X,INT(250-H),X+1,INT(250-H+(H-26)/2+2)
  NEXT X
  INKG "#6c72b4"
  FOR X=0 TO 638 STEP 2
    H=SIN(X*1.125+120)*18+SIN(X*2.8125)*8
    BAR X,INT(300-H),X+1,479
  NEXT X
END PROC

PROC DrawClouds
  SCREEN SELECT 1
  RESTORE CloudData
  FOR I=1 TO 7
    READ CX,CY,CS
    Cloud CX,CY,CS
    IF CX<90 THEN Cloud CX+640,CY,CS
    IF CX>550 THEN Cloud CX-640,CY,CS
  NEXT I
END PROC

PROC Cloud(CX,CY,CS)
  INKG "#dce9f7"
  CIRCLEF CX,CY+CS/3,CS*2,CS/2
  INKG "#ffffff"
  CIRCLEF CX-CS,CY,CS*0.75,CS*0.6
  CIRCLEF CX,CY-CS/3,CS,CS*0.8
  CIRCLEF CX+CS,CY,CS*0.8,CS*0.6
END PROC

PROC DrawHills
  SCREEN SELECT 2
  INKG "#4e9a5c"
  FOR X=0 TO 638 STEP 2
    H=SIN(X*1.125)*24+SIN(X*2.25+70)*12
    BAR X,INT(330-H),X+1,479
  NEXT X
  INKG "#7cc571"
  FOR X=0 TO 638 STEP 2
    H=SIN(X*1.125)*24+SIN(X*2.25+70)*12
    BAR X,INT(330-H),X+1,INT(333-H)
  NEXT X
END PROC

PROC DrawForest
  SCREEN SELECT 3
  INKG "#1f4a2c"
  BAR 0,410,639,479
  RESTORE TreeData
  FOR I=1 TO 9
    READ TX,TY,TS
    Tree TX,TY,TS
    IF TX<60 THEN Tree TX+640,TY,TS
    IF TX>580 THEN Tree TX-640,TY,TS
  NEXT I
END PROC

PROC Tree(TX,TY,TS)
  INKG "#5d3f27"
  BAR TX-4,TY,TX+4,420
  INKG "#245c33"
  CIRCLEF TX,TY,TS,TS
  CIRCLEF TX-TS*0.6,TY+TS*0.5,TS*0.8,TS*0.7
  CIRCLEF TX+TS*0.6,TY+TS*0.5,TS*0.8,TS*0.7
  INKG "#33793f"
  CIRCLEF TX-TS*0.3,TY-TS*0.3,TS*0.5,TS*0.45
END PROC

CloudData:
DATA 60,70,22, 200,120,16, 330,60,26, 450,150,14, 560,90,20, 140,180,12, 400,200,10

TreeData:
DATA 30,340,34, 100,360,26, 175,330,38, 250,355,28, 330,345,32, 400,365,24, 470,335,36, 545,355,28, 615,345,30


; ════════════════════════════════════════════════════════════════
;  Grafik och bana
; ════════════════════════════════════════════════════════════════
PROC LoadGraphics
  IMAGE LOAD 1,"Graphics/Skogshopp/gem.png",20,20,4
  IMAGE LOAD 2,"Graphics/Skogshopp/bagge.png",32,32,3
  IMAGE LOAD 3,"Graphics/Skogshopp/flag.png",48,128,2
  IMAGE LOAD 4,"Graphics/Skogshopp/sparkle.png",24,24,3
  SPRITE LOAD PLAYER,"Graphics/Skogshopp/kotte.png",32,32,6
  SPRITE LAYER PLAYER,4
  SPRITE IMAGE FLAGSPR,3,0
  SPRITE LAYER FLAGSPR,4
  ; Ädelstenen bredvid antalet i poängraden
  SPRITE IMAGE HUDGEM,1,0
  SPRITE LAYER HUDGEM,5
  SPRITE POS HUDGEM,296,12
  ; Texter: font 0 för poäng, font 1 för stora rubriker med vågeffekt
  FONT LOAD 0,"Graphics/Skogshopp/font.png",24,32
  FONT MAP 0,"ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
  FONT LOAD 1,"Graphics/Skogshopp/font.png",24,32
  FONT MAP 1,"ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
  FONT ZOOM 1,200
  FONT EFFECT ADD 1,WAVE,0,0,16,0.02
  SCREEN SELECT 4
  TILE MAP LW,LH
  TILE LOAD "Graphics/Skogshopp/tiles.png",32,32
END PROC

PROC LoadLevel
  SCREEN SELECT 4
  NG=0 : NE=0
  RESTORE LevelData
  FOR R=0 TO LH-1
    READ ROW$
    FOR C=0 TO LW-1
      CH$=MID$(ROW$,C+1,1)
      TI=INSTR(TILES$,CH$)-1
      TILE SET C,R,TI
      IF CH$="o" THEN GX(NG)=C*32+6 : GY(NG)=R*32+6 : GA(NG)=1 : NG=NG+1
      IF CH$="e" THEN EX(NE)=C*32 : EY(NE)=R*32 : ED(NE)=-1 : EA(NE)=1 : NE=NE+1
      IF CH$="S" THEN STARTX=C*32 : STARTY=R*32
      IF CH$="F" THEN FLAGX=C*32 : FLAGY=R*32
    NEXT C
  NEXT R
  TILE DRAW #4,0,0
  FOR I=0 TO NG-1
    SPRITE IMAGE GEMSPR+I,1,0
    SPRITE LAYER GEMSPR+I,4
    SPRITE POS GEMSPR+I,GX(I),GY(I)
    SPRITE ON GEMSPR+I
  NEXT I
  FOR I=0 TO NE-1
    SPRITE IMAGE BUGSPR+I,2,0
    SPRITE LAYER BUGSPR+I,4
    SPRITE ZOOM BUGSPR+I,100,100
    SPRITE ALPHA BUGSPR+I,255
    SPRITE POS BUGSPR+I,EX(I),EY(I)
    SPRITE ON BUGSPR+I
  NEXT I
  SPRITE POS FLAGSPR,FLAGX,FLAGY
  SPRITE ON FLAGSPR
  CPX=STARTX : CPY=STARTY
END PROC

LevelData:
@@LEVEL@@
'''


def main():
    rows = build_level()
    gems, bugs = check_level(rows)
    data = "\n".join(f'DATA "{r}"' for r in rows)
    text = PROGRAM.replace("@@LEVEL@@", data).lstrip("\n")
    path = os.path.join(ROOT, "Projects", "skogshopp.amosproj")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"Version": 2, "ProgramText": text}, f, ensure_ascii=False)
    print(f"wrote {path}: {len(text.splitlines())} lines, {gems} gems, {bugs} bugs")
    for r in rows:
        print(r)


if __name__ == "__main__":
    main()
