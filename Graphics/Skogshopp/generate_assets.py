#!/usr/bin/env python3
"""Genererar all grafik till demospelet Skogshopp (Projects/skogshopp.amosproj).

Allt ritas med kod här, så grafiken är helt egen. Kör om skriptet efter ändringar:

    python3 Graphics/Skogshopp/generate_assets.py

Obs: motorn gör rent svart (0,0,0) genomskinligt när bilder laddas, så konturer
använder en mörk lila nyans i stället för svart.
"""
import math
import os
import struct
import zlib

OUT = os.path.dirname(os.path.abspath(__file__))

# ── Palett ────────────────────────────────────────────────────────────
OUTLINE = (34, 20, 44)
WHITE = (250, 250, 245)
EYE = (40, 26, 60)

BODY = (214, 120, 48)
BODY_SHADE = (168, 82, 34)
BODY_LIGHT = (240, 160, 80)
BELLY = (252, 220, 168)
LEAF = (96, 186, 64)
LEAF_DARK = (52, 128, 44)
FEET = (110, 60, 36)
CHEEK = (240, 120, 120)

SHELL = (128, 64, 172)
SHELL_DARK = (84, 38, 124)
SHELL_LIGHT = (184, 130, 220)
BUG_HEAD = (60, 42, 80)

GRASS = (92, 184, 72)
GRASS_DARK = (58, 136, 52)
GRASS_LIGHT = (150, 220, 100)
DIRT = (150, 96, 56)
DIRT_DARK = (112, 70, 42)
DIRT_LIGHT = (184, 128, 78)
STONE = (140, 146, 160)
STONE_DARK = (96, 100, 118)
STONE_LIGHT = (190, 196, 210)
GOLD = (250, 196, 50)
GOLD_DARK = (206, 140, 30)
GOLD_LIGHT = (255, 236, 140)
WOOD = (176, 116, 62)
WOOD_DARK = (126, 80, 42)
WOOD_LIGHT = (214, 156, 92)


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [[None] * w for _ in range(h)]

    def set(self, x, y, c):
        x, y = int(x), int(y)
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y][x] = c

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.px[y][x]
        return None

    def rect(self, x0, y0, x1, y1, c):
        for y in range(int(y0), int(y1) + 1):
            for x in range(int(x0), int(x1) + 1):
                self.set(x, y, c)

    def ellipse(self, cx, cy, rx, ry, c):
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0:
                    self.set(x, y, c)

    def outline(self, c=OUTLINE):
        """Rita en kontur runt allt som inte är genomskinligt."""
        edge = []
        for y in range(self.h):
            for x in range(self.w):
                if self.px[y][x] is None:
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        n = self.get(x + dx, y + dy)
                        if n is not None and n != c:
                            edge.append((x, y))
                            break
        for x, y in edge:
            self.px[y][x] = c

    def blit(self, other, ox, oy):
        for y in range(other.h):
            for x in range(other.w):
                if other.px[y][x] is not None:
                    self.set(ox + x, oy + y, other.px[y][x])

    def save(self, name):
        raw = b""
        for row in self.px:
            raw += b"\x00" + b"".join(
                bytes((0, 0, 0, 0)) if c is None else bytes((c[0], c[1], c[2], 255)) for c in row)

        def chunk(tag, data):
            return (struct.pack(">I", len(data)) + tag + data +
                    struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

        png = (b"\x89PNG\r\n\x1a\n" +
               chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 6, 0, 0, 0)) +
               chunk(b"IDAT", zlib.compress(raw, 9)) +
               chunk(b"IEND", b""))
        with open(os.path.join(OUT, name), "wb") as f:
            f.write(png)
        print("wrote", name, f"{self.w}x{self.h}")


def sheet(frames):
    w, h = frames[0].w, frames[0].h
    s = Canvas(w * len(frames), h)
    for i, f in enumerate(frames):
        s.blit(f, i * w, 0)
    return s


