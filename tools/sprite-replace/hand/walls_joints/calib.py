"""Colour check (system Python): python calib.py names...

Resamples each classic render onto the sprite's pixels (the render's ortho
camera shows heights at 0.894 of the classic drawing, about the anchor)
and prints, per class of sprite pixel, the sprite's median colour, the
render's, and their ratio.
"""
import json
import math
import sys

import numpy as np
from PIL import Image

import probe

REN = "D:/OKReplace/hand/walls_joints/renders/"
K = math.sin(math.atan(2.0))


def aligned(name, scale=2):
    a = probe.load(name)
    h, w = a.shape[:2]
    hx, hy = probe.cat[name]["sprite"]["hotspot"]
    r = np.array(Image.open(REN + name + "_classic.png").convert("RGBA")).astype(float)
    out = np.zeros_like(a)
    for y in range(h):
        ry = int(round((hy + (y + 0.5 - hy) * K) * scale))
        if not 0 <= ry < r.shape[0]:
            continue
        for x in range(w):
            rx = int((x + 0.5) * scale)
            if 0 <= rx < r.shape[1]:
                out[y, x] = r[ry, rx]
    return a, out


def report(names, classes="RktgpW"):
    acc = {}
    for n in names:
        a, r = aligned(n)
        for y in range(a.shape[0]):
            for x in range(a.shape[1]):
                if a[y, x, 3] < 128 or r[y, x, 3] < 128:
                    continue
                c = probe.klass(a[y, x])
                acc.setdefault(c, ([], []))
                acc[c][0].append(a[y, x, :3])
                acc[c][1].append(r[y, x, :3])
    for c, (s, rr) in sorted(acc.items()):
        s, rr = np.median(np.array(s), 0), np.median(np.array(rr), 0)
        print("%s n=%5d sprite %s render %s ratio %s" % (c, len(acc[c][0]), s.round(), rr.round(),
                                                         (s / np.maximum(rr, 1)).round(2)))


if __name__ == "__main__":
    report(sys.argv[1:])
