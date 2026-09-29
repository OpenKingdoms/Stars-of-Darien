"""Picture, classic render (4x) and turned view side by side, system Python:

    python view.py Name suffix [out]

Reads work/<Name><suffix>_* (or renders/ when suffix is '-'), writes work/r5_view.png.
"""
import sys

from PIL import Image

OUT = r"D:\OKReplace\hand\props"
n, suf = sys.argv[1], sys.argv[2]
d, suf = (OUT + r"\renders", "") if suf == "-" else (OUT + r"\work", suf)
s = Image.open(r"D:\OKReplace\sprites\%s.png" % n).convert("RGBA")
c = Image.open(r"%s\%s%s_classic.png" % (d, n, suf)).convert("RGBA")
t = Image.open(r"%s\%s%s_turned.png" % (d, n, suf)).convert("RGBA").crop((60, 110, 452, 452))
W, H = s.size
k = 4
out = Image.new("RGBA", (W * k * 2 + 20 + t.width, max(H * k, t.height)), (90, 110, 70, 255))
out.alpha_composite(s.resize((W * k, H * k), Image.NEAREST), (0, 0))
out.alpha_composite(c.resize((W * k, H * k), Image.LANCZOS), (W * k + 10, 0))
out.alpha_composite(t, (2 * W * k + 20, 0))
out.save(sys.argv[3] if len(sys.argv) > 3 else OUT + r"\work\r5_view.png")
