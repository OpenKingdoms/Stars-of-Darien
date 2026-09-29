"""Median sRGB colour of sprite regions: python sample.py <name> x0 y0 x1 y1 [...]"""
import sys
import numpy as np
from PIL import Image
n = sys.argv[1]
a = np.array(Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")).astype(int)
v = [int(t) for t in sys.argv[2:]]
for i in range(0, len(v), 4):
    x0, y0, x1, y1 = v[i:i + 4]
    reg = a[y0:y1, x0:x1].reshape(-1, 4)
    reg = reg[reg[:, 3] > 128][:, :3]
    if len(reg) == 0:
        print((x0, y0, x1, y1), "empty")
        continue
    lum = reg @ [0.299, 0.587, 0.114]
    o = np.argsort(lum)
    q = lambda f: tuple(reg[o[int(f * (len(o) - 1))]])
    print((x0, y0, x1, y1), "n=%d" % len(reg), "median", tuple(int(t) for t in np.median(reg, 0)),
          "dark", q(0.15), "light", q(0.85))
