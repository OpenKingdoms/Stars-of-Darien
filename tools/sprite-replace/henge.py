"""Standing stones (the AraHenge, TarHenge, VerHenge and ZonHenge features)
built as solid granite blocks from the stone list in henges.json, run
inside Blender.

    blender -b --factory-startup --python tools/sprite-replace/henge.py -- <out dir> [names] [--catalog <dir>]

Every stone is one closed block of the listed width, thickness, height,
taper and top, turned by yaw and tipped by lean about its base, with
square, slightly uneven edges and a gently rolling surface seeded by the
sprite's name. Planar cuts make chamfers and flat breaks, and grooves cut
carved lines and cracks into the face where the sprite shows them, by
exact booleans, so every stone stays watertight. The faces the classic
camera saw take the sprite, its
painted light divided out and its broad shading flattened, so carvings,
cracks and weathering stay dark but no face is black. The faces it never
saw take a mottled granite tile in the stone's colour.

Writes <out>/models/<Name>.glb, and <out>/renders/<Name>_classic.png and
_turned.png for `python handcompare.py <out>/renders <Name> <sprite> 3`.
The catalog (catalog.json and sprites/) is the nearest folder above the
out dir that has one, unless --catalog names it. HENGE_DIFF=1 also writes
<Name>_fit.png, the classic silhouette against the sprite's. HENGE_FIT=1
prints the light that best explains the sprites' shading, where LIGHT
below comes from.

Same frame as carve.py: one unit is one cell, -Y toward the classic
camera, Z up, the origin at the sprite's hotspot on the ground.
"""
import json
import math
import os
import random
import re
import sys
import traceback
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import carve  # noqa: E402
import handkit as hk  # noqa: E402

CELL, TILT = carve.CELL, carve.TILT
VIEW = Vector((0.0, -1.0, 2.0)).normalized()  # from the ground toward the classic camera
PROP = 45.0  # tipped further than this it rests on an edge, not on its base
SEAT = 0.03  # a stone resting on others sinks this far into them, so no dark seam shows between
RING = 40  # points round each cross-section
STEP = 0.1  # cells between cross-sections
SEEN = 0.1  # faces turned further from the camera than this would smear the sprite
TILE = 64  # the granite tile's period in pixels, at the sprite's 16 pixels a cell
# the classic renderer's light, fitted to the henge sprites: shade = AMBIENT + DIRECT * max(0, n.LIGHT)
LIGHT = Vector((-0.565, -0.1, 0.819)).normalized()
AMBIENT, DIRECT = 0.33, 0.64


def load_henges(path=None):
    path = path or os.path.join(HERE, "henges.json")
    return {h["name"]: h for h in json.load(open(path))["henges"]}


def find_catalog(out):
    d = os.path.abspath(out)
    while True:
        if os.path.exists(os.path.join(d, "catalog.json")):
            return d
        up = os.path.dirname(d)
        if up == d:
            raise SystemExit("no catalog.json above " + out + ", pass --catalog")
        d = up


def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


