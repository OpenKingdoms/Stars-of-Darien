"""Taros hovels: a plank shed and pen under a draped teal hide, and their
wrecks, low mounds of ash-speckled debris criss-crossed with broken
boards, the back boards still standing."""
import math
import random

import numpy as np
from mathutils import Matrix, Vector

import kit as K

FUR_LIGHT = (72, 108, 100)
FUR_DARK = (32, 50, 47)
RENT = (10, 16, 15)
PLANK = (96, 88, 78)
PLANK_GAP = (26, 24, 20)
SHED = (64, 62, 54)
RED = (116, 26, 20)
BLUE = (18, 34, 58)
BLUE_FILL = (25, 40, 90)
ASH = (34, 33, 32)
STICK = (40, 34, 28)


def value_noise2(n, rx, ry, rng):
    """Tiling value noise with rx cells across and ry down."""
    g = rng.random((ry, rx))
    tx, ty = np.arange(n) * rx / n, np.arange(n) * ry / n
    ix0, iy0 = np.floor(tx).astype(int) % rx, np.floor(ty).astype(int) % ry
    ix1, iy1 = (ix0 + 1) % rx, (iy0 + 1) % ry
    fx, fy = tx - np.floor(tx), ty - np.floor(ty)
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    top = g[iy0][:, ix0] * (1 - fx)[None, :] + g[iy0][:, ix1] * fx[None, :]
    bot = g[iy1][:, ix0] * (1 - fx)[None, :] + g[iy1][:, ix1] * fx[None, :]
    return top * (1 - fy)[:, None] + bot * fy[:, None]


def tex_fur(n=256, seed=31):
    """Matted fur: streaks running down the hide (along v), clumped, with a
    fine grain; no blotches and no seams."""
    rng = K._rng(seed)
    v = (0.35 * value_noise2(n, 48, 6, rng) + 0.3 * value_noise2(n, 96, 10, rng) + 0.2 * value_noise2(n, 16, 3, rng)
         + 0.15 * value_noise2(n, 192, 24, rng))
    v = (v - v.min()) / (v.max() - v.min())
    v = np.clip((v - 0.5) * 1.6 + 0.5, 0, 1)
    img = K._mix(np.ones((n, n, 3), np.float32) * K._col(FUR_DARK), np.ones((n, n, 3), np.float32) * K._col(FUR_LIGHT),
                 v)
    grain = rng.random((n, n))
    img = img * (0.9 + 0.2 * grain)[..., None]
    return np.clip(img, 0, 1)


def hide():
    return K.texmat("fur", tex_fur, rough=0.95)


def planks():
    return K.texmat("planks", lambda: K.tex_planks(PLANK, PLANK_GAP, n=128, count=7, seed=32), rough=0.9)


def shed_planks():
    return K.texmat("shed_planks", lambda: K.tex_planks(SHED, PLANK_GAP, n=128, count=9, seed=33), rough=0.9)


def ash():
    """Ash-speckled debris: dark, sown with pale and black grains."""
    return K.texmat("ash", lambda: K.tex_speckle(ASH, [((120, 116, 108), 0.3), ((6, 6, 6), 0.55),
                                                       ((66, 62, 56), 0.45)], n=96, seed=34, size=3), rough=1.0)


def wood(shade=1.0, base=PLANK):
    k = int(round(shade * 20))
    return K.mat("wood_%d_%d" % (base[0], k), tuple(v * k / 20 for v in base), rough=0.9)


def interp(pts, t):
    """Smoothed piecewise lookup in [(t, v), ...]."""
    if t <= pts[0][0]:
        return pts[0][1]
    for (a, va), (b, vb) in zip(pts, pts[1:]):
        if t <= b:
            f = (t - a) / (b - a)
            f = f * f * (3 - 2 * f)
            return va + (vb - va) * f
    return pts[-1][1]


def _seg_dist(x, y, p0, p1):
    ax, ay = p0
    bx, by = p1
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy + 1e-9)))
    return math.hypot(x - ax - t * dx, y - ay - t * dy), t


