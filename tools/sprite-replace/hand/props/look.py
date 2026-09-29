"""Stacks close looks at several props for checking, system Python:

    python look.py <zoom> <Name> [<Name> ...]

Each row is the picture, the classic render, their 50 percent overlay and
the turned view, the first three at zoom times the picture; written to
work/look.png.
"""
import os
import sys

from PIL import Image, ImageDraw

OUT = r"D:\OKReplace\hand\props"
z = int(sys.argv[1])
rows = []
for name in sys.argv[2:]:
    spr = Image.open(os.path.join(r"D:\OKReplace\sprites", name + ".png")).convert("RGBA")
    W, H = spr.width * z, spr.height * z
    big = spr.resize((W, H), Image.NEAREST)
    ren = Image.open(os.path.join(OUT, "renders", name + "_classic.png")).convert("RGBA").resize((W, H), Image.LANCZOS)
    ghost = big.copy()
    ghost.putalpha(big.getchannel("A").point(lambda a: a // 2))
    tv = Image.open(os.path.join(OUT, "renders", name + "_turned.png")).convert("RGBA")
    tv.thumbnail((max(H, 160) * 2, max(H, 160)))
    row = Image.new("RGBA", (W * 3 + tv.width + 30, max(H, tv.height) + 16), (84, 104, 64, 255))
    for i, im in enumerate((big, ren, Image.alpha_composite(ren, ghost))):
        row.alpha_composite(im, (i * (W + 10), 16))
    row.alpha_composite(tv, (3 * (W + 10), 16))
    ImageDraw.Draw(row).text((4, 2), name, fill=(255, 255, 255, 255))
    rows.append(row)
sheet = Image.new("RGBA", (max(r.width for r in rows), sum(r.height for r in rows)), (84, 104, 64, 255))
y = 0
for r in rows:
    sheet.alpha_composite(r, (0, y))
    y += r.height
os.makedirs(os.path.join(OUT, "work"), exist_ok=True)
sheet.save(os.path.join(OUT, "work", "look.png"))
