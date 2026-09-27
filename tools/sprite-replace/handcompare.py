"""Side-by-side check for a hand-built model, run with the system Python.

    python handcompare.py <out dir> <name> <picture png> [scale]

Reads <name>_classic.png (from handkit.renders) and writes <name>_compare.png:
the original picture scaled up, the classic render, and the render with the
picture laid over it at half strength.
"""
import os
import sys

from PIL import Image

out, name, pic = sys.argv[1], sys.argv[2], sys.argv[3]
scale = int(sys.argv[4]) if len(sys.argv) > 4 else 2
spr = Image.open(pic).convert("RGBA")
W, H = spr.width * scale, spr.height * scale
big = spr.resize((W, H), Image.NEAREST)
ren = Image.open(os.path.join(out, name + "_classic.png")).convert("RGBA").resize((W, H))
ghost = big.copy()
ghost.putalpha(big.getchannel("A").point(lambda a: a // 2))
over = Image.alpha_composite(ren, ghost)
sheet = Image.new("RGBA", (W * 3 + 20, H), (84, 104, 64, 255))
for i, im in enumerate((big, ren, over)):
    sheet.alpha_composite(im, (i * (W + 10), 0))
turned = os.path.join(out, name + "_turned.png")
if os.path.exists(turned):
    t = Image.open(turned).convert("RGBA")
    t.thumbnail((H * 2, H))
    full = Image.new("RGBA", (sheet.width + t.width + 10, max(H, t.height)), (84, 104, 64, 255))
    full.alpha_composite(sheet, (0, 0))
    full.alpha_composite(t, (sheet.width + 10, 0))
    sheet = full
path = os.path.join(out, name + "_compare.png")
sheet.save(path)
print(path)
