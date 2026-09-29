"""A close look at part of a model: the drawing, the classic render and the
turned view, run with the system Python.

    python zoom.py <Name> x0 y0 x1 y1 [scale]

x0..y1 are sprite pixels. Writes work/zoom.png.
"""
import os
import sys

from PIL import Image

REN = "D:/OKReplace/hand/veruna_buildings/renders"
SPR = "D:/OKReplace/sprites"
name = sys.argv[1]
x0, y0, x1, y1 = (int(v) for v in sys.argv[2:6])
s = int(sys.argv[6]) if len(sys.argv) > 6 else 4
spr = Image.open(os.path.join(SPR, name + ".png")).convert("RGBA")
ren = Image.open(os.path.join(REN, name + "_classic.png")).convert("RGBA")
k = ren.width / spr.width
a = spr.crop((x0, y0, x1, y1)).resize(((x1 - x0) * s, (y1 - y0) * s), Image.NEAREST)
b = ren.crop((int(x0 * k), int(y0 * k), int(x1 * k), int(y1 * k))).resize(a.size, Image.BILINEAR)
bg = (84, 104, 64, 255)
out = Image.new("RGBA", (a.width * 2 + 10, a.height), bg)
out.alpha_composite(Image.alpha_composite(Image.new("RGBA", a.size, bg), a), (0, 0))
out.alpha_composite(Image.alpha_composite(Image.new("RGBA", b.size, bg), b), (a.width + 10, 0))
t = os.path.join(REN, name + "_turned.png")
if len(sys.argv) > 7 and os.path.exists(t):
    tv = Image.open(t).convert("RGBA")
    full = Image.new("RGBA", (out.width + tv.width + 10, max(out.height, tv.height)), bg)
    full.alpha_composite(out, (0, 0))
    full.alpha_composite(Image.alpha_composite(Image.new("RGBA", tv.size, bg), tv), (out.width + 10, 0))
    out = full
p = "D:/OKReplace/hand/veruna_buildings/work/zoom.png"
out.convert("RGB").save(p)
print(p)
