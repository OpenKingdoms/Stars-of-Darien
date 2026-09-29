"""The spires, one entry per feature, read off the sprites.

Crags (kind "crag", TarSpire01-08): one massif, the highest of its 'peaks'
(x, y, h, r, extras), faceted pyramids whose corners can point along the
arms, and its 'ridges' ((x, y, z), (x, y), extras), sharp-crested arms from
high in the mass down to the ground, broken by the 'noise' settings; then
detached 'knobs' and 'scree'. A point lands on the picture at row
hy - 16 y - 8 z, so a peak's tip reads at 2 y + h cells of height.
Hoodoos (kind "hoodoo", TarSpire09-15): lathed columns, profile (z, r)
bottom to top with (z, r, 1) for a crisp corner, in talus 'heaps' of
angular slabs and gravel on a sandy skirt. A domed top of radius R and
rise h over a side top at z, seen by the classic camera, reaches
2 y + z + sqrt(4 R^2 + h^2) on the picture.
Every entry's 'sturdy' pass (kit.sturdy) turns the pixel fit written here
into the sturdier build the owner asked for on 2026-09-27.
"""
import math


def dome(z0, r, h, n=4):
    """Points of a rounded top from radius r at z0 up to the axis at z0 + h."""
    return [(z0 + h * math.sin(0.5 * math.pi * i / n), r * math.cos(0.5 * math.pi * i / n)) for i in range(1, n + 1)]


def built(profile, girth, height):
    """A profile written in built numbers, turned back through the sturdy pass."""
    return [(z / height if z > 0 else z, r / girth, *rest) for z, r, *rest in profile]


MODELS = {}

# ---------------------------------------------------------------- crags

MODELS["TarSpire01"] = dict(
    kind="crag", seed=1, colour={"light": 0.47},
    sturdy=dict(girth=1.15, height=0.86, back=0.4),
    noise=dict(jag=0.15, crag=0.08, fine=0.06, flutes=0.12),
    peaks=[
        (0.6, 0.6, 5.7, 1.8, dict(ry=1.6, p=1.0, crown=0.2, corners=[(36, 0.9), (139, 0.85), (186, 1.0), (240, 1.0),
                                                                      (298, 0.78), (334, 0.85)])),
        (-2.1, 0.35, 1.9, 0.7, dict(sides=5, p=1.0, crown=0.12)),
        (2.2, 0.25, 2.6, 0.8, dict(sides=5, p=1.0, crown=0.12)),
        (-0.2, -1.8, 1.6, 0.75, dict(sides=5, p=1.0, crown=0.12)),
        (-1.8, -1.3, 1.4, 0.65, dict(sides=5, p=1.0, crown=0.12)),
    ],
    ridges=[
        ((-0.6, 0.5, 2.6), (-3.2, 0.2), dict(k=0.45, width=0.7, taper=0.3, flank=1.5)),
        ((1.8, 0.2, 2.8), (3.0, -0.6), dict(k=0.5, width=0.62, taper=0.3, flank=1.5)),
        ((-0.6, -0.4, 2.2), (-2.9, -1.4), dict(k=0.6, width=0.62, taper=0.3, flank=1.5)),
        ((0.1, -0.5, 2.6), (-0.1, -2.6), dict(k=0.6, width=0.85, taper=0.3, flank=1.5)),
        ((1.1, -0.5, 2.0), (1.6, -2.0), dict(k=0.7, width=0.5, taper=0.35, flank=1.5)),
        ((1.4, 1.2, 2.2), (2.2, 1.7), dict(k=0.8, width=0.6)),
        ((0.0, 1.1, 2.0), (-1.0, 1.6), dict(k=0.8, width=0.55)),
    ],
    knobs=[((-0.8, -2.8, 0.0), (0.5, 0.42, 0.42))],
    scree=[((0.0, -0.9, 3.2, 2.5), 18, {"inner": 0.7, "size": (0.1, 0.24)})],
)

