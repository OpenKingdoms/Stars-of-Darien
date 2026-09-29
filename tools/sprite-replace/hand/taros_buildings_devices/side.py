"""Sprite and stretched render side by side over a crop, with pixel ticks.

    python side.py <name> x0 y0 x1 y1 [scale]
"""
import json
import math
import sys

import numpy as np
from PIL import Image, ImageDraw

n = sys.argv[1]
x0, y0, x1, y1 = (int(v) for v in sys.argv[2:6])
s = int(sys.argv[6]) if len(sys.argv) > 6 else 3
cat = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
hx, hy = cat[n]["sprite"]["hotspot"]
spr = Image.open(r"D:\OKReplace\sprites\%s.png" % n).convert("RGBA")
W, H = spr.size
ren = Image.open(r"D:\OKReplace\hand\taros_buildings_devices\renders\%s_classic.png" % n).convert("RGBA")
ren = ren.resize((W * s, H * s), Image.BILINEAR)
K = 1 / (2 / math.sqrt(5))
arr = np.array(ren)
rows = np.clip(np.round(hy * s + (np.arange(H * s) - hy * s) / K).astype(int), 0, H * s - 1)
ren = Image.fromarray(arr[rows])
big = spr.resize((W * s, H * s), Image.NEAREST)
out = []
for im in (big, ren):
    bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
    bg.alpha_composite(im)
    c = bg.crop((x0 * s, y0 * s, x1 * s, y1 * s))
    d = ImageDraw.Draw(c)
    for x in range((x0 // 10) * 10, x1, 10):
        d.line([((x - x0) * s, 0), ((x - x0) * s, 5)], fill=(255, 255, 0, 255))
        if x % 20 == 0:
            d.text(((x - x0) * s + 1, 6), str(x), fill=(255, 255, 0, 255))
    for y in range((y0 // 10) * 10, y1, 10):
        d.line([(0, (y - y0) * s), (5, (y - y0) * s)], fill=(0, 255, 255, 255))
        if y % 20 == 0:
            d.text((7, (y - y0) * s - 5), str(y), fill=(0, 255, 255, 255))
    out.append(c)
sheet = Image.new("RGBA", (out[0].width * 2 + 8, out[0].height), (20, 20, 20, 255))
sheet.alpha_composite(out[0], (0, 0))
sheet.alpha_composite(out[1], (out[0].width + 8, 0))
p = r"D:\OKReplace\hand\taros_buildings_devices\scratch\%s_side.png" % n
sheet.save(p)
print(p, sheet.size)
