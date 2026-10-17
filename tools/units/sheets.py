"""Lays out review renders (review.py) as labelled sheets, with the system
Python and Pillow:

    python tools/units/sheets.py <review dir> [title]

Writes compare.png (the original and the remaster from the front, a low
three-quarter and the classic camera), turntable.png and animations.png
(each animation's frames, the original above the remaster).
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

BG = (54, 60, 52, 255)
INK = (236, 232, 220)


def font(size):
    for f in ("C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def tile(path, size=None):
    im = Image.open(path).convert("RGBA")
    if size:
        im = im.resize(size, Image.LANCZOS)
    back = Image.new("RGBA", im.size, BG)
    back.alpha_composite(im)
    return back


def grid(rows, labels_left, labels_top, title, out, cell, pad=12, head=44, left=150):
    f, ft = font(22), font(28)
    cols = max(len(r) for r in rows)
    W = left + cols * (cell[0] + pad) + pad
    H = head + 30 + len(rows) * (cell[1] + pad) + pad
    sheet = Image.new("RGBA", (W, H), (34, 38, 34, 255))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 8), title, fill=INK, font=ft)
    for c, lab in enumerate(labels_top):
        d.text((left + c * (cell[0] + pad) + 6, head), lab, fill=INK, font=f)
    for r, row in enumerate(rows):
        y = head + 30 + r * (cell[1] + pad)
        d.text((pad, y + cell[1] // 2 - 12), labels_left[r], fill=INK, font=f)
        for c, p in enumerate(row):
            if p and os.path.exists(p):
                sheet.alpha_composite(tile(p, cell), (left + c * (cell[0] + pad), y))
    sheet.convert("RGB").save(out)
    print(out)


def main(d, title="Elsin"):
    m = json.load(open(os.path.join(d, "manifest.json")))
    names = {"front": "front", "low34": "low three-quarter", "classic": "classic camera", "face": "face", "back34": "behind"}
    views = m["views"]
    grid([[os.path.join(d, "old_%s.png" % v) for v in views], [os.path.join(d, "new_%s.png" % v) for v in views]],
         ["original", "remaster"], [names.get(v, v) for v in views], title + ": the original and the remaster",
         os.path.join(d, "compare.png"), (600, 600))
    if not m["turn"]:
        return
    turn = [os.path.join(d, "turn_%d.png" % i) for i in range(len(m["turn"]))]
    grid([turn[:4], turn[4:]], ["", ""], ["%d degrees" % a for a in m["turn"][:4]], title + ": turntable",
         os.path.join(d, "turntable.png"), (420, 420), left=20)
    rows, left = [], []
    for anim, frames in m["anims"].items():
        rows.append([os.path.join(d, "old_%s_%02d.png" % (anim, f)) for f in frames])
        left.append(anim + "\noriginal")
        rows.append([os.path.join(d, "new_%s_%02d.png" % (anim, f)) for f in frames])
        left.append(anim + "\nremaster")
    if rows:
        top = ["frame %d" % f for f in next(iter(m["anims"].values()))]
        grid(rows, left, top, title + ": the script's animations, 15 frames a second", os.path.join(d, "animations.png"), (300, 300))


if __name__ == "__main__":
    main(sys.argv[1], *(sys.argv[2:3]))
