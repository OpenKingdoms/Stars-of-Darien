"""Marks the classic render's near-black pixels (luminance under 20) in red: python blackmask.py Name,Name"""
import sys

import numpy as np
from PIL import Image

ims = []
for n in sys.argv[1].split(","):
    a = np.asarray(Image.open("D:/OKReplace/hand/zhon_structures/renders/%s_classic.png" % n).convert("RGBA")).astype(float)
    m = (a[..., 3] > 127) & (a[..., :3] @ [0.299, 0.587, 0.114] < 20)
    a[m] = [255, 0, 0, 255]
    ims.append(Image.fromarray(a.astype("uint8")))
out = Image.new("RGBA", (sum(i.width + 6 for i in ims), max(i.height for i in ims)), (84, 104, 64, 255))
x = 0
for i in ims:
    out.alpha_composite(i, (x, 0))
    x += i.width + 6
out.save("D:/OKReplace/hand/zhon_structures/view2/black.png")
