"""Taros devices: gibbet, impaler (and its ruin), stocks and guillotine."""
import math
import random

import numpy as np

from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import kit as K

# colours read off the sprites (sRGB 0-255)
BLACK_WOOD = (14, 13, 12)
WHEEL_IRON = (44, 37, 34)
HUB_IRON = (40, 38, 30)
SAND_DARK = (66, 56, 40)
SAND_LIGHT = (148, 133, 96)
SAND_SOOT = (120, 106, 76)
SAND_BLACK = (40, 35, 28)
SAND_JOINT = (74, 62, 44)
SAND_PALE = (176, 162, 120)
HORN = (112, 112, 114)
IRON = (52, 53, 58)
STOCK_WOOD = (50, 47, 41)
STOCK_TOP = (86, 80, 68)
STUD = (170, 166, 156)
FLAG = (188, 168, 116)
FLAG_GREY = (142, 144, 138)
FLAG_JOINT = (70, 62, 48)
GUIL_DARK = (44, 42, 34)
GUIL_MID = (72, 68, 58)
GUIL_POST = (26, 26, 25)
GUIL_STONE = (74, 72, 68)
GUIL_MORTAR = (128, 124, 116)
GUIL_GREY = (120, 120, 116)
GUIL_TEAL = (70, 112, 104)
COPPER = (170, 104, 64)
STEEL = (178, 184, 190)


def iron():
    return K.mat("iron", IRON, rough=0.38, metal=0.6, spec=0.5)


def sandstone():
    return K.texmat("sandstone", lambda: K.tex_mottle(SAND_LIGHT, SAND_DARK, n=128, seed=11, amt=0.9),
                    rough=0.95)


def sooty_sandstone():
    return K.texmat("sooty_sandstone", lambda: K.tex_mottle(SAND_SOOT, SAND_BLACK, n=128, seed=12, amt=1.0),
                    rough=0.95)


# ---- gibbet --------------------------------------------------------------------

def gibbet(name, foot, hub, radius=1.56, spokes=16, tilt=5.0):
    """A black post with a spoked cart wheel lying flat on its top. Built
    sturdy: the post thick and braced at the foot, the whole lowered and
    the wheel widened (K.sturdy), so it stands as a gibbet, not a stalk."""
    P = K.Pic(name)
    fx, fy, _ = P.ground(*foot)
    hx_, hy_, hz = P.at(*hub)
    post = K.mat("black_wood", BLACK_WOOD, rough=0.8)
    rim_m = K.mat("wheel_iron", WHEEL_IRON, rough=0.6, metal=0.3)
    hub_m = K.mat("hub_iron", HUB_IRON, rough=0.55, metal=0.3)
    earth = K.mat("gib_earth", (58, 50, 38), rough=1.0)
    parts = []
    # the post, a square beam leaning a little, set into a mound of earth
    parts.append(K.loft([K.rect(fx, fy, 0.58, 0.58, 8, -0.2), K.rect(fx + (hx_ - fx) * 0.5, fy, 0.48, 0.48, 8, hz * 0.5),
                         K.rect(hx_, hy_, 0.42, 0.42, 8, hz + 0.05)], post, "post"))
    parts.append(K.lathe([(0.0, 0.0), (1.15, 0.0), (0.85, 0.16), (0.45, 0.3), (0.0, 0.34)], seg=10, mt=earth,
                         x=fx, y=fy, name="footing", smooth=40))
    # four braces from the post down into the mound
    for k in range(4):
        a = math.radians(8 + 45 + 90 * k)
        parts.append(K.board((fx + 0.22 * math.cos(a), fy + 0.22 * math.sin(a), 1.9),
                             (fx + 1.0 * math.cos(a), fy + 1.0 * math.sin(a), 0.1), 0.24, 0.22, post, "brace"))
    # a collar where the wheel sits
    parts.append(K.cyl(0.36, 0.3, 0.3, seg=8, x=hx_, y=hy_, z=hz - 0.45, mt=hub_m, name="collar"))
    # the wheel, built flat at the origin then tipped and lifted onto the post
    w = []
    w.append(K.torus(radius, 0.1, seg=36, rseg=5, mt=rim_m, name="rim"))
    w.append(K.torus(radius - 0.13, 0.07, seg=36, rseg=4, mt=rim_m, name="felloe"))
    for i in range(spokes):
        a = 2 * math.pi * (i + 0.5) / spokes
        sp = K.block(radius - 0.3, 0.1, 0.09, 0, 0, -0.045, 0, rim_m, "spoke")
        K.place(sp, Matrix.Rotation(a, 4, "Z") @ Matrix.Translation(((radius - 0.3) / 2 + 0.22, 0, 0)))
        w.append(sp)
    w.append(K.lathe([(0.0, -0.26), (0.32, -0.24), (0.4, -0.06), (0.37, 0.12), (0.25, 0.26), (0.1, 0.4),
                      (0.0, 0.42)], seg=10, mt=hub_m, name="hub", smooth=50))
    M = Matrix.Translation((hx_, hy_, hz)) @ Matrix.Rotation(math.radians(tilt), 4, "X")
    for p in w:
        K.place(p, M)
    parts += w
    return K.sturdy(parts, 1.1, 0.84, fx, fy)


