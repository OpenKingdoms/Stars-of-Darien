"""Reading a tree or plant sprite: its mask, colours, skeleton and crown.

Pure numpy, so it runs both in Blender (sprites loaded through bpy) and in
the system Python (through Pillow) for debug overlays. Rows run top-down as
in the picture, and colours stay in the sprite's own sRGB, 0 to 1.
"""
import json
import math
import os

import numpy as np

SPRITES = "D:/OKReplace/sprites"
CATALOG = "D:/OKReplace/catalog.json"
CELL = 16.0
TILT = 0.5

_catalog = None


def record(name):
    global _catalog
    if _catalog is None:
        _catalog = {r["name"]: r for r in json.load(open(CATALOG))}
    return _catalog[name]


def load_rgba(path):
    """(h, w, 4) floats, rows top-down, sRGB as stored."""
    try:
        from PIL import Image
        return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255.0
    except ImportError:
        import bpy
        img = bpy.data.images.load(path, check_existing=True)
        w, h = img.size
        a = np.empty(w * h * 4, np.float32)
        img.pixels.foreach_get(a)
        return a.reshape(h, w, 4)[::-1].copy()


class Sprite:
    def __init__(self, name):
        self.name = name
        self.rec = record(name)
        self.rgba = load_rgba(os.path.join(SPRITES, name + ".png"))
        self.h, self.w = self.rgba.shape[:2]
        self.hx, self.hy = self.rec["sprite"]["hotspot"]
        self.mask = self.rgba[..., 3] > 0.5
        self.rgb = self.rgba[..., :3]
        self.lum = self.rgb @ np.array([0.299, 0.587, 0.114], np.float32)
        ys, xs = np.nonzero(self.mask)
        self.top, self.bottom = int(ys.min()), int(ys.max())
        self.left, self.right = int(xs.min()), int(xs.max())

    # the classic projection and its inverse at a chosen depth
    def screen(self, x, y, z):
        return self.hx + x * CELL, self.hy - y * CELL - z * CELL * TILT

    def at_depth(self, col, row, y):
        """The point drawn at (col, row) that lies y cells north: (x, y, z)."""
        return (col - self.hx) / CELL, y, (self.hy - row - y * CELL) / (CELL * TILT)

    def inside(self, col, row, mask=None):
        m = self.mask if mask is None else mask
        c, r = int(math.floor(col)), int(math.floor(row))
        return 0 <= c < self.w and 0 <= r < self.h and bool(m[r, c])

    def colour(self, sel=None, lo=0.0, hi=1.0):
        """Mean colour of the pixels in sel whose brightness lies between the
        lo and hi quantiles: (0, 0.3) for the shadowed, (0.7, 1) for the lit."""
        sel = self.mask if sel is None else (sel & self.mask)
        if not sel.any():
            sel = self.mask
        lum = self.lum[sel]
        a, b = np.quantile(lum, lo), np.quantile(lum, hi)
        pick = (lum >= a) & (lum <= b)
        return tuple(float(v) for v in self.rgb[sel][pick].mean(0))

    def hue_mask(self, test):
        """Pixels whose colour passes test(r, g, b) on arrays."""
        r, g, b = self.rgb[..., 0], self.rgb[..., 1], self.rgb[..., 2]
        return self.mask & test(r, g, b)

    def row_span(self, row, mask=None):
        m = self.mask if mask is None else mask
        r = int(round(row))
        if not 0 <= r < self.h or not m[r].any():
            return None
        xs = np.nonzero(m[r])[0]
        return int(xs.min()), int(xs.max())


def to_linear(c):
    c = np.asarray(c, np.float64)
    return tuple(float(v) for v in np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4))


def edt(mask):
    """Euclidean distance from each drawn pixel to the nearest clear one
    (outside the picture counts as clear), 0 on clear pixels."""
    h, w = mask.shape
    pad = np.pad(mask, 1)
    by, bx = np.nonzero(~pad & _grow(pad))
    fy, fx = np.nonzero(mask)
    out = np.zeros((h, w), np.float32)
    if len(fy) == 0:
        return out
    B = np.stack([by - 1, bx - 1], 1).astype(np.float32)
    F = np.stack([fy, fx], 1).astype(np.float32)
    best = np.full(len(F), 1e9, np.float32)
    for i in range(0, len(B), 512):
        d = ((F[:, None, :] - B[None, i:i + 512, :]) ** 2).sum(-1)
        best = np.minimum(best, d.min(1))
    out[fy, fx] = np.sqrt(best)
    return out