def draped(x0, x1, y0, y1, prof, edge=0.8, low=0.25, lumps=0.2, seed=1, nx=26, ny=26, folds=(), sag=0.0,
           ridge_var=0.0, front=None, rent=None, poles=(), bulge=0.0):
    """A hide thrown over a frame. prof gives its height across y; the sides
    fall away within `edge` of x0 and x1; it sags between the poles at
    `poles` x positions; folds [(p0, p1, width, depth), ...] ruck it along
    lines (a negative depth is a crease); ridge_var makes the ridge lumpy;
    front(x) is the front edge's height, hanging unevenly; rent is an
    ellipse (cx, cy, rx, ry, turn) torn dark; bulge pushes the side edges
    in and out. UVs run down the hide by surface length, so the fur has no
    seams."""
    rnd = random.Random(seed)
    waves = [(rnd.uniform(1.5, 3.5), rnd.uniform(1.5, 3.5), rnd.uniform(0, 6.3), rnd.uniform(0.4, 1.0))
             for _ in range(4)]
    rv = [(rnd.uniform(1.2, 3.2), rnd.uniform(0, 6.3)) for _ in range(2)] + [(rnd.uniform(4.0, 6.5),
                                                                           rnd.uniform(0, 6.3))]
    bw = [(rnd.uniform(1.5, 3.5), rnd.uniform(0, 6.3)) for _ in range(4)]
    ps = sorted(poles)

    def fn(x, y):
        e = max(0.0, min(1.0, min(x - x0, x1 - x) / edge))
        s = low + (1 - low) * (e * e * (3 - 2 * e))
        z = interp(prof, y) * s
        z *= 1 + ridge_var * sum(w * math.sin(k * x + ph) for (k, ph), w in zip(rv, (1.0, 0.8, 0.35))) / 2.2
        if ps:
            # between two poles the hide hangs lower, most at the edges
            gaps = [(a, b) for a, b in zip(ps, ps[1:]) if a <= x <= b]
            if gaps:
                a, b = gaps[0]
                u = (x - a) / (b - a)
                ey = max(0.0, 1 - min(y - y0, y1 - y) / 1.6)
                z -= sag * math.sin(math.pi * u) * (0.35 + 0.65 * ey)
        for p0, p1, w, d in folds:
            dist, t = _seg_dist(x, y, p0, p1)
            z += d * math.exp(-(dist / w) ** 2) * math.sin(math.pi * min(1.0, t * 1.2 + 0.1)) * min(1.0, z)
        if front is not None:
            f = max(0.0, 1 - (y - y0) / 0.9)
            z = z * (1 - f) + front(x) * f
        fold = sum(a * math.sin(kx * x + ph) * math.sin(ky * y + ph * 0.7) for kx, ky, ph, a in waves) / 4
        z += lumps * fold * min(1.0, z)
        return max(0.05, z)

    grid = [[None] * (nx + 1) for _ in range(ny + 1)]
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx
            y = y0 + (y1 - y0) * j / ny
            u = i / nx
            side = (abs(u - 0.5) * 2) ** 3 * (1 if u > 0.5 else -1)
            x += bulge * side * sum(math.sin(k * y + ph + (0 if u > 0.5 else 2.0)) for k, ph in bw) / 2
            grid[j][i] = Vector((x, y, fn(x, y)))
    torn = set()
    if rent is not None:
        rcx, rcy, rrx, rry, rt = rent
        c, s = math.cos(math.radians(rt)), math.sin(math.radians(rt))
        for j in range(ny + 1):
            for i in range(nx + 1):
                p = grid[j][i]
                u, v = ((p.x - rcx) * c + (p.y - rcy) * s) / rrx, (-(p.x - rcx) * s + (p.y - rcy) * c) / rry
                if u * u + v * v < 1:
                    torn.add((j, i))
                    p.z -= 0.18 * (1 - u * u - v * v)
    # surface length down each column, for the fur's v
    arc = [[0.0] * (nx + 1) for _ in range(ny + 1)]
    for i in range(nx + 1):
        for j in range(1, ny + 1):
            arc[j][i] = arc[j - 1][i] + (grid[j][i] - grid[j - 1][i]).length
    verts = [tuple(grid[j][i]) for j in range(ny + 1) for i in range(nx + 1)]
    faces, fuv, dark = [], [], []
    for j in range(ny):
        for i in range(nx):
            q = [(j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i)]
            faces.append([a * (nx + 1) + b for a, b in q])
            fuv.append([(grid[a][b].x * 0.45, arc[a][b] * 0.45) for a, b in q])
            dark.append(sum(1 for k in q if k in torn) >= 3)
    m = len(verts)
    verts += [(x, y, z - 0.04) for x, y, z in verts]
    nf = len(faces)
    faces += [[m + k for k in reversed(f)] for f in faces[:nf]]
    fuv += [list(reversed(u)) for u in fuv[:nf]]
    dark += dark[:nf]
    ob = K.mesh("hide", verts, faces, hide(), smooth=60, recalc=False)
    ob.data.materials.append(K.mat("rent", RENT, rough=1.0))
    uv = ob.data.uv_layers.new(name="UVMap")
    for p, us, dk in zip(ob.data.polygons, fuv, dark):
        for li, u in zip(p.loop_indices, us):
            uv.data[li].uv = u
        if dk:
            p.material_index = 1
    return ob, fn


