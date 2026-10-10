"""Contact sheets of the destruction shots: each shot's stills from
review/ in a row, labelled, one sheet per scene in sheets/.

    python tools/trailer/sheets.py <out dir>
"""
import os
import re
import sys
from collections import defaultdict

from PIL import Image, ImageDraw

THUMB_W = 640
PAD = 8
LABEL = 26


def main(out):
    review = os.path.join(out, "review")
    if not os.path.isdir(review):
        return
    shots = defaultdict(list)
    for name in sorted(os.listdir(review)):
        m = re.match(r"(.+)-(\d{4})\.png$", name)
        if m:
            shots[m.group(1)].append((int(m.group(2)), os.path.join(review, name)))
    scenes = defaultdict(list)
    for shot in sorted(shots):
        scenes[shot.split("-")[0]].append(shot)
    os.makedirs(os.path.join(out, "sheets"), exist_ok=True)
    for scene, names in scenes.items():
        rows = [sorted(shots[n]) for n in names]
        first = Image.open(rows[0][0][1])
        th = round(THUMB_W * first.height / first.width)
        cols = max(len(r) for r in rows)
        sheet = Image.new("RGB", (PAD + cols * (THUMB_W + PAD), PAD + len(rows) * (th + LABEL + PAD)), (22, 22, 24))
        draw = ImageDraw.Draw(sheet)
        for r, (shot, row) in enumerate(zip(names, rows)):
            y = PAD + r * (th + LABEL + PAD)
            for c, (frame, path) in enumerate(row):
                x = PAD + c * (THUMB_W + PAD)
                img = Image.open(path).convert("RGB").resize((THUMB_W, th), Image.LANCZOS)
                sheet.paste(img, (x, y))
                draw.text((x + 4, y + th + 6), f"{shot}  {frame / 60:0.2f}s", fill=(235, 225, 190))
        path = os.path.join(out, "sheets", f"{scene}.png")
        sheet.save(path)
        print("sheet", path)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
