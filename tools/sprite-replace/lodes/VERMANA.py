"""Veruna mana lodestone: a green marble column, fluted, with a draped
collar, carrying a gold collar and two gold dolphins that hold a large blue
brilliant between their beaks and tails. Built to the picture's classic
silhouette.

    blender -b --factory-startup --python tools/sprite-replace/lodes/VERMANA.py
"""
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, r"D:\Projects\openkingdoms-unity\tools\sprite-replace")
import handkit as hk  # noqa: E402

NAME = "VERMANA"
PIC = r"D:\OKReplace\lodes\sprites\VERMANA.png"
HX, HY = 31, 110
CY = 0.3  # the column stands this far behind the anchor


def lin(r, g, b):
    """sRGB 0-255 to linear, for sampled picture colours."""
    return tuple(((c / 255.0 + 0.055) / 1.055) ** 2.4 if c > 10 else c / 255.0 / 12.92 for c in (r, g, b))


def P(col, row, y):
    """A picture point (continuous pixel coords) at depth y, placed where
    the classic camera would draw it: row = HY - 16 y - 8 z."""
    return Vector(((col - HX) / 16.0, y, (HY - row - 16.0 * y) / 8.0))


def cyl_uv(ob, yc, k=0.22):
    """Wraps u once round the column axis and runs v up it, so a tiling
    texture keeps its grain."""
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
            uv.data[li].uv = (u, co.z * k)
    return ob


def rings_mesh(rows, mat=None, name="rings", cap_bottom=True, cap_top=True):
    """Quads between rings of equal length (closed loops), capped at the
    ends; a single-point ring makes a fan."""
    bm = bmesh.new()
    vr = [[bm.verts.new(p) for p in ring] for ring in rows]
    for A, B in zip(vr, vr[1:]):
        if len(A) == 1:
            for k in range(len(B)):
                bm.faces.new((A[0], B[(k + 1) % len(B)], B[k]))
        elif len(B) == 1:
            for k in range(len(A)):
                bm.faces.new((A[k], A[(k + 1) % len(A)], B[0]))
        else:
            for k in range(len(A)):
                bm.faces.new((A[k], A[(k + 1) % len(A)], B[(k + 1) % len(B)], B[k]))
    if cap_bottom and len(vr[0]) > 1:
        bm.faces.new(list(reversed(vr[0])))
    if cap_top and len(vr[-1]) > 1:
        bm.faces.new(vr[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk._object(name, bm, mat)


def lathe(profile, seg, mat=None, name="lathe", rmod=None, yc=CY):
    """A surface of revolution round (0, yc) through (radius, z) points,
    bottom to top; rmod(angle, z) scales the radius for flutes."""
    rows = []
    for r, z in profile:
        if r < 1e-5:
            rows.append([(0.0, yc, z)])
            continue
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            rr = r * (rmod(a, z) if rmod else 1.0)
            ring.append((rr * math.cos(a), yc + rr * math.sin(a), z))
        rows.append(ring)
    return rings_mesh(rows, mat, name)


def brilliant(R, crown, pav, tilt, centre, seg, mats, mid=0.84, low=0.6):
    """A round brilliant, each ring turned half a facet from the last, built
    table up and tipped by tilt about x (negative leans the table back).
    mats: deep, mid, the bright crown facets, two pavilion blues, then the
    table's pale blue and its whiter left half."""
    rings = [(low * R, -pav * 0.42, 0.5), (R, -0.05, 0.0), (R, 0.05, 0.0),
             (mid * R, crown * 0.5, 0.5), (0.53 * R, crown, 0.0)]
    bm = bmesh.new()
    vr = [[bm.verts.new((r * math.cos(2 * math.pi * (k + h) / seg), r * math.sin(2 * math.pi * (k + h) / seg), z))
           for k in range(seg)] for r, z, h in rings]
    tip = bm.verts.new((0.0, 0.0, -pav))
    mid_v = bm.verts.new((0.0, 0.0, crown))
    faces = [(bm.faces.new((mid_v, vr[-1][k], vr[-1][(k + 1) % seg])), k, None) for k in range(seg)]
    for (A, B), (ra, rb) in zip(zip(vr, vr[1:]), zip(rings, rings[1:])):
        for k in range(seg):
            k1 = (k + 1) % seg
            if ra[2] == rb[2]:
                faces.append((bm.faces.new((A[k], A[k1], B[k1], B[k])), k, rb[1]))
            elif rb[2] > ra[2]:
                faces.append((bm.faces.new((A[k], A[k1], B[k])), k, rb[1]))
                faces.append((bm.faces.new((A[k1], B[k1], B[k])), k + 1, rb[1]))
            else:
                faces.append((bm.faces.new((A[k], B[k1], B[k])), k, rb[1]))
                faces.append((bm.faces.new((A[k], A[k1], B[k1])), k + 1, rb[1]))
    for k in range(seg):
        faces.append((bm.faces.new((vr[0][(k + 1) % seg], vr[0][k], tip)), k, -pav))
    for f, k, z in faces:
        cx = sum(v.co.x for v in f.verts) / len(f.verts)
        cy_ = sum(v.co.y for v in f.verts) / len(f.verts)
        cz = sum(v.co.z for v in f.verts) / len(f.verts)
        ang = math.degrees(math.atan2(cy_, cx))
        if z is None:
            # the table: pale blue, its left half whiter as painted
            f.material_index = 6 if cx < -0.02 * R else 5
        elif z == crown and cx < 0.3 * R:
            # the star facets round the table catch its light
            f.material_index = 2
        elif z > 0.05 and 112 < ang < 160:
            f.material_index = 2
        elif cz < -0.04:
            f.material_index = 3 + k % 2
        else:
            f.material_index = k % 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    rot = Matrix.Rotation(tilt, 4, "X")
    for v in bm.verts:
        v.co = rot @ v.co + centre
    ob = hk._object("gem", bm, mats[0])
    for m in mats[1:]:
        ob.data.materials.append(m)
    return ob


def catmull(pts, n):
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    c = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for j in range(n):
            t = j / n
            c.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                            + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t))
    c.append(pts[-1].copy())
    return c


