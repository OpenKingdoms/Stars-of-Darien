"""The sprite and the model's classic render side by side, enlarged, with
their mean drawn colours, for reading detail on small features.

    python look.py <name> [scale] [x0 y0 x1 y1]
"""
import sys

import numpy as np
from PIL import Image, ImageDraw

OUT = "D:/OKReplace/hand/aramon_buildings"


def means(name):
    """Mean colour of the sprite's opaque pixels and of the render's."""
    spr = np.array(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")).astype(float)
    ren = np.array(Image.open("%s/renders/%s_classic.png" % (OUT, name)).convert("RGBA")).astype(float)
    ms = spr[spr[..., 3] > 127][:, :3].mean(0)
    mr = ren[ren[..., 3] > 127][:, :3].mean(0)
    return ms, mr


def main():
    name = sys.argv[1]
    k = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    spr = Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")
    ren = Image.open("%s/renders/%s_classic.png" % (OUT, name)).convert("RGBA").resize(spr.size, Image.LANCZOS)
    box = [int(v) for v in sys.argv[3:7]] if len(sys.argv) > 6 else [0, 0, spr.width, spr.height]
    tiles = []
    for im in (spr, ren):
        bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
        bg.alpha_composite(im)
        c = bg.crop(box)
        tiles.append(c.resize((c.width * k, c.height * k), Image.NEAREST))
    W = tiles[0].width
    sheet = Image.new("RGBA", (W * 2 + 8, tiles[0].height + 16), (20, 20, 20, 255))
    sheet.alpha_composite(tiles[0], (0, 16))
    sheet.alpha_composite(tiles[1], (W + 8, 16))
    ms, mr = means(name)
    ImageDraw.Draw(sheet).text((4, 2), "%s sprite mean %d,%d,%d   model mean %d,%d,%d" % ((name,) + tuple(ms) + tuple(mr)),
                               fill=(255, 255, 255, 255))
    path = "%s/look2/%s_look.png" % (OUT, name)
    sheet.save(path)
    print(path, "sprite %d,%d,%d model %d,%d,%d" % (tuple(ms) + tuple(mr)))


if __name__ == "__main__":
    main()
