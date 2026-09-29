"""Corn colour numbers, picture against render, system Python:

    python cornstats.py Name [suffix]

Green share (G-R > 12), the 40-60 luminance band mean, bright (> 80),
dark (< 40) and tassel (pinkish pale, R >= G) shares and the soil tone.
"""
import sys

import numpy as np
from PIL import Image

OUT = r"D:\OKReplace\hand\props\renders"


def stats(im):
    a = np.array(im.convert("RGBA")).astype(np.float32)
    px = a[..., :3][a[..., 3] > 128]
    lum = px @ np.array([0.299, 0.587, 0.114])
    R, G, B = px[:, 0], px[:, 1], px[:, 2]
    band = px[(lum >= 40) & (lum <= 60)]
    tas = (lum > 90) & (R >= G - 2) & (R - B < 45)
    dk = px[lum < 30]
    return ("green%%=%4.1f band=%s bright%%=%4.1f dark%%=%4.1f tassel%%=%4.1f tas=%s lit=%s dk=%s mean=%s" % (
        100 * ((G - R) > 12).mean(), tuple(int(v) for v in band.mean(0)), 100 * (lum > 80).mean(),
        100 * (lum < 40).mean(), 100 * tas.mean(), tuple(int(v) for v in px[tas].mean(0)) if tas.any() else None,
        tuple(int(v) for v in px[lum > 80].mean(0)), tuple(int(v) for v in dk.mean(0)) if len(dk) else None,
        tuple(int(v) for v in px.mean(0))))


name = sys.argv[1]
suf = sys.argv[2] if len(sys.argv) > 2 else ""
spr = Image.open(r"D:\OKReplace\sprites\%s.png" % name)
ren = Image.open(r"%s\%s%s_classic.png" % (sys.argv[3] if len(sys.argv) > 3 else OUT, name, suf))
print("pic ", stats(spr))
print("ren ", stats(ren.resize(spr.size, Image.BOX)))
