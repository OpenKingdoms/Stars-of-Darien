"""Compare plus turned view for each mound, and the judge's numbers, with the
system Python: python look.py Name,Name [k]  -> view3/look.png"""
import os
import subprocess
import sys

import numpy as np
from PIL import Image

OUT = "D:/OKReplace/hand/zhon_structures"
TOOLS = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))


def stats(a, h):
    m = a[..., 3] > 127
    lum = a[..., :3] @ np.array([0.299, 0.587, 0.114])
    rows = np.where(m.any(1))[0]
    L = lum[m]

    def width(f):
        c = np.where(m[int(round(f * (h - 1)))])[0]
        return int(c[-1] - c[0] + 1) if len(c) else 0
    return "top %2d bot %3d med %3.0f p10 %2.0f p90 %3.0f black %.3f w%s" % (
        rows[0], rows[-1], np.median(L), np.percentile(L, 10), np.percentile(L, 90), (L < 20).mean(),
        [width(f) for f in (0.1, 0.2, 0.4, 0.6, 0.8, 0.95, 1.0)])


names = sys.argv[1].split(",")
k = float(sys.argv[2]) if len(sys.argv) > 2 else 1.5
ims = []
for n in names:
    subprocess.run([sys.executable, os.path.join(TOOLS, "handcompare.py"), OUT + "/renders", n,
                    "D:/OKReplace/sprites/%s.png" % n, "2"], check=True)
    spr = Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")
    ren = Image.open("%s/renders/%s_classic.png" % (OUT, n)).convert("RGBA").resize(spr.size, Image.LANCZOS)
    print("%-12s sprite %s" % (n, stats(np.asarray(spr).astype(float), spr.height)))
    print("%-12s render %s" % (n, stats(np.asarray(ren).astype(float), spr.height)))
    c = Image.open("%s/renders/%s_compare.png" % (OUT, n)).convert("RGBA")
    ims.append(c.resize((int(c.width * k), int(c.height * k)), Image.LANCZOS))
out = Image.new("RGBA", (max(i.width for i in ims), sum(i.height + 4 for i in ims)), (30, 30, 30, 255))
y = 0
for i in ims:
    out.alpha_composite(i, (0, y))
    y += i.height + 4
os.makedirs(OUT + "/view3", exist_ok=True)
out.save(OUT + "/view3/look.png")
print(out.size)
