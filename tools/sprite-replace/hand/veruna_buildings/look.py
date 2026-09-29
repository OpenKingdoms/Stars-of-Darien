"""Reading aids for the Veruna sprites, run with the system Python.

    python look.py grid <Name> [scale] [x0 y0 x1 y1]   gridded zoom into the work dir
    python look.py sample <Name> x0 y0 x1 y1            median colour of a sprite rectangle
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

SPRITES = "D:/OKReplace/sprites"
WORK = "D:/OKReplace/hand/veruna_buildings/work"
CAT = "D:/OKReplace/catalog.json"


def record(name):
    for r in json.load(open(CAT)):
        if r["name"] == name:
            return r
    raise SystemExit("no " + name)


def grid(name, s=4, crop=None):
    im = Image.open(os.path.join(SPRITES, name + ".png")).convert("RGBA")
    x0, y0 = 0, 0
    if crop:
        x0, y0, x1, y1 = crop
        im = im.crop(crop)
    big = Image.new("RGBA", (im.width * s + 30, im.height * s + 20), (84, 104, 64, 255))
    big.alpha_composite(im.resize((im.width * s, im.height * s), Image.NEAREST), (30, 20))
    d = ImageDraw.Draw(big)
    for gx in range((x0 // 10) * 10, x0 + im.width + 1, 10):
        if gx < x0:
            continue
        X = 30 + (gx - x0) * s
        d.line((X, 20, X, big.height), fill=(255, 255, 0, 90) if gx % 50 else (255, 60, 60, 160))
        if gx % 20 == 0:
            d.text((X + 1, 2), str(gx), fill=(255, 255, 255, 255))
    for gy in range((y0 // 10) * 10, y0 + im.height + 1, 10):
        if gy < y0:
            continue
        Y = 20 + (gy - y0) * s
        d.line((30, Y, big.width, Y), fill=(255, 255, 0, 90) if gy % 50 else (255, 60, 60, 160))
        if gy % 20 == 0:
            d.text((1, Y + 1), str(gy), fill=(255, 255, 255, 255))
    r = record(name)
    hx, hy = r["sprite"]["hotspot"]
    X, Y = 30 + (hx - x0) * s, 20 + (hy - y0) * s
    d.ellipse((X - 5, Y - 5, X + 5, Y + 5), outline=(0, 255, 255, 255), width=2)
    os.makedirs(WORK, exist_ok=True)
    out = os.path.join(WORK, name + "_grid.png")
    big.save(out)
    print(out, "hotspot", hx, hy, "size", r["sprite"]["w"], r["sprite"]["h"])


def sample(name, x0, y0, x1, y1):
    a = np.asarray(Image.open(os.path.join(SPRITES, name + ".png")).convert("RGBA")).astype(float)
    reg = a[y0:y1, x0:x1].reshape(-1, 4)
    reg = reg[reg[:, 3] > 128][:, :3]
    if not len(reg):
        print("empty")
        return
    med = np.median(reg, 0)
    print("median sRGB", [int(v) for v in med], "mean", [int(v) for v in reg.mean(0)], "n", len(reg))


if __name__ == "__main__":
    cmd, name = sys.argv[1], sys.argv[2]
    if cmd == "grid":
        s = int(sys.argv[3]) if len(sys.argv) > 3 else 4
        crop = tuple(int(v) for v in sys.argv[4:8]) if len(sys.argv) > 7 else None
        grid(name, s, crop)
    else:
        sample(name, *[int(v) for v in sys.argv[3:7]])