def to_linear(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(np.asarray(c, np.float32), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def luma(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


def granite(s, level):
    """The stone's sampled colour at the sprite's mean brightness (lifted by
    carve.ALBEDO_GAIN in carve.Sprite) times its gain, in linear light."""
    c = to_linear(hex_rgb(s["colour"]))
    return c * (level * s.get("gain", 1.0) / max(1e-4, float(luma(c))))


# ---- one stone ----------------------------------------------------------

def superellipse(a, b, p, t):
    c, s = math.cos(t), math.sin(t)
    return a * math.copysign(abs(c) ** (2 / p), c), b * math.copysign(abs(s) ** (2 / p), s)


def section(a, b, p):
    """RING points round a superellipse with their outward normals, spaced
    partly by length and partly by turn, so the corners keep enough points
    to stay square without the flat sides going coarse."""
    n = 2880
    pts, nrm = [], []
    for i in range(n):
        x, y = superellipse(a, b, p, 2 * math.pi * i / n)
        gx = math.copysign(abs(x / a) ** (p - 1), x) / a
        gy = math.copysign(abs(y / b) ** (p - 1), y) / b
        ln = math.hypot(gx, gy) or 1.0
        pts.append((x, y))
        nrm.append((gx / ln, gy / ln))
    ds = [math.dist(pts[i], pts[(i + 1) % n]) for i in range(n)]
    da = [abs((math.atan2(nrm[(i + 1) % n][1], nrm[(i + 1) % n][0]) - math.atan2(nrm[i][1], nrm[i][0])
               + math.pi) % (2 * math.pi) - math.pi) for i in range(n)]
    per, turn = sum(ds), sum(da)
    acc = [0.0]
    for i in range(n):
        acc.append(acc[-1] + 0.55 * ds[i] / per + 0.45 * da[i] / turn)
    out_p, out_n, j = [], [], 0
    for k in range(RING):
        target = acc[-1] * k / RING
        while acc[j + 1] < target:
            j += 1
        out_p.append(pts[j])
        out_n.append(nrm[j])
    return out_p, out_n


def lerp_profile(prof, f):
    """Width and sideways shift at fraction f of the length, from rows of
    [fraction, width, shift]."""
    if f <= prof[0][0]:
        return prof[0][1], prof[0][2] if len(prof[0]) > 2 else 0.0
    for r0, r1 in zip(prof, prof[1:]):
        if f <= r1[0]:
            t = (f - r0[0]) / max(1e-9, r1[0] - r0[0])
            d0 = r0[2] if len(r0) > 2 else 0.0
            d1 = r1[2] if len(r1) > 2 else 0.0
            return r0[1] + (r1[1] - r0[1]) * t, d0 + (d1 - d0) * t
    return prof[-1][1], prof[-1][2] if len(prof[-1]) > 2 else 0.0


def stone_mesh(s, seed, standing, stub):
    """The stone in its own frame: width on x, thickness on y, height up z
    from its base centre, as rings of points from the bottom to the top."""
    rng = random.Random(seed)
    h = s["height"]
    a0, b0 = s["width"] / 2, s["thickness"] / 2
    small = min(a0, b0)
    rubble = s["pose"] == "rubble"
    p = s.get("p") or (rng.uniform(5.0, 6.0) if rubble else rng.uniform(6.5, 7.5))
    base, norms = section(a0, b0, p)
    rb = min(s.get("round", 0.05), 0.25 * small)  # edge rounding
    offs = [Vector((rng.uniform(-50, 50), rng.uniform(-50, 50), rng.uniform(-50, 50))) for _ in range(4)]
    lo_amp = (0.08 * small + 0.012) if rubble else (0.04 * small + 0.008)
    hi_amp = min(0.015, 0.04 * small)
    wobble = min(0.04, 0.03 * h)
    tw = s.get("taper_w", s["taper"])
    tt = s.get("taper_t", s["taper"])
    hold = s.get("taper_hold", [0.0, 0.0])
    prof = s.get("profile")

    def sect(z):
        """x scale, y scale and the centre's shift at height z."""
        f = min(1.0, max(0.0, z / h))
        sx, sy = 1.0 + (tw - 1.0) * f, 1.0 + (tt - 1.0) * f
        cx, cy = (1.0 - sx) * hold[0] * a0, (1.0 - sy) * hold[1] * b0
        if prof:
            w, dx = lerp_profile(prof, f)
            sx, cx = w / (2 * a0), cx + dx
        return sx, sy, cx, cy

    def axis_off(z):
        # the stone's middle wanders a little up its length
        q = Vector((0.0, 0.0, z * 0.45)) + offs[2]
        return wobble * noise.noise(q), wobble * noise.noise(q + Vector((17.0, 3.0, 0.0)))

    def ring(z, inset, tilt, k=1.0, off=None):
        pts = []
        c = math.sqrt(max(0.0, 1.0 - tilt * tilt))
        for i in range(RING):
            zi = z[i] if isinstance(z, list) else z
            sx, sy, cx, cy = sect(zi)
            sx, sy = max(1e-3, sx * k), max(1e-3, sy * k)
            (bx, by), (nx, ny) = base[i], norms[i]
            mx, my = nx / sx, ny / sy
            ln = math.hypot(mx, my) or 1.0
            mx, my = mx / ln, my / ln
            ins = min(inset, 0.6 * small * min(sx, sy))
            ox, oy = axis_off(zi) if off is None else off
            pts.append((Vector((bx * sx - mx * ins + cx + ox, by * sy - my * ins + cy + oy, zi)),
                        Vector((mx * c, my * c, tilt))))
        return pts

    def centre(z):
        sx, sy, cx, cy = sect(z)
        ox, oy = axis_off(z)
        return cx + ox, cy + oy

    def shrink(r, f, zf=None, up=1.0):
        cx = sum(P.x for P, _ in r) / RING
        cy = sum(P.y for P, _ in r) / RING
        out = []
        for P, _ in r:
            x, y = cx + (P.x - cx) * f, cy + (P.y - cy) * f
            out.append((Vector((x, y, zf(x, y) if zf else P.z)), Vector((0.0, 0.0, up))))
        return out, (cx, cy)

    def jag(amount, sign, z):
        """A broken face, in cells in from the end: a tilted plane, rough but
        higher in the middle than at the rim, so it never dishes into a hole."""
        beta = rng.uniform(0, 2 * math.pi)
        slope = rng.uniform(0.25, 0.4)
        sx, sy, _, _ = sect(z)
        a, b = a0 * sx, b0 * sy
        ccx, ccy = centre(z)

        def f(x, y):
            u, v = (x - ccx) / a, (y - ccy) / b
            plane = (u * math.cos(beta) + v * math.sin(beta)) / 1.2
            rim = min(1.0, u * u + v * v)
            q = Vector((x * 3.0, y * 3.0, sign * 5.0)) + offs[3]
            d = 0.55 + slope * plane + 0.15 * noise.noise(q) - 0.35 * (1.0 - rim)
            return amount * max(0.0, min(1.0, d))
        return f

    rings = []
    # the bottom
    if standing:
        z_side = -stub
        rings.append(ring(-stub, 0.0, -1.0))
        bottom_centre = Vector((*centre(-stub), -stub))
    else:
        broken = s.get("base_shape", "flat" if s["pose"] == "lintel" else "broken") == "broken"
        amount = min(0.14, 0.12 * h) if broken else 0.0
        rough = jag(amount, -1.0, 0.0) if broken else (lambda x, y: 0.0)
        rim = ring(0.0, rb, -1.0)
        lift = [rough(P.x, P.y) for P, _ in rim]
        rim = [(Vector((P.x, P.y, z)), d) for (P, d), z in zip(rim, lift)]
        inner2, c = shrink(rim, 0.3, lambda x, y: rough(x, y), -1.0)
        inner1, _ = shrink(rim, 0.65, lambda x, y: rough(x, y), -1.0)
        bottom_centre = Vector((c[0], c[1], rough(c[0], c[1])))
        rings += [inner2, inner1, rim]
        # the rounded edge follows the rough end, so it never stands proud as a lip
        for phi in (60, 30):
            f = math.radians(phi)
            rings.append(ring([z + rb - rb * math.sin(f) for z in lift], rb * (1 - math.cos(f)), -math.sin(f)))
        z_side = rb + amount

    shape = s["top_shape"]
    top_centre = None
    if shape == "rounded":
        sx, sy, _, _ = sect(h)
        hd = min(0.45 * h, 0.75 * max(a0 * sx, b0 * sy))
        z_top = h - hd
    elif shape == "pointed":
        z_top = h * s.get("point_from", 0.55)
    elif shape == "broken":
        depth = s.get("break_depth", min(0.35, 0.3 * h))
        brk = jag(depth, 1.0, h)
        z_top = h - depth - 0.1
    else:
        z_top = h - rb

    # the sides
    n = max(1, int(math.ceil((z_top - z_side) / STEP)))
    first = 0 if not standing else 1
    for i in range(first, n + 1):
        rings.append(ring(z_side + (z_top - z_side) * i / n, 0.0, 0.0))

    # the top
    if shape == "rounded":
        steps = 6
        for k in range(1, steps + 1):
            phi = math.pi / 2 * k / (steps + 1)
            z = z_top + hd * math.sin(phi)
            sx, sy, cx, cy = sect(z_top)
            ox, oy = axis_off(z_top)
            rings.append(ring(z, 0.0, math.sin(phi), math.cos(phi), (ox, oy)))
        top_centre = Vector((*centre(z_top), h))
    elif shape == "pointed":
        tip = (rng.uniform(-0.25, 0.25) * a0 * tw, rng.uniform(-0.25, 0.25) * b0 * tt)
        steps = 8
        for k in range(1, steps):
            v = k / steps
            z = z_top + (h - z_top) * v
            ox, oy = axis_off(z)
            rings.append(ring(z, 0.0, 0.25 + 0.5 * v, max(0.04, (1 - v) ** 0.9),
                              (ox + tip[0] * v * v, oy + tip[1] * v * v)))
        cx, cy = centre(h)
        top_centre = Vector((tip[0] + cx, tip[1] + cy, h))
    elif shape == "broken":
        rim = ring(h, 0.0, 0.3)
        ccx, ccy = centre(h)
        chip_amt = s.get("chip", 0.25)
        chipped = []
        for P, d in rim:
            q = Vector((P.x * 2.2, P.y * 2.2, 11.0)) + offs[1]
            chip = max(0.0, noise.noise(q)) * chip_amt
            x, y = ccx + (P.x - ccx) * (1 - chip), ccy + (P.y - ccy) * (1 - chip)
            chipped.append((Vector((x, y, h - brk(x, y) - chip * 0.25)), d))
        rings.append(chipped)
        inner1, _ = shrink(chipped, 0.66, lambda x, y: h - brk(x, y))
        inner2, c = shrink(chipped, 0.3, lambda x, y: h - brk(x, y))
        rings += [inner1, inner2]
        top_centre = Vector((c[0], c[1], h - brk(c[0], c[1])))
    else:
        for phi in (30, 60, 90):
            f = math.radians(phi)
            rings.append(ring(h - rb + rb * math.sin(f), rb * (1 - math.cos(f)), math.sin(f)))
        dome = 0.01 + 0.01 * small
        sx, sy, _, _ = sect(h)
        ccx, ccy = centre(h)

        def top(x, y):
            return h + dome * max(0.0, 1.0 - ((x - ccx) / (a0 * sx)) ** 2 - ((y - ccy) / (b0 * sy)) ** 2)
        inner1, _ = shrink(rings[-1], 0.62, top)
        inner2, c = shrink(rings[-1], 0.28, top)
        rings += [inner1, inner2]
        top_centre = Vector((c[0], c[1], h + dome))

    # the rolling surface: every point pushed along its own outward direction
    def displace(P, d, amp=1.0):
        q1 = P * 0.9 + offs[0]
        q2 = P * 2.6 + offs[1]
        return P + d * amp * (lo_amp * noise.noise(q1) + hi_amp * noise.noise(q2))

    bm = bmesh.new()
    vb = bm.verts.new(displace(bottom_centre, Vector((0, 0, -1)), 0.5))
    rows = [[bm.verts.new(displace(P, d)) for P, d in r] for r in rings]
    vt = bm.verts.new(displace(top_centre, Vector((0, 0, 1)), 0.5))
    for k in range(RING):
        k1 = (k + 1) % RING
        bm.faces.new((vb, rows[0][k1], rows[0][k]))
        for a, b in zip(rows, rows[1:]):
            bm.faces.new((a[k], a[k1], b[k1], b[k]))
        bm.faces.new((rows[-1][k], rows[-1][k1], vt))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def bend(bm, s, M):
    """Curves a lying stone in plan along its length: bend_deg turns its
    heading clockwise evenly, and each (at, deg) in kinks turns it over a
    short stretch there, for a lintel listed as two halves."""
    deg = s.get("bend_deg") or 0.0
    kinks = s.get("kinks") or []
    if not deg and not kinks:
        return
    h = s["height"]
    zone = 0.8

    def turn(t):
        a = math.radians(deg) * min(1.0, max(0.0, t / h))
        for at, d in kinks:
            a += math.radians(d) * min(1.0, max(0.0, (t - at + zone / 2) / zone))
        return a
    L = math.radians(s["lean_dir_deg"])
    right = Vector((math.cos(L), -math.sin(L), 0.0))
    side = 1.0 if (M.to_3x3() @ Vector((1.0, 0.0, 0.0))).dot(right) > 0 else -1.0
    # the centre line, walked in small steps: x across, z along, heading a toward +x
    n = 400
    line = [(0.0, 0.0, 0.0)]
    for i in range(n):
        a = side * turn(h * (i + 0.5) / n)
        x, z, _ = line[-1]
        line.append((x + math.sin(a) * h / n, z + math.cos(a) * h / n, side * turn(h * (i + 1) / n)))
    for v in bm.verts:
        x, y, z = v.co
        f = min(n - 1e-6, max(0.0, z / h * n))
        i = int(f)
        (x0, z0, a0), (x1, z1, a1) = line[i], line[i + 1]
        cx, cz, a = x0 + (x1 - x0) * (f - i), z0 + (z1 - z0) * (f - i), a0 + (a1 - a0) * (f - i)
        past = z - min(h, max(0.0, z))  # beyond either end, carry on straight
        v.co = Vector((cx + x * math.cos(a) + past * math.sin(a), y, cz - x * math.sin(a) + past * math.cos(a)))


def pose(s):
    """Yaw about z, then a tip about the base centre so the top moves toward lean_dir."""
    yaw = math.radians(s["yaw_deg"])
    L = math.radians(s["lean_dir_deg"])
    tip = Matrix.Rotation(math.radians(s["lean_deg"]), 4, Vector((-math.cos(L), math.sin(L), 0.0)))
    return tip @ Matrix.Rotation(yaw, 4, "Z")


def cut(bm, co, no):
    """Cuts away everything on the side no points to and closes the cut."""
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=co, plane_no=no, clear_outer=True)
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def clip_ground(bm):
    """Cuts the stone off at the ground and closes the cut, so it stays solid."""
    cut(bm, (0, 0, 0), (0, 0, -1))


def groove_box(bvh, g, hot):
    """The slot for one groove as a box mesh, or None when either end
    misses the stone."""
    hx, hy = hot
    ends = []
    for sx, sy in g["px"]:
        far = 40.0
        o = Vector(((sx - hx) / CELL, (hy - sy - far * CELL * TILT) / CELL, far))
        loc, nrm, _, _ = bvh.ray_cast(o, -VIEW)
        if loc is not None:
            ends.append((loc, nrm))
    if len(ends) < 2:
        return None
    (a, na), (b, nb) = ends
    along = b - a
    length = along.length
    along.normalize()
    out = (na + nb).normalized()
    across = out.cross(along).normalized()
    out = along.cross(across).normalized()
    if out.dot(na + nb) < 0:
        out = -out
    w, d = g.get("width", 0.1), g.get("depth", 0.05)
    cb = bmesh.new()
    bmesh.ops.create_cube(cb, size=1.0)
    R = Matrix((along, across, out)).transposed().to_4x4()
    mid = (a + b) / 2 + out * (0.1 - d / 2)
    cb.transform(Matrix.Translation(mid) @ R @ Matrix.Diagonal((length + w * 0.5, w, 0.2 + d, 1.0)))
    return cb


def incise(bm, grooves, hot):
    """Cuts each groove into the stone: a slot between two sprite pixels,
    width wide and depth deep, on the face the classic camera saw there.
    Each is taken away by an exact boolean, one at a time, and a cut that
    would leave the stone open or take more than a sliver is skipped."""
    for g in grooves:
        bm.normal_update()
        cb = groove_box(BVHTree.FromBMesh(bm), g, hot)
        if cb is None:
            print("HENGE_GROOVE_MISSED", g["px"], flush=True)
            continue
        me = bpy.data.meshes.new("incise")
        bm.to_mesh(me)
        ob = bpy.data.objects.new("incise", me)
        bpy.context.collection.objects.link(ob)
        cme = bpy.data.meshes.new("groove")
        cb.to_mesh(cme)
        cb.free()
        cob = bpy.data.objects.new("groove", cme)
        bpy.context.collection.objects.link(cob)
        mod = ob.modifiers.new("groove", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cob
        dg = bpy.context.evaluated_depsgraph_get()
        cut_me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        trial = bmesh.new()
        trial.from_mesh(cut_me)
        before, after = bm.calc_volume(), trial.calc_volume()
        closed = trial.faces and all(e.is_manifold for e in trial.edges)
        if closed and 0.9 * before < after <= before:
            bm.clear()
            bm.from_mesh(cut_me)
        else:
            print("HENGE_GROOVE_SKIPPED", g["px"], "volume %.3f -> %.3f" % (before, after), flush=True)
        trial.free()
        for m, o in ((cut_me, None), (cme, cob), (me, ob)):
            if o is not None:
                bpy.data.objects.remove(o)
            bpy.data.meshes.remove(m)


def build_stone(s, seed, hot=None):
    lean = s["lean_deg"]
    standing = lean < PROP
    ext = max(s["width"], s["thickness"]) / 2
    stub = 0.3 + ext * math.tan(math.radians(min(lean, 44.0)))
    bm = stone_mesh(s, seed, standing, stub)
    # chamfers and flat breaks, in the stone's own frame: each cut takes away what lies beyond its plane
    for c in s.get("cuts") or []:
        cut(bm, Vector(c["at"]), Vector(c["n"]).normalized())
    M = pose(s)
    bend(bm, s, M)
    bm.transform(M)
    x, y = s["x"], s["y"]
    if s.get("anchor") == "centre":
        mid = M @ Vector((0.0, 0.0, s["height"] / 2))
        x, y = x - mid.x, y - mid.y
    bm.transform(Matrix.Translation((x, y, 0.0)))
    if standing:
        clip_ground(bm)
    else:
        zmin = min(v.co.z for v in bm.verts)
        zmax = max(v.co.z for v in bm.verts)
        if s.get("underside_z") is not None:
            bm.transform(Matrix.Translation((0.0, 0.0, s["underside_z"] - SEAT - zmin)))
        else:
            sink = min(0.08, 0.1 * (zmax - zmin))
            bm.transform(Matrix.Translation((0.0, 0.0, -sink - zmin)))
            clip_ground(bm)
    if s.get("grooves") and hot is not None:
        incise(bm, s["grooves"], hot)
    bm.normal_update()
    solid = all(e.is_manifold for e in bm.edges) and not any(e.is_boundary for e in bm.edges)
    return bm, solid


def merge_halves(stones):
    """A stone marked `continues` is the second half of an earlier one: the
    two become one stone, kinked where they meet."""
    for b in stones:
        if "continues" not in b:
            continue
        a = stones[b["continues"]]
        turn = (b["lean_dir_deg"] - a["lean_dir_deg"] + 180) % 360 - 180
        a["kinks"] = a.get("kinks", []) + [(a["height"], turn)]
        for k in ("width", "thickness"):
            a[k] = (a[k] + b[k]) / 2
        a["height"] += b["height"]
    return [s for s in stones if "continues" not in s]


def join_lintels(stones):
    """Lintels that meet end to end but stay two stones, as a cracked one
    does: each is lengthened into the other so the crack has no notch."""
    for a in stones:
        if a["pose"] != "lintel" or a.get("anchor") == "centre":
            continue
        la = math.radians(a["lean_dir_deg"])
        end = (a["x"] + a["height"] * math.sin(la), a["y"] + a["height"] * math.cos(la))
        for b in stones:
            if b is a or b["pose"] != "lintel" or b.get("anchor") == "centre":
                continue
            lb = math.radians(b["lean_dir_deg"])
            turn = abs((b["lean_dir_deg"] - a["lean_dir_deg"] + 180) % 360 - 180)
            if math.hypot(b["x"] - end[0], b["y"] - end[1]) < 0.35 and turn < 45:
                ext = 0.2 + max(a["width"], b["width"]) / 2 * math.tan(math.radians(turn) / 2)
                a["height"] += ext
                b["x"] -= ext * math.sin(lb)
                b["y"] -= ext * math.cos(lb)
                b["height"] += ext


def ground_chips(h):
    """Small stones for a sprite whose ground note names a pixel with chips on it."""
    m = re.search(r"chips.*pixel \((\d+), (\d+)\)", h.get("ground") or "")
    return (int(m.group(1)), int(m.group(2))) if m else None


# ---- painting -----------------------------------------------------------

def screen(hx, hy, co):
    return hx + co.x * CELL, hy - co.y * CELL - co.z * CELL * TILT


def shade(n):
    return AMBIENT + DIRECT * max(0.0, n.dot(LIGHT))


def pixel_hits(bvh, w, h, hx, hy):
    """The face under each sprite pixel as the classic camera saw it."""
    far = 40.0
    down = -VIEW
    hits = {}
    for py in range(h):
        for px in range(w):
            sx, sy = px + 0.5, py + 0.5
            o = Vector(((sx - hx) / CELL, (hy - sy - far * CELL * TILT) / CELL, far))
            loc, nrm, idx, _ = bvh.ray_cast(o, down)
            if loc is not None:
                hits[(px, py)] = (idx, nrm.copy())
    return hits


def shift(arr, dy, dx):
    """arr moved by dy rows and dx columns, zeros coming in, nothing wrapping."""
    h, w = arr.shape[:2]
    out = np.zeros_like(arr)
    out[max(dy, 0):h + min(dy, 0), max(dx, 0):w + min(dx, 0)] = \
        arr[max(-dy, 0):h + min(-dy, 0), max(-dx, 0):w + min(-dx, 0)]
    return out


def bleed(rgb, known):
    """Spreads known colours into the rest, as carve.Sprite.bleed does."""
    rgb = rgb.copy()
    known = known.copy()
    while known.any() and not known.all():
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape, np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            k = shift(known, dy, dx)
            acc += shift(rgb, dy, dx) * k[..., None]
            cnt += k
        grow = ~known & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        known = known | grow
    return rgb


def blur(val, mask, sigma):
    """A gaussian mean of val over the pixels in mask, for each pixel."""
    r = max(1, int(round(3 * sigma)))
    k = [math.exp(-i * i / (2 * sigma * sigma)) for i in range(-r, r + 1)]
    m = mask.astype(np.float32)

    def conv(a):
        t = sum(w * shift(a, 0, i - r) for i, w in enumerate(k))
        return sum(w * shift(t, i - r, 0) for i, w in enumerate(k))
    return conv(val * m) / np.maximum(conv(m), 1e-6)


def delit(spr, hits, face_stone, stones, greys, alpha, fit=None):
    """The sprite with its painted light divided out and its broad shading
    flattened to each stone's granite brightness, so what is left is the
    fine detail: carvings, cracks and weathering. Their darks go down to
    the stone's `dark` share of its granite, never to black."""
    w, h = spr.w, spr.h
    a = np.array(spr.img.pixels[:], np.float32).reshape(h, w, 4)[::-1]
    lin = to_linear(a[..., :3])
    out = lin.copy()
    known = np.zeros((h, w), bool)
    owner = np.full((h, w), -1, np.int32)
    for (px, py), (fi, n) in hits.items():
        k = face_stone[fi]
        if fit is not None and alpha[py, px]:
            fit.append((n, float(luma(lin[py, px])) / float(luma(greys[k]))))
        out[py, px] = lin[py, px] * min(2.5, 1.0 / shade(n))
        known[py, px] = True
        owner[py, px] = k
    spread = []
    for k in sorted(set(owner[known].tolist())):
        m = owner == k
        g = float(luma(greys[k]))
        on = m & alpha
        L = luma(out)
        if on.sum() >= 12:
            out[m] *= min(2.5, max(0.4, g / max(1e-4, float(np.median(L[on])))))
        L = np.maximum(luma(out), 1e-5)
        mean = blur(L, m, 1.5)
        ratio = L / np.maximum(mean, 1e-5)
        broad = np.clip(np.sqrt(mean / g), 0.85, 1.2)
        want = g * np.clip(ratio, stones[k].get("dark", 0.15), 1.7) * broad
        grey = greys[k] / g
        hue = out / L[..., None]
        hue = grey + np.clip(hue - grey, -0.3, 0.3) * np.clip(L / (0.4 * g), 0.0, 1.0)[..., None]
        out[m] = (hue * want[..., None])[m]
        if on.sum() >= 12:
            spread.append(float(np.std(np.log(np.clip(ratio[on], 0.3, 3.0)))))
    out = bleed(out, known) if known.any() else out
    return out, (float(np.median(spread)) if spread else 0.12)


def granite_tile(grey, amp, seed):
    """A seamless TILE square of mottled granite round the colour grey,
    repeated two by two so any face up to TILE pixels across fits in it."""
    rs = np.random.RandomState(seed & 0x7FFFFFFF)
    fy = np.fft.fftfreq(TILE)[:, None]
    fx = np.fft.fftfreq(TILE)[None, :]
    f = np.hypot(fx, fy)
    f[0, 0] = 1.0
    blotch = np.real(np.fft.ifft2(np.fft.fft2(rs.normal(size=(TILE, TILE))) * (f > 1.5 / TILE) / f ** 1.2))
    blotch = (blotch - blotch.mean()) / max(1e-6, blotch.std())
    speck = rs.normal(size=(TILE, TILE))
    v = 0.75 * blotch + 0.45 * speck
    v = v / max(1e-6, v.std())
    lum = np.exp(amp * v - 0.5 * amp * amp)
    tile = grey[None, None, :] * lum[..., None]
    return np.tile(tile, (2, 2, 1))


def paint(ob, face_stone, stones, spr, hx, hy, name, fit=None):
    """One texture for the whole group: the delit sprite for the faces the
    classic camera saw, and beside it a granite tile for each colour, which
    the other faces take by a flat projection along their main axis."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bvh = BVHTree.FromBMesh(bm)
    alpha = np.array(spr.alpha, bool)
    grown = alpha.copy()
    for _ in range(2):
        g2 = grown.copy()
        g2[1:] |= grown[:-1]
        g2[:-1] |= grown[1:]
        g2[:, 1:] |= grown[:, :-1]
        g2[:, :-1] |= grown[:, 1:]
        grown = g2
    px = np.array(spr.img.pixels[:], np.float32).reshape(spr.h, spr.w, 4)[::-1]
    level = float(np.mean(luma(to_linear(px[..., :3]))[alpha]))
    greys = [granite(s, level) for s in stones]
    hits = pixel_hits(bvh, spr.w, spr.h, hx, hy)
    sprite_rgb, amp = delit(spr, hits, face_stone, stones, greys, alpha, fit)
    amp = min(0.22, max(0.08, amp))

    # the atlas: the sprite at the top left, the tiles down the right
    keys = []
    for s in stones:
        key = (s["colour"].lower(), round(s.get("gain", 1.0), 3))
        if key not in keys:
            keys.append(key)
    T2 = 2 * TILE
    W = spr.w + 4 + T2
    H = max(spr.h, T2 * len(keys))
    atlas = np.zeros((H, W, 3), np.float32)
    atlas[...] = greys[0]
    atlas[:spr.h, :spr.w] = sprite_rgb
    tile_at = {}
    for i, key in enumerate(keys):
        k = next(j for j, s in enumerate(stones) if (s["colour"].lower(), round(s.get("gain", 1.0), 3)) == key)
        atlas[i * T2:(i + 1) * T2, spr.w + 4:] = granite_tile(greys[k], amp, zlib.crc32(("%s:%d" % (name, i)).encode()))
        tile_at[key] = (spr.w + 4, i * T2)
    img = bpy.data.images.new(name + "_atlas", W, H, alpha=True)
    pix = np.ones((H, W, 4), np.float32)
    pix[..., :3] = to_srgb(atlas)
    img.pixels[:] = pix[::-1].ravel()
    img.pack()

    def seen(f):
        n = f.normal
        if n.dot(VIEW) < SEEN:
            return False
        c = f.calc_center_median()
        probes = [c] + [c.lerp(v.co, 0.7) for v in f.verts]
        clear = sum(1 for q in probes if bvh.ray_cast(q + n * 2e-3, VIEW)[0] is None)
        if 2 * clear < len(probes):
            return False
        sx, sy = screen(hx, hy, c)
        xi, yi = int(sx), int(sy)
        return 0 <= xi < spr.w and 0 <= yi < spr.h and grown[yi, xi]

    looks = [seen(f) for f in bm.faces]
    bm.free()

    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 1.0
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    node.interpolation = "Linear"
    node.extension = "EXTEND"
    nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.append(mat)
    uv = me.uv_layers.new(name="UVMap")
    for poly, look in zip(me.polygons, looks):
        cos = [me.vertices[me.loops[li].vertex_index].co for li in poly.loop_indices]
        if look:
            for li, co in zip(poly.loop_indices, cos):
                sx, sy = screen(hx, hy, co)
                sx = min(spr.w - 0.5, max(0.5, sx))
                sy = min(spr.h - 0.5, max(0.5, sy))
                uv.data[li].uv = (sx / W, 1.0 - sy / H)
            continue
        s = stones[face_stone[poly.index]]
        x0, y0 = tile_at[(s["colour"].lower(), round(s.get("gain", 1.0), 3))]
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        pl = [(co.y, co.z) if ax == 0 else (co.x, co.z) if ax == 1 else (co.x, co.y) for co in cos]
        pl = [(u * CELL, -v * CELL) for u, v in pl]
        ou = math.floor(min(u for u, _ in pl) / TILE) * TILE
        ov = math.floor(min(v for _, v in pl) / TILE) * TILE
        for li, (u, v) in zip(poly.loop_indices, pl):
            tu = min(T2 - 0.5, u - ou + 0.5)
            tv = min(T2 - 0.5, v - ov + 0.5)
            uv.data[li].uv = ((x0 + tu) / W, 1.0 - (y0 + tv) / H)
    return sum(looks), len(looks), hits


def fit_report(spr, hits, out, name):
    """How well the classic silhouette covers the sprite's, and with
    HENGE_DIFF set a picture of it: grey both, red the sprite only (the
    model is short there), blue the model only (it spills over)."""
    alpha = np.array(spr.alpha, bool)
    model = np.zeros_like(alpha)
    for (px, py) in hits:
        model[py, px] = True
    both = int((alpha & model).sum())
    union = int((alpha | model).sum())
    miss = int((alpha & ~model).sum())
    extra = int((model & ~alpha).sum())
    if os.environ.get("HENGE_DIFF"):
        from_rgb = np.array(spr.rgb, np.float32)
        img = np.zeros(alpha.shape + (4,), np.float32)
        img[..., 3] = 1.0
        img[..., :3] = (84 / 255, 104 / 255, 64 / 255)
        img[alpha & model, :3] = 0.35 + 0.5 * from_rgb[alpha & model]
        img[alpha & ~model, :3] = (0.9, 0.15, 0.1)
        img[model & ~alpha, :3] = (0.15, 0.35, 0.95)
        im = bpy.data.images.new(name + "_fit", spr.w, spr.h, alpha=True)
        im.pixels[:] = img[::-1].ravel()
        im.filepath_raw = os.path.join(out, "renders", name + "_fit.png")
        im.file_format = "PNG"
        im.save()
    return both / max(1, union), miss, extra


# ---- one sprite ---------------------------------------------------------

def build(name, henge, row, catalog, out, fit=None):
    hk.reset()
    hx, hy = row["sprite"]["hotspot"]
    sprite_png = os.path.join(catalog, "sprites", name + ".png")
    spr = carve.Sprite(sprite_png)
    stones = [dict(s) for s in henge["stones"]]
    stones = merge_halves(stones)
    join_lintels(stones)
    chips = ground_chips(henge)
    if chips:
        rng = random.Random(zlib.crc32(name.encode()))
        cx, cy = (chips[0] - hx) / CELL, (hy - chips[1]) / CELL
        for i in range(3):
            stones.append({"pose": "rubble", "x": cx + rng.uniform(-0.35, 0.35), "y": cy + rng.uniform(-0.3, 0.3),
                           "width": rng.uniform(0.18, 0.3), "thickness": rng.uniform(0.15, 0.25),
                           "height": rng.uniform(0.12, 0.22), "yaw_deg": rng.uniform(0, 180), "lean_deg": 0,
                           "lean_dir_deg": 0, "taper": 0.8, "top_shape": "broken", "p": 6, "round": 0.02,
                           "colour": henge["stones"][0]["colour"]})
    all_bm = bmesh.new()
    face_stone = []
    for i, s in enumerate(stones):
        bm, solid = build_stone(s, zlib.crc32(("%s:%d" % (name, i)).encode()), (hx, hy))
        tmp = bpy.data.meshes.new("tmp")
        bm.to_mesh(tmp)
        nf = len(bm.faces)
        print("HENGE_STONE %s %d %s faces %d %s" % (name, i, s["pose"], nf, "solid" if solid else "OPEN"),
              flush=True)
        bm.free()
        all_bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
        face_stone += [i] * nf
    me = bpy.data.meshes.new(name)
    all_bm.to_mesh(me)
    all_bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    spr.opaque()
    seen, faces, hits = paint(ob, face_stone, stones, spr, hx, hy, name, fit)
    # painted from the player's own sprite, so the studio keeps it out of git
    ob["okFromPlayersFiles"] = True
    iou, miss, extra = fit_report(spr, hits, out, name)
    hk.smooth(ob, 40)
    glb = os.path.join(out, "models", name + ".glb")
    os.makedirs(os.path.dirname(glb), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_yup=True,
                              export_extras=True)
    hk.renders(ob, os.path.join(out, "renders"), name, sprite_png, (hx, hy), scale=3)
    lo = [min(v.co[i] for v in me.vertices) for i in range(3)]
    hi = [max(v.co[i] for v in me.vertices) for i in range(3)]
    print("HENGE_OK %s stones %d faces %d painted %d size %.2f x %.2f x %.2f top %.2f fit %.3f short %d spill %d" % (
        name, len(stones), faces, seen, hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2], hi[2], iou, miss, extra),
        flush=True)


def fit_light(samples):
    """Prints the light that best explains the sprites' shading, for LIGHT above."""
    N = np.array([tuple(n) for n, _ in samples], np.float32)
    y = np.array([v for _, v in samples], np.float32)
    best = None
    for el in range(10, 90, 3):
        for az in range(0, 360, 5):
            e, a = math.radians(el), math.radians(az)
            L = np.array([math.cos(e) * math.sin(a), math.cos(e) * math.cos(a), math.sin(e)], np.float32)
            x = np.maximum(0.0, N @ L)
            A = np.stack([np.ones_like(x), x], 1)
            coef, *_ = np.linalg.lstsq(A, y, rcond=None)
            err = float(np.mean((A @ coef - y) ** 2))
            if best is None or err < best[0]:
                best = (err, el, az, L, coef)
    err, el, az, L, (c0, c1) = best
    print("HENGE_FIT samples %d elevation %d azimuth %d light (%.3f, %.3f, %.3f) ambient %.3f direct %.3f "
          "ratio %.3f rms %.3f" % (len(y), el, az, L[0], L[1], L[2], c0, c1, c0 / max(1e-6, c0 + c1),
                                   math.sqrt(err)), flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    catalog = None
    if "--catalog" in argv:
        i = argv.index("--catalog")
        catalog = argv[i + 1]
        del argv[i:i + 2]
    if not argv:
        raise SystemExit(__doc__)
    out, names = argv[0], argv[1:]
    catalog = catalog or find_catalog(out)
    rows = {r["name"]: r for r in json.load(open(os.path.join(catalog, "catalog.json")))}
    henges = load_henges()
    fit = [] if os.environ.get("HENGE_FIT") else None
    done = failed = 0
    for name in names or sorted(henges):
        try:
            build(name, henges[name], rows[name], catalog, out, fit)
            done += 1
        except Exception as e:  # keep going, report at the end
            traceback.print_exc()
            print("HENGE_FAIL", name, repr(e), flush=True)
            failed += 1
    if fit:
        fit_light(fit)
    print("HENGE_DONE built %d failed %d" % (done, failed), flush=True)


if __name__ == "__main__":
    main()
