"""Veruna mana lodestone: a green marble column, fluted, with a draped
collar, carrying a gold collar and two swept gold horns that cradle a large
blue brilliant. Built to the picture's classic silhouette.

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


def sweep(pts, radius, sides=10, n=3, mat=None, name="horn", squash=1.0):
    """A tube along a smooth curve through pts; radius(t) sets its section,
    squash flattens it along the curve's bend. Closed at the root, pointed
    at the tip."""
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    c = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for j in range(n):
            t = j / n
            c.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                            + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t))
    c.append(pts[-1].copy())
    L = [0.0]
    for a, b in zip(c, c[1:]):
        L.append(L[-1] + (b - a).length)
    bm = bmesh.new()
    rings = []
    N = (c[1] - c[0]).normalized().orthogonal().normalized()
    for i, p in enumerate(c[:-1]):
        T = (c[i + 1] - c[max(i - 1, 0)]).normalized()
        N = (N - T * N.dot(T)).normalized()
        B = T.cross(N)
        r = radius(L[i] / L[-1])
        rings.append([bm.verts.new(p + (N * math.cos(2 * math.pi * k / sides) * squash
                                         + B * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)])
    tip = bm.verts.new(c[-1])
    for A, Bq in zip(rings, rings[1:]):
        for k in range(sides):
            bm.faces.new((A[k], A[(k + 1) % sides], Bq[(k + 1) % sides], Bq[k]))
    for k in range(sides):
        bm.faces.new((rings[-1][k], rings[-1][(k + 1) % sides], tip))
    bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk.smooth(hk._object(name, bm, mat), 70)


def brilliant(R, crown, pav, tilt, centre, seg, mats):
    """A round brilliant, each ring turned half a facet from the last, built
    table up and tipped by tilt about x (negative leans the table back).
    mats: deep, mid, the bright top-left crown facets, two pavilion blues."""
    rings = [(0.6 * R, -pav * 0.42, 0.5), (R, -0.035, 0.0), (R, 0.035, 0.0),
             (0.84 * R, crown * 0.5, 0.5), (0.53 * R, crown, 0.0)]
    bm = bmesh.new()
    vr = [[bm.verts.new((r * math.cos(2 * math.pi * (k + h) / seg), r * math.sin(2 * math.pi * (k + h) / seg), z))
           for k in range(seg)] for r, z, h in rings]
    tip = bm.verts.new((0.0, 0.0, -pav))
    faces = [(bm.faces.new(vr[-1]), 0, None)]
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
        if z is None:
            f.material_index = 0
            continue
        cx = sum(v.co.x for v in f.verts) / len(f.verts)
        cy_ = sum(v.co.y for v in f.verts) / len(f.verts)
        cz = sum(v.co.z for v in f.verts) / len(f.verts)
        ang = math.degrees(math.atan2(cy_, cx))
        if z > 0.05 and 112 < ang < 160:
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
MARBLE_LT = textured("ver_marble_pale", marble_img("ver_marble_pale_tex", (132, 180, 144), (184, 218, 190),
                                                   (88, 122, 98), 11), 0.34)
PALE = hk.pbr("ver_marble_ring", lin(212, 234, 214), rough=0.28)
GOLD = hk.pbr("ver_gold", lin(255, 222, 105), rough=0.18, metal=0.6)
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

parts = []

# the column: a base moulding, a deeply fluted shaft, a core under the drape
# and a capital, standing CY behind the anchor
FLUTES = 12
SHAFT = (0.42, 4.72)


def flute(a, z):
    if SHAFT[0] < z < SHAFT[1]:
        return 1.0 - 0.19 * (0.5 + 0.5 * math.cos(FLUTES * a)) ** 1.5
    return 1.0


prof = [(0.0, 0.0), (0.7, 0.0), (0.735, 0.06), (0.735, 0.13), (0.7, 0.19), (0.64, 0.24), (0.655, 0.3),
        (0.64, 0.36), (0.63, SHAFT[0]), (0.63, 1.5), (0.63, 2.6), (0.63, 3.7), (0.63, SHAFT[1]),
        (0.66, 4.77), (0.62, 4.82), (0.6, 5.0), (0.6, 5.9), (0.62, 6.0), (0.62, 6.1), (0.0, 6.1)]
parts.append(hk.smooth(cyl_uv(lathe(prof, 48, MARBLE, "column", rmod=flute), CY), 50))

# the drape: pale marble cloth hung in four swags, the front one full on,
# its folds twisting round the column
K, S = 48, 7
Z_TOP = 5.96
rows = [[(0.6 * math.cos(2 * math.pi * k / K), CY + 0.6 * math.sin(2 * math.pi * k / K), Z_TOP)
         for k in range(K)]]
for s in range(S + 2):
    ring = []
    for k in range(K):
        a = 2 * math.pi * k / K
        z_low = 4.76 + 0.46 * (1 - abs(math.cos(2 * a)) ** 0.8)
        t = min(1.0, s / S)
        z = Z_TOP - t * (Z_TOP - z_low)
        if s == S + 1:
            r = 0.6
        else:
            # the cloth swells out and falls in soft folds that follow the hem
            r = (0.6 + 0.08 + 0.07 * math.sin(math.pi * min(1.0, 0.2 + t)) + 0.035 * math.sin(3.2 * math.pi * t)
                 + 0.02 * math.cos(10 * a - 4 * z))
        ring.append((r * math.cos(a), CY + r * math.sin(a), z))
    rows.append(ring)
drape = rings_mesh(rows, MARBLE_LT, "drape", cap_bottom=False, cap_top=False)
parts.append(hk.smooth(cyl_uv(drape, CY), 60))

# a thin pale ring on the drape's top, under the gold
parts.append(hk.smooth(lathe([(0.6, 5.92), (0.68, 5.95), (0.7, 6.02), (0.68, 6.09), (0.6, 6.12)],
                             32, PALE, "ring"), 50))

# the gold collar on the capital
parts.append(hk.smooth(lathe([(0.0, 6.06), (0.5, 6.06), (0.57, 6.12), (0.58, 6.24), (0.52, 6.34),
                              (0.36, 6.42), (0.0, 6.44)], 28, GOLD, "collar"), 50))

# the brilliant, tipped back from the classic camera as the painting has
# it: the table high in the outline, the pavilion's point low; it sits over
# the column and its pavilion drops into claws rising from the collar
R, CROWN, PAV, TILT = 0.8, 0.4, 1.5, math.radians(-15)
n = Vector((0.0, -math.sin(TILT), math.cos(TILT)))
GEM_MATS = (SAPPHIRE, SAPPHIRE_MID, SAPPHIRE_LT, SAPPHIRE_PAV, SAPPHIRE_PAV2)
probe = brilliant(R, CROWN, PAV, TILT, Vector((0.0, 0.0, 0.0)), 16, GEM_MATS)
top_px = max(16 * v.co.y + 8 * v.co.z for v in probe.data.vertices)
bpy.data.objects.remove(probe, do_unlink=True)
gy = CY + 0.12
gz = (HY - 24.3 - top_px - 16 * gy) / 8
G = Vector((0.0, gy, gz))
parts.append(brilliant(R, CROWN, PAV, TILT, G, 16, GEM_MATS))
tip = G - n * PAV
print("GEM centre", tuple(round(c, 3) for c in G), "tip", tuple(round(c, 3) for c in tip))


def pav_r(d):
    """The pavilion's radius a distance d below the girdle."""
    if d < PAV * 0.42:
        return R + (0.6 * R - R) * d / (PAV * 0.42)
    return 0.6 * R * (PAV - d) / (PAV * 0.58)


