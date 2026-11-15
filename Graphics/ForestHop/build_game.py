#!/usr/bin/env python3
"""Builds Projects/foresthop.amosproj from the BASIC code and the level below.

    python3 Graphics/ForestHop/build_game.py

The level is drawn as ASCII (120 x 15 tiles of 32x32 pixels):

    .  empty         G  grass         D  dirt          B  stone
    Q  bonus block   L  log           C  crate         ^  spikes
    b  bush          p  pink flower   v  blue flower   m  mushroom
    o  gem           e  beetle        S  start         F  goal flag
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

W, H = 120, 15
GROUND = 13  # Top row of the ground; you stand on row 12


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

    # ── Start: a calm beginning with flowers, gems and bonus blocks ──
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

    # ── The first pit ──
    for x in range(22, 25):
        put(x, 10, "o")

    # ── Crates, spikes and a log above ──
    ground(25, 38)
    put(28, 12, "C")
    column(29, 11, 12, "C")
    row(32, 33, 12, "^")
    for x, y in ((31, 10), (32, 9), (33, 9), (34, 10)):
        put(x, y, "o")
    put(37, 12, "e")
    put(26, 12, "v")

    # ── Floating stone platforms over a big pit ──
    row(42, 44, 11, "B")
    row(47, 49, 9, "B")
    row(52, 54, 11, "B")
    for x in (43, 48, 53):
        put(x, 10 if x != 48 else 8, "o")
    put(48, 7, "o")
    put(48, 8, "e")

    # ── A row of bonus blocks and a high log ──
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

    # ── Stone stairs up and down ──
    ground(73, 82)
    for i, x in enumerate(range(73, 77)):
        column(x, 12 - i, 12, "B")
    column(77, 9, 12, "B")
    column(78, 9, 12, "B")
    for i, x in enumerate(range(79, 82)):
        column(x, 10 + i, 12, "B")
    for x in range(74, 81):
        put(x, 6 if x in (77, 78) else 7, "o")

    # ── A long log bridge over a deep pit ──
    row(85, 98, 10, "L")
    for x in range(87, 97, 2):
        put(x, 7, "o")
    put(92, 9, "e")

    # ── The end: crate stairs and the goal flag ──
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
    assert flat.count("e") <= 12, "at most 12 beetles"
    assert flat.count("o") <= 60, "at most 60 gems"
    return flat.count("o"), flat.count("e")


PROGRAM = r'''
; ════════════════════════════════════════════════════════════════
;  FOREST HOP - a platform game in AMOS Like Basic
;
;  Help Pinecone through the forest: collect gems, jump on the
;  beetles and reach the flag at the end of the level.
;
;  Left/right or A/D: walk      Space, Up or W: jump
;  (A gamepad works too.)
;
;  Shows: six layers with parallax scrolling, a raster sky, a tile
;  map, animated and mirrored sprites, font effects, collisions
;  and chiptune music.
;
;  All graphics are made by Graphics/ForestHop/generate_assets.py
;  and the level below by Graphics/ForestHop/build_game.py
; ════════════════════════════════════════════════════════════════

SCREEN 640,480

; ── Constants ───────────────────────────────────────────────────
LW=120 : LH=15 : WORLDW=LW*32
GRAV=0.6 : JUMPV=-13.5 : MAXFALL=11
ACC=0.45 : MAXV=4.2
TILES$="GDBQULC^bpvm"
GEMSPR=10 : BUGSPR=70 : FLAGSPR=2 : HUDGEM=3 : SPARKSPR=90 : PLAYER=99

DIM GX(60) : DIM GY(60) : DIM GA(60)
DIM EX(12) : DIM EY(12) : DIM ED(12) : DIM EA(12)
DIM SPK(6) : DIM SPX(6) : DIM SPY(6)

; ── Build the world ─────────────────────────────────────────────
DrawSky
DrawClouds
DrawHills
DrawForest
LoadGraphics
LoadLevel

MODE=0 : T=0 : SX=0 : HISCORE=0
SC=0 : GEMS=0 : LIVES=3
TitleMusic

; ════════════════════════════════════════════════════════════════
;  Main loop
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
;  Modes
; ════════════════════════════════════════════════════════════════
PROC TitleFrame
  ; The camera glides back and forth over the level as a demo
  CAMX=(1-COS(T*0.15))*(WORLDW-640)/2
  IF KEYSTATE("Space")=1 OR KEYSTATE("Return")=1 OR JOYSTATE(1,"A")=1 THEN NewGame
END PROC

PROC PlayFrame
  ReadInput
  MovePlayer
  AnimatePlayer
  ; Checkpoint in the middle of the level
  IF PX>56*32 THEN CPX=56*32 : CPY=11*32
  ; The goal
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
  ; Pinecone lands and hops with joy
  TV=0 : JK=0
  IF WT MOD 40=20 THEN JK=1
  MovePlayer
  AnimatePlayer
  ; Fireworks at the flag
  IF WT MOD 10=0 THEN Sparkle FLAGX-160+RND(320),90+RND(200)
  IF WT=60 THEN PLAY POWERUP
  IF WT>360 THEN EndRound
END PROC

PROC GameOverFrame
  WT=WT+1
  IF WT>360 THEN EndRound
END PROC


; ════════════════════════════════════════════════════════════════
;  The player
; ════════════════════════════════════════════════════════════════
PROC ReadInput
  TV=0
  IF KEYSTATE("Left")=1 OR KEYSTATE("A")=1 OR JOYSTATE(1,"Left")=1 THEN TV=-MAXV
  IF KEYSTATE("Right")=1 OR KEYSTATE("D")=1 OR JOYSTATE(1,"Right")=1 THEN TV=MAXV
  JK=0
  IF KEYSTATE("Space")=1 OR KEYSTATE("Up")=1 OR KEYSTATE("W")=1 OR JOYSTATE(1,"A")=1 THEN JK=1
END PROC

PROC MovePlayer
  ; Accelerate towards the wanted speed
  IF VX<TV THEN VX=MIN(VX+ACC,TV)
  IF VX>TV THEN VX=MAX(VX-ACC,TV)

  ; ── Horizontal, with walls ──
  PX=PX+VX
  IF PX<0 THEN PX=0 : VX=0
  IF VX>0 THEN
    IF SolidAt(PX+25,PY+6)=1 OR SolidAt(PX+25,PY+30)=1 THEN PX=INT((PX+25)/32)*32-26 : VX=0
  ENDIF
  IF VX<0 THEN
    IF SolidAt(PX+6,PY+6)=1 OR SolidAt(PX+6,PY+30)=1 THEN PX=INT((PX+6)/32)*32+26 : VX=0
  ENDIF

  ; ── Jump: only when the button is pressed, not while it is held ──
  IF JK=1 AND JHELD=0 AND ONG=1 THEN VY=JUMPV : ONG=0 : PLAY JUMP
  ; Let go of the button early = lower jump
  IF JK=0 AND VY<-5 THEN VY=-5
  JHELD=JK

  ; ── Vertical, with floors and ceilings ──
  VY=VY+GRAV
  IF VY>MAXFALL THEN VY=MAXFALL
  PY=PY+VY
  ONG=0
  IF VY>=0 THEN
    IF SolidAt(PX+8,PY+32)=1 OR SolidAt(PX+23,PY+32)=1 THEN PY=INT((PY+32)/32)*32-32 : VY=0 : ONG=1
  ELSE
    IF SolidAt(PX+8,PY+4)=1 OR SolidAt(PX+23,PY+4)=1 THEN HeadBump
  ENDIF

  ; ── Dangers ──
  IF TILE(4,PX+16,PY+28)=7 THEN Die
  IF PY>480 THEN Die
END PROC

PROC HeadBump
  BCX=INT((PX+16)/32) : BCY=INT((PY+4)/32)
  PY=BCY*32+28 : VY=1
  IF TILE(4,BCX*32,BCY*32)=3 THEN
    ; Bonus block: change to a used block, give points and a gem
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
  ; Mirror the sprite when walking left
  NF=FACE
  IF VX>0.3 THEN NF=1
  IF VX<-0.3 THEN NF=-1
  IF NF<>FACE THEN FACE=NF : SPRITE ZOOM PLAYER,FACE*100,100
  ; Blink for a while after starting
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
    GameOverMusic
  ELSE
    Spawn
  ENDIF
END PROC

PROC Win
  MODE=3 : WT=0
  SC=SC+1000+GEMS*10
  SPRITE FRAME PLAYER,4
  PLAY COIN
  WinMusic
END PROC

PROC EndRound
  IF SC>HISCORE THEN HISCORE=SC
  MODE=0
  SPRITE OFF PLAYER
  TitleMusic
END PROC

PROC NewGame
  SC=0 : GEMS=0 : LIVES=3
  LoadLevel
  CPX=STARTX : CPY=STARTY
  Spawn
  GameMusic
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
;  Gems, beetles and sparkles
; ════════════════════════════════════════════════════════════════
PROC UpdateGems
  ; All gems spin in step
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
    ; Beetles far off screen stand still
    IF EA(I)=1 AND ABS(EX(I)-SX-320)<480 THEN MoveBug I
    IF EA(I)<0 THEN
      ; Squashed: stays for a while and fades out
      EA(I)=EA(I)+1
      IF EA(I)=0 THEN SPRITE OFF BUGSPR+I
    ENDIF
  NEXT I
END PROC

PROC MoveBug(B)
  EX(B)=EX(B)+ED(B)*1.2
  ; Turn at walls and edges
  FX=EX(B)+16+ED(B)*14
  IF SolidAt(FX,EY(B)+16)=1 OR SolidAt(FX,EY(B)+40)=0 THEN ED(B)=-ED(B) : SPRITE ZOOM BUGSPR+B,0-ED(B)*100,100
  SPRITE IMAGE BUGSPR+B,2,BF
  SPRITE POS BUGSPR+B,EX(B),EY(B)
  IF MODE<>1 THEN EXIT PROC
  IF ABS(EX(B)-PX)<24 AND ABS(EY(B)-PY)<28 THEN
    ; Landing on top squashes the beetle, otherwise you get hurt
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

; Six sparkle sprites that are reused in turn
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
;  Camera and parallax
; ════════════════════════════════════════════════════════════════
PROC UpdateCamera
  IF MODE=1 OR MODE=2 THEN CAMX=PX-280
  IF CAMX<0 THEN CAMX=0
  IF CAMX>WORLDW-640 THEN CAMX=WORLDW-640
  ; Smooth follow
  SX=SX+(CAMX-SX)*0.18
  ; Further away = slower. The clouds also drift on their own.
  SCROLL 0,SX/10,0
  SCROLL 1,SX/6+T/4,0
  SCROLL 2,SX/4,0
  SCROLL 3,SX/2,0
  SCROLL 4,SX,0
END PROC


; ════════════════════════════════════════════════════════════════
;  Text (redrawn every frame on layer 5, which does not scroll)
; ════════════════════════════════════════════════════════════════
PROC DrawText
  FONT CLEAR
  IF MODE=0 THEN
    FONT PRINT #5,1,80,70,"FOREST HOP"
    IF INT(T/30) MOD 2=0 THEN
      TX$="PRESS SPACE TO PLAY"
      FONT PRINT #5,0,92,300,TX$
    ENDIF
    TX$="HI-SCORE " & HISCORE
    FONT PRINT #5,0,176,420,TX$
    SPRITE OFF HUDGEM
    EXIT PROC
  ENDIF
  TX$="SCORE " & SC
  FONT PRINT #5,0,12,8,TX$
  SPRITE ON HUDGEM
  TX$="X" & GEMS
  FONT PRINT #5,0,322,8,TX$
  TX$="LIVES " & LIVES
  FONT PRINT #5,0,460,8,TX$
  IF MODE=3 THEN FONT PRINT #5,1,32,150,"LEVEL CLEAR!"
  IF MODE=4 THEN FONT PRINT #5,1,104,150,"GAME OVER"
END PROC


; ════════════════════════════════════════════════════════════════
;  Music
; ════════════════════════════════════════════════════════════════
; The music is made in the tracker and lives in Sound/Tracker.
; Open the files with the TRACKER button to look at or change them.
PROC TitleMusic
  TRACK LOAD "Sound/Tracker/forest_morning.amostrk"
  TRACK LOOP 1
  TRACK PLAY
END PROC

PROC GameMusic
  TRACK LOAD "Sound/Tracker/forest_song.amostrk"
  TRACK LOOP 1
  TRACK PLAY
END PROC

; The fanfare and game over play once
PROC WinMusic
  TRACK LOAD "Sound/Tracker/victory.amostrk"
  TRACK LOOP 0
  TRACK PLAY
END PROC

PROC GameOverMusic
  TRACK LOAD "Sound/Tracker/game_over.amostrk"
  TRACK LOOP 0
  TRACK PLAY
END PROC


; ════════════════════════════════════════════════════════════════
;  The background layers are drawn once with BASIC commands.
;  Every layer is 640 pixels wide and repeats when it scrolls,
;  so everything is drawn so that the right edge meets the left edge.
; ════════════════════════════════════════════════════════════════
PROC DrawSky
  SCREEN SELECT 0
  ; The sky is raster colours, which stay put while the hills scroll
  RASTER 1,0,0,320,"#1d4aa0,#3f7fdc,#8cc6ff,#ffe6bf"
  RASTER 2,0,320,160,"#ffe6bf,#ffcf96"
  INKG "#fff4c4"
  CIRCLEF 470,110,44,44
  INKG "#fffbe6"
  CIRCLEF 470,110,34,34
  ; Blue mountains far away (a sum of sine waves that fit exactly into 640 pixels)
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
;  Graphics and level
; ════════════════════════════════════════════════════════════════
PROC LoadGraphics
  IMAGE LOAD 1,"Graphics/ForestHop/gem.png",20,20,4
  IMAGE LOAD 2,"Graphics/ForestHop/beetle.png",32,32,3
  IMAGE LOAD 3,"Graphics/ForestHop/flag.png",48,128,2
  IMAGE LOAD 4,"Graphics/ForestHop/sparkle.png",24,24,3
  SPRITE LOAD PLAYER,"Graphics/ForestHop/pinecone.png",32,32,6
  SPRITE LAYER PLAYER,4
  SPRITE IMAGE FLAGSPR,3,0
  SPRITE LAYER FLAGSPR,4
  ; The gem next to the count in the score row
  SPRITE IMAGE HUDGEM,1,0
  SPRITE LAYER HUDGEM,5
  SPRITE POS HUDGEM,296,12
  ; Text: font 0 for the score, font 1 for big headings with a wave effect
  FONT LOAD 0,"Graphics/ForestHop/font.png",24,32
  FONT MAP 0,"ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
  FONT LOAD 1,"Graphics/ForestHop/font.png",24,32
  FONT MAP 1,"ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"
  FONT ZOOM 1,200
  FONT EFFECT ADD 1,WAVE,0,0,16,0.02
  SCREEN SELECT 4
  TILE MAP LW,LH
  TILE LOAD "Graphics/ForestHop/tiles.png",32,32
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
    path = os.path.join(ROOT, "Projects", "foresthop.amosproj")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"Version": 2, "ProgramText": text}, f, ensure_ascii=False)
    print(f"wrote {path}: {len(text.splitlines())} lines, {gems} gems, {bugs} bugs")
    for r in rows:
        print(r)


if __name__ == "__main__":
    main()
