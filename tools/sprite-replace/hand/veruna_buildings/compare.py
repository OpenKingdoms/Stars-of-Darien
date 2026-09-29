"""Compare images and contact sheets for the Veruna pieces (system Python).

    python compare.py [names...]
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = "D:/OKReplace/hand/veruna_buildings"
REN = os.path.join(OUT, "renders")
ORDER = ["VerBuild01", "VerBuild01a", "VerBuild01b", "VerBuild02", "VerBuild02a", "VerBuild02b",
         "VerBuild03", "VerBuild03a", "VerBuild03b", "VerBuild04", "VerBuild04a", "VerBuild04b",
         "VerBuild05", "VerBuild05a", "VerBuild05b", "VerBuild06", "VerBuild06a", "VerBuild06b",
         "VerBuild07", "VerBuild07a", "VerBuild07b", "VerBuild08", "VerBuild08a", "VerBuild08b",
         "VerHut01", "VerHut01a", "VerHut02", "VerHut02a", "VerHut03", "VerHut03a",
         "VerHut04", "VerHut04a", "VerHut05", "VerHut05a", "VerHut06a", "VerHut07",
         "VerHut07a", "VerHut08", "VerHut08a", "VerHut09a", "VerHut10a", "VerHut11",
         "VerHut11a", "VerHut12", "VerHut12a"]
names = sys.argv[1:] or ORDER
tool = os.path.join(os.path.dirname(os.path.dirname(HERE)), "handcompare.py")
for n in names:
    if os.path.exists(os.path.join(REN, n + "_classic.png")):
        subprocess.run([sys.executable, tool, REN, n, "D:/OKReplace/sprites/%s.png" % n, "2"],
                       check=True, capture_output=True)
os.makedirs(os.path.join(OUT, "sheets"), exist_ok=True)
done = [n for n in ORDER if os.path.exists(os.path.join(REN, n + "_compare.png"))]
for k in range(0, len(done), 6):
    group = done[k:k + 6]
    ims = [Image.open(os.path.join(REN, n + "_compare.png")).convert("RGBA") for n in group]
    W = max(i.width for i in ims)
    H = sum(i.height + 24 for i in ims)
    sheet = Image.new("RGBA", (W, H), (40, 40, 40, 255))
    d = ImageDraw.Draw(sheet)
    y = 0
    for n, im in zip(group, ims):
        d.text((4, y + 4), n, fill=(255, 255, 255, 255))
        sheet.alpha_composite(im, (0, y + 24))
        y += im.height + 24
    p = os.path.join(OUT, "sheets", "sheet_%02d.png" % (k // 6 + 1))
    sheet.convert("RGB").save(p)
    print(p, group)