MODELS["TarSpire02"] = dict(
    kind="crag", seed=2, colour={"light": 0.43},
    sturdy=dict(girth=1.25, height=0.8, back=0.45),
    noise=dict(jag=0.13, crag=0.08, fine=0.06, flutes=0.14, res=0.16),
    peaks=[
        (0.6, 0.8, 8.9, 2.0, dict(ry=1.8, p=1.0, crown=0.16, corners=[(35, 0.9), (110, 0.85), (170, 1.0),
                                                                       (230, 1.0), (285, 0.95), (340, 1.0)])),
        (-1.0, 0.8, 6.2, 1.4, dict(sides=5, p=1.0, crown=0.12)),
        (2.0, 0.6, 6.3, 1.0, dict(sides=5, p=1.0, crown=0.12)),
        (-3.1, 0.3, 3.8, 0.9, dict(sides=5, p=1.0, crown=0.12)),
        (-1.6, -1.8, 2.2, 0.9, dict(sides=5, p=1.0, crown=0.12)),
        (0.0, -2.2, 2.4, 1.0, dict(sides=5, p=1.0, crown=0.12)),
        (1.4, -1.4, 2.4, 0.9, dict(sides=5, p=1.0, crown=0.12)),
        (2.6, -0.6, 1.8, 0.8, dict(sides=5, p=1.0, crown=0.12)),
    ],
    ridges=[
        ((0.5, 0.3, 5.5), (0.1, -3.0), dict(k=0.8, width=0.9, taper=0.4, flank=1.4)),
        ((0.0, 0.3, 5.0), (-1.9, -2.4), dict(k=0.8, width=0.8, taper=0.4, flank=1.4)),
        ((2.0, 0.4, 4.5), (2.9, -1.0), dict(k=0.8, width=0.7, taper=0.4, flank=1.4)),
    ],
    scree=[((0.0, -1.6, 3.6, 2.0), 22, {"inner": 0.7, "size": (0.1, 0.24)})],
)

MODELS["TarSpire03"] = dict(
    kind="crag", seed=3, colour={"light": 0.41},
    sturdy=dict(girth=1.25, height=0.8, back=0.4),
    noise=dict(jag=0.12, crag=0.07, fine=0.05, flutes=0.14, res=0.13),
    peaks=[
        (-0.2, 0.4, 7.0, 1.45, dict(ry=1.15, p=1.0, crown=0.18, corners=[(30, 0.9), (110, 0.85), (175, 1.0),
                                                                        (240, 1.0), (300, 0.95)])),
        (-1.5, 0.2, 3.5, 0.8, dict(sides=5, p=1.0, crown=0.12)),
        (1.2, 0.2, 3.8, 0.8, dict(sides=5, p=1.0, crown=0.12)),
        (-0.8, -1.0, 1.6, 0.7, dict(sides=5, p=1.0, crown=0.12)),
        (0.5, -0.9, 1.6, 0.7, dict(sides=5, p=1.0, crown=0.12)),
        (-1.8, -0.4, 1.4, 0.6, dict(sides=5, p=1.0, crown=0.12)),
        (1.7, -0.5, 1.4, 0.6, dict(sides=5, p=1.0, crown=0.12)),
    ],
    ridges=[
        ((-0.2, 0.0, 4.0), (-0.3, -1.8), dict(k=0.8, width=0.7, taper=0.4, flank=1.4)),
    ],
    scree=[((0.0, -0.9, 2.3, 1.3), 14, {"inner": 0.7, "size": (0.08, 0.2)})],
)

