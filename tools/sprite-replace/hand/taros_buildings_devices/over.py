"""The overlay panel of a compare, enlarged with sprite-pixel ticks, and the
render's rows stretched back by the ortho's squash about the anchor.

    python over.py <name> [x0 y0 x1 y1] [scale]
"""
import json
import math
import sys

from PIL import Image, ImageDraw

n = sys.argv[1]
cat = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
r = cat[n]
hx, hy = r["sprite"]["hotspot"]
spr = Image.open(r"D:\OKReplace\sprites\%s.png" % n).convert("RGBA")
W, H = spr.size
box = tuple(int(v) for v in sys.argv[2:6]) if len(sys.argv) >= 6 else (0, 0, W, H)
s = int(sys.argv[6]) if len(sys.argv) >= 7 else 3
ren = Image.open(r"D:\OKReplace\hand\taros_buildings_devices\renders\%s_classic.png" % n).convert("RGBA")
ren = ren.resize((W * s, H * s), Image.BILINEAR)
if "--raw" not in sys.argv:
    import numpy as np
    K = 1 / (2 / math.sqrt(5))
    arr = np.array(ren)
    rows = np.clip(np.round(hy * s + (np.arange(H * s) - hy * s) / K).astype(int), 0, H * s - 1)
    ren = Image.fromarray(arr[rows])
big = spr.resize((W * s, H * s), Image.NEAREST)
bg = Image.new("RGBA", big.size, (84, 104, 64, 255))
mode = sys.argv[7] if len(sys.argv) >= 8 else "over"
if mode == "over":
    g = big.copy()
    g.putalpha(big.getchannel("A").point(lambda a: a * 55 // 100))
    bg.alpha_composite(ren)
    bg.alpha_composite(g)
else:
    # the two outlines: sprite edge red, model edge yellow
    bg.alpha_composite(ren)
    import numpy as np
    a = np.array(big.getchannel("A")) > 128
    b = np.array(ren.getchannel("A")) > 128
    ea = a ^ np.roll(a, 1, 0) | a ^ np.roll(a, 1, 1)
    eb = b ^ np.roll(b, 1, 0) | b ^ np.roll(b, 1, 1)
    px = np.array(bg)
    px[ea] = (255, 40, 40, 255)
    px[eb] = (255, 255, 0, 255)
    bg = Image.fromarray(px)
x0, y0, x1, y1 = box
c = bg.crop((x0 * s, y0 * s, x1 * s, y1 * s))
d = ImageDraw.Draw(c)
for x in range((x0 // 10) * 10, x1, 10):
    d.line([((x - x0) * s, 0), ((x - x0) * s, 6)], fill=(255, 255, 0, 255))
    d.text(((x - x0) * s + 1, 7), str(x), fill=(255, 255, 0, 255))
for y in range((y0 // 10) * 10, y1, 10):
    d.line([(0, (y - y0) * s), (6, (y - y0) * s)], fill=(0, 255, 255, 255))
    d.text((8, (y - y0) * s - 5), str(y), fill=(0, 255, 255, 255))
p = r"D:\OKReplace\hand\taros_buildings_devices\scratch\%s_over.png" % n
c.save(p)
print(p, c.size)
