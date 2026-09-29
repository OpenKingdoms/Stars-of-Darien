"""Ruins of the Aramon cottages: stone walls standing to ragged tops, the
roof fallen in as rubble with thatch, stones and timbers on it, and the
chimney snapped off. Built on the intact bodies where they exist."""
import math
import random

import kit
from kit import Mat, px
from models import HUT04, HUT06, P, broken_chimney, model, ruin_mats


def inner(r, p, t=0.3, z=0.3, grow=0.0):
    """The body's floor inside its walls as sprite pixels at height z."""
    c, s = math.cos(math.radians(p["yaw"])), math.sin(math.radians(p["yaw"]))
    hl, hw = p["L"] / 2 - t + grow, p["W"] / 2 - t + grow
    out = []
    for u, v in ((-hl, -hw), (hl, -hw), (hl, hw), (-hl, hw)):
        x, y = p["cx"] + u * c - v * s, p["cy"] + u * s + v * c
        out.append(kit.screen(r, x, y, z))
    return out


def sheets(B, mat, r, spots, seed=0):
    """Fallen thatch lying in flat torn sheets: (col, row, z, size) each,
    where the sprite draws the sheet's middle."""
    g = random.Random(seed)
    for c, rw, z, sz in spots:
        x, y = px(r, c, rw, z)
        k = g.randint(3, 5)
        pts = []
        for i in range(k):
            a = 2 * math.pi * (i + g.uniform(-0.25, 0.25)) / k
            rr = sz * g.uniform(0.45, 0.75)
            pts.append((rr * math.cos(a), rr * math.sin(a), 0.0))
        with B.at(x, y, z, yaw=g.uniform(0, 360), pitch=g.uniform(-18, 18), roll=g.uniform(-18, 18)):
            B.slab(mat, pts, 0.12)


def stones(B, mat, r, spots, seed=0):
    """Round field stones from the walls, (col, row, z, size) each."""
    for i, (c, rw, z, sz) in enumerate(spots):
        x, y = px(r, c, rw, z)
        kit.rock(B, mat, x, y, z - sz * 0.25, sz, sz * 0.85, sz * 0.6, seed=seed + i)


def sheet_mat(n, seed=61):
    return Mat(n + "_sheet", tex=kit.tex_thatch(n + "_sheet", "#9a8446", dark="#5a4a26", light="#c09a5a", seed=seed,
                                                speck="#d08a4a", speck_amt=0.05), uv=1.2)


def cottage_ruin(B, r, p, tops, heap_top=0.7, n_debris=60, seed=1, t=0.3, spill=None, mats=None):
    """The walls of body p to their ragged tops, the floor heaped with the
    fallen roof, and debris lying on the heap; returns the materials and
    the heap's surface."""
    n = r["name"]
    m = mats or ruin_mats(n)
    m["sheet"] = sheet_mat(n)
    kit.ruin_walls(B, m["wall"], m["top"], p, tops, t=t, seed=seed)
    hs = [kit.heap(B, m["heap"], r, inner(r, p, t=t * 0.9, z=heap_top * 0.5), heap_top, z_at=heap_top * 0.5,
                   cell=0.4, noise=0.22, seed=seed + 1, edge=0.9)]
    for i, (poly, top) in enumerate(spill or []):
        hs.append(kit.heap(B, m["heap"], r, poly, top, z_at=top * 0.4, cell=0.35, noise=0.15, seed=seed + 5 + i,
                           edge=0.5))
    ground = kit.ground_of(*hs)
    kit.debris(B, m, r, inner(r, p, t=t, z=heap_top * 0.5), n_debris, z_at=heap_top * 0.5, size=(0.5, 1.8),
               seed=seed + 2, tilt=30, kinds={"beam": 3, "plank": 2, "stone": 5, "straw": 3}, ground=ground)
    return m, ground


# ---------------------------------------------------------------- the turned cottages

