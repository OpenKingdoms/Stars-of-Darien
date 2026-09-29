"""Compares, fit numbers and contact sheets for the spires, run with the system Python:

    python sheets.py [Name ...]

Runs handcompare.py for each rendered model, prints how much of the
picture the classic render covers and how much spills past it, with the
mean colours of both (the render stretched back up by 1/0.894 about the
anchor row, undoing the handkit ortho's squash), writes a fit map (green both, red picture only, blue
render only) to work/, and without names lays the compares out six to a
sheet in sheets/.
"""
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(os.path.dirname(HERE))
OUT = r"D:\OKReplace\hand\spires"
SPRITES = r"D:\OKReplace\sprites"
ORDER = ["TarSpire%02d" % i for i in range(1, 16)]
CAT = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
K = 2 / 5 ** 0.5  # the handkit ortho squashes height by this


def fit(n):
    spr = Image.open(os.path.join(SPRITES, n + ".png")).convert("RGBA")
    ren = Image.open(os.path.join(OUT, "renders", n + "_classic.png")).convert("RGBA").resize(spr.size, Image.BOX)
    a = np.array(spr).astype(float)
    b = np.array(ren).astype(float)
    hy = CAT[n]["sprite"]["hotspot"][1]
    rows = np.clip(np.round(hy + (np.arange(spr.height) - hy) * K).astype(int), 0, spr.height - 1)
    b = b[rows]
    ma, mb = a[..., 3] > 128, b[..., 3] > 128
    both = ma & mb
    cover = both.sum() / max(1, ma.sum())
    spill = (mb & ~ma).sum() / max(1, mb.sum())
    ca = a[ma][:, :3].mean(0) if ma.any() else np.zeros(3)
    cb = b[mb][:, :3].mean(0) if mb.any() else np.zeros(3)
    m = np.zeros(a.shape[:2] + (3,), np.uint8)
    m[both] = (60, 170, 60)
    m[ma & ~mb] = (220, 40, 40)
    m[mb & ~ma] = (50, 80, 230)
    im = Image.fromarray(m).resize((spr.width * 3, spr.height * 3), Image.NEAREST)
    os.makedirs(os.path.join(OUT, "work"), exist_ok=True)
    im.save(os.path.join(OUT, "work", n + "_fit.png"))
    print("%-11s cover %.2f spill %.2f  picture %s render %s" % (n, cover, spill, tuple(int(v) for v in ca),
                                                               tuple(int(v) for v in cb)))
    return cover, spill


def main():
    names = sys.argv[1:] or ORDER
    renders = os.path.join(OUT, "renders")
    done = []
    for n in names:
        if not os.path.exists(os.path.join(renders, n + "_classic.png")):
            print("no render", n)
            continue
        subprocess.run([sys.executable, os.path.join(TOOLS, "handcompare.py"), renders, n,
                        os.path.join(SPRITES, n + ".png"), "2"], check=True, stdout=subprocess.DEVNULL)
        fit(n)
        done.append(n)
    if sys.argv[1:]:
        return
    os.makedirs(os.path.join(OUT, "sheets"), exist_ok=True)
    for s in range(0, len(done), 6):
        group = done[s:s + 6]
        ims = [Image.open(os.path.join(renders, n + "_compare.png")).convert("RGBA") for n in group]
        W = max(i.width for i in ims)
        H = sum(i.height + 22 for i in ims)
        sheet = Image.new("RGBA", (W, H), (60, 76, 48, 255))
        d = ImageDraw.Draw(sheet)
        y = 0
        for n, im in zip(group, ims):
            d.text((6, y + 4), n, fill=(255, 255, 255, 255))
            sheet.alpha_composite(im, (0, y + 20))
            y += im.height + 22
        path = os.path.join(OUT, "sheets", "spires_%d.png" % (s // 6 + 1))
        sheet.save(path)
        print(path)


if __name__ == "__main__":
    main()