# ── Hjälten: Kotte ────────────────────────────────────────────────────
def kotte(pose):
    c = Canvas(32, 32)
    bob = 1 if pose == "walk2" else 0
    by = 17 + bob

    # Fötter
    feet = {
        "idle": ((10, 27), (19, 27)),
        "walk1": ((7, 27), (21, 26)),
        "walk2": ((11, 27), (18, 27)),
        "walk3": ((13, 26), (17, 27)),
        "jump": ((9, 24), (20, 25)),
        "hurt": ((9, 27), (20, 27)),
    }[pose]
    for fx, fy in feet:
        c.rect(fx, fy, fx + 4, fy + 2, FEET)

    # Kropp med skuggning
    c.ellipse(16, by, 11, 10, BODY_SHADE)
    c.ellipse(15, by - 1, 10, 9, BODY)
    c.ellipse(12, by - 5, 4, 3, BODY_LIGHT)
    # Mage
    c.ellipse(18, by + 3, 6, 5, BELLY)

    # Löv på huvudet
    c.ellipse(18, by - 11, 5, 2.5, LEAF)
    c.rect(15, by - 11, 21, by - 11, LEAF_DARK)
    c.rect(14, by - 10, 14, by - 8, LEAF_DARK)

    # Armar
    if pose == "jump":
        c.ellipse(6, by - 6, 2, 3, BODY_SHADE)
        c.ellipse(26, by - 6, 2, 3, BODY_SHADE)
    else:
        c.ellipse(6, by + 2, 2, 3, BODY_SHADE)
        c.ellipse(26, by + 2, 2, 3, BODY_SHADE)

    # Ansikte (tittar åt höger)
    if pose == "hurt":
        for ex in (17, 23):
            for d in range(-1, 2):
                c.set(ex + d, by - 4 + d, EYE)
                c.set(ex + d, by - 4 - d, EYE)
        c.rect(19, by + 1, 22, by + 2, EYE)
    else:
        for ex in (17, 23):
            c.rect(ex - 1, by - 6, ex + 1, by - 2, WHITE)
            c.rect(ex, by - 5, ex + 1, by - 3, EYE)
            c.set(ex, by - 5, WHITE)
        c.rect(14, by - 1, 15, by, CHEEK)
        c.rect(25, by - 1, 25, by, CHEEK)
        c.rect(19, by + 1, 22, by + 1, EYE)
        c.set(18, by, EYE)
        c.set(23, by, EYE)

    c.outline()
    return c


# ── Fiende: skalbagge ─────────────────────────────────────────────────
def beetle(frame):
    c = Canvas(32, 32)
    if frame == 2:  # Mosad
        c.ellipse(16, 28, 13, 3, SHELL_DARK)
        c.ellipse(16, 27, 12, 2.5, SHELL)
        c.rect(8, 26, 11, 26, SHELL_LIGHT)
        for ex in (6, 10):
            c.rect(ex - 1, 27, ex + 1, 27, EYE)
        c.outline()
        return c

    # Ben (växlar mellan bilderna)
    legs = (6, 14, 22) if frame == 0 else (9, 17, 25)
    for lx in legs:
        c.rect(lx, 25, lx + 1, 29, BUG_HEAD)
        c.rect(lx - 1, 29, lx + 2, 29, BUG_HEAD)

    # Skal
    c.ellipse(18, 18, 12, 10, SHELL_DARK)
    c.ellipse(18, 17, 11, 9, SHELL)
    c.ellipse(14, 12, 4, 2.5, SHELL_LIGHT)
    c.rect(18, 9, 18, 26, SHELL_DARK)
    for sx, sy in ((13, 19), (23, 15), (24, 22)):
        c.ellipse(sx, sy, 2, 2, SHELL_DARK)

    # Huvud (tittar åt vänster)
    c.ellipse(7, 20, 6, 5, BUG_HEAD)
    for ex in (4, 9):
        c.rect(ex - 1, 17, ex + 1, 20, WHITE)
        c.rect(ex - 1, 18, ex, 20, EYE)
    # Antenner
    c.rect(4, 12, 4, 15, BUG_HEAD)
    c.rect(2, 11, 3, 11, BUG_HEAD)
    c.rect(9, 12, 9, 15, BUG_HEAD)
    c.rect(10, 11, 11, 11, BUG_HEAD)

    c.outline()
    return c