def pole(x, y, h, r=0.11, mt=None, lean=(0.0, 0.0)):
    ob = K.cyl(r, h, r * 0.8, seg=6, x=x, y=y, mt=mt or planks(), name="pole")
    if lean != (0.0, 0.0):
        for v in ob.data.vertices:
            f = v.co.z / h
            v.co.x += lean[0] * f
            v.co.y += lean[1] * f
    return ob


def rails(p0, p1, zs, mt, w=0.16, t=0.1):
    out = []
    for z in zs:
        out.append(K.board((p0[0], p0[1], z), (p1[0], p1[1], z), w, t, mt, "rail", up=(0, -1, 0)))
    return out


def shed(x0, x1, yf, yb, hf, hb, mt, roof_mt, seed=1, stakes=None, broken=None, ragged=0.0, poles_up=(),
         leaning=(), roof=True):
    """A plank shed: stake walls round a lean-to roof of boards, high at the
    front (yf, hf) and low at the back (yb, hb). broken=(t0, t1) leaves out
    front stakes and roof boards between those fractions of the width;
    ragged makes the stake tops uneven; poles_up are (x, y, height) poles
    standing above the roof; leaning are boards (a, b) propped against it;
    roof False leaves the roof boards off."""
    rnd = random.Random(seed)
    parts = []
    n = stakes or max(6, int((x1 - x0) / 0.24))

    def keep(t):
        return broken is None or not (broken[0] < t < broken[1])

    def top_f(t):
        h = hf * (1 if keep(t) else rnd.uniform(0.2, 0.5))
        return h * (1 + rnd.uniform(-ragged, ragged * 0.6))

    parts += K.slats((x0, yf), (x1, yf), 0, hf, n, mt, gap=0.08, seed=seed, tops=top_f)
    parts += K.slats((x0, yb), (x1, yb), 0, hb, n, mt, gap=0.08, seed=seed + 1,
                     tops=lambda t: hb * (1 + rnd.uniform(-ragged, ragged * 0.6)))
    for x in (x0, x1):
        # with the roof fallen in, the side walls are snapped to about the back wall's height
        side = ((lambda t: (hf + (hb - hf) * t) * (1 + rnd.uniform(-ragged, ragged * 0.5))) if roof else
                (lambda t: hb * rnd.uniform(0.35, 0.95)))
        parts += K.slats((x, yf), (x, yb), 0, hf, max(3, int(abs(yb - yf) / 0.26)), mt, gap=0.08, seed=seed + 2,
                         tops=side)
    for x in (x0 - 0.05, x1 + 0.05):
        # a fallen roof leaves the front corner posts snapped
        parts.append(pole(x, yf - 0.05, hf + 0.7 if roof else hf * rnd.uniform(0.3, 0.55), 0.14, mt))
        parts.append(pole(x, yb, hb + 0.3, 0.13, mt))
    for px, py, ph in poles_up:
        parts.append(pole(px, py, ph, 0.11, mt, lean=(rnd.uniform(-0.3, 0.3), rnd.uniform(-0.2, 0.2))))
    if roof:
        parts += rails((x0 - 0.1, yf - 0.1), (x1 + 0.1, yf - 0.1), [hf * 0.72], mt)
    # the roof boards, running along x, stepping from front to back
    k = max(4, int(abs(yb - yf) / 0.3)) if roof else 0
    for i in range(k):
        t = (i + 0.5) / k
        y = yf + (yb - yf) * t
        z = hf + 0.1 + (hb - hf) * t
        if broken is not None and rnd.random() < 0.4:
            continue
        shade = rnd.uniform(0.75, 1.1)
        dx0, dx1 = rnd.uniform(-0.2, 0.3) * (ragged > 0), rnd.uniform(-0.3, 0.2) * (ragged > 0)
        parts.append(K.board((x0 - 0.25 + dx0, y, z + rnd.uniform(-0.04, 0.04)),
                             (x1 + 0.25 + dx1, y, z + rnd.uniform(-0.04, 0.04)), abs(yb - yf) / k * 0.72, 0.08,
                             wood(shade), "roof_board"))
    for a, b in leaning:
        parts.append(K.board(a, b, 0.22, 0.07, wood(rnd.uniform(0.7, 1.0)), "leaning_board", up=(0, -1, 0)))
    for p in parts:
        K.uv_box(p, 0.5, rnd.random(), rnd.random())
    return parts


