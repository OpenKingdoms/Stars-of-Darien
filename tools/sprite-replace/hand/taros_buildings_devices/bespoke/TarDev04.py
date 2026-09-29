"""TarDev04, the impaler on its steel-horned pedestal.

    blender -b --factory-startup --python bespoke/TarDev04.py

The pedestal, skirt, side horns, hook and hung ring are the round-4
model. The two top spikes are drawn from the sprite as before, but each
root now runs on down its own axis into the pyramid cap, so both grow out
of the stone.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402
from common import K  # noqa: E402
import devices as D  # noqa: E402
from mathutils import Vector  # noqa: E402

NAME = "TarDev04"
SHAPE = dict(a0=1.28, a1=0.85, h=4.2, cap=0.5, n=16, rz=45, dome=False, yscale=0.85, x=-0.12, lump=0.07, skirt=14)
# the central spike and the leaning one: sprite points, root radius, taper,
# and how far (pre-sturdy cells) the root carries on into the cap
TOP = [([(46, 68, 0), (46, 36, 0), (47, 2, 0.1)], 0.2, 1.3, 0.55, "iron"),
       ([(41, 66, 0), (35, 39, -0.07), (29, 12, -0.15)], 0.18, 1.8, 0.75, "steel")]
SIDE = [([(60, 74, 0), (74, 64, 0), (85, 50, 0.1), (90, 36, 0.2)], 0.18),
        ([(38, 84, -0.3), (22, 84, -0.3), (8, 80, -0.3), (0, 70, -0.2)], 0.18, 1.8),
        ([(50, 90, -1.08), (58, 96, -1.22), (62, 108, -1.32)], 0.12)]
RING = ((24, 103, -0.75), 0.5)


def rooted(P, pts, r0, taper, root, mt):
    """A spike along sprite points whose root carries on down its axis into
    the stone, the visible part as spike() draws it."""
    path = [Vector(p) for p in P.path(pts)]
    r0 *= D.SPIKE_GIRTH
    n = len(path)
    radii = [r0 * (1 - (i / (n - 1)) ** max(taper, 1.6)) for i in range(n)]
    radii[-1] = 0.0
    lead = path[0] - (path[1] - path[0]).normalized() * root
    return K.tube([lead] + path, [r0] + radii, seg=8 if r0 > 0.19 else 6, sub=3, mt=mt, name="spike")


def inside(bvh, v):
    """Whether v is inside the closed cairn: the first face up is seen from within."""
    hit = bvh.ray_cast(v, Vector((0, 0, 1)))
    return hit[0] is not None and hit[1].z > 0


def build():
    P = K.Pic(NAME)
    shape, ys, sk = D._shape(SHAPE)
    parts = D._squash_y(D.body(seed=4, **shape), ys, shape.get("y", 0.0))
    axis = (shape["x"], 0.0)
    cairn = next(p for p in parts if p.name.startswith("cairn"))
    bvh = D._bvh(cairn)
    parts += D.skirt(shape["a0"], shape["h"], shape["n"], shape["rz"], ys, shape["x"], 0.0, sk, 44, D.stacked_stone())
    mats = {"iron": D.iron(), "steel": D.steel()}
    tops = [rooted(P, pts, r0, tp, root, mats[m]) for pts, r0, tp, root, m in TOP]
    parts += tops
    parts += D._spikes(P, SIDE, D.steel(), None, (2,), bvh, axis)
    (c, R) = RING
    parts += D.hung_ring(bvh, P.at(*c), axis, R=R, r=0.07)
    parts = K.sturdy(parts, 1.12, 0.85)
    # check the roots: every vertex of each spike's lowest ring is in the stone
    _, cb = C._tree(cairn)
    for t in tops:
        vs = [v.co for v in t.data.vertices]
        z0 = min(v.z for v in vs)
        low = [v for v in vs if v.z < z0 + 0.3]
        print("HANDKIT_ROOT %s base z %.2f, %d of %d low verts inside" % (
            NAME, z0, sum(inside(cb, v) for v in low), len(low)))
    return parts


if __name__ == "__main__":
    C.main(NAME, build)