def body(pts, size, side_axis, flat=0.7, sides=12, n=3, mat=None, name="body", tip_round=0.0):
    """A sweep along a smooth curve through pts whose section is an ellipse:
    size(s0, s1) gives its half-height in the plane of the bend, with s0 and
    s1 the distance from the start and from the end, and flat how thick it
    is along side_axis (the lateral axis, kept square to the curve). Closed
    at the start, drawn to a point or a small dome at the end."""
    c = catmull(pts, n)
    L = [0.0]
    for a, b in zip(c, c[1:]):
        L.append(L[-1] + (b - a).length)
    bm = bmesh.new()
    rings = []
    last = None
    for i, p in enumerate(c[:-1]):
        T = (c[i + 1] - c[max(i - 1, 0)]).normalized()
        Lv = side_axis - T * side_axis.dot(T)
        Lv.normalize()
        D = T.cross(Lv)
        r = size(L[i], L[-1] - L[i])
        rings.append([bm.verts.new(p + (D * math.cos(2 * math.pi * k / sides)
                                        + Lv * math.sin(2 * math.pi * k / sides) * flat) * r)
                      for k in range(sides)])
        last = (p, T, D, Lv, r)
    if tip_round > 0:
        p, T, D, Lv, r = last
        q = c[-1]
        rings.append([bm.verts.new(q + (D * math.cos(2 * math.pi * k / sides)
                                        + Lv * math.sin(2 * math.pi * k / sides) * flat) * tip_round)
                      for k in range(sides)])
        tip = bm.verts.new(q + T * tip_round * 0.9)
    else:
        tip = bm.verts.new(c[-1])
    for A, Bq in zip(rings, rings[1:]):
        for k in range(sides):
            bm.faces.new((A[k], A[(k + 1) % sides], Bq[(k + 1) % sides], Bq[k]))
    for k in range(sides):
        bm.faces.new((rings[-1][k], rings[-1][(k + 1) % sides], tip))
    bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk.smooth(hk._object(name, bm, mat), 75)


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


