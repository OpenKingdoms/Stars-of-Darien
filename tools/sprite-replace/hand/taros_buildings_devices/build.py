"""Builds the Taros buildings and devices, one model per name.

    blender -b --factory-startup --python build.py -- [names...]

With no names, builds all 26. Each goes to
D:/OKReplace/hand/taros_buildings_devices/models/<Name>.glb, with its
classic and turned renders beside the sprite in renders/. Tarbuild02, the
intact hall, is not one of ours: named on its own it is built only to line
its ruins up, and its model and renders go to scratch/.
"""
import importlib
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit as K  # noqa: E402
import devices as D  # noqa: E402
import huts as H  # noqa: E402
import zigg as Z  # noqa: E402
import ruin01 as R1  # noqa: E402
import spiked as S  # noqa: E402
import ruin02 as R2  # noqa: E402
import fortress as F  # noqa: E402
import ruin03 as R3  # noqa: E402
import prison as P  # noqa: E402

# Sprite points are (column, row, depth y); a 4th value puts the point at
# that height instead, for things lying on the ground.
MODELS = {
    # gibbets: foot of the post, and the hub drawn at the wheel's middle
    "TarDev01": lambda: D.gibbet("TarDev01", (22, 102), (25, 23.5, 0.0)),
    "TarDev02": lambda: D.gibbet("TarDev02", (24.5, 103), (26, 23.5, 0.0)),
    "TarDev03": lambda: D.gibbet("TarDev03", (24, 105), (24, 23.5, 0.0)),

    # impalers: the cairn's body() shape, then horns (sprite points, root
    # radius, taper), then rings (point, face, radius)
    # 05's pedestal is a rough square frustum seen corner-on (n 16, turned
    # 45); 04 and 06 stand on the same, and are built in bespoke/
    "TarDev05": lambda: D.impaler("TarDev05", dict(a0=1.24, a1=0.86, h=3.5, cap=1.45, n=16, rz=45, dome=False,
                                                   lump=0.03), [
        ([(34, 66, 0), (35, 32, 0), (33, 2, 0.1)], 0.2),
        ([(28, 68, 0), (14, 44, 0), (3, 20, 0.1)], 0.17),
        ([(40, 68, 0), (54, 46, 0), (66, 20, 0.1)], 0.17),
        ([(24, 90, -0.4), (10, 95, -0.5), (1, 92, -0.5), (0, 84, -0.5)], 0.23, 2.6),
        ([(46, 88, -0.4), (60, 94, -0.5), (69, 90, -0.5), (70, 80, -0.5)], 0.23, 2.6),
    ], rings=[((17, 110, -0.8), (-1, -1))], seed=5),

    # damaged impalers: the stump snapped to a jagged top (depth, jag, seed),
    # silver horns standing, leaning or lying (a 4th value is a height)
    "TarDev04a": lambda: D.impaler_ruin("TarDev04a", dict(a0=1.2, a1=0.82, h=4.3, cap=0, n=16, rz=45, yscale=0.85,
                                                          x=0.3, lump=0.08), (0.5, 0.45, 3), [
        ([(54, 44, 0), (44, 30, 0), (41, 16, 0.1), (46, 3, 0.2)], 0.24, 2.4),
        ([(70, 40, 0), (80, 24, 0), (89, 12, 0.1), (99, 4, 0.2)], 0.24, 2.4),
        ([(50, 54, -0.7), (42, 72, -1.0), (38, 90, None, 0.2)], 0.2, 2.2),
        ([(52, 117, 0, 0.22), (80, 112, 0, 0.24), (100, 97, 0, 0.24), (110, 72, 0, 0.14)], 0.24, 2.4),
        ([(40, 98, 0, 0.16), (47, 110, 0, 0.16), (51, 124, 0, 0.12)], 0.17, 2.2),
    ], rings=[((12, 88, 0.47), None)], seed=7),
    "TarDev05a": lambda: D.impaler_ruin("TarDev05a", dict(a0=1.24, a1=0.9, h=3.8, cap=0, n=16, rz=45, lump=0.03,
                                                          x=0.1), (0.4, 0.4, 5), [
        ([(70, 42, 0), (72, 20, 0), (68, 2, 0.1)], 0.22, 2.4),
        ([(60, 36, 0), (42, 32, 0), (28, 44, -0.2), (20, 64, -0.5), (18, 86, 0, 0.2)], 0.24, 2.6),
        ([(80, 64, -0.5), (88, 66, -0.6), (92, 70, -0.6)], 0.2, 2.2),
        ([(55, 64, -0.5), (45, 62, -0.7), (38, 68, -0.8)], 0.2, 2.2),
    ], rings=[((77, 82, -0.8), (1, -1))], seed=8),
    "TarDev06a": lambda: D.impaler_ruin("TarDev06a", dict(a0=1.12, a1=0.62, h=4.2, cap=0, n=16, rz=45, yscale=0.88,
                                                          lump=0.06), (0.4, 0.4, 7), [
        ([(64, 32, 0), (68, 12, 0), (70, 0, 0.1)], 0.22, 2.4),
        ([(50, 48, 0), (30, 48, 0), (15, 58, 0), (8, 68, 0)], 0.22, 2.4),
        ([(78, 50, 0), (95, 48, 0), (104, 38, 0), (104, 30, 0)], 0.22, 2.4),
        ([(20, 100, 0, 0.2), (50, 108, 0, 0.24), (80, 106, 0, 0.24), (97, 98, 0, 0.16)], 0.24, 2.4),
        ([(22, 70, 0, 0.14), (15, 85, 0, 0.14), (10, 98, 0, 0.12)], 0.17, 2.2),
    ], rings=[((106, 74, 0.55), None)], seed=9),

    # stocks: the dais (width, depth, thickness, turn) and each set of stocks
    # (x, y, turn[, studs along the front])
    "TarDev07": lambda: D.stocks("TarDev07", 3.8, 5.7, 0.4, 4.7, [
        (-1.85, 1.2, -90 + 4.7), (-1.7, -1.7, -90 + 4.7), (1.65, 1.65, 90 + 4.7), (1.9, -1.26, 90 + 4.7)]),
    "TarDev08": lambda: D.stocks("TarDev08", 6.0, 3.6, 0.4, 2.5, [
        (-1.47, 1.8, 180 + 2.5), (1.81, 1.84, 180 + 2.5), (-1.53, -1.85, 2.5 - 15, 5), (1.75, -1.8, 2.5, 5)]),

    "TarDev09": lambda: D.guillotine("TarDev09", (12, 115), (137, 138), 19, 146),

    # hovels and their wrecks
    "TarHut01": H.hut01,
    "TarHut02": H.hut02,
    "TarHut01a": H.hut01_ruin,
    "TarHut02a": H.hut02_ruin,

    # the spiral tower on its platform, and its ruins
    "Tarbuild01": Z.tarbuild01,
    "Tarbuild01a": R1.tarbuild01a,
    "Tarbuild01b": R1.tarbuild01b,

    # the spiked hall's ruins; the intact hall is not ours to ship and is
    # built only to line the ruins up (python build.py -- Tarbuild02)
    "Tarbuild02": S.tarbuild02,
    "Tarbuild02a": R2.tarbuild02a,

    # the fortress and its ruins
    "Tarbuild03": F.tarbuild03,
    "Tarbuild03a": R3.tarbuild03a,
    "Tarbuild03b": R3.tarbuild03b,

    # the prison and its ruin
    "Tarbuild04": P.tarbuild04,
}


