"""Veruna lodestone: a gold octopus clasping a grey boulder, arms raised
round a blue crystal that floats over its head. Built to the picture's
classic silhouette.

    blender -b --factory-startup --python tools/sprite-replace/lodes/VERLODE.py
"""
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, r"D:\Projects\openkingdoms-unity\tools\sprite-replace")
import handkit as hk  # noqa: E402

NAME = "VERLODE"
PIC = r"D:\OKReplace\lodes\sprites\VERLODE.png"
HX, HY = 31, 110


def lin(r, g, b):
    """sRGB 0-255 to linear, for sampled picture colours."""
    return tuple(((c / 255.0 + 0.055) / 1.055) ** 2.4 if c > 10 else c / 255.0 / 12.92 for c in (r, g, b))


def P(col, row, y):
    """A picture point (continuous pixel coords) at depth y, placed where
    the classic camera would draw it: row = HY - 16 y - 8 z."""
    return Vector(((col - HX) / 16.0, y, (HY - row - 16.0 * y) / 8.0))


def catmull(pts, n):
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for j in range(n):
            t = j / n
            out.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t))
    out.append(pts[-1].copy())
    return out


def tube(pts, r0, r1, sides=8, n=3, power=0.9, mat=None, name="arm", inner=None, suckers=None,
         inward=None, round_tip=False):
    """A tapering tentacle along a smooth curve through pts, closed at the
    base and drawn to a point at the tip, or to a small dome if round_tip.
    inner=(mat, t0) puts the inside of the curl from t0 on on a second
    material; suckers=(mat, t0, t1, count) sets small cups along that inner
    side. inward(p) overrides the curl as the inner side's direction."""
    c = catmull(pts, n)
    L = [0.0]
    for a, b in zip(c, c[1:]):
        L.append(L[-1] + (b - a).length)
    bm = bmesh.new()
    rings, frames = [], []
    T0 = (c[1] - c[0]).normalized()
    N = T0.orthogonal().normalized()
    body = c if round_tip else c[:-1]
    for i, p in enumerate(body):
        T = (c[min(i + 1, len(c) - 1)] - c[max(i - 1, 0)]).normalized()
        N = (N - T * N.dot(T)).normalized()
        B = T.cross(N)
        t = L[i] / L[-1]
        r = r1 + (r0 - r1) * (1 - t) ** power
        if inward is not None:
            k = inward(p)
        else:
            # the inside of the bend, lifted a little so the camera above sees it
            k = c[min(i + 1, len(c) - 1)] - 2 * p + c[max(i - 1, 0)]
            k = (k - T * k.dot(T))
            k = (k.normalized() if k.length > 1e-6 else -N) + Vector((0, 0, 0.45))
        k = (k - T * k.dot(T)).normalized()
        frames.append((p, T, N, B, r, t, k))
        rings.append([bm.verts.new(p + (N * math.cos(2 * math.pi * j / sides) + B * math.sin(2 * math.pi * j / sides)) * r)
                      for j in range(sides)])
    if round_tip:
        p, T, N, B, r, t, k = frames[-1]
        q = p + T * r1 * 0.55
        frames.append((q, T, N, B, r1 * 0.8, 1.0, k))
        rings.append([bm.verts.new(q + (N * math.cos(2 * math.pi * j / sides) + B * math.sin(2 * math.pi * j / sides))
                                   * r1 * 0.8) for j in range(sides)])
        tip = bm.verts.new(p + T * r1)
    else:
        tip = bm.verts.new(c[-1])
    faces = []
    for i, (A, Bq) in enumerate(zip(rings, rings[1:])):
        for j in range(sides):
            f = bm.faces.new((A[j], A[(j + 1) % sides], Bq[(j + 1) % sides], Bq[j]))
            faces.append((f, i, j))
    for j in range(sides):
        bm.faces.new((rings[-1][j], rings[-1][(j + 1) % sides], tip))
    bm.faces.new(list(reversed(rings[0])))
    if inner:
        for f, i, j in faces:
            p, T, N, B, r, t, k = frames[i]
            a = 2 * math.pi * (j + 0.5) / sides
            d = N * math.cos(a) + B * math.sin(a)
            if t >= inner[1] and d.dot(k) > 0.55:
                f.material_index = 1
    cups = []
    if suckers:
        smat, s0, s1, count = suckers
        for q in range(count):
            tt = s0 + (s1 - s0) * (q + 0.5) / count
            i = min(range(len(frames)), key=lambda m: abs(frames[m][5] - tt))
            p, T, N, B, r, t, k = frames[i]
            rr = max(0.024, min(0.065, r * 0.62))
            cups.append((p + k * r * 0.93, k, T, rr))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = hk._object(name, bm, mat)
    if inner:
        ob.data.materials.append(inner[0])
    hk.smooth(ob, 70)
    out = [ob]
    for p, k, T, rr in cups:
        out.append(cup(p, k, T, rr, suckers[0]))
    return out