MODELS["TarSpire04"] = dict(
    kind="crag", seed=4, colour={"light": 0.44},
    sturdy=dict(girth=1.12, height=0.92, back=0.25),
    noise=dict(jag=0.13, crag=0.07, fine=0.05, flutes=0.1, res=0.12),
    peaks=[
        (0.3, 0.25, 3.8, 1.15, dict(ry=0.9, p=1.0, crown=0.18, corners=[(36, 0.9), (139, 0.85), (186, 1.0),
                                                                        (240, 1.0), (298, 0.78), (334, 0.85)])),
        (-1.25, 0.2, 1.15, 0.45, dict(sides=5, p=1.0, crown=0.12)),
        (1.3, 0.15, 1.55, 0.5, dict(sides=5, p=1.0, crown=0.12)),
        (-0.1, -1.05, 1.0, 0.45, dict(sides=5, p=1.0, crown=0.12)),
        (-1.05, -0.75, 0.85, 0.4, dict(sides=5, p=1.0, crown=0.12)),
    ],
    ridges=[
        ((-0.35, 0.3, 1.7), (-1.95, 0.1), dict(k=0.45, width=0.42, taper=0.3, flank=1.5)),
        ((1.1, 0.12, 1.8), (1.85, -0.35), dict(k=0.5, width=0.38, taper=0.3, flank=1.5)),
        ((-0.35, -0.25, 1.4), (-1.7, -0.85), dict(k=0.6, width=0.38, taper=0.3, flank=1.5)),
        ((0.06, -0.3, 1.7), (-0.25, -1.6), dict(k=0.6, width=0.5, taper=0.3, flank=1.5)),
        ((0.7, -0.3, 1.3), (0.95, -1.15), dict(k=0.7, width=0.32, taper=0.35, flank=1.5)),
        ((0.85, 0.7, 1.3), (1.3, 1.0), dict(k=0.8, width=0.36)),
        ((0.0, 0.65, 1.2), (-0.6, 0.95), dict(k=0.8, width=0.33)),
    ],
    scree=[((0.0, -0.6, 2.0, 1.4), 10, {"inner": 0.7, "size": (0.07, 0.16)})],
)

MODELS["TarSpire05"] = dict(
    kind="crag", seed=5, colour={"light": 0.38},
    sturdy=dict(girth=1.12, height=0.9, back=0.2),
    noise=dict(jag=0.12, crag=0.14, fine=0.06, flutes=0.1, res=0.12),
    peaks=[
        (0.25, 0.85, 3.4, 1.3, dict(sides=5, p=1.0, crown=0.18, cut=(200, 0.55, 0.94))),
        (-1.4, 0.6, 2.7, 0.85, dict(sides=5, p=1.0, crown=0.2, cut=(150, 0.7, 0.9))),
        (1.35, 0.45, 2.5, 0.9, dict(sides=4, p=1.0, crown=0.22, cut=(20, 0.6, 0.92))),
        (-0.2, -0.55, 2.3, 0.95, dict(sides=6, p=1.0, crown=0.2, cut=(250, 0.6, 0.9))),
        (0.95, -1.3, 1.75, 0.85, dict(sides=5, p=1.0, crown=0.25, cut=(300, 0.5, 0.92))),
        (-1.05, -1.45, 1.45, 0.8, dict(sides=4, p=1.0, crown=0.25, cut=(220, 0.5, 0.92))),
        # the common base the knobs rise from, joining them to about half their height
        (0.0, 0.0, 1.2, 2.05, dict(ry=1.8, sides=8, p=0.95, corner=0.12, crown=0.35)),
    ],
    scree=[((0.0, -1.0, 2.0, 1.4), 12, {"inner": 0.7, "size": (0.07, 0.17)})],
)

MODELS["TarSpire06"] = dict(
    kind="crag", seed=6, colour={"light": 0.43},
    sturdy=dict(girth=1.12, height=0.88, back=0.3),
    noise=dict(jag=0.15, crag=0.08, fine=0.06, flutes=0.12),
    peaks=[
        (-0.9, 0.9, 4.4, 1.6, dict(p=1.0, crown=0.16, corners=[(20, 0.9), (110, 0.85), (175, 0.95), (235, 1.0),
                                                                (300, 0.9)])),
        (0.9, 0.6, 3.4, 1.0, dict(sides=5, p=1.0, crown=0.1)),
        (0.0, -0.4, 3.0, 0.9, dict(sides=5, p=1.0, crown=0.1)),
        (-1.6, -1.2, 1.8, 0.7, dict(sides=5, p=1.0, crown=0.12)),
        (0.5, -1.8, 1.5, 0.7, dict(sides=5, p=1.0, crown=0.12)),
        (1.9, -0.6, 1.8, 0.7, dict(sides=5, p=1.0, crown=0.12)),
        (1.4, -1.6, 1.2, 0.7, dict(sides=5, p=1.0, crown=0.12)),
    ],
    ridges=[
        ((-1.1, 0.4, 2.6), (-1.6, -2.3), dict(k=0.6, width=0.7, taper=0.3, flank=1.5)),
        ((0.0, -0.4, 2.4), (0.5, -2.8), dict(k=0.6, width=0.7, taper=0.3, flank=1.5)),
        ((0.9, 0.5, 2.4), (2.6, -0.9), dict(k=0.55, width=0.6, taper=0.3, flank=1.5)),
        ((-1.4, 0.8, 2.6), (-1.95, 0.2), dict(k=0.5, width=0.65, taper=0.3, flank=1.5)),
        ((-1.0, 1.4, 2.4), (-1.5, 2.0), dict(k=0.8, width=0.6)),
    ],
    knobs=[((1.45, 2.55, 0.0), (0.55, 0.5, 1.0)), ((-2.45, 1.55, 0.0), (0.34, 0.3, 0.42))],
    scree=[((0.0, -1.2, 2.6, 2.0), 16, {"inner": 0.7, "size": (0.08, 0.2)})],
)

