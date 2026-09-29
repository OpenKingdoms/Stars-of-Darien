"""Tone numbers for a bespoke model against its sprite, system Python.

    python measure.py <name> [cells x0 y0 x1 y1 ...]

Blurred-luma correlation (Gaussian radius 4 at sprite scale) over the
pixels both draw, the share of each under luma 40, mean RGB, and the
sprite/model median luma of the named 20 px cells (top-left corners).
"""
import sys

import numpy as np
from PIL import Image, ImageFilter

OUT = "D:/OKReplace/hand/aramon_buildings"
LW = np.array([0.299, 0.587, 0.114])


def load(name):
    spr = Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")
    ren = Image.open("%s/renders/%s_classic.png" % (OUT, name)).convert("RGBA").resize(spr.size, Image.LANCZOS)
    return np.array(spr).astype(float), np.array(ren).astype(float)


def blur_l(a, rad=4):
    lum = (a[..., :3] @ LW) * (a[..., 3] / 255.0)
    w = Image.fromarray((a[..., 3]).astype(np.uint8)).filter(ImageFilter.GaussianBlur(rad))
    l_ = Image.fromarray(np.clip(lum, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(rad))
    return np.array(l_).astype(float) / np.maximum(1.0, np.array(w).astype(float)) * 255.0


def main():
    name = sys.argv[1]
    s, m = load(name)
    ms, mm = s[..., 3] > 127, m[..., 3] > 127
    both = ms & mm
    bs, bm = blur_l(s), blur_l(m)
    corr = np.corrcoef(bs[both], bm[both])[0, 1]
    ls, lm = s[..., :3] @ LW, m[..., :3] @ LW
    print("%s corr %.3f  under40 sprite %.3f model %.3f  mean sprite %s model %s" % (
        name, corr, (ls[ms] < 40).mean(), (lm[mm] < 40).mean(),
        tuple(int(v) for v in s[ms][:, :3].mean(0)), tuple(int(v) for v in m[mm][:, :3].mean(0))))
    cells = [int(v) for v in sys.argv[2:]]
    for i in range(0, len(cells) - 1, 2):
        x, y = cells[i], cells[i + 1]
        a, b = ms[y:y + 20, x:x + 20], mm[y:y + 20, x:x + 20]
        vs = np.median(ls[y:y + 20, x:x + 20][a]) if a.any() else -1
        vm = np.median(lm[y:y + 20, x:x + 20][b]) if b.any() else -1
        print("  (%d,%d) %d/%d" % (x, y, vs, vm))


if __name__ == "__main__":
    main()
