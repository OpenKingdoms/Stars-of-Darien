"""Tone and coverage numbers for the drawing against the classic render,
run with the system Python.

    python stats.py <Name> [<Name> ...] [--rect x0 y0 x1 y1]

For the drawn pixels of each: mean colour, the mean of the brightest
tenth, the 10th-percentile luminance and the share brighter than 160;
then how much of the drawing the render covers and how much it adds.
"""
import os
import sys

import numpy as np
from PIL import Image

REN = "D:/OKReplace/hand/veruna_buildings/renders"
SPR = "D:/OKReplace/sprites"


def lum(a):
    return a[:, 0] * 0.299 + a[:, 1] * 0.587 + a[:, 2] * 0.114


def tones(px):
    if not len(px):
        return "empty"
    L = lum(px)
    top = px[L >= np.percentile(L, 90)].mean(0)
    return "mean %3d %3d %3d  top10 %3d %3d %3d  p10L %3d  >160 %2d%%" % (
        *px.mean(0), *top, np.percentile(L, 10), 100 * (L > 160).mean())


def stats(name, rect=None):
    s = np.asarray(Image.open(os.path.join(SPR, name + ".png")).convert("RGBA")).astype(float)
    r = Image.open(os.path.join(REN, name + "_classic.png")).convert("RGBA")
    r = np.asarray(r.resize((s.shape[1], s.shape[0]), Image.BILINEAR)).astype(float)
    if rect:
        x0, y0, x1, y1 = rect
        s, r = s[y0:y1, x0:x1], r[y0:y1, x0:x1]
    ms, mr = s[..., 3] > 128, r[..., 3] > 128
    print(name, rect or "")
    print("  drawing", tones(s[ms][:, :3]))
    print("  render ", tones(r[mr][:, :3]))
    both = (ms & mr).sum()
    print("  covers %d%% of the drawing, adds %d%%" % (100 * both / max(1, ms.sum()),
                                                      100 * (mr & ~ms).sum() / max(1, ms.sum())))


if __name__ == "__main__":
    args = sys.argv[1:]
    rect = None
    if "--rect" in args:
        i = args.index("--rect")
        rect = tuple(int(v) for v in args[i + 1:i + 5])
        args = args[:i] + args[i + 5:]
    for n in args:
        stats(n, rect)
