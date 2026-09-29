"""Sprite viewer: python look.py <out.png> <scale> name [name ...], labels and a hotspot cross."""
import sys
from PIL import Image, ImageDraw
import json
SP = "D:/OKReplace/sprites/"
cat = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}
out, sc, names = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
ims = []
for n in names:
    im = Image.open(SP + n + ".png").convert("RGBA")
    im = im.resize((im.width * sc, im.height * sc), Image.NEAREST)
    bg = Image.new("RGBA", (im.width + 8, im.height + 22), (84, 104, 64, 255))
    bg.alpha_composite(im, (4, 18))
    d = ImageDraw.Draw(bg)
    hx, hy = cat[n]["sprite"]["hotspot"]
    cx, cy = 4 + hx * sc, 18 + hy * sc
    d.line((cx - 6, cy, cx + 6, cy), fill=(255, 0, 0, 255))
    d.line((cx, cy - 6, cx, cy + 6), fill=(255, 0, 0, 255))
    d.text((4, 2), n, fill=(255, 255, 255, 255))
    ims.append(bg)
W = 1800
x = y = rowh = 0
pos = []
for im in ims:
    if x + im.width > W:
        x, y = 0, y + rowh + 6
        rowh = 0
    pos.append((x, y))
    x += im.width + 6
    rowh = max(rowh, im.height)
sheet = Image.new("RGBA", (W, y + rowh), (40, 40, 40, 255))
for im, p in zip(ims, pos):
    sheet.alpha_composite(im, p)
sheet.save(out)
print(out, sheet.size)