def marble_img(name, base, vein, dark, seed, n=256):
    """Green marble: a mottled ground with soft light veins and a few dark
    threads, tiling in both directions."""
    y, x = np.mgrid[0:n, 0:n] / n
    warp = noise(n, 3.6, seed)
    mott = noise(n, 3.0, seed + 3)
    v1 = np.abs(np.sin(2 * np.pi * (2 * x + 1 * y) + 6.0 * warp))
    v2 = np.abs(np.sin(2 * np.pi * (1 * x - 2 * y) + 5.0 * noise(n, 3.4, seed + 9)))
    light = np.clip(1 - v1 / 0.3, 0, 1) ** 1.2
    thread = np.clip(1 - v2 / 0.07, 0, 1)
    b, lv, dk = (np.array(c, np.float32) / 255 for c in (base, vein, dark))
    col = b[None, None, :] * (0.82 + 0.36 * mott[..., None])
    col = col * (1 - 0.7 * light[..., None]) + lv[None, None, :] * 0.7 * light[..., None]
    col = col * (1 - 0.6 * thread[..., None]) + dk[None, None, :] * 0.6 * thread[..., None]
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


def mirror(ob):
    me = ob.data
    for v in me.vertices:
        v.co.x = -v.co.x
    me.flip_normals()
    return ob


def classic_extents(ob):
    cols = [HX + 16 * v.co.x for v in ob.data.vertices]
    rows = [HY - 16 * v.co.y - 8 * v.co.z for v in ob.data.vertices]
    print("CLASSIC_EXTENTS left %.1f right %.1f top %.1f bottom %.1f" % (min(cols), max(cols), min(rows), max(rows)))


hk.reset()

MARBLE = textured("ver_marble", marble_img("ver_marble_tex", (92, 114, 104), (160, 194, 170), (40, 50, 48), 5), 0.3)
MARBLE_LT = textured("ver_marble_pale", marble_img("ver_marble_pale_tex", (126, 164, 128), (172, 210, 176),
                                                   (78, 112, 84), 11), 0.36)
PALE = hk.pbr("ver_marble_ring", lin(212, 234, 214), rough=0.28)
GOLD = hk.pbr("ver_gold", lin(236, 190, 80), rough=0.28, metal=0.96)
# the collar is older, duller gold than the dolphins
GOLD_OLD = hk.pbr("ver_gold_collar", lin(200, 160, 72), rough=0.38, metal=0.95)
JET = hk.pbr("ver_dolphin_eye", lin(28, 22, 16), rough=0.15)
SAPPHIRE = hk.pbr("ver_sapphire", lin(8, 26, 118), rough=0.12, emit=lin(10, 30, 150), strength=0.45)
SAPPHIRE_MID = hk.pbr("ver_sapphire_facet", lin(11, 34, 138), rough=0.12, emit=lin(14, 40, 175), strength=0.5)
SAPPHIRE_LT = hk.pbr("ver_sapphire_light", lin(100, 145, 235), rough=0.04, emit=lin(110, 155, 255), strength=0.85)
# the pavilion faces down onto the gold claws: rougher and less specular so
# it glows deep blue instead of mirroring them
SAPPHIRE_PAV = hk.pbr("ver_sapphire_pavilion", lin(8, 26, 118), rough=0.35, emit=lin(10, 30, 150), strength=0.5)
SAPPHIRE_PAV2 = hk.pbr("ver_sapphire_pavilion_facet", lin(11, 34, 138), rough=0.35, emit=lin(14, 40, 175),
                       strength=0.55)
