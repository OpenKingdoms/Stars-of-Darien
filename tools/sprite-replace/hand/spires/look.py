"""Zoomed sprite views with a cell grid, run with the system Python.

    python look.py Name [scale]

Vertical lines every cell east of the anchor (16 px), horizontal lines every
cell of height at the anchor (8 px), written to D:/OKReplace/hand/spires/work.
"""
import json
import sys

from PIL import Image, ImageDraw

CAT = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
name = sys.argv[1]
S = int(sys.argv[2]) if len(sys.argv) > 2 else 5
im = Image.open(r"D:\OKReplace\sprites\%s.png" % name).convert("RGBA")
hx, hy = CAT[name]["sprite"]["hotspot"]
big = im.resize((im.width * S, im.height * S), Image.NEAREST)
bg = Image.new("RGBA", big.size, (84, 104, 64, 255))
bg.alpha_composite(big)
d = ImageDraw.Draw(bg)
for k in range(-12, 13):
    x = (hx + 16 * k) * S
    if 0 <= x < bg.width:
        d.line((x, 0, x, bg.height), fill=(255, 255, 0, 90) if k else (255, 0, 0, 160))
for k in range(-30, 30):
    y = (hy - 8 * k) * S
    if 0 <= y < bg.height:
        d.line((0, y, bg.width, y), fill=(0, 255, 255, 70) if k else (255, 0, 0, 160))
        d.text((2, y + 1), "z%d" % k, fill=(255, 255, 255, 200))
bg.save(r"D:\OKReplace\hand\spires\work\look_%s.png" % name)
