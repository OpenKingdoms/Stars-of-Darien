"""Low-level pieces for the bespoke termite mounds, run inside Blender.

What a Zhon termite mound is: one solid mass of sun-dried earth built as a
tight bundle of upright chimneys fused along their length. Up to its
shoulder it is a steep drum, a little wider at the foot, its wall fluted by
the outer chimneys, some of which stop short as pale knobs down the face.
Over the shoulder the top is one continuous earthen crown rising toward the
back, studded with half-sunk knobs, and a few chimneys come free of it as
stout fingers at the back, each ending in a rounded pale cap of fresh mud. The body is dark brown with pale
grit, and pale spoil spills out over the ground at the front.

Each ZonTmoundNN.py fits its own mound to its own sprite and calls run().
Shared here are only the fitting frame and the pieces: a rib column, the
domed core, a crown knob, a proud finger, the spoil fan, the materials and
the export. sprites.json comes from readsprite.py (system Python).
"""
import json
import math
import os
import random
import sys

import bmesh

HERE = os.path.dirname(os.path.abspath(__file__))
FAM = os.path.dirname(HERE)
for _p in (HERE, FAM):
    if _p not in sys.path:
        sys.path.insert(0, _p)
import kit  # noqa: E402
from kit import hk, lathe, rock  # noqa: E402

OUT = "D:/OKReplace/hand/zhon_structures"
SPR = json.load(open(os.path.join(HERE, "sprites.json")))
SMOOTH = 75  # six-sided columns shade round
K = 0.894  # handkit's classic render draws depth and height at this share of the classic projection


def sprite(name):
    return SPR[name]


def interp(pts, c):
    """Piecewise-linear value at c through (c, v) points sorted by c."""
    if c <= pts[0][0]:
        return pts[0][1]
    for (c0, v0), (c1, v1) in zip(pts, pts[1:]):
        if c <= c1:
            return v0 + (v1 - v0) * (c - c0) / max(1e-9, c1 - c0)
    return pts[-1][1]


class Mound:
    """The fitted frame of one mound. The wall stands on an ellipse (cx, cy,
    rx, ry). sky lists (picture column, render row) points the back rim and
    the crown reach in the classic render, and the proud fingers take the sprite's
    peaks above it. sx is the width scale from the picture to the model."""

    def __init__(self, name, rx, ry, sky, cx=0.0, cy=0.0, sx=1.1, sr=0.23, dome=1.6, k=2.0, taper=0.0, tilt=0.0,
                 side=1.1):
        S = SPR[name]
        self.taper = taper  # the foot is this much wider than the shoulder, a beehive's batter
        self.tilt = tilt  # the crown rises this much from its middle to the back, and falls as much to the front
        self.S, self.hx, self.hy, self.w = S, S["hx"], S["hy"], S["w"]
        self.rx, self.ry, self.cx, self.cy, self.sx, self.sr, self.dome, self.k = rx, ry, cx, cy, sx, sr, dome, k
        self.sky = sorted(sky)
        self.side = side
        self.H = 0.0
        self.H = self.rim(cx)

    def col(self, x):
        return self.hx + x * 16 / self.sx

    def x_of(self, c):
        return (c - self.hx) * self.sx / 16

    def up(self, x):
        """The skyline at x in classic-projection px above the anchor."""
        return (self.hy - interp(self.sky, self.col(x))) / K

    def back(self, x):
        t = max(-0.98, min(0.98, (x - self.cx) / self.rx))
        return self.cy + self.ry * math.sqrt(1 - t * t)

    def rim(self, x):
        """Tip height of a back-rim column at x, so its nub meets the skyline.
        At the flanks, where the back is shallow, held within 0.6 and side
        times the middle's so the sides do not shoot up."""
        z = (self.up(x) - 16 * (self.back(x) + self.sr * 0.9)) / 8 - 0.05
        return max(0.6 * self.H, min(self.side * self.H, z)) if self.H else z

    def rho(self, x, y):
        return math.hypot((x - self.cx) / self.rx, (y - self.cy) / self.ry)

    def crown(self, x, y):
        z = self.rim(x) + self.dome * max(0.0, 1 - self.rho(x, y) ** self.k) + self.tilt * (y - self.cy) / self.ry
        return min(z, (self.up(x) - 16 * (y + 0.6 * self.sr)) / 8)

    def at(self, a, s=1.0, f=1.0):
        """A point on the ellipse at angle a, radius share s, at height share f
        of the wall (1 the shoulder, 0 the foot, which is taper wider)."""
        s *= 1 + self.taper * (1 - f)
        return self.cx + s * self.rx * math.cos(a), self.cy + s * self.ry * math.sin(a)

    def render_row(self, y, z):
        return self.hy - K * (16 * y + 8 * z)


