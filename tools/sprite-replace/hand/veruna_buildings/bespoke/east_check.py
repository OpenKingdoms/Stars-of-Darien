"""Numbers and a picture for the VerBuild05a east shed against the drawing
(system Python): python east_check.py [tag]"""
import sys

import numpy as np
from PIL import Image

OUT = "D:/OKReplace/hand/veruna_buildings"
s = np.asarray(Image.open("D:/OKReplace/sprites/VerBuild05a.png").convert("RGBA")).astype(float)
r = Image.open(OUT + "/renders/VerBuild05a_classic.png").convert("RGBA")
r = np.asarray(r.resize((s.shape[1], s.shape[0]), Image.BILINEAR)).astype(float)
for tag, a in (("drawing", s), ("model", r)):
    c = a[30:70, 158:179]
    m = c[..., 3] > 128
    px = c[m][:, :3]
    L = px @ [0.299, 0.587, 0.114]
    st = a[37:67, 160:169]
    sm = st[..., 3] > 128
    print("%-8s cover %2d%%  mean %s  lum std %.1f  strip mean %s" % (
        tag, 100 * m.mean(), px.mean(0).astype(int), L.std(), st[sm][:, :3].mean(0).astype(int)))
box = (130, 15, 179, 85)


def pad(a):
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")
    bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
    bg.alpha_composite(im)
    return bg


A = pad(s[box[1]:box[3], box[0]:box[2]])
B = pad(r[box[1]:box[3], box[0]:box[2]])
out = Image.new("RGBA", (A.width * 2 + 4, A.height), (0, 0, 0, 255))
out.paste(A, (0, 0))
out.paste(B, (A.width + 4, 0))
tag = sys.argv[1] if len(sys.argv) > 1 else "cur"
out.resize((out.width * 7, out.height * 7), Image.NEAREST).save(OUT + "/work/r6_east_%s.png" % tag)