MODELS["TarSpire07"] = dict(
    kind="crag", seed=7, colour={"light": 0.4},
    sturdy=dict(girth=1.18, height=0.85, back=0.3),
    noise=dict(jag=0.12, crag=0.07, fine=0.06, flutes=0.14, res=0.18),
    peaks=[
        (0.75, 0.8, 11.1, 2.8, dict(ry=2.3, p=1.0, crown=0.12, corners=[(30, 0.9), (100, 0.85), (160, 1.0),
                                                                        (215, 1.0), (270, 0.95), (330, 1.0)])),
        (-1.7, 0.7, 5.8, 1.5, dict(sides=5, p=1.0, crown=0.12)),
        (-3.0, 0.4, 5.0, 1.2, dict(sides=5, p=1.0, crown=0.12)),
        (2.9, 0.4, 4.5, 1.3, dict(sides=5, p=1.0, crown=0.12)),
        (-2.5, -1.8, 2.4, 1.0, dict(sides=5, p=1.0, crown=0.12)),
        (-0.4, -2.2, 2.6, 1.1, dict(sides=5, p=1.0, crown=0.12)),
        (1.6, -2.0, 2.6, 1.1, dict(sides=5, p=1.0, crown=0.12)),
        (2.9, -1.5, 1.6, 0.9, dict(sides=5, p=1.0, crown=0.12)),
    ],
    ridges=[
        ((0.5, 0.3, 7.0), (-0.4, -2.9), dict(k=0.8, width=1.1, taper=0.4, flank=1.4)),
        ((1.2, 0.3, 6.5), (2.0, -2.7), dict(k=0.8, width=1.0, taper=0.4, flank=1.4)),
        ((0.0, 0.7, 7.5), (-2.8, 0.4), dict(k=0.9, width=1.1, taper=0.4, flank=1.4)),
        ((1.6, 0.7, 6.5), (3.6, 0.1), dict(k=0.9, width=1.0, taper=0.4, flank=1.4)),
        ((-0.2, 0.2, 5.0), (-2.4, -2.2), dict(k=0.8, width=0.9, taper=0.4, flank=1.4)),
    ],
    scree=[((0.0, -1.8, 4.2, 2.2), 26, {"inner": 0.7, "size": (0.1, 0.26)})],
)

MODELS["TarSpire08"] = dict(
    kind="crag", seed=8, colour={"light": 0.42},
    sturdy=dict(girth=1.15, height=0.9, back=0.25),
    noise=dict(jag=0.08, crag=0.05, fine=0.04, flutes=0.3, flute_n=14, res=0.1),
    peaks=[
        (0.1, 0.15, 3.5, 1.4, dict(ry=1.2, p=1.0, crown=0.06, corners=[(10, 0.95), (100, 0.8), (170, 0.95),
                                                                        (265, 1.2)])),
        (-0.6, -0.7, 1.0, 0.55, dict(sides=5, p=1.0, crown=0.12)),
        (0.65, -0.6, 0.8, 0.5, dict(sides=5, p=1.0, crown=0.12)),
    ],
    scree=[((0.0, -0.5, 1.5, 1.2), 8, {"inner": 0.75, "size": (0.07, 0.15)})],
)