def cup(p, n, T, r, mat):
    """A sucker: a low five-sided ring with a dimple, facing n."""
    bm = bmesh.new()
    B = n.cross(T).normalized()
    T = B.cross(n).normalized()
    outer = [bm.verts.new(p + (T * math.cos(a) + B * math.sin(a)) * r - n * r * 0.3)
             for a in (2 * math.pi * j / 5 for j in range(5))]
    lip = [bm.verts.new(p + (T * math.cos(a) + B * math.sin(a)) * r * 0.8 + n * r * 0.35)
           for a in (2 * math.pi * j / 5 for j in range(5))]
    pit = bm.verts.new(p + n * r * 0.05)
    for j in range(5):
        j1 = (j + 1) % 5
        bm.faces.new((outer[j], outer[j1], lip[j1], lip[j]))
        bm.faces.new((lip[j], lip[j1], pit))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk.smooth(hk._object("sucker", bm, mat), 60)


def lathe(profile, seg, mat=None, name="lathe", x=0.0, y=0.0, rmod=None, phase=0.0, lean=None):
    """A surface of revolution through (radius, z) points, bottom to top;
    lean(z) shifts each ring back in y."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        yy = y + (lean(z) if lean else 0.0)
        if r < 1e-5:
            rings.append([bm.verts.new((x, yy, z))])
            continue
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg + phase
            rr = r * (rmod(a, z) if rmod else 1.0)
            ring.append(bm.verts.new((x + rr * math.cos(a), yy + rr * math.sin(a), z)))
        rings.append(ring)
    for A, B in zip(rings, rings[1:]):
        if len(A) == 1:
            for k in range(seg):
                bm.faces.new((A[0], B[(k + 1) % seg], B[k]))
        elif len(B) == 1:
            for k in range(seg):
                bm.faces.new((A[k], A[(k + 1) % seg], B[0]))
        else:
            for k in range(seg):
                bm.faces.new((A[k], A[(k + 1) % seg], B[(k + 1) % seg], B[k]))
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk._object(name, bm, mat)


def crystal(rings, top, bot, seg, sx, sy, mats, name="crystal"):
    """A faceted crystal through (scale, z, half-turn) rings with an apex and
    a tip; sx, sy are the girdle's half-widths, so it can be flattened. Each
    ring turned half a facet from the next gives kite facets, and the facets
    alternate between the two materials so they read."""
    bm = bmesh.new()
    vr = []
    for s, z, h in rings:
        vr.append([bm.verts.new((s * sx * math.cos(2 * math.pi * (k + h) / seg),
                                 s * sy * math.sin(2 * math.pi * (k + h) / seg), z)) for k in range(seg)])
    apex = bm.verts.new((0.0, 0.0, top))
    tip = bm.verts.new((0.0, 0.0, bot))
    out = []
    for (A, B), (ra, rb) in zip(zip(vr, vr[1:]), zip(rings, rings[1:])):
        for k in range(seg):
            k1 = (k + 1) % seg
            if ra[2] == rb[2]:
                out.append((bm.faces.new((A[k], A[k1], B[k1], B[k])), k))
            elif rb[2] > ra[2]:
                out.append((bm.faces.new((A[k], A[k1], B[k])), k))
                out.append((bm.faces.new((A[k1], B[k1], B[k])), k + 1))
            else:
                out.append((bm.faces.new((A[k], B[k1], B[k])), k))
                out.append((bm.faces.new((A[k], A[k1], B[k1])), k + 1))
    for k in range(seg):
        out.append((bm.faces.new((vr[-1][k], vr[-1][(k + 1) % seg], apex)), k + 1))
        out.append((bm.faces.new((vr[0][(k + 1) % seg], vr[0][k], tip)), k))
    for f, k in out:
        f.material_index = k % 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = hk._object(name, bm, mats[0])
    ob.data.materials.append(mats[1])
    return ob


def gem_eye(c, n, r, depth, mat, name="eye"):
    """A small faceted cabochon at c facing n: an octagon girdle and a
    two-tier dome, flat shaded so every facet catches the light."""
    bm = bmesh.new()
    n = n.normalized()
    u = n.cross(Vector((0, 0, 1))).normalized()
    v = u.cross(n).normalized()
    ring = lambda s, d, h: [bm.verts.new(c + (u * math.cos(2 * math.pi * (k + h) / 8) * 1.0
                                             + v * math.sin(2 * math.pi * (k + h) / 8) * 1.15) * r * s + n * depth * d)
                            for k in range(8)]
    g0, g1 = ring(1.0, -0.2, 0.0), ring(0.62, 0.65, 0.5)
    top = bm.verts.new(c + n * depth)
    for k in range(8):
        k1 = (k + 1) % 8
        bm.faces.new((g0[k], g0[k1], g1[k]))
        bm.faces.new((g0[k1], g1[k1], g1[k]))
        bm.faces.new((g1[k], g1[k1], top))
    bm.faces.new(list(reversed(g0)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk._object(name, bm, mat)


def ellipsoid(c, r, mat=None, name="ball", u=12, v=8):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    for vt in bm.verts:
        vt.co = Vector((c[0] + vt.co.x * r[0], c[1] + vt.co.y * r[1], c[2] + vt.co.z * r[2]))
    return hk.smooth(hk._object(name, bm, mat), 80)


def image(name, rgb):
    """A packed image from an (h, w, 3) array of sRGB values in 0-1."""
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    px = np.ones((h, w, 4), np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels[:] = px.ravel()
    img.pack()
    return img


def noise(n, beta, seed):
    """Tileable 1/f noise in 0-1."""
    rng = np.random.default_rng(seed)
    f = np.fft.fft2(rng.standard_normal((n, n)))
    fx, fy = np.fft.fftfreq(n)[:, None], np.fft.fftfreq(n)[None, :]
    r = np.sqrt(fx * fx + fy * fy)
    r[0, 0] = 1.0
    f = f / r ** (beta / 2)
    f[0, 0] = 0
    t = np.real(np.fft.ifft2(f))
    return (t - t.min()) / (t.max() - t.min())


def granite_img(name, base, seed, n=256):
    """Weathered grey stone: broad cloudy patches, fine grit and a few
    darker specks, tiling in both directions."""
    rng = np.random.default_rng(seed)
    cloud = noise(n, 3.2, seed)
    grit = rng.random((n, n))
    speck = (noise(n, 1.2, seed + 7) > 0.8).astype(np.float32)
    b = np.array(base, np.float32) / 255
    shade = 0.8 + 0.34 * cloud + 0.1 * (grit - 0.5) - 0.18 * speck
    col = b[None, None, :] * shade[..., None]
    col[..., 2] *= 0.98 + 0.05 * cloud
    return image(name, col)


def textured(name, img, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    return m


def boulder(prof, seg, mat, yc, facets, seed, name="stone", sx=None):
    """A lumpy rounded stone through (radius, z) rings, bottom to top, round
    (0, yc): each vertex pushed in or out by a little smooth noise, then
    planed flat wherever a chisel facet (normal, depth from the centre at
    height zc) cuts it; sx(z) widens it side to side. Wrapped in a spherical
    UV for its grain."""
    rng = np.random.default_rng(seed)
    waves = [(rng.integers(1, 5), rng.uniform(0, 2 * math.pi), rng.uniform(1.5, 6.0), rng.uniform(0, 2 * math.pi))
             for _ in range(7)]
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        if r < 1e-5:
            rings.append([bm.verts.new((0.0, yc, z))])
            continue
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            lump = sum(math.sin(m * a + ph) * math.sin(f * z + pz) for m, ph, f, pz in waves) / len(waves)
            rr = r * (1.0 + (0.05 + 0.07 * min(1.0, max(0.0, (z - 0.4) / 0.35))) * lump)
            ring.append(bm.verts.new((rr * math.cos(a) * (sx(z) if sx else 1.0), yc + rr * math.sin(a), z)))
        rings.append(ring)
    for A, B in zip(rings, rings[1:]):
        if len(A) == 1:
            for k in range(seg):
                bm.faces.new((A[0], B[(k + 1) % seg], B[k]))
        elif len(B) == 1:
            for k in range(seg):
                bm.faces.new((A[k], A[(k + 1) % seg], B[0]))
        else:
            for k in range(seg):
                bm.faces.new((A[k], A[(k + 1) % seg], B[(k + 1) % seg], B[k]))
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    for n, d, zc in facets:
        c = Vector((0.0, yc, zc))
        for v in bm.verts:
            s = (v.co - c).dot(n)
            if s > d:
                v.co -= n * (s - d)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = hk._object(name, bm, mat)
    me = ob.data
    uv = me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        us = []
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            us.append((math.atan2(co.y - yc, co.x) / (2 * math.pi)) % 1.0)
        if max(us) - min(us) > 0.5:
            us = [u + 1.0 if u < 0.5 else u for u in us]
        for li, u in zip(p.loop_indices, us):
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (u * 2.0, co.z * 0.6)
    return ob


def classic_extents(ob):
    cols = [HX + 16 * v.co.x for v in ob.data.vertices]
    rows = [HY - 16 * v.co.y - 8 * v.co.z for v in ob.data.vertices]
    print("CLASSIC_EXTENTS left %.1f right %.1f top %.1f bottom %.1f" % (min(cols), max(cols), min(rows), max(rows)))


hk.reset()

GOLD = hk.pbr("ver_gold", lin(236, 190, 80), rough=0.28, metal=0.95)
GOLD_BACK = hk.pbr("ver_gold_worn", lin(218, 168, 66), rough=0.34, metal=0.95)
GOLD_PALE = hk.pbr("ver_gold_underside", lin(240, 212, 140), rough=0.4, metal=0.6)
STONE = textured("ver_stone", granite_img("ver_stone_tex", (100, 99, 96), 3), 0.78)
CRYSTAL = hk.pbr("ver_crystal", lin(6, 18, 96), rough=0.13, emit=lin(8, 26, 150), strength=0.4)
CRYSTAL_LT = hk.pbr("ver_crystal_facet", lin(12, 32, 140), rough=0.1, emit=lin(24, 58, 240), strength=1.3)
EMERALD = hk.pbr("ver_emerald", lin(11, 110, 61), rough=0.08, emit=lin(20, 150, 80), strength=0.3)

parts = []

# the stone: a weathered boulder on a flattened base, a few chisel facets
# planed into it, narrowing to a small foot so none of it shows below the
# ground arms' curls
STONE_Y = 0.3
STONE_PROF = [(0.0, 0.0), (0.27, 0.0), (0.31, 0.03), (0.35, 0.09), (0.41, 0.24), (0.49, 0.42), (0.57, 0.62),
              (0.63, 0.82), (0.65, 0.98), (0.62, 1.14), (0.53, 1.28), (0.4, 1.38), (0.22, 1.44), (0.0, 1.46)]


def facet(az, el, zc, d):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e))), d, zc


FACETS = [facet(-124, 14, 0.82, 0.56), facet(-60, -10, 0.58, 0.46), facet(-90, 40, 0.98, 0.53),
          facet(-160, -8, 0.6, 0.6), facet(150, 8, 0.72, 0.56), facet(95, 25, 0.95, 0.52),
          facet(36, 20, 0.9, 0.56), facet(-18, 2, 0.7, 0.62)]
parts.append(hk.smooth(boulder(STONE_PROF, 22, STONE, STONE_Y, FACETS, 5,
                               sx=lambda z: 1.08 + 0.22 * min(1.0, max(0.0, (1.0 - z) / 0.5))), 40))

# body: a flared skirt the arms spring from and the round mantle over it,
# leaning back a touch; set back like the stone
BODY_Y = 0.25
HEAD_Z, HEAD_R, HEAD_H = 2.3, 0.58, 0.64
prof = [(0.0, 1.34), (0.46, 1.37), (0.6, 1.48), (0.645, 1.64), (0.63, 1.8), (0.6, 1.93)]
for k in range(12):
    a = -0.42 + (math.pi / 2 + 0.42) * k / 11
    prof.append((HEAD_R * math.cos(a), HEAD_Z + HEAD_H * math.sin(a)))
prof[-1] = (0.0, HEAD_Z + HEAD_H)
lean = lambda z: BODY_Y + 0.07 * min(1.0, max(0.0, (z - 1.9) / 1.0)) ** 2
parts.append(hk.smooth(lathe(prof, 22, GOLD, "body", lean=lean), 60))


def head_surface(x, z, out=0.0):
    """The point on the mantle's front at x, z, pushed out along its normal."""
    r = HEAD_R * math.sqrt(max(0.0, 1 - ((z - HEAD_Z) / HEAD_H) ** 2))
    y = lean(z) - math.sqrt(max(0.0, r * r - x * x))
    n = Vector((x, y - lean(z), (z - HEAD_Z) * (HEAD_R / HEAD_H) ** 2)).normalized()
    return Vector((x, y, z)) + n * out, n