# ---- impaler -------------------------------------------------------------------

def stacked_stone():
    """Packed sandstone: small courses of flat stones, finely speckled."""
    def make():
        img = K.tex_courses(SAND_LIGHT, SAND_JOINT, n=128, rows=14, per_row=6, seed=13, var=0.28, jw=1.4)
        rng = K._rng(14)
        grain = rng.random((64, 64)).repeat(2, 0).repeat(2, 1)
        img = K._mix(img, img * 0.45, (grain < 0.16).astype(np.float32))
        img = K._mix(img, K._col(SAND_PALE) + img * 0, (grain > 0.9).astype(np.float32) * 0.7)
        m = K.fbm(128, rng, ((4, 0.6), (8, 0.4)))
        return K._mix(img, img * 0.62, np.clip((0.45 - m) * 2.5, 0, 1))
    return K.texmat("stacked_stone", make, rough=0.95)


def cracked_stone():
    """The packed sandstone broken up: the same courses crossed by dark
    cracks, with a few stones knocked out to dark gaps."""
    def make():
        img = np.array(stacked_img())
        rng = K._rng(16)
        d1, d2, idx = K.voronoi(128, 26, rng)
        f = np.clip(2 * d1 / (d1 + d2 + 1e-6), 0, 1)
        crack = np.clip((f - 0.92) * 14, 0, 1)
        gone = (rng.random(26) < 0.12)[idx].astype(np.float32) * np.clip((0.8 - f) * 4, 0, 1)
        img = K._mix(img, img * 0 + K._col((34, 28, 20)), np.maximum(crack, gone * 0.85))
        return np.clip(img, 0, 1)
    return K.texmat("cracked_stone", make, rough=0.95)


def stacked_img():
    img = K.tex_courses(SAND_LIGHT, SAND_JOINT, n=128, rows=14, per_row=6, seed=13, var=0.28, jw=1.4)
    rng = K._rng(14)
    grain = rng.random((64, 64)).repeat(2, 0).repeat(2, 1)
    img = K._mix(img, img * 0.45, (grain < 0.16).astype(np.float32))
    img = K._mix(img, K._col(SAND_PALE) + img * 0, (grain > 0.9).astype(np.float32) * 0.7)
    m = K.fbm(128, rng, ((4, 0.6), (8, 0.4)))
    return K._mix(img, img * 0.62, np.clip((0.45 - m) * 2.5, 0, 1))


def sooty_stone():
    """Broken, blackened masonry: stones of mixed shade with dark gaps."""
    return K.texmat("sooty_stone", lambda: K.tex_rubble((78, 68, 52), (18, 16, 12), (128, 114, 84), n=128, count=150,
                                                         seed=15, var=0.45), rough=0.95)


def _section(rx, ry, n, rz, seg):
    """A superellipse (n 2 round, 4 a rounded square, large a square) turned rz degrees."""
    c, s = math.cos(math.radians(rz)), math.sin(math.radians(rz))
    out = []
    for i in range(seg):
        t = 2 * math.pi * i / seg
        u, v = math.cos(t), math.sin(t)
        px = rx * math.copysign(abs(u) ** (2 / n), u)
        py = ry * math.copysign(abs(v) ** (2 / n), v)
        out.append((px * c - py * s, px * s + py * c))
    return out


