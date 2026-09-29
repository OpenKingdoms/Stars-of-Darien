"""A close look for tuning, run with the system Python:

    python zoom.py <Name> [zoom]

Puts the picture, the classic render and a 50 percent overlay side by side
at zoom times the render scale into D:/OKReplace/hand/props/work.
"""
import os
import sys

from PIL import Image

OUT = r"D:\OKReplace\hand\props"
name = sys.argv[1]
z = int(sys.argv[2]) if len(sys.argv) > 2 else 3
spr = Image.open(os.path.join(r"D:\OKReplace\sprites", name + ".png")).convert("RGBA")
W, H = spr.width * 2 * z, spr.height * 2 * z
big = spr.resize((W, H), Image.NEAREST)
ren = Image.open(os.path.join(OUT, "renders", name + "_classic.png")).convert("RGBA").resize((W, H), Image.LANCZOS)
ghost = big.copy()
ghost.putalpha(big.getchannel("A").point(lambda a: a // 2))
over = Image.alpha_composite(ren, ghost)
sheet = Image.new("RGBA", (W * 3 + 20, H), (84, 104, 64, 255))
for i, im in enumerate((big, ren, over)):
    sheet.alpha_composite(im, (i * (W + 10), 0))
os.makedirs(os.path.join(OUT, "work"), exist_ok=True)
sheet.save(os.path.join(OUT, "work", name + "_zoom.png"))
