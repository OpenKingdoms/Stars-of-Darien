"""Puts fit maps side by side: python fits.py <out.png> names..."""
import sys
from PIL import Image, ImageDraw
R = "D:/OKReplace/hand/walls_joints/renders/"
ims = [(n, Image.open(R + n + "_fit.png")) for n in sys.argv[2:]]
W = sum(i.width for _, i in ims) + 10 * len(ims)
H = max(i.height for _, i in ims) + 16
s = Image.new("RGB", (W, H), (0, 0, 0))
d = ImageDraw.Draw(s)
x = 0
for n, i in ims:
    s.paste(i, (x, 16))
    d.text((x + 2, 2), n, fill=(255, 255, 255))
    x += i.width + 10
s.save(sys.argv[1])