# ── Ädelsten (snurrar) ────────────────────────────────────────────────
def gem(frame):
    c = Canvas(20, 20)
    width = [7, 5, 2, 5][frame]
    cx = 10
    for y in range(3, 18):
        t = (y - 3) / 14.0
        half = width * (t / 0.35 if t < 0.35 else (1 - t) / 0.65)
        for x in range(int(cx - half), int(cx + half) + 1):
            left = x < cx
            if y < 8:
                col = (170, 250, 255) if left else (110, 220, 250)
            else:
                col = (60, 170, 240) if left else (40, 120, 210)
            c.set(x, y, col)
    if frame != 2:
        c.set(cx - 2, 6, WHITE)
        c.set(cx - 1, 5, WHITE)
    c.outline((20, 50, 110))
    return c


# ── Målflagga ─────────────────────────────────────────────────────────
def flag(frame):
    c = Canvas(48, 128)
    c.rect(6, 4, 9, 127, STONE_LIGHT)
    c.rect(9, 4, 10, 127, STONE_DARK)
    c.ellipse(8, 4, 4, 4, GOLD)
    for x in range(11, 44):
        wave = math.sin((x - 11) / 32 * math.pi * 2 + frame * math.pi) * 2.5
        for y in range(10, 34):
            yy = int(y + wave * (x - 11) / 32)
            col = LEAF if (y - 10) < 12 else LEAF_DARK
            c.set(x, yy, col)
    # Stjärna på flaggan
    for i in range(5):
        a = -math.pi / 2 + i * 2 * math.pi / 5
        for r in range(0, 7):
            c.set(26 + math.cos(a) * r, 22 + math.sin(a) * r, GOLD_LIGHT)
    c.ellipse(26, 22, 2.5, 2.5, GOLD)
    c.outline()
    return c


# ── Liten stjärna när man tar ett bonusblock ──────────────────────────
def sparkle(frame):
    c = Canvas(24, 24)
    r = [4, 8, 10][frame]
    col = [GOLD_LIGHT, GOLD, (255, 255, 220)][frame]
    for i in range(8):
        a = i * math.pi / 4
        rr = r if i % 2 == 0 else r * 0.5
        for d in range(int(rr) + 1):
            c.set(12 + math.cos(a) * d, 12 + math.sin(a) * d, col)
    c.ellipse(12, 12, 2, 2, WHITE)
    return c


# ── Tiles (32x32) ─────────────────────────────────────────────────────
def noise(x, y, seed):
    return (math.sin(x * 12.9898 + y * 78.233 + seed * 37.719) * 43758.5453) % 1.0