# emerald eyes where the classic camera saw them, each set in a gold mound
# under a heavy brow
EYE_X, EYE_Z = 0.265, 2.6
for s in (-1, 1):
    c, n = head_surface(s * EYE_X, EYE_Z)
    parts.append(ellipsoid(c + n * 0.02, (0.17, 0.11, 0.16), GOLD, "eye_mound", u=8, v=6))
    parts.append(gem_eye(c + n * 0.1, n, 0.14, 0.07, EMERALD))
    brow = []
    for a in (0.15, 0.75, 1.4, 2.1, 2.75):
        q, _ = head_surface(s * EYE_X - s * 0.16 * math.cos(a), EYE_Z + 0.07 + 0.1 * math.sin(a), 0.1)
        brow.append(q)
    parts += tube(brow, 0.055, 0.03, sides=6, n=2, power=1.0, mat=GOLD, name="brow")


# arms, left side as (col, row, depth) picture points, mirrored for the right
def arm_pts(spec, side):
    out = []
    for p in spec:
        v = Vector(p[1:]) if p[0] == "xyz" else P(*p)
        out.append(Vector((v.x * side, v.y, v.z)))
    return out


# the tall arms swing forward, back behind the crystal and forward again,
# the tips hooking in over it
TALL = [("xyz", -0.3, 0.05, 1.8), ("xyz", -0.55, -0.15, 2.45), (20.3, 86, -0.3), (18.6, 78, -0.28),
        (18.3, 70, -0.1), (16.8, 62, 0.1), (14.0, 56.5, 0.25), (11.0, 52, 0.38), (9.3, 46, 0.45),
        (9.6, 40, 0.2), (11.3, 36, -0.15), (13.6, 33.6, -0.38), (15.6, 32.5, -0.36), (16.8, 33.0, -0.08),
        (17.2, 33.6, 0.22)]
