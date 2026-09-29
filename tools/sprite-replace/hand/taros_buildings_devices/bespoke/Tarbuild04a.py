"""Tarbuild04a, the Taros prison fallen in along its back-left diagonal.

    blender -b --factory-startup --python bespoke/Tarbuild04a.py

The round-4 ruin, built here from the prison's parts, with everything it
kept standing seated on stone: each kept tusk keeps a stub of corner
parapet to stand in, a merlon whose parapet was cut from under it drops onto
what is below (or goes, if that is far), and a shuttered window whose
wall was broken away below its top is left out. The rubble heaps are a
little thinner, for the building budget.
"""
import math
import os
import random
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402
from common import K  # noqa: E402
import prison as PR  # noqa: E402
from prison import KEEP_H, T1_H, T2_H, _break, barrel, beams, prison, ruin_rubble, rubble_stones, tusk, wood  # noqa
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

NAME = "Tarbuild04a"
WALLS = ("tower1", "keep", "shoulder", "wing", "left_wall", "gatehouse", "tower2", "terrace", "chimney", "lodge")
# the kept tusks: the tall tower's back-right one and the front tower's back-right one,
# with the corner column of its tower's broken top each stands on (the
# ruined_block cell at that corner)
TUSKS = [(-3.9, 8.5, 0.5, 0.4, T1_H, (-4.64, -3.7, 7.61, 8.7)), (6.0, -3.0, 0.6, 0.4, T2_H, (5.13, 6.1, -3.79, -2.8))]
# the shuttered windows prison() sets: (x, wall face y, centre z)
WINDOWS = [(-8.1, 3.25, T1_H - 1.9), (-5.9, 3.25, T1_H - 1.9)] + [(x, 3.1, 2.5) for x in (-6.3, -4.6, -2.6, -0.6)]
THIN = 0.7  # the heaps' boulder counts, against round 4's
RINGS = -1  # and one ring fewer in each heap's mesh


def _bvh(parts):
    vs, fs = [], []
    for p in parts:
        k = len(vs)
        vs += [v.co.copy() for v in p.data.vertices]
        fs += [tuple(k + i for i in pg.vertices) for pg in p.data.polygons]
    return BVHTree.FromPolygons(vs, fs)


def _box(p):
    xs = [v.co for v in p.data.vertices]
    return Vector([min(v[i] for v in xs) for i in range(3)]), Vector([max(v[i] for v in xs) for i in range(3)])


def top_at(bvh, x, y, z=30.0):
    hit = bvh.ray_cast(Vector((x, y, z)), Vector((0, 0, -1)))
    return hit[0].z if hit[0] is not None else 0.0


def seat_tusks(walls):
    """Each kept tusk where the intact prison sets it, its root in a stub of
    corner parapet left standing on the corner's own column of masonry."""
    out = []
    for x, y, dx, dy, h, (x0, x1, y0, y1) in TUSKS:
        top = min(top_at(walls, x0 + (x1 - x0) * u, y0 + (y1 - y0) * v) for u in (0.1, 0.5, 0.9) for v in (0.1, 0.5, 0.9))
        pier = K.block(x1 - x0, y1 - y0, h + 0.3 - top + 0.05, (x0 + x1) / 2, (y0 + y1) / 2, top - 0.05, 0, PR.wall(),
                       "tusk_pier")
        K.two_tone(pier, PR.flag())
        K.uv_box(pier, 1 / 2.5)
        t = tusk(x, y, h + 0.5, dx, dy)
        lo, _ = _box(t)
        print("HANDKIT_TUSK %s at (%.1f, %.1f) pier %.2f to %.2f, tusk base %.2f" % (NAME, x, y, top, h + 0.3, lo.z))
        out += [pier, t]
    return out


def drop_windows(parts, walls):
    """Leaves out each window whose wall no longer stands behind its whole frame."""
    gone = set()
    for wx, yf, wz in WINDOWS:
        need = wz + 0.95 / 2 + 0.05
        tops = [top_at(walls, wx + dx, yf + 0.12) for dx in (-0.4, 0.0, 0.4)]
        if min(tops) >= need:
            continue
        print("HANDKIT_WINDOW %s at x %.1f z %.1f left out, wall top %.2f under %.2f" % (NAME, wx, wz, min(tops), need))
        for i, p in enumerate(parts):
            if p.name.startswith("win_"):
                lo, hi = _box(p)
                c = (lo + hi) / 2
                if abs(c.x - wx) < 0.9 and abs(c.y - yf) < 0.6 and abs(c.z - wz) < 0.9:
                    gone.add(i)
    return [p for i, p in enumerate(parts) if i not in gone], [parts[i] for i in gone]


