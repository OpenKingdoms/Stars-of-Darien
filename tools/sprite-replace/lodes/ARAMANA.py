"""Aramon divine lodestone, ARALODE's big brother: a light grey stone tholos,
six piers with engaged columns carrying six open arches under a gold-trimmed
entablature and attic, crowned by a massive glowing emerald egg in a gold cup.

    blender -b --factory-startup --python tools/sprite-replace/lodes/ARAMANA.py
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import handkit as hk  # noqa: E402

NAME = "ARAMANA"
PIC = r"D:\OKReplace\lodes\sprites\ARAMANA.png"
HOT = (31, 110)
OUT = r"D:\OKReplace\lodes\hand"


def lin(rgb):
    """sRGB 0-255 to linear 0-1."""
    return tuple(((c / 255) / 12.92) if c <= 10 else (((c / 255) + 0.055) / 1.055) ** 2.4 for c in rgb)


def lathe_bm(prof, seg=6, rot=0.0, tiles=False):
    """A bmesh solid of revolution with seg flat sides from (r, z) pairs, ending
    on the axis or closed as a ring. Each facet gets u across it and v up the
    piece, and with tiles facet i takes the i-th of seg side-by-side tiles."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0.0, 0.0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(rot + 2 * math.pi * i / seg),
                                        r * math.sin(rot + 2 * math.pi * i / seg), z)) for i in range(seg)])
    z0, z1 = min(z for _, z in prof), max(z for _, z in prof)
    closed = len(rings[0]) > 1 and len(rings[-1]) > 1
    n = len(rings)
    for k in range(n if closed else n - 1):
        a, b = rings[k], rings[(k + 1) % n]
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1:
                vs, us = (a[0], b[i], b[j]), (0.5, 0.0, 1.0)
            elif len(b) == 1:
                vs, us = (a[i], a[j], b[0]), (0.0, 1.0, 0.5)
            else:
                vs, us = (a[i], a[j], b[j], b[i]), (0.0, 1.0, 1.0, 0.0)
            f = bm.faces.new(vs)
            for lp, u in zip(f.loops, us):
                u = (i + u) / seg if tiles else u
                lp[uvl].uv = (u, (lp.vert.co.z - z0) / max(1e-6, z1 - z0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def lathe(name, prof, mat, seg=6, rot=0.0, x=0.0, y=0.0):
    ob = hk._object(name, lathe_bm(prof, seg, rot), mat)
    ob.location = (x, y, 0.0)
    return ob


# the six faces of the hexagon, flat to the front: outward normals at 30, 90 ... degrees
FACES = [math.radians(30 + 60 * k) for k in range(6)]


def on_face(a, u, y, z):
    """World point u along the face whose outward normal is at angle a, y out from the centre."""
    return (-u * math.sin(a) + y * math.cos(a), u * math.cos(a) + y * math.sin(a), z)


def prism(bm, pts, y0, y1, a):
    """Adds a prism of the convex (u, z) polygon pts, from y0 to y1 out along face a."""
    A = [bm.verts.new(on_face(a, u, y0, z)) for u, z in pts]
    B = [bm.verts.new(on_face(a, u, y1, z)) for u, z in pts]
    bm.faces.new(A)
    bm.faces.new(B[::-1])
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((A[i], A[j], B[j], B[i]))


def half_ring(bm, r0, r1, zc, y0, y1, a, n=14):
    """Adds a half annulus round (0, zc) in the face plane, from y0 to y1 out along face a."""
    th = [math.pi * k / n for k in range(n + 1)]
    for t0, t1 in zip(th, th[1:]):
        pts = [(r0 * math.cos(t0), zc + r0 * math.sin(t0)), (r1 * math.cos(t0), zc + r1 * math.sin(t0)),
               (r1 * math.cos(t1), zc + r1 * math.sin(t1)), (r0 * math.cos(t1), zc + r0 * math.sin(t1))]
        prism(bm, pts, y0, y1, a)


def mesh(name, build, mat):
    bm = bmesh.new()
    build(bm)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk._object(name, bm, mat)


def box_uv(ob, k=0.9):
    """World-scale box mapping, so the masonry keeps one course height everywhere."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    mw = ob.matrix_world
    for p in me.polygons:
        n = (mw.to_3x3() @ p.normal).normalized()
        for li in p.loop_indices:
            co = mw @ me.vertices[me.loops[li].vertex_index].co
            if abs(n.z) > 0.7:
                uv.data[li].uv = (co.x * k, co.y * k)
            else:
                t = (-n.y, n.x)
                d = math.hypot(*t) or 1.0
                uv.data[li].uv = ((co.x * t[0] + co.y * t[1]) / d * k, co.z * k)


def image(name, rgb):
    """A packed image from an (h, w, 3) array of sRGB values in 0-1."""
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    img["okGenerated"] = True  # made here, so it ships (okpaint.py)
    px = np.ones((h, w, 4), np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels[:] = px.ravel()
    img.pack()
    return img


def noise(n=128, beta=2.0, seed=1):
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


def polish(m, coat=0.0, coat_rough=0.03, spec=None, rough=None):
    """A clear lacquer over a material, and optionally a stronger specular."""
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Coat Weight"].default_value = coat
    b.inputs["Coat Roughness"].default_value = coat_rough
    if spec is not None:
        b.inputs["Specular IOR Level"].default_value = spec
    if rough is not None:
        b.inputs["Roughness"].default_value = rough
    return m


def textured(name, base_img=None, base_rgb=None, rough=0.8, metal=0.0, emit_img=None, strength=0.0,
             ior=1.5, spec=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["IOR"].default_value = ior
    b.inputs["Specular IOR Level"].default_value = spec
    if base_img is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = base_img
        nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (*base_rgb, 1.0)
    if emit_img is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = emit_img
        t.extension = "EXTEND"
        nt.links.new(t.outputs["Color"], b.inputs["Emission Color"])
        b.inputs["Emission Strength"].default_value = strength
    return m


def ashlar_img(name, rgb, seed, n=256, courses=4, blocks=2, joint=3, depth=0.45, weather=0.0):
    """Dressed stone in staggered courses: each block its own shade, fine
    grain over all, darker joints between them, and with weather some dark
    staining down the blocks."""
    grain = noise(n, 2.1, seed)
    fine = noise(n, 0.8, seed + 7)
    yy, xx = np.mgrid[0:n, 0:n]
    ch, bw = n // courses, n // blocks
    course = yy // ch
    off = (course % 2) * (bw // 2)
    col = ((xx + off) % n) // bw
    tone = 0.88 + 0.22 * np.random.default_rng(seed).random((courses, blocks))
    v = (0.8 + 0.3 * grain + 0.1 * (fine - 0.5)) * tone[course, col]
    if weather:
        stain = noise(n, 2.6, seed + 11)
        v = v * (1 - weather * np.clip((stain - 0.45) / 0.4, 0, 1))
    j = ((yy % ch) < joint) | (((xx + off) % bw) < joint)
    v = np.where(j, v * (1 - depth), v)
    base = np.array(rgb, np.float32) / 255
    return image(name, v[..., None] * base[None, None, :])


def paving_img(name, rgb, seed, n=256, cells=4, joint=3):
    """Square flags for the inner floor, each its own shade."""
    grain = noise(n, 2.0, seed)
    yy, xx = np.mgrid[0:n, 0:n]
    s = n // cells
    tone = 0.9 + 0.16 * np.random.default_rng(seed).random((cells, cells))
    v = (0.86 + 0.2 * grain) * tone[np.minimum(yy // s, cells - 1), np.minimum(xx // s, cells - 1)]
    j = ((yy % s) < joint) | ((xx % s) < joint)
    v = np.where(j, v * 0.62, v)
    return image(name, v[..., None] * (np.array(rgb, np.float32) / 255)[None, None, :])


def crystal_glow(name, facets, prof, vb, cuts=(), glints=(), gain=1.0, n=96, h=256, seed=4):
    """The emerald's inner light as in ARALODE, one tile per facet: a foot to tip
    colour stepped by (below, above) at the break vb, cuts (facet, u0, u1, left,
    right) in two tones, and glints (facet, u, half width, v0, v1, peak, rgb)."""
    zs = [z for _, z in prof[1:]]
    rs = [r for r, _ in prof[1:]]
    rmax, zb, zt = max(rs), prof[0][1], prof[-1][1]
    v = ((np.arange(h) + 0.5) / h)[:, None, None]
    x = ((np.arange(n) + 0.5) / n - 0.5)[None, :, None] * rmax
    r = np.interp(zb + v * (zt - zb), zs, rs)
    u = np.clip(x / np.maximum(r, 1e-3) + 0.5, 0, 1)
    du = 1.5 * rmax / n / np.maximum(r, 1e-3)
    mottle = noise(256, 1.6, seed)
    tiles = []
    fine = noise(256, 0.4, seed + 3)
    rng = np.random.default_rng(seed + 5).random(64)
    stria = np.interp(np.arange(256) / 4.0, np.arange(64), rng)
    for i, (lo, hi, v0, v1, rim, (below, above)) in enumerate(facets):
        t = np.clip((v - v0) / (v1 - v0), 0, 1)
        t = t * t * (3 - 2 * t)
        c = (np.array(lo, np.float32) * (1 - t) + np.array(hi, np.float32) * t) / 255
        c = c * np.where(v < vb, below, above)
        # the light gathers at the point, seen from any side
        tip = np.clip((v - 0.86) / 0.14, 0, 1) ** 1.5 * 0.6
        c = c * (1 - tip) + np.array((120, 235, 104), np.float32) / 255 * tip
        if rim is not None:
            w = np.clip((u - 0.55) / 0.45, 0, 1) ** 1.5 * (v > vb)
            c = c * (1 - w) + (np.array(rim, np.float32) / 255) * w
        edge = 1.0 + 0.25 * np.abs(2 * u - 1) ** 6
        m = 0.85 + 0.3 * mottle[:h, i * 32:i * 32 + n, None]
        cols = ((np.arange(n)[None, :] * 0.5 + np.arange(h)[:, None] * 0.1).astype(int) + 40 * i) % 256
        m = m * (0.88 + 0.24 * stria[cols])[..., None]
        fl = np.clip((fine[:h, i * 32:i * 32 + n, None] - 0.8) / 0.2, 0, 1) * (v > vb * 0.6)
        c = gain * (c * edge * m + fl * np.array((24, 80, 26), np.float32) / 255)
        for f, u0, u1, left, right in cuts:
            if f == i:
                side = np.clip((u - (u0 + (u1 - u0) * v)) / du + 0.5, 0, 1)
                c = c * (left * (1 - side) + right * side)
        for f, uc, hw, g0, g1, peak, rgb in glints:
            if f != i:
                continue
            core = np.clip(1 - (np.abs(u - uc) / hw) ** 8, 0, 1)
            halo = np.exp(-((u - uc) / (3 * hw)) ** 2)
            along = np.clip((v - g0) / 0.05, 0, 1) * np.clip((g1 - v) / 0.08, 0, 1)
            c = c + halo * along * peak * np.array((10, 60, 12), np.float32) / 255
            a = peak * core * along
            c = c * (1 - a) + np.array(rgb, np.float32) / 255 * a
        tiles.append(c / np.maximum(1.0, c.max(axis=2, keepdims=True)))
    return image(name, np.concatenate(tiles, axis=1))


def edge_glow(name, verts, rings, vb, h=256, w=4):
    """The chamfers' light, a (dark, lit) column per 30 degrees: the upright edges
    brighten from the underside toward the point, the ring chamfers stay dark
    but for the break line at vb."""
    v = ((np.arange(h) + 0.5) / h)[:, None, None]
    up = np.clip((v - 0.4 * vb) / (1 - 0.4 * vb), 0, 1) ** 1.3
    brk = np.exp(-((v - vb) / 0.015) ** 2)
    cols = []
    for j in range(12):
        lo, hi = (np.array(c, np.float32) / 255 for c in (verts[j // 2] if j % 2 == 0 else rings[j // 2]))
        t = up if j % 2 == 0 else brk
        cols.append(np.broadcast_to(lo * (1 - t) + hi * t, (h, w, 3)))
    return image(name, np.concatenate(cols, axis=1))


def crystal(name, prof, body, edge, width=0.018, seg=6, rot=0.0):
    """A faceted crystal: flat facets in the body material, and every facet
    edge chamfered into a thin face of the edge material that catches light.
    Each facet's tile is the facet laid flat, u straight across it and v up."""
    bm = lathe_bm(prof, seg, rot, tiles=True)
    zb, zt = min(z for _, z in prof), max(z for _, z in prof)
    rmax = max(r for r, _ in prof)
    edges = [e for e in bm.edges
             if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(6)
             and min(v.co.z for v in e.verts) > zb + 1e-4 and max(v.co.z for v in e.verts) < zt - 1e-4]
    bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET", segments=1, profile=0.5,
                    affect="EDGES", clamp_overlap=True, material=1)
    uvl = bm.loops.layers.uv["UVMap"]
    for f in bm.faces:
        c = f.calc_center_median()
        if f.material_index == 1:
            j = round(math.degrees(math.atan2(c.y, c.x) - rot) / 30) % 12
            for lp in f.loops:
                lp[uvl].uv = ((j + 0.5) / 12, (lp.vert.co.z - zb) / (zt - zb))
        else:
            i = int(((math.atan2(c.y, c.x) - rot) % (2 * math.pi)) // (2 * math.pi / seg))
            phi = rot + (i + 0.5) * 2 * math.pi / seg
            tx, ty = -math.sin(phi), math.cos(phi)
            for lp in f.loops:
                x = lp.vert.co.x * tx + lp.vert.co.y * ty
                lp[uvl].uv = ((i + 0.5 + x / rmax) / seg, (lp.vert.co.z - zb) / (zt - zb))
    ob = hk._object(name, bm, body)
    ob.data.materials.append(edge)
    return ob


def boolean_cut(ob, cutter):
    mod = ob.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    hk._apply(ob)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return ob


def shade_recesses(ob, recess, outer=None, down=-0.3):
    """Gives the recess material to faces that look down and, with outer, to a
    ring wall's jambs and soffits, every face but its outer, inner and tops."""
    me = ob.data
    me.materials.append(recess)
    k = len(me.materials) - 1
    for p in me.polygons:
        n, c = p.normal, p.center
        dark = n.z < down
        if outer is not None and abs(n.z) < 0.7:
            a = math.atan2(c.y, c.x)
            fa = min(FACES, key=lambda f: abs(math.remainder(a - f, 2 * math.pi)))
            radial = n.x * math.cos(fa) + n.y * math.sin(fa)
            dist = c.x * math.cos(fa) + c.y * math.sin(fa)
            dark = dark or not (radial < -0.9 or (radial > 0.9 and dist > outer - 0.02))
        if dark:
            p.material_index = k


hk.reset()
# weathered Aramon stone: the family's light grey, a shade darker, deeper joints
stone = textured("ara_stone", ashlar_img("ara_stone_tex", (142, 137, 120), 3, weather=0.25), rough=0.82)
stone_pale = textured("ara_stone_pale", ashlar_img("ara_stone_pale_tex", (160, 155, 137), 6, joint=2, depth=0.3,
                                                   weather=0.15), rough=0.78)
stone_dark = textured("ara_stone_dark", ashlar_img("ara_stone_dark_tex", (100, 97, 87), 5, weather=0.3), rough=0.9)
recess = textured("ara_stone_recess", ashlar_img("ara_stone_recess_tex", (74, 72, 66), 9, depth=0.35), rough=0.92)
# the floor's flags with a green glow round its gem, one picture FLOOR_UV cells across
FLOOR_UV = 2.2
fy, fx = (np.mgrid[0:256, 0:256] + 0.5) / 256 - 0.5
fd = np.hypot(fx, fy) * FLOOR_UV
floor_glow = np.exp(-(fd / 0.42) ** 2) * 0.55 + np.exp(-((fd - 0.5) / 0.05) ** 2) * 0.25
floor_mat = textured("ara_floor", paving_img("ara_floor_tex", (150, 145, 128), 8, cells=6), rough=0.75,
                     emit_img=image("ara_floor_glow_tex", floor_glow[..., None] * np.array((60, 200, 90)) / 255),
                     strength=2.0)
# polished gold as on ARALODE, its rounded edges drawing a bright line of sun
gold = hk.pbr("ara_gold", lin((250, 206, 108)), rough=0.12, metal=1.0)
gold_dark = hk.pbr("ara_gold_dark", lin((200, 154, 70)), rough=0.2, metal=0.85)
glow_floor = hk.pbr("ara_floor_glow", lin((20, 90, 40)), rough=0.2, emit=lin((60, 200, 90)), strength=1.2)

parts, stones = [], []

# ---- plan and heights, in cells --------------------------------------------
RO, RI = 1.33, 1.0             # the arcade wall's outer and inner corner radius
AO, AI = RO * math.cos(math.pi / 6), RI * math.cos(math.pi / 6)
RF = 1.15                      # the dark recessed foot
ZF = 0.3                       # the floor, on top of the foot
HW = 0.4                       # half the arch opening
ZS = 2.62                      # the arches spring here
ZW = 3.35                      # the wall top, under the architrave
RA = 1.40                      # architrave, over the corner columns
RC = 1.46                      # cornice
ZC = ZW + 0.66                 # cornice top
RD = 1.27                      # attic
ZD = 5.25                      # attic top, under its cap
RDC = 1.33                     # attic cap
ZR = 5.52                      # roof top, where the neck stands

# ---- the dark recessed foot and the paved floor ----------------------------------
foot = lathe("foot", [(0, 0), (RF, 0), (RF, ZF - 0.02), (0, ZF - 0.02)], stone_dark)
hk.bevel(foot, 0.02, 1)
stones.append(foot)
flr = lathe("floor", [(0, ZF - 0.03), (RI + 0.04, ZF - 0.03), (RI + 0.04, ZF + 0.012), (0, ZF + 0.012)], floor_mat)
parts.append(lathe("inlay", [(0.46, ZF + 0.005), (0.55, ZF + 0.005), (0.55, ZF + 0.02), (0.46, ZF + 0.02)], gold))
parts.append(lathe("floor_gem", [(0, ZF + 0.005), (0.2, ZF + 0.005), (0.16, ZF + 0.035), (0, ZF + 0.035)],
                   glow_floor))

# ---- the arcade: a hexagonal ring wall with a round arch through each face ----------
wall = hk._object("wall", lathe_bm([(RI, ZF - 0.01), (RO, ZF - 0.01), (RO, ZW), (RI, ZW)]), stone)
arch_pts = [(-HW, ZF - 0.2), (HW, ZF - 0.2)] + [(HW * math.cos(t), ZS + HW * math.sin(t))
                                                for t in (math.pi * k / 16 for k in range(17))]
cutter = mesh("cutter", lambda bm: [prism(bm, arch_pts, AI - 0.3, AO + 0.3, a) for a in FACES], None)
boolean_cut(wall, cutter)
stones.append(wall)
shade_recesses(wall, recess, outer=AO)


def arch_trim(bm):
    for a in FACES:
        half_ring(bm, HW, HW + 0.09, ZS, AO - 0.01, AO + 0.04, a)
        for s in (-1, 1):
            # the frame runs down both jambs, with an impost block at the springing
            prism(bm, [(s * HW, ZF), (s * (HW + 0.09), ZF), (s * (HW + 0.09), ZS), (s * HW, ZS)][::s],
                  AO - 0.01, AO + 0.04, a)
            prism(bm, [(s * (HW - 0.02), ZS - 0.09), (s * (HW + 0.13), ZS - 0.09), (s * (HW + 0.13), ZS),
                       (s * (HW - 0.02), ZS)][::s], AO - 0.01, AO + 0.07, a)


trim_arch = mesh("arch_trim", arch_trim, stone_pale)
stones.append(trim_arch)


def keystones(bm):
    zc = ZS + HW
    for a in FACES:
        prism(bm, [(-0.06, zc - 0.03), (0.06, zc - 0.03), (0.09, zc + 0.17), (-0.09, zc + 0.17)], AO - 0.01, AO + 0.08, a)


parts.append(mesh("keystones", keystones, gold))

# slender engaged columns on the six outer corners, gold necking under each capital
RCOL = RO - 0.05
for k in range(6):
    c = math.radians(60 * k)
    x, y = RCOL * math.cos(c), RCOL * math.sin(c)
    col = lathe("column", [(0, ZF + 0.06), (0.158, ZF + 0.06), (0.158, ZF + 0.14), (0.15, ZF + 0.19),
                           (0.145, ZF + 0.25), (0.14, ZW - 0.24), (0.14, ZW - 0.2), (0.17, ZW - 0.08),
                           (0.18, ZW), (0, ZW)], stone_pale, seg=12, x=x, y=y)
    stones.append(col)
    necking = [(0.13, ZW - 0.24), (0.15, ZW - 0.24)]
    necking += [(0.145 + 0.02 * math.cos(t), ZW - 0.21 + 0.025 * math.sin(t)) for t in
                (math.radians(v) for v in (-60, 0, 60))]
    necking += [(0.13, ZW - 0.18)]
    parts.append(lathe("necking", necking, gold, seg=12, x=x, y=y))

# ---- entablature: architrave, a shadowed frieze with a gold band, cornice with a gold bead
arch_band = lathe("architrave", [(0, ZW - 0.02), (RA, ZW - 0.02), (RA, ZW + 0.2), (0, ZW + 0.2)], stone_pale)
frieze = lathe("frieze", [(0, ZW + 0.19), (RA - 0.06, ZW + 0.19), (RA - 0.06, ZW + 0.41), (0, ZW + 0.41)],
               stone_dark)
cornice = lathe("cornice", [(0, ZW + 0.4), (RA - 0.06, ZW + 0.4), (RC, ZW + 0.52), (RC, ZC - 0.03),
                            (RC - 0.03, ZC), (RD + 0.02, ZC + 0.06), (0, ZC + 0.06)], stone_pale)
for p in (arch_band, frieze, cornice):
    hk.bevel(p, 0.02, 1)
stones += [arch_band, frieze, cornice]
shade_recesses(cornice, recess)
parts.append(lathe("frieze_band", [(RA - 0.07, ZW + 0.37), (RA - 0.04, ZW + 0.37), (RA - 0.04, ZW + 0.41),
                                   (RA - 0.07, ZW + 0.41)], gold))
bead = [(RC - 0.1, ZC - 0.012), (RC - 0.02, ZC - 0.012)]
bead += [(RC - 0.02 + 0.03 * math.cos(t), ZC + 0.018 + 0.03 * math.sin(t))
         for t in (math.radians(a) for a in (-60, -20, 20, 60, 100))]
bead += [(RC - 0.1, ZC + 0.03)]
trim = lathe("cornice_bead", bead, gold)
parts.append(trim)

# ---- attic with a carved gold panel on each face, its cap, a stepped roof ------------
attic = lathe("attic", [(0, ZC - 0.01), (RD, ZC - 0.01), (RD, ZD), (0, ZD)], stone)
attic_cap = lathe("attic_cap", [(0, ZD - 0.01), (RD, ZD - 0.01), (RDC, ZD + 0.05), (RDC, ZD + 0.13),
                                (RDC - 0.03, ZD + 0.15), (0, ZD + 0.15)], stone_pale)
roof1 = lathe("roof1", [(0, ZD + 0.14), (1.12, ZD + 0.14), (1.12, ZD + 0.22), (0, ZD + 0.22)], stone)
roof2 = lathe("roof2", [(0, ZD + 0.21), (0.9, ZD + 0.21), (0.9, ZR - 0.02), (0.87, ZR), (0, ZR)], stone)
for p in (attic, attic_cap, roof1, roof2):
    hk.bevel(p, 0.02, 1)
stones += [attic, attic_cap, roof1, roof2]
shade_recesses(attic_cap, recess)

ZM = (ZC + 0.06 + ZD) / 2   # the attic panels' middle
AP = RD * math.cos(math.pi / 6)


def panels(bm):
    for a in FACES:
        prism(bm, [(-0.36, ZM - 0.3), (0.36, ZM - 0.3), (0.36, ZM + 0.3), (-0.36, ZM + 0.3)], AP - 0.01, AP + 0.015, a)


def panel_frames(bm):
    for a in FACES:
        for u0, u1, z0, z1 in ((-0.4, 0.4, ZM + 0.29, ZM + 0.34), (-0.4, 0.4, ZM - 0.34, ZM - 0.29),
                               (-0.4, -0.35, ZM - 0.29, ZM + 0.29), (0.35, 0.4, ZM - 0.29, ZM + 0.29)):
            prism(bm, [(u0, z0), (u1, z0), (u1, z1), (u0, z1)], AP - 0.01, AP + 0.04, a)
        for uc, s in ((0.0, 0.14), (-0.2, 0.08), (0.2, 0.08)):
            prism(bm, [(uc, ZM - s), (uc + s, ZM), (uc, ZM + s), (uc - s, ZM)], AP, AP + 0.06, a)


parts.append(mesh("attic_panels", panels, gold_dark))
parts.append(mesh("attic_frames", panel_frames, gold))

# a small stone pinnacle with a gold point on the cornice over each corner column
RP = RC - 0.13
for k in range(6):
    c = math.radians(60 * k)
    x, y = RP * math.cos(c), RP * math.sin(c)
    pin = lathe("pinnacle", [(0, ZC - 0.02), (0.075, ZC - 0.02), (0.075, ZC + 0.07), (0.055, ZC + 0.09),
                             (0.055, ZC + 0.33), (0.075, ZC + 0.36), (0.075, ZC + 0.42), (0, ZC + 0.42)],
                stone_pale, seg=6, x=x, y=y)
    hk.bevel(pin, 0.01, 1)
    stones.append(pin)
    parts.append(lathe("pinnacle_tip", [(0, ZC + 0.41), (0.06, ZC + 0.41), (0.0, ZC + 0.7)], gold, seg=6, x=x, y=y))

# ---- the neck on the roof: a gold foot, a stone shaft banded in gold ----------------
ZN = 6.74                      # the neck's top, the cup's foot
neck_foot = lathe("neck_foot", [(0, ZR - 0.01), (0.52, ZR - 0.01), (0.52, ZR + 0.06), (0.46, ZR + 0.1),
                                (0.4, ZR + 0.18), (0.35, ZR + 0.3), (0.33, ZR + 0.36), (0, ZR + 0.36)], gold)
parts.append(neck_foot)
neck = lathe("neck", [(0, ZR + 0.3), (0.31, ZR + 0.3), (0.28, ZR + 0.5), (0.26, ZN - 0.4), (0.3, ZN - 0.22),
                      (0.3, ZN), (0, ZN)], stone_pale)
hk.bevel(neck, 0.015, 1)
stones.append(neck)
for zb_, r_ in ((ZR + 0.62, 0.28), (ZN - 0.1, 0.32)):
    ring = [(r_ - 0.05, zb_), (r_, zb_)]
    ring += [(r_ + 0.03 * math.cos(t), zb_ + 0.035 + 0.035 * math.sin(t)) for t in
             (math.radians(a) for a in (-60, -20, 20, 60))]
    ring += [(r_ - 0.05, zb_ + 0.07)]
    parts.append(lathe("neck_band", ring, gold))

# ---- the divine lodestone: a massive emerald egg with a six-sided pyramid point ------
# a corner to the front. The dark rounded underside narrows into the cup, and
# the break where the point meets the body is a crease lit along its edge
ZE, ZB, ZT = 7.03, 9.9, 11.6
CRYSTAL = [(0, ZE), (0.5, 7.12), (0.88, 7.45), (1.02, 7.76), (1.07, 8.1), (1.03, 8.5), (0.9, 9.2), (0.72, ZB),
           (0, ZT)]
ROT = math.pi / 6
VB = (ZB - ZE) / (ZT - ZE)
# facets counted from the back right round to the right: the front left one
# is the lit facet, the right one the shadow with a cool rim. The pair after
# the rim is the tone below and above the break
FIRE = [
    ((2, 7, 4), (44, 128, 58), 0.2, 0.95, None, (0.85, 1.05)),           # back right
    ((2, 7, 4), (40, 118, 54), 0.2, 0.95, None, (0.85, 1.05)),           # back left
    ((2, 10, 4), (60, 150, 64), 0.22, 0.95, None, (0.8, 1.15)),          # left
    ((3, 14, 6), (124, 238, 104), 0.36, 0.85, None, (0.55, 1.3)),        # front left, the lit facet
    ((2, 10, 5), (54, 136, 62), 0.3, 0.95, None, (0.68, 1.15)),          # front right
    ((3, 10, 8), (40, 136, 92), 0.2, 0.95, (18, 56, 104), (0.9, 1.05)),  # right, shadow with a cool rim
]
# the light the cut faces throw back, on the point: a broad streak down the
# lit facet by the front edge, thinner ones beside it, each a near-white core
# in a green bloom, and a few small glints low on the body
GLINTS = [
    (3, 0.74, 0.06, VB + 0.02, 0.97, 1.0, (240, 255, 232)),
    (3, 0.42, 0.018, VB + 0.1, 0.9, 0.85, (215, 255, 205)),
    (4, 0.2, 0.025, VB + 0.05, 0.95, 0.7, (200, 255, 190)),
    (4, 0.72, 0.012, VB + 0.2, 0.9, 0.5, (170, 245, 165)),
    (2, 0.8, 0.03, VB + 0.1, 0.97, 0.6, (200, 255, 190)),
    (5, 0.25, 0.02, VB + 0.1, 0.9, 0.45, (130, 220, 210)),
    (3, 0.6, 0.02, 0.36, VB - 0.03, 0.5, (160, 245, 160)),
    (4, 0.3, 0.015, 0.4, VB - 0.04, 0.35, (130, 230, 140)),
]
# the facets behind, seen through the stone, split each face in two tones
CUTS = [
    (2, 0.4, 0.5, 0.85, 1.15),
    (3, 0.3, 0.5, 1.1, 0.8),
    (4, 0.45, 0.5, 1.2, 0.8),
    (5, 0.3, 0.45, 1.25, 0.9),
]
# the crystal's materials carry "crystal" in their names: the game pulses their glow
emerald = polish(textured("aramana_crystal", base_rgb=lin((5, 24, 11)), rough=0.01, ior=1.58, spec=1.0,
                          emit_img=crystal_glow("aramana_crystal_fire", FIRE, CRYSTAL, VB, CUTS, GLINTS),
                          strength=1.8), coat=1.0, coat_rough=0.0)
# upright edges corner by corner from the back right, then the break line facet by facet
EDGE_VERTS = [((6, 20, 16), (110, 180, 160)), ((6, 20, 16), (110, 180, 160)), ((10, 30, 20), (170, 230, 170)),
              ((16, 46, 24), (220, 255, 210)), ((20, 60, 30), (240, 255, 235)), ((12, 36, 24), (190, 245, 185))]
EDGE_RINGS = [((6, 18, 12), (120, 190, 150)), ((6, 18, 12), (120, 190, 150)), ((10, 30, 18), (190, 245, 185)),
              ((14, 40, 20), (240, 255, 230)), ((12, 36, 20), (215, 255, 205)), ((8, 22, 26), (130, 200, 190))]
emerald_edge = polish(textured("aramana_crystal_edge", base_rgb=lin((16, 40, 24)), rough=0.01, spec=1.0,
                               emit_img=edge_glow("aramana_crystal_edge_glow", EDGE_VERTS, EDGE_RINGS, VB),
                               strength=1.3), coat=1.0, coat_rough=0.0)
spike = crystal("crystal", CRYSTAL, emerald, emerald_edge, rot=ROT)
parts.append(spike)

# the gold cup the egg sits in, about 0.9 across, with six short claws on it
cup = [(0, ZN - 0.02), (0.28, ZN - 0.02), (0.33, ZN + 0.06), (0.4, ZN + 0.2), (0.45, ZN + 0.3),
       (0.47, ZN + 0.34), (0.44, ZN + 0.36), (0, ZN + 0.36)]
cup_ob = lathe("cup", cup, gold, rot=ROT)
parts.append(cup_ob)
CLAW = (ZN + 0.04, 7.34)       # the claws run from low on the cup to just over the egg's foot


def prongs(bm):
    # up the cup's corners, under the egg's corner edges
    for k in range(6):
        z0, z1 = CLAW
        prism(bm, [(-0.055, z0), (0.055, z0), (0.045, z1 - 0.08), (0.0, z1), (-0.045, z1 - 0.08)], 0.3, 0.36,
              ROT + math.radians(60 * k))


claws = mesh("claws", prongs, gold)
for v in claws.data.vertices:
    # each claw leans out along the cup and over the egg's underside
    a = ROT + round((math.atan2(v.co.y, v.co.x) - ROT) / (math.pi / 3)) * (math.pi / 3)
    f = min(1.0, max(0.0, (v.co.z - CLAW[0]) / (CLAW[1] - CLAW[0])))
    push = -0.06 * f + 0.55 * f * f
    v.co.x += push * math.cos(a)
    v.co.y += push * math.sin(a)
parts.append(claws)

bpy.context.view_layer.update()
for p in stones:
    box_uv(p)
# the floor's flags and glow are one picture over the whole floor
uv = flr.data.uv_layers.get("UVMap") or flr.data.uv_layers.new(name="UVMap")
for lp in flr.data.loops:
    co = flr.data.vertices[lp.vertex_index].co
    uv.data[lp.index].uv = (0.5 + co.x / FLOOR_UV, 0.5 + co.y / FLOOR_UV)
stones.append(flr)
for p in parts:
    if p is not spike and p.data.uv_layers.get("UVMap") is None:
        box_uv(p)
for p in stones + parts:
    if p is not spike:  # the crystal stays flat shaded, every facet crisp
        hk.smooth(p, 35)
hk.smooth(trim, 80)

ob = hk.finish(stones + parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "aramanadivinelodestone", "replacesPiece": "aramana"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, PIC, HOT, scale=4)