MID = [("xyz", -0.3, -0.12, 2.0), (21.0, 97.3, -0.2), (15.5, 96.8, -0.26), (12.0, 95.3, -0.32),
       (9.8, 92.0, -0.38), (8.4, 87.0, -0.42), (7.9, 80.0, -0.45), (7.4, 75.5, -0.46), (6.5, 72.0, -0.46),
       (5.3, 70.2, -0.44)]
LOW = [("xyz", -0.05, 0.02, 2.1), ("xyz", -0.2, -0.36, 1.9), ("xyz", -0.5, -0.46, 1.55), ("xyz", -0.9, -0.44, 0.98),
       ("xyz", -1.12, -0.4, 0.45), ("xyz", -1.2, -0.5, 0.1), ("xyz", -1.2, -0.82, 0.08),
       ("xyz", -1.05, -1.0, 0.08), ("xyz", -0.78, -1.02, 0.08), ("xyz", -0.56, -0.86, 0.1),
       ("xyz", -0.45, -0.64, 0.14), ("xyz", -0.44, -0.46, 0.24)]
BACK = [("xyz", -0.2, 0.25, 1.75), ("xyz", -0.45, 0.6, 1.15), ("xyz", -0.6, 1.0, 0.4),
        ("xyz", -0.62, 1.3, 0.08), ("xyz", -0.45, 1.58, 0.08), ("xyz", -0.2, 1.6, 0.1),
        ("xyz", -0.12, 1.4, 0.18)]

