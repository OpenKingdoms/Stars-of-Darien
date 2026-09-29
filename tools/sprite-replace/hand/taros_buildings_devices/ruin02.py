"""Tarbuild02a and Tarbuild02b: the spiked hall damaged and fallen in. The
first keeps its roof, dented by two shallow craters; the second has lost
the roof over its right two-thirds, where tan rubble is heaped under
tilted roof slabs and spills out of the broken end."""
import math
import random

from mathutils import Matrix

import kit as K
import spiked as S
from spiked import HALL, SPIRES

TAN = (160, 138, 106)
TAN_GAP = (40, 32, 26)
TAN_LIGHT = (200, 180, 146)


def tan_rubble():
    return K.texmat("sp_tan", lambda: K.tex_boulders(TAN, TAN_GAP, TAN_LIGHT, n=256, count=200, seed=56, var=0.35),
                    rough=0.95)


def tan_stones():
    return [K.mat("sp_tan_a", (168, 146, 114), rough=0.9), K.mat("sp_tan_b", (132, 112, 88), rough=0.9),
            K.mat("sp_tan_c", (150, 118, 108), rough=0.9), S.beam()]


def slabs(pieces, surf, seed=1, mt=None):
    """Roof panels lying broken and tilted on the rubble surface surf:
    [(x, y, lift, w, d, yaw, tilt), ...]."""
    out = []
    for i, (x, y, lift, w, d, yaw, tilt) in enumerate(pieces):
        z = surf(x, y) + lift
        ob = K.block(w, d, 0.3, 0, 0, -0.15, 0, mt or S.panel(), "slab")
        K.uv_box(ob, 0.3, i * 0.37, i * 0.21)
        K.place(ob, Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(yaw), 4, "Z")
                @ Matrix.Rotation(math.radians(tilt), 4, "X"))
        out.append(ob)
    return out


def heaps(specs, ground, seed=1, size=(0.3, 0.6), per_area=1.3, power=1.4):
    return K.boulder_heaps(specs, tan_rubble(), tan_stones(), ground, seed=seed, per_area=per_area, size=size,
                           power=power)


def tarbuild02a(name="Tarbuild02a"):
    craters = [(-5.5, -0.45, 2.3, 2.35, 0.75, 3), (2.4, 1.6, 1.9, 1.05, 0.5, 8)]
    parts = S.hall(**HALL, craters=craters, horns={1: "gone"}, seed=5)
    # a small pebble heap against the left fins, a few stones under the first post
    p, _, _ = heaps([(-11.6, 0.7, 0.8, 3.0, 0.9, 0, (-0.3, 0.0)), (-8.0, -4.9, 0.9, 0.5, 0.35),
                     (-6.7, -5.3, 0.45, 0.35, 0.22)], None, seed=31, size=(0.2, 0.4), per_area=2.6)
    return parts + p


def pebbles():
    """The spilled sand and pebbles: smaller and browner than the heaps."""
    return K.texmat("sp_pebbles", lambda: K.tex_boulders((128, 108, 82), (34, 26, 20), (168, 148, 116), n=256,
                                                         count=320, seed=57, var=0.35), rough=0.95)


def slab(x, y, z, w, d, yaw, tilt, roll=0.0, ring=None, mt=None):
    """A big roof slab fallen into the shell, centred at (x, y, z), turned
    yaw, tipped tilt about its length (its back edge up) and roll about
    its depth (its right end down); ring=(u, v) sets the roof's iron ring
    and blade on it there."""
    ob = K.block(w, d, 0.3, 0, 0, -0.15, 0, mt or S.panel(), "slab")
    K.uv_box(ob, 0.3, x * 0.13, y * 0.17)
    parts = [ob]
    if ring is not None:
        parts += S.ring_blade(ring[0], ring[1], 0.15)
    M = (Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(yaw), 4, "Z")
         @ Matrix.Rotation(math.radians(roll), 4, "Y") @ Matrix.Rotation(math.radians(tilt), 4, "X"))
    for p in parts:
        K.place(p, M)
    return parts


def post_stump(x, y, z0, z1, seed):
    """A back post snapped off: square to the front, its top broken flat
    on a slant, a collar part way up."""
    rnd = random.Random(seed)
    g = K.GIRTH * 1.1
    w = 0.46 * g
    dz = rnd.uniform(0.12, 0.3) * rnd.choice((-1, 1))
    base = K.rect(x, y, w, w, 0, z0)
    top = [(px, py, z1 + dz * (px - x) / (w / 2)) for px, py, _ in K.rect(x, y, w * 0.92, w * 0.92, 0, z1)]
    parts = [K.loft([base, top], S.iron(), "post_stump")]
    zc = z0 + (z1 - z0) * 0.62
    parts.append(K.block(w * 1.25, w * 1.25, 0.18, x, y, zc, 0, S.iron(), "post_collar"))
    for p in parts:
        K.uv_box(p, 0.5)
    return parts