def pen(x0, x1, yf, yb, h, mt, seed=1, mid=0.5):
    """A pen of close upright slats under a top rail, with a rail across
    the middle and a post at each front corner."""
    parts = K.slats((x0, yf), (x1, yf), 0, h, int((x1 - x0) / 0.19), mt, thick=0.09, gap=0.22, seed=seed,
                    jitter=0.08)
    parts += rails((x0 - 0.12, yf - 0.08), (x1 + 0.12, yf - 0.08), [h - 0.12], mt, w=0.24, t=0.12)
    parts += rails((x0 - 0.1, yf - 0.08), (x1 + 0.1, yf - 0.08), [h * mid], mt)
    for x in (x0, x1):
        parts += K.slats((x, yf), (x, yb), 0, h, int(abs(yb - yf) / 0.22), mt, thick=0.09, gap=0.22, seed=seed + 3,
                         jitter=0.08)
        parts += rails((x, yf - 0.1), (x, yb + 0.1), [h - 0.12], mt, w=0.2, t=0.1)
        parts.append(pole(x, yf - 0.08, h + 0.25, 0.13, mt))
    for p in parts:
        K.uv_box(p, 0.5)
    return parts


def trough(x, y, w, d, h, rz=0.0):
    """A plank trough brim-full of dark blue."""
    mt = planks()
    parts = []
    c, s = math.cos(math.radians(rz)), math.sin(math.radians(rz))
    for (ax, ay, bx, by) in ((-w / 2, -d / 2, w / 2, -d / 2), (w / 2, -d / 2, w / 2, d / 2),
                             (w / 2, d / 2, -w / 2, d / 2), (-w / 2, d / 2, -w / 2, -d / 2)):
        pa = (x + ax * c - ay * s, y + ax * s + ay * c)
        pb = (x + bx * c - by * s, y + bx * s + by * c)
        parts += K.wall_run(pa, pb, 0, h, 0.12, mt, name="trough")
    parts.append(K.block(w - 0.2, d - 0.2, h - 0.08, x, y, 0, rz, K.mat("blue_fill", BLUE_FILL, rough=0.4, spec=0.5),
                         "trough_fill"))
    for p in parts:
        K.uv_box(p, 0.5)
    return parts


def sticks(items, seed=1):
    """Thin dark sticks and broken boards strewn on the ground or a heap:
    [(x0, y0, z0, x1, y1, z1), ...]; a short item is a stick, a long one a board."""
    rnd = random.Random(seed)
    out = []
    for x0, y0, z0, x1, y1, z1 in items:
        L = math.dist((x0, y0), (x1, y1))
        if rnd.random() < 0.5:
            mt = wood(rnd.uniform(0.5, 0.95), STICK)
            w, t = rnd.uniform(0.07, 0.11), rnd.uniform(0.05, 0.08)
        else:
            mt = wood(rnd.uniform(0.6, 1.05))
            w, t = rnd.uniform(0.13, 0.19), 0.05
        up = (rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4), 1)
        out.append(K.board((x0, y0, z0), (x1, y1, z1), w, t, mt, "stick", up=up))
    return out


