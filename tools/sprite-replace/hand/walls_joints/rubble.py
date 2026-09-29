"""Rubble for ruins: a palette of the drawing's debris tones, a mound kept
under the drawing's outline, and loose blocks lying on it.

Every face or block takes the palette tone nearest the colour the
drawing shows where it lands in the classic view, so a pale heap stays
pale and a dark gap stays dark, with plain materials only.
"""
import math

import numpy as np
from mathutils import Vector


def palette(m, k=5, prefix="rub", rect=None, gain=0.9, floor=22, warm=(1.0, 1.0, 1.0), cap=256, pick=None):
    """k tones by k-means over the drawn pixels brighter than floor and
    darker than cap (pick(r, g, b) may leave out more)."""
    px = m.pix
    x0, y0, x1, y1 = rect or (0, 0, m.w, m.h)
    a = px[y0:y1, x0:x1].reshape(-1, 4)
    a = a[a[:, 3] > 0.5][:, :3] * 255.0
    lum = a @ np.array([0.3, 0.59, 0.11])
    a = a[(lum > floor) & (lum < cap)]
    if pick is not None:
        a = a[np.array([bool(pick(*c)) for c in a])]
    rng = np.random.default_rng(len(m.name))
    cen = a[rng.choice(len(a), k, replace=False)].astype(float)
    for _ in range(12):
        d = ((a[:, None, :] - cen[None]) ** 2).sum(2)
        lab = d.argmin(1)
        for i in range(k):
            if (lab == i).any():
                cen[i] = a[lab == i].mean(0)
    order = np.argsort(cen @ np.array([0.3, 0.59, 0.11]))
    cen = cen[order]
    keys = []
    for i, c in enumerate(cen):
        key = "%s%d" % (prefix, i)
        m.col(key, tuple(min(255.0, float(v) * gain * w) for v, w in zip(c, warm)), 0.95)
        keys.append(key)
    return Tones(m, cen, keys)