# ---------------------------------------------------------------- hoodoos
# Heaps: talus of angular slabs round the column feet, `top` high at r_in,
# falling as (1 - u)^fall to r_out, lower toward the camera by `front`; n
# slabs of half size `size` and `chips` small stones on it. The sandy skirt
# 'sand' (cx, cy, rx, ry) has loose grit over its broken edge.

MODELS["TarSpire09"] = dict(
    kind="hoodoo", seed=9,
    sturdy=dict(girth=1.2, height=0.82, back=0.7),
    columns=[dict(profile=[(-0.3, 1.25), (0.5, 1.0), (1.4, 0.82), (2.4, 0.72), (3.8, 0.7), (5.2, 0.74), (6.0, 0.78),
                           (6.6, 0.86), (7.2, 0.8), (7.8, 0.9), (8.4, 0.82), (9.0, 0.92), (9.6, 0.84), (10.2, 0.93),
                           (10.8, 0.86), (11.4, 0.92), (12.0, 0.84), (12.25, 0.86)] + dome(12.25, 0.86, 0.85, 4),
                  x=0.05, y=0.2, lean=(-0.4, 0.0), seg=14, sub=2, period=0.6, ledge=0.05)],
    heaps=[dict(cx=0.05, cy=0.2, r_in=0.9, r_out=2.3, top=1.8, fall=1.5, front=0.75, n=44, size=(0.26, 0.44),
                chips=28)],
    sand=(0.1, 0.0, 2.45, 2.25),
)

MODELS["TarSpire10"] = dict(
    kind="hoodoo", seed=10, bands={"tone": 1.05},
    sturdy=dict(girth=1.06, height=0.94, back=0.2),
    columns=[dict(profile=[(-0.3, 2.3), (1.0, 2.2), (3.0, 2.05), (4.6, 2.1), (5.6, 1.95), (6.4, 2.2), (7.0, 2.28),
                           (7.35, 2.22, 1), (7.45, 1.9), (7.5, 1.0), (7.5, 0.0)],
                  x=0.3, y=0.3, seg=22, sub=3, period=0.8, ledge=0.08)],
    heaps=[dict(cx=0.15, cy=0.3, r_in=2.2, r_out=4.2, rx=1.08, top=2.3, fall=1.4, front=0.5, n=70, size=(0.34, 0.52),
                chips=30, chip_size=(0.08, 0.16))],
    sand=(0.15, 0.2, 4.75, 4.0),
    twigs=[((-3.2, -0.4, 1.6), (-1, -0.3, 0.8), 0.9), ((3.6, 0.4, 1.6), (1, 0.2, 0.9), 0.8)],
)

MODELS["TarSpire11"] = dict(
    kind="hoodoo", seed=11, bands={"tone": 1.08},
    sturdy=dict(girth=1.08, height=0.93, back=0.3),
    columns=[dict(profile=[(-0.3, 2.1), (1.0, 1.8), (2.5, 1.6), (4.2, 1.55), (5.6, 1.65), (6.4, 2.05), (7.0, 2.35),
                           (7.7, 2.3), (8.3, 2.45), (8.9, 2.4)] + dome(8.9, 2.4, 0.8, 4),
                  x=0.35, y=0.5, ry=0.9, seg=22, sub=3, period=0.75, ledge=0.07),
             dict(profile=[(-0.3, 0.7), (1.5, 0.55), (3.0, 0.6), (4.3, 0.52), (5.2, 0.58), (6.0, 0.55)]
                  + dome(6.0, 0.55, 0.6, 3), x=-1.4, y=-1.0, seg=12, sub=2, period=0.6, ledge=0.05)],
    heaps=[dict(cx=0.05, cy=0.3, r_in=1.8, r_out=3.6, rx=1.15, ry=0.85, top=1.9, fall=1.4, front=0.55, n=52,
                size=(0.28, 0.44), chips=30, chip_size=(0.08, 0.16))],
    sand=(0.0, 0.1, 4.4, 3.3),
    twigs=[((3.4, 0.6, 2.2), (1, 0.1, 0.6), 0.8)],
)