for m in (SAPPHIRE_PAV, SAPPHIRE_PAV2):
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.2
# the table: a large pale window, whiter on its left as painted
TABLE = hk.pbr("ver_sapphire_table", lin(120, 160, 245), rough=0.04, emit=lin(80, 130, 255), strength=1.3)
TABLE_W = hk.pbr("ver_sapphire_table_white", lin(226, 236, 255), rough=0.04, emit=lin(210, 222, 255), strength=1.4)

parts = []

# the column: a base moulding, a deeply fluted shaft, a core under the drape
# and a capital, standing CY behind the anchor
FLUTES = 12
SHAFT = (0.42, 4.72)


def flute(a, z):
    if SHAFT[0] < z < SHAFT[1]:
        return 1.0 - 0.19 * (0.5 + 0.5 * math.cos(FLUTES * a)) ** 1.5
    return 1.0


# the column and capital are drawn heavier than the picture's, CS wider
CS = 1.26
prof = [(0.0, 0.0), (0.7, 0.0), (0.735, 0.06), (0.735, 0.13), (0.7, 0.19), (0.64, 0.24), (0.655, 0.3),
        (0.64, 0.36), (0.63, SHAFT[0]), (0.63, 1.5), (0.63, 2.6), (0.63, 3.7), (0.63, SHAFT[1]),
        (0.67, 4.76), (0.675, 4.8), (0.64, 4.85), (0.6, 5.0), (0.6, 5.9), (0.62, 6.0), (0.62, 6.1), (0.0, 6.1)]
prof = [(r * CS, z) for r, z in prof]
parts.append(hk.smooth(cyl_uv(lathe(prof, 48, MARBLE, "column", rmod=flute), CY), 50))

# the drape: pale marble cloth pinned under the ring at four points and
# hung between them in four clean swags, the front one full on, each with
# a couple of soft folds that follow its sag and a rolled hem
K, S = 32, 8
Z_TOP = 5.96
R0 = 0.6 * CS
rows = [[(R0 * math.cos(2 * math.pi * k / K), CY + R0 * math.sin(2 * math.pi * k / K), Z_TOP)
         for k in range(K)]]
hem, under = [], []
for s in range(S + 1):
    ring = []
    for k in range(K):
        a = 2 * math.pi * k / K
        u = ((a - math.pi / 4) % (math.pi / 2)) / (math.pi / 2)
        sag = math.sin(math.pi * u)
        z_low = 5.22 - 0.46 * sag ** 0.9
        t = s / S
        z = Z_TOP - t * (Z_TOP - z_low)
        r = CS * (0.6 + 0.045 + 0.03 * math.sin(math.pi * min(1.0, 1.6 * t))
                  + sag * (0.075 * math.sin(math.pi * (0.25 + 0.6 * t)) + 0.016 * math.sin(4 * math.pi * t) * t))
        ring.append((r * math.cos(a), CY + r * math.sin(a), z))
        if s == S:
            hem.append(((r + 0.022) * math.cos(a), CY + (r + 0.022) * math.sin(a), z - 0.035))
            under.append((R0 * math.cos(a), CY + R0 * math.sin(a), z - 0.03))
    rows.append(ring)
rows += [hem, under]
drape = rings_mesh(rows, MARBLE_LT, "drape", cap_bottom=False, cap_top=False)
parts.append(hk.smooth(cyl_uv(drape, CY), 60))

# a thin pale ring on the drape's top, under the gold
parts.append(hk.smooth(lathe([(r * CS, z) for r, z in ((0.6, 5.92), (0.68, 5.95), (0.7, 6.02), (0.68, 6.09),
                                                        (0.6, 6.12))], 32, PALE, "ring"), 50))

