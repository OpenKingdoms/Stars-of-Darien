"""Marks see-through pixels inside a classic render's silhouette in magenta.
    python seamview.py Name,Name,... <out dir>"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import metrics  # noqa: E402

for n in sys.argv[1].split(","):
    full = np.asarray(Image.open(os.path.join(metrics.OUT, n + "_classic.png")).convert("RGBA")).astype(float)
    fa = full[..., 3]
    border = np.zeros(fa.shape, bool)
    border[0], border[-1], border[:, 0], border[:, -1] = True, True, True, True
    outside = metrics._flood(border, fa < 128)
    inner = ~metrics._grow(outside, 3)
    seam = inner & (fa < 250)
    bg = np.zeros_like(full)
    bg[...] = (84, 104, 64, 255)
    a = fa[..., None] / 255
    out = bg * (1 - a) + full * a
    out[..., 3] = 255
    out[seam] = (255, 0, 255, 255)
    ys, xs = np.nonzero(seam)
    print(n, int(seam.sum()), "rows %s..%s cols %s..%s" % ((ys.min(), ys.max(), xs.min(), xs.max()) if len(ys) else ("-",) * 4))
    Image.fromarray(out.astype(np.uint8)).save(os.path.join(sys.argv[2], n + "_seams.png"))
