"""Compare sheets for the Aramon buildings, run with the system Python.

    python check.py [name ...]

For each name: handcompare.py's picture, a silhouette check (sprite only
red, model only blue, both grey, from the model's projected triangles in
the classic view, exact, without handkit's vertical squeeze) and the
coverage numbers; then contact sheets of six into sheets/.
"""
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw

OUT = "D:/OKReplace/hand/aramon_buildings"
TOOLS = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REN = OUT + "/renders"


def silhouette(name, k=4):
    d = json.load(open("%s/%s_proj.json" % (REN, name)))
    w, h = d["w"], d["h"]
    im = Image.new("L", (w * k, h * k), 0)
    dr = ImageDraw.Draw(im)
    for t in d["tris"]:
        dr.polygon([(t[0] * k, t[1] * k), (t[2] * k, t[3] * k), (t[4] * k, t[5] * k)], fill=255)
    m = np.array(im.resize((w, h), Image.BOX)) > 127
    spr = np.array(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA"))[..., 3] > 127
    both = (m & spr).sum()
    cover = both / max(1, spr.sum())
    over = (m & ~spr).sum() / max(1, m.sum())
    # model pixels outside the sprite's picture
    outside = 0
    img = np.zeros((h, w, 3), np.uint8) + np.array([84, 104, 64], np.uint8)
    img[spr & ~m] = (230, 40, 40)
    img[m & ~spr] = (40, 90, 240)
    img[m & spr] = (200, 200, 200)
    Image.fromarray(img).resize((w * 2, h * 2), Image.NEAREST).save("%s/%s_sil.png" % (REN, name))
    return cover, over


def main():
    names = sys.argv[1:]
    if not names:
        names = sorted(n[:-6] for n in os.listdir(REN) if n.endswith("_proj.json"))
    rows = []
    for n in names:
        subprocess.run([sys.executable, os.path.join(TOOLS, "handcompare.py"), REN, n,
                        "D:/OKReplace/sprites/%s.png" % n, "2"], check=True, capture_output=True)
        c, o = silhouette(n)
        rows.append((n, c, o))
        print("%-12s cover %.3f  over %.3f" % (n, c, o))
    json.dump({n: [c, o] for n, c, o in rows}, open(OUT + "/renders/_metrics_last.json", "w"))
    os.makedirs(OUT + "/sheets", exist_ok=True)
    for s in range(0, len(names), 6):
        tiles = []
        for n in names[s:s + 6]:
            cmp_ = Image.open("%s/%s_compare.png" % (REN, n)).convert("RGBA")
            sil = Image.open("%s/%s_sil.png" % (REN, n)).convert("RGBA")
            c, o = [r[1:] for r in rows if r[0] == n][0]
            t = Image.new("RGBA", (cmp_.width + sil.width + 10, max(cmp_.height, sil.height) + 16), (30, 30, 30, 255))
            t.alpha_composite(cmp_, (0, 16))
            t.alpha_composite(sil, (cmp_.width + 10, 16))
            ImageDraw.Draw(t).text((4, 2), "%s   cover %.2f  over %.2f" % (n, c, o), fill=(255, 255, 255, 255))
            tiles.append(t)
        W = max(t.width for t in tiles)
        H = sum(t.height for t in tiles) + 8 * (len(tiles) - 1)
        sheet = Image.new("RGBA", (W, H), (20, 20, 20, 255))
        y = 0
        for t in tiles:
            sheet.alpha_composite(t, (0, y))
            y += t.height + 8
        tag = names[s] if len(names) > 1 else names[0]
        sheet.save("%s/sheets/sheet_%02d_%s.png" % (OUT, s // 6 + 1, tag))


if __name__ == "__main__":
    main()
