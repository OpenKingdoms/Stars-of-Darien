"""A sprite blown up with a cell grid through its anchor, for measuring by eye.

    python gridview.py <name> [scale] [x0 y0 x1 y1]

Vertical lines every cell east of the anchor (16 px), horizontal lines every
half cell (8 px, one cell of height at the anchor's depth), labelled in
sprite pixels. Writes D:/OKReplace/hand/taros_buildings_devices/scratch/<name>_grid.png.
"""
import json
import sys

from PIL import Image, ImageDraw

ROOT = "D:/OKReplace"
OUT = ROOT + "/hand/taros_buildings_devices/scratch"
name = sys.argv[1]
s = int(sys.argv[2]) if len(sys.argv) > 2 else 3
cat = {r["name"]: r for r in json.load(open(ROOT + "/catalog.json"))}
hx, hy = cat[name]["sprite"]["hotspot"]
im = Image.open("%s/sprites/%s.png" % (ROOT, name)).convert("RGBA")
if len(sys.argv) > 6:
    box = tuple(int(v) for v in sys.argv[3:7])
else:
    box = (0, 0, im.width, im.height)
im = im.crop(box)
ox, oy = box[0], box[1]
bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
bg.alpha_composite(im)
big = bg.resize((im.width * s, im.height * s), Image.NEAREST)
d = ImageDraw.Draw(big)
for px in range(-40, 60):
    c = hx + 16 * px - ox
    if 0 <= c < im.width:
        col = (255, 60, 60, 255) if px == 0 else (255, 255, 0, 110)
        d.line([(c * s, 0), (c * s, big.height)], fill=col, width=1)
        d.text((c * s + 2, 2), str(hx + 16 * px), fill=(255, 255, 255, 255))
for k in range(-80, 80):
    r = hy - 8 * k - oy
    if 0 <= r < im.height:
        col = (255, 60, 60, 255) if k == 0 else ((0, 255, 255, 140) if k % 2 == 0 else (0, 255, 255, 60))
        d.line([(0, r * s), (big.width, r * s)], fill=col, width=1)
        if k % 2 == 0:
            d.text((2, r * s + 1), str(hy - 8 * k), fill=(255, 255, 255, 255))
big.save("%s/%s_grid.png" % (OUT, name))
print("%s/%s_grid.png" % (OUT, name), big.size)
