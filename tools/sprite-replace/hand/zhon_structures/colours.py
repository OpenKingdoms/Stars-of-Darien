"""Luminance-quantile colours of a sprite region: dark, mid, light tones."""
import sys
import numpy as np
from PIL import Image


def tones(n, box=None, q=(10, 30, 50, 70, 90)):
    a = np.asarray(Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")).astype(float)
    if box:
        x0, y0, x1, y1 = box
        a = a[y0:y1, x0:x1]
    px = a[a[..., 3] > 127][:, :3]
    lum = px @ [0.299, 0.587, 0.114]
    out = []
    for p in q:
        lo, hi = np.percentile(lum, max(0, p - 10)), np.percentile(lum, min(100, p + 10))
        sel = px[(lum >= lo) & (lum <= hi)]
        out.append(tuple(int(v) for v in sel.mean(0)))
    return out


if __name__ == "__main__":
    n = sys.argv[1]
    box = tuple(int(v) for v in sys.argv[2].split(",")) if len(sys.argv) > 2 else None
    for p, c in zip((10, 30, 50, 70, 90), tones(n, box)):
        print(n, box, "p%d" % p, c, "lin %.3f %.3f %.3f" % tuple((v / 255) ** 2.2 for v in c))
