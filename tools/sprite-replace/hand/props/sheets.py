"""Compares and contact sheets for the props family, run with the system Python:

    python sheets.py [Name ...]

Runs handcompare.py on every model rendered in D:/OKReplace/hand/props/renders
and lays the compares out six to a sheet in sheets/, each labelled.
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(os.path.dirname(HERE))
OUT = r"D:\OKReplace\hand\props"
SPRITES = r"D:\OKReplace\sprites"
ORDER = ["VerFountain01", "VerFountain01a", "Arafount01", "Arafount01a", "VerLamp01", "VerLamp01a", "VerLamp02",
         "VerLamp02a", "ZonFire01", "ZonFire02", "ZonFireD01", "ZonFireD02", "VerAlt01", "ZonAlt01", "Aracart01",
         "Aracart02", "Aracart03", "Aracart04", "Aracart05", "Aracrop07", "Aracrop08", "Aracrop09", "Aracrop10",
         "Aracrop11", "Aracrop12"]


def main():
    names = sys.argv[1:] or ORDER
    renders = os.path.join(OUT, "renders")
    done = []
    for n in names:
        if not os.path.exists(os.path.join(renders, n + "_classic.png")):
            print("no render", n)
            continue
        subprocess.run([sys.executable, os.path.join(TOOLS, "handcompare.py"), renders, n,
                        os.path.join(SPRITES, n + ".png"), "2"], check=True, stdout=subprocess.DEVNULL)
        done.append(n)
    if sys.argv[1:]:
        return
    os.makedirs(os.path.join(OUT, "sheets"), exist_ok=True)
    for s in range(0, len(done), 6):
        group = done[s:s + 6]
        ims = [Image.open(os.path.join(renders, n + "_compare.png")).convert("RGBA") for n in group]
        W = max(i.width for i in ims)
        H = sum(i.height + 22 for i in ims)
        sheet = Image.new("RGBA", (W, H), (60, 76, 48, 255))
        d = ImageDraw.Draw(sheet)
        y = 0
        for n, im in zip(group, ims):
            d.text((6, y + 4), n, fill=(255, 255, 255, 255))
            sheet.alpha_composite(im, (0, y + 20))
            y += im.height + 22
        path = os.path.join(OUT, "sheets", "props_%d.png" % (s // 6 + 1))
        sheet.save(path)
        print(path)


if __name__ == "__main__":
    main()