def scraps(pts, mt, seed=1, size=(0.9, 0.6)):
    """Torn pieces of hide or cloth lying crumpled on a heap: a ragged,
    rounded outline with no points, rucked up in soft folds."""
    rnd = random.Random(seed)
    out = []
    for x, y, z in pts:
        w, d = size[0] * rnd.uniform(0.7, 1.3) / 2, size[1] * rnd.uniform(0.7, 1.3) / 2
        edge = K.outline_fn(rnd.randrange(10 ** 6), 0.2, lobes=(2, 3, 4))
        ph = rnd.uniform(0, 6.3)
        seg, rings = 12, 3
        verts = [(0.0, 0.0, 0.1)]
        for i in range(1, rings + 1):
            f = i / rings
            for j in range(seg):
                t = 2 * math.pi * j / seg
                u, v = f * edge(t) * w * math.cos(t), f * edge(t) * d * math.sin(t)
                verts.append((u, v, 0.1 * (1 - f) + 0.09 * math.sin(4 * u / w + ph) * math.cos(3 * v / d - ph)))
        faces = [[0, 1 + j, 1 + (j + 1) % seg] for j in range(seg)]
        for i in range(rings - 1):
            a0, b0 = 1 + i * seg, 1 + (i + 1) * seg
            faces += [[a0 + j, b0 + j, b0 + (j + 1) % seg, a0 + (j + 1) % seg] for j in range(seg)]
        m = len(verts)
        verts += [(vx, vy, vz - 0.03) for vx, vy, vz in verts]
        faces += [[m + k for k in reversed(f)] for f in faces[:]]
        s = K.mesh("scrap", verts, faces, mt, smooth=60, recalc=False)
        K.place(s, Matrix.Translation((x, y, z + 0.04)) @ Matrix.Rotation(rnd.uniform(0, 6.3), 4, "Z")
                @ Matrix.Rotation(rnd.uniform(-0.25, 0.25), 4, "X"))
        K.uv_box(s, 0.4)
        out.append(s)
    return out