# the gold collar: a heavy band round the capital, the marble showing in
# its middle, that the claws spring from
parts.append(hk.smooth(lathe([(r * CS, z) for r, z in ((0.0, 6.2), (0.5, 6.04), (0.57, 6.09), (0.6, 6.18),
                                                        (0.58, 6.28), (0.52, 6.34), (0.44, 6.35), (0.39, 6.3),
                                                        (0.37, 6.22), (0.0, 6.2))], 32, GOLD_OLD, "collar"), 40))
parts.append(hk.smooth(lathe([(r * CS, z) for r, z in ((0.0, 6.2), (0.375, 6.2), (0.37, 6.27), (0.3, 6.34),
                                                        (0.16, 6.39), (0.0, 6.4))], 24, MARBLE,
                             "capital_top"), 50))

# the brilliant, tipped back from the classic camera as the painting has
# it: the table high in the outline, the long pavilion's point low; it
# floats over the collar in four claws
R, CROWN, PAV, TILT = 0.92, 0.47, 1.76, math.radians(-15)
MID, LOW = 0.84, 0.72
n = Vector((0.0, -math.sin(TILT), math.cos(TILT)))
GEM_MATS = (SAPPHIRE, SAPPHIRE_MID, SAPPHIRE_LT, SAPPHIRE_PAV, SAPPHIRE_PAV2, TABLE, TABLE_W)
probe = brilliant(R, CROWN, PAV, TILT, Vector((0.0, 0.0, 0.0)), 16, GEM_MATS, MID, LOW)
top_px = max(16 * v.co.y + 8 * v.co.z for v in probe.data.vertices)
bpy.data.objects.remove(probe, do_unlink=True)
gy = CY + 0.12
gz = (HY - 24.3 - top_px - 16 * gy) / 8
G = Vector((0.0, gy, gz))
parts.append(brilliant(R, CROWN, PAV, TILT, G, 16, GEM_MATS, MID, LOW))
tip = G - n * PAV
print("GEM centre", tuple(round(c, 3) for c in G), "tip", tuple(round(c, 3) for c in tip),
      "tip row %.1f" % (HY - 16 * tip.y - 8 * tip.z))


def pav_r(d):
    """The pavilion's radius a distance d below the girdle."""
    if d < PAV * 0.42:
        return R + (LOW * R - R) * d / (PAV * 0.42)
    return LOW * R * (PAV - d) / (PAV * 0.58)


# two broad claws, flat against the stone like petals, rise from the back
# of the collar and close round the pavilion's lower cone; the dolphins'
# tails hold its front
for deg in (45, 135):
    a = math.radians(deg)
    u = Vector((math.cos(a), math.sin(a), 0.0))
    w = (u - n * u.dot(n)).normalized()
    hug = [G - n * (PAV * f) + w * (pav_r(PAV * f) + 0.045) for f in (0.86, 0.74)]
    root = Vector((0.46 * CS * math.cos(a), CY + 0.46 * CS * math.sin(a), 6.3))
    lift = root.lerp(hug[0], 0.5) + w * 0.07
    end = G - n * (PAV * 0.62) + w * (pav_r(PAV * 0.62) + 0.02)
    parts.append(body([root, lift] + hug + [end], lambda s0, s1: 0.13 - 0.035 * min(1.0, s0 / 0.8),
                      w, flat=0.42, sides=8, n=2, mat=GOLD, name="claw", tip_round=0.05))

# the dolphins: each rests its belly on the collar and curls its tail up
# under the gem, the flukes cradling the pavilion either side of its point;
# the back arches outward with a swept fin on it, and the round head leans
# in so the short beak rests on the gem's girdle
girdle = G + Vector((-R - 0.02, -0.06, 0.01))
u = Vector((-math.sqrt(0.5), -math.sqrt(0.5), 0.0))
W_FRONT = (u - n * u.dot(n)).normalized()
TAIL = [G - n * (PAV * 0.88) + W_FRONT * (pav_r(PAV * 0.88) + 0.06), Vector((-0.39, CY - 0.45 * CS, 6.7)),
        Vector((-0.5 * CS, CY - 0.37 * CS, 6.37))]
