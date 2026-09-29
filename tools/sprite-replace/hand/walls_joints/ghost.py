"""Outlines a built model over another sprite with the same anchor (system Python):
python ghost.py <model> <sprite> <out.png> -- to see what of an intact piece a ruin keeps."""
import json
import sys

from PIL import Image, ImageDraw

cat = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}
mdl, spr, out = sys.argv[1:4]
d = json.load(open("D:/OKReplace/hand/walls_joints/renders/%s_proj.json" % mdl))
hm, hs = cat[mdl]["sprite"]["hotspot"], cat[spr]["sprite"]["hotspot"]
dx, dy = hs[0] - hm[0], hs[1] - hm[1]
im = Image.open("D:/OKReplace/sprites/%s.png" % spr).convert("RGBA")
S = 5
big = Image.new("RGBA", (im.width * S, im.height * S), (84, 104, 64, 255))
big.alpha_composite(im.resize((im.width * S, im.height * S), Image.NEAREST))
ov = Image.new("RGBA", big.size, (0, 0, 0, 0))
dr = ImageDraw.Draw(ov)
for t in d["tris"]:
    dr.polygon([((x + dx) * S, (y + dy) * S) for x, y in t], outline=(255, 0, 255, 90))
big.alpha_composite(ov)
big.save(out)
