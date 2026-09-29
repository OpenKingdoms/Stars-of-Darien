"""Reads a sprite as text: python probe.py <name> [step] [classes|rgb x0 y0 x1 y1].

classes prints one letter per pixel (every step-th): . clear, s cast shadow,
R roof tile, k dark stone, g grey stone, t timber, p plaster, w white, ? other.
rgb prints the median sRGB of a rectangle.
"""
import json
import sys

import numpy as np
from PIL import Image

SP = "D:/OKReplace/sprites/"
cat = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}


def load(n):
    return np.array(Image.open(SP + n + ".png").convert("RGBA")).astype(float)


def klass(p):
    r, g, b, a = p
    if a < 128:
        return "."
    mx, mn = max(r, g, b), min(r, g, b)
    lum = 0.3 * r + 0.59 * g + 0.11 * b
    if lum < 22:
        return "s"
    if r > 1.6 * g and r > 90:
        return "R"
    if lum > 190:
        return "w"
    if r > g * 1.12 and r > b * 1.3 and lum < 110:
        return "t"
    if lum < 55:
        return "k"
    if lum < 100:
        return "g"
    return "p"


if __name__ == "__main__":
    n = sys.argv[1]
    a = load(n)
    h, w = a.shape[:2]
    r = cat[n]
    print(n, "w", w, "h", h, "hot", r["sprite"]["hotspot"], "fp", r["footprint"], "ht", r["height"])
    if len(sys.argv) > 3 and sys.argv[3] == "rgb":
        x0, y0, x1, y1 = map(int, sys.argv[4:8])
        q = a[y0:y1, x0:x1].reshape(-1, 4)
        q = q[q[:, 3] > 128][:, :3]
        print("median", np.median(q, 0).round(), "mean", q.mean(0).round(), "n", len(q))
        sys.exit()
    step = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    hdr = "".join(str((x // 10) % 10) if x % 10 == 0 else " " for x in range(0, w, step))
    print("    " + hdr)
    for y in range(0, h, step):
        print("%3d " % y + "".join(klass(a[y, x]) for x in range(0, w, step)))
