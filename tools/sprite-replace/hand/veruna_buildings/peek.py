"""Stacks the compare images of the named models into work/peek.png.

    python peek.py <Name> [<Name> ...]
"""
import os
import sys

from PIL import Image, ImageDraw

REN = "D:/OKReplace/hand/veruna_buildings/renders"
ims = [(n, Image.open(os.path.join(REN, n + "_compare.png")).convert("RGBA")) for n in sys.argv[1:]]
W = max(i.width for _, i in ims)
H = sum(i.height + 20 for _, i in ims)
S = Image.new("RGBA", (W, H), (40, 40, 40, 255))
d = ImageDraw.Draw(S)
y = 0
for n, i in ims:
    d.text((4, y + 3), n, fill=(255, 255, 255, 255))
    S.alpha_composite(i, (0, y + 20))
    y += i.height + 20
S.convert("RGB").save("D:/OKReplace/hand/veruna_buildings/work/peek.png")