# the raised arms carry their pale band and cups on the side toward the
# crystal and underneath, where a tilted camera finds them
inward = lambda p: Vector((-math.copysign(1.0, p.x), 0.2, -0.45))

# the ground arms keep theirs on the inside of the curl, tipped up
curl = lambda p: Vector((math.copysign(0.82, p.x) - p.x, -0.74 - p.y, 0.0)).normalized() + Vector((0, 0, 0.5))

for side in (-1, 1):
    parts += tube(arm_pts(TALL, side), 0.16, 0.02, power=1.8, mat=GOLD, name="arm_tall",
                  inner=(GOLD_PALE, 0.22), suckers=(GOLD_PALE, 0.24, 0.64, 6), inward=inward)
    parts += tube(arm_pts(MID, side), 0.165, 0.03, power=1.8, mat=GOLD, name="arm_mid",
                  inner=(GOLD_PALE, 0.22), suckers=(GOLD_PALE, 0.26, 0.72, 4), inward=inward, round_tip=True)
    parts += tube(arm_pts(LOW, side), 0.155, 0.04, mat=GOLD, name="arm_low",
                  inner=(GOLD_PALE, 0.5), suckers=(GOLD_PALE, 0.5, 0.94, 7), inward=curl)
    parts += tube(arm_pts(BACK, side), 0.15, 0.02, n=2, power=1.8, mat=GOLD_BACK, name="arm_back")

# the crystal floating over the head: a thick, heavy gem, a little flattened
# front to back so its crown still comes to the picture's point
ZG = 7.55
parts.append(crystal([(0.72, ZG - 1.0, 0.5), (1.0, ZG - 0.18, 0.0), (1.0, ZG + 0.18, 0.0),
                      (0.74, ZG + 0.66, 0.5)],
                     9.55, 4.94, 8, 0.68, 0.52, (CRYSTAL, CRYSTAL_LT)))

ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\VERLODE.glb",
               {"replacesTexture": "verlode_regularlodestone", "replacesPiece": "VerLode"})
classic_extents(ob)
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, (HX, HY), scale=4)
