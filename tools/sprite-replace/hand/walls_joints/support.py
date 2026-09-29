"""Finds loose parts that touch nothing, run in Blender on a model's parts.

An island (faces joined through shared corners) is held up when it
reaches the ground or touches an island that is held up: their faces
cross, or a corner of one lies within tol of the other's surface.
"""
import bmesh
from mathutils.bvhtree import BVHTree


class Island:
    def __init__(self, ob, faces, verts, polys):
        self.ob, self.faces = ob, faces
        self.verts, self.polys = verts, polys
        self.lo = [min(v[i] for v in verts) for i in range(3)]
        self.hi = [max(v[i] for v in verts) for i in range(3)]
        self.tree = BVHTree.FromPolygons(verts, polys, all_triangles=False)


def islands(objs):
    out = []
    for ob in objs:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.transform(ob.matrix_world)
        bm.faces.ensure_lookup_table()
        bm.verts.ensure_lookup_table()
        seen = set()
        for f in bm.faces:
            if f.index in seen:
                continue
            seen.add(f.index)
            stack, comp = [f], []
            while stack:
                g = stack.pop()
                comp.append(g)
                for v in g.verts:
                    for h in v.link_faces:
                        if h.index not in seen:
                            seen.add(h.index)
                            stack.append(h)
            vid = {}
            verts, polys = [], []
            for g in comp:
                poly = []
                for v in g.verts:
                    if v.index not in vid:
                        vid[v.index] = len(verts)
                        verts.append(tuple(v.co))
                    poly.append(vid[v.index])
                polys.append(poly)
            out.append(Island(ob, [g.index for g in comp], verts, polys))
        bm.free()
    return out


def _near(a, b, tol):
    for i in range(3):
        if a.lo[i] > b.hi[i] + tol or b.lo[i] > a.hi[i] + tol:
            return False
    return True


def _touch(a, b, tol):
    if a.tree.overlap(b.tree):
        return True
    for v in a.verts:
        if b.tree.find_nearest(v, tol)[0] is not None:
            return True
    for v in b.verts:
        if a.tree.find_nearest(v, tol)[0] is not None:
            return True
    return False


def loose(objs, tol=0.05, ground=0.04):
    """The islands that nothing holds up."""
    isl = islands(objs)
    held = [i for i, s in enumerate(isl) if s.lo[2] < ground]
    ok = set(held)
    todo = list(held)
    rest = set(range(len(isl))) - ok
    while todo and rest:
        a = isl[todo.pop()]
        hit = [j for j in rest if _near(a, isl[j], tol) and _touch(a, isl[j], tol)]
        for j in hit:
            rest.discard(j)
            ok.add(j)
            todo.append(j)
    return [isl[j] for j in sorted(rest)]


def drop(objs, tol=0.05):
    """Removes every loose island; returns what was removed as
    (part, faces, low corner, high corner)."""
    gone = loose(objs, tol)
    by = {}
    for s in gone:
        by.setdefault(s.ob.name, (s.ob, set()))[1].update(s.faces)
    for ob, fs in by.values():
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in fs], context="FACES")
        bm.to_mesh(ob.data)
        bm.free()
    return [(s.ob.name, len(s.faces), [round(v, 2) for v in s.lo], [round(v, 2) for v in s.hi]) for s in gone]
