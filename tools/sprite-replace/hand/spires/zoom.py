"""Close-up of a compare for iterating, system Python:

    python zoom.py [try] Name ...  ->  work/<Name>_zoom.png

Shows the picture, the classic render, a fit map (green both, red picture
only, blue render only, the render stretched back by 1/0.894 about the
anchor row) and the turned view, and prints the cover and spill. With try,
it reads the trial builds in work/try (see variants.py).
"""
import json
import os
import sys

import numpy as np
from PIL import Image

OUT = r"D:\OKReplace\hand\spires"
CAT = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
K = 2 / 5 ** 0.5
args = sys.argv[1:]
trial = bool(args) and args[0] == "try"
names = args[1:] if trial else args
ren_dir = os.path.join(OUT, "work", "try", "renders") if trial else os.path.join(OUT, "renders")
for n in names:
    base = n.split("_")[0]
    spr = Image.open(r"D:\OKReplace\sprites\%s.png" % base).convert("RGBA")
    ren = Image.open(os.path.join(ren_dir, n + "_classic.png")).convert("RGBA")
    S = 3
    W, H = spr.width * S, spr.height * S
    bg = (60, 76, 48, 255)
    big = Image.new("RGBA", (W, H), bg)
    big.alpha_composite(spr.resize((W, H), Image.NEAREST))
    rr = Image.new("RGBA", (W, H), bg)
    rr.alpha_composite(ren.resize((W, H), Image.LANCZOS))
    a = np.array(spr).astype(float)
    b = np.array(ren.resize(spr.size, Image.BOX)).astype(float)
    hy = CAT[base]["sprite"]["hotspot"][1]
    rows = np.clip(np.round(hy + (np.arange(spr.height) - hy) * K).astype(int), 0, spr.height - 1)
    b = b[rows]
    ma, mb = a[..., 3] > 128, b[..., 3] > 128
    m = np.zeros(a.shape[:2] + (3,), np.uint8)
    m[:] = (40, 50, 34)
    m[ma & mb] = (60, 170, 60)
    m[ma & ~mb] = (220, 40, 40)
    m[mb & ~ma] = (50, 80, 230)
    fit = Image.fromarray(m).resize((W, H), Image.NEAREST).convert("RGBA")
    t = Image.open(os.path.join(ren_dir, n + "_turned.png")).convert("RGBA")
    t = t.resize((H, H), Image.LANCZOS) if t.height != H else t
    out = Image.new("RGBA", (W * 3 + t.width + 30, H), bg)
    for i, im in enumerate((big, rr, fit)):
        out.alpha_composite(im, (i * (W + 10), 0))
    out.alpha_composite(t, (3 * (W + 10), 0))
    if out.width > 1900:
        out = out.resize((1900, int(out.height * 1900 / out.width)), Image.LANCZOS)
    path = os.path.join(OUT, "work", n + "_zoom.png")
    out.save(path)
    cover = (ma & mb).sum() / max(1, ma.sum())
    spill = (mb & ~ma).sum() / max(1, mb.sum())
    print("%s cover %.2f spill %.2f  picture %s render %s" % (
        n, cover, spill, tuple(int(v) for v in a[ma][:, :3].mean(0)), tuple(int(v) for v in b[mb][:, :3].mean(0))))
