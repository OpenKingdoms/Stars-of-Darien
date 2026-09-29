"""Tarbuild01a and Tarbuild01b: the spiral tower broken down, its top
heaped with boulders, and separate boulder heaps lying on the platform
and spilling over its rim."""
import math
import random

from mathutils import Matrix

import kit as K
import zigg as Z
from zigg import AXIS, HEIGHTS, PLAT, RADII, WIDTHS

# the ruins are charred: the sprites' heaps sit near (24, 21, 18)
BOULDER = (33, 30, 26)
BOULDER_GAP = (4, 4, 3)
BOULDER_LIGHT = (52, 47, 40)


def boulder_mat():
    return K.texmat("zg_boulders", lambda: K.tex_boulders(BOULDER, BOULDER_GAP, BOULDER_LIGHT, n=256, count=70,
                                                           seed=45), rough=0.95)


def heavy_mat():
    """The destroyed tower's heap: charcoal lumps with near black gaps."""
    return K.texmat("zg_boulders_heavy", lambda: K.tex_boulders((27, 25, 22), (3, 3, 2), (42, 38, 32), n=256,
                                                                 count=34, seed=47, var=0.38), rough=0.95)


def boulder_stones(k=1.0):
    def c(v):
        return tuple(int(x * k) for x in v)
    return [K.mat("zg_boulder_a", c((42, 38, 33)), rough=0.9), K.mat("zg_boulder_b", c((28, 26, 22)), rough=0.9),
            K.mat("zg_boulder_c", c((52, 47, 41)), rough=0.9),
            K.texmat("zg_boulder_d", lambda: K.tex_mottle(c((46, 42, 36)), c((18, 16, 14)), n=64, seed=46),
                     rough=0.9)]


def char_wall():
    """The broken tower's walls, blackened by the fall."""
    return K.texmat("zg_wall_char", lambda: K.tex_soot((32, 31, 27), (11, 11, 9), n=128, seed=48), rough=0.95)


def soot_mats():
    return [K.mat("zg_soot_core", (12, 11, 10), rough=1.0),
            K.texmat("zg_soot", lambda: K.tex_soot((40, 37, 32), (14, 13, 11), n=128, seed=49), rough=1.0)]


def arc_slab(x0, y0, r0, r1, a0, a1, h, mt, top=None, seg=6, name="arc_slab"):
    """A curved block of the tower wall: an annulus sector r0..r1 between
    angles a0..a1 (degrees), h tall."""
    ring = [(x0 + r1 * math.cos(math.radians(a0 + (a1 - a0) * i / seg)),
             y0 + r1 * math.sin(math.radians(a0 + (a1 - a0) * i / seg))) for i in range(seg + 1)]
    ring += [(x0 + r0 * math.cos(math.radians(a1 - (a1 - a0) * i / seg)),
              y0 + r0 * math.sin(math.radians(a1 - (a1 - a0) * i / seg))) for i in range(seg + 1)]
    ob = K.prism(ring, 0, h, mt, name)
    if top is not None:
        K.two_tone(ob, top)
    K.uv_box(ob, 0.5)
    return ob


def fallen(pieces):
    """Curved wall pieces lying tilted where they fell:
    [(x, y, z, r, span, h, yaw, tilt), ...], z read off the picture."""
    out = []
    for x, y, z, r, span, h, yaw, tilt in pieces:
        z = Z.rise(z)
        ob = arc_slab(0, 0, r - 0.45, r, -span / 2, span / 2, h, char_wall(), top=Z.lip())
        K.place(ob, Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(yaw), 4, "Z")
                @ Matrix.Rotation(math.radians(tilt), 4, "Y") @ Matrix.Translation((-r, 0, -h / 2)))
        out.append(ob)
    return out


def cone(cut):
    """The tower's stepped slopes smoothed to a cone, capped at cut, over
    the platform: the ground a heap on the tower lies on."""
    x0, y0 = AXIS
    poly = K.rect(PLAT["cx"], PLAT["cy"], PLAT["w"], PLAT["d"], PLAT["rz"])
    zb = PLAT["h"]

    def ground(x, y):
        r = math.hypot(x - x0, y - y0)
        if r >= RADII[0]:
            return zb if K.inside_poly(poly, x, y) else 0.0
        for (ra, za), (rb, zb_) in zip(zip(RADII, HEIGHTS), zip(RADII[1:], HEIGHTS[1:])):
            if rb <= r <= ra:
                f = (ra - r) / (ra - rb)
                return min(cut, za + (zb_ - za) * f)
        return min(cut, HEIGHTS[-1])
    return ground


