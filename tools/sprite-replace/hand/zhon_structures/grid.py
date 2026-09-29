"""A sprite enlarged with the ground grid of the true ortho camera drawn on it:
x at 16 px a cell, depth at 16 cos(atan 0.5) px a cell, height at half that."""
import json
import math
import sys
from PIL import Image, ImageDraw

cat = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}
KY = 16 * math.cos(math.atan(0.5))  # 14.31 px per cell of depth
KZ = KY / 2                         # 7.16 px per cell of height


def grid(n, out, s=3, z=0.0, crop=None):
    r = cat[n]
    hx, hy = r["sprite"]["hotspot"]
    im = Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")
    big = im.resize((im.width * s, im.height * s), Image.NEAREST)
    bg = Image.new("RGBA", big.size, (84, 104, 64, 255))
    bg.alpha_composite(big)
    ov = Image.new("RGBA", big.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    R = 20
    for i in range(-R, R + 1):
        col = (255, 255, 0, 150) if i % 5 == 0 else (255, 255, 255, 60)
        if i == 0:
            col = (255, 0, 0, 200)
        # line x = i across depth
        x = (hx + 16 * i) * s
        d.line([(x, (hy - KY * -R - KZ * z) * s), (x, (hy - KY * R - KZ * z) * s)], fill=col)
        yrow = (hy - KY * i - KZ * z) * s
        d.line([((hx - 16 * R) * s, yrow), ((hx + 16 * R) * s, yrow)], fill=col)
    bg.alpha_composite(ov)
    if crop:
        bg = bg.crop(tuple(c * s for c in crop))
    bg.save(out)
    print(out, bg.size)


if __name__ == "__main__":
    n, out = sys.argv[1], sys.argv[2]
    s = int(sys.argv[3]) if len(sys.argv) > 3 else 3
    z = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
    crop = tuple(int(v) for v in sys.argv[5].split(",")) if len(sys.argv) > 5 else None
    grid(n, out, s, z, crop)