class Tones:
    def __init__(self, m, cen, keys):
        self.m, self.cen, self.keys = m, cen, keys
        self.median = False

    def at(self, x, y, z, rad=1):
        """The tone nearest the drawing's colour where (x, y, z) is drawn."""
        c, r = self.m.scr(x, y, z)
        ci, ri = int(c), int(r)
        px = self.m.pix
        q = px[max(0, ri - rad):ri + rad + 1, max(0, ci - rad):ci + rad + 1].reshape(-1, 4)
        q = q[q[:, 3] > 0.5][:, :3] * 255.0
        if not len(q):
            return self.keys[len(self.keys) // 2]
        col = np.median(q, 0) if self.median else q.mean(0)
        return self.keys[int(((self.cen - col[None]) ** 2).sum(1).argmin())]


def drawn(m, x, y, z):
    c, r = m.scr(x, y, z)
    ci, ri = int(math.floor(c)), int(math.floor(r))
    return 0 <= ci < m.w and 0 <= ri < m.h and m.pix[ri, ci, 3] > 0.5


def top_allowed(m, x, y, zmax, step=0.1):
    """The highest z up to zmax whose point is drawn, climbing from the ground."""
    z = 0.0
    if not drawn(m, x, y, 0.0):
        return -1.0
    while z + step <= zmax and drawn(m, x, y, z + step):
        z += step
    return z


def mound(m, tones, box, hfn, cell=0.3, seed=0, noise=0.18):
    """A heap over box = (x0, x1, y0, y1): hfn(x, y) is how high it may rise
    there; it is kept to the drawing (never drawn where the picture is
    empty) and each face takes the drawing's tone."""
    rng = np.random.default_rng(seed + len(m.name))
    x0, x1, y0, y1 = box
    nx, ny = max(2, int((x1 - x0) / cell) + 1), max(2, int((y1 - y0) / cell) + 1)
    Z = np.zeros((ny, nx))
    for j in range(ny):
        for i in range(nx):
            x, y = x0 + (x1 - x0) * i / (nx - 1), y0 + (y1 - y0) * j / (ny - 1)
            h = hfn(x, y)
            if h <= 0:
                Z[j, i] = -1
                continue
            h *= 1.0 + rng.uniform(-noise, noise)
            top = top_allowed(m, x, y, h)
            if top < 0:
                # a speck of ground showing through the drawing: take the
                # highest drawn point over it rather than open a hole
                z = h
                while z > 0 and not drawn(m, x, y, z):
                    z -= 0.1
                top = z if z > 0.2 else -1.0
            Z[j, i] = min(h, top)
    # the heap comes down to the ground at its rim, never ends in the air
    rim = Z.copy()
    for j in range(ny):
        for i in range(nx):
            if Z[j, i] < 0:
                nb = [Z[jj, ii] for jj in (j - 1, j, j + 1) for ii in (i - 1, i, i + 1)
                      if 0 <= jj < ny and 0 <= ii < nx]
                if any(v > 0 for v in nb):
                    rim[j, i] = 0.0
            elif j in (0, ny - 1) or i in (0, nx - 1):
                rim[j, i] = 0.0
    Z = rim
    # soften the heap one pass, keep the holes
    Zs = Z.copy()
    for j in range(1, ny - 1):
        for i in range(1, nx - 1):
            if Z[j, i] > 0:
                nb = [Z[j + dj, i + di] for dj in (-1, 0, 1) for di in (-1, 0, 1) if Z[j + dj, i + di] >= 0]
                Zs[j, i] = 0.5 * Z[j, i] + 0.5 * sum(nb) / len(nb)
    Z = Zs
    faces = {}
    for j in range(ny - 1):
        for i in range(nx - 1):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if any(Z[b, a] < 0 for a, b in q):
                continue
            if all(Z[b, a] < 0.02 for a, b in q):
                continue
            pts = [(x0 + (x1 - x0) * a / (nx - 1), y0 + (y1 - y0) * b / (ny - 1), max(0.0, Z[b, a])) for a, b in q]
            cx = sum(p[0] for p in pts) / 4
            cy = sum(p[1] for p in pts) / 4
            cz = sum(p[2] for p in pts) / 4
            key = tones.at(cx, cy, cz)
            faces.setdefault(key, []).append(pts)
    for key, fl in faces.items():
        m.faces(key, fl)
    return Z, (x0, x1, y0, y1, nx, ny)


def height_at(Zinfo, x, y):
    """The heap's surface over (x, y), as its faces make it."""
    Z, (x0, x1, y0, y1, nx, ny) = Zinfo
    i = (x - x0) / (x1 - x0) * (nx - 1)
    j = (y - y0) / (y1 - y0) * (ny - 1)
    if not (0 <= i <= nx - 1 and 0 <= j <= ny - 1):
        return 0.0
    i0, j0 = min(int(i), nx - 2), min(int(j), ny - 2)
    fi, fj = i - i0, j - j0
    q = [max(0.0, float(Z[j0 + b, i0 + a])) for b in (0, 1) for a in (0, 1)]
    if any(Z[j0 + b, i0 + a] < 0 for b in (0, 1) for a in (0, 1)):
        return 0.0
    return (q[0] * (1 - fi) + q[1] * fi) * (1 - fj) + (q[2] * (1 - fi) + q[3] * fi) * fj


def blocks(m, tones, box, n, size=(0.2, 0.55), zfn=None, near=None, seed=1, tries=40, round_=False, loose_edge=False):
    """n loose blocks inside box, each where the drawing shows something,
    resting on zfn(x, y) (the mound) and toned from the drawing."""
    rng = np.random.default_rng(seed + 7 * len(m.name))
    x0, x1, y0, y1 = box
    placed = 0
    for _ in range(n * tries):
        if placed >= n:
            break
        x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
        if near is not None and not near(x, y):
            continue
        z = zfn(x, y) if zfn else 0.0
        s = rng.uniform(*size)
        if not (near_drawn(m, x, y, z + s * 0.4, px=2) if loose_edge else drawn(m, x, y, z + s * 0.6)):
            continue
        key = tones.at(x, y, z + s * 0.5)
        if round_:
            m.boulder(key, x, y, s * rng.uniform(0.9, 1.3), s * rng.uniform(0.8, 1.2), s * rng.uniform(0.6, 0.95),
                      z=max(0.0, z - s * 0.25), yaw=rng.uniform(0, math.pi))
        else:
            m.rock(key, x, y, s * rng.uniform(0.8, 1.4), s * rng.uniform(0.7, 1.2), s * rng.uniform(0.5, 0.9),
                   yaw=rng.uniform(0, math.pi), z=max(0.0, z - s * 0.2), jag=0.3,
                   pitch=rng.uniform(-0.3, 0.3), roll=rng.uniform(-0.3, 0.3))
        placed += 1
    return placed


def line_dist(pts, x, y):
    """Distance in plan from (x, y) to a polyline."""
    best = 1e9
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        d = b - a
        L2 = d.length_squared or 1e-9
        u = max(0.0, min(1.0, (Vector((x, y)) - a).dot(d) / L2))
        best = min(best, (a + d * u - Vector((x, y))).length)
    return best


def standing(m, pts, t0, t1, zmax, step=0.3, off=(0.0, 0.0), keep=0.85, floor=0.3):
    """How high a broken wall stands along pts from t0 to t1, read off the
    drawing: at each station the highest point over the centreline (moved
    by off in plan) that the picture still draws, times keep. Returns a
    function of t for the builders."""
    import kit
    ts, hs = [], []
    t = t0
    while t <= t1 + 1e-6:
        pos, d, _ = kit.point_at(pts, t)
        x, y = pos.x + off[0], pos.y + off[1]
        h = top_allowed(m, x, y, zmax, step=0.1)
        ts.append(t)
        hs.append(max(0.0, h * keep) if h > floor else 0.0)
        t += step

    def f(t):
        if t <= ts[0]:
            return hs[0]
        for i in range(len(ts) - 1):
            if ts[i] <= t <= ts[i + 1]:
                u = (t - ts[i]) / (ts[i + 1] - ts[i])
                return hs[i] * (1 - u) + hs[i + 1] * u
        return hs[-1]
    return f


class Heap:
    """A smooth heap built as rings round a centre (heap_mesh): its
    surface height over any plan point, 0 outside it."""

    def __init__(self, cx, cy, rfn, H, seg):
        self.cx, self.cy, self.rfn, self.H, self.seg = cx, cy, rfn, H, seg
        self.K = len(H) - 1

    def __call__(self, x, y):
        dx, dy = x - self.cx, y - self.cy
        a = math.atan2(dy, dx) % (2 * math.pi)
        R = self.rfn(a)
        s = math.hypot(dx, dy) / max(1e-6, R)
        if s >= 1.0:
            return 0.0
        fk = s * self.K
        fj = a / (2 * math.pi) * self.seg
        k0, j0 = min(int(fk), self.K - 1), int(fj) % self.seg
        u, v = fk - k0, fj - int(fj)
        j1 = (j0 + 1) % self.seg
        h = self.H
        return ((h[k0][j0] * (1 - v) + h[k0][j1] * v) * (1 - u) + (h[k0 + 1][j0] * (1 - v) + h[k0 + 1][j1] * v) * u)


def heap_mesh(m, tones, cx, cy, rfn, hfn, rings=12, seg=56, skirt=0.3, key=None, clip=True, passes=2, rad=2,
              bumps=0.0, seed=0):
    """A heap with no grid: rings of points from the centre out to the
    outline rfn(bearing in radians), each as high as hfn(x, y), sloping to
    the ground over the outer skirt (a share of the radius), kept under
    the drawing and smoothed. bumps roughens the surface after that.
    Faces take key, or the drawing's tone. Returns a Heap for resting
    things on it."""
    rng = np.random.default_rng(seed + len(m.name))
    K = rings
    H = [[0.0] * seg for _ in range(K + 1)]
    P = [[None] * seg for _ in range(K + 1)]
    for k in range(K + 1):
        s = k / K
        for j in range(seg):
            a = 2 * math.pi * j / seg
            R = rfn(a)
            x, y = cx + R * s * math.cos(a), cy + R * s * math.sin(a)
            P[k][j] = (x, y)
            if k == K:
                continue
            t = min(1.0, (1.0 - s) / skirt) if skirt > 0 else 1.0
            h = max(0.0, hfn(x, y)) * (3 * t * t - 2 * t * t * t)
            if clip and h > 0:
                top = top_allowed(m, x, y, h + 0.05)
                if top < 0:
                    top = 0.0
                h = min(h, top)
            H[k][j] = h
    for _ in range(passes):
        G = [row[:] for row in H]
        for k in range(K):
            for j in range(seg):
                nb = [H[k][(j - 1) % seg], H[k][(j + 1) % seg], H[k + 1][j]]
                nb.append(H[k - 1][j] if k > 0 else H[k][j])
                G[k][j] = 0.5 * H[k][j] + 0.125 * sum(nb)
        H = G
    if bumps > 0:
        for k in range(1, K):
            for j in range(seg):
                if H[k][j] > 0.05:
                    H[k][j] = max(0.0, H[k][j] + rng.uniform(-bumps, bumps) * min(1.0, H[k][j]))
    faces = {}
    for k in range(K):
        for j in range(seg):
            jj = (j + 1) % seg
            q = [(P[k][j][0], P[k][j][1], H[k][j]), (P[k + 1][j][0], P[k + 1][j][1], H[k + 1][j]),
                 (P[k + 1][jj][0], P[k + 1][jj][1], H[k + 1][jj]), (P[k][jj][0], P[k][jj][1], H[k][jj])]
            if k == 0:
                q = [(cx, cy, sum(H[0]) / seg)] + q[1:3]
            if all(v[2] < 0.01 for v in q):
                continue
            if key is not None:
                kk = key
            else:
                mx = sum(v[0] for v in q) / len(q)
                my = sum(v[1] for v in q) / len(q)
                mz = sum(v[2] for v in q) / len(q)
                kk = tones.at(mx, my, mz, rad=rad)
            faces.setdefault(kk, []).append(q)
    for kk, fl in faces.items():
        m.faces(kk, fl)
    # the centre point is shared by the fan
    H[0] = [sum(H[0]) / seg] * seg
    return Heap(cx, cy, rfn, H, seg)


def radial(rs):
    """A function of the bearing (radians) through radii sampled evenly round."""
    seg = len(rs)

    def f(a):
        u = (a % (2 * math.pi)) / (2 * math.pi) * seg
        j0 = int(u) % seg
        v = u - int(u)
        return rs[j0] * (1 - v) + rs[(j0 + 1) % seg] * v
    return f


def outline(m, cx, cy, rmax, seg=72, step=0.05, north=None, smooth=3, margin=0.0, jag=0.0, seed=0):
    """The footprint's radius at each bearing: how far out from (cx, cy)
    the ground is still drawn, but no further than rmax(a), nor than
    north(a) on the far side where the heap hides the ground. jag roughens
    it a little before it is smoothed. Returned as a function of the
    bearing in radians."""
    rng = np.random.default_rng(seed)
    rs = []
    for j in range(seg):
        a = 2 * math.pi * j / seg
        r = 0.2
        while r < rmax(a) and near_drawn(m, cx + (r + step) * math.cos(a), cy + (r + step) * math.sin(a), 0.0):
            r += step
        if north is not None and math.sin(a) > 0.2:
            r = min(r, north(a))
        rs.append(max(0.3, r - margin) * (1.0 + rng.uniform(-jag, jag)))
    for _ in range(smooth):
        rs = [(rs[(j - 1) % seg] + 2 * rs[j] + rs[(j + 1) % seg]) / 4 for j in range(seg)]
    return radial(rs)


def near_drawn(m, x, y, z, px=1):
    """drawn(), forgiving the specks of ground a rubbly edge lets through."""
    c, r = m.scr(x, y, z)
    ci, ri = int(math.floor(c)), int(math.floor(r))
    if not (0 <= ci < m.w and 0 <= ri < m.h):
        return False
    q = m.pix[max(0, ri - px):ri + px + 1, max(0, ci - px):ci + px + 1, 3]
    return bool((q > 0.5).any())


def shape(cx, cy, inside, rmax=8.0, seg=72, step=0.04):
    """The radius of a plan shape round (cx, cy) at each bearing, where
    inside(x, y) stops holding, as a function of the bearing."""
    rs = []
    for j in range(seg):
        a = 2 * math.pi * j / seg
        r = 0.0
        while r < rmax and inside(cx + (r + step) * math.cos(a), cy + (r + step) * math.sin(a)):
            r += step
        rs.append(max(0.2, r))
    return radial(rs)
