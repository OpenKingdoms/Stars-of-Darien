"""Compare images and contact sheets for the family (system Python).

    python sheets.py [names...]

Runs handcompare.py for each built model (all when none are named), then
lays the compares out six to a sheet in sheets/, each labelled with its
name, triangle count and silhouette fit.
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw

import fit as fitmod

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = "D:/OKReplace/hand/walls_joints"
REN = os.path.join(OUT, "renders")
ORDER = ["VerWall01", "VerWall02", "VerWall03", "VerWall04", "VerWall05", "VerWall06",
         "VerWall07", "VerWall08", "VerWall09", "VerWall10", "VerWall11", "VerWall12",
         "VerWall10a", "VerWall11a", "VerJoint01", "VerJoint02", "VerJoint03", "VerJoint04",
         "VerJoint05", "VerJoint06", "VerJoint07", "VerJoint08", "VerJoint08a", "VerJoint09",
         "VerJoint10", "VerJoint11", "VerJoint12", "VerTow01", "VerTow02", "VerTow03",
         "VerTow04", "AraWall01", "AraWall04", "AraWall05", "AraWall06", "AraWall07",
         "AraWall08", "AraWall05a", "AraWall06a", "AraWall07a", "AraTow01a", "AraTow02a",
         "AraTow03a", "TarTower01", "TarTower02", "TarTower01a", "TarTower02a", "TarWall01a",
         "TarWall02a", "VerFence08", "VerFence11", "VerFence12", "ZonOldW03", "ZonOldW04"]
tool = os.path.join(os.path.dirname(os.path.dirname(HERE)), "handcompare.py")
names = sys.argv[1:] or ORDER
for n in names:
    if os.path.exists(os.path.join(REN, n + "_classic.png")):
        subprocess.run([sys.executable, tool, REN, n, "D:/OKReplace/sprites/%s.png" % n, "2"],
                       check=True, capture_output=True)
os.makedirs(os.path.join(OUT, "sheets"), exist_ok=True)
done = [n for n in ORDER if os.path.exists(os.path.join(REN, n + "_compare.png"))]
for k in range(0, len(done), 6):
    group = done[k:k + 6]
    ims = [Image.open(os.path.join(REN, n + "_compare.png")).convert("RGBA") for n in group]
    W = max(i.width for i in ims)
    H = sum(i.height + 24 for i in ims)
    sheet = Image.new("RGBA", (W, H), (40, 40, 40, 255))
    d = ImageDraw.Draw(sheet)
    y = 0
    for n, im in zip(group, ims):
        label = n
        if os.path.exists(os.path.join(REN, n + "_proj.json")):
            c, s = fitmod.fit(n, save=False)
            label += "   cover %.2f  spill %.2f" % (c, s)
        d.text((4, y + 4), label, fill=(255, 255, 255, 255))
        sheet.alpha_composite(im, (0, y + 24))
        y += im.height + 24
    p = os.path.join(OUT, "sheets", "sheet_%02d.png" % (k // 6 + 1))
    sheet.convert("RGB").save(p)
    print(p, group)