RUIN04 = P(cx=-0.33, cy=-0.25, yaw=72.6, L=5.5, W=4.35, H=1.8, R=0, hip=0.3)
RUIN06 = P(cx=0.28, cy=-0.38, yaw=100.0, L=5.8, W=4.55, H=2.6, R=0, hip=0.28)


@model("AraHut04a")
def arahut04a(B, r):
    p = RUIN04
    # local -u is the front end (bottom of the picture); S is the right wall
    m, ground = cottage_ruin(B, r, p, {
        "S": [(0, 0.5), (0.3, 0.7), (0.45, 1.2), (0.65, 1.7), (1, 1.85)],
        "E": [(0, 1.85), (0.45, 1.6), (0.7, 1.8), (1, 1.9)],
        "N": [(0, 1.9), (0.25, 1.8), (0.5, 1.2), (0.75, 0.7), (1, 0.45)],
        "W": [(0, 0.45), (0.4, 0.25), (0.7, 0.5), (1, 0.5)],
    }, heap_top=0.75, n_debris=70, seed=3, spill=[
        ([(2, 88), (8, 82), (30, 98), (56, 108), (60, 114), (20, 112), (4, 102)], 0.3)])
    # the chimney stack at the back left corner, its pot at col 34 row 10
    x, y = px(r, 34, 16, 2.3)
    broken_chimney(B, m, x, y, 0.0, 2.2, 0.9, 0.85)
    sheets(B, m["sheet"], r, [(66, 40, 0.8, 1.3), (22, 70, 0.7, 1.0), (45, 92, 0.6, 0.9), (63, 97, 0.5, 0.8),
                              (74, 60, 0.8, 0.5)], seed=4)
    # a door board fallen flat, cols 40-65 rows 65-80
    a, b = px(r, 40, 71, 0.7), px(r, 66, 76, 0.7)
    B.beam(m["plank"], (a[0], a[1], 0.75), (b[0], b[1], 0.8), 0.8, 0.08)
    stones(B, m["stone"], r, [(30, 50, 0.8, 0.35), (38, 55, 0.8, 0.3), (46, 52, 0.8, 0.35), (54, 57, 0.8, 0.3),
                              (26, 58, 0.7, 0.3), (60, 47, 0.8, 0.3), (40, 62, 0.8, 0.3), (18, 85, 0.6, 0.35),
                              (28, 92, 0.5, 0.3), (72, 82, 0.5, 0.3)], seed=5)