def body(a0, a1, h, cap, n=2.0, rz=45.0, squash=1.0, dome=True, lump=0.1, seed=1, x=0.0, y=0.0, broken=None,
         stone=None, top_mt=None, seg=32, rings=9, proud=6):
    """The impaler's cairn, one solid of packed stone: a section of half
    width a0 at the ground (depth a0 * squash) tapering to a1 at h, closed
    by a dome or, with dome False, a low pyramid cap. lump swells the
    lower courses unevenly; proud sets a few flat stones into the base.
    broken=(depth, amp, seed) snaps the top off in a jagged edge instead."""
    rnd = random.Random(seed)
    stone = stone or stacked_stone()
    waves = [(rnd.randint(2, 5), rnd.uniform(0, 6.3), rnd.uniform(0.5, 1.0)) for _ in range(4)]
    top = h if broken is None else h - broken[0]
    base = _section(1.0, squash, n, rz, seg)
    angs = [math.atan2(py, px) for px, py in base]

    def ring(z, r, amp):
        out = []
        for (px, py), a in zip(base, angs):
            w = sum(m * math.sin(k * a + ph + z * 1.7) for k, ph, m in waves) / 3.0
            k = r * (1 + amp * w)
            out.append((x + px * k, y + py * k, z))
        return out

    rs = []
    for i in range(rings + 1):
        f = i / rings
        z = top * f
        hf = z / h
        r = a0 + (a1 - a0) * hf ** 0.85
        r *= 1 + 0.05 * math.sin(math.pi * min(1.0, hf * 2.2))  # a swell over the lower courses
        amp = lump * (1 - hf) ** 1.5 + 0.015
        rs.append(ring(z, r, amp))
    rs[0] = [(px, py, -0.05) for px, py, _ in rs[0]]
    parts = []
    if broken is None:
        if dome:
            for i in range(1, 5):
                phi = math.pi / 2 * i / 5
                rs.append(ring(h + cap * math.sin(phi), a1 * math.cos(phi), 0.012))
        rs.append([(x, y, h + cap)])
        ob = K.loft(rs, stone, "cairn", smooth=60 if dome else None)
    else:
        # a jagged rim, the crater inside it sooty and fallen in
        depth, jamp, jseed = broken
        jr = random.Random(jseed)
        ph = [jr.uniform(0, 6.3) for _ in range(3)]
        rim = []
        for (px, py, pz), a in zip(rs[-1], angs):
            j = (math.sin(3 * a + ph[0]) * 0.5 + math.sin(7 * a + ph[1]) * 0.3 + math.sin(13 * a + ph[2]) * 0.2)
            rim.append((px, py, pz + jamp * j))
        rs[-1] = rim
        rs.append([(x + (px - x) * 0.7, y + (py - y) * 0.7, pz - 0.35) for px, py, pz in rim])
        rs.append([(x, y, top - 0.6)])
        ob = K.loft(rs, top_mt or cracked_stone(), "cairn", smooth=45)
        # the same cracked sandstone all round; only the crater inside the rim is sooty
        ob.data.materials.append(sooty_stone())
        for p in ob.data.polygons:
            if p.normal.z > 0.5 and p.center.z > top - 0.7:
                p.material_index = 1
        for k in range(10):
            a, rr = rnd.uniform(0, 2 * math.pi), a1 * math.sqrt(rnd.random()) * 0.9
            s = rnd.uniform(0.45, 0.85)
            st = K.rock((x + rr * math.cos(a), y + rr * math.sin(a) * squash, top - 0.2 + rnd.uniform(0, 0.35)),
                        (s, s * rnd.uniform(0.6, 0.9), s * rnd.uniform(0.5, 0.8)), rnd.randrange(10 ** 6),
                        sooty_stone() if k % 3 == 0 else (top_mt or cracked_stone()), "broken", jitter=0.3)
            K.uv_box(st, 0.5, rnd.random(), rnd.random())
            parts.append(st)
    K.uv_cyl(ob, 0.5, x, y)
    parts.append(ob)
    # a few flat stones standing a little proud near the foot, round the front
    for k in range(proud):
        a = math.atan2(-abs(math.sin(rnd.uniform(0, 6.3))) - 0.2, math.cos(rnd.uniform(0, 6.3)))
        zz = rnd.uniform(0.1, 0.9)
        r = a0 + (a1 - a0) * zz / h
        px, py = x + r * math.cos(a) * 0.97, y + r * math.sin(a) * squash * 0.97
        s = rnd.uniform(0.4, 0.62)
        st = K.rock((px, py, zz), (s, s * 0.7, s * 0.45), rnd.randrange(10 ** 6), stone, "proud", jitter=0.2)
        K.uv_box(st, 0.5, rnd.random(), rnd.random())
        parts.append(st)
    return parts


def horn_mat():
    """Silver-grey horn with a hard shine."""
    return K.mat("horn", HORN, rough=0.28, metal=0.55, spec=0.9)


def steel(bright=False):
    """Dark steel with a strong silver highlight, for the standing horns."""
    if bright:
        return K.mat("horn_steel_bright", (118, 118, 124), rough=0.16, metal=0.3, spec=1.0)
    return K.mat("horn_steel", (70, 70, 76), rough=0.18, metal=0.25, spec=1.0)


