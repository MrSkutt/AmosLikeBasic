;-----------------------------------------------------------------------------
; DHSPLAY.S - player for DHS songs (Dead Hackers Society format)
;
; A DHS song is a YM register dump at 50 Hz, with STe DMA samples and LMC1992
; (Microwire) commands. The player needs no interrupt but the VBL: call
; dhs_play from it 50 times a second. It is position independent and has
; no self-modifying code, so it can be included anywhere. Devpac syntax.
;
;   lea    song,a0       ; the song, on an even address
;   bsr    dhs_init      ; d0.l = 0: ok, -1: not a DHS song
;   ...
;   bsr    dhs_play      ; 50 times a second (keeps all registers)
;   ...
;   bsr    dhs_stop      ; silence, DMA stopped, LMC1992 back to normal
;
; A song is made for 50 frames a second. On a 50 Hz screen, call dhs_play
; every VBL; at 60 Hz (colour) or 71 Hz (monochrome), leave a call out now
; and then, as DHSTEST.S does.
;
; All three must run in supervisor mode. Songs without DMA samples or LMC
; commands (header flags 0) touch only the YM and play on any ST; the others
; need an STe (or a Falcon/TT with STe sound).
;
; The song's format (big-endian, offsets from the song's start):
;    0  "DHS1"
;    4  L offset of the frames
;    8  L offset of the frame the song loops back to (0: it stops at the end)
;   12  L offset of the sample table (0: none)
;   16  W number of samples
;   18  W flags: bit 0 DMA used, bit 1 LMC1992 used
;   20  L number of frames
;   24  32 bytes title (ASCII), 56  8 bytes reserved
; A frame: W mask - bits 0-13 the YM registers written, bit 14 a DMA event,
; bit 15 an LMC command; $FFFF ends the song. Then the LMC command (W) if
; bit 15, a byte for each register in the mask from 0 up (register 7: bits
; 0-5, the port bits are kept), the DMA event (B: 0 stops, n starts sample
; n, $80+n queues sample n to follow the one playing without a gap; $80 alone
; in a frame waits until the queued sample has begun) if bit 14,
; and a pad byte if needed so the next frame is on an even address.
; A sample (16 bytes): L start, L end, L loop start (0: played once; the loop
; goes from there to the end), W DMA mode ($FF8920), W reserved.
;-----------------------------------------------------------------------------

DHS_POS		equ	0	; L the next frame
DHS_LOOP	equ	4	; L the loop frame (0: none)
DHS_SAMPLES	equ	8	; L the sample table
DHS_BASE	equ	12	; L the song
DHS_FLAGS	equ	16	; W the header's flags
DHS_ON		equ	18	; B playing
DHS_WAIT	equ	19	; B VBLs waited for the DMA at a sync
DHS_QADR	equ	20	; L the start of the sample queued last
DHS_SIZE	equ	24

YM_SELECT	equ	$ffff8800
DMA_CONTROL	equ	$ffff8901
DMA_SH		equ	$ffff8903	; the start address: high, middle, low byte
DMA_SM		equ	$ffff8905
DMA_SL		equ	$ffff8907
DMA_EH		equ	$ffff890f	; the end address (the byte after the last)
DMA_EM		equ	$ffff8911
DMA_EL		equ	$ffff8913
DMA_CH		equ	$ffff8909	; where the DMA is: high, middle, low byte
DMA_CM		equ	$ffff890b
DMA_CL		equ	$ffff890d
DMA_MODE	equ	$ffff8921
MW_DATA		equ	$ffff8922
MW_MASK		equ	$ffff8924

;-----------------------------------------------------------------------------
; dhs_init: a0 = the song. d0.l = 0 ok, -1 not a DHS song.
;-----------------------------------------------------------------------------
dhs_init:
	movem.l	d1/a1,-(sp)
	lea	dhs_vars(pc),a1
	clr.b	DHS_ON(a1)
	clr.b	DHS_WAIT(a1)
	cmp.l	#'DHS1',(a0)
	bne.s	.bad
	move.l	a0,DHS_BASE(a1)
	move.l	4(a0),d1
	add.l	a0,d1
	move.l	d1,DHS_POS(a1)
	move.l	8(a0),d1
	beq.s	.noloop
	add.l	a0,d1
.noloop	move.l	d1,DHS_LOOP(a1)
	move.l	12(a0),d1
	add.l	a0,d1
	move.l	d1,DHS_SAMPLES(a1)
	move.w	18(a0),DHS_FLAGS(a1)
	bsr	dhs_quiet
	st	DHS_ON(a1)
	moveq	#0,d0
	movem.l	(sp)+,d1/a1
	rts
.bad	moveq	#-1,d0
	movem.l	(sp)+,d1/a1
	rts