def sag_roof(x0, x1, yf, yb, zf, zb, sag, hole, mt, seed=1, nx=8, ny=6):
    """A plank roof fallen in: a sheet from its back edge (yb, zb) down to
    its front (yf, zf), sagging sag in the middle and twisted, with a hole
    (cx, cy, rx, ry) torn through it."""
    rnd = random.Random(seed)
    tw = rnd.uniform(-0.25, 0.25)
    verts, faces = [], []

    def z(i, j):
        u, v = i / nx, j / ny
        return (zf + (zb - zf) * v - sag * math.sin(math.pi * u) * math.sin(math.pi * v) + tw * (u - 0.5) * v
                + rnd.uniform(-0.05, 0.05))
    for j in range(ny + 1):
        for i in range(nx + 1):
            verts.append((x0 + (x1 - x0) * i / nx + rnd.uniform(-0.05, 0.05) * (0 < i < nx), yf + (yb - yf) * j / ny,
                          z(i, j)))
    hx, hy, hrx, hry = hole
    for j in range(ny):
        for i in range(nx):
            cx = x0 + (x1 - x0) * (i + 0.5) / nx
            cy = yf + (yb - yf) * (j + 0.5) / ny
            if ((cx - hx) / hrx) ** 2 + ((cy - hy) / hry) ** 2 < 1:
                continue
            a = j * (nx + 1) + i
            faces.append([a, a + 1, a + nx + 2, a + nx + 1])
    m = len(verts)
    verts += [(x, y, zz - 0.07) for x, y, zz in verts]
    faces += [[m + k for k in reversed(f)] for f in faces[:]]
    ob = K.mesh("sag_roof", verts, faces, mt, smooth=50, recalc=False)
    for p in ob.data.polygons[:len(faces) // 2]:
        if p.normal.z < 0:
            p.flip()
    K.uv_box(ob, 0.5)
    return ob


def debris(spec, boards, seed=1, pebbles=60, poles=6, bits=14, lean=()):
    """The wreck's debris: a low, soft-edged mound of ash (cx, cy, rx, ry,
    h[, turn[, peak]]) heaped with broken planks and poles crossing at all angles
    and tilts, its ragged edge breaking up into loose grit and short bits
    of board, its bulk leaning toward peak; lean are boards (a, b) propped
    against what still stands.
    Returns (parts, fn)."""
    cx, cy, rx, ry, h = spec[:5]
    rot = spec[5] if len(spec) > 5 else 0.0
    peak = spec[6] if len(spec) > 6 else (0.0, 0.0)
    ob, fn, inside = K.heap(cx, cy, rx, ry, h, ash(), seed=seed, rings=7, seg=34, amp=0.26, lump=0.22, power=1.9,
                            rot=rot, name="debris", uvk=0.6, sink=0.1, peak=peak)
    parts = [ob]
    rnd = random.Random(seed + 1)
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))

    def at(r, a):
        u, v = rx * r * math.cos(a), ry * r * math.sin(a)
        return cx + u * c - v * s, cy + u * s + v * c
    # loose grit, and short bits of board scattered past the edge
    grit = [K.mat("grit_dark", (22, 21, 20), rough=1.0), K.mat("grit_pale", (78, 74, 68), rough=1.0)]
    for k in range(pebbles):
        x, y = at(rnd.uniform(0.8, 1.35), rnd.uniform(0, 2 * math.pi))
        sz = rnd.uniform(0.1, 0.22)
        parts.append(K.rock((x, y, max(0.0, fn(x, y)) + sz * 0.2), (sz, sz * 0.8, sz * 0.5), rnd.randrange(10 ** 6),
                            grit[k % 2], "grit"))
    items = []
    for k in range(bits):
        x, y = at(rnd.uniform(0.85, 1.3), rnd.uniform(0, 2 * math.pi))
        L, a = rnd.uniform(0.25, 0.6), rnd.uniform(0, math.pi)
        items.append((x, y, 0.04, x + L * math.cos(a), y + L * math.sin(a), 0.04 + rnd.uniform(0, 0.08)))
    parts += sticks(items, seed=seed * 7)
    # broken planks, and poles, crossing on the heap; a third of them
    # propped up at one end on the others
    n, (lo, hi) = boards
    for k in range(n + poles):
        for _ in range(30):
            x, y = at(math.sqrt(rnd.random()) * 0.85, rnd.uniform(0, 2 * math.pi))
            if inside(x, y):
                break
        L = rnd.uniform(lo, hi) * (1.2 if k >= n else 1.0)
        a = rnd.uniform(0, math.pi)
        dx, dy = math.cos(a) * L / 2, math.sin(a) * L / 2
        za, zb = max(0.03, fn(x - dx, y - dy)), max(0.03, fn(x + dx, y + dy))
        if rnd.random() < 0.35:
            lift = rnd.uniform(0.25, 0.7)
            if rnd.random() < 0.5:
                za += lift
            else:
                zb += lift
        if k < n:
            parts += sticks([(x - dx, y - dy, za + 0.04, x + dx, y + dy, zb + 0.04)], seed=seed * 100 + k)
        else:
            r = rnd.uniform(0.07, 0.1)
            parts.append(K.tube([(x - dx, y - dy, za + r), (x + dx, y + dy, zb + r)], [r, r * 0.85], seg=5, sub=1,
                                mt=wood(rnd.uniform(0.55, 0.85)), name="pole_broken"))
    for pa, pb in lean:
        parts.append(K.board(pa, pb, rnd.uniform(0.16, 0.22), 0.06, wood(rnd.uniform(0.6, 0.95)), "leaning_board",
                             up=(0, -1, 0)))
    return parts, fn


def hide_bits(fn, pts, seed=1):
    """Torn teal hide lying between the planks."""
    return scraps([(x, y, fn(x, y)) for x, y in pts], hide(), seed=seed, size=(1.0, 0.7))


# ---- the models -------------------------------------------------------------------

