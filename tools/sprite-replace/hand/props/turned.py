"""All the family's turned (low three-quarter) views on one sheet, system Python:

    python turned.py [size] [out name]

Five to a row at size pixels each, labelled, written to work/<out name>.png.
"""
import os
import sys

from PIL import Image, ImageDraw

from sheets import ORDER, OUT

size = int(sys.argv[1]) if len(sys.argv) > 1 else 300
name = sys.argv[2] if len(sys.argv) > 2 else "turned_all"
cols = 5
rows = (len(ORDER) + cols - 1) // cols
sheet = Image.new("RGBA", (cols * size, rows * (size + 16)), (84, 104, 64, 255))
d = ImageDraw.Draw(sheet)
for i, n in enumerate(ORDER):
    t = Image.open(os.path.join(OUT, "renders", n + "_turned.png")).convert("RGBA").resize((size, size), Image.LANCZOS)
    x, y = (i % cols) * size, (i // cols) * (size + 16)
    sheet.alpha_composite(t, (x, y + 16))
    d.text((x + 4, y + 2), n, fill=(255, 255, 255, 255))
sheet.save(os.path.join(OUT, "work", name + ".png"))
