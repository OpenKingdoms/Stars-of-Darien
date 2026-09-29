"""Median luma per 10 px cell of the sprite and the classic render, as
two text grids (value // 10, '.' where nothing is drawn), system Python."""
import sys
import numpy as np
from measure import load, LW
name = sys.argv[1]
s, m = load(name)
for lab, a in (("sprite", s), ("model", m)):
    L = a[..., :3] @ LW
    ok = a[..., 3] > 127
    print(lab, "   " + "".join("%-3d" % (x * 10 // 10) if x % 30 == 0 else "   " for x in range(0, a.shape[1] // 10)))
    for y in range(0, a.shape[0], 10):
        row = []
        for x in range(0, a.shape[1], 10):
            k = ok[y:y + 10, x:x + 10]
            row.append("%2d" % min(99, int(np.median(L[y:y + 10, x:x + 10][k]))) if k.sum() > 20 else " .")
        print("%3d " % y + " ".join(row))
