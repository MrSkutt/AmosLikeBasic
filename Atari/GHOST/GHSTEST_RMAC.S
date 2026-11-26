;-----------------------------------------------------------------------------
; GHSTEST_RMAC.S - plays SONG.GHS from the VBL until SPACE is pressed. rmac.
;
; The player is called as a routine from the VBL (see vbl below), so
; GHSPLAY_RMAC.S can be taken out and used as it is. When the tracker saves
; a song as GHOST, it writes this file with GHSPLAY_RMAC.S pasted in where it
; is included, so that it is one file. Build it, next to the song, with:
;   rmac -p -o GHSTEST.PRG GHSTEST_RMAC.S
; (GHSTEST.S is the same for Devpac: both give the same program.)
;
; A GHOST song is made for 50 frames a second. The screen's VBL can come 50,
; 60 (a colour screen at 60 Hz) or 71 times a second (the monochrome
; monitor), so the VBL here counts and leaves out a call now and then: the
; player is called 50 times a second on average whatever the screen.
;
; With SHOWTIME 1 the screen's border is red while ghs_play runs: its height
; is the player's time (a line is 512 cycles; 1 % of a 50 Hz frame is about
; 3 lines).
;-----------------------------------------------------------------------------

SHOWTIME	equ	1

	.text

start:	clr.l	-(sp)			; supervisor mode
	move.w	#$20,-(sp)		; Super
	trap	#1
	addq.l	#6,sp
	move.l	d0,old_ssp

	lea	song,a0
	bsr	ghs_init
	tst.l	d0
	bne.s	.notdhs

	pea	text_play
	move.w	#9,-(sp)		; Cconws
	trap	#1
	addq.l	#6,sp

	move.w	#50,vbl_hz			; how often the VBL comes:
	move.b	$ffff8260.w,d0			; the monochrome monitor 71 Hz,
	and.b	#3,d0				; a colour screen 50 or 60 Hz
	cmp.b	#2,d0
	bne.s	.colour
	move.w	#71,vbl_hz
	bra.s	.hz
.colour:	btst	#1,$ffff820a.w			; the sync mode: bit 1 set is 50 Hz
	bne.s	.hz
	move.w	#60,vbl_hz
.hz:
	move.w	$ffff8240.w,old_colour
	move.l	$70.w,old_vbl
	move.l	#vbl,$70.w		; our VBL first, then the system's

.key:	move.w	#7,-(sp)		; Crawcin
	trap	#1
	addq.l	#2,sp
	cmp.b	#' ',d0
	bne.s	.key

	move.l	old_vbl,$70.w
	bsr	ghs_stop
	move.w	old_colour,$ffff8240.w
	bra.s	.quit

.notdhs:	pea	text_bad
	move.w	#9,-(sp)
	trap	#1
	addq.l	#6,sp
	move.w	#7,-(sp)
	trap	#1
	addq.l	#2,sp

.quit:	move.l	old_ssp,-(sp)		; back to user mode
	move.w	#$20,-(sp)
	trap	#1
	addq.l	#6,sp
	clr.w	-(sp)			; Pterm0
	trap	#1

;-----------------------------------------------------------------------------
; The VBL: the player 50 times a second, then on to the system's VBL
;-----------------------------------------------------------------------------
vbl:
	move.l	d0,-(sp)
	move.w	vbl_count,d0		; 50 added each VBL: the player is due
	add.w	#50,d0			; when it reaches the VBL's rate
	cmp.w	vbl_hz,d0
	blo.s	.skip
	sub.w	vbl_hz,d0
	move.w	d0,vbl_count

	.if	SHOWTIME
	move.w	#1400,d0		; wait for the visible top border, so that
.wait:	dbf	d0,.wait		; the time bar is seen (the VBL comes in the
	move.w	#$700,$ffff8240.w	; blanking, which is not shown)
	.endif

	bsr	ghs_play

	.if	SHOWTIME
	move.w	old_colour,$ffff8240.w
	.endif
	bra.s	.done
.skip:	move.w	d0,vbl_count
.done:	move.l	(sp)+,d0
	move.l	old_vbl,-(sp)
	rts

	include	"GHSPLAY_RMAC.S"

	.data
text_play:
	dc.b	27,"E","GHOST player test - SPACE to quit",13,10,0
text_bad:
	dc.b	27,"E","SONG.GHS is not a GHOST song. Press a key.",13,10,0
	even
song:	incbin	"SONG.GHS"
	even

	.bss
old_ssp:	ds.l	1
old_vbl:	ds.l	1
old_colour:	ds.w	1
vbl_hz:		ds.w	1			; how often the VBL comes, Hz
vbl_count:	ds.w	1