def ruin(name, limit, wall_top, tower_heaps, heaps, pieces, seed=1, cut=12.4, per_area=1.0, size=(0.5, 1.1),
         merlons=1.0, mat=None, horns=None, keep=None, stone_k=1.0,
         scorch=1.6):
    """The platform, keeping its merlons and rim except under a heap (and
    only a share `merlons` of the rest, and those keep(u, v) allows), its
    corner horns cut to `horns`, a scorch under each heap, the tower cut down to
    limit(angle, t) with its walls broken to wall_top(angle), boulder
    heaps on the tower and on the platform. Heights are as read off the
    picture; the sturdy tower's rise() lowers them."""
    x0, y0 = AXIS
    zb = PLAT["h"]
    cut = Z.rise(cut)
    mrnd = random.Random(seed + 5)
    poly = K.rect(PLAT["cx"], PLAT["cy"], PLAT["w"], PLAT["d"], PLAT["rz"])

    def plat_ground(x, y):
        return zb if K.inside_poly(poly, x, y) else 0.0

    rub, stones = mat or boulder_mat(), boulder_stones(stone_k)
    parts, surf, inside = K.boulder_heaps(heaps, rub, stones, plat_ground, seed=seed, per_area=per_area,
                                          size=size)
    t_parts, t_surf, t_inside = K.boulder_heaps(tower_heaps, rub, stones, cone(cut), seed=seed + 50,
                                                per_area=per_area * 1.2, size=size)
    parts += t_parts

    def buried(x, y):
        return inside(x, y) and surf(x, y) > zb + 0.5

    fell = {}

    def broken(x, y):
        k = (round(x, 1), round(y, 1))
        if k not in fell:
            fell[k] = mrnd.random() > merlons
        return buried(x, y) or fell[k]

    parts += Z.platform(**PLAT, broken=broken, seed=seed, rim_gone=lambda x, y: buried(x, y), horns=horns,
                        keep=keep)
    c, s = math.cos(math.radians(PLAT["rz"])), math.sin(math.radians(PLAT["rz"]))
    hw, hd = PLAT["w"] / 2 - 0.3, PLAT["d"] / 2 - 0.3

    def on_plat(x, y):
        dx, dy = x - PLAT["cx"], y - PLAT["cy"]
        u = max(-hw, min(hw, dx * c + dy * s))
        v = max(-hd, min(hd, -dx * s + dy * c))
        return PLAT["cx"] + u * c - v * s, PLAT["cy"] + u * s + v * c

    for i, sp in enumerate(heaps):
        parts += K.soot(sp[0], sp[1], sp[2] * scorch, sp[3] * scorch, plat_ground, soot_mats(), seed=seed + 31 * i,
                        rot=sp[5] if len(sp) > 5 else 0.0, clip=on_plat)

    def keep_step(t, x, y, z):
        a = math.degrees(math.atan2(y - y0, x - x0)) % 360
        return z <= Z.rise(limit(a, t))

    def top(x, y, z):
        a = math.degrees(math.atan2(y - y0, x - x0)) % 360
        return min(z, Z.rise(wall_top(a)))

    parts += Z.spiral(x0, y0, zb, RADII, HEIGHTS, WIDTHS, keep=keep_step, wall_top=top, wall_m=char_wall())
    parts += fallen(pieces)
    return parts


