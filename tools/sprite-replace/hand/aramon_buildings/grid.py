"""Label and tile sprites into one picture for reading them side by side.

    python grid.py <out.png> <scale> <name> [<name> ...]
"""
import sys

from PIL import Image, ImageDraw

SPR = "D:/OKReplace/sprites/%s.png"
out, scale, names = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
tiles = []
for n in names:
    im = Image.open(SPR % n).convert("RGBA")
    bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
    bg.alpha_composite(im)
    bg = bg.resize((im.width * scale, im.height * scale), Image.NEAREST)
    t = Image.new("RGBA", (bg.width, bg.height + 14), (30, 30, 30, 255))
    t.alpha_composite(bg, (0, 14))
    ImageDraw.Draw(t).text((2, 1), n, fill=(255, 255, 255, 255))
    tiles.append(t)
W = sum(t.width for t in tiles) + 6 * (len(tiles) - 1)
H = max(t.height for t in tiles)
sheet = Image.new("RGBA", (W, H), (20, 20, 20, 255))
x = 0
for t in tiles:
    sheet.alpha_composite(t, (x, 0))
    x += t.width + 6
sheet.save(out)