def skirt(a0, h, n, rz, yscale, x, y, count, seed, stone):
    """Lumpy blocks heaped round the pedestal's foot."""
    rnd = random.Random(seed)
    out = []
    sec = _section(1.0, 1.0, n, rz, count)
    for k, (px, py) in enumerate(sec):
        # set along the faces, pulled in at the corners so they stay sharp
        corner = abs(abs(math.atan2(py, px)) % (math.pi / 2) - math.pi / 4) > math.pi / 5
        s = rnd.uniform(0.62, 0.95)
        r = a0 * rnd.uniform(0.95, 1.03) * (0.86 if corner else 1.0)
        c = (x + px * r, y + py * r * yscale, rnd.uniform(-0.05, 0.15))
        st = K.boulder(c, (s, s * rnd.uniform(0.75, 0.95), s * rnd.uniform(0.55, 0.75)), rnd.randrange(10 ** 6),
                       stone, "skirt", seg=6, rings=3, rough=0.3)
        K.uv_box(st, 0.5, rnd.random(), rnd.random())
        out.append(st)
    return out


def _shape(shape):
    """body() keywords, less the pedestal's own: its plan squashed front to
    back by yscale, and a skirt of blocks round its foot."""
    shape = dict(shape)
    return shape, shape.pop("yscale", 1.0), shape.pop("skirt", 0)


def _squash_y(parts, k, y0=0.0):
    if k != 1.0:
        for p in parts:
            K.place(p, Matrix.Translation((0, y0, 0)) @ Matrix.Scale(k, 4, Vector((0, 1, 0)))
                    @ Matrix.Translation((0, -y0, 0)))
    return parts


def spike(P, pts, r0=0.17, seg=6, taper=1.3, mt=None, flat=1.0, lead=None):
    """A curved horn or spike along sprite points, thick at the root, pointed;
    a larger taper keeps it thick further along. lead, a point in the
    model's frame, starts it further back (inside the stone it grows from)."""
    path = P.path(pts)
    if lead is not None:
        path = [tuple(lead)] + path
    n = len(path)
    radii = [r0 * (1 - (i / (n - 1)) ** taper) for i in range(n)]
    radii[-1] = 0.0
    return K.tube(path, radii, seg=seg, sub=3, mt=mt or iron(), name="spike", flat=flat)


def ring(c, R=0.33, r=0.045, axis="Z", face=None):
    ob = K.torus(R, r, seg=16, rseg=4, mt=iron(), name="ring", axis="Z")
    M = Matrix.Identity(4)
    if face is not None:
        # hanging on a face whose outward normal points along face (x, y)
        n = Vector((face[0], face[1], 0)).normalized()
        M = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
    K.place(ob, Matrix.Translation(c) @ M)
    return ob


# the classic camera's line of sight: every point along it is drawn on one pixel
SIGHT = Vector((0.0, 1.0, -2.0)).normalized()


def _bvh(ob):
    me = ob.data
    return BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [tuple(p.vertices) for p in me.polygons])


def _face(bvh, c, axis, reach=0.5, sight=True):
    """Where the line of sight through c meets the stone (or, without
    sight, the stone nearest c), with the face's normal there averaged
    over reach."""
    c = Vector(c)
    loc = bvh.ray_cast(c - SIGHT * 6, SIGHT)[0] if sight else None
    if loc is None:
        loc = bvh.find_nearest(c)[0]
    N = Vector()
    for _, n, _, _ in bvh.find_nearest_range(loc, reach):
        N += n
    out = Vector((loc.x - axis[0], loc.y - axis[1], 0.0))
    if N.length < 1e-6:
        N = out
    N.normalize()
    if N.dot(out) < 0:
        N = -N
    return loc, N


def hung_ring(bvh, c, axis, R=0.33, r=0.045):
    """An iron ring lying against the stone where c is drawn, hanging from
    an eye bolt set into the stone at its top, a square plate round it."""
    loc, N = _face(bvh, c, axis)
    Z = Vector((0, 0, 1))
    U = (Z - N * Z.dot(N)).normalized()
    H = U.cross(N)
    C = loc + N * (r + 0.03)
    T = C + U * R
    base, n2 = _face(bvh, T, axis, 0.3, sight=False)
    T = base + n2 * (r + 0.03)
    er = r + 0.05

    def basis(a, b, n, o):
        M = Matrix((a, b, n)).transposed().to_4x4()
        return Matrix.Translation(o) @ M

    ring_ob = K.torus(R, r, seg=16, rseg=4, mt=iron(), name="ring", axis="Z")
    K.place(ring_ob, basis(H, U, N, C))
    eye = K.torus(er, 0.035, seg=10, rseg=4, mt=iron(), name="ring_eye", axis="Z")
    K.place(eye, basis(N, U, N.cross(U), T))
    plate = K.block(er * 3.2, er * 3.2, 0.08, 0, 0, -0.04, 0, iron(), "ring_plate")
    K.place(plate, basis(H, U, N, base + n2 * 0.01))
    return [ring_ob, eye, plate]


SPIKE_GIRTH = 1.45


