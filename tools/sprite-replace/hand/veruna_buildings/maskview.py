"""Shows where a pic_field keeps, over the drawing (system Python).

    python maskview.py <Name> <pred> <sigma> <bias> [x0 y0 x1 y1] [scale]

pred is one of: roof (roofish), light (pale plaster or stone), bright
(max channel over 0.38 and grey). Writes work/mask_<Name>.png.
"""
import sys

import numpy as np
from PIL import Image

name, pred = sys.argv[1], sys.argv[2]
sigma, bias = float(sys.argv[3]), float(sys.argv[4])
crop = tuple(int(v) for v in sys.argv[5:9]) if len(sys.argv) > 8 else None
s = int(sys.argv[9]) if len(sys.argv) > 9 else 3
a = np.asarray(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")).astype(float) / 255
r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
mx, mn = a[..., :3].max(2), a[..., :3].min(2)
if pred == "roof":
    M = (r > 0.15) & (r > 1.3 * g) & (r > 1.4 * b)
elif pred == "light":
    roofish = (r > 0.15) & (r > 1.3 * g) & (r > 1.4 * b)
    wood = (r > g * 1.12) & (g > b * 1.08) & (mx < 0.55)
    M = (mx >= 0.36) & ~roofish & ((mx - mn) <= 0.3) & ~wood
elif pred.startswith("grey"):
    # grey<t>: sooty or pale grey brighter than t
    M = (mx > float(pred[4:] or 0.3)) & ((mx - mn) < 0.2)
else:
    M = (mx > 0.38) & ((mx - mn) < 0.22)
M = (M & (al > 0.5)).astype(float)
k = max(1, int(sigma * 3))
x = np.arange(-k, k + 1)
gk = np.exp(-0.5 * (x / sigma) ** 2)
gk /= gk.sum()
P = np.pad(M, k, mode="edge")
P = np.apply_along_axis(lambda v: np.convolve(v, gk, "valid"), 1, P)
P = np.apply_along_axis(lambda v: np.convolve(v, gk, "valid"), 0, P)
out = a[..., :3] * 255 * 0.55
out[..., 1] += (P > bias) * 110
im = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))
if crop:
    im = im.crop(crop)
im = im.resize((im.width * s, im.height * s), Image.NEAREST)
p = "D:/OKReplace/hand/veruna_buildings/work/mask_%s.png" % name
im.save(p)
print(p)