def mats(b, body, core, neck, cap, fleck, low, sand, sand2, dark, seed=1, fl=0.15):
    """The mound's materials. Shaft faces take pale grit at fl of them and no
    dark. The lower wall is a shade darker with a little of the body tone."""
    m = dict(body=b.mat("mound_body", body), core=b.mat("mound_core", core), cap=b.mat("mound_cap", cap),
             neck=b.mat("mound_neck", neck), fleck=b.mat("mound_fleck", fleck), low=b.mat("mound_low", low),
             dark=b.mat("mound_dark", dark),
             sand=b.mat("mound_sand", sand, rough=0.95), sand2=b.mat("mound_sand2", sand2, rough=0.95))
    b.rgb = dict(mound_body=body, mound_core=core, mound_neck=neck, mound_low=low)
    rng = random.Random(seed)
    b.speckle.update({m["body"]: (rng.randrange(999), [(m["fleck"], fl)]),
                      m["core"]: (rng.randrange(999), [(m["fleck"], fl * 0.8), (m["body"], 0.3)]),
                      m["low"]: (rng.randrange(999), [(m["body"], 0.3), (m["fleck"], fl * 0.3)]),
                      m["sand"]: (m["sand2"], 0.4, rng.randrange(999))})
    return m


def _bands(z0, z1, step):
    n = max(1, int(math.ceil((z1 - z0) / step - 1e-6)))
    return [z0 + (z1 - z0) * i / n for i in range(n + 1)]


def column(b, m, x, y, z0, top, r, rng, seg=6, lean=(0.0, 0.0), crook=0.1, face=None, flat=1.0, step=0.6,
           plain=0.45, flare=1.2):
    """One rib of the wall from z0 to a rounded pale nub at top. The lower
    plain share of the shaft is one band of the darker wall tone, and above it
    the shaft is split every step so no speckled face runs tall. The rib is
    flattened to flat of its width along the direction face (front to back)."""
    c = top - 0.75 * r
    L = max(0.05, c - z0)
    ph, px_, py_ = rng.uniform(0, 6.28), rng.uniform(0, 6.28), rng.uniform(0, 6.28)

    def oval(a, z, i):
        return 1.0 + 0.07 * math.cos(2 * a + ph)
    zs = _bands(z0 + plain * L, c, step) if L > 0.8 else [z0 + 0.4 * L, c]
    rings = lathe(b.bm(m["low"]), x, y, [(r * flare, z0), (r * 1.02, zs[0])], seg=seg, wobble=oval,
                  bottom=False, top=False)
    rings += lathe(b.bm(m["body"]), x, y, [(r * (1.0 - 0.04 * (z - zs[0]) / max(0.1, c - zs[0])), z) for z in zs],
                   seg=seg, wobble=oval, bottom=False, top=False)
    rings += _tip(b, m, x, y, c, top, r * 0.96, seg, oval, rings=1)
    _bend(rings, x, y, z0, r, lean, crook, face, flat, px_, py_)
    return c