def _spikes(P, spikes, mt=None, mats=None, sink=(), bvh=None, axis=(0.0, 0.0)):
    """[(points, root radius[, taper]), ...]; mats gives some their own
    material by index; the spikes listed in sink grow out of the stone."""
    out = []
    for i, sp in enumerate(spikes):
        pts, r0 = sp[0], sp[1]
        taper = sp[2] if len(sp) > 2 else 1.3
        # horns and spikes built thicker than the picture's pixel-thin ones
        r0 *= SPIKE_GIRTH
        lead = None
        if i in sink:
            loc, N = _face(bvh, P.path(pts[:1])[0], axis, 0.3, sight=False)
            lead = loc - N * (r0 + 0.12)
        out.append(spike(P, pts, r0, seg=8 if r0 > 0.19 else 6, taper=max(taper, 1.6),
                         mt=(mats or {}).get(i, mt), lead=lead))
    return out


def impaler(name, shape, spikes, rings=(), seed=1, mt=None, mats=None, hang=False, sink=()):
    """shape holds body()'s keywords (and yscale, skirt); rings are
    (point, face[, radius]); mt is the spikes' material, mats some
    spikes' own by index. With hang the rings hang from eye bolts in the
    stone where they are drawn; the spikes listed in sink grow out of it."""
    P = K.Pic(name)
    shape, ys, sk = _shape(shape)
    parts = _squash_y(body(seed=seed, **shape), ys, shape.get("y", 0.0))
    axis = (shape.get("x", 0.0), shape.get("y", 0.0))
    bvh = _bvh(next(p for p in parts if p.name.startswith("cairn"))) if (hang or sink) else None
    if sk:
        parts += skirt(shape["a0"], shape["h"], shape.get("n", 2.0), shape.get("rz", 45.0), ys, shape.get("x", 0.0),
                       shape.get("y", 0.0), sk, seed + 40, stacked_stone())
    parts += _spikes(P, spikes, mt, mats, sink, bvh, axis)
    for c, face, *R in rings:
        if hang:
            parts += hung_ring(bvh, P.at(*c), axis, R=R[0] if R else 0.33, r=0.07 if R else 0.045)
        else:
            parts.append(ring(P.at(*c), R=R[0] if R else 0.33, r=0.07 if R else 0.045, face=face))
    return K.sturdy(parts, 1.12, 0.85)


def impaler_ruin(name, shape, cut, spikes, rings=(), seed=2, chips=True):
    """The stump, its lit left face standing to a jagged top (cut is
    body()'s broken), horns standing, leaning or fallen, and sandstone chips
    lying where the picture drew them."""
    P = K.Pic(name)
    shape, ys, _ = _shape(dict(shape, lump=shape.get("lump", 0.1) + 0.1))
    parts = _squash_y(body(seed=seed, broken=cut, proud=3, **shape), ys, shape.get("y", 0.0))
    parts += _spikes(P, spikes, horn_mat())
    for c, face in rings:
        if face is None:
            parts.append(ring(P.ground(*c[:2], 0.05), R=c[2], r=0.06))
        else:
            parts.append(ring(P.at(*c), face=face))
    if chips:
        stone = sandstone()
        rnd = random.Random(seed)
        for col, row, w, hh, n, *_ in K.blob_cache(name):
            x, y, _ = P.ground(col, row, 0.12)
            s = max(0.22, min(0.6, math.sqrt(n) / 16 * 1.25))
            ch = K.rock((x, y, s * 0.3), (s, s * rnd.uniform(0.7, 1.0), s * 0.6), rnd.randrange(10 ** 6), stone,
                        "chip")
            K.uv_box(ch, 0.5)
            parts.append(ch)
    return K.sturdy(parts, 1.1, 0.88)


# ---- stocks ---------------------------------------------------------------------

def tex_flags(n=256, per=16, seed=21):
    """Square flags laid in a grid, per to a side, each mottled somewhere
    between tan and grey-blue, with dark mortar between."""
    rng = K._rng(seed)
    cell = n // per
    tint = rng.random((per, per))
    shade = 1 + 0.16 * (rng.random((per, per)) - 0.5) * 2
    idx = np.arange(n) // cell
    t = tint[idx][:, idx]
    s = shade[idx][:, idx]
    img = K._mix(np.ones((n, n, 3), np.float32) * K._col(FLAG), np.ones((n, n, 3), np.float32) * K._col(FLAG_GREY),
                 np.clip((t - 0.62) * 2.2, 0, 0.8))
    m = K.fbm(n, rng, ((16, 0.3), (32, 0.3), (64, 0.25), (128, 0.15)))
    img = img * (s * (0.74 + 0.5 * m))[..., None]
    pos = np.arange(n) % cell
    jitter = rng.integers(0, 2, (per, per))
    j = jitter[idx][:, idx]
    edge = ((pos[None, :] < 1 + j) | (pos[:, None] < 1 + j)).astype(np.float32)
    img = K._mix(img, np.ones((n, n, 3), np.float32) * K._col(FLAG_JOINT), edge * 0.9)
    return np.clip(img, 0, 1)


