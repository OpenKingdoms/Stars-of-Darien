"""Silhouette and colour fit of props against their pictures, system Python:

    python fit.py [-d dir] [-s suffix] <Name> [<Name> ...]

For each, reads <Name><suffix>_classic.png (renders/ by default), scales it
to the picture and prints IoU, coverage of the picture, the four edges and
the centroid of both, the long axis angle on screen, and the share of lime,
dark and pale pixels.
"""
import math
import os
import sys

import numpy as np
from PIL import Image

OUT = r"D:\OKReplace\hand\props"


def mask_stats(al):
    ys, xs = np.nonzero(al)
    cx, cy = xs.mean(), ys.mean()
    dx, dy = xs - cx, ys - cy
    a = 0.5 * math.degrees(math.atan2(2 * (dx * dy).mean(), (dx * dx).mean() - (dy * dy).mean()))
    return (xs.min(), ys.min(), xs.max(), ys.max()), (round(float(cx), 1), round(float(cy), 1)), round(-a, 1)


def shares(rgb, al):
    px = rgb[al].astype(np.float32)
    r, g, b = px[:, 0], px[:, 1], px[:, 2]
    lum = px @ np.array([0.299, 0.587, 0.114])
    lime = (g >= 88) & (g > r * 1.12) & (b < g * 0.62)
    pale = (lum > 80) & (px.max(1) - px.min(1) < 40)
    return {"lime%": round(float(100 * lime.mean()), 1), "dark%": round(float(100 * (lum < 40).mean()), 1),
            "pale%": round(float(100 * pale.mean()), 1), "lum": int(lum.mean())}


def main():
    args = sys.argv[1:]
    rdir, suf = os.path.join(OUT, "renders"), ""
    while args and args[0] in ("-d", "-s"):
        if args[0] == "-d":
            rdir = args[1]
        else:
            suf = args[1]
        args = args[2:]
    for name in args:
        spr = np.array(Image.open(r"D:\OKReplace\sprites\%s.png" % name).convert("RGBA"))
        im = Image.open(os.path.join(rdir, name + suf + "_classic.png")).convert("RGBA")
        ren = np.array(im.resize((spr.shape[1], spr.shape[0]), Image.BOX))
        a, b = spr[..., 3] > 128, ren[..., 3] > 128
        iou = (a & b).sum() / max(1, (a | b).sum())
        cov = (a & b).sum() / max(1, a.sum())
        print("%s%s IoU %.2f cov %.2f prec %.2f" % (name, suf, iou, cov, (a & b).sum() / max(1, b.sum())))
        for label, al, rgb in (("  picture", a, spr), ("  render ", b, ren)):
            box, c, ang = mask_stats(al)
            print(label, "box", tuple(int(v) for v in box), "centre", c, "axis", ang, shares(rgb[..., :3], al))


if __name__ == "__main__":
    main()