def _tip(b, m, x, y, c, top, r, seg, oval, rings=2):
    """A rounded end: the shoulder in the neck tone rounding over, only a
    small pale nub at the very top, so no pale disc shows from above."""
    h = top - c
    prof = [(r, c), (r * 0.86, c + 0.5 * h), (r * 0.52, c + 0.85 * h)] if rings > 1 else [(r, c), (r * 0.52, c + 0.85 * h)]
    out = lathe(b.bm(m["neck"]), x, y, prof, seg=seg, wobble=oval, bottom=False, top=False)
    out += lathe(b.bm(m["cap"]), x, y, [(r * 0.52, c + 0.85 * h), (0, top)], seg=seg, wobble=oval, bottom=False)
    return out


def _bend(rings, x, y, z0, r, lean, crook, face, flat, px_, py_):
    for ring in rings:
        z = ring[0].co.z
        dz = max(0.0, z - z0)
        kx = crook * r * math.sin(dz * 2.1 + px_)
        ky = crook * r * math.sin(dz * 1.7 + py_)
        for v in ring:
            if face is not None and flat != 1.0:
                ux, uy = math.cos(face), math.sin(face)
                d = ((v.co.x - x) * ux + (v.co.y - y) * uy) * (flat - 1.0)
                v.co.x += ux * d
                v.co.y += uy * d
            v.co.x += lean[0] * dz + kx
            v.co.y += lean[1] * dz + ky


def core(b, M, m, angles, drop=0.15, valley=0.04, foot=1.05, walls=(0.0, 0.45, 0.8, 1.0),
         rings=(0.88, 0.76, 0.62, 0.44, 0.24), groove=0.12, flute_to=0.66, zt=None):
    """The fused mass: a fluted wall on the outer ring (ridges behind the
    columns, valleys between) up to just under the rim tips, then a dome
    only drop under the crown, so the top is one continuous lit surface.
    The flutes run on up the dome as grooves between the chimneys' fused
    backs, groove deep at the shoulder and fading out at flute_to, so the
    classic camera sees them as the picture's upright streaks. zt(a)
    overrides the wall top. Returns the dome height at (x, y)."""
    bm = b.bm(m["core"])
    lowm = b.bm(m["low"])
    A = []
    for i, a in enumerate(angles):
        a2 = angles[(i + 1) % len(angles)] + (2 * math.pi if i + 1 == len(angles) else 0.0)
        A += [(a, 0.99), ((a + a2) / 2, 1 - valley / min(M.rx, M.ry))]

    def top(a):
        x, y = M.at(a)
        return zt(a) if zt else M.crown(x, y) - drop
    co = []
    for f in walls:
        sf = foot if f == 0 else 1.0
        co.append([(*M.at(a, s * sf, f), f * top(a)) for a, s in A])
    # the lower wall is its own darker part, each band made in its lower ring's part
    wall = None
    for f0, c0, c1 in zip(walls, co, co[1:]):
        tgt = lowm if f0 < 0.5 else bm
        r0 = [tgt.verts.new(p) for p in c0]
        r1 = [tgt.verts.new(p) for p in c1]
        _bridge(tgt, r0, r1)
        wall = r1
    prev = wall
    step = 1
    for s in rings:
        if s < flute_to and step == 1:
            step = 2
        ring = []
        fade = max(0.0, min(1.0, (s - flute_to) / max(0.05, 0.86 - flute_to)))
        for i, (a, sv) in enumerate(A[::step]):
            x, y = M.at(a, s * (sv if step == 1 else 1.0))
            z = M.crown(x, y) - drop
            if step == 1:
                z += (-groove if i % 2 else 0.25 * groove) * (0.35 + 0.65 * fade)
            z += 0.05 * math.sin(7.3 * x + 2.1) * math.sin(6.1 * y + 0.7)
            ring.append(bm.verts.new((x, y, z)))
        _bridge(bm, prev, ring)
        prev = ring
    c = bm.verts.new((M.cx, M.cy, M.crown(M.cx, M.cy) - drop))
    for i in range(len(prev)):
        bm.faces.new((prev[i], prev[(i + 1) % len(prev)], c))
    return lambda x, y: M.crown(x, y) - drop


