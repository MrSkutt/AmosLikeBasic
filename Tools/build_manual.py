#!/usr/bin/env python3
"""Builds the manual as PDF (and HTML) from the Markdown files in Documentation/Manual.

    python3 Tools/build_manual.py

Output:
    Documentation/AMOS Like Basic - Manual.pdf
    Documentation/Manual/build/manual.html   (the same as a web page)

The chapters are read in file name order. Every file starts with a heading
"# Chapter name". A file that only contains "# Part: Name" becomes a part page.

Supported Markdown:
    # / ## / ###           headings (# = a new chapter on a new page)
    **bold**, *italic*, `code`, [link](url), ![image](path)
    ```basic ... ```       code examples with coloured BASIC (```text for plain text)
    - item, 1. item        lists
    | a | b |              tables
    > **Tip:** text        a box (Tip, Note, Try it, Good to know)
    ---                    a line

The PDF is printed with Google Chrome (or Chromium/Edge) without a window.
"""
import glob
import html
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SRC = os.path.join(ROOT, "Documentation", "Manual")
BUILD = os.path.join(SRC, "build")
PDF = os.path.join(ROOT, "Documentation", "AMOS Like Basic - Manual.pdf")

# ── Färgmarkering av BASIC ────────────────────────────────────────────

KEYWORDS = """
IF THEN ELSE ENDIF END FOR TO STEP NEXT WHILE WEND DO LOOP REPEAT UNTIL EXIT GOTO GOSUB RETURN
PROC ENDPROC FUNCTION ENDFUNCTION SELECT CASE DEFAULT ENDSELECT LET SWAP DIM DATA READ RESTORE REM
AND OR NOT MOD DIV ON ERROR RESUME INC DEC WAIT VBL PRINT PRINTG INPUT INPUTG LOCATE LOCATEG
CLS CLSG CLSGA PAPER PAPERG INK INKG SCREEN SELECT SCROLL LOAD PLOT LINE BOX BAR CIRCLE CIRCLEF
ELLIPSE FILL TEXT IMAGE SPRITE ANIM SAM MUSIC PLAY RASTER PARTICLE TILE FONT MAP TIMER TICKS
PROFILE OPEN CLOSE APPEND OUT IN AT
POS MOVE ZOOM ROTATE FRAME LAYER ON OFF ALPHA FADE GROUP UNGROUP EFFECT ADD UPDATE HANDLE
SET DRAW DEF STOP WRAP DEL GFXMODE SPACE INFO BPM VOLUME WAVE BLUR COLOR LOOP ONCE START TRACK
""".split()

FUNCTIONS = """
ABS ANYPLAYING ASC ATN CHR$ COS EOF EXP FADING FIND FINDLAST HEX HIT HITBOX HITBOXGROUP HITCIRCLE
HITCIRCLEGROUP HITGROUP INKEY$ INSTR INT JOIN$ JOYAXIS JOYSTATE KEYSTATE LAYERFADING LEFT$ LEN
LOG LOG10 LOG2 LOWER$ MAX MICROS MID$ MILLIS MIN PLAYING REPEAT$ REPLACE$ RIGHT$ RND SGN SIN
SPLIT$ SQR STR$ TAN TRACKPLAYING TRACKPOS TRACKROW TRIM$ UPPER$ VAL WORD$ TRUE FALSE
""".split()

TOKEN = re.compile(r'''
    (?P<str>"[^"\n]*"?)
  | (?P<comment>;.*$|(?<![\w$])REM(?![\w$]).*$)
  | (?P<num>(?<![\w$])\d+(?:\.\d+)?)
  | (?P<word>[A-Za-z_][\w]*\$?)
''', re.X | re.M | re.I)