def tile_dirt(c, seed=1):
    c.rect(0, 0, 31, 31, DIRT)
    for y in range(32):
        for x in range(32):
            n = noise(x // 2, y // 2, seed)
            if n < 0.08:
                c.set(x, y, DIRT_DARK)
            elif n > 0.95:
                c.set(x, y, DIRT_LIGHT)
    for px, py in ((6, 20), (22, 10), (16, 27)):
        c.ellipse(px, py, 2, 1.5, STONE)


def tile_grass():
    c = Canvas(32, 32)
    tile_dirt(c, 2)
    c.rect(0, 0, 31, 7, GRASS)
    c.rect(0, 0, 31, 1, GRASS_LIGHT)
    for x in range(0, 32, 3):
        h = 2 + int(noise(x, 0, 3) * 4)
        c.rect(x, 8, x + 1, 7 + h, GRASS_DARK if x % 2 else GRASS)
    return c


def tile_stone():
    c = Canvas(32, 32)
    c.rect(0, 0, 31, 31, STONE_DARK)
    for row, off in ((0, 0), (1, 8), (2, 0), (3, 8)):
        y0 = row * 8
        for bx in range(-8, 32, 16):
            x0 = bx + off
            c.rect(x0 + 1, y0 + 1, x0 + 14, y0 + 6, STONE)
            c.rect(x0 + 1, y0 + 1, x0 + 14, y0 + 1, STONE_LIGHT)
    return c


def tile_bonus():
    c = Canvas(32, 32)
    c.rect(0, 0, 31, 31, GOLD_DARK)
    c.rect(1, 1, 30, 30, GOLD)
    c.rect(1, 1, 30, 2, GOLD_LIGHT)
    c.rect(1, 1, 2, 30, GOLD_LIGHT)
    for x, y in ((4, 4), (27, 4), (4, 27), (27, 27)):
        c.rect(x, y, x + 1, y + 1, GOLD_DARK)
    # Ädelsten i mitten
    for y in range(8, 25):
        t = (y - 8) / 16
        half = 7 * (t / 0.35 if t < 0.35 else (1 - t) / 0.65)
        for x in range(int(16 - half), int(16 + half) + 1):
            c.set(x, y, (120, 220, 250) if x < 16 else (60, 150, 230))
    return c


def tile_used():
    c = Canvas(32, 32)
    c.rect(0, 0, 31, 31, WOOD_DARK)
    c.rect(1, 1, 30, 30, (150, 102, 70))
    for x, y in ((4, 4), (27, 4), (4, 27), (27, 27)):
        c.rect(x, y, x + 1, y + 1, WOOD_DARK)
    return c


def tile_log():
    c = Canvas(32, 32)
    c.rect(0, 0, 31, 15, WOOD)
    c.rect(0, 0, 31, 2, WOOD_LIGHT)
    c.rect(0, 13, 31, 15, WOOD_DARK)
    for x in (0, 15, 31):
        c.rect(x, 0, x, 15, WOOD_DARK)
    for x in range(3, 30, 6):
        c.rect(x, 6, x + 2, 6, WOOD_DARK)
    return c


def tile_crate():
    c = Canvas(32, 32)
    c.rect(0, 0, 31, 31, WOOD_DARK)
    c.rect(2, 2, 29, 29, WOOD)
    c.rect(2, 2, 29, 4, WOOD_LIGHT)
    for i in range(26):
        c.rect(3 + i, 3 + i, 4 + i, 4 + i, WOOD_DARK)
    c.rect(0, 14, 31, 17, WOOD_DARK)
    return c


def tile_spikes():
    c = Canvas(32, 32)
    c.rect(0, 26, 31, 31, STONE_DARK)
    for sx in range(0, 32, 8):
        for y in range(10, 26):
            half = (y - 10) / 16 * 4
            for x in range(int(sx + 4 - half), int(sx + 4 + half) + 1):
                c.set(x, y, STONE_LIGHT if x < sx + 4 else STONE)
    return c


def tile_bush():
    c = Canvas(32, 32)
    for cx, cy, r in ((9, 22, 8), (22, 22, 8), (16, 17, 9)):
        c.ellipse(cx, cy, r, r, GRASS_DARK)
    for cx, cy, r in ((9, 21, 6), (22, 21, 6), (16, 16, 7)):
        c.ellipse(cx, cy, r, r, GRASS)
    c.ellipse(13, 13, 3, 2, GRASS_LIGHT)
    c.rect(2, 30, 29, 31, GRASS_DARK)
    c.outline()
    return c


def tile_flower(petal):
    c = Canvas(32, 32)
    c.rect(15, 18, 16, 31, GRASS_DARK)
    c.ellipse(11, 25, 4, 2, GRASS)
    for a in range(5):
        ang = a * 2 * math.pi / 5
        c.ellipse(16 + math.cos(ang) * 4, 14 + math.sin(ang) * 4, 3, 3, petal)
    c.ellipse(16, 14, 2.5, 2.5, GOLD)
    c.outline()
    return c


def tile_mushroom():
    c = Canvas(32, 32)
    c.rect(13, 20, 19, 31, BELLY)
    c.ellipse(16, 18, 11, 7, (210, 60, 70))
    c.rect(5, 18, 27, 20, (210, 60, 70))
    for sx, sy in ((10, 15), (19, 13), (23, 18)):
        c.ellipse(sx, sy, 2, 1.5, WHITE)
    c.outline()
    return c


def build_tiles():
    tiles = [
        tile_grass(),            # 0 gräs (fast)
        None,                    # 1 jord (fast)
        tile_stone(),            # 2 sten (fast)
        tile_bonus(),            # 3 bonusblock (fast)
        tile_used(),             # 4 använt block (fast)
        tile_log(),              # 5 stock (fast)
        tile_crate(),            # 6 låda (fast)
        tile_spikes(),           # 7 taggar (farlig)
        tile_bush(),             # 8 buske (dekor)
        tile_flower((240, 110, 170)),  # 9 blomma (dekor)
        tile_flower((140, 160, 250)),  # 10 blomma (dekor)
        tile_mushroom(),         # 11 svamp (dekor)
    ]
    d = Canvas(32, 32)
    tile_dirt(d, 7)
    tiles[1] = d
    s = Canvas(32 * 8, 32 * 2)
    for i, t in enumerate(tiles):
        s.blit(t, (i % 8) * 32, (i // 8) * 32)
    return s


# ── Bitmapfont 24x32 (5x7-glyfer förstorade 4 gånger) ─────────────────
GLYPHS = {
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "C": ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    "G": ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "I": ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
    "J": ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "Q": ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "V": ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
    "W": ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
    "X": ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
    "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    "Z": ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    "Å": ["00100", "01010", "00100", "01110", "10001", "11111", "10001"],
    "Ä": ["01010", "00000", "01110", "10001", "11111", "10001", "10001"],
    "Ö": ["01010", "00000", "01110", "10001", "10001", "10001", "01110"],
    "0": ["01110", "10011", "10101", "10101", "11001", "10001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
    "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
    "9": ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    "!": ["00100", "00100", "00100", "00100", "00100", "00000", "00100"],
    "?": ["01110", "10001", "00001", "00110", "00100", "00000", "00100"],
    ":": ["00000", "00100", "00100", "00000", "00100", "00100", "00000"],
    ".": ["00000", "00000", "00000", "00000", "00000", "00100", "00100"],
    "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    "+": ["00000", "00100", "00100", "11111", "00100", "00100", "00000"],
    "*": ["00000", "00100", "10101", "01110", "10101", "00100", "00000"],
    "/": ["00001", "00010", "00010", "00100", "01000", "01000", "10000"],
}
FONT_MAP = "ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789!?:.-+*/"


def build_font():
    cols = 16
    rows = math.ceil(len(FONT_MAP) / cols)
    s = Canvas(24 * cols, 32 * rows)
    for i, ch in enumerate(FONT_MAP):
        g = GLYPHS[ch]
        c = Canvas(24, 32)
        for gy, line in enumerate(g):
            # Färgövergång uppifrån och ner: ljusgul → orange
            t = gy / 6
            col = (255, int(236 - t * 110), int(120 - t * 90))
            for gx, bit in enumerate(line):
                if bit == "1":
                    c.rect(2 + gx * 4, 2 + gy * 4, 5 + gx * 4, 5 + gy * 4, col)
        # Skugga snett nedåt höger, sedan kontur
        shadow = Canvas(24, 32)
        for y in range(32):
            for x in range(24):
                if c.px[y][x] is not None:
                    shadow.set(x + 2, y + 2, (120, 50, 40))
        shadow.blit(c, 0, 0)
        shadow.outline()
        s.blit(shadow, (i % cols) * 24, (i // cols) * 32)
    return s


if __name__ == "__main__":
    sheet([kotte(p) for p in ("idle", "walk1", "walk2", "walk3", "jump", "hurt")]).save("kotte.png")
    sheet([beetle(f) for f in range(3)]).save("bagge.png")
    sheet([gem(f) for f in range(4)]).save("gem.png")
    sheet([flag(f) for f in range(2)]).save("flag.png")
    sheet([sparkle(f) for f in range(3)]).save("sparkle.png")
    build_tiles().save("tiles.png")
    build_font().save("font.png")
    print("FONT MAP:", FONT_MAP)
