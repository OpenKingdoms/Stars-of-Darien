"""Parametric builders for the Zhon structures family, run inside Blender.

Frame as handkit: one unit is one map cell, -Y toward the classic camera,
Z up, the origin at the feature's anchor. Every builder takes a dict of
parameters and returns the list of objects for handkit.finish.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

TOOLS = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)
import handkit as hk  # noqa: E402

GAIN = 1.2  # the sprite has its light painted in; the render lights it again


def lin(c, gain=GAIN):
    return tuple(min(1.0, v / 255.0 * gain) ** 2.2 for v in c)


class Build:
    """Geometry gathered into one bmesh per material, made into objects at the end."""

    def __init__(self):
        self.parts = {}
        self.mats = {}
        self.extra = []
        self.sheets = set()  # materials whose open sheets are wound by hand
        self.speckle = {}  # material: (second material, share, seed), faces split between the two
        self.tops = {}  # material: (top material, least normal z), upward faces take the top material
        self.flat = set()  # materials shaded faceted, whatever the model's smoothing

    def mat(self, name, rgb, rough=0.85, metal=0.0, gain=GAIN):
        if name not in self.mats:
            self.mats[name] = hk.pbr(name, lin(rgb, gain), rough, metal)
        return name

    def bm(self, m):
        if m not in self.parts:
            self.parts[m] = bmesh.new()
        return self.parts[m]

    def objects(self, smooth_angle=50):
        out = []
        for m, bm in self.parts.items():
            if not bm.verts:
                bm.free()
                continue
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
            if m not in self.sheets:
                bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            ob = hk._object(m, bm, self.mats[m])
            if m in self.speckle:
                # (second material, share, seed), or (seed, [(material, share), ...]) for several
                sp = self.speckle[m]
                seed, mix = (sp[2], [sp[:2]]) if len(sp) == 3 else sp
                for m2, _ in mix:
                    ob.data.materials.append(self.mats[m2])
                rng = random.Random(seed)
                for poly_ in ob.data.polygons:
                    r, idx = rng.random(), 0
                    for k, (_, share) in enumerate(mix):
                        r -= share
                        if r < 0:
                            idx = k + 1
                            break
                    poly_.material_index = idx
            if m in self.tops:
                m2, nz = self.tops[m]
                ob.data.materials.append(self.mats[m2])
                for poly_ in ob.data.polygons:
                    poly_.material_index = 1 if poly_.normal.z > nz else 0
            hk.smooth(ob, 1 if m in self.flat else smooth_angle)
            out.append(ob)
        return out + self.extra


# ---------------------------------------------------------------- primitives

def _xf(verts, M):
    for v in verts:
        v.co = M @ v.co


def box(bm, c, s, rot=(0.0, 0.0, 0.0), base=True):
    """A box of size s; c is its bottom centre (base) or its centre, turned by rot (radians, XYZ)."""
    r = bmesh.ops.create_cube(bm, size=1.0)
    M = Matrix.Translation(Vector(c)) @ Matrix.Rotation(rot[2], 4, "Z") @ Matrix.Rotation(rot[1], 4, "Y") \
        @ Matrix.Rotation(rot[0], 4, "X") @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
    if base:
        M = M @ Matrix.Translation((0, 0, 0.5))
    _xf(r["verts"], M)
    return r["verts"]


def aabb(bm, x0, x1, y0, y1, z0, z1):
    return box(bm, ((x0 + x1) / 2, (y0 + y1) / 2, z0), (x1 - x0, y1 - y0, z1 - z0))


def tube(bm, p0, p1, r0, r1, seg=8, cap=True, twist=0.0):
    """A tapered log from p0 to p1."""
    p0, p1 = Vector(p0), Vector(p1)
    ax = p1 - p0
    L = ax.length
    r = bmesh.ops.create_cone(bm, cap_ends=cap, segments=seg, radius1=r0, radius2=max(r1, 1e-4), depth=L)
    q = ax.normalized().to_track_quat("Z", "Y")
    M = Matrix.Translation(p0 + ax / 2) @ q.to_matrix().to_4x4() @ Matrix.Rotation(twist, 4, "Z")
    _xf(r["verts"], M)
    return r["verts"]


def lathe(bm, cx, cy, profile, seg=16, wobble=None, phase=0.0, bottom=True, top=True):
    """A closed solid of revolution: profile is (radius, z) from bottom to top;
    wobble(angle, z) scales the radius, for lumps and flutes. bottom and top
    False leave those ends open where nothing can see them."""
    rings = []
    for rr, z in profile:
        if rr <= 1e-4:
            rings.append([bm.verts.new((cx, cy, z))])
            continue
        ring = []
        for i in range(seg):
            a = phase + 2 * math.pi * i / seg
            k = wobble(a, z, i) if wobble else 1.0
            ring.append(bm.verts.new((cx + math.cos(a) * rr * k, cy + math.sin(a) * rr * k, z)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            q = [a[i % len(a)], a[j % len(a)], b[j % len(b)], b[i % len(b)]]
            q = [v for k, v in enumerate(q) if v not in q[:k]]
            if len(q) >= 3:
                bm.faces.new(q)
    if len(rings[0]) > 1 and bottom:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1 and top:
        bm.faces.new(rings[-1])
    return rings


def chip(bm, c, s, rng):
    """A splinter or clod lying on the ground: a squashed tetrahedron."""
    a = rng.uniform(0, 6.28)
    pts = [(c[0] + math.cos(a + k * 2.1) * s * rng.uniform(0.6, 1.3), c[1] + math.sin(a + k * 2.1) * s * rng.uniform(0.6, 1.3),
            0.0) for k in range(3)]
    pts.append((c[0], c[1], s * rng.uniform(0.3, 0.7)))
    return poly(bm, pts, [(0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3)])


def rock(bm, c, radii, rng, sub=1, rough=0.22, flat=0.0, rot=None):
    """A lumpy boulder: an icosphere pushed about, flattened underneath by flat."""
    r = bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    ph = [rng.uniform(0, 6.28) for _ in range(6)]
    fr = [rng.uniform(1.5, 3.2) for _ in range(3)]
    rot = rng.uniform(0, 6.28) if rot is None else rot
    for v in r["verts"]:
        x, y, z = v.co
        n = 1.0 + rough * (math.sin(fr[0] * x + ph[0]) * math.cos(fr[1] * y + ph[1]) * 0.6
                           + math.sin(fr[2] * z + ph[2]) * 0.4 + rng.uniform(-0.3, 0.3))
        z = max(z, -1.0 + flat * 2) if flat else z
        v.co = Vector((x * n * radii[0], y * n * radii[1], z * n * radii[2]))
    M = Matrix.Translation(Vector(c)) @ Matrix.Rotation(rot, 4, "Z")
    _xf(r["verts"], M)
    return r["verts"]


def poly(bm, pts, faces):
    vs = [bm.verts.new(p) for p in pts]
    for f in faces:
        try:
            bm.faces.new([vs[i] for i in f])
        except ValueError:
            pass
    return vs


def grid_surface(bm, fn, nu, nv, closed_u=False):
    """A sheet from fn(u, v) -> (x, y, z), u and v in 0..1."""
    cols = nu if closed_u else nu + 1
    vs = [[bm.verts.new(fn(i / nu, j / nv)) for i in range(cols)] for j in range(nv + 1)]
    for j in range(nv):
        for i in range(nu):
            i2 = (i + 1) % cols if closed_u else i + 1
            bm.faces.new((vs[j][i], vs[j][i2], vs[j + 1][i2], vs[j + 1][i]))
    return vs


def stairs(bm, x0, x1, y_bottom, y_top, z_bottom, z_top, n, back_to=None):
    """A flight rising from y_bottom (low) to y_top (high) between x0 and x1:
    n solid steps, each running back to back_to (default y_top)."""
    back = y_top if back_to is None else back_to
    for i in range(n):
        t0 = i / n
        ys = y_bottom + (y_top - y_bottom) * t0
        zt = z_bottom + (z_top - z_bottom) * (i + 1) / n
        lo, hi = min(ys, back), max(ys, back)
        aabb(bm, x0, x1, lo, hi, z_bottom, zt)


def stairs_x(bm, y0, y1, x_bottom, x_top, z_bottom, z_top, n):
    """A flight rising along x from x_bottom to x_top between y0 and y1."""
    for i in range(n):
        xs = x_bottom + (x_top - x_bottom) * i / n
        zt = z_bottom + (z_top - z_bottom) * (i + 1) / n
        lo, hi = min(xs, x_top), max(xs, x_top)
        aabb(bm, lo, hi, y0, y1, z_bottom, zt)


def sturdy(p):
    """The parameters with the sturdiness pass applied: p["sturdy"] = (girth,
    height) scales what makes a tall thing thick and what makes it tall, so a
    pixel-exact fit to the half-height classic picture does not stand spindly."""
    g, h = p.get("sturdy", (1.0, 1.0))
    if g == 1.0 and h == 1.0:
        return p
    q = dict(p)
    kind = p["kind"]
    if kind == "termite_mound":
        q["h"] = p["h"] * h
        for k in ("r", "cr", "ar", "ary"):  # not sr: each pipe keeps the sprite's column width
            if k in p:
                q[k] = p[k] * g
    elif kind == "thatch_hut":
        q["rw"], q["wh"], q["za"] = p["rw"] * g, p["wh"] * h, p["za"] * h
        q["tiers"] = [(r * g, z * h) for r, z in p["tiers"]]
        q["door_h"] = p.get("door_h", 1.5) * h
    elif kind == "hide_tent":
        cx, cy, rz, rr = p["ring"]
        q["ring"] = (cx, cy, rz * h, rr)
        if p.get("porch"):
            ax, ay, az = p["porch"]["apex"]
            q["porch"] = dict(p["porch"], apex=(ax, ay, az * h))
    elif kind == "palisade":
        q["h"] = p["h"] * h
        q["r"] = p.get("r", 0.36) * g
        q["log_r"] = p.get("log_r", 0.3) * g
        q["skulls"] = [(i, z * h, s) for i, z, s in p.get("skulls", [])]
        q["fallen"] = [tuple(f[:4]) + tuple(v * h for v in f[4:]) for f in p.get("fallen", [])]
    return q


def tri_count(objs):
    n = 0
    for ob in objs:
        if ob.type != "MESH":
            continue
        ob.data.calc_loop_triangles()
        n += len(ob.data.loop_triangles)
    return n


# ---------------------------------------------------------------- termite mound

def termite_mound(b, p):
    """One earthen mass bristling with thin pinnacles packed like organ pipes
    round a fused core, each on a flared foot and ending in a small pale
    point, on a flared apron of spoil.
    p: h (tallest spire), r (spire ring radius), cr (core base radius), ar,
    ary (apron radii), ah (apron height), n (spires), sr (spire radius),
    knobs (share of short spires), core (core height share), speck (pale and
    dark shares of spire faces), seed, cx, cy."""
    rng = random.Random(p.get("seed", 1))
    cx, cy = p.get("cx", 0.0), p.get("cy", 0.0)
    H, R, n = p["h"], p["r"], p["n"]
    tone = p.get("tone", 1.0)

    def earth(key, c):
        return tuple(v * tone for v in p.get(key, c))
    body = b.mat("mound_body", earth("body", (70, 60, 42)))
    body2 = b.mat("mound_body2", earth("body2", (56, 48, 33)))
    dark = b.mat("mound_dark", earth("dark", (46, 40, 28)))
    dark2 = b.mat("mound_dark2", earth("dark2", (58, 50, 35)))
    cap = b.mat("mound_cap", p.get("cap", (132, 113, 80)))
    sand = b.mat("mound_sand", p.get("sand", (104, 92, 64)), rough=0.95)
    sand2 = b.mat("mound_sand2", p.get("sand2", (88, 76, 52)), rough=0.95)
    pale, dk = p.get("speck", (0.0, 0.06))
    mix = [(m, s) for m, s in ((body2, 0.38), (cap, pale), (dark, dk)) if s > 0]
    b.speckle.update({body: (rng.randrange(999), mix),
                      dark: (dark2, 0.35, rng.randrange(999)), sand: (sand2, 0.4, rng.randrange(999))})
    ar, ah = p["ar"] * p.get("apron", 1.0), p["ah"]
    ary = p.get("ary", p["ar"]) * p.get("apron", 1.0)

    def lump(a, z, i):
        return 1.0 + 0.07 * math.sin(3 * a + 1.3) + 0.05 * math.sin(7 * a + 0.4) + rng.uniform(-0.06, 0.06)
    # the apron: a flared skirt of spoil, lumpy at its foot
    rings = lathe(b.bm(sand), cx, cy, [(ar, 0.0), (ar * 0.9, ah * 0.2), (ar * 0.7, ah * 0.55),
                                       (ar * 0.52, ah), (ar * 0.3, ah * 1.3), (0, ah * 1.4)], seg=18, wobble=lump,
                  bottom=False)
    for ring in rings:
        for v in ring:
            v.co.y = cy + (v.co.y - cy) * ary / ar
    for i in range(p.get("clods", 5)):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0.6, 0.95) * ar
        m = sand if i % 3 else dark
        rock(b.bm(m), (cx + math.cos(a) * d, cy + math.sin(a) * d * ary / ar, 0.0),
             (rng.uniform(0.14, 0.28),) * 2 + (rng.uniform(0.08, 0.18),), rng, sub=1, rough=0.3)

    # the fused core, fluted like the spires that grow out of it
    CR, CH = p.get("cr", R), p.get("core", 0.8) * H
    # flutes at the pipes' own pitch, so the core between them reads as more fused pipes
    nf = p.get("flutes") or max(12, int(round(2 * math.pi * CR / (2.2 * p["sr"]))))
    ph = rng.uniform(0, 6.28)

    def knot(a, z, i):
        fade = max(p.get("flute_keep", 0.0), max(0.0, 1.0 - z / CH) ** 0.5)
        return 1.0 + (0.13 * math.cos(nf * a + ph + 0.3 * z) + rng.uniform(-0.04, 0.04)) * fade
    core_m = body2 if p.get("core_mat") == "body2" else dark
    lathe(b.bm(core_m), cx, cy, [(CR * 1.08, 0.0), (CR, CH * 0.25), (CR * 0.86, CH * 0.55), (CR * 0.58, CH * 0.8),
                               (CR * 0.3, CH * 0.95), (0, CH)], seg=2 * nf, wobble=knot, bottom=False)
    # most spires stand in a ring round the core, the rest inside it: the
    # middle and back tallest, the front ring a row of shorter knobs
    outer = int(round(n * p.get("ring", 0.65)))
    a0 = rng.uniform(0, 6.28)
    gold = math.pi * (3 - math.sqrt(5))
    for i in range(n):
        if i < outer:
            a = a0 + 2 * math.pi * i / outer + rng.uniform(-0.08, 0.08)
            d = R * rng.uniform(0.92, 1.0)
        else:
            t = (i - outer + 0.5) / max(1, n - outer)
            a = (i - outer) * gold + a0
            d = R * p.get("inner", 0.72) * math.sqrt(t)
        x, y = cx + math.cos(a) * d, cy + math.sin(a) * d
        front = (cy - y) / max(R, 1e-3)
        kn = p.get("knobs", 0.35) * ((1.0 + 0.8 * front) if i < outer else p.get("inner_knobs", 0.5))
        if rng.random() < kn:
            h = H * rng.uniform(*p.get("knob_h", (0.35, 0.75)))
        else:
            k = 1.0 - p.get("fall", 0.22) * (d / R) ** 2 + 0.06 * (y - cy) / R
            h = H * k * rng.uniform(p.get("vary", 0.8), 1.0)
        sr = p["sr"] * rng.uniform(0.85, 1.12) * (1.08 - 0.15 * d / R)
        lean = (d / R) * p.get("lean", 0.06)
        spire(b, body, body2, cap, x, y, h, sr, math.cos(a) * lean, math.sin(a) * lean, rng, seg=p.get("seg", 6),
              flare=p.get("flare", 1.8))


def spire(b, body, body2, cap, x, y, h, r, lx, ly, rng, seg=6, flare=1.8):
    """One fluted spire on a flared foot, tapering and a little crooked,
    leaning by (lx, ly) per unit height, ending in a blunt nub, pale only at its top."""
    fl = rng.uniform(0, 6.28)
    bumps = [rng.uniform(0.9, 1.1) for _ in range(6)]

    def flute(a, z, i):
        k = bumps[min(5, int(z / max(h, 1e-3) * 6))]
        return (1.0 + 0.08 * math.cos(3 * a + fl)) * k
    c = h - 0.7 * r  # where the rounded nub starts
    # nearly a column, so the pack stands as pipes, not spikes
    prof = [(r * flare, 0.0), (r * 1.08, h * 0.14), (r * 0.96, h * 0.4)] +         ([(r * 0.86, h * 0.64)] if seg > 5 else []) + [(r * 0.74, h * 0.84), (r * 0.62, c)]
    rings = lathe(b.bm(body), x, y, prof, seg=seg, wobble=flute, bottom=False, top=False)
    tip = lathe(b.bm(body2), x, y, [(r * 0.62, c), (r * 0.52, c + 0.35 * r)], seg=seg, wobble=flute,
                bottom=False, top=False)
    tip += lathe(b.bm(cap), x, y, [(r * 0.52, c + 0.35 * r), (r * 0.3, c + 0.6 * r), (0, h)], seg=seg, wobble=flute,
                 bottom=False)
    # crooked: each ring below the point pushed a little aside, the point following
    kx = ky = 0.0
    for ring in rings + tip:
        z = ring[0].co.z
        if 0 < z < c - 1e-4:
            kx, ky = rng.uniform(-0.2, 0.2) * r, rng.uniform(-0.2, 0.2) * r
        elif z <= 0:
            kx = ky = 0.0
        for v in ring:
            v.co.x += lx * v.co.z + kx
            v.co.y += ly * v.co.z + ky


# ---------------------------------------------------------------- thatched hut

def thatch_tier(bm, cx, cy, r_lo, z_lo, r_hi, z_hi, teeth, tooth, rng, droop=0.12):
    """One layer of thatch: a cone band from (r_hi, z_hi) down to (r_lo, z_lo)
    ending in a ragged fringe of straw tips, closed underneath."""
    n = teeth * 2
    top, mid, edge, under = [], [], [], []
    for i in range(n):
        a = 2 * math.pi * i / n + rng.uniform(-0.02, 0.02)
        ca, sa = math.cos(a), math.sin(a)
        groove = 1.0 + (0.035 if i % 2 else -0.035) * rng.uniform(0.5, 1.5)
        tip = i % 2 == 0
        re = r_lo + (tooth * rng.uniform(0.4, 1.3) if tip else tooth * 0.2)
        ze = z_lo - (droop * rng.uniform(0.6, 1.3) if tip else 0.0)
        rm = r_hi + (r_lo - r_hi) * 0.55
        zm = z_hi + (z_lo - z_hi) * 0.55
        top.append(bm.verts.new((cx + ca * r_hi * groove, cy + sa * r_hi * groove, z_hi)))
        mid.append(bm.verts.new((cx + ca * rm * groove, cy + sa * rm * groove, zm + rng.uniform(-0.03, 0.03))))
        edge.append(bm.verts.new((cx + ca * re, cy + sa * re, ze)))
        ru = r_lo - tooth * 0.3
        under.append(bm.verts.new((cx + ca * ru, cy + sa * ru, z_lo + 0.12)))
    for ring_a, ring_b in ((top, mid), (mid, edge), (edge, under)):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((ring_a[i], ring_a[j], ring_b[j], ring_b[i]))
    # close underneath back to the top ring's radius
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((under[i], under[j], top[j], top[i]))


def thatch_hut(b, p):
    """A round mud-walled hut under a layered, bristling thatch cone.
    p: rw (wall radius), wh (wall height), re (eave radius), ze (eave height),
    za (apex height), tiers [(r, z), ...] from the eave up, door (angle, width),
    wcx, wcy (wall centre), rcx, rcy (roof centre)."""
    rng = random.Random(p.get("seed", 3))
    wall = b.mat("hut_wall", p.get("wall", (92, 78, 58)))
    base = b.mat("hut_base", p.get("base", (70, 60, 46)))
    dark = b.mat("hut_dark", (22, 18, 13))
    straw = b.mat("hut_straw", p.get("straw", (92, 76, 28)), rough=0.95)
    straw2 = b.mat("hut_straw_dark", p.get("straw2", (70, 57, 22)), rough=0.95)
    wcx, wcy = p.get("wcx", 0.0), p.get("wcy", 0.0)
    rcx, rcy = p.get("rcx", 0.0), p.get("rcy", 0.0)
    rw, wh = p["rw"], p["wh"]

    def daub(a, z, i):
        return 1.0 + 0.015 * math.sin(9 * a) + rng.uniform(-0.01, 0.01)
    lathe(b.bm(wall), wcx, wcy, [(rw, 0.25), (rw * 0.99, wh * 0.6), (rw * 0.97, wh), (0, wh)], seg=24,
          wobble=daub)
    lathe(b.bm(base), wcx, wcy, [(rw + 0.08, 0.0), (rw + 0.06, 0.28), (rw - 0.05, 0.3), (0, 0.3)], seg=24)
    da, dw = p.get("door", (-90.0, 0.9))
    a = math.radians(da)
    dx, dy = wcx + math.cos(a) * (rw - 0.05), wcy + math.sin(a) * (rw - 0.05)
    box(b.bm(dark), (dx, dy, 0.0), (dw, 0.3, p.get("door_h", 1.5)), rot=(0, 0, a + math.pi / 2))
    # the door's posts and lintel
    for s in (-1, 1):
        px = dx + math.cos(a + math.pi / 2) * s * (dw / 2 + 0.07)
        py = dy + math.sin(a + math.pi / 2) * s * (dw / 2 + 0.07)
        box(b.bm(base), (px, py, 0.0), (0.16, 0.34, p.get("door_h", 1.5) + 0.1), rot=(0, 0, a + math.pi / 2))
    # the thatch: tiers from the eave up, each overlapping the one below
    tiers = p["tiers"]
    teeth = p.get("teeth", 36)
    for k, ((r0, z0), (r1, z1)) in enumerate(zip(tiers, tiers[1:])):
        m = straw if k % 2 == 0 else straw2
        thatch_tier(b.bm(m), rcx, rcy, r0, z0, r1 * 0.8, z1 + 0.18, teeth if k == 0 else max(30, teeth * 2 // 3),
                    p.get("tooth", 0.3) * (1.0 if k == 0 else 0.8), rng, droop=0.18 if k == 0 else 0.12)
    rt, zt = tiers[-1]
    # the crown and its bound top knot
    lathe(b.bm(straw), rcx, rcy, [(rt, zt - 0.1), (rt * 0.6, p["za"] - 0.25), (0.12, p["za"]), (0, p["za"] + 0.05)],
          seg=12)
    lathe(b.bm(straw2), rcx, rcy, [(0.2, p["za"] - 0.3), (0.16, p["za"] + 0.15), (0.03, p["za"] + 0.35),
                                   (0, p["za"] + 0.36)], seg=8)


# ---------------------------------------------------------------- hide tent

def outward_face(bm, vs, centre):
    """A face wound so its normal points away from centre."""
    f = bm.faces.new(vs)
    f.normal_update()
    c = f.calc_center_median()
    if f.normal.dot(c - Vector(centre)) < 0:
        f.normal_flip()
    return f


def sheet(bm, fn, nu, nv, centre):
    """A patch from fn(u, v), wound to face away from centre."""
    vs = [[bm.verts.new(fn(i / nu, j / nv)) for i in range(nu + 1)] for j in range(nv + 1)]
    for j in range(nv):
        for i in range(nu):
            outward_face(bm, (vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]), centre)
    return vs


def hide_tent(b, p):
    """Hides stretched over a pole frame: poles run from a smoke ring down to
    staked feet, the hide puffs out between them, and a porch flap is pegged
    out in front. p: ring (cx, cy, z, r), feet [(x, y)], bulge, dome,
    porch dict(between=(i, j), apex=(x, y, z), stakes=[(x, y), ...])."""
    hide = b.mat("tent_hide", p.get("hide", (104, 81, 47)))
    hide2 = b.mat("tent_hide_dark", p.get("hide2", (88, 68, 40)))
    rope = b.mat("tent_rope", p.get("rope", (62, 63, 62)), rough=0.7)
    wood = b.mat("tent_stake", (70, 52, 34))
    b.sheets.update((hide, hide2))
    rng = random.Random(p.get("seed", 5))
    cx, cy, rz, rr = p["ring"]
    feet = p["feet"]
    n = len(feet)
    centre = (cx, cy, rz * 0.35)
    ring = []
    for fx, fy in feet:
        d = Vector((fx - cx, fy - cy)).normalized()
        ring.append(Vector((cx + d.x * rr, cy + d.y * rr, rz)))
    feet3 = [Vector((fx, fy, 0.0)) for fx, fy in feet]
    bulge, dome = p.get("bulge", 0.35), p.get("dome", 0.35)

    def pole_pt(i, v):
        a, f = ring[i], feet3[i]
        q = a.lerp(f, v)
        out = Vector((q.x - cx, q.y - cy, 0)).normalized()
        return q + out * dome * math.sin(math.pi * v) + Vector((0, 0, dome * 0.6 * math.sin(math.pi * v)))

    for i in range(n):
        j = (i + 1) % n
        puff = bulge * rng.uniform(0.7, 1.2)
        m = hide if i % 2 == 0 else hide2

        ph = [rng.uniform(0, 6.28) for _ in range(4)]
        wr = p.get("wrinkle", 0.12)

        def fn(u, v, i=i, j=j, puff=puff, ph=ph):
            q = pole_pt(i, v).lerp(pole_pt(j, v), u)
            out = Vector((q.x - cx, q.y - cy, 0)).normalized()
            k = math.sin(math.pi * u) * math.sin(math.pi * min(1.0, v * 1.15)) ** 0.8
            # folds in the hide, strongest mid-panel
            w = wr * k * (math.sin(9 * u + 4 * v + ph[0]) + 0.6 * math.sin(3 * u - 8 * v + ph[1]))
            return q + out * (puff * k + w) + Vector((0, 0, puff * 0.4 * k + 0.5 * w))
        sheet(b.bm(m), fn, 7, 6, centre)
        # the pole's rope, lying along the crease
        pts = [pole_pt(i, t / 6) for t in range(7)]
        for a, c in zip(pts, pts[1:]):
            tube(b.bm(rope), a, c, 0.07, 0.07, seg=6)
        tube(b.bm(wood), feet3[i] + Vector((0, 0, -0.1)), feet3[i] + (feet3[i] - Vector((cx, cy, 0))).normalized() * 0.25
             + Vector((0, 0, 0.35)), 0.06, 0.03, seg=5)
    # the smoke ring and the cap within it
    for i in range(n):
        tube(b.bm(rope), ring[i], ring[(i + 1) % n], 0.08, 0.08, seg=6)
    top = Vector((cx, cy, rz + p.get("cap", 0.3)))
    for i in range(n):
        a, c = ring[i], ring[(i + 1) % n]

        def cap(u, v, a=a, c=c):
            q = a.lerp(c, u).lerp(top, v)
            return q + Vector((0, 0, 0.12 * math.sin(math.pi * u) * (1 - v)))
        sheet(b.bm(hide), cap, 3, 2, (cx, cy, 0.0))
    # the porch: a lower flap pegged out in front between two feet
    pr = p.get("porch")
    if pr:
        i, j = pr["between"]
        apex = Vector(pr["apex"])
        s0, s1 = (Vector((x, y, 0.0)) for x, y in pr["stakes"])
        fa, fb = feet3[i], feet3[j]
        # where the porch meets the tent: part way up the panel between the feet
        back = pole_pt(i, 0.45).lerp(pole_pt(j, 0.45), 0.5)
        pc = (apex.x, apex.y, 0.0)
        tris = ((back, apex, fa), (back, fb, apex), (apex, s0, fa), (apex, s1, s0), (apex, fb, s1))
        for a, c, d in tris:
            def fan(u, v, a=a, c=c, d=d):
                q = a.lerp(c, u).lerp(a.lerp(d, u), v)
                q = a + (c - a) * u * (1 - v) + (d - a) * u * v
                return q + Vector((0, 0, 0.1 * math.sin(math.pi * u) * math.sin(math.pi * v)))
            sheet(b.bm(hide2), fan, 3, 3, pc)
        for a, c in ((back, apex), (apex, s0), (apex, s1)):
            tube(b.bm(rope), a, c, 0.06, 0.06, seg=6)
        for s in (s0, s1):
            tube(b.bm(wood), s + Vector((0, 0, -0.1)), s + (s - apex).normalized() * 0.2 + Vector((0, 0, 0.35)),
                 0.06, 0.03, seg=5)



# ---------------------------------------------------------------- palisade

def _skull(b, bone, dark, c, face, s=0.3):
    """A skull of radius s at c, its face toward the horizontal direction face."""
    rng = random.Random(int(c[0] * 100 + c[1] * 10 + c[2]))
    rock(b.bm(bone), c, (s, s * 0.95, s * 1.05), rng, sub=1, rough=0.05)
    f = Vector((face[0], face[1], 0)).normalized()
    for k in (-1, 1):
        side = Vector((-f.y, f.x, 0)) * 0.4 * s * k
        e = Vector(c) + f * 0.78 * s + side + Vector((0, 0, 0.1 * s))
        box(b.bm(dark), e, (0.38 * s, 0.38 * s, 0.34 * s), base=False)
    jaw = Vector(c) + f * 0.45 * s + Vector((0, 0, -0.85 * s))
    box(b.bm(bone), jaw, (0.9 * s, 0.9 * s, 0.45 * s), rot=(0, 0, math.atan2(f.y, f.x)), base=False)


def blade(bm, p0, d, L, r, rng, flat=0.4):
    """A steel blade: a flat four-sided spike from p0 along d, widest (r)
    a fifth of the way out and drawn to a point; the root sinks into the post."""
    d = Vector(d).normalized()
    w = d.cross(Vector((0, 0, 1)))
    w = w.normalized() if w.length > 1e-3 else Vector((1, 0, 0))
    t = w.cross(d).normalized()
    roll = rng.uniform(-0.5, 0.5)
    w, t = w * math.cos(roll) + t * math.sin(roll), t * math.cos(roll) - w * math.sin(roll)
    p0 = Vector(p0)
    q = p0 + d * L * 0.22
    pts = [q + w * r, q + t * r * flat, q - w * r, q - t * r * flat, p0 - d * L * 0.05, p0 + d * L]
    faces = [(i, (i + 1) % 4, 5) for i in range(4)] + [((i + 1) % 4, i, 4) for i in range(4)]
    return poly(bm, pts, faces)


def stake_post(b, mats, x, y, h, r, cross, rng, head=True, lean=(0.0, 0.0), blades=8, jag=False, splinter=False,
               hscale=1.0):
    """One palisade post: a rough dark log under a bound, sharpened ogive head,
    steel blades bristling from both faces of the wall. h is the tip height;
    splinter snaps the head part way up into splinters."""
    body, headm, band, steel = mats["body"], mats["head"], mats["band"], mats["steel"]
    hl = ((min(1.9, h * 0.36) if h > 3.5 else min(1.6, h * 0.6)) * hscale) if head else 0.0  # head length
    hb = max(0.2, h - hl)
    lx, ly = lean
    seg = 9
    parts = []

    def bark(a, z, i):
        return 1.0 + rng.uniform(-0.13, 0.13)
    log = lathe(b.bm(body), x, y, [(r * 1.12, 0.0), (r * 1.0, 0.35), (r * 0.98, hb * 0.45), (r * 0.94, hb + 0.05)],
                seg=seg, wobble=bark, bottom=False, top=not head, phase=rng.uniform(0, 1))
    # crooked: the middle of the log pushed aside
    kx, ky = rng.uniform(-0.07, 0.07), rng.uniform(-0.07, 0.07)
    for v in log[2]:
        v.co.x += kx
        v.co.y += ky
    parts += log
    if head:
        rh = r * ((0.72 if h > 4 else 0.82) + 0.14 * hscale)
        prof = [(rh * 1.0, hb - 0.05), (rh * 1.04, hb + hl * 0.18), (rh * 0.84, hb + hl * 0.42),
                (rh * 0.52, hb + hl * 0.68), (rh * 0.2, hb + hl * 0.9), (0, h)]
        cut = rng.uniform(0.35, 0.55)
        if splinter:
            prof = [pq for pq in prof if pq[1] < hb + hl * cut] + [(0, hb + hl * cut)]
        parts += lathe(b.bm(headm), x, y, prof, seg=seg, phase=rng.uniform(0, 1), bottom=False)
        for zb in (hb + hl * 0.1, hb + hl * 0.34):
            if splinter and zb > hb + hl * (cut - 0.08):
                continue
            rz = rh * (1.02 if zb < hb + hl * 0.2 else 1.0)
            parts += lathe(b.bm(band), x, y, [(rz * 1.2, zb - 0.07), (rz * 1.2, zb + 0.06), (rz * 0.98, zb + 0.1)],
                           seg=seg, bottom=False, top=False)
        if splinter:
            zc = hb + hl * cut
            for i in range(5):
                a = 2 * math.pi * i / 5 + rng.uniform(0, 0.5)
                px, py = x + math.cos(a) * rh * 0.5, y + math.sin(a) * rh * 0.5
                parts.append(tube(b.bm(headm), (px, py, zc - 0.1),
                                  (px + math.cos(a) * 0.06, py + math.sin(a) * 0.06, zc + rng.uniform(0.2, 0.55)),
                                  rh * 0.34, 0.02, seg=4, cap=False))
    elif jag:
        # a snapped top: splinters of uneven height
        for i in range(5):
            a = 2 * math.pi * i / 5 + rng.uniform(0, 0.5)
            px, py = x + math.cos(a) * r * 0.55, y + math.sin(a) * r * 0.55
            parts.append(tube(b.bm(headm), (px, py, hb - 0.05),
                              (px + math.cos(a) * 0.05, py + math.sin(a) * 0.05, hb + rng.uniform(0.15, 0.6)),
                              r * 0.35, 0.02, seg=4, cap=False))
    for ring in parts:
        for v in ring:
            v.co.x += lx * v.co.z
            v.co.y += ly * v.co.z
    # blades both sides across the wall, fanned and raked, low ones down, high ones up
    cx_, cy_ = cross
    for k in range(blades):
        s = 1 if k % 2 else -1
        t = (k // 2 + rng.uniform(0.1, 0.9)) / max(1, (blades + 1) // 2)
        zb = hb * (0.1 + 0.78 * t)
        yaw = math.radians(rng.uniform(20, 40)) * rng.choice((-1, 1))
        pitch = math.radians(rng.uniform(-30, -5) if t < 0.5 else rng.uniform(-12, 20))
        dx = cx_ * math.cos(yaw) - cy_ * math.sin(yaw)
        dy = cx_ * math.sin(yaw) + cy_ * math.cos(yaw)
        d = Vector((dx * s * math.cos(pitch), dy * s * math.cos(pitch), math.sin(pitch)))
        p0 = Vector((x + lx * zb, y + ly * zb, zb)) + Vector((dx * s, dy * s, 0)) * r * 0.85
        blade(b.bm(steel), p0, d, rng.uniform(0.9, 1.2), rng.uniform(0.05, 0.07) * 1.4, rng, flat=0.3)
    return hb


def palisade(b, p):
    """A run of sharpened stake posts. p: axis ('x' runs east-west, 'y'
    north-south), a0, a1 (first and last post along the axis), c (the line
    across it), n posts, h, r, stagger; posts [(k, lean_x, lean_y)] per post
    (k 1 intact, 0 < k < 1 a stump keeping its head, k < 0 a snapped stump,
    None gone); tip (share of stumps knocked askew, least and most degrees);
    splinter [post]; hvary (head size spread); skulls [(post, z, side)];
    fallen [(x, y, angle deg, length, z)]; debris (count, spread along,
    spread across); blades loose on the ground; base_blades along the foot."""
    rng = random.Random(p.get("seed", 7))
    g = p.get("gain", GAIN)
    mats = dict(body=b.mat("pal_body", p.get("body", (24, 20, 16)), gain=g),
                head=b.mat("pal_head", p.get("head", (66, 52, 38)), gain=g),
                band=b.mat("pal_band", p.get("band", (80, 42, 28)), gain=g),
                steel=b.mat("pal_steel", p.get("steel", (66, 68, 74)), rough=0.35, metal=0.5,
                            gain=p.get("steel_gain", 1.3)))
    bone = b.mat("pal_bone", (196, 190, 176), gain=g)
    dark = b.mat("pal_char", (20, 18, 15))
    ax = p["axis"]
    n = p["n"]
    cross = (0.0, 1.0) if ax == "x" else (1.0, 0.0)
    posts = p.get("posts") or [(1.0, 0.0, 0.0)] * n
    tip_share, tip_lo, tip_hi = p.get("tip", (0.0, 5, 20))
    hv = p.get("hvary", 0.0)
    tops = []
    for i in range(n):
        t = i / max(1, n - 1)
        along = p["xs"][i] if p.get("xs") else p["a0"] + (p["a1"] - p["a0"]) * t
        off = p.get("stagger", 0.12) * (1 if i % 2 else -1) + rng.uniform(-0.05, 0.05)
        x, y = (along, p["c"] + off) if ax == "x" else (p["c"] + off, along)
        spec = posts[i] if i < len(posts) else (1.0, 0.0, 0.0)
        if spec is None:
            tops.append(None)
            continue
        k, lx, ly = spec
        if abs(k) < 0.7 and abs(lx) + abs(ly) < 0.15 and rng.random() < tip_share:
            a = rng.uniform(0, 2 * math.pi)
            tt = math.tan(math.radians(rng.uniform(tip_lo, tip_hi)))
            lx, ly = math.cos(a) * tt, math.sin(a) * tt
        else:
            lx += rng.uniform(-0.035, 0.035)
            ly += rng.uniform(-0.035, 0.035)
        h = p["h"] * abs(k) * rng.uniform(0.93, 1.03)
        nb = p.get("post_blades", 9) if k > 0.5 else p.get("stump_blades", 2)
        hb = stake_post(b, mats, x, y, h, p.get("r", 0.36) * rng.uniform(0.92, 1.08), cross, rng, head=k > 0,
                        lean=(lx, ly), blades=nb, jag=k < 0, splinter=i in p.get("splinter", ()),
                        hscale=1.0 + rng.uniform(-hv, hv))
        tops.append((x, y, hb, lx, ly))
    for pi, z, side in p.get("skulls", []):
        if pi >= len(tops) or tops[pi] is None:
            continue
        x, y, hb, lx, ly = tops[pi]
        z = min(z, hb + 0.2)
        d = Vector((cross[0] * side, cross[1] * side, 0))
        off = p.get("r", 0.36) + p.get("skull", 0.3) * 0.75
        c = (x + lx * z + d.x * off, y + ly * z + d.y * off, z)
        _skull(b, bone, dark, c, (d.x * 0.6, -1.0, 0), p.get("skull", 0.3))
        tube(b.bm(mats["steel"]), (x + lx * z, y + ly * z, z - 0.1), c, 0.05, 0.03, seg=4)
    # posts thrown down, their heads still on
    for f in p.get("fallen", []):
        fx, fy, ang, L = f[:4]
        z = f[4] if len(f) > 4 else 0.3
        a = math.radians(ang)
        d = Vector((math.cos(a), math.sin(a), 0))
        p0 = Vector((fx, fy, z))
        p1 = p0 + d * L * 0.7
        lr = p.get("log_r", 0.3)
        tube(b.bm(mats["body"]), p0 + Vector((0, 0, -0.02)), p1 + Vector((0, 0, 0.03)), lr, lr * 0.97, seg=7)
        tube(b.bm(mats["head"]), p1 + Vector((0, 0, 0.03)), p1 + d * L * 0.3 + Vector((0, 0, 0.06)), lr * 1.1, 0.03,
             seg=7)
        for q in (0.25, 0.55):
            blade(b.bm(mats["steel"]), p0 + d * L * q + Vector((0, 0, 0.2)), (-d.y, d.x, 0.5), 0.8, 0.06, rng)
    # skulls and loose blades lying about
    for fx, fy in p.get("ground_skulls", []):
        _skull(b, bone, dark, (fx, fy, 0.3), (0, -1, 0), p.get("skull", 0.3))
    dc, dx_, dy_ = p.get("debris", (0, 0, 0))
    a0, a1 = (p["xs"][0], p["xs"][-1]) if p.get("xs") else (p["a0"], p["a1"])
    ca, cb = (a0 + a1) / 2, p["c"]
    for i in range(dc):
        u = rng.uniform(-1, 1) * dx_
        w = rng.gauss(0, 0.45) * dy_
        x, y = (ca + u, cb + w) if ax == "x" else (cb + w, ca + u)
        chip(b.bm(dark if i % 4 else mats["body"]), (x, y), rng.uniform(0.06, 0.17), rng)
    for i in range(p.get("blades", 0)):
        u = rng.uniform(-1, 1) * dx_ * 0.8
        w = rng.uniform(-1, 1) * dy_ * 0.5
        x, y = (ca + u, cb + w) if ax == "x" else (cb + w, ca + u)
        a = rng.uniform(0, 6.28)
        blade(b.bm(mats["steel"]), (x, y, 0.1), (math.cos(a), math.sin(a), rng.uniform(0.0, 0.25)),
              rng.uniform(0.7, 1.0), rng.uniform(0.06, 0.08), rng)
    # short thick blades set along both faces at the foot of the run
    nbb = p.get("base_blades", 0)
    for i in range(nbb):
        u = a0 + (a1 - a0) * (i + rng.uniform(0.1, 0.9)) / nbb
        s = 1 if i % 2 else -1
        w = cb + s * (p.get("r", 0.36) + 0.05)
        x, y = (u, w) if ax == "x" else (w, u)
        yaw = rng.uniform(-0.7, 0.7)
        out = Vector((cross[0] * s, cross[1] * s, 0))
        d = Vector((out.x * math.cos(yaw) - out.y * math.sin(yaw), out.x * math.sin(yaw) + out.y * math.cos(yaw),
                    -rng.uniform(0.3, 0.8)))
        blade(b.bm(mats["steel"]), (x, y, rng.uniform(0.5, 1.0)), d, rng.uniform(0.6, 0.8),
              rng.uniform(0.1, 0.12), rng, flat=0.3)
    if p.get("char"):
        # a charred bed of splinters along the foot of the run
        L = abs(a1 - a0) / 2 + 0.6
        wdt = p["char"]
        bm = b.bm(dark)
        low, top = [], []
        m = 24
        for i in range(m):
            a = 2 * math.pi * i / m
            rr = 1.0 + rng.uniform(-0.25, 0.15)
            u, w = math.cos(a) * L * rr, math.sin(a) * wdt * rr
            q = (ca + u, cb + w) if ax == "x" else (cb + w, ca + u)
            low.append(bm.verts.new((q[0], q[1], 0.0)))
            top.append(bm.verts.new((q[0], q[1], 0.02 + rng.uniform(0, 0.03))))
        bm.faces.new(top)
        bm.faces.new(list(reversed(low)))
        for i in range(m):
            j = (i + 1) % m
            bm.faces.new((low[i], low[j], top[j], top[i]))


# ---------------------------------------------------------------- barricade remains

KY = 16 * math.cos(math.atan(0.5))  # sprite rows per cell of depth, true ortho
KZ = KY / 2                         # sprite rows per cell of height


def _pile_data(name):
    import json
    return json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "piles.json")))[name]


def _stake(bm, a, c, rad, rng, point=True):
    """A broken stake from a to c, sharpened at c when point is set."""
    tube(bm, a, c, rad, rad * 0.85, seg=5)
    if point:
        d = (c - a).normalized()
        tube(bm, c, c + d * rad * rng.uniform(1.5, 2.5), rad * 0.85, 0.01, seg=5, cap=False)


def _twig(bm, p0, direction, L, rad, rng):
    """A thorn branch lying on the ground: a crooked stem with two forks."""
    d = direction.normalized()
    side = Vector((-d.y, d.x, 0.0))
    p1 = p0 + d * L * 0.55 + side * rng.uniform(-0.12, 0.12) + Vector((0, 0, rng.uniform(0.0, 0.15)))
    p2 = p1 + d * L * 0.45 + side * rng.uniform(-0.12, 0.12) + Vector((0, 0, rng.uniform(0.0, 0.2)))
    tube(bm, p0, p1, rad, rad * 0.8, seg=4)
    tube(bm, p1, p2, rad * 0.8, rad * 0.3, seg=4)
    for q, s_ in ((p1, 1), (p0.lerp(p1, 0.6), -1)):
        tip = q + (d * 0.6 + side * 0.8 * s_).normalized() * L * rng.uniform(0.2, 0.35) + Vector((0, 0, 0.08))
        tube(bm, q, tip, rad * 0.6, rad * 0.2, seg=3)


def barricade_remains(b, p):
    """What is left of a Zhon barricade: a heap of dark boulders tangled with
    broken sharpened stakes and thorn branches, on a bed of trampled straw
    and earth showing at its foot. Boulders and sticks come from piles.json
    (readpile.py), placed where the picture shows them; p may add pieces."""
    d = _pile_data(p.get("pile", p["name"]))
    hx, hy = d["hot"]
    rng = random.Random(p.get("seed", 9))
    bed = b.mat("pile_bed", p.get("bed", (74, 72, 46)), rough=0.95)
    straw = b.mat("pile_straw", p.get("straw", (112, 102, 64)), rough=0.95)
    grass = b.mat("pile_grass", p.get("grass", (64, 74, 40)), rough=0.95)
    wood = b.mat("pile_wood", p.get("wood", (78, 52, 34)))
    wood2 = b.mat("pile_wood_dark", p.get("wood2", (44, 32, 22)))
    stones = [b.mat("pile_stone_dark", (40, 41, 38), rough=0.75), b.mat("pile_stone", (64, 64, 58), rough=0.7),
              b.mat("pile_stone_light", (118, 116, 106), rough=0.6)]
    if p.get("stone"):
        # faceted boulders in one dark stone, their upward facets catching the light
        dk, lt = p["stone"]
        stones = [b.mat("pile_stone_facet", dk, rough=0.8)] * 3
        b.mat("pile_stone_top", lt, rough=0.7)
        b.tops[stones[0]] = ("pile_stone_top", 0.8)
        b.flat.add(stones[0])
    bd = d["bed"]
    bx, by = (bd["x0"] + bd["x1"]) / 2, (bd["front"] + bd["back"]) / 2
    rx, ry = (bd["x1"] - bd["x0"]) / 2 * 0.95, (bd["back"] - bd["front"]) / 2 * 1.0
    hc = p.get("heap", 0.35)

    def heap(x, y):
        q = ((x - bx) / rx) ** 2 + ((y - by) / ry) ** 2
        return max(0.0, hc * (1 - q))

    # the bed: a low lumpy dome of earth and trampled straw
    def bedfn(u, v):
        a = 2 * math.pi * u
        rr = (1 - v) * (1.0 + 0.1 * math.sin(5 * a + 1) + 0.06 * math.sin(11 * a))
        x, y = bx + math.cos(a) * rx * rr, by + math.sin(a) * ry * rr
        return (x, y, 0.02 + heap(x, y) * (1 - (1 - v) ** 3) + (0.03 * math.sin(7 * x) * math.cos(5 * y)) * v)
    grid_surface(b.bm(bed), bedfn, 20, 4, closed_u=True)
    for i in range(p.get("tufts", 30)):
        a = rng.uniform(0, 6.28)
        rr = math.sqrt(rng.uniform(0.2, 1)) * 0.95
        x, y = bx + math.cos(a) * rx * rr, by + math.sin(a) * ry * rr
        z = heap(x, y)
        m = straw if i % 3 else grass
        if p.get("straw_bits"):
            m = straw
        for k in range(p.get("tuft_blades", 3)):
            aa = rng.uniform(0, 6.28)
            L = rng.uniform(0.2, 0.4)
            tube(b.bm(m), (x, y, z), (x + math.cos(aa) * L * 0.6, y + math.sin(aa) * L * 0.6, z + L * 0.6), 0.04, 0.005,
                 seg=3, cap=False)
    # straw strewn flat over the bed
    for i in range(p.get("straw_bits", 0)):
        a = rng.uniform(0, 6.28)
        rr = math.sqrt(rng.uniform(0.0, 1)) * 0.95
        x, y = bx + math.cos(a) * rx * rr, by + math.sin(a) * ry * rr
        z = heap(x, y) + 0.03
        aa, L = rng.uniform(0, 6.28), rng.uniform(0.2, 0.4)
        tube(b.bm(straw), (x, y, z), (x + math.cos(aa) * L, y + math.sin(aa) * L, z + rng.uniform(0, 0.08)), 0.035,
             0.02, seg=3, cap=False)
    # boulders where the picture has grey blobs; a big blob is a cluster.
    # pack pulls them in toward the middle, where they pile in layers
    tops = []
    pk = p.get("pack", 0.0)
    placed = []
    for bo in sorted(d["boulders"] + p.get("add_boulders", []),
                     key=lambda q: -math.hypot(q["x"] - bx, q["y"] - by) if pk else 0):
        r = bo["r"] * p.get("rscale", 1.0)
        if r < 0.1:
            continue
        if pk:
            bo = dict(bo, x=bx + (bo["x"] - bx) * (1 - pk), y=by + (bo["y"] - by) * (1 - pk))
        m = stones[0] if bo["lum"] < 62 else stones[2] if bo["lum"] > 110 else stones[1]
        parts = [(bo["x"], bo["y"], r)]
        if r > 0.6:
            n = 3 if r < 0.9 else 4
            parts = []
            for k in range(n):
                a = 2 * math.pi * k / n + rng.uniform(-0.4, 0.4)
                parts.append((bo["x"] + math.cos(a) * r * 0.45, bo["y"] + math.sin(a) * r * 0.4, r * rng.uniform(0.5, 0.62)))
        for k, (x, y, rr) in enumerate(parts):
            z = heap(x, y) * 0.6
            if pk:
                # resting on the boulders already laid, up to three deep
                for qx, qy, qz, qr in placed:
                    dd = math.hypot(x - qx, y - qy)
                    if dd < (qr + rr) * 0.85:
                        z = max(z, qz - rr * 0.35)
                z = min(z, heap(x, y) * 0.6 + 1.1)
            mk = m if k == 0 else stones[min(2, max(0, stones.index(m) + rng.choice((-1, 0, 0, 1))))]
            rock(b.bm(mk), (x, y, z + rr * 0.45), (rr * 1.05, rr, rr * 0.85), rng, sub=2 if rr > 0.33 else 1, rough=0.3)
            tops.append((x, y, z + rr * 1.2, rr))
            placed.append((x, y, z + rr * 1.2, rr))

    def rest(x, y):
        """Height a stick lying at (x, y) rests at: on the bed or across a boulder."""
        z = heap(x, y) + 0.1
        for bx_, by_, bz, br in tops:
            dd = math.hypot(x - bx_, y - by_)
            if dd < br:
                z = max(z, bz * math.sqrt(max(0.0, 1 - (dd / br) ** 2)) + 0.05)
        return z

    # stakes where the picture has brown streaks: the lower end rests on the
    # heap, the upper end rises steeply if it stands up the picture
    for st in d["sticks"] + p.get("add_sticks", []):
        (c0, r0), (c1, r1) = st["low"], st["high"]
        dcol, drow = (c1 - c0) / 16.0, r0 - r1  # drow >= 0: the high end is up the picture
        L2 = math.hypot(dcol, drow / KY)
        if L2 < 0.25:
            continue
        steep = drow > 2.2 * abs(c1 - c0) and drow > 10
        th = math.radians(p.get("stand", 50) if steep else 12)
        x0 = (c0 - hx) / 16.0
        y0 = (hy - r0) / KY
        z0 = heap(x0, y0) * 0.8 + 0.1
        y0 = (hy - r0 - KZ * z0) / KY
        # solve drow = KY dy + KZ tan(th) sqrt(dcol^2 + dy^2) for dy
        lo, hi = -6.0, 6.0
        for _ in range(40):
            mid = (lo + hi) / 2
            f = KY * mid + KZ * math.tan(th) * math.hypot(dcol, mid) - drow
            if f > 0:
                hi = mid
            else:
                lo = mid
        dy = (lo + hi) / 2
        dz = math.tan(th) * math.hypot(dcol, dy)
        rad = max(0.09, min(0.2, st["thick"] / 16.0 * 0.85))
        a, c = Vector((x0, y0, max(z0, rest(x0, y0)))), Vector((x0 + dcol, y0 + dy, z0 + dz))
        if not steep:
            c.z = max(c.z, rest(c.x, c.y))
        m = wood if rng.random() < 0.6 else wood2
        _stake(b.bm(m), a, c, rad, rng, point=rng.random() < 0.7)
    # more broken stakes thrown across the heap
    for i in range(p.get("tangle", 6)):
        a = rng.uniform(0, 2 * math.pi)
        rr = math.sqrt(rng.uniform(0, 1)) * 0.55
        x, y = bx + math.cos(a) * rx * rr, by + math.sin(a) * ry * rr
        ang = rng.uniform(0, 2 * math.pi)
        L = rng.uniform(0.7, 1.3)
        e = Vector((math.cos(ang), math.sin(ang), 0))
        p0 = Vector((x, y, 0)) - e * L * 0.5
        p1 = Vector((x, y, 0)) + e * L * 0.5
        p0.z, p1.z = rest(p0.x, p0.y), rest(p1.x, p1.y) + rng.uniform(0.0, 0.5)
        _stake(b.bm(wood2 if i % 2 else wood), p0, p1, rng.uniform(0.09, 0.14), rng, point=rng.random() < 0.5)
    # dark stakes thrown right across the middle of the heap
    for i in range(p.get("cross_stakes", 0)):
        ang = math.pi * i / max(1, p["cross_stakes"]) + rng.uniform(-0.25, 0.25)
        e = Vector((math.cos(ang), math.sin(ang), 0))
        c = Vector((bx + rng.uniform(-0.3, 0.3) * rx, by + rng.uniform(-0.3, 0.3) * ry, 0))
        L = rng.uniform(1.6, 2.4)
        p0, p1 = c - e * L * 0.5, c + e * L * 0.5
        p0.z = rest(p0.x, p0.y) + rng.uniform(0.0, 0.2)
        p1.z = rest(p1.x, p1.y) + rng.uniform(0.1, 0.6)
        _stake(b.bm(wood2), p0, p1, rng.uniform(0.09, 0.13), rng, point=rng.random() < 0.6)
    # thorn branches about the foot of the heap, reaching outward
    for i in range(p.get("twigs", 8)):
        a = 2 * math.pi * i / p.get("twigs", 8) + rng.uniform(-0.3, 0.3)
        p0 = Vector((bx + math.cos(a) * rx * 0.8, by + math.sin(a) * ry * 0.8, 0.12))
        _twig(b.bm(wood), p0, Vector((math.cos(a), math.sin(a), 0.1)), rng.uniform(0.8, 1.4), 0.05, rng)
    # dark clods and splinters about the edge
    for i in range(p.get("chips", 30)):
        a = rng.uniform(0, 6.28)
        rr = rng.uniform(0.7, 1.2)
        chip(b.bm(wood2), (bx + math.cos(a) * rx * rr, by + math.sin(a) * ry * rr), rng.uniform(0.05, 0.12), rng)


# ---------------------------------------------------------------- temples and tombs

def _noise2(x, y, s):
    """A smooth value in about -1..1 from a few sines, the same for the same seed."""
    return (math.sin(1.3 * x + 2.1 * s) * math.cos(1.7 * y + 0.7 * s) * 0.6
            + math.sin(0.7 * x - 1.1 * y + 1.9 * s) * 0.4)


TEMPLE_STONE = dict(light=(146, 117, 78), mid=(134, 104, 64), dark=(120, 93, 57), moss=(104, 100, 50),
                    moss2=(86, 88, 44), shade=(46, 36, 24), tread=(150, 114, 72), riser=(100, 77, 50),
                    edge=(84, 64, 42), rubble=(92, 72, 48), rubble_dark=(64, 49, 33), soot=(74, 62, 48),
                    grime=(98, 80, 56), joint=(45, 38, 30), pit=(42, 35, 29), bed=(50, 40, 35))
KEEP = ("joint", "pit", "bed", "shade")  # colours a ruin's darkening leaves alone
SIDES = ("front", "back", "left", "right")


def _solid(q):
    """The box a standing block fills, for hiding the faces set against it;
    a sloped block counts only up to its lower edge."""
    z1 = q["z1"] if not q["wedge"] else min(q["wedge"][1], q["wedge"][2])
    return ((q["x0"], q["y0"], q["z0"]), (q["x1"], q["y1"], z1), q)


def _solids(q, n=4):
    """The boxes a standing block fills: a sloped one as n steps under its slope."""
    if not q["wedge"]:
        return [_solid(q)]
    ax, h0, h1 = q["wedge"]
    out = []
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n
        z1 = min(h0 + (h1 - h0) * t0, h0 + (h1 - h0) * t1)
        if ax == "y":
            a, c = q["y0"] + (q["y1"] - q["y0"]) * t0, q["y0"] + (q["y1"] - q["y0"]) * t1
            out.append(((q["x0"], a, q["z0"]), (q["x1"], c, z1), q))
        else:
            a, c = q["x0"] + (q["x1"] - q["x0"]) * t0, q["x0"] + (q["x1"] - q["x0"]) * t1
            out.append(((a, q["y0"], q["z0"]), (c, q["y1"], z1), q))
    return out


def _merge_boxes(boxes):
    """Joins boxes that share a face exactly into one, along x, then y, then z.
    A box may carry a seventh item, the set of sides it must draw; a joined
    box draws its end sides as its end boxes did and the rest if either did."""
    ends = (("left", "right"), ("front", "back"), (None, "top"))
    for axis in (0, 1, 2):
        a0, a1 = 2 * axis, 2 * axis + 1
        others = [k for k in range(6) if k not in (a0, a1)]
        groups = {}
        for bx in boxes:
            groups.setdefault(tuple(round(bx[k], 4) for k in others), []).append(bx)
        out = []
        for g in groups.values():
            g.sort(key=lambda bb: bb[a0])
            cur = list(g[0])
            for bx in g[1:]:
                if bx[a0] <= cur[a1] + 1e-4:
                    if len(cur) > 6:
                        lo, hi = ends[axis]
                        high = bx[6] if bx[a1] >= cur[a1] else cur[6]
                        both = (cur[6] | bx[6]) - {lo, hi}
                        cur[6] = both | ({lo} & cur[6]) | ({hi} & high)
                    cur[a1] = max(cur[a1], bx[a1])
                else:
                    out.append(cur)
                    cur = list(bx)
            out.append(cur)
        boxes = out
    return boxes


def _box_sides(x0, x1, y0, y1, z0, z1, bottom=False):
    """The faces of a box as (side, axis, plane, facing, span over the other two axes)."""
    out = [("top", 2, z1, 1, ((x0, x1), (y0, y1))), ("front", 1, y0, -1, ((x0, x1), (z0, z1))),
           ("back", 1, y1, 1, ((x0, x1), (z0, z1))), ("left", 0, x0, -1, ((y0, y1), (z0, z1))),
           ("right", 0, x1, 1, ((y0, y1), (z0, z1)))]
    if bottom:
        out.append(("bottom", 2, z0, -1, ((x0, x1), (y0, y1))))
    return out


def _face(bm, side, x0, x1, y0, y1, z0, z1):
    """One face of a box, wound to face outward."""
    pts = {"top": [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
           "bottom": [(x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0)],
           "front": [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
           "back": [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],
           "left": [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)],
           "right": [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)]}[side]
    return poly(bm, pts, [(0, 1, 2, 3)])


class _Cover:
    """Answers whether a face lies wholly against a set of boxes, exactly:
    the boxes touching the face are cut into a grid over it and every cell
    of the grid must fall inside one of them."""
    E = 1e-4

    def __init__(self, boxes):
        self.cells = {}
        for bx in boxes:
            lo, hi = bx[0], bx[1]
            for i in range(int(math.floor(lo[0])), int(math.floor(hi[0])) + 1):
                for j in range(int(math.floor(lo[1])), int(math.floor(hi[1])) + 1):
                    self.cells.setdefault((i, j), []).append(bx)

    def covered(self, axis, c, sgn, span):
        return self.cover(axis, c, sgn, span) == 2

    def cover(self, axis, c, sgn, span):
        """0 when nothing touches the face, 1 when something touches part of it, 2 when it is wholly covered."""
        E = self.E
        ua, va = [k for k in range(3) if k != axis]
        (u0, u1), (v0, v1) = span
        if u1 - u0 < E or v1 - v0 < E:
            return 2
        probe = c + sgn * 2e-3
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[axis] = hi[axis] = probe
        lo[ua], hi[ua], lo[va], hi[va] = u0, u1, v0, v1
        seen, cand = set(), []
        for i in range(int(math.floor(lo[0])), int(math.floor(hi[0])) + 1):
            for j in range(int(math.floor(lo[1])), int(math.floor(hi[1])) + 1):
                for bx in self.cells.get((i, j), ()):
                    if id(bx) in seen:
                        continue
                    seen.add(id(bx))
                    blo, bhi = bx[0], bx[1]
                    if not blo[axis] - E <= probe <= bhi[axis] + E:
                        continue
                    if min(bhi[ua], u1) - max(blo[ua], u0) <= E or min(bhi[va], v1) - max(blo[va], v0) <= E:
                        continue
                    cand.append(bx)
        if not cand:
            return 0

        def cuts(k, a, b):
            return sorted(set([a, b] + [min(b, max(a, bx[0][k])) for bx in cand] + [min(b, max(a, bx[1][k]))
                                                                                   for bx in cand]))
        us, vs = cuts(ua, u0, u1), cuts(va, v0, v1)
        for ua_, ub_ in zip(us, us[1:]):
            if ub_ - ua_ < E:
                continue
            um = (ua_ + ub_) / 2
            for va_, vb_ in zip(vs, vs[1:]):
                if vb_ - va_ < E:
                    continue
                vm = (va_ + vb_) / 2
                if not any(bx[0][ua] - E <= um <= bx[1][ua] + E and bx[0][va] - E <= vm <= bx[1][va] + E
                           for bx in cand):
                    return 1
        return 2


def _clip(pts, mx, my, nx, ny):
    """The part of a polygon on the near side of the line through (mx, my) with normal (nx, ny)."""
    out = []
    for i in range(len(pts)):
        a, c = pts[i], pts[(i + 1) % len(pts)]
        da = (a[0] - mx) * nx + (a[1] - my) * ny
        dc = (c[0] - mx) * nx + (c[1] - my) * ny
        if da <= 0:
            out.append(a)
        if (da < 0 < dc) or (dc < 0 < da):
            t = da / (da - dc)
            out.append((a[0] + (c[0] - a[0]) * t, a[1] + (c[1] - a[1]) * t))
    return out


class Mason:
    """Dressed stone laid block by block, so a ruin can take blocks away.
    A block is a dict: box x0 x1 y0 y1 z0 z1, material, grid (the fill it
    came from), wedge (a sloped top), moss (a cap of moss on its top edge).
    emit() draws only the faces nothing covers and seals every joint with a
    dark core set back behind the faces, so no seam shows the ground."""

    def __init__(self, b, p, rng):
        self.b, self.p, self.rng = b, p, rng
        pal = dict(TEMPLE_STONE, **p.get("stone", {}))
        dk = p.get("darken", 1.0)
        self.pal = {k: (c if k in KEEP else tuple(v * dk for v in c)) for k, c in pal.items()}
        if "soot" not in p.get("stone", {}):
            # soot: the middle tone gone dull and grey
            mid = self.pal["mid"]
            avg = sum(mid) / 3
            self.pal["soot"] = tuple(0.62 * v + 0.2 * avg for v in mid)
        self.gain = p.get("gain", 0.9)
        self.tone = [self.mat("light"), self.mat("mid"), self.mat("dark")]
        self.blocks = []
        self.ngrid = 0
        self.nstair = 0

    def mat(self, key, rough=0.9):
        """A named material from the palette: 'light', 'shade', 'tread' and so on."""
        return self.b.mat("st_" + key, self.pal[key], rough=rough, gain=self.gain)

    def pick(self, weights=None):
        weights = weights or self.p.get("tones", (0.4, 0.42, 0.18))
        r = self.rng.random() * sum(weights)
        for m, w in zip(self.tone, weights):
            r -= w
            if r <= 0:
                return m
        return self.tone[1]

    def _mossy(self, x, y, moss):
        pr = self.p.get("moss", 0.1) if moss is None else moss
        for cx, cy, rx, ry, extra in self.p.get("moss_zones", ()):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 < 1:
                pr += extra
        return self.rng.random() < pr

    def block(self, x0, x1, y0, y1, z0, z1, mat=None, grid=None, moss=None, tones=None, wedge=None, gap=True,
              keep=False, step=False, **extra):
        if isinstance(mat, (tuple, list)):
            mat = self.rng.choice(mat)
        explicit = mat is not None
        if isinstance(mat, str):
            mat = self.mat(mat)
        m = mat or self.pick(tones)
        blk = dict(x0=x0, x1=x1, y0=y0, y1=y1, z0=z0, z1=z1, mat=m, grid=grid, wedge=wedge, gap=gap, keep=keep,
                   tilt=None, step=step, moss=not explicit and not step and self._mossy((x0 + x1) / 2, (y0 + y1) / 2,
                                                                                        moss))
        blk.update(extra)
        self.blocks.append(blk)
        return blk

    def fill(self, x0, x1, y0, y1, z0, z1, bl=1.6, bd=1.6, bh=1.0, bond=True, jit=0.0, **kw):
        """A solid of courses: blocks about bl long, bd deep, bh high, in running
        bond; jit moves each row's joints at random, for flagging."""
        self.ngrid += 1
        g = self.ngrid
        nk = max(1, int(round((z1 - z0) / bh)))
        nj = max(1, int(round((y1 - y0) / bd)))
        n = max(1, int(round((x1 - x0) / bl)))
        for k in range(nk):
            za, zb = z0 + (z1 - z0) * k / nk, z0 + (z1 - z0) * (k + 1) / nk
            for j in range(nj):
                off = 0.5 if bond and (k + (j if jit else 0)) % 2 else 0.0
                cuts = [x0 + (i + off + self.rng.uniform(-jit, jit)) * (x1 - x0) / n for i in range(0, n + 1)]
                cuts = sorted(set([x0, x1] + [c for c in cuts if x0 + 0.1 < c < x1 - 0.1]))
                ya, yb = y0 + (y1 - y0) * j / nj, y0 + (y1 - y0) * (j + 1) / nj
                for xa, xb in zip(cuts, cuts[1:]):
                    self.block(xa, xb, ya, yb, za, zb, grid=g, **kw)
        return g

    def slab(self, x0, x1, y0, y1, z, t=0.25, bl=1.4, bd=1.2, **kw):
        """Paving: one course of flat slabs laid on z."""
        return self.fill(x0, x1, y0, y1, z, z + t, bl=bl, bd=bd, bh=t + 1, **kw)

    def talus(self, x0, x1, yf, yb, z0, z1, n, bl=1.5, **kw):
        """A battered face: n courses, each set back from the one below."""
        run = (yb - yf) / (n + 0.3)
        for k in range(n):
            self.fill(x0, x1, yf + run * k, yb, z0 + (z1 - z0) * k / n, z0 + (z1 - z0) * (k + 1) / n, bl=bl,
                      bd=yb - yf + 1, bh=9, **kw)

    def stair(self, x0, x1, y0, y1, z0, z1, n, axis="y", mat="tread", zb=None, nose=0.04, tt=0.08, **kw):
        """A flight of n steps climbing from y0 (at z0) to y1 (at z1); axis 'x'
        climbs from x0 to x1 instead, between y0 and y1. zb is the foot of the
        solid. Each step is a riser block under a tread whose nose overhangs
        the step below, its front edge darker so the flight reads as steps
        even from straight above."""
        zb = z0 if zb is None else zb
        self.nstair += 1
        a0, a1 = (y0, y1) if axis == "y" else (x0, x1)
        up = 1 if a1 > a0 else -1
        side = {("y", 1): "front", ("y", -1): "back", ("x", 1): "left", ("x", -1): "right"}[(axis, up)]
        for i in range(n):
            a, c = a0 + (a1 - a0) * i / n, a0 + (a1 - a0) * (i + 1) / n
            zt = z0 + (z1 - z0) * (i + 1) / n
            lo, hi = min(a, c), max(a, c)
            nlo, nhi = (lo - nose, hi) if up > 0 else (lo, hi + nose)
            sid = (self.nstair, i)
            for part, (p0, p1), (za, zc), m in (("body", (lo, hi), (zb, zt - tt), "riser"),
                                                ("tread", (nlo, nhi), (zt - tt, zt), mat)):
                if zc - za < 1e-3:
                    continue
                box_ = (x0, x1, p0, p1) if axis == "y" else (p0, p1, y0, y1)
                self.block(*box_, za, zc, mat=m, gap=False, step=True, part=part, sid=sid,
                           edge=side if part == "tread" else None, **kw)

    def cheek(self, x0, x1, y0, y1, z0, za, zb, axis="y", n=4, **kw):
        """A sloped wall beside a stair, its top rising from za (at y0, or x0) to zb, in n blocks."""
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            h0, h1 = za + (zb - za) * t0, za + (zb - za) * t1
            if axis == "y":
                ya, yb = y0 + (y1 - y0) * t0, y0 + (y1 - y0) * t1
                self.block(x0, x1, ya, yb, z0, max(h0, h1), wedge=("y", h0, h1), **kw)
            else:
                xa, xb = x0 + (x1 - x0) * t0, x0 + (x1 - x0) * t1
                self.block(xa, xb, y0, y1, z0, max(h0, h1), wedge=("x", h0, h1), **kw)

    def drum(self, x, y, r, h, z0=0.0, seg=10, mat=None):
        """A round stone: a foot, a bollard, a fallen column drum."""
        m = self.mat(mat) if isinstance(mat, str) else (mat or self.pick())
        lathe(self.b.bm(m), x, y, [(r, z0), (r * 0.94, z0 + h), (0, z0 + h)], seg=seg, bottom=False)

    def cut(self, x0, x1, y0, y1, z0, z1):
        """Carves a box out of the stone laid so far: each block it touches is
        replaced by the pieces of it left outside."""
        E = 1e-6
        out = []
        for q in self.blocks:
            if (q.get("gone") or q["wedge"] or q["tilt"] is not None
                    or min(q["x1"], x1) - max(q["x0"], x0) <= E or min(q["y1"], y1) - max(q["y0"], y0) <= E
                    or min(q["z1"], z1) - max(q["z0"], z0) <= E):
                out.append(q)
                continue
            xa, xb = max(q["x0"], x0), min(q["x1"], x1)
            ya, yb = max(q["y0"], y0), min(q["y1"], y1)
            za, zb = max(q["z0"], z0), min(q["z1"], z1)
            pieces = [(q["x0"], xa, q["y0"], q["y1"], q["z0"], q["z1"]), (xb, q["x1"], q["y0"], q["y1"], q["z0"], q["z1"]),
                      (xa, xb, q["y0"], ya, q["z0"], q["z1"]), (xa, xb, yb, q["y1"], q["z0"], q["z1"]),
                      (xa, xb, ya, yb, q["z0"], za), (xa, xb, ya, yb, zb, q["z1"])]
            for pc in pieces:
                if pc[1] - pc[0] > E and pc[3] - pc[2] > E and pc[5] - pc[4] > E:
                    out.append(dict(q, x0=pc[0], x1=pc[1], y0=pc[2], y1=pc[3], z0=pc[4], z1=pc[5]))
        self.blocks = out

    def pit(self, x0, x1, y0, y1, z, depth=1.0):
        """A slot sunk depth into the stone below z, floored with dark stone."""
        self.cut(x0, x1, y0, y1, z - depth, z + 0.05)
        self.block(x0, x1, y0, y1, z - depth - 0.15, z - depth, mat="pit", gap=False)

    def flags(self, x0, x1, y0, y1, z, t=0.03, size=0.65, seed=None, mats=("flag", "flag2", "flag3"), joint=0.03,
              stain=None):
        """Irregular flagstones on a dark bed: the cells round jittered points,
        each drawn in from its neighbours by the joint and raised t; flags
        under stain (cx, cy, rx, ry) are sooted."""
        rng = random.Random(seed) if seed is not None else self.rng
        nx, ny = max(1, round((x1 - x0) / size)), max(1, round((y1 - y0) / size))
        pts = [(x0 + (i + 0.5 + rng.uniform(-0.38, 0.38)) * (x1 - x0) / nx,
                y0 + (j + 0.5 + rng.uniform(-0.38, 0.38)) * (y1 - y0) / ny) for i in range(nx) for j in range(ny)]
        for k, (px, py) in enumerate(pts):
            cell = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
            for m, (qx, qy) in enumerate(pts):
                if m != k and abs(qx - px) < 3 * size and abs(qy - py) < 3 * size:
                    cell = _clip(cell, (px + qx) / 2, (py + qy) / 2, qx - px, qy - py)
            if len(cell) < 3:
                continue
            cx = sum(v[0] for v in cell) / len(cell)
            cy = sum(v[1] for v in cell) / len(cell)
            shrunk = []
            for vx, vy in cell:
                d = math.hypot(vx - cx, vy - cy)
                f = max(0.0, 1 - joint / max(d, 1e-3) * 1.4)
                shrunk.append((cx + (vx - cx) * f, cy + (vy - cy) * f))
            zt = z + t + rng.uniform(-0.008, 0.008)
            key = rng.choice(mats)
            if stain:
                d = math.hypot((cx - stain[0]) / stain[2], (cy - stain[1]) / stain[3])
                key = "stain" if d < 0.6 else "stain_mid" if d < 0.9 else key
            bm = self.b.bm(self.mat(key))
            n = len(shrunk)
            vs = [(x, y, z) for x, y in shrunk] + [(x, y, zt) for x, y in shrunk]
            poly(bm, vs, [tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)])

    # ---- ruin
    def surface(self, x, y):
        """The top of the standing stone at (x, y), or the ground."""
        z = 0.0
        for o in self.blocks:
            if o.get("gone") or o["tilt"] is not None:
                continue
            if o["x0"] <= x <= o["x1"] and o["y0"] <= y <= o["y1"]:
                if o["wedge"]:
                    ax, h0, h1 = o["wedge"]
                    t = (y - o["y0"]) / (o["y1"] - o["y0"]) if ax == "y" else (x - o["x0"]) / (o["x1"] - o["x0"])
                    z = max(z, h0 + (h1 - h0) * t)
                else:
                    z = max(z, o["z1"])
        return z

    def collapse(self, cx, cy, rx, ry, zkeep, rise=3.0, jag=0.6, tip=0.25, zmin=None):
        """Takes away blocks inside an ellipse above a ragged height that climbs
        toward the rim; some blocks at the break are knocked askew instead."""
        s = self.rng.uniform(0, 9)
        for blk in self.blocks:
            if blk.get("gone") or blk["keep"] or blk.get("loose"):
                continue
            x, y = (blk["x0"] + blk["x1"]) / 2, (blk["y0"] + blk["y1"]) / 2
            q = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
            if q >= 1:
                continue
            zk = zkeep + rise * q ** 1.5 + jag * _noise2(x * 1.7, y * 1.7, s)
            if zmin is not None:
                zk = max(zk, zmin)
            if blk["z1"] > zk + 0.05:
                small = max(blk["x1"] - blk["x0"], blk["y1"] - blk["y0"], blk["z1"] - blk["z0"]) < 2.2
                if (blk["z0"] < zk - 0.1 and small and not blk["step"] and not blk["wedge"]
                        and self.rng.random() < tip):
                    blk["tilt"] = (self.rng.uniform(-0.35, 0.35), self.rng.uniform(-0.35, 0.35),
                                   self.rng.uniform(-0.3, 0.3))
                else:
                    blk["gone"] = True

    def knock(self, cx, cy, rx, ry, share=0.5, seed=None):
        """Knocks treads out of the flights inside an ellipse, leaving the dark risers bare."""
        rng = random.Random(seed) if seed is not None else self.rng
        for blk in self.blocks:
            if blk.get("part") != "tread" or blk.get("gone"):
                continue
            x, y = (blk["x0"] + blk["x1"]) / 2, (blk["y0"] + blk["y1"]) / 2
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 < 1 and rng.random() < share:
                blk["gone"] = True

    def loose(self, cx, cy, rx, ry, n, z0=None, size=(1.5, 1.0, 0.7), tilt=0.45, seed=None, spread=0.25, mat=None):
        """Tumbled ashlar lying about: n blocks strewn in an ellipse, each on
        whatever it fell on (z0 None) or on z0."""
        rng = random.Random(seed) if seed is not None else self.rng
        for i in range(n):
            a = rng.uniform(0, 2 * math.pi)
            d = math.sqrt(rng.uniform(0, 1))
            x, y = cx + math.cos(a) * rx * d, cy + math.sin(a) * ry * d
            sx, sy, sz = (v * rng.uniform(1 - spread, 1 + spread) for v in size)
            z = self.surface(x, y) if z0 is None else z0
            blk = self.block(x - sx / 2, x + sx / 2, y - sy / 2, y + sy / 2, z, z + sz, mat=mat, moss=0.0)
            blk["tilt"] = (rng.uniform(-tilt, tilt), rng.uniform(-tilt, tilt), rng.uniform(0, math.pi))
            blk["loose"] = True

    def heap(self, cx, cy, rx, ry, h, z0=None, stones=None, seed=None, lumps=0.18, big=0, chips=None, follow=False):
        """A heap of broken stone: a low dark core buried under 30 to 60 chips
        of 0.2 to 0.5 cells, dressed faces and sooty breaks mixed, with big
        tumbled blocks half sunk in it. It lies on the stone left standing
        under its middle unless z0 is given; follow drapes it over the stone
        under each point instead, for rubble spilled down a flight."""
        rng = random.Random(seed) if seed is not None else self.rng
        if z0 is None:
            z0 = max(0.0, self.surface(cx, cy) - 0.2 * h)
        dark = self.mat("rubble_dark", 0.95)
        ph = [rng.uniform(0, 6.28) for _ in range(4)]
        hc = h * 0.45

        def base(x, y):
            return self.surface(x, y) - 0.05 if follow else z0

        def core(x, y):
            q = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
            return base(x, y) + hc * max(0.0, 1 - q) ** 0.65

        def fn(u, v):
            a = 2 * math.pi * u
            t = 1 - v
            rr = t * 0.92 * (1.0 + 0.12 * math.sin(4 * a + ph[0]) + 0.08 * math.sin(7 * a + ph[1]))
            x, y = cx + math.cos(a) * rx * rr, cy + math.sin(a) * ry * rr
            z = base(x, y) + hc * (1 - t * t) ** 0.65 * (1 + lumps * math.sin(5 * a + 4 * t + ph[2]) * (1 - t))
            return (x, y, z)
        # the bed of grit between the chips, mid brown so the heap reads as broken stone, not a boulder
        grid_surface(self.b.bm(self.mat("rubble", 0.95)), fn, 9, 2, closed_u=True)
        n = chips or int(min(60, max(30, 24 + 4.0 * rx * ry)))
        mats = self.tone + [self.mat("soot"), self.mat("rubble", 0.95), dark]
        weights = (0.3, 0.3, 0.18, 0.12, 0.06, 0.04)
        placed = []
        for i in range(n):
            a = rng.uniform(0, 2 * math.pi)
            d = math.sqrt(rng.uniform(0, 1)) * rng.uniform(0.85, 1.08)
            x, y = cx + math.cos(a) * rx * d, cy + math.sin(a) * ry * d
            s = rng.uniform(0.2, 0.5) * (1.0 - 0.2 * min(1.0, d))
            z = core(x, y) - s * 0.25
            # a chip may lie on one laid before it
            for px, py, pz, ps in placed:
                if math.hypot(x - px, y - py) < (ps + s) * 0.45:
                    z = max(z, pz + ps * 0.35)
            placed.append((x, y, z, s))
            r_ = rng.random() * sum(weights)
            m = mats[-1]
            for mm, w in zip(mats, weights):
                r_ -= w
                if r_ <= 0:
                    m = mm
                    break
            bm = self.b.bm(m)
            rot = (rng.uniform(-0.7, 0.7), rng.uniform(-0.7, 0.7), rng.uniform(0, math.pi))
            kind = rng.random()
            # chips sit sunk in the heap, so none of them needs an underside
            if kind < 0.3:
                w, dd, hh = s * rng.uniform(1.0, 1.6), s * rng.uniform(0.7, 1.0), s * rng.uniform(0.4, 0.7)
                M = Matrix.Translation(Vector((x, y, z - hh * 0.2))) @ Matrix.Rotation(rot[2], 4, "Z") @ \
                    Matrix.Rotation(rot[0] * 0.6, 4, "X") @ Matrix.Rotation(rot[1] * 0.6, 4, "Y")
                pts = [M @ Vector((sx * w / 2, sy * dd / 2, sz * hh)) for sz in (0, 1) for sy in (-1, 1) for sx in (-1, 1)]
                poly(bm, [tuple(v) for v in pts], [(4, 5, 7, 6), (0, 1, 5, 4), (1, 3, 7, 5), (3, 2, 6, 7), (2, 0, 4, 6)])
            elif kind < 0.85:
                # a wedge of split stone: a triangular prism
                w, dd, hh = s * rng.uniform(0.9, 1.4), s * rng.uniform(0.6, 1.0), s * rng.uniform(0.4, 0.8)
                M = Matrix.Translation(Vector((x, y, z))) @ Matrix.Rotation(rot[2], 4, "Z") @ \
                    Matrix.Rotation(rot[0] * 0.5, 4, "X")
                pts = [M @ Vector(v) for v in ((-w / 2, -dd / 2, 0), (w / 2, -dd / 2, 0), (w * rng.uniform(-0.3, 0.3), dd / 2, 0),
                                               (-w / 2, -dd / 2, hh), (w / 2, -dd / 2, hh * rng.uniform(0.3, 1.0)),
                                               (w * rng.uniform(-0.3, 0.3), dd / 2, hh * rng.uniform(0.5, 1.2)))]
                poly(bm, [tuple(v) for v in pts], [(3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)])
            else:
                aa = rng.uniform(0, 6.28)
                pts = [(x + math.cos(aa + k * 2.1) * s * rng.uniform(0.6, 1.0), y + math.sin(aa + k * 2.1) * s *
                        rng.uniform(0.6, 1.0), z) for k in range(3)]
                pts.append((x + rng.uniform(-0.3, 0.3) * s, y + rng.uniform(-0.3, 0.3) * s, z + s * rng.uniform(0.6, 1.0)))
                poly(bm, pts, [(0, 1, 3), (1, 2, 3), (2, 0, 3)])
        for i in range(big):
            a = rng.uniform(0, 2 * math.pi)
            d = math.sqrt(rng.uniform(0, 1)) * 0.75
            x, y = cx + math.cos(a) * rx * d, cy + math.sin(a) * ry * d
            sx, sy, sz = 1.3 * rng.uniform(0.8, 1.2), 0.9 * rng.uniform(0.8, 1.2), 0.65 * rng.uniform(0.8, 1.2)
            z = core(x, y) - sz * 0.3
            blk = self.block(x - sx / 2, x + sx / 2, y - sy / 2, y + sy / 2, z, z + sz, moss=0.0)
            blk["tilt"] = (rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), rng.uniform(0, math.pi))
            blk["loose"] = True

    def pebbles(self, cx, cy, rx, ry, n, seed=None, r=(0.08, 0.2)):
        """Chips of stone scattered on the ground about a ruin."""
        rng = random.Random(seed) if seed is not None else self.rng
        dirt, dark = self.mat("rubble", 0.95), self.mat("rubble_dark", 0.95)
        for i in range(n):
            a = rng.uniform(0, 2 * math.pi)
            d = math.sqrt(rng.uniform(0, 1))
            chip(self.b.bm(dark if i % 2 else dirt), (cx + math.cos(a) * rx * d, cy + math.sin(a) * ry * d),
                 rng.uniform(*r), rng)

    # ---- output
    def _top(self, q, x0, x1, y0, y1, z, rng, m):
        """The top of a block: plain, split by a dark nosing strip, stained, or with moss creeping in from an edge."""
        b = self.b
        if q.get("stain"):
            # a soft soot stain: the top cut into small squares over the stain,
            # each shaded by its ragged distance from the stain's middle
            scx, scy, srx, sry = q["stain"]
            sy0, sy1 = max(y0, scy - sry * 1.1), min(y1, scy + sry * 1.1)
            if sy1 <= sy0:
                _face(b.bm(m), "top", x0, x1, y0, y1, z, z)
                return
            for a0, a1 in ((y0, sy0), (sy1, y1)):
                if a1 - a0 > 1e-3:
                    _face(b.bm(m), "top", x0, x1, a0, a1, z, z)
            nx, ny = max(1, round((x1 - x0) / 0.17)), max(1, round((sy1 - sy0) / 0.17))
            levels = [self.mat("stain"), self.mat("stain_mid"), self.mat("stain_light")]
            for i in range(nx):
                for j in range(ny):
                    a0, a1 = x0 + (x1 - x0) * i / nx, x0 + (x1 - x0) * (i + 1) / nx
                    c0, c1 = sy0 + (sy1 - sy0) * j / ny, sy0 + (sy1 - sy0) * (j + 1) / ny
                    mx, my = (a0 + a1) / 2, (c0 + c1) / 2
                    d = (math.hypot((mx - scx) / srx, (my - scy) / sry) + 0.2 * _noise2(mx * 2.3, my * 2.3, 3.0)
                         + rng.uniform(-0.08, 0.08))
                    mm = levels[0] if d < 0.45 else levels[1] if d < 0.72 else levels[2] if d < 0.95 else m
                    _face(b.bm(mm), "top", a0, a1, c0, c1, z, z)
            return
        if q.get("edge"):
            ew = 0.07
            side = q["edge"]
            em = self.mat("edge")
            if side == "front":
                _face(b.bm(em), "top", x0, x1, y0, y0 + ew, z, z)
                _face(b.bm(m), "top", x0, x1, y0 + ew, y1, z, z)
            elif side == "back":
                _face(b.bm(em), "top", x0, x1, y1 - ew, y1, z, z)
                _face(b.bm(m), "top", x0, x1, y0, y1 - ew, z, z)
            elif side == "left":
                _face(b.bm(em), "top", x0, x0 + ew, y0, y1, z, z)
                _face(b.bm(m), "top", x0 + ew, x1, y0, y1, z, z)
            else:
                _face(b.bm(em), "top", x1 - ew, x1, y0, y1, z, z)
                _face(b.bm(m), "top", x0, x1 - ew, y0, y1, z, z)
            return
        if not q["moss"]:
            _face(b.bm(m), "top", x0, x1, y0, y1, z, z)
            return
        mm = self.mat("moss" if rng.random() < 0.6 else "moss2")
        self._moss_top(x0, x1, y0, y1, lambda x, y: z, rng, m, mm, q["moss_front"])

    def _moss_top(self, x0, x1, y0, y1, zf, rng, m, mm, front):
        """A top with moss creeping in from the front or back edge, its inner
        edge ragged; zf(x, y) is the top's height, so a slope can carry it."""
        b = self.b
        f = rng.uniform(0.25, 0.6)
        xs = [x0, x0 + (x1 - x0) * rng.uniform(0.2, 0.4), x0 + (x1 - x0) * rng.uniform(0.55, 0.8), x1]
        ds = [min(0.9, max(0.1, f + rng.uniform(-0.22, 0.22))) * (y1 - y0) for _ in xs]
        if front:
            line = [(x, y0 + d) for x, d in zip(xs, ds)]
            moss_ = [(x0, y0), (x1, y0)] + line[::-1]
            rest = line + [(x1, y1), (x0, y1)]
        else:
            line = [(x, y1 - d) for x, d in zip(xs, ds)]
            moss_ = line + [(x1, y1), (x0, y1)]
            rest = [(x0, y0), (x1, y0)] + line[::-1]
        poly(b.bm(mm), [(x, y, zf(x, y)) for x, y in moss_], [tuple(range(len(moss_)))])
        poly(b.bm(m), [(x, y, zf(x, y)) for x, y in rest], [tuple(range(len(rest)))])

    def _shrink(self, st, g):
        """How far each side of a block is drawn in from its cell: half the
        joint where a neighbour or nothing lies, none where a neighbour lies
        against part of it (the two faces meet and no seam can open)."""
        return {k: (0.0 if st[k] == 1 else g / 2) for k in SIDES}

    def _core_box(self, x0, x1, y0, y1, st, d_in):
        """The core's sides: set back behind a face that shows, pushed on into
        the neighbour behind a face that is wholly covered, so every joint
        it opens onto is backed."""
        off = {k: (-d_in if st[k] == 2 else d_in) for k in SIDES}
        return x0 + off["left"], x1 - off["right"], y0 + off["front"], y1 - off["back"]

    def _wedge(self, q, cover, rng, gap, d_in):
        x0, x1, y0, y1, z0 = (q[k] for k in ("x0", "x1", "y0", "y1", "z0"))
        ax, h0, h1 = q["wedge"]
        hy0, hy1 = (h0, h1) if ax == "y" else (max(h0, h1),) * 2
        hmax = max(h0, h1)
        sides = [("front", 1, y0, -1, ((x0, x1), (z0, hy0))), ("back", 1, y1, 1, ((x0, x1), (z0, hy1))),
                 ("left", 0, x0, -1, ((y0, y1), (z0, hmax))), ("right", 0, x1, 1, ((y0, y1), (z0, hmax)))]
        st = {s[0]: cover.cover(*s[1:]) for s in sides}
        g = gap * rng.uniform(0.6, 1.3) if q["gap"] else 0.0
        sh = self._shrink(st, g)

        def shape(a0, a1, b0, b1, t0, t1, bm, which):
            pts = [(a0, b0, z0), (a1, b0, z0), (a1, b1, z0), (a0, b1, z0)]
            if ax == "y":
                tops = [(a0, b0, t0), (a1, b0, t0), (a1, b1, t1), (a0, b1, t1)]
            else:
                tops = [(a0, b0, t0), (a1, b0, t1), (a1, b1, t1), (a0, b1, t0)]
            faces = {"top": (4, 5, 6, 7), "front": (0, 1, 5, 4), "right": (1, 2, 6, 5), "back": (2, 3, 7, 6),
                     "left": (3, 0, 4, 7)}
            poly(bm, pts + tops, [faces[k] for k in which])
        X0, X1, Y0, Y1 = x0 + sh["left"], x1 - sh["right"], y0 + sh["front"], y1 - sh["back"]
        t0, t1 = h0 - g * 0.4, h1 - g * 0.4
        # moss on the slope only where a part asks for it (moss_slope), so older stair cheeks stay as they were
        mossy = q["moss"] and q.get("moss_slope")
        shape(X0, X1, Y0, Y1, t0, t1, self.b.bm(q["mat"]), ([] if mossy else ["top"]) + [k for k in SIDES if st[k] != 2])
        if mossy:
            def zf(x, y):
                return t0 + (t1 - t0) * ((y - Y0) / (Y1 - Y0) if ax == "y" else (x - X0) / (X1 - X0))
            mm = self.mat("moss" if rng.random() < 0.6 else "moss2")
            self._moss_top(X0, X1, Y0, Y1, zf, rng, q["mat"], mm, rng.random() < 0.5)
        if q["gap"]:
            cx0, cx1, cy0, cy1 = self._core_box(x0, x1, y0, y1, st, d_in)
            a0, a1, c0, c1 = (y0, y1, cy0, cy1) if ax == "y" else (x0, x1, cx0, cx1)

            def h(c):  # the true slope's height over the core's own ends
                return h0 + (h1 - h0) * (c - a0) / (a1 - a0)
            shape(cx0, cx1, cy0, cy1, h(c0) - 0.07, h(c1) - 0.07, self.b.bm(self.mat("joint")),
                  ["top"] + [k for k in SIDES if st[k] != 2])

    def emit(self):
        b, p = self.b, self.p
        rng = random.Random(p.get("seed", 1) * 7 + 3)
        gap = p.get("gap", 0.05)
        d_in = gap * 0.65 + 0.02  # the core sits this far behind a face, clear of the widest joint
        live = [q for q in self.blocks if not q.get("gone") and q["tilt"] is None]
        cover = _Cover([s for q in live for s in _solids(q)])
        ruined = any(q.get("gone") or q["tilt"] is not None for q in self.blocks if not q.get("loose"))
        was = _Cover([s for q in self.blocks if not q.get("loose") for s in _solids(q)]) if ruined else None
        soot, grime = self.mat("soot"), self.mat("grime")
        dirt = p.get("dirt_zones", ())
        cores, floor = [], []
        for q in live:
            if q["wedge"]:
                self._wedge(q, cover, rng, gap, d_in)
                continue
            x0, x1, y0, y1, z0, z1 = (q[k] for k in ("x0", "x1", "y0", "y1", "z0", "z1"))
            sides = _box_sides(x0, x1, y0, y1, z0, z1)
            st = {s[0]: cover.cover(*s[1:]) for s in sides}
            vis = {k: v != 2 for k, v in st.items()}
            if not any(vis.values()):
                if q["gap"]:
                    # buried: nothing drawn, but its core keeps the others whole so they merge
                    cx0, cx1, cy0, cy1 = self._core_box(x0, x1, y0, y1, st, d_in)
                    cores.append([cx0, cx1, cy0, cy1, z0, z1, set()])
                continue
            broken = {s[0]: bool(was is not None and vis[s[0]] and was.covered(*s[1:])) for s in sides}
            cx_, cy_ = (x0 + x1) / 2, (y0 + y1) / 2
            dirty = any(((cx_ - zx) / zrx) ** 2 + ((cy_ - zy) / zry) ** 2 < 1 and rng.random() < share
                        for zx, zy, zrx, zry, share in dirt)
            grimy = dirty or (ruined and z0 < 0.05 and not q["step"])
            m = soot if dirty else q["mat"]
            gx0, gx1, gy0, gy1, gz1 = x0, x1, y0, y1, z1
            if q["gap"]:
                rough = q.get("rough", 0.0)
                g = gap * rng.uniform(0.6, 1.3) * (2.0 if rough else 1.0)
                sh = self._shrink(st, g)
                gx0, gx1, gy0, gy1 = x0 + sh["left"], x1 - sh["right"], y0 + sh["front"], y1 - sh["back"]
                if st["front"] == 0:
                    gy0 -= rng.uniform(0, 0.015)
                if st["top"] != 1:
                    gz1 = z1 - g * 0.4 - rng.uniform(0, 0.02) + (rng.uniform(-rough, rough * 0.5) if rough else 0.0)
                # the core clears the widest joint a stone of this kind can have
                gmax = gap * 1.3 * (2.0 if rough else 1.0)
                cx0, cx1, cy0, cy1 = self._core_box(x0, x1, y0, y1, st, gmax / 2 + 0.02)
                # a core side set into a covered neighbour never shows
                need = {k for k in SIDES if st[k] != 2} | ({"top"} if vis["top"] else set())
                cores.append([cx0, cx1, cy0, cy1, z0, z1 - ((gmax * 0.4 + 0.04 + rough) if vis["top"] else 0.0), need])
                if z0 < 0.01:
                    floor.append([x0, x1, y0, y1, 0.0, 0.0])
            q["moss_front"] = rng.random() < 0.7
            for side in SIDES:
                if not vis.get(side):
                    continue
                sm = soot if broken[side] else (grime if grimy else m)
                if side == "front" and q["moss"] and q["moss_front"] and vis["top"]:
                    lip = min(gz1 - z0, rng.uniform(0.06, 0.12))
                    _face(b.bm(sm), side, gx0, gx1, gy0, gy1, z0, gz1 - lip)
                    _face(b.bm(self.mat("moss2")), side, gx0, gx1, gy0, gy1, gz1 - lip, gz1)
                else:
                    _face(b.bm(sm), side, gx0, gx1, gy0, gy1, z0, gz1)
            if vis["top"]:
                self._top(q, gx0, gx1, gy0, gy1, gz1, rng, soot if broken["top"] and rng.random() < 0.35 else m)
        # tumbled blocks, some of them sooty
        for q in self.blocks:
            if q.get("gone") or q["tilt"] is None:
                continue
            x0, x1, y0, y1, z0, z1 = (q[k] for k in ("x0", "x1", "y0", "y1", "z0", "z1"))
            s = (x1 - x0, y1 - y0, z1 - z0)
            m = soot if ruined and rng.random() < 0.3 else q["mat"]
            bm = b.bm(m)
            vs = box(bm, ((x0 + x1) / 2, (y0 + y1) / 2, z0 + s[2] / 2), s, rot=q["tilt"], base=False)
            lo = min(v.co.z for v in vs)
            base_z = z0 if q.get("loose") else z0 - s[2] * 0.3
            for v in vs:
                v.co.z += base_z - lo
            if q.get("loose"):
                # the face it lies on is never seen
                faces = {f for v in vs for f in v.link_faces}
                under = min(faces, key=lambda f: f.calc_center_median().z)
                bmesh.ops.delete(bm, geom=[under], context="FACES_ONLY")

        # the dark joint behind the stones: the cores joined into as few boxes as
        # will make them, each drawing only the sides a joint can open onto
        merged = _merge_boxes([c for c in cores if c[1] - c[0] > 1e-3 and c[3] - c[2] > 1e-3 and c[5] - c[4] > 1e-3])
        ccover = _Cover([((c[0], c[2], c[4]), (c[1], c[3], c[5]), None) for c in merged]
                        + [_solid(q) for q in live if not q["gap"] and not q["wedge"]])
        jm = b.bm(self.mat("joint"))
        for c in merged:
            for side, ax, pl, sg, span in _box_sides(*c[:6]):
                if side in c[6] and not ccover.covered(ax, pl, sg, span):
                    _face(jm, side, *c[:6])
        # and a dark floor under the lowest course, for any joint that runs to the ground
        for c in _merge_boxes(floor):
            _face(jm, "top", c[0], c[1], c[2], c[3], 0.004, 0.004)


def temple(b, p):
    """A Zhon temple or tomb laid in dressed stone. p["parts"] is a list of
    (method, kwargs) run on a Mason in order; p["ruin"] a list of the same
    for the damage (collapse, knock, heap, loose, pebbles), run after the parts."""
    m = Mason(b, p, random.Random(p.get("seed", 1)))
    for name, kw in list(p["parts"]) + list(p.get("ruin", [])):
        getattr(m, name)(**kw)
    m.emit()
    # every part is wound outward already; open-bottomed blocks would confuse a recalculation
    b.sheets.update(k for k in b.parts if k.startswith("st_"))
