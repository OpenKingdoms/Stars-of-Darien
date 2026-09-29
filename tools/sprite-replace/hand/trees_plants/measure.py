"""Silhouette numbers for built models, run with the system Python.

    python measure.py Name [Name ...]

Classic view: filled and spanned sprite pixels per height band against
the drawing's, frame-edge clipping and the top's offset. Turned view: the
model's height over its widest and median row, marker left out.
"""
import sys

import numpy as np
from PIL import Image

REN = "D:/OKReplace/hand/trees_plants/renders"
if "--prev" in sys.argv:
    REN = "D:/OKReplace/hand/trees_plants/work/r4/prev"
SPR = "D:/OKReplace/sprites"


def alpha(path):
    return np.asarray(Image.open(path).convert("RGBA"), np.float64)


def classic(name):
    s = alpha("%s/%s.png" % (SPR, name))
    sm = s[..., 3] > 127
    r = alpha("%s/%s_classic.png" % (REN, name))
    h, w = sm.shape
    rm = r[..., 3].reshape(h, 2, w, 2).mean((1, 3)) > 127
    rows = np.nonzero(sm.any(1))[0]
    top, bot = rows.min(), rows.max()
    out = []
    for f in (0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.97):
        y = int(top + f * (bot - top))
        band = slice(max(0, y - 1), y + 2)
        sf, rf = sm[band].sum(1).mean(), rm[band].sum(1).mean()

        def span(m):
            xs = np.nonzero(m[band].any(0))[0]
            return (xs.max() - xs.min() + 1) if len(xs) else 0
        out.append("%d%%:%d/%d(%d/%d)" % (f * 100, rf, sf, span(rm), span(sm)))
    rr = np.nonzero(rm.any(1))[0]
    clip = "L" * bool(rm[:, 0].sum() > 2) + "R" * bool(rm[:, -1].sum() > 2)
    ratio = rm.sum() / max(sm.sum(), 1)
    print("%s classic top %+d rows(2x) fill %.2f clip[%s]" % (name, 2 * (rr.min() - top) if len(rr) else 999, ratio, clip))
    print("   render/sprite filled (span):", " ".join(out))


def turned(name):
    t = alpha("%s/%s_turned.png" % (REN, name))
    a = t[..., 3] > 127
    blue = (t[..., 2] > t[..., 0] * 1.8) & (t[..., 2] > 120)
    cols = np.nonzero((a & blue).sum(0) > 20)[0]
    if len(cols):
        a[:, cols.min() - 1:] = False
    ys, xs = np.nonzero(a)
    H = ys.max() - ys.min() + 1
    widths = []
    for y in range(ys.min(), ys.max() + 1):
        x = np.nonzero(a[y])[0]
        widths.append(x.max() - x.min() + 1 if len(x) else 0)
    widths = np.array(widths)
    up = widths[: int(len(widths) * 0.5)]
    print("   turned H %d  H/max %.2f  H/bbox %.2f  H/median %.2f  upper-half median width %d" %
          (H, H / widths.max(), H / (xs.max() - xs.min() + 1), H / np.median(widths), np.median(up)))


if __name__ == "__main__":
    for n in [a for a in sys.argv[1:] if not a.startswith("--")]:
        classic(n)
        turned(n)
