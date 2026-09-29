"""TarDev06, the impaler with the bright right spike and long side horns.

    blender -b --factory-startup --python bespoke/TarDev06.py

The pedestal, skirt, spikes, horns and hook are the round-4 model. The
shackle is made here, after the model is made sturdy, against the left
face itself: the ring and its plate lie flat on that face, the eye bolt
stands square out of it, and the ring hangs from the eye at the same hang
point as before.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402
from common import K  # noqa: E402
import devices as D  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

NAME = "TarDev06"
SHAPE = dict(a0=1.2, a1=0.68, h=4.4, cap=0.5, n=16, rz=45, dome=False, yscale=0.88, x=0.06, lump=0.07, skirt=14)
SPIKES = [([(48, 66, 0), (49, 32, 0), (48, 2, 0.1)], 0.2),
          ([(56, 66, 0), (62, 40, 0), (63, 12, 0.1)], 0.15),
          ([(38, 80, 0), (20, 70, 0), (8, 58, 0), (0, 44, 0.1)], 0.23, 2.4),
          ([(62, 86, 0), (80, 86, 0), (92, 76, 0), (96, 64, 0.1)], 0.22, 2.4),
          ([(44, 96, -1.08), (41, 106, -1.28), (40, 118, -1.38)], 0.12)]
RING_AT = (31, 95, -0.5)
# the sprite's loop is about 12 px across; the old 0.33 ring drew about 7
R, r = 0.42, 0.06
GIRTH, RISE = 1.12, 0.85


def hang_point(bvh, c, axis):
    """Where the round-4 model hung its ring (hung_ring's eye), pre-sturdy."""
    loc, N = D._face(bvh, c, axis)
    Z = Vector((0, 0, 1))
    U = (Z - N * Z.dot(N)).normalized()
    T = loc + N * (0.045 + 0.03) + U * 0.33
    base, _ = D._face(bvh, T, axis, 0.3, sight=False)
    return base


def face_normal(cairn, at, reach=0.9):
    """The left face's plane near at: the area-weighted normal of the
    pedestal faces round it that look the same way as the nearest one."""
    me = cairn.data
    near = min(me.polygons, key=lambda p: (p.center - at).length)
    ref = Vector((near.normal.x, near.normal.y, 0)).normalized()
    N = Vector()
    for p in me.polygons:
        if (p.center - at).length < reach and Vector((p.normal.x, p.normal.y, 0)).normalized().dot(ref) > 0.8:
            N += p.normal * p.area
    return N.normalized()


def shackle(cairn, base):
    """Plate, eye bolt and ring on the face at base, all in the face's frame."""
    _, bvh = C._tree(cairn)
    base = bvh.find_nearest(base)[0]
    N = face_normal(cairn, base)
    Z = Vector((0, 0, 1))
    U = (Z - N * Z.dot(N)).normalized()
    H = U.cross(N)
    er = r + 0.05
    T = base + N * (r + 0.03)
    Cn = T - U * R

    def basis(a, b, n, o):
        return Matrix.Translation(o) @ Matrix((a, b, n)).transposed().to_4x4()

    ring = K.torus(R, r, seg=18, rseg=5, mt=D.iron(), name="ring", axis="Z")
    K.place(ring, basis(H, U, N, Cn))
    eye = K.torus(er, 0.04, seg=10, rseg=4, mt=D.iron(), name="ring_eye", axis="Z")
    K.place(eye, basis(N, U, N.cross(U), T))
    plate = K.block(er * 3.2, er * 3.2, 0.1, 0, 0, -0.05, 0, D.iron(), "ring_plate")
    K.place(plate, basis(H, U, N, base + N * 0.01))
    # how the ring lies on the lumpy stone: its signed gap round the loop
    gaps = []
    import math
    for k in range(24):
        a = 2 * math.pi * k / 24
        q = Cn + (H * math.cos(a) + U * math.sin(a)) * R - N * r
        loc, nrm, _, d = bvh.find_nearest(q)
        gaps.append(d if (q - loc).dot(nrm) > 0 else -d)
    print("HANDKIT_RING %s normal (%.2f, %.2f, %.2f) back gap min %.3f max %.3f" % (
        NAME, N.x, N.y, N.z, min(gaps), max(gaps)))
    return [ring, eye, plate]


def build():
    P = K.Pic(NAME)
    shape, ys, sk = D._shape(SHAPE)
    parts = D._squash_y(D.body(seed=6, **shape), ys, shape.get("y", 0.0))
    axis = (shape["x"], 0.0)
    cairn = next(p for p in parts if p.name.startswith("cairn"))
    bvh = D._bvh(cairn)
    parts += D.skirt(shape["a0"], shape["h"], shape["n"], shape["rz"], ys, shape["x"], 0.0, sk, 46, D.stacked_stone())
    parts += D._spikes(P, SPIKES, D.steel(), {0: D.iron(), 1: D.steel(True)}, (4,), bvh, axis)
    base = hang_point(bvh, P.at(*RING_AT), axis)
    parts = K.sturdy(parts, GIRTH, RISE)
    base = Matrix.Diagonal((GIRTH, GIRTH, RISE, 1.0)) @ base
    return parts + shackle(cairn, base)


if __name__ == "__main__":
    C.main(NAME, build)
