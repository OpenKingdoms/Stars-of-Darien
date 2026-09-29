"""Enlarged sprite previews with the hotspot marked, for reading sprites."""
import json
import sys
from PIL import Image, ImageDraw

names = sys.argv[2].split(",")
scale = int(sys.argv[3]) if len(sys.argv) > 3 else 3
out = sys.argv[1]
cat = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}
ims = []
for n in names:
    r = cat[n]
    s = Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")
    big = s.resize((s.width * scale, s.height * scale), Image.NEAREST)
    bg = Image.new("RGBA", big.size, (84, 104, 64, 255))
    bg.alpha_composite(big)
    d = ImageDraw.Draw(bg)
    hx, hy = r["sprite"]["hotspot"]
    d.line([(hx * scale - 6, hy * scale), (hx * scale + 6, hy * scale)], fill=(255, 0, 0), width=1)
    d.line([(hx * scale, hy * scale - 6), (hx * scale, hy * scale + 6)], fill=(255, 0, 0), width=1)
    d.text((2, 2), n, fill=(255, 255, 255))
    ims.append(bg)
W = sum(i.width for i in ims) + 10 * len(ims)
H = max(i.height for i in ims)
sheet = Image.new("RGBA", (W, H), (40, 40, 40, 255))
x = 0
for i in ims:
    sheet.alpha_composite(i, (x, 0))
    x += i.width + 10
sheet.save(out)
print(out, sheet.size)
