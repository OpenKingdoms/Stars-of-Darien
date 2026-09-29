"""Pictures over classic renders (3x) and turned views for several models,
system Python:

    python grid.py out.png suffix Name [Name ...]

Reads work/<Name><suffix>_* (renders/ when suffix is '-').
"""
import sys

from PIL import Image, ImageDraw

OUT = r"D:\OKReplace\hand\props"
out, suf, names = sys.argv[1], sys.argv[2], sys.argv[3:]
d, suf = (OUT + r"\renders", "") if suf == "-" else (OUT + r"\work", suf)
k, cells = 3, []
for n in names:
    s = Image.open(r"D:\OKReplace\sprites\%s.png" % n).convert("RGBA")
    c = Image.open(r"%s\%s%s_classic.png" % (d, n, suf)).convert("RGBA")
    t = Image.open(r"%s\%s%s_turned.png" % (d, n, suf)).convert("RGBA").crop((40, 130, 472, 400))
    W, H = s.size
    col = Image.new("RGBA", (max(W * k, t.width), H * k * 2 + t.height + 30), (90, 110, 70, 255))
    col.alpha_composite(s.resize((W * k, H * k), Image.NEAREST), (0, 14))
    col.alpha_composite(c.resize((W * k, H * k), Image.LANCZOS), (0, 18 + H * k))
    col.alpha_composite(t, (0, 22 + 2 * H * k))
    ImageDraw.Draw(col).text((4, 0), n, fill=(255, 255, 255, 255))
    cells.append(col)
sheet = Image.new("RGBA", (sum(c.width + 6 for c in cells), max(c.height for c in cells)), (60, 76, 48, 255))
x = 0
for c in cells:
    sheet.alpha_composite(c, (x, 0))
    x += c.width + 6
sheet.save(OUT + r"\work" + "\\" + out)
