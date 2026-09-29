"""Stacks compare images for a close look: python view.py out.png names..."""
import os
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.dirname(os.path.dirname(HERE))
OUT = r"D:\OKReplace\hand\taros_buildings_devices"
ren = os.path.join(OUT, "renders")
dst, names = sys.argv[1], sys.argv[2:]
ims = []
for n in names:
    subprocess.run([sys.executable, os.path.join(KIT, "handcompare.py"), ren, n,
                    r"D:\OKReplace\sprites\%s.png" % n, "2"], check=True, capture_output=True)
    ims.append(Image.open(os.path.join(ren, n + "_compare.png")).convert("RGB"))
W = max(i.width for i in ims)
f = min(2.0, 1800 / W)
ims = [i.resize((int(i.width * f), int(i.height * f)), Image.LANCZOS) for i in ims]
H = sum(i.height + 6 for i in ims)
s = Image.new("RGB", (max(i.width for i in ims), H), (30, 30, 30))
y = 0
for i in ims:
    s.paste(i, (0, y))
    y += i.height + 6
p = os.path.join(OUT, "scratch", dst)
s.save(p)
print(p, s.size)