MODELS["TarSpire12"] = dict(
    kind="hoodoo", seed=12,
    sturdy=dict(girth=1.32, height=0.78, back=0.7),
    columns=[dict(profile=[(-0.3, 1.0), (0.6, 0.78), (1.6, 0.66), (2.2, 0.64), (3.2, 0.58), (3.8, 0.64), (4.4, 0.56),
                           (5.0, 0.62), (5.6, 0.54), (6.2, 0.6), (6.8, 0.52), (7.4, 0.58), (8.0, 0.5), (8.6, 0.56),
                           (9.2, 0.5), (9.8, 0.55), (10.3, 0.52)] + dome(10.3, 0.52, 0.6, 3),
                  x=0.25, y=0.3, lean=(0.05, 0.0), seg=12, sub=2, period=0.6, ledge=0.04),
             dict(profile=[(-0.3, 0.9), (0.7, 0.66), (1.6, 0.56), (2.2, 0.55), (3.0, 0.6), (3.6, 0.53), (4.3, 0.6),
                           (5.0, 0.54), (5.6, 0.58), (6.1, 0.55)] + dome(6.1, 0.55, 0.6, 3),
                  x=-0.95, y=0.0, lean=(0.05, 0.0), seg=12, sub=2, period=0.6, ledge=0.04)],
    heaps=[dict(cx=-0.2, cy=0.2, r_in=0.9, r_out=2.2, rx=1.1, ry=0.9, top=1.8, fall=1.5, front=0.7, n=36,
                size=(0.24, 0.42), chips=26)],
    sand=(-0.1, 0.0, 2.4, 2.05),
)

MODELS["TarSpire13"] = dict(
    kind="hoodoo", seed=13,
    sturdy=dict(girth=1.25, height=0.8, back=0.7),
    columns=[dict(profile=[(-0.3, 1.15), (0.6, 0.92), (1.6, 0.82), (2.5, 0.78), (3.6, 0.82), (4.4, 0.74), (5.4, 0.82),
                           (6.4, 0.74), (7.4, 0.83), (8.4, 0.75), (9.4, 0.84), (10.4, 0.76), (11.4, 0.84), (12.2, 0.78),
                           (12.9, 0.8)] + dome(12.9, 0.8, 0.8, 4),
                  x=0.2, y=0.4, seg=14, sub=2, period=0.7, ledge=0.05),
             dict(profile=[(-0.3, 0.8), (0.7, 0.62), (1.6, 0.54), (2.6, 0.52), (3.6, 0.56), (4.6, 0.5), (5.6, 0.56),
                           (6.6, 0.5), (7.6, 0.55), (8.6, 0.48), (9.6, 0.53), (10.4, 0.47), (10.9, 0.5)]
                  + dome(10.9, 0.5, 0.55, 3), x=-1.15, y=0.6, lean=(-0.1, 0.0), seg=12, sub=2, period=0.65, ledge=0.05),
             dict(profile=[(-0.3, 0.65), (0.7, 0.5), (1.6, 0.42), (2.4, 0.4), (3.6, 0.43), (4.8, 0.38), (5.8, 0.41),
                           (6.6, 0.38)] + dome(6.6, 0.38, 0.45, 3), x=1.15, y=-0.5, seg=10, sub=2, period=0.6,
                  ledge=0.05)],
    heaps=[dict(cx=0.1, cy=0.2, r_in=1.1, r_out=2.9, rx=1.1, ry=0.85, top=2.0, fall=1.5, front=0.7, n=64,
                size=(0.26, 0.46), chips=32)],
    sand=(0.1, 0.0, 3.15, 2.5),
    twigs=[((-1.9, -0.2, 1.9), (-1, -0.2, 0.9), 1.2), ((1.9, 0.0, 1.9), (1, 0.1, 0.9), 1.2)],
)

