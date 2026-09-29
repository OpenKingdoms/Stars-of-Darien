"""Large review tiles, run with the system Python.

    python review.py [name ...]

For each name: the sprite and the classic render side by side at 3x on
grass, and the turned view beside them, into tmp/rv_<name>.png.
"""
import json
import os
import sys

from PIL import Image, ImageDraw

OUT = "D:/OKReplace/hand/aramon_buildings"


def tile(name, k=3):
    spr = Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")
    ren = Image.open("%s/renders/%s_classic.png" % (OUT, name)).convert("RGBA").resize(spr.size, Image.LANCZOS)
    tiles = []
    for im in (spr, ren):
        bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
        bg.alpha_composite(im)
        tiles.append(bg.resize((im.width * k, im.height * k), Image.NEAREST if im is spr else Image.LANCZOS))
    t = Image.open("%s/renders/%s_turned.png" % (OUT, name)).convert("RGBA")
    tb = Image.new("RGBA", t.size, (84, 104, 64, 255))
    tb.alpha_composite(t)
    H = max(tiles[0].height, tb.height)
    W = tiles[0].width * 2 + tb.width + 16
    sheet = Image.new("RGBA", (W, H + 16), (20, 20, 20, 255))
    sheet.alpha_composite(tiles[0], (0, 16))
    sheet.alpha_composite(tiles[1], (tiles[0].width + 8, 16))
    sheet.alpha_composite(tb, (tiles[0].width * 2 + 16, 16))
    ImageDraw.Draw(sheet).text((4, 2), name, fill=(255, 255, 255, 255))
    path = "%s/tmp/rv_%s.png" % (OUT, name)
    sheet.save(path)
    return path


if __name__ == "__main__":
    names = sys.argv[1:] or json.load(open(OUT + "/round1.json"))["names"]
    k = 3
    if names and names[0].startswith("k="):
        k = int(names.pop(0)[2:])
    for n in names:
        print(tile(n, k))