def close(mask, n=1):
    """Fills gaps and pinholes n pixels across."""
    m = mask.copy()
    for _ in range(n):
        m = _grow(m)
    for _ in range(n):
        m = ~_grow(~m)
    return m | mask


def _grow(m):
    p = np.pad(m, 1)
    g = p.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            g |= np.roll(np.roll(p, dy, 0), dx, 1)
    return g[1:-1, 1:-1]


def erode(m, n=1):
    for _ in range(n):
        m = ~_grow(~m)
    return m


def thin(mask):
    """Zhang-Suen thinning to a one pixel skeleton."""
    img = np.pad(mask.astype(np.uint8), 1)
    while True:
        changed = False
        for step in (0, 1):
            P2 = np.roll(img, 1, 0)
            P3 = np.roll(np.roll(img, 1, 0), -1, 1)
            P4 = np.roll(img, -1, 1)
            P5 = np.roll(np.roll(img, -1, 0), -1, 1)
            P6 = np.roll(img, -1, 0)
            P7 = np.roll(np.roll(img, -1, 0), 1, 1)
            P8 = np.roll(img, 1, 1)
            P9 = np.roll(np.roll(img, 1, 0), 1, 1)
            ring = [P2, P3, P4, P5, P6, P7, P8, P9, P2]
            B = sum(ring[:8])
            A = sum(((ring[i] == 0) & (ring[i + 1] == 1)).astype(np.uint8) for i in range(8))
            if step == 0:
                c = (P2 * P4 * P6 == 0) & (P4 * P6 * P8 == 0)
            else:
                c = (P2 * P4 * P8 == 0) & (P2 * P6 * P8 == 0)
            rm = (img == 1) & (B >= 2) & (B <= 6) & (A == 1) & c
            if rm.any():
                img[rm] = 0
                changed = True
        if not changed:
            break
    return img[1:-1, 1:-1].astype(bool)


NB8 = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]


