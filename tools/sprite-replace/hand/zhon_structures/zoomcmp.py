"""Sprite and classic render of a region side by side, enlarged, with an outline
of the sprite drawn on the render.  python zoomcmp.py <Name> <out.png> [x0,y0,x1,y1] [scale]"""
import sys
import numpy as np
from PIL import Image, ImageFilter

n, out = sys.argv[1], sys.argv[2]
spr = Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")
import json, math
CAT = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}
hy = CAT[n]["sprite"]["hotspot"][1]
ren = Image.open("D:/OKReplace/hand/zhon_structures/renders/%s_classic.png" % n).convert("RGBA").resize(spr.size, Image.LANCZOS)
# undo the true ortho's vertical squeeze about the anchor row, so rows compare one to one
k = 1 / math.cos(math.atan(0.5))
tall = ren.resize((spr.width, int(round(spr.height * k))), Image.LANCZOS)
top = int(round(hy * k - hy))
canvas = Image.new("RGBA", spr.size, (0, 0, 0, 0))
canvas.alpha_composite(tall.crop((0, top, spr.width, top + spr.height)))
ren = canvas
box = tuple(int(v) for v in sys.argv[3].split(",")) if len(sys.argv) > 3 and sys.argv[3] != "all" else (0, 0, spr.width, spr.height)
s = int(sys.argv[4]) if len(sys.argv) > 4 else 3
bg = (84, 104, 64, 255)
tiles = []
a = np.asarray(spr)[..., 3] > 127
edge = Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.FIND_EDGES)
for im in (spr, ren):
    t = Image.new("RGBA", spr.size, bg)
    t.alpha_composite(im)
    tiles.append(t)
t = tiles[1].copy()
e = np.asarray(edge) > 0
arr = np.asarray(t).copy()
arr[e] = (255, 0, 255, 255)
tiles.append(Image.fromarray(arr))
tiles = [x.crop(box).resize(((box[2] - box[0]) * s, (box[3] - box[1]) * s), Image.NEAREST) for x in tiles]
W = sum(x.width for x in tiles) + 8 * 2
sheet = Image.new("RGBA", (W, tiles[0].height), (20, 20, 20, 255))
x = 0
for tt in tiles:
    sheet.alpha_composite(tt, (x, 0))
    x += tt.width + 8
sheet.save(out)
print(out, sheet.size)
