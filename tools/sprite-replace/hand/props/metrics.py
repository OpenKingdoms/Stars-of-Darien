"""Colour numbers for a prop against its picture, run with the system Python:

    python metrics.py <Name> [render dir] [suffix] [x0 y0 x1 y1]

Reads <Name><suffix>_classic.png (renders/ by default) and prints, for the
picture and the render (scaled to the picture, and at its own size) over
their own opaque pixels: the mean, the 10th and 90th percentile of
luminance, the mean of the green pixels and the share of dark (luminance
under 40) and pale (over 80, near grey) pixels. A box in picture pixels
limits all three to that region.
"""
import os
import sys

import numpy as np
from PIL import Image

OUT = r"D:\OKReplace\hand\props"


def stats(a, box=None):
    rgb, al = a[..., :3].astype(np.float32), a[..., 3] > 128
    if box:
        x0, y0, x1, y1 = box
        m = np.zeros_like(al)
        m[y0:y1, x0:x1] = True
        al &= m
    px = rgb[al]
    lum = px @ np.array([0.299, 0.587, 0.114])
    g = px[(px[:, 1] > px[:, 0]) & (px[:, 1] > px[:, 2])]
    pale = (lum > 80) & (px.max(1) - px.min(1) < 40)
    prof = np.array([(lum_r > 90).mean() if len(lum_r) else 0.0 for lum_r in
                     [(rgb[y][al[y]] @ np.array([0.299, 0.587, 0.114])) for y in range(rgb.shape[0])]])
    prof = prof[prof.nonzero()[0].min():prof.nonzero()[0].max() + 1] if prof.any() else prof
    prof = prof - prof.mean()
    ac = max((float((prof[:-k] * prof[k:]).mean() / max(1e-9, (prof * prof).mean())) for k in range(
        max(2, len(prof) // 9), max(3, len(prof) // 4))), default=0.0)
    return {"n": len(px), "bright%": float(round(100.0 * (lum > 80).mean(), 1)),
            "mid%": float(round(100.0 * ((lum >= 40) & (lum <= 80)).mean(), 1)), "rowac": round(ac, 2),
            "mean": tuple(int(v) for v in px.mean(0)), "lum": int(lum.mean()),
            "p10": int(np.percentile(lum, 10)), "p90": int(np.percentile(lum, 90)),
            "green": tuple(int(v) for v in g.mean(0)) if len(g) else None,
            "dark%": float(round(100.0 * (lum < 40).mean(), 1)), "pale%": float(round(100.0 * pale.mean(), 1))}


def main():
    name = sys.argv[1]
    rdir = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] != "-" else os.path.join(OUT, "renders")
    suf = sys.argv[3] if len(sys.argv) > 3 and sys.argv[3] != "-" else ""
    box = tuple(map(int, sys.argv[4:8])) if len(sys.argv) > 7 else None
    spr = Image.open(r"D:\OKReplace\sprites\%s.png" % name).convert("RGBA")
    full = Image.open(os.path.join(rdir, name + suf + "_classic.png")).convert("RGBA")
    ren = full.resize(spr.size, Image.BOX)
    for label, im, b in (("picture", spr, box), ("render ", ren, box),
                         ("render2", full, box and tuple(2 * v for v in box))):
        print(label, stats(np.array(im), b))


if __name__ == "__main__":
    main()