def seat_merlons(parts):
    """A merlon left over a cut parapet drops onto what is under it, or
    goes if that is far down."""
    rest = [p for p in parts if not p.name.startswith("merlon")]
    bvh = _bvh(rest)
    keep, gone = [], []
    for p in parts:
        if not p.name.startswith("merlon"):
            keep.append(p)
            continue
        lo, hi = _box(p)
        c = (lo + hi) / 2
        gaps = []
        for fx in (-0.35, 0.0, 0.35):
            for fy in (-0.35, 0.0, 0.35):
                q = Vector((c.x + (hi.x - lo.x) * fx, c.y + (hi.y - lo.y) * fy, lo.z + 0.01))
                h = bvh.ray_cast(q, Vector((0, 0, -1)), 3.0)
                gaps.append(h[3] - 0.01 if h[0] is not None else 3.0)
        g = min(gaps)
        if g <= 0.02:
            keep.append(p)
        elif max(gaps) < 0.5:
            K.move(p, (0, 0, -(max(gaps) + 0.03)))
            print("HANDKIT_MERLON %s at (%.1f, %.1f, %.1f) dropped %.2f" % (NAME, c.x, c.y, lo.z, max(gaps) + 0.03))
            keep.append(p)
        else:
            print("HANDKIT_MERLON %s at (%.1f, %.1f, %.1f) left out, %.2f over" % (NAME, c.x, c.y, lo.z, g))
            gone.append(p)
    return keep, gone


def settle(parts):
    """The plank by the outside barrels lies on the ground, and a timber
    left a hair over the rubble beds into it."""
    for i, p in enumerate(parts):
        if p.name.startswith("plank"):
            bpy_remove(p, p.data)
            parts[i] = K.board((9.0, -4.8, 0.05), (9.6, -6.4, 0.05), 0.25, 0.08, wood(), "plank")
            K.uv_box(parts[i], 0.5)
    for nm, g, lo, hi in C.floaters(parts):
        p = next(q for q in parts if q.name == nm)
        if nm.startswith("beam") and g < 0.3:
            K.move(p, (0, 0, -(g + 0.05)))
            print("HANDKIT_SETTLE %s %s down %.2f" % (NAME, nm, g + 0.05))