def tarbuild01a(name="Tarbuild01a"):
    def limit(a, t):
        # standing to the fifth level at the back left, a terrace lower at the front right
        return 10.3 + 0.9 * math.cos(math.radians(a - 150)) + K.jagged(a / 360.0, 0.45, 3, freq=3.0)

    def wall_top(a):
        return 11.4 + 0.8 * math.cos(math.radians(a - 150)) + K.jagged(a / 360.0, 0.6, 9, freq=4.0)

    ax, ay = AXIS
    tower_heaps = [(ax + 0.1, ay - 0.2, 2.3, 2.3, 1.2, 0, (0.0, -0.6)),
                   (ax + 0.4, ay - 2.6, 2.0, 1.7, 1.0, 10, (0.0, -0.5)),
                   (ax + 1.2, ay - 4.4, 1.5, 1.1, 0.7, 20)]
    heaps = [(-6.6, 3.9, 1.3, 1.3, 0.9, 20), (-7.9, -2.4, 1.7, 2.5, 1.0, -10, (-0.4, 0.0)),
             (4.7, -3.6, 2.8, 2.1, 1.2, 15), (5.6, 5.4, 1.9, 1.5, 1.0, -20, (0.3, 0.3)),
             (-3.2, -6.9, 1.6, 1.0, 0.8, 5, (0.0, -0.5)), (7.4, 0.2, 1.0, 2.0, 0.8, 0, (0.5, 0.0)),
             (1.4, -6.9, 1.0, 0.7, 0.5, 0)]
    pieces = [(ax - 1.0, ay + 1.2, 10.9, 2.6, 40, 1.0, 60, 25), (ax + 1.4, ay - 0.9, 10.2, 2.4, 45, 0.9, -30, -20),
              (ax + 0.2, ay - 3.2, 8.2, 2.4, 50, 0.9, 110, 15)]
    def keep(u, v):
        # the back edge keeps three at its left end and the left edge two at its back end
        if v > PLAT["d"] / 2 - 0.5:
            return u < -2.0
        if u < -PLAT["w"] / 2 + 0.5:
            return v > 3.5
        return True
    return ruin(name, limit, wall_top, tower_heaps, heaps, pieces, seed=11, cut=10.6, per_area=0.85,
                horns=(0.4, 0, 0, 0), keep=keep, stone_k=0.8, scorch=1.8)


def tarbuild01b(name="Tarbuild01b"):
    def left_front(a):
        return 1.0 if 172 <= a <= 292 else 0.0

    def limit(a, t):
        # the first turn stands, and the second round the left and front
        return 2.2 + 2.6 * left_front(a) + K.jagged(a / 360.0, 0.3, 5, freq=3.0)

    def wall_top(a):
        # broken low all round but for a short stub at the back
        return (2.9 + 2.4 * left_front(a) + 1.6 * math.exp(-((a - 95) / 16.0) ** 2)
                + K.jagged(a / 360.0, 0.35, 6, freq=4.0))

    ax, ay = AXIS
    tower_heaps = [(ax + 0.9, ay + 0.9, 3.9, 3.6, 2.6, 20, (0.1, 0.2))]
    heaps = [(5.4, -1.6, 2.2, 4.0, 1.8, 0, (0.4, 0.0)), (5.2, 5.0, 2.4, 1.9, 1.5, 10, (0.2, 0.3)),
             (-8.2, -1.8, 1.2, 2.8, 1.0, 0, (-0.4, 0.0)), (-0.6, -6.9, 2.2, 1.1, 1.0, 5, (0.0, -0.5)),
             (3.2, -5.6, 1.9, 1.6, 1.3, 20), (-6.2, 5.6, 1.4, 1.1, 0.8, 0), (-1.8, 6.0, 2.2, 1.0, 1.1, 0),
             (7.8, 2.2, 0.9, 1.8, 0.8, 0, (0.5, 0.0))]
    pieces = [(ax + 0.6, ay + 3.3, 3.2, 3.8, 55, 1.2, 20, 35), (ax + 2.8, ay + 0.9, 3.6, 3.0, 50, 1.1, -60, -30),
              (ax + 2.4, ay - 1.6, 3.4, 2.6, 45, 1.0, -120, 25), (ax - 0.4, ay - 1.2, 3.4, 3.4, 45, 1.1, 150, -20),
              (ax - 0.6, ay + 1.4, 4.2, 2.4, 55, 0.9, 60, 15), (ax + 1.8, ay + 2.2, 4.0, 2.2, 40, 0.9, -10, -25)]
    return ruin(name, limit, wall_top, tower_heaps, heaps, pieces, seed=21, cut=2.6, per_area=1.25, size=(0.55, 1.3),
                merlons=0.3, mat=heavy_mat(), horns=(0, 0, 0, 0), stone_k=0.6, scorch=2.1)