def ridges(M, angles, n, rho, rng, avoid=(), pitch=0.55):
    """Up to n sites on the dome's ridges (behind the rib columns) at radius
    share rho (lo, hi), clear of avoid and of each other."""
    out = []
    for _ in range(600):
        if len(out) >= n:
            break
        a = rng.choice(angles)
        x, y = M.at(a, rng.uniform(*rho))
        if all(math.hypot(x - px, y - py) >= pitch for px, py in list(avoid) + out):
            out.append((x, y))
    return out


def _bridge(bm, r0, r1):
    n0, n1 = len(r0), len(r1)
    if n0 == n1:
        for i in range(n0):
            bm.faces.new((r0[i], r0[(i + 1) % n0], r1[(i + 1) % n1], r1[i]))
    else:  # n0 == 2 n1
        for i in range(n1):
            bm.faces.new((r0[2 * i], r0[2 * i + 1], r0[(2 * i + 2) % n0], r1[(i + 1) % n1], r1[i]))


def knob(b, m, x, y, zd, proud, r, rng, seg=7, stretch=1.25, tilt=(0.05, 0.2)):
    """A crown chimney's end half-sunk in the dome: a rounded lump standing
    proud of the dome by proud, longer front to back and tipped back a
    little so the classic camera sees it as a fingertip over a short shaft.
    Its shoulder is the dome's own tone and only the very top is pale."""
    top = zd + proud
    c = top - 0.75 * r
    z0 = min(zd - 0.25, c - 0.15)
    ph = rng.uniform(-0.3, 0.3)
    lean = rng.uniform(*tilt)
    h = top - c

    def oval(a, z, i):  # stretched along y
        return math.hypot(math.cos(a + ph), math.sin(a + ph) * stretch) / math.hypot(math.cos(a + ph), math.sin(a + ph))
    rings = lathe(b.bm(m["body"]), x, y, [(r * 1.15, z0), (r, c), (r * 0.86, c + 0.5 * h), (r * 0.5, c + 0.85 * h)],
                  seg=seg, wobble=oval, bottom=False, top=False)
    rings += lathe(b.bm(m["cap"]), x, y, [(r * 0.5, c + 0.85 * h), (0, top)], seg=seg, wobble=oval, bottom=False)
    for ring in rings:
        dz = max(0.0, ring[0].co.z - zd)
        for v in ring:
            v.co.y += lean * dz


def finger(b, m, x, y, zd, top, r, rng, seg=8, step=0.32, lean=(0.0, 0.0), crook=0.08):
    """A chimney standing free of the dome: flared into it, a stout shaft
    split every step for the speckle, a rounded pale cap."""
    c = top - 0.8 * r
    z0 = zd - 0.35
    ph, px_, py_ = rng.uniform(0, 6.28), rng.uniform(0, 6.28), rng.uniform(0, 6.28)

    def oval(a, z, i):
        return 1.0 + 0.06 * math.cos(2 * a + ph)
    zs = _bands(zd + 0.25, c, step)
    prof = [(r * 1.55, z0), (r * 1.25, zd + 0.03)] + [(r * (1.06 - 0.08 * (z - zs[0]) / max(0.1, c - zs[0])), z)
                                                       for z in zs]
    rings = lathe(b.bm(m["body"]), x, y, prof, seg=seg, wobble=oval, bottom=False, top=False)
    rings += _tip(b, m, x, y, c, top, r * 0.98, seg, oval)
    _bend(rings, x, y, zd, r, lean, crook, None, 1.0, px_, py_)