MODELS["TarSpire14"] = dict(
    kind="hoodoo", seed=14, bands={"tone": 1.1, "ramp": [(4.4, 1.2), (5.4, 0.8), (7.6, 0.6), (8.05, 1.15)], "under": 0.3,
                                       "top_light": 0.7},
    sturdy=dict(girth=1.22, height=0.87, back=0.15),
    # built: a rock foot flaring to r 0.8 at the talus crest, a 1.0-cell waist
    # at z 2.6, then a tall egg widening at a quarter cell per cell to 1.22
    # at z 7 under a blunt dome, top 8.2
    columns=[dict(profile=built([(-0.3, 1.1), (0.5, 1.0), (1.2, 0.9), (1.85, 0.78), (2.25, 0.58), (2.6, 0.46),
                                 (2.95, 0.48), (3.5, 0.63), (4.2, 0.81), (4.85, 0.94), (5.5, 1.05), (6.25, 1.15),
                                 (7.0, 1.22), (7.4, 1.2)] + dome(7.4, 1.2, 0.8, 4), 1.22, 0.87),
                  x=-0.05, y=0.2, seg=16, sub=2, period=0.6, ledge=0.05)],
    heaps=[dict(cx=0.05, cy=0.03, r_in=0.5, r_out=2.1, ry=0.95, ry_back=0.58, top=1.8, fall=1.3, front=0.8, n=42,
                size=(0.15, 0.25), gap=0.5, chips=44, chip_size=(0.05, 0.1), jitter=0.7, rings=10, seg=30,
                collar=(16, (0.15, 0.24), 0.2, 0.45))],
    sand=(0.1, 0.0, 2.25, 2.0, 1.3),
    sand_pal={"dark": (94, 81, 60), "mid": (120, 104, 75), "light": (146, 126, 92)},
    slab_pal={"dark": (84, 74, 53), "body": (134, 119, 84), "light": (182, 164, 115)},
    mound_pal={"dark": (67, 58, 41), "mid": (110, 97, 69), "light": (143, 124, 90)},
)

MODELS["TarSpire15"] = dict(
    kind="hoodoo", seed=15,
    bands={"tone": 1.32, "ramp": [(5.2, 0.7), (6.3, 0.74), (6.9, 1.35)], "under": 0.6,
           # band n spans z 0.53 n: dark down the trunk, pale on the crown
           "seq": ["mid", "hi", "brown", "mid", "dark", "brown", "mid", "brown", "dark", "brown", "brown", "brown"]},
    sturdy=dict(girth=1.15, height=0.96, back=0.15),
    # built: a stout trunk 2.0 across where it leaves the heap and 1.75 under
    # the head, then a rounded knobbly crown 2.2 across and 2.0 high that
    # only just bulges past it, top 7.1
    columns=[dict(profile=built([(-0.3, 1.45), (0.8, 1.25), (1.6, 1.1), (2.3, 1.0), (3.1, 0.95), (3.9, 0.9),
                                 (4.6, 0.87), (5.0, 0.87), (5.25, 0.95), (5.5, 1.05), (5.8, 1.08), (6.1, 1.04),
                                 (6.4, 0.94), (6.68, 0.78), (6.9, 0.57), (7.03, 0.32), (7.1, 0.0)], 1.15, 0.96),
                  x=0.05, y=0.2, seg=22, sub=2, period=0.55, ledge=0.08, wob=0.1, knob=0.15)],
    # a packed heap of rounded boulders of mixed size about 2.2 up the trunk,
    # touching, so the mound shows only in the crevices between them
    heaps=[dict(cx=0.0, cy=0.1, r_in=0.92, r_out=2.6, ry=0.7, ry_back=0.5, top=2.2, fall=1.25, front=0.85,
                lump=0.2, n=90, size=(0.25, 0.6), grow=0.1, gap=0.75, packed=True, outer=0.95, shape="boulder", flat=0.72,
                tilt=0.12, chips=24, chip_size=(0.06, 0.12))],
    mound_pal={"dark": (30, 25, 19), "mid": (46, 39, 29), "light": (62, 53, 39)},
    sand=(0.0, 0.0, 2.85, 1.9, 1.35),
    sand_pal={"dark": (114, 97, 72), "mid": (143, 123, 89), "light": (175, 150, 110)},
    slab_pal={"dark": (76, 67, 48), "body": (121, 107, 76), "light": (164, 148, 104)},
)