def build():
    def ragged(seed, cut):
        rnd = random.Random(seed)
        return lambda e, f: not (e in cut and cut[e][0] < f < cut[e][1]) and rnd.random() > 0.15

    def parapet_gone(seed):
        rnd = random.Random(seed)
        return lambda e, f: e in "fr" and rnd.random() > 0.6

    def back_left(x, y):
        return (y > 7.3 and x < -5.8) or (x > -4.6 and y > 7.7)

    cuts = {"t1": _break(-4.3, 3.9, 3.0, 5.6, seed=1, keep=back_left),
            "keep": _break(0.5, 0.8, 4.0, 3.3, seed=2),
            "shoulder": _break(-1.2, 4.4, 2.2, 2.9, seed=3),
            "wing": _break(-0.6, 3.5, 1.6, 2.2, seed=4),
            "t2": _break(0.9, -3.3, 2.0, 3.8, seed=5)}
    damage = {"gone": ("lodge_roof", "yard_props"), "t1_tusks": (), "t2_tusks": (), "cuts": cuts,
              "crenels": {"t1": ragged(1, {"r": (0.0, 1.0), "f": (0.0, 1.0)}),
                          "keep": ragged(2, {"l": (0.0, 1.0), "b": (0.5, 1.0)}),
                          "wing": ragged(3, {"f": (0.5, 1.0)}), "shoulder": ragged(4, {"b": (0.0, 1.0)}),
                          "t2": parapet_gone(5), "terrace": ragged(6, {})}}
    parts = prison(damage)
    walls = _bvh([p for p in parts if p.name.startswith(WALLS)])
    parts, dropped = drop_windows(parts, walls)
    parts += seat_tusks(walls)
    blocks = [(-10.3, -3.7, 3.25, 8.7, T1_H, "t1"), (0.3, 5.2, -2.8, 7.2, KEEP_H, "keep"),
              (-3.7, 0.3, 3.6, 8.3, 7.0, "shoulder"), (-7.1, 0.3, 3.1, 4.2, 5.0, "wing"),
              (0.3, 6.1, -7.75, -2.8, T2_H, "t2"), (-10.3, -7.6, -5.2, 3.1, 5.0, None),
              (-7.1, 0.3, -7.75, -6.2, 3.6, None), (5.2, 9.5, -4.5, 8.3, 4.0, None)]

    def ground(x, y):
        z = 0.0
        for x0, x1, y0, y1, h, nm in blocks:
            if x0 <= x <= x1 and y0 <= y <= y1:
                z = max(z, h - (cuts[nm](x, y) if nm else 0.0) - 0.2)
        return z

    slope = [(-5.4, 4.9, 1.9, 1.8, 0.8, 30), (-3.5, 5.0, 1.5, 1.7, 1.6, 0, (-0.5, 0.0)),
             (-1.4, 4.3, 1.9, 1.5, 1.4, 20), (-0.9, 2.1, 1.9, 1.7, 2.9, 0, (0.3, 0.6)),
             (1.3, 1.0, 1.5, 2.3, 1.0, 10), (-0.7, -0.6, 1.3, 2.4, 2.0, 0, (0.7, 0.0)),
             (1.3, -3.4, 1.3, 1.0, 0.7, 0)]
    rub, stones = ruin_rubble(), rubble_stones()
    p, fn0, ins0 = K.boulder_heaps(slope, rub, stones, ground, seed=80, per_area=0.45 * THIN, size=(0.4, 0.8), rings=4 + RINGS,
                                   ring_seg=11, power=1.1)
    parts += p

    def over(x, y):
        return max(ground(x + dx, y + dy) for dx in (-0.6, 0.0, 0.6) for dy in (-0.6, 0.0, 0.6))

    top = [(-8.6, 7.45, 1.35, 0.95, 0.8), (-6.0, 7.45, 1.5, 0.95, 0.9), (-8.6, 5.25, 1.35, 1.35, 1.0),
           (-5.8, 5.2, 1.7, 1.55, 1.3, 0, (0.3, -0.3)), (-7.2, 6.3, 1.6, 1.3, 1.1)]
    p, fn1, ins1 = K.boulder_heaps(top, rub, stones, over, seed=84, per_area=0.8 * THIN, size=(0.5, 0.9), rings=5 + RINGS,
                                   ring_seg=12, power=1.1)
    parts += p
    yard = [(-1.7, 1.25, 2.2, 1.45, 2.6, 0, (0.8, 0.3)), (-1.9, -1.0, 2.0, 1.45, 1.8, 0, (0.8, 0.0)),
            (-3.2, 2.2, 1.1, 0.85, 0.7)]
    p, fn2, ins2 = K.boulder_heaps(yard, rub, stones, ground, seed=88, per_area=0.6 * THIN, size=(0.45, 0.85), rings=4 + RINGS,
                                   ring_seg=12, power=1.1)
    parts += p

    def fn(x, y):
        return max(fn0(x, y) if ins0(x, y) else 0.0, fn1(x, y) if ins1(x, y) else 0.0,
                   fn2(x, y) if ins2(x, y) else 0.0)

    def ins(x, y):
        return ins0(x, y) or ins1(x, y) or ins2(x, y)

    lying = [(fn, ins, (-7.3, 2.8, -4.5, 6.8))]
    tm = K.mat("pr_timber", (62, 46, 32), rough=0.85)
    for (ax, ay), (bx, by), up in (((-7.6, 6.1), (-6.3, 4.5), 1.3), ((-8.9, 6.9), (-9.8, 5.3), 1.0),
                                   ((-5.9, 7.3), (-4.5, 8.0), 1.1), ((-6.8, 5.0), (-7.9, 3.8), 0.9)):
        parts.append(K.board((ax, ay, fn(ax, ay) - 0.3), (bx, by, fn(ax, ay) + up), 0.3, 0.22, tm, "beam"))
    for (ax, ay), (bx, by) in (((-3.7, 0.5), (-0.9, 1.7)), ((-3.2, -1.7), (-1.0, 0.3)), ((-2.3, 2.9), (-0.5, 2.0))):
        parts.append(K.board((ax, ay, fn(ax, ay) - 0.05), (bx, by, fn(bx, by) - 0.05), 0.3, 0.22, tm, "beam"))
    crate = K.block(0.9, 0.8, 0.7, 0, 0, -0.35, 0, wood(), "crate")
    K.place(crate, Matrix.Translation((-3.3, 2.3, fn(-3.3, 2.3) - 0.1)) @ Matrix.Rotation(math.radians(20), 4, "Z")
            @ Matrix.Rotation(math.radians(18), 4, "Y") @ Matrix.Rotation(math.radians(-10), 4, "X"))
    parts.append(crate)
    parts += barrel(-2.9, 1.2, fn(-2.9, 1.2) - 0.5, lying=True, yaw=35)
    for (x, y, yaw) in ((-5.6, 2.2, 30), (-5.3, 1.2, 20), (-4.4, 0.4, 70)):
        parts += barrel(x, y, lying=True, yaw=yaw)
    p, _, _ = K.boulder_heaps([(-10.8, -1.2, 0.6, 1.6, 0.5, 0, (-0.3, 0.0)), (-10.6, -8.1, 0.8, 0.5, 0.4, 25),
                               (2.3, -8.3, 1.2, 0.6, 0.5, 0, (0.0, -0.3)), (9.9, 1.5, 0.6, 1.4, 0.45, 0, (0.3, 0.0)),
                               (-4.2, -8.3, 1.0, 0.5, 0.4)], rub, stones, None, seed=95, per_area=0.5,
                              size=(0.3, 0.55), rings=3, ring_seg=12)
    parts += p
    parts += beams(lying, 18, seed=91)
    parts, gone = seat_merlons(parts)
    settle(parts)
    for p in dropped + gone:
        me = p.data
        bpy_remove(p, me)
    tally = Counter()
    for p in parts:
        tally[p.name.split(".")[0]] += sum(len(pg.vertices) - 2 for pg in p.data.polygons)
    print("HANDKIT_TRIS_BY", NAME, tally.most_common(8))
    return parts


def bpy_remove(ob, me):
    import bpy
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.meshes.remove(me)


if __name__ == "__main__":
    C.main(NAME, build)