def hut01(name="TarHut01"):
    hd, fn = draped(-2.45, 2.45, -2.6, 1.95,
                    [(-2.6, 0.6), (-2.2, 1.45), (-1.3, 2.5), (-0.4, 3.2), (0.4, 3.35), (1.2, 2.8), (1.95, 1.7)],
                    edge=0.9, low=0.3, lumps=0.4, seed=3, nx=24, ny=24,
                    folds=[((0.0, 0.4), (2.3, -2.4), 0.32, -0.8), ((0.45, 0.6), (2.5, -1.8), 0.3, 0.35),
                           ((-1.9, 1.2), (-0.3, -2.0), 0.4, 0.4), ((-2.3, -0.4), (-0.6, 1.4), 0.3, -0.35)],
                    sag=0.45, poles=(-2.35, 2.35), ridge_var=0.08, bulge=0.18,
                    front=lambda x: 0.62 + 0.25 * math.sin(2.7 * x + 1.0) + 0.14 * math.sin(6.1 * x),
                    rent=(-1.6, -0.5, 0.5, 0.24, 25))
    parts = [hd]
    parts += pen(-2.2, -0.2, 2.0, 3.3, 3.9, planks(), seed=4)
    parts += shed(-0.1, 2.25, 1.9, 3.75, 4.6, 2.3, shed_planks(), shed_planks(), seed=5)
    parts.append(K.board((0.3, 2.3, 2.35), (2.1, 2.3, 2.3), 0.3, 0.05, K.mat("blue_cloth", BLUE), "band",
                         up=(0, -1, 0)))
    parts.append(pole(0.3, 1.8, 6.0, 0.12))
    parts += K.slats((-2.3, -3.2), (2.3, -3.2), 0, 0.75, 16, planks(), gap=0.1, seed=6)
    parts += trough(0.15, -2.95, 1.8, 0.75, 0.55)
    for x in (-2.35, 2.35):
        for y in (-3.05, 1.8):
            parts.append(pole(x, y, 1.1 if y < 0 else 2.2, 0.13))
    return K.sturdy(parts, 1.0, 0.88)


def hut02(name="TarHut02"):
    hd, fn = draped(-3.85, 1.35, -1.7, 2.0,
                    [(-1.7, 0.3), (-1.2, 1.6), (-0.6, 2.9), (0.2, 3.9), (0.9, 4.2), (1.5, 3.3), (2.0, 2.0)],
                    edge=0.75, low=0.25, lumps=0.45, seed=7, nx=26, ny=22,
                    folds=[((-1.4, 0.9), (0.2, -1.4), 0.3, -0.8), ((-0.9, 1.0), (0.8, -1.2), 0.3, 0.4),
                           ((-3.2, 0.2), (-2.0, -1.3), 0.35, 0.45), ((0.4, 1.5), (1.2, -0.6), 0.3, -0.5),
                           ((-2.6, 1.6), (-2.2, -0.8), 0.3, -0.45)],
                    sag=0.4, poles=(-3.6, -1.2, 1.2), ridge_var=0.22, bulge=0.3,
                    front=lambda x: 0.45 + 0.2 * math.sin(2.1 * x + 0.4) + 0.1 * math.sin(5.3 * x),
                    rent=(-3.0, -0.35, 0.45, 0.22, -20))
    parts = [hd]
    parts += shed(1.15, 4.1, -1.6, 0.6, 4.2, 3.9, shed_planks(), planks(), seed=8, ragged=0.12,
                  poles_up=((1.6, -1.55, 5.6), (3.2, -1.5, 5.2), (4.0, 0.3, 5.0)),
                  leaning=(((1.5, -2.3, 0.0), (1.9, -1.7, 3.0)), ((3.7, -2.2, 0.0), (3.4, -1.7, 2.6))))
    parts.append(K.block(0.35, 0.06, 2.4, 2.25, -1.72, 0.3, 0, K.mat("blue_cloth", BLUE), "door_cloth"))
    # blue cloth hung inside the shed
    cloth = K.sheet(-0.9, 0.9, -0.5, 0.5, 5, 3, lambda u, v: 0.12 * math.sin(3 * u) * math.cos(4 * v),
                    K.mat("blue_cloth", BLUE), "cloth", thick=0.03)
    K.place(cloth, Matrix.Translation((2.8, -0.6, 2.0)) @ Matrix.Rotation(math.radians(80), 4, "X"))
    parts.append(cloth)
    parts.append(pole(-3.5, 1.2, 4.6, 0.12))
    parts += [K.block(1.8, 0.75, 0.55, -1.1, -1.75, 0, 3, K.mat("red_paint", RED, rough=0.7), "trough"),
              K.block(1.9, 0.85, 0.1, -1.1, -1.75, 0.55, 3, K.mat("red_dark", (70, 16, 12), rough=0.7), "trough_rim")]
    # two planks laid across the chest, and sticks and boards strewn along the front
    parts += sticks([(-2.3, -1.6, 0.66, -0.1, -2.05, 0.7), (-1.8, -1.35, 0.7, -0.4, -2.3, 0.62)], seed=9)
    rnd = random.Random(10)
    items = []
    for k in range(11):
        x = -3.8 + 7.6 * (k + rnd.random()) / 11
        y = rnd.uniform(-2.4, -1.85)
        L, a = rnd.uniform(0.5, 1.4), rnd.uniform(-0.6, 0.6) + (0 if k % 3 else 1.2)
        items.append((x, y, 0.04, x + L * math.cos(a), y + L * math.sin(a) * 0.5, rnd.uniform(0.04, 0.3)))
    parts += sticks(items, seed=11)
    return K.sturdy(parts, 1.0, 0.88)