@model("AraHut06a")
def arahut06a(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    p = RUIN06
    m = ruin_mats(n, wall="#4a4030", top="#b0a490", heap="#2e261c")
    m["heap"] = Mat(n + "_heap", tex=kit.tex_grit(n + "_heap", "#2e2418", light="#8a7a60", dark="#0e0a06", uv=1.5,
                                                   chips=0.1, darks=0.25, seed=12), uv=1.5)
    # only the west and north walls stand, pale topped, in an L; the east
    # and south are down to stubs under the rubble
    kit.ruin_walls(B, m["wall"], m["top"], p, {
        "N": [(0, 2.5), (0.3, 2.3), (0.5, 1.6), (0.65, 1.9), (0.85, 1.6), (1, 1.2)],
        "E": [(0, 0.7), (0.3, 1.4), (0.6, 2.2), (1, 2.5)],
    }, t=0.3, seed=5, jitter=0.12, notch=0.1)
    kit.ruin_walls(B, m["wall"], m["wall"], p, {
        "S": [(0, 0.15), (0.3, 0.3), (0.6, 0.2), (1, 0.35)],
        "W": [(0, 0.3), (0.5, 0.15), (1, 0.2)],
    }, t=0.3, seed=7, jitter=0.08, notch=0.1)
    # the fallen roof heaped inside and spilling out past the front and east
    inside = inner(r, p, t=0.25, z=0.4)
    front = [(18, 100), (60, 98), (88, 104), (84, 124), (30, 125), (14, 112)]
    east = [(62, 40), (86, 34), (90, 80), (86, 106), (64, 104)]
    hs = [kit.heap(B, m["heap"], r, kit.jag(inside, 2.0, 4.0, 1), 0.8, z_at=0.4, cell=0.35, noise=0.06, seed=2,
                   edge=1.0, pic=pic)]
    for i, q in enumerate((front, east)):
        hs.append(kit.heap(B, m["heap"], r, kit.jag(q, 2.0, 4.0, 3 + i), 0.5, z_at=0.25, cell=0.35, noise=0.06,
                           seed=4 + i, edge=0.6, pic=pic))
    ground = kit.ground_of(*hs)
    # one large sheet of the burnt roof lies diagonally across the middle
    corners = [(10, 66, 0.55), (22, 48, 0.85), (68, 20, 1.3), (72, 38, 1.1)]
    sheet = Mat(n + "_bigsheet", tex=kit.tex_thatch(n + "_bigsheet", "#3e2e1e", dark="#1a120a", light="#5a4428",
                                                     seed=7, speck="#8a5a2a", speck_amt=0.03), uv=1.4)
    B.slab(sheet, [px(r, c, rw, z) + (z,) for c, rw, z in corners], 0.12)
    # charred thatch, timbers and stones on the heap
    cols = kit.palette(pic, k=6)
    stone = kit.swatches(n + "_st", cols, "mottle", seed=20)
    wood = kit.swatches(n + "_wd", [c for c in cols if c[0] >= c[2]] or cols, "planks", seed=30)
    dark = Mat(n + "_charsheet", tex=kit.tex_thatch(n + "_charsheet", "#2a1e12", dark="#120c06", light="#4a3620",
                                                     seed=8), uv=1.0, ref="#2a1e12")
    rim = Mat(n + "_charrim", "#c08a4a", ref="#c08a4a")
    dm = {"stone": stone, "chunk": stone, "slab": stone, "beam": wood, "board": wood, "sheet": [dark],
          "sheet_rim": [rim]}
    for i, (q, k, z) in enumerate(((inside, 55, 0.4), (front, 26, 0.25), (east, 18, 0.25))):
        kit.cover(B, dm, r, kit.plan_of(r, q, z), ground, k, {"sheet": 2, "beam": 3, "stone": 3, "board": 1},
                  pic=pic, size=(0.6, 1.2), length=(0.8, 2.0), seed=40 + i, tilt=25, grow=0, stone=(0.25, 0.5),
                  jumble=0.15)
    # the chimney breast on the east wall stands in grey stone, cols 58-82
    x, y = px(r, 70, 80, 1.4)
    broken_chimney(B, m, x, y, 0.0, 1.5, 0.9, 1.3)
    kit.leaning(B, m["beam"], r, [(30, 30, 0.9, 58, 20, 1.6)], w=0.16)


RUIN05 = P(cx=0.25, cy=-0.5, yaw=-8.0, L=6.44, W=3.38, H=1.78, R=0, hip=-1)
RUIN07 = P(cx=0.4, cy=-0.55, yaw=0.0, L=4.7, W=3.125, H=1.6, R=0, hip=-1)


@model("AraHut05a")
def arahut05a(B, r):
    p = RUIN05
    m, ground = cottage_ruin(B, r, p, {
        "W": [(0, 2.3), (0.5, 2.1), (1, 1.5)],
        "N": [(0, 0.4), (0.25, 0.9), (0.45, 1.5), (0.6, 1.9), (0.8, 2.1), (1, 2.3)],
        "S": [(0, 1.5), (0.2, 1.1), (0.4, 0.9), (0.6, 0.5), (0.8, 0.3), (1, 0.3)],
        "E": [(0, 0.3), (0.5, 0.2), (1, 0.4)],
    }, heap_top=0.7, n_debris=60, seed=7, spill=[
        ([(60, 22), (90, 25), (108, 40), (111, 60), (100, 85), (80, 92), (58, 80), (52, 55)], 1.1)])
    # the chimney stands at the top of the west end, col 10
    x, y = px(r, 12, 10, 2.6)
    broken_chimney(B, m, x, y, 0.0, 2.5, 0.88, 0.88)
    # the door leaf hangs open in the front wall, cols 26-40
    x, y = px(r, 34, 70)
    B.box(m["plank"], x, y + 0.1, 0.0, 0.8, 0.1, 1.5, yaw=-30)
    B.box(m["beam"], x - 0.5, y + 0.2, 0.0, 0.14, 0.14, 1.6)
    sheets(B, m["sheet"], r, [(62, 66, 0.8, 0.8), (76, 54, 1.0, 0.9), (92, 44, 1.0, 0.8), (82, 76, 0.7, 0.7),
                              (68, 40, 0.8, 0.6), (98, 66, 0.8, 0.6)], seed=8)
    stones(B, m["stone"], r, [(70, 30, 1.0, 0.35), (84, 34, 1.0, 0.4), (100, 50, 0.8, 0.35), (72, 84, 0.6, 0.4),
                              (90, 80, 0.6, 0.35), (56, 50, 0.8, 0.3), (46, 60, 0.7, 0.3), (104, 36, 0.8, 0.3),
                              (8, 82, 0.3, 0.35)], seed=9)
    kit.debris(B, m, r, [(60, 22), (90, 25), (108, 40), (111, 60), (100, 85), (80, 92), (58, 80)], 30, z_at=0.5,
               size=(0.5, 1.6), seed=10, tilt=30, kinds={"beam": 3, "stone": 4, "straw": 2}, ground=ground)


@model("AraHut07a")
def arahut07a(B, r):
    p = RUIN07
    m, ground = cottage_ruin(B, r, p, {
        "W": [(0, 1.7), (0.5, 1.6), (1, 1.4)],
        "N": [(0, 1.95), (0.3, 1.8), (0.4, 1.1), (0.7, 1.0), (1, 1.7)],
        "E": [(0, 1.0), (0.5, 1.4), (1, 1.95)],
        "S": [(0, 1.2), (0.3, 0.5), (0.6, 0.4), (0.8, 0.8), (1, 1.0)],
    }, heap_top=0.5, n_debris=55, seed=9)
    # the lean-to room on the west end, its walls still standing
    kit.ruin_walls(B, m["wall"], m["top"], P(cx=-2.45, cy=-0.62, yaw=0.0, L=1.2, W=1.8, H=1.3, R=0), {
        "W": [(0, 1.3), (1, 1.2)], "N": [(0, 1.3), (1, 1.3)], "S": [(0, 0.6), (1, 1.1)]}, t=0.25, seed=11)
    kit.heap(B, m["heap"], r, [(3, 30), (17, 30), (17, 58), (3, 58)], 0.4, z_at=0.2, cell=0.3, noise=0.15, seed=16,
             edge=0.4)
    # a partition closes a small back room at the east end
    kit.broken_wall(B, m["wall"], m["top"], (1.25, 0.95), (1.25, 0.3), 0.25, [(0, 1.6), (1, 1.5)], seed=12)
    kit.broken_wall(B, m["wall"], m["top"], (1.25, 0.3), (2.6, 0.3), 0.25, [(0, 1.5), (1, 1.4)], seed=13)
    # boards of the upper floor fallen across the middle
    a, b = px(r, 44, 25, 0.6), px(r, 66, 20, 0.6)
    for i in range(4):
        B.beam(m["plank"], (a[0], a[1] - 0.3 * i, 0.55 + 0.05 * i), (b[0], b[1] - 0.3 * i, 0.6), 0.28, 0.06)
    sheets(B, m["sheet"], r, [(78, 20, 0.8, 0.6), (70, 40, 0.6, 0.6), (84, 48, 0.6, 0.5), (36, 40, 0.5, 0.5),
                              (4, 58, 0.3, 0.5)], seed=14)
    stones(B, m["stone"], r, [(30, 40, 0.5, 0.3), (38, 48, 0.5, 0.3), (56, 44, 0.5, 0.3), (72, 55, 0.4, 0.35),
                              (22, 62, 0.2, 0.3), (60, 60, 0.3, 0.3)], seed=15)


# ---------------------------------------------------------------- the slab-roofed houses

def rafters(B, mat, r, poly, ground, n, length=(2.0, 3.5), w=0.16, seed=0, z_at=0.8):
    """Long roof timbers fallen across a heap: each laid between two points
    of its surface, a few with one end still up on a wall."""
    g = random.Random(seed)
    P = [px(r, c, rw, z_at) for c, rw in poly]
    xs, ys = [q[0] for q in P], [q[1] for q in P]
    k = tries = 0
    while k < n and tries < n * 80:
        tries += 1
        x, y = g.uniform(min(xs), max(xs)), g.uniform(min(ys), max(ys))
        if not kit.inside_poly(P, x, y):
            continue
        a = g.uniform(0, math.pi)
        L = g.uniform(*length)
        dx, dy = math.cos(a) * L / 2, math.sin(a) * L / 2
        z0 = ground(x - dx, y - dy) + w * 0.4
        z1 = ground(x + dx, y + dy) + w * 0.4
        if g.random() < 0.2:
            z1 += g.uniform(0.4, 1.0)
        B.beam(mat, (x - dx, y - dy, z0), (x + dx, y + dy, z1), w, w * g.uniform(0.8, 1.2), twist=g.uniform(-20, 20))
        k += 1


def slab_ruin(B, r, p, tops, heap_top=1.6, seed=1, n_pieces=150, front_frame=(-0.5, 0.5), pic=None):
    """A slab-roofed cottage fallen in: ragged dark stubs of its end and back
    walls, the front frame standing, and a mound heaped highest in the
    middle, covered in crossing timbers, roof slabs and stones that spill
    over its outline; colours read off the sprite."""
    n = r["name"]
    pic = pic or kit.Picture(r)
    m = {"wall": Mat(n + "_swall", tex=kit.tex_stone(n + "_swall", "#3a3630", rows=6, seed=seed + 1,
                                                     mortar="#1e1c18"), uv=2.0),
         "top": Mat(n + "_stop", tex=kit.tex_mottle(n + "_stop", "#4a453c", var=0.25, seed=seed + 2), uv=1.0),
         "timber": Mat(n + "_stimber", tex=kit.tex_planks(n + "_stimber", "#221c16", boards=2, seed=seed + 3), uv=1.0),
         "chim": Mat(n + "_schim", tex=kit.tex_stone(n + "_schim", "#3e3a34", rows=7, seed=seed + 4,
                                                     mortar="#1a1814"), uv=1.5),
         "heap": Mat(n + "_ash", tex=kit.tex_grit(n + "_ash", "#1e1a16", light="#8a8680", dark="#080706", uv=2.0,
                                                  chips=0.08, darks=0.3, seed=seed + 5), uv=2.0)}
    kit.ruin_walls(B, m["wall"], m["top"], p, tops, t=0.32, seed=seed, jitter=0.25, notch=0.4)
    if front_frame:
        # what stands of the front frame: sill, posts and a rail between
        # front_frame's two fractions of the front's length
        f0, f1 = front_frame
        L, W = p["L"], p["W"]
        with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
            y = -W / 2 + 0.1
            a, b = -L / 2 + L * (f0 + 0.5), -L / 2 + L * (f1 + 0.5)
            B.beam(m["timber"], (a, y, 0.08), (b, y, 0.08), 0.18)
            B.beam(m["timber"], (a, y, 1.1), (b, y, 1.05), 0.15)
            k = max(1, int(round((b - a) / 1.2)))
            g = random.Random(seed)
            for i in range(k + 1):
                xx = a + (b - a) * i / k
                B.beam(m["timber"], (xx, y, 0), (xx, y, g.uniform(1.0, 1.5)), 0.16)
                if i < k and g.random() < 0.6:
                    B.beam(m["timber"], (xx, y, 0.1), (a + (b - a) * (i + 1) / k, y, 1.0), 0.12)
    # the fallen roof mounded up inside, highest in the middle
    poly = inner(r, p, t=0.1, z=heap_top * 0.4)
    h = kit.heap(B, m["heap"], r, kit.jag(poly, 2.5, 4.0, seed), heap_top, z_at=heap_top * 0.4, cell=0.4,
                 noise=0.06, seed=seed + 6, edge=1.9, pic=pic)
    cols = kit.palette(pic, k=6)
    slab = kit.swatches(n + "_ds", cols, "mottle", seed=seed + 10, gain=1.3)
    wood = kit.swatches(n + "_dw", [c for c in cols if c[0] >= c[2]] or cols, "planks", seed=seed + 20, gain=1.3)
    dm = {"slab": slab, "stone": slab, "beam": wood, "board": wood}
    # pieces lie over the mound and spill past its edge onto the ground
    wide = inner(r, p, t=-0.8, z=heap_top * 0.3)
    kit.cover(B, dm, r, kit.plan_of(r, wide, heap_top * 0.3), kit.ground_of(h, lambda x, y: 0.0), n_pieces,
              {"beam": 4, "slab": 4, "stone": 2, "board": 1}, pic=pic, size=(0.5, 1.0), length=(1.2, 2.8),
              width=(0.12, 0.2), seed=seed + 7, tilt=25, grow=0, stone=(0.3, 0.55), jumble=0.2, stick=0.08)
    # the pale stones of the walls scattered through it
    kit.cover(B, dm, r, kit.plan_of(r, wide, heap_top * 0.3), kit.ground_of(h, lambda x, y: 0.0), n_pieces // 3,
              {"stone": 1}, pic=pic, stone=(0.15, 0.3), seed=seed + 8, tilt=25, grow=0, q=0.85, jumble=0.25)
    return m, h


RUIN01 = P(cx=0.06, cy=0.15, yaw=0.0, L=7.4, W=3.9, H=1.6, R=0, hip=-1)
RUIN02 = P(cx=-0.12, cy=0.05, yaw=0.0, L=7.0, W=3.5, H=1.6, R=0, hip=-1)


@model("AraHut01a")
def arahut01a(B, r):
    p = RUIN01
    m, h = slab_ruin(B, r, p, {
        "W": [(0, 1.1), (0.3, 0.6), (0.6, 0.9), (1, 0.5)],
        "N": [(0, 0.8), (0.2, 0.4), (0.4, 0.7), (0.6, 0.3), (0.8, 0.6), (1, 0.9)],
        "E": [(0, 0.7), (0.5, 1.0), (1, 0.6)],
    }, heap_top=1.9, seed=21, n_pieces=200, front_frame=(0.1, 0.85))
    # the chimney stack at the west end, col 12
    x, y = px(r, 12, 30, 2.2)
    broken_chimney(B, m, x, y, 0.0, 2.1, 0.95, 1.0)
    # two rafters slid off the heap to the ground at the front left
    kit.leaning(B, m["timber"], r, [(2, 80, 0.1, 22, 64, 1.1), (8, 86, 0.05, 30, 70, 1.0)], w=0.17)


@model("AraHut02a")
def arahut02a(B, r):
    p = RUIN02
    m, h = slab_ruin(B, r, p, {
        "W": [(0, 1.0), (0.5, 0.6), (1, 0.4)],
        "N": [(0, 0.6), (0.3, 0.3), (0.5, 0.7), (0.75, 0.3), (1, 0.5)],
        "E": [(0, 0.4), (0.5, 0.7), (1, 0.5)],
    }, heap_top=1.8, seed=31, n_pieces=190, front_frame=(0.62, 0.95))
    # the chimney at the top left, col 25
    x, y = px(r, 25, 12, 2.2)
    broken_chimney(B, m, x, y, 0.0, 2.1, 0.95, 0.95)
    kit.leaning(B, m["timber"], r, [(106, 40, 1.2, 124, 10, 2.4)], w=0.17)