# a short stem to the pavilion's point and four claws gripping its sides
parts.append(sweep([Vector((0.0, CY, 6.3)), Vector((0.0, CY - 0.03, 6.7)), Vector((0.0, (CY + tip.y) / 2, 6.95)),
                    tip + n * 0.18],
                   lambda t: 0.2 - 0.12 * t + 0.06 * max(0.0, t - 0.75) / 0.25, sides=10, n=3, mat=GOLD,
                   name="stem"))
for a in (math.radians(d) for d in (45, 135, 225, 315)):
    u = Vector((math.cos(a), math.sin(a), 0.0))
    w = (u - n * u.dot(n)).normalized()
    dd = PAV * 0.5
    grip = G - n * dd + w * (pav_r(dd) + 0.03)
    root = Vector((0.3 * math.cos(a), CY + 0.3 * math.sin(a), 6.4))
    mid = root.lerp(grip, 0.45) + u * 0.08
    top = G - n * (dd - 0.32) + w * (pav_r(dd - 0.32) + 0.03)
    parts.append(sweep([root, mid, grip, top], lambda t: 0.085 - 0.045 * t, sides=6, n=3, mat=GOLD,
                       name="claw"))

# the horns: swept gold, round in section, from the collar out and forward
# along the painted crescent, the upper tip curling in toward the gem; the
# outer fin point is a spur off the lower bend
HORN = [(26.5, 60.5, 0.12), (23.0, 60.0, 0.0), (19.0, 58.5, -0.15), (15.5, 56.0, -0.28), (12.2, 52.8, -0.38),
        (10.2, 49.0, -0.42), (9.8, 45.5, -0.4), (10.3, 42.0, -0.32), (11.4, 38.8, -0.2), (13.2, 36.0, -0.08),
        (15.3, 34.2, 0.02), (17.0, 33.6, 0.1), (18.6, 34.6, 0.15)]
FIN = [(13.0, 52.0, -0.4), (9.0, 51.4, -0.38), (5.0, 51.6, -0.35), (0.3, 52.5, -0.3)]


def horn_r(t):
    prof = [(0.0, 0.2), (0.14, 0.25), (0.32, 0.3), (0.48, 0.34), (0.62, 0.3), (0.78, 0.21), (0.9, 0.11),
            (1.0, 0.012)]
    for (t0, r0), (t1, r1) in zip(prof, prof[1:]):
        if t <= t1:
            return r0 + (r1 - r0) * (t - t0) / (t1 - t0)
    return prof[-1][1]


for side in (-1, 1):
    h = sweep([P(*q) for q in HORN], horn_r, sides=10, n=3, mat=GOLD, name="horn")
    f = sweep([P(*q) for q in FIN], lambda t: 0.19 * (1 - t) ** 0.7 + 0.015, sides=8, n=3, mat=GOLD,
              name="fin")
    if side > 0:
        mirror(h)
        mirror(f)
    parts += [h, f]

ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\VERMANA.glb",
               {"replacesTexture": "vermana_divinelodestone", "replacesPiece": "VerLode"})
classic_extents(ob)
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, (HX, HY), scale=4)
