"""Stacks tune.py variants of one prop for a side-by-side look, system Python:

    python tunesheet.py <Name> <tag> [<tag> ...]

Each row is the picture, the variant's classic render, their overlay and
its turned view, at 4x; written to work/<Name>_tune.png.
"""
import os
import sys

from PIL import Image, ImageDraw

WORK = r"D:\OKReplace\hand\props\work"
name, tags = sys.argv[1], sys.argv[2:]
spr = Image.open(os.path.join(r"D:\OKReplace\sprites", name + ".png")).convert("RGBA")
W, H = spr.width * 4, spr.height * 4
big = spr.resize((W, H), Image.NEAREST)
ghost = big.copy()
ghost.putalpha(big.getchannel("A").point(lambda a: a // 2))
rows = []
for t in tags:
    ren = Image.open(os.path.join(WORK, "%s_%s_classic.png" % (name, t))).convert("RGBA").resize((W, H), Image.LANCZOS)
    tv = Image.open(os.path.join(WORK, "%s_%s_turned.png" % (name, t))).convert("RGBA")
    tv.thumbnail((H * 2, H))
    row = Image.new("RGBA", (W * 3 + tv.width + 30, H + 16), (84, 104, 64, 255))
    for i, im in enumerate((big, ren, Image.alpha_composite(ren, ghost))):
        row.alpha_composite(im, (i * (W + 10), 16))
    row.alpha_composite(tv, (3 * (W + 10), 16))
    ImageDraw.Draw(row).text((4, 2), t, fill=(255, 255, 255, 255))
    rows.append(row)
sheet = Image.new("RGBA", (max(r.width for r in rows), sum(r.height for r in rows)), (84, 104, 64, 255))
y = 0
for r in rows:
    sheet.alpha_composite(r, (0, y))
    y += r.height
sheet.save(os.path.join(WORK, name + "_tune.png"))
