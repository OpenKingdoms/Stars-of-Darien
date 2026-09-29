"""Reads a sprite for modelling, run with the system Python.

    python probe.py spans <Name> [step]          silhouette span per row
    python probe.py colour <Name> x0 y0 x1 y1   median sRGB of a box's pixels
    python probe.py grid <Name> [cell]           median colour of each cell of a grid
    python probe.py clusters <Name> [k] [x0 y0 x1 y1]   the main colours and their share
"""
import json
import sys

from PIL import Image

ROOT = r"D:\OKReplace"


def load(name):
    cat = {r["name"]: r for r in json.load(open(ROOT + r"\catalog.json"))}
    im = Image.open(ROOT + r"\sprites\%s.png" % name).convert("RGBA")
    return cat[name], im


def median(px):
    if not px:
        return None
    return tuple(sorted(p[i] for p in px)[len(px) // 2] for i in range(3))


def box_pixels(im, x0, y0, x1, y1):
    out = []
    for y in range(max(0, y0), min(im.height, y1)):
        for x in range(max(0, x0), min(im.width, x1)):
            p = im.getpixel((x, y))
            if p[3] > 128:
                out.append(p[:3])
    return out


def main():
    cmd, name = sys.argv[1], sys.argv[2]
    r, im = load(name)
    hx, hy = r["sprite"]["hotspot"]
    print(name, "size", im.size, "hotspot", (hx, hy), "footprint", r["footprint"], "height", r["height"])
    if cmd == "spans":
        step = int(sys.argv[3]) if len(sys.argv) > 3 else 2
        for y in range(0, im.height, step):
            xs = [x for x in range(im.width) if im.getpixel((x, y))[3] > 128]
            if xs:
                runs, s = [], xs[0]
                for a, b in zip(xs, xs[1:] + [None]):
                    if b != a + 1:
                        runs.append((s, a))
                        s = b
                print("row %3d (%+4d)  %s" % (y, hy - y, " ".join("%d-%d" % ru for ru in runs)))
    elif cmd == "colour":
        x0, y0, x1, y1 = map(int, sys.argv[3:7])
        px = box_pixels(im, x0, y0, x1, y1)
        print(len(px), "px median", median(px))
    elif cmd == "grid":
        c = int(sys.argv[3]) if len(sys.argv) > 3 else 8
        for y0 in range(0, im.height, c):
            row = []
            for x0 in range(0, im.width, c):
                m = median(box_pixels(im, x0, y0, x0 + c, y0 + c))
                row.append("%3d,%3d,%3d" % m if m else "     .     ")
            print("%3d " % y0 + " | ".join(row))
    elif cmd == "clusters":
        k = int(sys.argv[3]) if len(sys.argv) > 3 else 6
        x0, y0, x1, y1 = map(int, sys.argv[4:8]) if len(sys.argv) > 7 else (0, 0, im.width, im.height)
        px = box_pixels(im, x0, y0, x1, y1)
        cents = [px[i * len(px) // k] for i in range(k)]
        for _ in range(12):
            groups = [[] for _ in cents]
            for p in px:
                j = min(range(len(cents)), key=lambda c: sum((p[i] - cents[c][i]) ** 2 for i in range(3)))
                groups[j].append(p)
            cents = [tuple(sum(q[i] for q in g) // len(g) for i in range(3)) if g else cents[j]
                     for j, g in enumerate(groups)]
        for c, g in sorted(zip(cents, groups), key=lambda cg: -len(cg[1])):
            print("  %3d%%  %s" % (100 * len(g) // len(px), c))


if __name__ == "__main__":
    main()