def skeleton_tree(mask, root, dt=None, prune=(3.0, 1.2), bridge=10.0, min_part=3):
    """The skeleton as a tree grown from root (col, row): a list of branches,
    each {"px": [(col, row), ...], "r": [px radius...], "parent": i, "at": k},
    the first the longest path from the root. Spurs shorter than
    max(prune[0], prune[1] * radius) are cut, and skeleton pieces the drawing
    left unjoined are bridged to the nearest part when within bridge pixels."""
    import heapq
    if dt is None:
        dt = edt(mask)
    sk = thin(mask)
    pts = set(zip(*[a.tolist() for a in np.nonzero(sk)]))  # (row, col)
    if not pts:
        return []
    rc = (root[1], root[0])
    start = min(pts, key=lambda p: (p[0] - rc[0]) ** 2 + (p[1] - rc[1]) ** 2)
    parent, dist = {start: None}, {start: 0.0}
    done = set()

    def grow(seed_list):
        heap = [(dist[s], s) for s in seed_list]
        heapq.heapify(heap)
        while heap:
            d, p = heapq.heappop(heap)
            if p in done:
                continue
            done.add(p)
            for dy, dx in NB8:
                q = (p[0] + dy, p[1] + dx)
                if q in pts and q not in done:
                    nd = d + (1.4142 if dy and dx else 1.0)
                    if nd < dist.get(q, 1e18):
                        dist[q], parent[q] = nd, p
                        heapq.heappush(heap, (nd, q))

    grow([start])
    # bridge the unjoined pieces, nearest first
    while True:
        rest = pts - done
        if not rest:
            break
        comps, seen = [], set()
        for p in rest:
            if p in seen:
                continue
            comp, stack = [], [p]
            seen.add(p)
            while stack:
                a = stack.pop()
                comp.append(a)
                for dy, dx in NB8:
                    q = (a[0] + dy, a[1] + dx)
                    if q in rest and q not in seen:
                        seen.add(q)
                        stack.append(q)
            comps.append(comp)
        D = np.array(sorted(done), np.float32)
        best = None
        for comp in comps:
            C = np.array(comp, np.float32)
            d = ((C[:, None, :] - D[None, :, :]) ** 2).sum(-1)
            i, j = np.unravel_index(np.argmin(d), d.shape)
            g = math.sqrt(float(d[i, j]))
            if best is None or g < best[0]:
                best = (g, comp, tuple(int(v) for v in C[i]), tuple(int(v) for v in D[j]))
        g, comp, a, b = best
        if g > bridge or len(comp) < min_part:
            pts -= set(comp)
            continue
        parent[a], dist[a] = b, dist[b] + g
        grow([a])
    # children and subtree lengths
    kids = {p: [] for p in done}
    for p in done:
        if parent[p] is not None:
            kids[parent[p]].append(p)
    order = sorted(done, key=lambda p: dist[p])

    def seglen(a, b):
        return math.hypot(a[0] - b[0], a[1] - b[1])
    # prune spurs: repeatedly cut leaf chains shorter than the local radius allows
    alive = set(done)
    for _ in range(12):
        height = {}
        for p in reversed(order):
            if p not in alive:
                continue
            ks = [k for k in kids[p] if k in alive]
            height[p] = max((height[k] + seglen(p, k) for k in ks), default=0.0)
        cut = False
        for p in order:
            if p not in alive:
                continue
            ks = [k for k in kids[p] if k in alive]
            if len(ks) < 2 and not (p == start and ks):
                continue
            lim = max(prune[0], prune[1] * float(dt[p[0], p[1]]))
            main = max(ks, key=lambda k: height[k] + seglen(p, k))
            for k in ks:
                if k is not main and height[k] + seglen(p, k) < lim:
                    stack = [k]
                    while stack:
                        a = stack.pop()
                        alive.discard(a)
                        stack.extend(kids[a])
                    cut = True
        if not cut:
            break
    height = {}
    for p in reversed(order):
        if p in alive:
            ks = [k for k in kids[p] if k in alive]
            height[p] = max((height[k] + seglen(p, k) for k in ks), default=0.0)
    # decompose into branches along the longest continuation
    branches = []
    todo = [(start, None, 0)]
    while todo:
        p, par, at = todo.pop(0)
        chain = [p] if par is None else [parent[p], p]
        while True:
            ks = [k for k in kids[chain[-1]] if k in alive]
            if not ks:
                break
            main = max(ks, key=lambda k: height[k] + seglen(chain[-1], k))
            idx = len(branches)
            for k in ks:
                if k is not main:
                    todo.append((k, idx, len(chain) - 1))
            chain.append(main)
        if len(chain) < 2:
            continue
        branches.append({"px": [(c, r) for r, c in chain], "r": [float(dt[r, c]) for r, c in chain],
                         "parent": par, "at": at})
    return branches


def smooth_path(px, win=2):
    a = np.array(px, np.float64)
    if len(a) < 3:
        return a
    out = a.copy()
    for i in range(1, len(a) - 1):
        lo, hi = max(0, i - win), min(len(a), i + win + 1)
        out[i] = a[lo:hi].mean(0)
    return out


def resample(a, step):
    """Points every step pixels along the polyline, ends kept."""
    seg = np.hypot(*(a[1:] - a[:-1]).T)
    s = np.concatenate([[0.0], np.cumsum(seg)])
    L = s[-1]
    if L < 1e-6:
        return a[:1], np.array([0.0])
    n = max(1, int(round(L / step)))
    t = np.linspace(0.0, L, n + 1)
    return np.stack([np.interp(t, s, a[:, 0]), np.interp(t, s, a[:, 1])], 1), t


def pack_circles(mask, dt=None, min_r=3.0, cover=0.985, max_n=80):
    """Circles (col, row, r px) covering mask, biggest first: each new one
    the widest spot not yet covered."""
    if dt is None:
        dt = edt(mask)
    h, w = mask.shape
    yy, xx = np.mgrid[0:h, 0:w]
    covered = np.zeros_like(mask)
    total = mask.sum()
    out = []
    while len(out) < max_n:
        cand = np.where(mask & ~covered, dt, 0)
        i = int(np.argmax(cand))
        r0 = float(cand.flat[i])
        if r0 < min_r:
            break
        row, col = divmod(i, w)
        out.append((float(col), float(row), r0))
        covered |= (xx - col) ** 2 + (yy - row) ** 2 <= (r0 + 0.5) ** 2
        if covered[mask].sum() >= cover * total:
            break
    return out