def place(M, zd, col, row, r, L=(1.2, 1.5), ymax=0.85, zmax=99.0):
    """Where a finger stands so its cap lands on the sprite's fingertip at
    (col, row) in the classic render: (x, y, dome z, tip z). It stands as far
    back on the crown as it must for a height in L. The cap's top is about
    9r px over its apex in the classic projection."""
    x = M.x_of(col)
    t = max(-0.9, min(0.9, (x - M.cx) / M.rx))
    yb = M.cy + ymax * M.ry * math.sqrt(1 - t * t)
    yf = M.cy - 0.2 * M.ry
    want = (M.hy - row) / K - 9 * r

    def need(y):  # the height over the dome that puts the cap on the fingertip from y
        return (want - 16 * y) / 8 - zd(x, y)
    L0 = sum(L) / 2
    lo, hi = yf, yb
    if need(yb) > L0:  # even from the back it must stand tall
        y = yb
    elif need(yf) < L0:
        y = yf
    else:
        for _ in range(40):
            mid = (lo + hi) / 2
            lo, hi = (mid, hi) if need(mid) > L0 else (lo, mid)
        y = (lo + hi) / 2
    h = max(0.8, min(max(L[0], min(L[1], need(y))), zmax - zd(x, y)))
    return x, y, zd(x, y), zd(x, y) + h


def rib(b, m, M, a, zwall, tip, r, rng, **kw):
    """A wall chimney at angle a: its foot on the battered foot ellipse,
    leaning in with the wall so it meets the shoulder at zwall."""
    x0, y0 = M.at(a, 1.0, 0.0)
    x1, y1 = M.at(a, 1.0, 1.0)
    lean = ((x1 - x0) / max(0.5, zwall), (y1 - y0) / max(0.5, zwall))
    return column(b, m, x0, y0, 0.0, tip, r, rng, lean=lean, **kw)


def outer_ring(M, n, rng, a0=None, jitter=0.06):
    a0 = rng.uniform(0, 6.28) if a0 is None else a0
    return [a0 + 2 * math.pi * i / n + rng.uniform(-jitter, jitter) for i in range(n)]


def fan(b, M, m, a0, a1, h0, rng, emax=0.7, emin=0.2, na=14, clods=0, inset=0.12, lump=0.05, over=0.3):
    """The spoil spilt over the ground at the foot between angles a0 and a1
    (radians, -pi/2 is the front): a lumpy wedge from h0 against the wall to
    the ground, its edge pushed out along the wall's normal until it meets
    the sprite's own bottom outline in the classic render."""
    bot = M.S["bot"]
    bm = b.bm(m["sand"])
    ph = [rng.uniform(0, 6.28) for _ in range(4)]
    grid = []
    for i in range(na + 1):
        u = i / na
        a = a0 + (a1 - a0) * u
        wx, wy = M.at(a, 1.0, 0.0)
        nx, ny = math.cos(a) / M.rx, math.sin(a) / M.ry
        ln = math.hypot(nx, ny)
        nx, ny = nx / ln, ny / ln
        e = emax
        for k in range(1, 61):
            ee = emax * k / 60
            px, py = wx + nx * ee, wy + ny * ee
            c = int(round(M.S["hx"] + 16 * px))  # where the render draws it
            if not 1 <= c < M.w - 1 or bot[c] is None:
                e = max(0.0, ee - emax / 30)
                break
            if M.render_row(py, 0.0) >= M.hy - bot[c] + over:
                e = ee
                break
        taper = math.sin(math.pi * u) ** 0.35
        e = max(emin * taper, e)
        h = h0 * (0.4 + 0.6 * taper) * (1 + lump * math.sin(3 * a + ph[0]))
        row = []
        for v, zf in ((-inset, 1.0), (0.3, 0.72), (0.65, 0.36), (1.0, 0.0)):
            d = v * e if v > 0 else v
            jit = lump * math.sin(7 * a + 5 * v + ph[1]) * (1 if 0 < v < 1 else 0)
            row.append(bm.verts.new((wx + nx * d, wy + ny * d, max(0.0, h * zf + jit))))
        grid.append(row)
    for r0, r1 in zip(grid, grid[1:]):
        for j in range(len(r0) - 1):
            bm.faces.new((r0[j], r0[j + 1], r1[j + 1], r1[j]))
    for i in range(clods):
        u = rng.uniform(0.15, 0.85)
        a = a0 + (a1 - a0) * u
        wx, wy = M.at(a, 1.0)
        d = rng.uniform(0.15, 0.4)
        mk = m["sand2"] if i % 2 else m["sand"]
        rock(b.bm(mk), (wx + math.cos(a) * d, wy + math.sin(a) * d, 0.0),
             (rng.uniform(0.12, 0.2),) * 2 + (rng.uniform(0.08, 0.13),), rng, sub=1, rough=0.3)


