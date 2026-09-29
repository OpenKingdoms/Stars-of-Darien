"""Compare images and contact sheets for the family, with the system Python.

    python sheets.py [names...]

Runs handcompare.py for each model that has renders, then lays the compares
six to a sheet in D:/OKReplace/hand/taros_buildings_devices/sheets.
"""
import json
import os
import subprocess
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.dirname(os.path.dirname(HERE))
OUT = r"D:\OKReplace\hand\taros_buildings_devices"
ORDER = ["Tarbuild01", "Tarbuild01a", "Tarbuild01b", "Tarbuild02a", "Tarbuild02b", "Tarbuild03", "Tarbuild03a",
         "Tarbuild03b", "Tarbuild04", "Tarbuild04a", "TarHut01", "TarHut01a", "TarHut02", "TarHut02a",
         "TarDev01", "TarDev02", "TarDev03", "TarDev04", "TarDev05", "TarDev06", "TarDev04a", "TarDev05a",
         "TarDev06a", "TarDev07", "TarDev08", "TarDev09"]
names = sys.argv[1:] or ORDER
ren = os.path.join(OUT, "renders")
done = []
for n in names:
    if not os.path.exists(os.path.join(ren, n + "_classic.png")):
        continue
    subprocess.run([sys.executable, os.path.join(KIT, "handcompare.py"), ren, n,
                    r"D:\OKReplace\sprites\%s.png" % n, "2"], check=True, capture_output=True)
    done.append(n)
os.makedirs(os.path.join(OUT, "sheets"), exist_ok=True)
allc = [n for n in ORDER if os.path.exists(os.path.join(ren, n + "_compare.png"))]
for k in range(0, len(allc), 6):
    group = allc[k:k + 6]
    ims = [Image.open(os.path.join(ren, n + "_compare.png")).convert("RGB") for n in group]
    W = 1800
    rows = []
    for n, im in zip(group, ims):
        # small pictures are blown up so each row reads, wide ones fit the sheet
        s = min(W / im.width, max(1.0, 300 / im.height))
        im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
        rows.append((n, im))
    H = sum(im.height + 22 for _, im in rows)
    sheet = Image.new("RGB", (W, H), (40, 44, 36))
    d = ImageDraw.Draw(sheet)
    y = 0
    for n, im in rows:
        d.text((6, y + 4), n, fill=(255, 255, 255))
        sheet.paste(im, (0, y + 20))
        y += im.height + 22
    p = os.path.join(OUT, "sheets", "sheet_%02d.png" % (k // 6 + 1))
    sheet.save(p)
    print(p, group)
print("compared", done)
