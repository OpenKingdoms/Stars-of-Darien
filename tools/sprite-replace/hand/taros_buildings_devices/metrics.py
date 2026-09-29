"""Silhouette and colour fit of a model against its sprite, system Python.

    python metrics.py [names...]

The classic render is stretched back up by 1/0.894 about the anchor row
(the handkit ortho squashes height) before the masks are compared. Prints
IoU, how much of the sprite the model covers, how much of the model spills
past the sprite, and median colours of each.
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image

OUT = r"D:\OKReplace\hand\taros_buildings_devices\renders"
cat = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
S = 2
K = 1 / (2 / math.sqrt(5))  # the ortho's vertical squash, undone


def fit(n):
    r = cat[n]
    hx, hy = r["sprite"]["hotspot"]
    spr = np.array(Image.open(r"D:\OKReplace\sprites\%s.png" % n).convert("RGBA")).astype(float)
    W, H = spr.shape[1], spr.shape[0]
    ren = Image.open(os.path.join(OUT, n + "_classic.png")).convert("RGBA").resize((W, H), Image.BILINEAR)
    ren = np.array(ren).astype(float)
    # stretch rows about the anchor
    rows = np.clip(np.round(hy + (np.arange(H) - hy) / K).astype(int), 0, H - 1)
    st = ren[rows]
    a = spr[..., 3] > 128
    b = st[..., 3] > 128
    iou = (a & b).sum() / max(1, (a | b).sum())
    cover = (a & b).sum() / max(1, a.sum())
    spill = (b & ~a).sum() / max(1, b.sum())
    ms = np.median(spr[a][:, :3], 0).astype(int)
    mr = np.median(st[b][:, :3], 0).astype(int) if b.any() else None
    both = a & b
    ls = spr[both][:, :3].mean() if both.any() else 0
    lr = st[both][:, :3].mean() if both.any() else 0
    return iou, cover, spill, tuple(ms), tuple(mr) if mr is not None else None, ls, lr


for n in sys.argv[1:]:
    if not os.path.exists(os.path.join(OUT, n + "_classic.png")):
        continue
    iou, cover, spill, ms, mr, ls, lr = fit(n)
    print("%-12s IoU %.2f cover %.2f spill %.2f  sprite %s model %s  lum %.0f/%.0f" % (n, iou, cover, spill, ms, mr,
                                                                                   ls, lr))


def profile(n, cols=10):
    """Top and bottom opaque rows at a spread of columns, sprite vs model
    (model stretched back up), and left/right extents at a spread of rows."""
    r = cat[n]
    hx, hy = r["sprite"]["hotspot"]
    spr = np.array(Image.open(r"D:\OKReplace\sprites\%s.png" % n).convert("RGBA"))
    W, H = spr.shape[1], spr.shape[0]
    ren = np.array(Image.open(os.path.join(OUT, n + "_classic.png")).convert("RGBA").resize((W, H), Image.BILINEAR))
    rows = np.clip(np.round(hy + (np.arange(H) - hy) / K).astype(int), 0, H - 1)
    a, b = spr[..., 3] > 128, ren[rows][..., 3] > 128
    out = []
    for c in np.linspace(W * 0.05, W * 0.95, cols).astype(int):
        def tb(m):
            ys = np.where(m[:, c])[0]
            return (int(ys[0]), int(ys[-1])) if len(ys) else None
        out.append("c%d s%s m%s" % (c, tb(a), tb(b)))
    print(n, "columns:", "  ".join(out))
    out = []
    for y in np.linspace(H * 0.05, H * 0.95, cols).astype(int):
        def lr(m):
            xs = np.where(m[y])[0]
            return (int(xs[0]), int(xs[-1])) if len(xs) else None
        out.append("r%d s%s m%s" % (y, lr(a), lr(b)))
    print(n, "rows:", "  ".join(out))


if os.environ.get("PROFILE"):
    for n in sys.argv[1:]:
        profile(n)
