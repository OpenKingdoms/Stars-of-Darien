"""Hay tone of Aracart02, picture against a render, system Python:

    python haystats.py [render path]

Hay pixels are the yellow ones (blue under half of green, red near green);
prints their share, median and p10/p90 luminance and mean colour.
"""
import sys

import numpy as np
from PIL import Image


def hay(im):
    a = np.array(im.convert("RGBA")).astype(np.float32)
    px = a[..., :3][a[..., 3] > 128]
    R, G, B = px[:, 0], px[:, 1], px[:, 2]
    m = (B < 0.55 * G) & (R > 0.75 * G) & (G > 30)
    lum = px[m] @ np.array([0.299, 0.587, 0.114])
    return "hay%%=%.0f med=%d p10=%d p90=%d mean=%s" % (100 * m.mean(), np.median(lum), np.percentile(lum, 10),
                                                    np.percentile(lum, 90), tuple(int(v) for v in px[m].mean(0)))


spr = Image.open(r"D:\OKReplace\sprites\Aracart02.png")
print("pic", hay(spr))
for p in sys.argv[1:] or [r"D:\OKReplace\hand\props\renders\Aracart02_classic.png"]:
    print("ren", hay(Image.open(p).resize(spr.size, Image.BOX)), p[-30:])