def highlight_basic(code):
    out = []
    for line in code.split("\n"):
        # Etikett i början av raden: "Loop:"
        m = re.match(r"^(\s*)([A-Za-z_]\w*):\s*$", line)
        if m:
            out.append(html.escape(m.group(1)) + f'<span class="lbl">{html.escape(m.group(2))}:</span>')
            continue
        pos, parts = 0, []
        for t in TOKEN.finditer(line):
            parts.append(html.escape(line[pos:t.start()]))
            text = html.escape(t.group(0))
            if t.group("str"):
                parts.append(f'<span class="str">{text}</span>')
            elif t.group("comment"):
                parts.append(f'<span class="com">{text}</span>')
            elif t.group("num"):
                parts.append(f'<span class="num">{text}</span>')
            else:
                up = t.group(0).upper()
                if up in KEYWORDS:
                    parts.append(f'<span class="kw">{text}</span>')
                elif up in FUNCTIONS:
                    parts.append(f'<span class="fn">{text}</span>')
                else:
                    parts.append(text)
            pos = t.end()
        parts.append(html.escape(line[pos:]))
        out.append("".join(parts))
    return "\n".join(out)


# ── Markdown ──────────────────────────────────────────────────────────

def slug(text, used):
    t = text.lower().translate(str.maketrans("åäöéü", "aaoeu"))
    s = re.sub(r"[^a-z0-9]+", "-", t).strip("-") or "avsnitt"
    base, n = s, 2
    while s in used:
        s = f"{base}-{n}"
        n += 1
    used.add(s)
    return s


def inline(text):
    # Kod först, så att * och _ i kod inte tolkas
    codes = []

    def keep_code(m):
        codes.append(f"<code>{html.escape(m.group(1))}</code>")
        return f"\x00{len(codes) - 1}\x00"

    text = re.sub(r"`([^`]+)`", keep_code, text)
    text = html.escape(text, quote=False)
    text = re.sub(r"!\[([^\]]*)\]\(([^)]+)\)", r'<img src="\2" alt="\1">', text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<!\w)\*(?!\s)(.+?)(?<!\s)\*(?!\w)", r"<em>\1</em>", text)
    return re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], text)


def markdown_to_html(md, toc, used_ids):
    lines = md.replace("\r\n", "\n").split("\n")
    out = []
    i = 0
    para = []

    def flush_para():
        if para:
            out.append(f"<p>{inline(' '.join(para))}</p>")
            para.clear()

    while i < len(lines):
        line = lines[i]

        if line.startswith("```"):
            flush_para()
            lang = line[3:].strip().lower()
            code = []
            i += 1
            while i < len(lines) and not lines[i].startswith("```"):
                code.append(lines[i])
                i += 1
            body = "\n".join(code)
            if lang.startswith("basic"):
                out.append(f'<pre class="code basic"><code>{highlight_basic(body)}</code></pre>')
            else:
                out.append(f'<pre class="code"><code>{html.escape(body)}</code></pre>')
            i += 1
            continue

        m = re.match(r"^(#{1,3})\s+(.*)$", line)
        if m:
            flush_para()
            level, title = len(m.group(1)), m.group(2).strip()
            if level == 1 and title.lower().startswith("part:"):
                name = title[5:].strip()
                hid = slug(name, used_ids)
                toc.append((0, name, hid))
                out.append(f'<section class="part" id="{hid}"><div class="part-title">{inline(name)}</div></section>')
            else:
                hid = slug(title, used_ids)
                toc.append((level, title, hid))
                cls = ' class="chapter"' if level == 1 else ""
                out.append(f'<h{level} id="{hid}"{cls}>{inline(title)}</h{level}>')
            i += 1
            continue

        if re.match(r"^-{3,}\s*$", line):
            flush_para()
            out.append("<hr>")
            i += 1
            continue

        if line.startswith(">"):
            flush_para()
            quote = []
            while i < len(lines) and lines[i].startswith(">"):
                quote.append(lines[i][1:].strip())
                i += 1
            text = " ".join(quote)
            kind = "note"
            m2 = re.match(r"\*\*(Tip|Note|Try it|Good to know)\b", text)
            if m2:
                kind = {"Tip": "tip", "Note": "warn"}.get(m2.group(1), "try")
            out.append(f'<div class="box {kind}">{inline(text)}</div>')
            continue

        if line.startswith("|"):
            flush_para()
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            head, body = rows[0], [r for r in rows[1:] if not all(re.match(r"^:?-+:?$", c) for c in r)]
            t = ["<table><thead><tr>"] + [f"<th>{inline(c)}</th>" for c in head] + ["</tr></thead><tbody>"]
            for r in body:
                t += ["<tr>"] + [f"<td>{inline(c)}</td>" for c in r] + ["</tr>"]
            t.append("</tbody></table>")
            out.append("".join(t))
            continue

        m = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", line)
        if m:
            flush_para()
            ordered = m.group(2)[0].isdigit()
            tag = "ol" if ordered else "ul"
            items = []
            while i < len(lines):
                m = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", lines[i])
                if m:
                    items.append(m.group(3))
                elif lines[i].startswith("  ") and lines[i].strip() and items:
                    items[-1] += " " + lines[i].strip()
                else:
                    break
                i += 1
            out.append(f"<{tag}>" + "".join(f"<li>{inline(x)}</li>" for x in items) + f"</{tag}>")
            continue

        if not line.strip():
            flush_para()
        else:
            para.append(line.strip())
        i += 1

    flush_para()
    return "\n".join(out)