;-----------------------------------------------------------------------------
; dhs_play: one frame. Call from the VBL. Keeps all registers.
;-----------------------------------------------------------------------------
dhs_play:
	movem.l	d0-d2/a0-a2,-(sp)
	lea	dhs_vars(pc),a2
	tst.b	DHS_ON(a2)
	beq	.out
	move.l	DHS_POS(a2),a0
.next	move.w	(a0)+,d0
	cmp.w	#$ffff,d0
	bne.s	.sync
	move.l	DHS_LOOP(a2),d1		; the end: back to the loop, or stop
	beq.s	.end
	move.l	d1,a0
	bra.s	.next
.end	clr.b	DHS_ON(a2)
	bsr	dhs_quiet
	bra	.out

	; A sync: a frame of only the DMA event $80. The song goes on when the
	; sample queued last has begun, so the YM keeps time with the DMA
	; whatever its clock. Not yet: the same frame next VBL (2 VBLs at most).
.sync	cmp.w	#$4000,d0
	bne.s	.frame
	cmp.b	#$80,(a0)
	bne.s	.frame
	moveq	#0,d1
	move.b	DMA_CH.w,d1
	lsl.l	#8,d1
	move.b	DMA_CM.w,d1
	lsl.l	#8,d1
	move.b	DMA_CL.w,d1
	sub.l	DHS_QADR(a2),d1		; just past the queued sample's start?
	cmp.l	#16384,d1
	blo.s	.synced
	addq.b	#1,DHS_WAIT(a2)
	cmp.b	#3,DHS_WAIT(a2)
	blo	.out
.synced	clr.b	DHS_WAIT(a2)
	addq.l	#2,a0			; past $80 and its pad byte
	bra.s	.next

.frame	move.w	d0,d2			; bits 14-15 kept for later
	bpl.s	.nolmc
	move.w	#$07ff,MW_MASK.w	; an LMC1992 command (one a frame at most:
	move.w	(a0)+,MW_DATA.w		; the Microwire is done long before the next)
.nolmc	lea	YM_SELECT.w,a1

	lsr.w	#1,d0
	bcc.s	.n0
	move.b	#0,(a1)
	move.b	(a0)+,2(a1)
.n0	lsr.w	#1,d0
	bcc.s	.n1
	move.b	#1,(a1)
	move.b	(a0)+,2(a1)
.n1	lsr.w	#1,d0
	bcc.s	.n2
	move.b	#2,(a1)
	move.b	(a0)+,2(a1)
.n2	lsr.w	#1,d0
	bcc.s	.n3
	move.b	#3,(a1)
	move.b	(a0)+,2(a1)
.n3	lsr.w	#1,d0
	bcc.s	.n4
	move.b	#4,(a1)
	move.b	(a0)+,2(a1)
.n4	lsr.w	#1,d0
	bcc.s	.n5
	move.b	#5,(a1)
	move.b	(a0)+,2(a1)
.n5	lsr.w	#1,d0
	bcc.s	.n6
	move.b	#6,(a1)
	move.b	(a0)+,2(a1)
.n6	lsr.w	#1,d0
	bcc.s	.n7
	move.b	#7,(a1)			; the mixer: the port bits (6-7) kept
	move.b	(a1),d1
	and.b	#$c0,d1
	or.b	(a0)+,d1
	move.b	d1,2(a1)
.n7	lsr.w	#1,d0
	bcc.s	.n8
	move.b	#8,(a1)
	move.b	(a0)+,2(a1)
.n8	lsr.w	#1,d0
	bcc.s	.n9
	move.b	#9,(a1)
	move.b	(a0)+,2(a1)
.n9	lsr.w	#1,d0
	bcc.s	.n10
	move.b	#10,(a1)
	move.b	(a0)+,2(a1)
.n10	lsr.w	#1,d0
	bcc.s	.n11
	move.b	#11,(a1)
	move.b	(a0)+,2(a1)
.n11	lsr.w	#1,d0
	bcc.s	.n12
	move.b	#12,(a1)
	move.b	(a0)+,2(a1)
.n12	lsr.w	#1,d0
	bcc.s	.n13
	move.b	#13,(a1)		; written only when the envelope starts again
	move.b	(a0)+,2(a1)
.n13
	btst	#14,d2
	beq.s	.nodma
	moveq	#0,d1
	move.b	(a0)+,d1
	bsr.s	dhs_dma
.nodma	move.l	a0,d0			; the next frame is on an even address
	addq.l	#1,d0
	and.b	#$fe,d0
	move.l	d0,DHS_POS(a2)
.out	movem.l	(sp)+,d0-d2/a0-a2
	rts

