"""Same-facing coplanar overlaps in built models, which z-fight in Unity.

    blender -b --factory-startup --python coplanar.py -- name [name ...] [--mats a,b]

For each model: the area, in square cells, where two faces of different
materials (or of one material in --self mode) lie in one plane facing the
same way, by material pair, largest first.
"""
import sys
from collections import defaultdict

import bmesh
import bpy
import numpy as np
from mathutils import Vector

OUT = "D:/OKReplace/hand/aramon_buildings/models"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
same = "--self" in argv
names = [a for a in argv if not a.startswith("--")]


def faces_of(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath="%s/%s.glb" % (OUT, name))
    F = []
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH":
            continue
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.transform(ob.matrix_world)
        bm.normal_update()
        mats = [m.name if m else "?" for m in ob.data.materials]
        for f in bm.faces:
            if f.calc_area() < 1e-5:
                continue
            n = f.normal.copy()
            if n.z < -0.99 and abs(f.verts[0].co.z) < 0.05:
                continue  # a bottom on the ground, never seen
            F.append((mats[f.material_index] if mats else ob.name, n, n.dot(f.verts[0].co),
                      [v.co.copy() for v in f.verts], f.calc_area()))
        bm.free()
    return F


def inside(pt, poly, u, v):
    x, y = pt.dot(u), pt.dot(v)
    P = [(p.dot(u), p.dot(v)) for p in poly]
    c = False
    for i in range(len(P)):
        (ax, ay), (bx, by) = P[i], P[i - 1]
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
            c = not c
    return c


def overlap(fa, fb, k=5):
    """Rough shared area of two coplanar faces: sample a over a grid."""
    n = fa[1]
    u = (fa[3][1] - fa[3][0]).normalized()
    v = n.cross(u)
    pa = fa[3]
    xs, ys = [p.dot(u) for p in pa], [p.dot(v) for p in pa]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    hit = tot = 0
    for i in range(k):
        for j in range(k):
            pt = u * (x0 + (x1 - x0) * (i + 0.5) / k) + v * (y0 + (y1 - y0) * (j + 0.5) / k) + n * fa[2]
            if inside(pt, pa, u, v):
                tot += 1
                if inside(pt, fb[3], u, v):
                    hit += 1
    return fa[4] * hit / max(1, tot)


for name in names:
    F = faces_of(name)
    key = defaultdict(list)
    for f in F:
        n = f[1]
        key[(round(n.x, 3), round(n.y, 3), round(n.z, 3), round(f[2], 3))].append(f)
    pairs = defaultdict(float)
    for fs in key.values():
        if len(fs) < 2:
            continue
        for i in range(len(fs)):
            for j in range(i + 1, len(fs)):
                a, b = fs[i], fs[j]
                if (a[0] == b[0]) != same:
                    continue
                if abs(a[2] - b[2]) > 2e-4 or a[1].dot(b[1]) < 0.9999:
                    continue
                s = overlap(a, b)
                if s > 1e-4:
                    pairs[tuple(sorted((a[0], b[0])))] += s
    tot = sum(pairs.values())
    print("COPLANAR %s total %.3f" % (name, tot))
    for (a, b), s in sorted(pairs.items(), key=lambda kv: -kv[1])[:8]:
        print("   %.3f  %s | %s" % (s, a, b))