# the fallen roof's own dark stone, the gaps between its broken slabs, and
# the sandy gravel darkened to the picture's brown-grey pebbles
ROOF_DARK = (35, 32, 30)


def roof_dark():
    return K.texmat("sp_roof_dark", lambda: K.tex_mottle(ROOF_DARK, (16, 15, 14), (68, 62, 58), n=128, seed=58),
                    rough=0.9)


def roof_gaps():
    return K.mat("sp_roof_gap", (11, 10, 9), rough=1.0)


def gravel():
    return K.texmat("sp_gravel", lambda: K.tex_boulders((104, 90, 69), (16, 13, 11), (132, 118, 95), n=256,
                                                        count=200, seed=56, var=0.35), rough=0.95)


def gravel_stones():
    return [K.mat("sp_gravel_a", (110, 95, 74), rough=0.9), K.mat("sp_gravel_b", (86, 72, 58), rough=0.9),
            K.mat("sp_gravel_c", (98, 77, 70), rough=0.9), S.beam()]


def roof_jumble(region, surf, box, avoid, mt, seed=1, cell=(1.1, 1.0)):
    """Broken roof slabs heaped over one another, one to a jittered cell of
    box where region holds and no named slab (avoid) lies."""
    rnd = random.Random(seed)
    x0, x1, y0, y1 = box
    nx, ny = max(1, int((x1 - x0) / cell[0])), max(1, int((y1 - y0) / cell[1]))
    out = []
    for i in range(nx):
        for j in range(ny):
            x = x0 + (i + rnd.uniform(0.2, 0.8)) * (x1 - x0) / nx
            y = y0 + (j + rnd.uniform(0.2, 0.8)) * (y1 - y0) / ny
            if not region(x, y) or any(ax0 < x < ax1 and ay0 < y < ay1 for ax0, ax1, ay0, ay1 in avoid):
                continue
            w, d, t = rnd.uniform(1.1, 1.9), rnd.uniform(0.8, 1.4), rnd.uniform(0.2, 0.3)
            ob = K.block(w, d, t, 0, 0, -t / 2, 0, mt, "roof_slab")
            K.uv_box(ob, 0.3, rnd.random(), rnd.random())
            M = (Matrix.Translation((x, y, surf(x, y) + rnd.uniform(0.05, 0.45)))
                 @ Matrix.Rotation(math.radians(rnd.uniform(-35, 35)), 4, "Z")
                 @ Matrix.Rotation(math.radians(rnd.uniform(-16, 16)), 4, "Y")
                 @ Matrix.Rotation(math.radians(rnd.choice((-1, 1)) * rnd.uniform(15, 38)), 4, "X"))
            K.place(ob, M)
            out.append(ob)
    return out