def flagstone():
    return K.texmat("flagstone", tex_flags, rough=0.95)


def dais(w, d, t, rz, cx=0.0, cy=0.0):
    """A low paved platform of square flags half a cell across."""
    slab = K.block(w, d, t, cx, cy, 0, rz, flagstone(), "dais")
    K.uv_box(slab, 1 / 8.0)
    return [slab]


def stock(cx, cy, z, rz, L=1.9, W=0.95, mt=None, studs=0):
    """One set of stocks: a low open frame, long axis along local x. The hole
    boards stand along its local -y side between two posts, end boards close
    it, and a bench runs inside; turned rz degrees and moved to cx, cy.
    studs sets a row of iron studs along the face of the lower board."""
    wood = mt or K.mat("stock_wood", STOCK_WOOD, rough=0.8)
    top = K.mat("stock_top", STOCK_TOP, rough=0.75)
    hole = K.mat("stock_hole", (10, 9, 8), rough=1.0)
    parts = []
    for s in (-1, 1):
        parts.append(K.block(0.26, 0.26, 1.05, s * L / 2, 0, z, 0, wood, "stock_post"))
        parts.append(K.block(0.14, W, 0.5, s * (L / 2 - 0.03), W / 2, z, 0, wood, "end_board"))
    parts.append(K.block(L + 0.3, 0.26, 0.12, 0, 0, z + 1.05, 0, wood, "stock_cap"))
    parts.append(K.block(L, 0.14, 0.26, 0, 0, z + 0.3, 0, wood, "stock_board"))
    parts.append(K.block(L, 0.14, 0.26, 0, 0, z + 0.58, 0, wood, "stock_board"))
    for p in parts:
        K.two_tone(p, top)
    for i in range(3):
        x = -L / 3 + i * L / 3
        parts.append(K.block(0.17, 0.18, 0.12, x, 0, z + 0.5, 0, hole, "stock_hole"))
    for i in range(studs):
        x = -L / 2 + L * (i + 0.5) / studs
        parts.append(K.block(0.1, 0.05, 0.1, x, -0.085, z + 0.38, 0, K.mat("stud", STUD, rough=0.4, metal=0.4),
                             "stud"))
    b = K.block(L - 0.2, 0.45, 0.08, 0, W * 0.6, z + 0.36, 0, wood, "bench")
    K.two_tone(b, top)
    parts.append(b)
    parts.append(K.block(L - 0.1, W, 0.06, 0, W / 2, z, 0, hole, "stock_floor"))
    M = Matrix.Translation((cx, cy, 0)) @ Matrix.Rotation(math.radians(rz), 4, "Z")
    for p in parts:
        K.place(p, M)
    return parts


def stocks(name, w, d, t, rz, units, cx=0.0, cy=0.0):
    """units: (x, y, turn[, studs]) for each set of stocks."""
    parts = dais(w, d, t, rz, cx, cy)
    for ux, uy, urz, *st in units:
        parts += stock(ux, uy, t, urz, studs=st[0] if st else 0)
    return parts


# ---- guillotine -----------------------------------------------------------------

def tex_blocks():
    """Grey stone blocks coursed with light mortar."""
    return K.tex_courses(GUIL_STONE, GUIL_MORTAR, n=64, rows=5, per_row=3, seed=31, var=0.3, jw=1.5)


def ratchet(c, r, w, mt, teeth=12):
    """A toothed drum at c turning on an axis along local x."""
    parts = [K.cyl(r, w, seg=16, z=-w / 2, mt=mt, name="ratchet", smooth=40)]
    for k in range(teeth):
        t = K.block(0.24, 0.14, w, r + 0.06, 0, -w / 2, 0, mt, "tooth", top=(0.24, 0.14))
        K.rotz(t, 360.0 * k / teeth)
        parts.append(t)
    M = Matrix.Translation(c) @ Matrix.Rotation(math.pi / 2, 4, "Y")
    for p in parts:
        K.place(p, M)
    return parts


def star_foot(x, y, mt, r=0.75):
    """A post's foot: a squat block with four spikes splayed along the ground."""
    parts = [K.block(0.9, 0.9, 0.55, x, y, 0, 45, mt, "foot", top=(0.6, 0.6))]
    for k in range(4):
        a = math.radians(45 + 90 * k)
        parts.append(K.tube([(x + 0.3 * math.cos(a), y + 0.3 * math.sin(a), 0.35),
                             (x + r * 0.6 * math.cos(a), y + r * 0.6 * math.sin(a), 0.16),
                             (x + r * math.cos(a), y + r * math.sin(a), 0.02)], [0.2, 0.13, 0.0], seg=4, sub=1, mt=mt,
                            name="foot_spike", shade=None))
    return parts


