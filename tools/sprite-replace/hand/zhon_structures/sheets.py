"""Runs handcompare for each name and composes contact sheets, 6 compares a sheet.
    python sheets.py <sheet prefix> name,name,...   (or 'all')"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = "D:/OKReplace/hand/zhon_structures"
sys.path.insert(0, HERE)
import models  # noqa: E402

prefix = sys.argv[1]
names = list(models.MODELS) if sys.argv[2] == "all" else sys.argv[2].split(",")
paths = []
for n in names:
    subprocess.run([sys.executable, os.path.join(TOOLS, "handcompare.py"), OUT + "/renders", n,
                    "D:/OKReplace/sprites/%s.png" % n, "2"], check=True, capture_output=True)
    paths.append((n, OUT + "/renders/%s_compare.png" % n))
MAXW = 1800
for s in range(0, len(paths), 6):
    ims = []
    for n, p in paths[s:s + 6]:
        im = Image.open(p).convert("RGBA")
        if im.width < 900:
            k = -(-900 // im.width)
            im = im.resize((im.width * k, im.height * k), Image.NEAREST)
        if im.width > MAXW:
            im = im.resize((MAXW, int(im.height * MAXW / im.width)))
        lab = Image.new("RGBA", (im.width, im.height + 16), (40, 40, 40, 255))
        lab.alpha_composite(im, (0, 16))
        ImageDraw.Draw(lab).text((4, 2), n, fill=(255, 255, 255))
        ims.append(lab)
    W = max(i.width for i in ims)
    H = sum(i.height for i in ims) + 6 * len(ims)
    sheet = Image.new("RGBA", (W, H), (30, 30, 30, 255))
    y = 0
    for i in ims:
        sheet.alpha_composite(i, (0, y))
        y += i.height + 6
    path = os.path.join(OUT, "sheets", "%s_%d.png" % (prefix, s // 6 + 1))
    sheet.save(path)
    print(path, sheet.size)