# models that did not converge through the kit are built one to a script
# in bespoke/, each shaped against its own sprite
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "bespoke"))
for _n in ("TarDev04", "TarDev06", "Tarbuild02b", "Tarbuild04a"):
    MODELS[_n] = importlib.import_module(_n).build

CALIBRATION = ("Tarbuild02",)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or [n for n in MODELS if n not in CALIBRATION]
    for n in names:
        if n not in MODELS:
            print("HANDKIT_SKIP", n, "not in the table")
            continue
        t = time.time()
        K.reset()
        if n.startswith("Tarbuild"):
            K.TONE = 0.8
        elif n.startswith("TarHut"):
            K.TONE = 0.78 if n.endswith("a") else 0.86
        elif n[:8] in ("TarDev04", "TarDev05", "TarDev06"):
            K.TONE = 0.8
        parts = MODELS[n]()
        painted = getattr(parts, "painted", ())
        ob, tris = K.finish(n, parts, painted)
        if n in CALIBRATION:
            glb = os.path.join(K.OUT, "models", n + ".glb")
            os.replace(glb, os.path.join(K.OUT, "scratch", n + ".glb"))
            for view in ("classic", "turned"):
                p = os.path.join(K.OUT, "renders", "%s_%s.png" % (n, view))
                os.replace(p, os.path.join(K.OUT, "scratch", os.path.basename(p)))
        print("HANDKIT_DONE %s tris=%d %.1fs" % (n, tris, time.time() - t))


if __name__ == "__main__":
    main()