def guillotine(name, foot_left, foot_right, top_left, top_right, depth=2.9, post_h=10.6, body=(1.0, 4.6),
               blades=26, bays=7):
    """A long frame between two tall horned posts. The posts stand at the
    front corners on spiked feet and lean a little, as drawn; the body sits
    behind them: a trough of stone blocks in bays along the bottom, a row of
    blades hanging from a great roller along the top front, ratchet drums
    at both ends, the winch wheel on top at the right and a copper crank
    out of the right end."""
    P = K.Pic(name)
    fl = Vector(P.ground(*foot_left)[:2])
    fr = Vector(P.ground(*foot_right)[:2])
    L = (fr - fl).length
    rz = math.degrees(math.atan2(fr.y - fl.y, fr.x - fl.x))
    mid = (fl + fr) / 2
    lean = [(top_left - foot_left[0]) / 16.0, (top_right - foot_right[0]) / 16.0]
    dark = K.mat("guil_dark", GUIL_DARK, rough=0.7, metal=0.2)
    midm = K.mat("guil_mid", GUIL_MID, rough=0.55, metal=0.35)
    postm = K.mat("guil_post", GUIL_POST, rough=0.65)
    steel = K.mat("steel", STEEL, rough=0.3, metal=0.8, spec=0.5)
    grey = K.mat("guil_grey", GUIL_GREY, rough=0.45, metal=0.5, spec=0.6)
    teal = K.mat("guil_teal", GUIL_TEAL, rough=0.45, metal=0.4, spec=0.6)
    copper = K.mat("guil_copper", COPPER, rough=0.35, metal=0.8, spec=0.6)
    stone = K.texmat("guil_stone", tex_blocks, rough=0.9)
    z0, z1 = body
    D = depth
    yf = 0.35  # the body's front, behind the posts
    zt = z0 + (z1 - z0) * 0.55  # top of the stone trough
    rr = 0.5  # the roller's radius
    ry, rzc = yf + rr, z1 - rr * 0.6
    parts = []
    # local frame: x along the machine, +y back, origin mid front
    for i, s in enumerate((-1, 1)):
        x = s * L / 2
        xt = x + lean[i]
        # the posts built thick, tapering, with a collar at the body's top
        parts.append(K.loft([K.rect(x, 0, 0.54, 0.54, 0, 0), K.rect(x + lean[i] * 0.4, 0, 0.45, 0.45, 0, post_h * 0.4),
                             K.rect(xt, 0, 0.38, 0.38, 0, post_h)], postm, "post"))
        parts.append(K.block(0.66, 0.66, 0.35, x + lean[i] * z1 / post_h, 0, z1 - 0.1, 0, dark, "post_collar"))
        parts += star_foot(x, 0, postm, r=0.95)
        # the horned finial: a knob and two ram horns sweeping out and up, tipped teal
        parts.append(K.lathe([(0, 0), (0.34, 0.05), (0.4, 0.3), (0.22, 0.6), (0, 0.7)], seg=8, mt=postm,
                             x=xt, y=0, z=post_h, name="finial", smooth=50))
        for hs in (-1, 1):
            parts.append(K.tube([(xt, 0, post_h + 0.4), (xt + hs * 0.5, 0, post_h + 0.6),
                                 (xt + hs * 0.85, 0, post_h + 1.1)], [0.17, 0.14, 0.1], seg=5, sub=2, mt=midm,
                                name="horn"))
            parts.append(K.tube([(xt + hs * 0.85, 0, post_h + 1.1), (xt + hs * 0.8, 0, post_h + 1.5)],
                                [0.1, 0.0], seg=5, sub=1, mt=teal, name="horn_tip"))
        # a back leg, the end frame rails, and a toothed ratchet drum on each end
        parts.append(K.block(0.4, 0.4, z1, x, D, 0, 0, postm, "leg"))
        parts.append(K.block(0.28, D - yf + 0.3, 0.28, x, (D + yf) / 2, z1 - 0.28, 0, dark, "end_rail"))
        parts.append(K.block(0.28, D - yf + 0.3, 0.22, x, (D + yf) / 2, zt, 0, dark, "end_rail"))
        parts.append(K.block(0.28, D - yf + 0.3, 0.22, x, (D + yf) / 2, z0 - 0.2, 0, dark, "end_rail"))
        parts.append(K.block(0.22, D + 0.2, 0.2, x, D / 2, 0.5, 0, dark, "foot_rail"))
        parts += ratchet((x + s * 0.3, D * 0.45, z1 + 0.1), 0.62, 0.5, grey)
    # the great roller across the top front, a steel edge catching the light
    roll = K.cyl(rr, L - 0.2, seg=14, z=-(L - 0.2) / 2, mt=midm, name="roller", smooth=40)
    K.place(roll, Matrix.Translation((0, ry, rzc)) @ Matrix.Rotation(math.pi / 2, 4, "Y"))
    parts.append(roll)
    parts.append(K.block(L - 0.3, 0.12, 0.1, 0, ry - rr * 0.55, rzc + rr * 0.78, 0, steel, "blade_bar"))
    # the back frame: beams along the top, a row of teeth, slats down the back
    for yy in (D * 0.7, D):
        parts.append(K.block(L, 0.34, 0.36, 0, yy, z1 - 0.36, 0, dark, "beam"))
    parts.append(K.block(L, 0.3, 0.26, 0, yf, zt, 0, dark, "sill"))
    parts.append(K.block(L, 0.3, 0.3, 0, yf, z0 - 0.3, 0, dark, "sill"))
    parts.append(K.block(L, 0.28, 0.24, 0, D, z0 - 0.1, 0, dark, "sill"))
    parts.append(K.block(L - 0.1, D - yf, 0.12, 0, (D + yf) / 2, z0 - 0.05, 0, dark, "trough_floor"))
    for i in range(blades):
        x = -L / 2 + (i + 0.5) * L / blades
        # the blades hang from under the roller, a pale edge at the bottom
        bw = L / blades * 0.5
        parts.append(K.block(bw, 0.1, rzc - rr * 0.5 - zt, x, yf + 0.1, zt + 0.25, 0, midm, "blade"))
        parts.append(K.block(bw, 0.12, 0.08, x, yf + 0.1, zt + 0.2, 0, steel, "blade_edge"))
        if i % 2:
            parts.append(K.block(0.12, 0.12, 0.55, x, D, z1, 0, dark, "tooth"))
            parts.append(K.block(0.1, 0.1, z1 - z0, x, D, z0, 0, dark, "back_slat"))
    # the trough: stone blocks in the bays between dark posts
    bw = L / bays
    for i in range(bays + 1):
        x = -L / 2 + i * bw
        parts.append(K.block(0.22, 0.3, zt - z0 + 0.3, x, yf, z0 - 0.1, 0, dark, "bay_post"))
    for i in range(bays):
        x = -L / 2 + (i + 0.5) * bw
        b = K.block(bw - 0.3, 0.9, zt - z0 - 0.1, x, yf + 0.5, z0 + 0.05, 0, stone, "stone_block")
        K.uv_box(b, 1 / (zt - z0))
        parts.append(b)
    # the winch wheel on top at the right, tinted teal
    wx = L / 2 - 1.2
    wh = [K.torus(0.9, 0.07, seg=20, rseg=4, mt=teal, name="winch_rim")]
    for i in range(8):
        sp = K.block(0.85, 0.06, 0.06, 0.42, 0, -0.03, 0, teal, "winch_spoke")
        K.place(sp, Matrix.Rotation(i * math.pi / 4, 4, "Z"))
        wh.append(sp)
    wh.append(K.cyl(0.16, 0.3, seg=8, z=-0.15, mt=dark, name="winch_hub"))
    Mw = Matrix.Translation((wx, D * 0.72, z1 + 1.9)) @ Matrix.Rotation(math.radians(55), 4, "X")
    for p in wh:
        K.place(p, Mw)
    parts += wh
    parts.append(K.block(0.2, 0.2, 2.0, wx, D * 0.72, z1, 0, dark, "winch_stand"))
    parts.append(K.block(1.2, 0.9, 0.9, wx + 0.5, D * 0.72, z1 - 0.1, 0, midm, "winch_housing"))
    # the copper crank out of the right end, and a short one on the left
    xr = L / 2 + 0.55
    parts.append(K.tube([(xr, ry, rzc), (xr + 1.1, ry - 0.2, rzc - 0.1)], [0.06, 0.06], seg=5, sub=1, mt=copper,
                        name="crank"))
    parts.append(K.tube([(xr + 1.1, ry - 0.2, rzc - 0.1), (xr + 1.1, ry - 0.2, rzc - 0.75)], [0.06, 0.06], seg=5,
                        sub=1, mt=copper, name="crank"))
    parts.append(K.cyl(0.13, 0.35, seg=6, x=xr + 1.1, y=ry - 0.2, z=rzc - 1.1, mt=dark, name="crank_knob"))
    parts.append(K.tube([(-xr, ry, rzc), (-xr - 0.6, ry - 0.1, rzc)], [0.05, 0.05], seg=5, sub=1, mt=copper,
                        name="crank"))
    M = Matrix.Translation((mid.x, mid.y, 0)) @ Matrix.Rotation(math.radians(rz), 4, "Z")
    for p in parts:
        K.place(p, M)
    return parts
