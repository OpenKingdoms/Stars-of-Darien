"""Shared runner for the bespoke Taros models, run inside Blender.

Each bespoke/<Name>.py builds one model against its own sprite and calls
run(). run() exports and renders it like build.py does, after listing any
piece that is not joined to the ground through other pieces.
"""
import os
import sys
import time

from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
FAM = os.path.dirname(HERE)
if FAM not in sys.path:
    sys.path.insert(0, FAM)
import kit as K  # noqa: E402


def _tree(ob):
    M = ob.matrix_world
    vs = [M @ v.co for v in ob.data.vertices]
    return vs, BVHTree.FromPolygons(vs, [tuple(p.vertices) for p in ob.data.polygons])


def _gap(va, tb, cap):
    best = cap
    for v in va:
        hit = tb.find_nearest(v, best)
        if hit[0] is not None:
            best = min(best, hit[3])
            if best < 1e-4:
                break
    return best


def floaters(parts, tol=0.03, ground=0.06):
    """The pieces not joined to the ground through touching pieces, with
    each one's gap to the nearest other piece."""
    info = []
    for p in parts:
        if not p.data.polygons:
            info.append(None)
            continue
        vs, t = _tree(p)
        lo = Vector([min(v[i] for v in vs) for i in range(3)])
        hi = Vector([max(v[i] for v in vs) for i in range(3)])
        info.append((vs, t, lo, hi))
    n = len(parts)
    near = [[] for _ in range(n)]
    for a in range(n):
        if info[a] is None:
            continue
        va, ta, la, ha = info[a]
        for b in range(a + 1, n):
            if info[b] is None:
                continue
            vb, tb, lb, hb = info[b]
            if any(la[i] > hb[i] + tol or lb[i] > ha[i] + tol for i in range(3)):
                continue
            d = 0.0 if ta.overlap(tb) else min(_gap(va, tb, 1.0), _gap(vb, ta, 1.0))
            near[a].append((b, d))
            near[b].append((a, d))
    seen = set(i for i in range(n) if info[i] is not None and info[i][2].z <= ground)
    todo = list(seen)
    while todo:
        a = todo.pop()
        for b, d in near[a]:
            if d <= tol and b not in seen:
                seen.add(b)
                todo.append(b)
    out = []
    for i in range(n):
        if info[i] is None or i in seen:
            continue
        g = min([d for _, d in near[i]] or [1.0])
        lo, hi = info[i][2], info[i][3]
        out.append((parts[i].name, g, tuple(round(c, 2) for c in lo), tuple(round(c, 2) for c in hi)))
    return out


def run(name, build, tone=0.8):
    t = time.time()
    K.reset()
    K.TONE = tone
    parts = build()
    for nm, g, lo, hi in floaters(parts):
        print("HANDKIT_FLOAT %s %s gap %.3f box %s %s" % (name, nm, g, lo, hi))
    ob, tris = K.finish(name, parts, getattr(parts, "painted", ()))
    print("HANDKIT_DONE %s tris=%d %.1fs" % (name, tris, time.time() - t))
    return ob


def main(name, build, tone=0.8):
    """Runs the model when Blender runs its script."""
    run(name, build, tone)
