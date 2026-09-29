"""A sprite region enlarged with a pixel ruler, for reading coordinates.
    python ruler.py <Name> <out.png> [scale] [x0,y0,x1,y1]"""
import sys
from PIL import Image, ImageDraw

n, out = sys.argv[1], sys.argv[2]
s = int(sys.argv[3]) if len(sys.argv) > 3 else 4
im = Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")
x0, y0, x1, y1 = [int(v) for v in sys.argv[4].split(",")] if len(sys.argv) > 4 else (0, 0, im.width, im.height)
bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
bg.alpha_composite(im)
c = bg.crop((x0, y0, x1, y1)).resize(((x1 - x0) * s, (y1 - y0) * s), Image.NEAREST)
M = 28
sheet = Image.new("RGBA", (c.width + M, c.height + M), (20, 20, 20, 255))
sheet.alpha_composite(c, (M, M))
d = ImageDraw.Draw(sheet)
for x in range((x0 // 8) * 8, x1, 8):
    if x < x0:
        continue
    X = M + (x - x0) * s
    d.line([(X, M - (8 if x % 16 == 0 else 4)), (X, M)], fill=(255, 255, 0))
    if x % 16 == 0:
        d.text((X + 1, 1), str(x), fill=(255, 255, 255))
        d.line([(X, M), (X, sheet.height)], fill=(255, 255, 0, 60))
for y in range((y0 // 8) * 8, y1, 8):
    if y < y0:
        continue
    Y = M + (y - y0) * s
    d.line([(M - (8 if y % 16 == 0 else 4), Y), (M, Y)], fill=(255, 255, 0))
    if y % 16 == 0:
        d.text((1, Y + 1), str(y), fill=(255, 255, 255))
sheet.save(out)
print(out, sheet.size)