S_RING = (TAIL[1] - TAIL[0]).length + (TAIL[2] - TAIL[1]).length
SPINE = [(18.0, 58.2, -0.06), (14.2, 55.2, -0.1), (11.4, 51.4, -0.1),
         (10.0, 47.2, -0.08), (9.5, 43.0, -0.02), (9.7, 38.9, 0.14), (13.6, 36.0, 0.26)]
FIN = [(10.4, 49.2, -0.12), (6.4, 50.6, -0.12), (3.0, 52.0, -0.11), (1.0, 53.0, -0.1)]


# the dolphins thickened to suit the heavier stone, the body more than the head
DS, DH = 1.12, 1.04


def dolphin_size(s0, s1):
    """Beak, round head and a body that thins to the tail, by the distance
    from the tail (s0) and from the beak's tip (s1)."""
    if s0 < S_RING:
        taper = (0.055 + 0.06 * s0 / S_RING) / 0.32
    else:
        taper = min(1.0, 0.36 + 0.64 * (s0 - S_RING) / 1.3)
    if s1 < 0.28:
        return DH * (0.035 + 0.04 * s1 / 0.28)
    if s1 < 0.56:
        return DH * (0.075 + 0.225 * math.sin(0.5 * math.pi * (s1 - 0.28) / 0.28))
    k = DH + (DS - DH) * min(1.0, (s1 - 0.56) / 0.5)
    return k * min(0.3 + 0.02 * min(1.0, (s1 - 0.56) / 0.4), 0.32 * taper)


Y = Vector((0.0, 1.0, 0.0))
for side in (-1, 1):
    pts = [q.copy() for q in TAIL] + [P(*q) for q in SPINE] + [girdle.copy()]
    group = [body(pts, dolphin_size, Y, flat=0.7, sides=12, n=3, mat=GOLD, name="dolphin", tip_round=0.035)]
    group.append(body([P(*q) for q in FIN], lambda s0, s1: DS * (0.07 + 0.15 * min(1.0, s1 / 0.7)), Y,
                      flat=0.22, sides=6, n=3, mat=GOLD, name="fin", tip_round=0.06))
    # flukes: two thin lobes swept up from the tail's end, lying round the
    # pavilion
    t0 = pts[0]
    T = (pts[1] - pts[0]).normalized()
    spread = T.cross(W_FRONT).normalized()
    for sgn in (-1, 1):
        lobe = [t0 + T * 0.08, t0 - T * 0.01 + spread * sgn * 0.09 * DS, t0 - T * 0.05 + spread * sgn * 0.18 * DS]
        group.append(body(lobe, lambda s0, s1: DS * (0.07 * min(1.0, s1 / 0.14) ** 0.7 + 0.004),
                          W_FRONT, flat=0.3, sides=6, n=2, mat=GOLD, name="fluke"))
    # small jet eyes either side of the head, above and behind the beak
    head = P(*SPINE[5])
    Th = (P(*SPINE[6]) - head).normalized()
    Lh = (Y - Th * Y.dot(Th)).normalized()
    Dh = Th.cross(Lh)
    for sgn in (-1, 1):
        e = head + Th * 0.14 + Dh * 0.07 * DH + Lh * sgn * 0.185 * DH
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=6, v_segments=4, radius=0.045)
        for v in bm.verts:
            v.co += e
        group.append(hk.smooth(hk._object("eye", bm, JET), 80))
    if side > 0:
        for g in group:
            mirror(g)
    parts += group

ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\VERMANA.glb",
               {"replacesTexture": "vermana_divinelodestone", "replacesPiece": "VerLode"})
classic_extents(ob)
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, (HX, HY), scale=4)
