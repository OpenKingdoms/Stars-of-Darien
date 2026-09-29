"""Upscales a compare image for a closer look: python zoom.py <name> [factor]"""
import sys
from PIL import Image
n = sys.argv[1]
f = float(sys.argv[2]) if len(sys.argv) > 2 else 2
im = Image.open(r"D:\OKReplace\hand\taros_buildings_devices\renders\%s_compare.png" % n)
im = im.resize((int(im.width * f), int(im.height * f)), Image.NEAREST)
p = r"D:\OKReplace\hand\taros_buildings_devices\scratch\%s_cmpzoom.png" % n
im.save(p)
print(p, im.size)