# ── Sidan ─────────────────────────────────────────────────────────────

CSS = r"""
@page { size: A4; margin: 20mm 18mm 20mm 18mm;
        @bottom-center { content: counter(page); font: 9pt "Helvetica Neue", Arial, sans-serif; color: #667; } }
@page :first { @bottom-center { content: none; } }
* { box-sizing: border-box; }
body { font: 10.5pt/1.5 "Helvetica Neue", Arial, sans-serif; color: #1b1f2a; margin: 0; }
h1, h2, h3 { font-family: "Avenir Next", "Helvetica Neue", Arial, sans-serif; color: #0b2a9b; line-height: 1.2; }
h1.chapter { break-before: page; font-size: 26pt; margin: 0 0 6mm; padding-bottom: 3mm;
             border-bottom: 3px solid #ffd400; }
h2 { font-size: 15pt; margin: 8mm 0 2mm; break-after: avoid; }
h3 { font-size: 12pt; margin: 5mm 0 1.5mm; break-after: avoid; color: #2a3f8f; }
p { margin: 0 0 2.5mm; }
a { color: #0b2a9b; text-decoration: none; }
code { font: 9.5pt "Menlo", "Consolas", monospace; background: #eef1fa; padding: 0 1mm; border-radius: 2px; }
pre.code { background: #0b1a4d; color: #e8eeff; padding: 3mm 4mm; border-radius: 3px;
           border-left: 4px solid #ffd400; font: 9pt/1.45 "Menlo", "Consolas", monospace;
           white-space: pre-wrap; break-inside: avoid; margin: 2mm 0 4mm; }
pre.code code { background: none; padding: 0; font: inherit; color: inherit; }
pre.code .kw { color: #ffd400; font-weight: bold; }
pre.code .fn { color: #7fd7ff; }
pre.code .str { color: #9df59d; }
pre.code .num { color: #ffb37a; }
pre.code .com { color: #8f9bc8; font-style: italic; }
pre.code .lbl { color: #ff8fd0; font-weight: bold; }
table { border-collapse: collapse; width: 100%; margin: 2mm 0 4mm; font-size: 9.5pt; break-inside: auto; }
th { background: #0b2a9b; color: white; text-align: left; padding: 1.5mm 2mm; }
td { border-bottom: 1px solid #d5dbec; padding: 1.5mm 2mm; vertical-align: top; }
tr { break-inside: avoid; }
td code { white-space: nowrap; }
ul, ol { margin: 0 0 3mm; padding-left: 6mm; }
li { margin: 0.6mm 0; }
hr { border: 0; border-top: 1px solid #c5cde6; margin: 5mm 0; }
img { max-width: 100%; border: 1px solid #c5cde6; margin: 2mm 0; }
.box { border-radius: 3px; padding: 2.5mm 4mm; margin: 2mm 0 4mm; break-inside: avoid; }
.box.tip { background: #fff8d6; border-left: 4px solid #ffd400; }
.box.warn { background: #ffe9e6; border-left: 4px solid #e0533d; }
.box.try { background: #e6f6ea; border-left: 4px solid #2fa84f; }
.box.note { background: #eef1fa; border-left: 4px solid #7f9cff; }

.cover { height: 257mm; display: flex; flex-direction: column; justify-content: center; text-align: center;
         background: #0b2a9b; color: white; margin: -20mm -18mm 0; padding: 0 18mm; break-after: page; }
.cover .title { font: bold 44pt "Avenir Next", "Helvetica Neue", Arial, sans-serif; color: #ffd400;
                letter-spacing: 1px; }
.cover .subtitle { font-size: 16pt; margin-top: 6mm; color: #cfe0ff; }
.cover .rainbow { height: 6mm; margin: 14mm auto; width: 70%;
                  background: linear-gradient(90deg, #e53935, #fb8c00, #fdd835, #43a047, #1e88e5, #8e24aa); }
.cover .small { margin-top: 20mm; font-size: 10pt; color: #9fb4ff; }
.part { break-before: page; height: 240mm; display: flex; align-items: center; justify-content: center; }
.part-title { font: bold 32pt "Avenir Next", "Helvetica Neue", Arial, sans-serif; color: #0b2a9b;
              border-top: 4px solid #ffd400; border-bottom: 4px solid #ffd400; padding: 6mm 12mm; }
.toc { break-after: page; }
.toc h1 { font-size: 24pt; border-bottom: 3px solid #ffd400; padding-bottom: 3mm; }
.toc ol { list-style: none; padding: 0; }
.toc li.toc-part { font-weight: bold; color: #0b2a9b; margin-top: 5mm; }
.toc li.l1 { font-weight: bold; margin-top: 1.5mm; }
.toc li.l2 { padding-left: 6mm; font-size: 9.5pt; color: #3a4466; }
.toc a { color: inherit; }
@media screen { body { max-width: 190mm; margin: 0 auto; padding: 10mm; }
                .cover { margin: 0 0 10mm; height: auto; padding: 30mm 10mm; } .part { height: auto; padding: 20mm 0; } }
"""