def cull(b, m, M, walltop, keep=0.955):
    """Drops rib faces buried in the core, where nothing can see them."""
    for key in ("body", "low"):
        bm = b.parts.get(m[key])
        if bm is None:
            continue
        dead = [f for f in bm.faces if all(M.rho(v.co.x, v.co.y) < keep * (1 + M.taper * max(0.0, 1 - v.co.z / max(0.5, walltop(v.co.x, v.co.y))))
                                           and v.co.z < walltop(v.co.x, v.co.y) - 0.04
                                           for v in f.verts)]
        bmesh.ops.delete(bm, geom=dead, context="FACES")


def lit_left(b, objs, keys=("mound_body", "mound_low", "mound_core", "mound_neck"), k=2.4, nx=-0.35):
    """The sprite is lit from the left: faces turned that way take a lighter
    tone of their material (plain faces only, the grit stays), so the left
    flank reads as the picture paints it rather than as a black band."""
    for ob in objs:
        if ob.name not in keys or ob.type != "MESH":
            continue
        rgb = [min(255, int(round(v * k))) for v in b.rgb[ob.name]]
        ob.data.materials.append(hk.pbr(ob.name + "_lit", kit.lin(rgb), 0.85, 0.0))
        i = len(ob.data.materials) - 1
        for f in ob.data.polygons:
            if f.material_index == 0 and f.normal.x < nx and f.normal.z < 0.6:
                f.material_index = i


def run(name, build):
    """Builds, exports and renders one mound. build(b) fills a kit.Build."""
    S = SPR[name]
    hk.reset()
    b = kit.Build()
    build(b)
    objs = b.objects(SMOOTH)
    lit_left(b, objs)
    tris = kit.tri_count(objs)
    print("ZS_PARTS", name, " ".join("%s:%d" % (o.name, kit.tri_count([o])) for o in objs))
    ob = hk.finish(objs, os.path.join(OUT, "models", name + ".glb"),
                   {"replacesFeature": name, "handFamily": "zhon_structures", "bespoke": True})
    hx, hy = S["hx"], S["hy"]
    rows = [hy - K * (16 * v.co.y + 8 * v.co.z) for v in ob.data.vertices]
    cols = [hx + 16 * v.co.x for v in ob.data.vertices]
    clip = min(rows) < -0.5 or max(rows) > S["h"] - 0.5 or min(cols) < -0.5 or max(cols) > S["w"] - 0.5
    print("ZS_CLASSIC", name, "render rows %.0f..%.0f cols %.0f..%.0f of sprite %dx%d%s" % (
        min(rows), max(rows), min(cols), max(cols), S["w"], S["h"], "  CLIPPED" if clip else ""))
    dims = ob.dimensions
    print("ZS_SIZE", name, "%.2f wide %.2f deep %.2f tall" % tuple(dims))
    if not os.environ.get("ZS_NORENDER"):
        hk.renders(ob, os.path.join(OUT, "renders"), name, "D:/OKReplace/sprites/%s.png" % name, (hx, hy), scale=2)
    print("ZS_BUILT", name, "tris", tris)
    return tris
