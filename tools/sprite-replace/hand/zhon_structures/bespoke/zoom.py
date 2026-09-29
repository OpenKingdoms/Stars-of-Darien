"""Enlarges compare images for reading by eye: python zoom.py out.png Name,Name [k]"""
import sys

from PIL import Image

R = "D:/OKReplace/hand/zhon_structures/renders/%s_compare.png"
names = sys.argv[2].split(",")
k = float(sys.argv[3]) if len(sys.argv) > 3 else 2
ims = [Image.open(R % n).convert("RGBA") for n in names]
ims = [i.resize((int(i.width * k), int(i.height * k)), Image.NEAREST) for i in ims]
out = Image.new("RGBA", (max(i.width for i in ims), sum(i.height + 4 for i in ims)), (30, 30, 30, 255))
y = 0
for i in ims:
    out.alpha_composite(i, (0, y))
    y += i.height + 4
out.save(sys.argv[1])
print(out.size)