def build():
    files = sorted(glob.glob(os.path.join(SRC, "*.md")))
    if not files:
        sys.exit(f"No chapters found in {SRC}")

    toc, used, chapters = [], set(), []
    for f in files:
        with open(f, encoding="utf-8") as fh:
            chapters.append(markdown_to_html(fh.read(), toc, used))

    toc_html = ['<nav class="toc"><h1>Contents</h1><ol>']
    for level, title, hid in toc:
        if level == 3:
            continue
        cls = {0: "toc-part", 1: "l1", 2: "l2"}[level]
        toc_html.append(f'<li class="{cls}"><a href="#{hid}">{inline(title)}</a></li>')
    toc_html.append("</ol></nav>")

    cover = """<section class="cover">
<div class="title">AMOS LIKE BASIC</div>
<div class="subtitle">Learn to program games and graphics in BASIC</div>
<div class="rainbow"></div>
<div class="subtitle">Guide and reference</div>
<div class="small">For macOS and Windows</div>
</section>"""

    page = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>AMOS Like Basic – Manual</title>
<style>{CSS}</style></head>
<body>
{cover}
{''.join(toc_html)}
{''.join(chapters)}
</body></html>"""

    os.makedirs(BUILD, exist_ok=True)
    html_path = os.path.join(BUILD, "manual.html")
    with open(html_path, "w", encoding="utf-8") as fh:
        fh.write(page)
    print("wrote", html_path)

    chrome = find_chrome()
    if not chrome:
        print("!! No Chrome/Chromium/Edge found, so no PDF was made. Open the HTML file and print it to PDF.")
        return
    subprocess.run([chrome, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                    f"--print-to-pdf={PDF}", "file://" + html_path],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print("wrote", PDF)


def find_chrome():
    for c in ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
              "/Applications/Chromium.app/Contents/MacOS/Chromium",
              "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
              r"C:\Program Files\Google\Chrome\Application\chrome.exe",
              r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"]:
        if os.path.exists(c):
            return c
    for name in ["google-chrome", "chromium", "chromium-browser", "chrome", "msedge"]:
        if shutil.which(name):
            return shutil.which(name)
    return None


if __name__ == "__main__":
    build()