def tarbuild02b(name="Tarbuild02b"):
    cx, cy, W, D, H = HALL["cx"], HALL["cy"], HALL["W"], HALL["D"], HALL["H"]
    x0, x1, y0, y1 = cx - W / 2, cx + W / 2, cy - D / 2, cy + D / 2
    zr = H - 0.3
    # the whole roof has fallen in; the back wall is torn along its top
    holes = [[(x0 + 0.3, y0 + 0.3), (x1 - 0.3, y0 + 0.3), (x1 - 0.3, y1 - 0.3), (x0 + 0.3, y1 - 0.3)]]
    spires = {0: -1.2, 2: "gone", 3: "gone", 4: "gone", 5: "gone", 6: "gone"}
    fins = {(1, i): 0 for i in range(4)}
    parts = S.hall(**HALL, holes=holes, spires=spires, horns={1: "gone", 2: "gone"}, fins=fins, seed=6, walls="fl",
                   door_prop=False, rings=False, ragged={"b": (0.72, 1.0)})
    # the back posts, snapped flat and reaching down into the torn wall
    for i, top in ((2, 2.3), (3, 2.1), (4, 2.4), (5, 2.2), (6, 2.3)):
        parts += post_stump(SPIRES[i], y1 - 0.35, zr - 1.4, zr + 1.4 + top * K.RISE, seed=70 + i)

    # the left two-thirds are the fallen roof's dark slabs; sandy gravel
    # shows only in a strip behind the ring slab and over the right third
    def strip(x, y):
        return -0.9 < x < 3.1 and 0.2 < y < 1.35

    def dark(x, y):
        return x < 2.4 and not strip(x, y)

    def fill(x, y):
        # packed in the shell near the old roof line, highest against the
        # back wall, heaped up toward the back wall's height at the left end
        f = zr - 0.55 + 0.3 * max(0.0, min(1.0, (y - (y1 - 2.4)) / 1.6))
        f += 0.4 * max(0.0, min(1.0, (-2.0 - x) / 4.5))
        return min(zr - 0.15, f + 0.15 * math.sin(1.7 * x + 0.6) * math.cos(1.3 * y + 0.2))

    fx0, fx1, fy0, fy1 = x0 + 0.3, x1 - 0.3, y0 + 0.3, y1 - 0.3
    body = K.heightfield(fx0, fx1, fy0, fy1, 21, 7, fill, gravel(), "rubble_fill")
    body.data.materials.append(roof_gaps())
    for pg in body.data.polygons:
        if dark(pg.center.x, pg.center.y):
            pg.material_index = 1
    K.uv_box(body, 0.45)
    parts.append(body)
    p, surf, ins = K.boulder_heaps([(1.2, 0.75, 1.9, 0.5, 0.3), (4.1, 0.0, 1.6, 2.6, 1.0, 10),
                                    (6.1, 0.3, 2.3, 2.6, 1.0), (5.1, -2.3, 2.1, 1.0, 0.6)], gravel(), gravel_stones(),
                                   fill, seed=41, per_area=0.75, size=(0.3, 0.6), power=1.4)
    parts += p

    def talus(x, y):
        # the spill down the left end over the stepped fins, reaching the
        # ground a little past their feet
        d = max(0.0, x0 - x)
        e = max(0.0, abs(y - 0.3) - 2.9)
        return max(0.0, 3.7 * (1 - d / 4.3) - 1.6 * e)

    p, _, _ = K.boulder_heaps([(x0 - 1.5, 0.5, 3.0, 4.7, 0.45, 0, (0.3, 0.2))], pebbles(), tan_stones(), talus,
                              seed=51, per_area=1.0, size=(0.3, 0.55))
    parts += p
    # out of the broken right end, and a low spill along the front
    p, _, _ = K.boulder_heaps([(9.9, -0.5, 1.7, 3.4, 3.2, 0, (0.5, 0.0))], gravel(), gravel_stones(), None, seed=61,
                              per_area=1.3, size=(0.3, 0.6), power=1.4)
    parts += p
    p, _, _ = K.boulder_heaps([(-2.9, y0 - 0.5, 5.0, 1.1, 0.55, 0, (0.0, -0.3)), (4.6, y0 - 0.4, 2.2, 0.8, 0.4)],
                              pebbles(), tan_stones(), None, seed=62, per_area=1.0, size=(0.25, 0.45))
    parts += p
    # the fallen roof: big plain slabs tipping into the shell at the left,
    # the long middle slab still carrying its ring and blade, a jumble of
    # broken slabs heaped round them with black gaps between; red-veined
    # flagstones only on the gravel to the right
    dk = roof_dark()
    named = [((-5.3, 1.3, 4.2, 2.6, -6, 28, -14), None), ((-4.9, -1.35, 4.6, 2.5, 6, -14, 16), (-0.2, 0.25)),
             ((-7.5, 0.1, 1.3, 5.3, 2, 4, -38), None), ((-0.2, -0.85, 5.6, 2.3, -3, 7, -4), (0.9, 0.2))]
    for (x, y, w, d, yaw, tilt, roll), ring in named:
        z = zr - 0.05 if ring == (0.9, 0.2) else fill(x, y) + 0.3
        parts += slab(x, y, z, w, d, yaw, tilt, roll, ring=ring, mt=dk)
    avoid = [(-6.0, -4.2, -1.8, -0.4), (-2.8, 2.6, -1.9, 0.2)]
    parts += roof_jumble(dark, fill, (x0 + 0.5, 2.3, y0 + 0.5, y1 - 0.4), avoid, dk, seed=43)
    parts += slabs([(1.2, -2.5, 0.25, 1.5, 1.2, -25, -24)], fill, mt=dk)
    parts += slabs([(3.6, -1.0, 0.3, 2.1, 1.6, 12, 22), (5.8, 0.8, 0.3, 2.0, 1.5, -18, -28),
                    (7.3, -1.9, 0.25, 1.9, 1.5, 30, 32), (2.0, 1.4, 0.3, 1.8, 1.4, 40, 20),
                    (4.6, -2.6, 0.25, 1.8, 1.4, -8, -35), (7.6, 1.2, 0.3, 1.6, 1.3, 70, 30),
                    (8.7, -0.3, 0.25, 1.4, 1.6, 15, 28)], surf)
    return parts
