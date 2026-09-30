"""Compare images and contact sheets for the trees_plants family, run
with the system Python after build.py.

    python sheets.py [Name ...]          compares for these (default all)
    python sheets.py --sheets            also the 6-per-sheet contact sheets
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import models  # noqa: E402

OUT = os.environ.get("OK_REPLACE", "D:/OKReplace") + "/hand/trees_plants"
REN = os.path.join(OUT, "renders")
COMPARE = os.path.normpath(os.path.join(HERE, "..", "..", "handcompare.py"))


def compare(name):
    subprocess.run([sys.executable, COMPARE, REN, name, os.environ.get("OK_REPLACE", "D:/OKReplace") + "/sprites/%s.png" % name, "2"],
                   check=True, stdout=subprocess.DEVNULL)
    return os.path.join(REN, name + "_compare.png")


def sheets(names, per=6, width=1800, folder=None, prefix="sheet"):
    folder = folder or os.path.join(OUT, "sheets")
    os.makedirs(folder, exist_ok=True)
    paths = []
    for s in range(0, len(names), per):
        group = names[s:s + per]
        ims = []
        for n in group:
            p = os.path.join(REN, n + "_compare.png")
            if not os.path.exists(p):
                continue
            im = Image.open(p).convert("RGBA")
            k = min(1.0, width / im.width)
            im = im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)
            lab = Image.new("RGBA", (im.width, im.height + 16), (30, 30, 30, 255))
            lab.alpha_composite(im, (0, 16))
            ImageDraw.Draw(lab).text((4, 2), n, fill=(255, 255, 255, 255))
            ims.append(lab)
        if not ims:
            continue
        sheet = Image.new("RGBA", (max(i.width for i in ims), sum(i.height + 6 for i in ims)), (30, 30, 30, 255))
        y = 0
        for i in ims:
            sheet.alpha_composite(i, (0, y))
            y += i.height + 6
        path = os.path.join(folder, "%s_%02d.png" % (prefix, s // per + 1))
        sheet.save(path)
        paths.append(path)
    return paths


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    names = args or list(models.MODELS)
    for n in names:
        if os.path.exists(os.path.join(REN, n + "_classic.png")):
            print(compare(n))
    if "--sheets" in sys.argv:
        for p in sheets(list(models.ORDER)):
            print(p)
    for a in sys.argv[1:]:
        if a.startswith("--work="):
            # a scratch sheet of just these names, for iterating
            for p in sheets(names, folder=os.path.join(OUT, "work"), prefix=a[7:]):
                print(p)
