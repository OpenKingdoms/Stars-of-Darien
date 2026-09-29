"""A zoomed sprite with a pixel grid, for reading coordinates off it.

    python ruler.py <name> <out.png> [scale] [x0 y0 x1 y1]

Thin lines every 4 px, stronger every 16, labels every 16; the hotspot
is marked with a cross.
"""
import json
import sys

from PIL import Image, ImageDraw

name, out = sys.argv[1], sys.argv[2]
k = int(sys.argv[3]) if len(sys.argv) > 3 else 6
cat = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}
hx, hy = cat[name]["sprite"]["hotspot"]
im = Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")
box = [int(v) for v in sys.argv[4:8]] if len(sys.argv) > 7 else [0, 0, im.width, im.height]
bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
bg.alpha_composite(im)
c = bg.crop(box).resize(((box[2] - box[0]) * k, (box[3] - box[1]) * k), Image.NEAREST)
d = ImageDraw.Draw(c)
for x in range(box[0], box[2] + 1):
    if x % 4 == 0:
        X = (x - box[0]) * k
        d.line([(X, 0), (X, c.height)], fill=(255, 255, 0, 200) if x % 16 == 0 else (0, 0, 0, 70))
        if x % 16 == 0:
            d.text((X + 2, 2), str(x), fill=(255, 255, 0, 255))
for y in range(box[1], box[3] + 1):
    if y % 4 == 0:
        Y = (y - box[1]) * k
        d.line([(0, Y), (c.width, Y)], fill=(0, 255, 255, 200) if y % 16 == 0 else (0, 0, 0, 70))
        if y % 16 == 0:
            d.text((2, Y + 2), str(y), fill=(0, 255, 255, 255))
X, Y = (hx - box[0]) * k, (hy - box[1]) * k
d.line([(X - 10, Y), (X + 10, Y)], fill=(255, 0, 0, 255), width=2)
d.line([(X, Y - 10), (X, Y + 10)], fill=(255, 0, 0, 255), width=2)
c.save(out)
