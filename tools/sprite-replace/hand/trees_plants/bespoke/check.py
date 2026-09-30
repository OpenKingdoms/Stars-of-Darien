"""Checks for the bespoke models, run with the system Python after run.py.

    python bespoke/check.py Name [Name ...]

Prints measure.py's classic bands and turned ratios plus the front view's
height over its widest row and the plan size from the .glb, and writes
work/r5/<Name>_check.png: the compare row over the front, back, side and
top views.
"""
import os
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
FAM = os.path.dirname(HERE)
sys.path.insert(0, FAM)
import measure  # noqa: E402

OUT = os.environ.get("OK_REPLACE", "D:/OKReplace") + "/hand/trees_plants"
WORK = os.path.join(OUT, "work", "r5")
COMPARE = os.path.normpath(os.path.join(FAM, "..", "..", "handcompare.py"))


def ratio(path):
    a = np.asarray(Image.open(path).convert("RGBA"))[..., 3] > 127
    ys = np.nonzero(a.any(1))[0]
    w = [np.ptp(np.nonzero(a[y])[0]) + 1 for y in ys]
    return (ys.max() - ys.min() + 1) / max(w), (ys.max() - ys.min() + 1) / np.median(w)


def sheet(name):
    subprocess.run([sys.executable, COMPARE, os.path.join(OUT, "renders"), name, os.environ.get("OK_REPLACE", "D:/OKReplace") + "/sprites/%s.png" % name, "2"],
                   check=True, stdout=subprocess.DEVNULL)
    comp = Image.open(os.path.join(OUT, "renders", name + "_compare.png")).convert("RGBA")
    vs = [Image.open(os.path.join(WORK, "views", "%s_%s.png" % (name, t))).convert("RGBA")
          for t in ("front", "back", "side", "top")]
    row = Image.new("RGBA", (sum(v.width for v in vs) + 30, vs[0].height), (84, 104, 64, 255))
    x = 0
    for v in vs:
        row.alpha_composite(v, (x, 0))
        x += v.width + 10
    W = max(comp.width, row.width)
    out = Image.new("RGBA", (W, comp.height + row.height + 10), (30, 30, 30, 255))
    out.alpha_composite(comp, (0, 0))
    out.alpha_composite(row, (0, comp.height + 10))
    p = os.path.join(WORK, name + "_check.png")
    out.save(p)
    return p


if __name__ == "__main__":
    for n in sys.argv[1:]:
        measure.classic(n)
        measure.turned(n)
        f = ratio(os.path.join(WORK, "views", n + "_front.png"))
        b = ratio(os.path.join(WORK, "views", n + "_back.png"))
        print("   front20 H/max %.2f H/median %.2f   back20 H/max %.2f H/median %.2f" % (f + b))
        subprocess.run([sys.executable, os.path.join(FAM, "glbsize.py"), n])
        print("  ", sheet(n))
