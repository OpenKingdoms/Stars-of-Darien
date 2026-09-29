"""Numbers and a picture for the VerBuild05a lean-to region against the
drawing (system Python): python leanto_check.py [tag]"""
import sys

import numpy as np
from PIL import Image

OUT = "D:/OKReplace/hand/veruna_buildings"
R = (0, 40, 50, 133)


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def report(tag, a):
    x0, y0, x1, y1 = R
    c = a[y0:y1, x0:x1]
    m = c[..., 3] > 128
    L = lum(c[..., :3])
    px = c[m][:, :3]
    Lm = L[m]
    dx = np.abs(np.diff(L, axis=1))[m[:, 1:] & m[:, :-1]]
    dy = np.abs(np.diff(L, axis=0))[m[1:] & m[:-1]]
    grain = np.concatenate([dx, dy]).mean()
    pale = px[Lm > 180]
    print("%-8s cover %2d%%  >180 %2d%% (%s)  mid60-100 %2d%%  <50 %2d%%  grain %.1f  mean %s" % (
        tag, 100 * m.mean(), 100 * (Lm > 180).mean(), pale.mean(0).astype(int) if len(pale) else "-",
        100 * ((Lm >= 60) & (Lm <= 100)).mean(), 100 * (Lm < 50).mean(), grain, px.mean(0).astype(int)))
    return m


s = np.asarray(Image.open("D:/OKReplace/sprites/VerBuild05a.png").convert("RGBA")).astype(float)
r = Image.open(OUT + "/renders/VerBuild05a_classic.png").convert("RGBA")
r = np.asarray(r.resize((s.shape[1], s.shape[0]), Image.BILINEAR)).astype(float)
ms = report("drawing", s)
mr = report("model", r)
x0, y0, x1, y1 = (0, 30, 70, 133)
S, M = s[y0:y1, x0:x1, 3] > 128, r[y0:y1, x0:x1, 3] > 128
img = np.zeros(S.shape + (3,), np.uint8) + 30
img[S & M] = (150, 150, 150)
img[S & ~M] = (220, 60, 60)
img[~S & M] = (60, 90, 220)
tag = sys.argv[1] if len(sys.argv) > 1 else "cur"
side = np.concatenate([np.asarray(Image.fromarray(img)), np.full((S.shape[0], 4, 3), 0, np.uint8),
                       np.clip(r[y0:y1, x0:x1, :3], 0, 255).astype(np.uint8), np.full((S.shape[0], 4, 3), 0, np.uint8),
                       np.clip(s[y0:y1, x0:x1, :3], 0, 255).astype(np.uint8)], axis=1)
Image.fromarray(side).resize((side.shape[1] * 4, side.shape[0] * 4), Image.NEAREST).save(OUT + "/work/r5_%s.png" % tag)
