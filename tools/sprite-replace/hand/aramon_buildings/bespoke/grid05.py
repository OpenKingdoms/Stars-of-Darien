"""The sprite and the classic render side by side at k x with a 20 px grid."""
import sys
from PIL import Image, ImageDraw
OUT = "D:/OKReplace/hand/aramon_buildings"
name, k = sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 3
spr = Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")
ren = Image.open("%s/renders/%s_classic.png" % (OUT, name)).convert("RGBA").resize(spr.size, Image.LANCZOS)
tiles = []
for im in (spr, ren):
    bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
    bg.alpha_composite(im)
    t = bg.resize((im.width * k, im.height * k), Image.NEAREST)
    d = ImageDraw.Draw(t)
    for x in range(0, im.width, 20):
        d.line([(x * k, 0), (x * k, t.height)], fill=(255, 255, 0, 90) if x % 100 else (255, 60, 60, 160))
        d.text((x * k + 2, 2), str(x), fill=(255, 255, 0, 255))
    for y in range(0, im.height, 20):
        d.line([(0, y * k), (t.width, y * k)], fill=(255, 255, 0, 90) if y % 100 else (255, 60, 60, 160))
        d.text((2, y * k + 2), str(y), fill=(255, 255, 0, 255))
    tiles.append(t)
sheet = Image.new("RGBA", (tiles[0].width * 2 + 8, tiles[0].height), (20, 20, 20, 255))
sheet.alpha_composite(tiles[0], (0, 0))
sheet.alpha_composite(tiles[1], (tiles[0].width + 8, 0))
p = "%s/tmp/grid_%s.png" % (OUT, name)
sheet.save(p)
print(p)