;-----------------------------------------------------------------------------
; dhs_dma: d1.w = the DMA event, a2 = the variables. Uses d0-d2/a1.
;   0        stop
;   n        start sample n now (the one playing is cut)
;   $80+n    queue sample n: it follows the one playing without a gap
;   $80      (a frame of its own) sync: wait for the queued sample, above
; The DMA takes its start and end registers when a frame starts. So for a
; loop, the loop start is written straight after the start, and the DMA goes
; on with it at the end; and a queued sample is written into the registers
; while the one before plays (repeat on), and the DMA goes on with it at the
; end, to the sample. Queued samples have the mode of the one playing.
;-----------------------------------------------------------------------------
dhs_dma:
	moveq	#0,d0
	tst.b	d1
	bmi.s	.queue
	move.b	d0,DMA_CONTROL.w
	tst.w	d1
	beq.s	.done
	bsr.s	.regs
	move.b	5(a1),DMA_MODE.w	; the mode (the low byte of its word)
	move.l	(a1),d0			; loop start
	bne.s	.loop
	move.b	#1,DMA_CONTROL.w	; played once
.done	rts
.loop	move.b	#3,DMA_CONTROL.w	; played round and round...
	add.l	d2,d0			; ...from the loop start after the first time
	bra.s	dhs_setstart
.queue	and.w	#$7f,d1
	bsr.s	.regs
	btst	#0,DMA_CONTROL.w
	bne.s	.on
	move.b	5(a1),DMA_MODE.w	; nothing playing: it starts now
.on	move.b	#3,DMA_CONTROL.w	; repeat on, so that the DMA goes on to it
	rts
; the sample's start and end into the registers; a1 then points at its loop
; start, d2 is the song's address
.regs	subq.w	#1,d1
	lsl.w	#4,d1
	move.l	DHS_SAMPLES(a2),a1
	add.w	d1,a1
	move.l	DHS_BASE(a2),d2
	move.l	(a1)+,d0		; start
	add.l	d2,d0
	move.l	d0,DHS_QADR(a2)
	bsr.s	dhs_setstart
	move.l	(a1)+,d0		; end
	add.l	d2,d0
	swap	d0
	move.b	d0,DMA_EH.w
	swap	d0
	move.w	d0,d1
	lsr.w	#8,d1
	move.b	d1,DMA_EM.w
	move.b	d0,DMA_EL.w
	rts

; dhs_setstart: d0.l = the DMA's start address. Uses d1.
dhs_setstart:
	swap	d0
	move.b	d0,DMA_SH.w
	swap	d0
	move.w	d0,d1
	lsr.w	#8,d1
	move.b	d1,DMA_SM.w
	move.b	d0,DMA_SL.w
	rts

;-----------------------------------------------------------------------------
; dhs_stop: silence; the DMA stopped and the LMC1992 as TOS has it, if the
; song used them.
;-----------------------------------------------------------------------------
dhs_stop:
	movem.l	d0-d1/a0-a1,-(sp)
	lea	dhs_vars(pc),a1
	clr.b	DHS_ON(a1)
	bsr.s	dhs_quiet
	btst	#1,DHS_FLAGS+1(a1)
	beq.s	.nolmc
	lea	dhs_lmcdefault(pc),a0
	moveq	#6-1,d1
.lmc	move.w	(a0)+,d0
	bsr.s	dhs_wire
	dbf	d1,.lmc
.nolmc	movem.l	(sp)+,d0-d1/a0-a1
	rts

; dhs_quiet: the YM's three volumes 0, its tone and noise off; the DMA stopped
dhs_quiet:
	movem.l	d0/a0-a1,-(sp)
	lea	dhs_vars(pc),a1
	lea	YM_SELECT.w,a0
	moveq	#0,d0
	move.b	#8,(a0)
	move.b	d0,2(a0)
	move.b	#9,(a0)
	move.b	d0,2(a0)
	move.b	#10,(a0)
	move.b	d0,2(a0)
	move.b	#7,(a0)
	move.b	(a0),d0
	or.b	#$3f,d0
	move.b	d0,2(a0)
	btst	#0,DHS_FLAGS+1(a1)
	beq.s	.nodma
	moveq	#0,d0
	move.b	d0,DMA_CONTROL.w
.nodma	movem.l	(sp)+,d0/a0-a1
	rts

; dhs_wire: d0.w = a Microwire command, sent when the last one is done. Uses d0.
dhs_wire:
	move.l	d1,-(sp)
	move.w	#2000,d1
.idle	cmp.w	#$07ff,MW_MASK.w	; the last command shifted out
	dbeq	d1,.idle
	move.w	#$07ff,MW_MASK.w
	move.w	d0,MW_DATA.w
	move.w	#2000,d1
.go	cmp.w	#$07ff,MW_MASK.w	; this one started
	dbne	d1,.go
	move.l	(sp)+,d1
	rts

; The LMC1992 as TOS sets it: master 0 dB, left and right 0 dB, bass and
; treble flat, the YM mixed in
dhs_lmcdefault:
	dc.w	$04e8			; master  40
	dc.w	$0554			; left    20
	dc.w	$0514			; right   20
	dc.w	$0446			; bass     6
	dc.w	$0486			; treble   6
	dc.w	$0401			; mix      1

	even
dhs_vars:
	ds.b	DHS_SIZE
	even