def hut01_ruin(name="TarHut01a"):
    parts, fn = debris((0.0, -0.7, 2.45, 3.05, 0.8), (30, (0.8, 2.2)), seed=11, poles=6, lean=[
        ((-1.6, 0.9, 0.3), (-1.4, 1.95, 2.6)), ((-0.6, 1.1, 0.35), (-0.9, 1.95, 3.0)),
        ((0.9, 1.0, 0.35), (1.2, 1.85, 1.6)), ((1.7, 0.7, 0.3), (1.9, 1.8, 1.3)),
        ((-1.2, -3.1, 0.3), (-1.0, -3.7, 0.55)), ((0.6, -3.0, 0.35), (0.9, -3.7, 0.5))])
    parts += pen(-2.0, 0.1, 2.1, 3.1, 3.5, planks(), seed=12)
    # the shack at the back right fallen in: its stakes snapped to uneven
    # heights, its roof a sagging sheet run down to the ground, holed
    parts += shed(0.25, 2.2, 1.95, 3.6, 4.4, 2.2, shed_planks(), shed_planks(), seed=13, broken=(0.0, 1.0),
                  ragged=0.35, roof=False)
    parts.append(sag_roof(0.05, 2.45, 1.35, 3.6, 0.35, 2.25, 0.6, (0.95, 2.55, 0.42, 0.45),
                          K.texmat("sag_planks", lambda: K.tex_planks((44, 42, 36), PLANK_GAP, n=128, count=9, seed=35),
                                   rough=0.9), seed=17))
    parts.append(pole(0.25, 1.8, 5.8, 0.12))
    parts += K.slats((-2.0, -3.75), (2.2, -3.75), 0, 0.5, 14, planks(), gap=0.15, seed=14, jitter=0.9)
    parts += hide_bits(fn, [(-1.6, 0.4), (-0.5, -2.2), (1.8, 0.9), (0.6, -1.3), (-1.8, -2.6), (0.9, -2.9),
                            (-0.9, -0.6), (1.2, -0.2), (-1.3, 1.5)], seed=15)
    parts += scraps([(-1.5, -3.3, fn(-1.5, -3.3)), (0.8, -3.6, 0.03), (1.6, -1.9, fn(1.6, -1.9))],
                    K.mat("blue_crate", BLUE_FILL), seed=16, size=(0.8, 0.5))
    return K.sturdy(parts, 1.0, 0.88)


def hut02_ruin(name="TarHut02a"):
    # the heap slopes down to the shed's foot rather than rising against it
    parts, fn = debris((-1.3, 0.0, 2.8, 2.3, 0.85, 4, (-0.45, 0.1)), (32, (0.8, 2.3)), seed=21, poles=6, lean=[
        ((1.0, -1.1, 0.25), (1.75, -1.2, 1.9)), ((0.9, 0.3, 0.3), (1.75, 0.2, 2.3)),
        ((-3.6, 0.6, 0.2), (-3.3, 1.0, 2.2))])
    parts += shed(1.8, 4.2, -1.8, 0.65, 4.0, 3.7, shed_planks(), planks(), seed=22)
    parts.append(pole(-3.3, 1.0, 3.2, 0.12))
    parts.append(pole(1.3, 1.4, 4.4, 0.12))
    parts += hide_bits(fn, [(-0.3, 1.6), (0.4, 1.4), (-2.3, -1.6), (-3.2, 0.3), (-1.6, 1.0), (0.3, -1.2),
                            (-2.4, 0.9)], seed=23)
    red = K.mat("red_paint", RED, rough=0.7)
    parts += scraps([(-1.2, 0.0, fn(-1.2, 0.0)), (-0.2, -0.4, fn(-0.2, -0.4)), (-0.7, -2.6, fn(-0.7, -2.6))], red,
                    seed=24, size=(0.75, 0.55))
    return K.sturdy(parts, 1.0, 0.88)
