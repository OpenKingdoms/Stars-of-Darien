"""Taros fortress towers and their ruins, and the ruins of Taros walls.

The tower rises in tiers over an oval plan centred on (cx, cy): a battered
outer wall of lit blocks in courses over dark joints, crowned with pointed
merlons, a cell deeper to the north than the tiers inside it (north), and
a gatehouse in the same courses with an arched door, a pale lintel course
and crenels on its south face; a broad ring of dark crimson tile strips
climbing gently from the outer wall to an inner ring wall crowned with
cones, rows of black stone cones across its front; and a keep faced with
the lightest stone, a crenellated parapet across its front and a dark red
pyramid set back behind it. Steel spikes stick out from the outer wall and
the tile ring. Built sturdy: the keep and pyramid wider and much lower
than a pixel-exact fit.

The tower ruins are a heap of black boulders over the tower's plan, its
edge following the drawing, with stubs of the outer wall round the left
and back still carrying curved strips of the tile ring and a few spikes,
and the gatehouse stump with its arch at the front. The wall ruins are
boulder heaps where the wall still stands, an open breach between them
where logs, spikes and a few red flecks lie, and (TarWall01a) a
flat-faced stump of the wall at its south end. TarWall02a builds each
stack from big round stones, 0.6 to 0.9 cells, laid over a core sunk well
inside them: a ring of half-sunk stones on the drawing's outline, a row
along the crest, the rest dropped lowest first, and one or two stones
crowning the column where the drawing has them. Each stone has a lit top
and a dark foot. Low beds of boulders lie in the breach under its logs.
"""
import math

import kit
import rubble

TOWER = dict(kind="tower", cx=0.1, cy=0.35, ax=3.55, ay=2.95, north=1.0, H1=2.6, t1=0.55, ax2=1.95, ay2=1.5,
             H2=4.25, kx0=-1.275, kx1=1.525, ky0=-0.325, ky1=1.675, H3=6.3, H4=9.3)
# stubs: (bearing from, to, height) of the outer wall still standing, degrees
# anticlockwise from east; tiles: (bearing from, to) of the tile strips they
# carry; spikes: (bearing, length, height, droop, turn) out of the stubs,
# turn degrees off the radial. Things lying on the heap are placed where the
# drawing shows them, by sprite pixel (col, row): tile_bits and shards_at
# for pieces of tile, inner_spikes as (root, tip).
RUIN = dict(kind="towerruin", heap=1.9, n=114, stubs=[(52, 128, 1.9), (150, 248, 1.9)],
            tiles=[(58, 116), (168, 214)], tile_w=1.1,
            spikes=[(215, 1.0, 1.1, 0.5, -10), (226, 0.9, 1.0, 0.45, -4),
                    (78, 0.9, 1.5, 0.25, -70), (104, 0.9, 1.5, 0.25, -70), (176, 1.0, 1.4, 0.1, 0)],
            inner_spikes=[((32, 56), (26, 62)), ((40, 62), (36, 69)), ((49, 64), (46, 73)), ((70, 66), (71, 70)),
                          ((89, 63), (94, 71)), ((97, 58), (101, 64)), ((107, 90), (115, 93))],
            tile_bits=[(74, 90)], shards_at=[(78, 32), (97, 41), (59, 64), (102, 70), (81, 81)],
            gate=(48, 100, 76, 120))

T = {
    # sturdy: keep and pyramid about 20 percent wider and much lower than
    # the pixel-exact fit (keep 2.35 x 1.55 to 7.3, apex 11.2)
    "TarTower01": dict(TOWER, seed=1),
    "TarTower02": dict(TOWER, seed=2),
    "TarTower01a": dict(RUIN, base="TarTower01", seed=11),
    "TarTower02a": dict(RUIN, base="TarTower02", seed=12),
    # heaps: (end, end, reach, height) boulder heaps along a line, reach
    # their plan radius round it; breach: (x0, x1, y0, y1) of the open
    # ground between them; logs and spikes: (end, end) and shards_at by
    # sprite pixel; stump: (x0, x1, y front, depth, height) of a standing
    # piece of wall
    "TarWall01a": dict(kind="wallruin", heaps=[((-1.3, 2.05), (1.3, 2.05), 1.0, 1.9),
                                                ((-1.3, -1.25), (1.3, -1.25), 1.0, 1.8)],
                       breach=(-2.0, 2.0, -0.1, 1.0), n=130, seed=21,
                       logs=[((36, 27), (51, 30)), ((45, 37), (66, 34)), ((22, 47), (29, 50))],
                       spikes=[((33, 41), (17, 39)), ((65, 30), (65, 24)), ((57, 63), (58, 68))],
                       shards_at=[(65, 27), (43, 35), (13, 50), (42, 54), (28, 40)],
                       stump=(-1.0, 0.9, -2.6, 0.85, 1.5)),
    # the breach holds two low beds of boulders where the drawing has them,
    # under the crossed logs and at the back; each heap may name its
    # boulder count and size range. shell: the two stacks as big round
    # stones over a sunk core (see stone_shell), tops the crowning stones
    # by sprite pixel (col, row, radius)
    "TarWall02a": dict(kind="wallruin", heaps=[((-1.95, -1.25), (-1.95, 1.55), 1.0, 2.95, 68, (0.3, 0.56)),
                                                ((1.74, -1.25), (1.74, 1.55), 1.0, 2.85, 72, (0.3, 0.56)),
                                                ((0.2, -1.75), (0.5, -0.8), 0.5, 0.85, 16, (0.32, 0.62)),
                                                ((-0.7, 1.3), (0.45, 1.8), 0.5, 0.85, 18, (0.32, 0.58))],
                       tones=dict(gain=0.95, cap=135), lit_tops=[(98, 97, 95), (66, 65, 63)], core=0.42, stack=True,
                       gap=(19, 19, 20), lit_pick=(30, 92, 0.28), facet=True,
                       shell=dict(height=2.2, size=(0.32, 0.45), core_in=0.35, gapf=0.72, core_step=3, sink=0.15, cap=0.3, jag=0.14, jitter=0.4,
                                  tops={0: [(20, 17, 0.38), (20, 6, 0.36)], 1: [(70, 18, 0.38), (71, 8, 0.36)]},
                                  smooth=False, lit_pick=(25, 70, 0.25), shrink=0.92, lit=(3, 1, 3), foot=0.1, side=-1, topcap=3, edge=0.85), inset=0.2, pebbles=14, heap_res=(6, 28), skirt_n=10, margin=0.03,
                       breach=(-0.9, 0.8, -1.5, 1.8), n=140, seed=22,
                       logs=[((20, 31), (43, 27)), ((51, 36), (35, 60)), ((32, 50), (52, 51))],
                       spikes=[((37, 25), (44, 22)), ((48, 31), (52, 28))],
                       shards_at=[(30, 22), (56, 42), (40, 62)]),
}


def boulder_tones(m, gain=0.9, cap=95, k=5):
    """The drawing's dark stone tones for boulders, its crimson and pale
    steel left out; a higher cap keeps its lit tops."""
    return rubble.palette(m, k=k, prefix="rub", gain=gain, floor=12, cap=cap,
                          pick=lambda r, g, b: not (r > 1.5 * g + 8))


class LitTones:
    """Tones picked from the drawing round a point at a luminance
    percentile q, so boulders keep the lit tops they are drawn with; q may
    be (dark, lit, share): that share of boulders take the lit percentile,
    the rest the dark one, dark bodies speckled with lit stones as drawn."""

    def __init__(self, tones, q=80, rad=2, jitter=0.0):
        self.t, self.q, self.rad, self.jitter = tones, q, rad, jitter
        self.keys = tones.keys
        self.rng = tones.m.rng

    def at(self, x, y, z):
        import numpy as np
        m = self.t.m
        c, r = m.scr(x, y, z)
        ci, ri, k = int(c), int(r), self.rad
        a = m.pix[max(0, ri - k):ri + k + 1, max(0, ci - k):ci + k + 1].reshape(-1, 4)
        a = a[a[:, 3] > 0.5][:, :3] * 255.0
        if not len(a):
            return self.keys[len(self.keys) // 2]
        lum = a @ np.array([0.3, 0.59, 0.11])
        q = self.q
        if isinstance(q, (tuple, list)):
            q = q[1] if self.rng.random() < q[2] else q[0]
        col = a[int(np.argsort(lum)[min(len(a) - 1, int(len(a) * q / 100))])]
        i = int(((self.t.cen - col[None]) ** 2).sum(1).argmin())
        # neighbours a tone apart now and then, so each boulder stands out
        if self.rng.random() < self.jitter:
            i = max(0, min(len(self.keys) - 1, i + self.rng.choice((-1, 1))))
        return self.keys[i]


def palette(m):
    whole = (0, 0, m.w, m.h)

    def lum(r, g, b):
        return 0.3 * r + 0.59 * g + 0.11 * b
    stone = m.sample(*whole, pick=lambda r, g, b: 35 < lum(r, g, b) < 90 and abs(r - b) < 25)
    # dark warm stone, deep crimson tiles and mid-grey steel, as the drawing has them
    m.col("stone", (stone[0] * 0.52, stone[1] * 0.48, stone[2] * 0.48), 0.9, spec=0.25)
    m.shade("stone2", "stone", 0.78, 0.9)
    m.shade("stone3", "stone", 1.25, 0.9)
    m.col("red", (70, 12, 10), 0.8, spec=0.06)
    m.shade("red2", "red", 0.8, 0.8)
    m.col("steel", (125, 125, 130), 0.45, 0.5)
    m.col("dark", (14, 12, 12), 1.0)
    # coursed masonry: lit block faces over dark recessed joints, the keep's
    # face the lightest stone and a pale lintel over the gate
    m.col("joint", (15, 15, 18), 1.0, spec=0.2)
    m.col("blk", (92, 90, 93), 0.9, spec=0.3)
    m.col("blk2", (64, 62, 65), 0.9, spec=0.3)
    m.col("blk3", (44, 42, 45), 0.9, spec=0.3)
    m.col("keepst", (104, 102, 106), 0.9, spec=0.3)
    m.col("keepst2", (80, 78, 82), 0.9, spec=0.3)
    m.col("lintel", (124, 122, 118), 0.85, spec=0.3)


def oval(cx, cy, ax, ay, n=40):
    return [(cx + ax * math.cos(2 * math.pi * i / n), cy + ay * math.sin(2 * math.pi * i / n)) for i in range(n)]


def frustum(m, key, c0, a0, b0, z0, c1, a1, b1, z1, n=40, keys=None, arc=None):
    """An oval band from radii (a0, b0) round c0 at z0 to (a1, b1) round
    c1 at z1, as quads; keys cycles materials round it (tile panels); arc
    (from, to, in degrees) keeps only that part."""
    t0, t1 = (0.0, 360.0) if arc is None else arc
    k = n if arc is None else max(2, int(n * (t1 - t0) / 360.0))
    pts = []
    for i in range(k + 1):
        t = math.radians(t0 + (t1 - t0) * i / k)
        pts.append(((c0[0] + a0 * math.cos(t), c0[1] + b0 * math.sin(t), z0),
                    (c1[0] + a1 * math.cos(t), c1[1] + b1 * math.sin(t), z1)))
    polys = {}
    for i in range(k):
        kk = keys[i % len(keys)] if keys else key
        (p0, p1), (q0, q1) = pts[i], pts[i + 1]
        polys.setdefault(kk, []).append([p0, q0, q1, p1])
    for kk, pl in polys.items():
        m.faces(kk, pl)


def cone(m, key, base, tip, r, seg=6):
    """A spike from a base point to its tip."""
    from mathutils import Matrix, Vector
    b, t = Vector(base), Vector(tip)
    d = t - b
    L = d.length
    q = d.normalized().to_track_quat("Z", "Y")
    M = Matrix.Translation((b + t) / 2) @ q.to_matrix().to_4x4()
    import bmesh
    bmesh.ops.create_cone(m.bm(key), cap_ends=True, segments=seg, radius1=r, radius2=0.0005, depth=L, matrix=M)


def spike(m, key, x, y, z0, h, r, n=4):
    """An n-sided pyramid standing on the surface, its base left open."""
    b = [(x + r * math.cos(2 * math.pi * (i + 0.5) / n), y + r * math.sin(2 * math.pi * (i + 0.5) / n), z0)
         for i in range(n)]
    m.faces(key, [[b[i], b[(i + 1) % n], (x, y, z0 + h)] for i in range(n)])


def blocks_row(m, u0, u1, z0, z1, blen, key_of, place, jw=0.08, jz=0.13, stagger=0.0):
    """Blocks along one course from u0 to u1, about blen long with jw
    joints between them and jz between courses; place(key, a, b, z0, z1)
    builds each."""
    rng = m.rng
    u = u0 - stagger * blen
    while u < u1 - 0.05:
        v = min(u1, u + blen * rng.uniform(0.75, 1.25))
        if u1 - v < 0.35 * blen:
            v = u1
        a, b = max(u0, u) + jw / 2, v - jw / 2
        if b - a > 0.12:
            place(key_of(), a, b, z0 + jz / 2, z1 - jz / 2)
        u = v


def pick_block(m, keys=("blk", "blk", "blk2", "blk3")):
    return lambda: keys[m.rng.randrange(len(keys))]


def course_ring(m, co, afn, bfn, z0, z1, courses, arc, blen=0.85, proud=0.06, depth=0.14, skip=None, keys=None):
    """Coursed blocks round an oval wall face whose radii at height z are
    afn(z), bfn(z) about co, between bearings arc (degrees), standing proud
    of the dark core so the joints between them read; skip(x, y, z) leaves
    a block out."""
    t0, t1 = arc
    kf = pick_block(m, keys) if keys else pick_block(m)

    def P(t, z, off):
        t = math.radians(t)
        return (co[0] + (afn(z) + off) * math.cos(t), co[1] + (bfn(z) + off) * math.sin(t), z)

    def place(key, ta, tb, za, zb):
        tm = math.radians((ta + tb) / 2)
        x, y = co[0] + afn(za) * math.cos(tm), co[1] + bfn(za) * math.sin(tm)
        if skip and skip(x, y, (za + zb) / 2):
            return
        o = [P(ta, za, proud), P(tb, za, proud), P(tb, zb, proud), P(ta, zb, proud)]
        i = [P(ta, za, -depth), P(tb, za, -depth), P(tb, zb, -depth), P(ta, zb, -depth)]
        m.solid(key, [o, i[::-1], [o[0], i[0], i[1], o[1]], [o[3], o[2], i[2], i[3]],
                      [o[0], o[3], i[3], i[0]], [o[1], i[1], i[2], o[2]]])
    for c in range(courses):
        za, zb = z0 + (z1 - z0) * c / courses, z0 + (z1 - z0) * (c + 1) / courses
        a, b = afn((za + zb) / 2), bfn((za + zb) / 2)
        per = math.radians(1.0) * math.sqrt((a * a + b * b) / 2)
        # in degrees along the arc: joints and block lengths
        blocks_row(m, t0, t1, za, zb, blen / per, kf, place, jw=0.08 / per, stagger=0.5 * (c % 2))


def face_courses(m, x0, x1, y, z0, z1, courses, blen, keys, proud=0.05, gap=None):
    """Coursed blocks on a south-facing plane at y, a little proud of it;
    gap(z0, z1) gives an (xa, xb) span a course leaves open, or None."""
    kf = pick_block(m, keys)

    def place(key, a, b, za, zb):
        m.box(key, a, b, y - proud, y + 0.04, za, zb)
    for c in range(courses):
        za, zb = z0 + (z1 - z0) * c / courses, z0 + (z1 - z0) * (c + 1) / courses
        g = gap(za, zb) if gap else None
        spans = [(x0, x1)] if g is None else [(x0, g[0]), (g[1], x1)]
        for a, b in spans:
            if b - a > 0.15:
                blocks_row(m, a, b, za, zb, blen, kf, place, stagger=0.5 * (c % 2))


def side_courses(m, x, y0, y1, z0, z1, courses, blen, keys, side, proud=0.05):
    """Coursed blocks on an east (side 1) or west (side -1) facing plane at x."""
    kf = pick_block(m, keys)

    def place(key, a, b, za, zb):
        m.box(key, x - 0.04 * side, x + proud * side, a, b, za, zb)
    for c in range(courses):
        za, zb = z0 + (z1 - z0) * c / courses, z0 + (z1 - z0) * (c + 1) / courses
        blocks_row(m, y0, y1, za, zb, blen, kf, place, stagger=0.5 * (c % 2))


def gatehouse(m, x, yf, w, d, h, door=(1.2, 1.8), crenel=True, broken=False, key="stone3", coursed=False):
    """A block on the south face, its front at yf: an arched doorway (width,
    height to the crown) sunk into the front with a dark back, a ring of
    voussoirs round it, and crenels along the top (or a ragged broken top).
    coursed: the front and sides laid in lit blocks over dark joints, with a
    pale lintel course over the arch."""
    rng = m.rng
    dw, dh = door
    r = dw / 2
    spring = dh - r
    fd = 0.45
    n = 10
    arch = [(x + r * math.cos(math.pi * (1 - i / n)), spring + r * math.sin(math.pi * (1 - i / n)))
            for i in range(n + 1)]
    if broken:
        k = 6
        top = [(x + w / 2 - w * i / k, h * rng.uniform(0.7, 1.0)) for i in range(k + 1)]
    else:
        top = [(x + w / 2, h), (x - w / 2, h)]
    # the front skin with the doorway cut out of it, one polygon in (x, z)
    front = [(x - w / 2, 0.0), (x - r, 0.0)] + arch + [(x + r, 0.0), (x + w / 2, 0.0)] + top
    m.plate("joint" if coursed else key, [(u, yf, z) for u, z in front], (0.0, fd, 0.0))
    back = [(x - w / 2, 0.0), (x + w / 2, 0.0)] + top
    m.plate("joint" if coursed else key, [(u, yf + fd, z) for u, z in back], (0.0, d - fd, 0.0))
    # the doorway's dark back
    hole = [(x - r, 0.0), (x + r, 0.0)] + arch[::-1]
    m.plate("dark", [(u, yf + fd - 0.02, z) for u, z in hole], (0.0, 0.015, 0.0))
    # voussoirs: a ring a little proud of the face round the arch
    ro = r + 0.2
    outer = [(x + ro * math.cos(math.pi * i / n), spring + ro * math.sin(math.pi * i / n)) for i in range(n + 1)]
    inner = [(x + r * math.cos(math.pi * i / n), spring + r * math.sin(math.pi * i / n)) for i in range(n + 1)]
    ring = [(x + ro, spring - 0.25), (x + r, spring - 0.25)] + inner + [(x - r, spring - 0.25),
                                                                       (x - ro, spring - 0.25)] + outer[::-1]
    if spring + ro < min(z for _, z in top) - 0.05:
        m.plate("blk2" if coursed else "stone2", [(u, yf - 0.05, z) for u, z in ring], (0.0, 0.06, 0.0))
    if coursed:
        zl = h - 0.3

        def gap(za, zb):
            if za >= spring + ro:
                return None
            if zb <= spring - 0.25:
                return (x - r - 0.02, x + r + 0.02)
            e = ro if za <= spring else math.sqrt(max(0.0, ro * ro - (za - spring) ** 2))
            return (x - e - 0.02, x + e + 0.02)
        face_courses(m, x - w / 2, x + w / 2, yf, 0.0, zl, 5, 0.7, ("blk", "blk2", "blk3"), gap=gap)
        m.box("lintel", x - w / 2 - 0.02, x + w / 2 + 0.02, yf - 0.07, yf + 0.05, zl + 0.03, h - 0.02)
        for sd in (-1, 1):
            side_courses(m, x + sd * w / 2, yf + 0.05, yf + d, 0.0, h, 5, 0.75, ("blk2", "blk3", "stone3"), sd)
    if crenel and not broken:
        # crenels along the front and both sides of the top
        cw, ch, cd = 0.34, 0.36, 0.3
        k = max(2, int(round(w / 0.62)))
        for i in range(k):
            cxx = x - w / 2 + cw / 2 + (w - cw) * i / (k - 1)
            m.box("blk2" if coursed else "stone3", cxx - cw / 2, cxx + cw / 2, yf, yf + cd, h, h + ch)
            m.box("lintel" if coursed else "stone2", cxx - cw / 2 - 0.03, cxx + cw / 2 + 0.03, yf - 0.03, yf + cd + 0.03,
                  h + ch, h + ch + 0.06)
        ks = max(2, int(round(d / 0.62)))
        for side in (-1, 1):
            ex = x + side * (w / 2 - cd / 2)
            for i in range(1, ks):
                cyy = yf + cw / 2 + (d - cw) * i / (ks - 1)
                m.box("stone3", ex - cd / 2, ex + cd / 2, cyy - cw / 2, cyy + cw / 2, h, h + ch)


def merlons(m, key, cx, cy, a, b, z, n, w=0.42, d=0.3, h=0.6, a0=0.0, a1=2 * math.pi):
    """A crown of pointed merlons round an oval: four-sided spikes on a
    rectangular foot, set side by side so the rim reads as one spiked edge."""
    for i in range(n):
        t = a0 + (a1 - a0) * (i + 0.5) / n
        x, y = cx + a * math.cos(t), cy + b * math.sin(t)
        tx, ty = -a * math.sin(t), b * math.cos(t)
        L = math.hypot(tx, ty)
        tx, ty = tx / L, ty / L
        nx, ny = ty, -tx
        hw, hd = w / 2, d / 2
        P = [(x + tx * hw + nx * hd, y + ty * hw + ny * hd), (x - tx * hw + nx * hd, y - ty * hw + ny * hd),
             (x - tx * hw - nx * hd, y - ty * hw - ny * hd), (x + tx * hw - nx * hd, y + ty * hw - ny * hd)]
        base = [(px, py, z) for px, py in P]
        tip = (x, y, z + h)
        m.solid(key, [base[::-1]] + [[base[k], base[(k + 1) % 4], tip] for k in range(4)])


# the tile ring from the outer rim (0) to the inner ring wall (1): broad
# crimson strips between narrow bands of black stone, each band set with a
# row of cones
TILE_BANDS = [(0.0, 0.26, "red"), (0.26, 0.34, "stone2"), (0.34, 0.64, "red"), (0.64, 0.72, "stone2"),
              (0.72, 0.95, "red"), (0.95, 1.0, "stone2")]
CONE_ROWS = [(0.3, 34), (0.68, 26), (0.975, 20)]


def tower(m, p):
    cx, cy, ax, ay, H1, t1 = p["cx"], p["cy"], p["ax"], p["ay"], p["H1"], p["t1"]
    rng = m.rng
    # the outer wall's oval runs a cell deeper to the north, its south face kept
    no = p.get("north", 0.0)
    co, ayo = (cx, cy + no / 2), ay + no / 2
    c = (cx, cy)
    gx, gyf, gw, gd, gh = cx, cy - ay - 0.55, 2.8, 1.5, 2.4
    # between the lit blocks the drawing's stone is near black
    m.shade("stone2", "stone", 0.62, 0.9)
    m.shade("stone3", "stone", 1.1, 0.9)
    m.col("blk3", (38, 36, 39), 0.9, spec=0.3)
    m.col("red", (74, 6, 5), 0.8, spec=0.06)
    m.shade("red2", "red", 0.8, 0.8)

    # the outer wall: battered, a dark core laid over with coursed blocks
    # where it can be seen, crowned with pointed merlons
    def fa(z):
        return ax + 0.35 * (1 - z / H1)

    def fb(z):
        return ayo + 0.35 * (1 - z / H1)
    frustum(m, "joint" if not m.fast else "stone", co, fa(0), fb(0), 0.0, co, fa(H1), fb(H1), H1)
    if not m.fast:
        def behind_gate(x, y, z):
            return abs(x - gx) < gw / 2 + 0.1 and y < cy and z < gh + 0.05
        course_ring(m, co, fa, fb, 0.0, H1, 5, (150.0, 400.0), skip=behind_gate,
                    keys=("blk", "blk2", "blk3", "blk3", "stone3", "stone3"))
        # the rest of the round, seen only from behind, in plain dark courses
        frustum(m, "stone2", co, fa(0) + 0.02, fb(0) + 0.02, 0.0, co, fa(H1) + 0.02, fb(H1) + 0.02, H1,
                arc=(45.0, 145.0), n=40)
    # its flat top, wider at the back, from the outer face to the tiles
    frustum(m, "stone2", co, ax, ayo, H1, c, ax - t1, ay - t1, H1)
    if not m.fast:
        merlons(m, "blk3", co[0], co[1], ax - 0.2, ayo - 0.2, H1, 48)
    # the tile ring climbing to the inner wall
    ax2, ay2, H2 = p["ax2"], p["ay2"], p["H2"]
    zr0, zr1 = H1, H1 + 0.75
    a0, b0 = ax - t1 + 0.02, ay - t1 + 0.02
    a1, b1 = ax2 + 0.1, ay2 + 0.1

    def at(f):
        return a0 + (a1 - a0) * f, b0 + (b1 - b0) * f, zr0 + (zr1 - zr0) * f
    for f0, f1, key in (TILE_BANDS if not m.fast else [(0.0, 1.0, "red")]):
        (ra, rb, za), (rc, rd, zc) = at(f0), at(f1)
        keys = ["red", "red", "red2"] if key == "red" else None
        frustum(m, key, c, ra, rb, za, c, rc, rd, zc, keys=keys)
    if not m.fast:
        for f, k in CONE_ROWS:
            ra, rb, z = at(f)
            for i, (x, y) in enumerate(oval(cx, cy, ra, rb, k)):
                # across the front only: round the sides the tiles show clear
                if 205 < 360.0 * i / k < 335:
                    spike(m, "stone3", x, y, z - 0.05, 0.5, 0.15)
    # the inner ring wall above the tiles, and its crest of cones
    frustum(m, "stone2", c, ax2 + 0.1, ay2 + 0.1, zr1, c, ax2, ay2, H2)
    frustum(m, "stone2", c, ax2, ay2, H2, c, 0.01, 0.01, H2)
    if not m.fast:
        for x, y in oval(cx, cy, ax2 - 0.1, ay2 - 0.1, 20):
            spike(m, "blk2", x, y, H2 - 0.02, 0.57, 0.17, n=5)
    # the keep: a dark core faced with the lightest stone in courses, a
    # crenellated parapet across its front, and the pyramid set back behind it
    kx0, kx1, ky0, ky1, H3, H4 = p["kx0"], p["kx1"], p["ky0"], p["ky1"], p["H3"], p["H4"]
    m.box("joint" if not m.fast else "stone2", kx0, kx1, ky0, ky1, H2 - 0.1, H3)
    # the string course under the parapet, proud at the front only so the
    # sides do not draw a pale frame round the pyramid
    m.box("keepst2", kx0 - 0.02, kx1 + 0.02, ky0 - 0.08, ky1 + 0.02, H3 - 0.22, H3)
    pt, ph = 0.32, 0.42
    m.box("keepst2", kx0 - 0.04, kx1 + 0.04, ky0 - 0.04, ky0 + pt, H3, H3 + ph)
    if not m.fast:
        face_courses(m, kx0, kx1, ky0, H2 - 0.1, H3 - 0.22, 6, 0.62, ("keepst", "keepst2", "blk2"))
        for sd, xx in ((-1, kx0), (1, kx1)):
            side_courses(m, xx, ky0, ky1, H2 - 0.1, H3 - 0.22, 6, 0.8, ("blk2", "blk3", "blk3"), sd, proud=0.03)
        # four square merlons with pointed caps along the parapet
        n, mw = 4, 0.5
        for i in range(n):
            x = kx0 + 0.04 + mw / 2 + (kx1 - kx0 - 0.08 - mw) * i / (n - 1)
            y0, y1 = ky0 - 0.04, ky0 + pt
            m.box("keepst", x - mw / 2, x + mw / 2, y0, y1, H3 + ph, H3 + ph + 0.45)
            B = [(x - mw / 2, y0), (x + mw / 2, y0), (x + mw / 2, y1), (x - mw / 2, y1)]
            zt = H3 + ph + 0.45
            m.faces("keepst2", [[(*B[k], zt), (*B[(k + 1) % 4], zt), (x, (y0 + y1) / 2, zt + 0.32)] for k in range(4)])
        nb = 6
        for i in range(nb):
            x = kx0 + (kx1 - kx0) * (i + 0.5) / nb
            spike(m, "stone3", x, ky1 - 0.12, H3, 0.55, 0.17, n=5)
    kxm, kym = (kx0 + kx1) / 2, (ky0 + ky1) / 2
    # the pyramid's eave raised a little and drawn in from the front so the
    # parapet and its merlons stand clear of it; the peak kept low
    ze = H3 + 0.25
    E = [(kx0 + 0.2, ky0 + pt + 0.12), (kx1 - 0.2, ky0 + pt + 0.12), (kx1 - 0.12, ky1 - 0.12), (kx0 + 0.12, ky1 - 0.12)]
    apex = (kxm - 0.2, kym + 0.1, H4)
    faces = [[(*E[i], ze), (*E[(i + 1) % 4], ze), apex] for i in range(4)]
    m.solid("red2", faces[0:1] + faces[2:3] + [[(*e, ze) for e in E[::-1]]] + faces[1:2] + faces[3:4])
    # the gatehouse on the south face, standing a little proud of the wall's foot
    gatehouse(m, gx, gyf, gw, gd, gh, coursed=not m.fast)
    if m.fast:
        return
    # steel spikes out of the outer wall and the tile ring
    for i in range(14):
        a = math.radians(-205 + 230 * i / 13)
        x, y = co[0] + (ax + 0.1) * math.cos(a), co[1] + (ayo + 0.1) * math.sin(a)
        if abs(x - cx) < 1.6 and y < cy:
            continue
        L = rng.uniform(1.5, 2.1)
        out = (math.cos(a), math.sin(a))
        cone(m, "steel", (x, y, 0.9), (x + out[0] * L, y + out[1] * L, 0.9 - L * 0.35), 0.09)
    for i in range(12):
        a = math.radians(-200 + 220 * i / 11)
        # rooted in the first tile strip
        ra, rb, z = at(0.06)
        x, y = cx + ra * math.cos(a), cy + rb * math.sin(a)
        L = rng.uniform(0.9, 1.3)
        out = (math.cos(a), math.sin(a))
        cone(m, "steel", (x, y, z - 0.05), (x + out[0] * L, y + out[1] * L, z + L * 0.3), 0.06)


def arc_wall(m, cx, cy, a, b, t, d0, d1, h, key="stone", jag=0.35):
    """A broken length of an oval wall between bearings d0 and d1 (degrees),
    t thick, standing about h with a ragged top, low at both broken ends.
    Returns the top at each of its stations."""
    rng = m.rng
    n = max(3, int((d1 - d0) / 6))
    tops = [h * rng.uniform(1 - jag, 1.0) for _ in range(n + 1)]
    tops[0] *= 0.55
    tops[-1] *= 0.55
    for i in range(n):
        u0, u1 = math.radians(d0 + (d1 - d0) * i / n), math.radians(d0 + (d1 - d0) * (i + 1) / n)
        P = [(cx + a * math.cos(u), cy + b * math.sin(u)) for u in (u0, u1)]
        Q = [(cx + (a - t) * math.cos(u), cy + (b - t) * math.sin(u)) for u in (u0, u1)]
        za, zb = tops[i], tops[i + 1]
        bot = [(*P[0], 0.0), (*P[1], 0.0), (*Q[1], 0.0), (*Q[0], 0.0)]
        top = [(*P[0], za), (*P[1], zb), (*Q[1], zb - 0.1), (*Q[0], za - 0.1)]
        m.solid(key if i % 2 else "stone2", [bot[::-1], top] + [[bot[k], bot[(k + 1) % 4], top[(k + 1) % 4], top[k]]
                                                                 for k in range(4)])
    return [(d0 + (d1 - d0) * i / n, tops[i]) for i in range(n + 1)]


def stub_top(tops, d):
    for (a0, h0), (a1, h1) in zip(tops, tops[1:]):
        if a0 <= d <= a1:
            return h0 + (h1 - h0) * (d - a0) / max(1e-6, a1 - a0)
    return None


def tower_ruin(m, r):
    """The fallen tower: a boulder heap over its plan, stubs of the outer
    wall on the left and back carrying curved strips of the tile ring and
    spikes, and the gatehouse stump with its arch."""
    p = kit.params(r["base"], T)
    rng = m.rng
    tones = boulder_tones(m)
    cx, cy, ax, ay = p["cx"], p["cy"], p["ax"], p["ay"]
    t = p["t1"] + 0.2
    # the gate stump is paler than the heap, as the drawing has it
    m.col("gate", m.sample(*r["gate"], pick=lambda r_, g, b: 40 < 0.3 * r_ + 0.59 * g + 0.11 * b < 150), 0.9,
          spec=0.3)
    gatehouse(m, cx, cy - ay - 0.55, 2.8, 1.3, 1.7, door=(1.2, 1.35), crenel=False, broken=True, key="gate")
    stubs = []
    for d0, d1, h in r["stubs"]:
        stubs.append(arc_wall(m, cx, cy, ax + 0.15, ay + 0.15, t, d0, d1, h))

    # the heap: its edge read off the drawing, no further out than the old
    # wall's foot behind, highest over the inner ring, its middle lower; a
    # core the boulders mostly hide
    def north(a):
        return 1.0 / math.sqrt((math.cos(a) / (ax + 0.35)) ** 2 + (math.sin(a) / (ay + 0.35)) ** 2)
    rfn = rubble.outline(m, cx, cy, lambda a: north(a) + 0.5, north=north, jag=0.06, seed=r["seed"])

    def hfn(x, y):
        e = math.sqrt(((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2)
        ring = max(0.0, 1 - ((e - 0.55) / 0.5) ** 2)
        return r["heap"] * max(0.72, ring) if e < 1.1 else 0.0
    heap = rubble.heap_mesh(m, tones, cx, cy, rfn, hfn, rings=9, seg=44, skirt=0.3, key=tones.keys[0],
                            bumps=0.22, seed=r["seed"])
    # the tile strips the stubs still carry, lying on the heap behind them
    for d0, d1 in r["tiles"]:
        tile_strip(m, cx, cy, ax + 0.15 - t, ay + 0.15 - t, d0, d1, stubs, heap, width=r["tile_w"])
    for cr in r["tile_bits"]:
        x, y, z = on_surface(m, cr, heap)
        tile_bit(m, x, y, z, 0.7)

    box = (cx - ax - 1.0, cx + ax + 1.0, cy - ay - 1.2, cy + ay + 0.8)

    def on_heap(x, y):
        a = math.atan2(y - cy, x - cx)
        if math.hypot(x - cx, y - cy) > rfn(a) - 0.3:
            return False
        # the tile strips stay clear
        e = math.hypot((x - cx) / ax, (y - cy) / ay)
        d = math.degrees(math.atan2((y - cy) / ay, (x - cx) / ax)) % 360
        if e > 0.62 and any(d0 - 3 < d < d1 + 3 for d0, d1 in r["tiles"]):
            return False
        # nor may one stand in front of the gate
        return not (abs(x - cx) < 1.3 and y < cy - ay + 0.3)
    rubble.blocks(m, tones, box, r["n"], size=(0.4, 1.0), zfn=heap, near=on_heap, seed=r["seed"], round_=True)

    # smaller stones down the heap's skirt, out to its ragged edge
    def skirt(x, y):
        a = math.atan2(y - cy, x - cx)
        d = math.hypot(x - cx, y - cy)
        return rfn(a) * 0.7 < d < rfn(a) - 0.1 and on_heap_any(x, y)

    def on_heap_any(x, y):
        return not (abs(x - cx) < 1.3 and y < cy - ay + 0.3)
    rubble.blocks(m, tones, box, r.get("n_skirt", 36), size=(0.22, 0.5), zfn=heap, near=skirt, seed=r["seed"] + 3,
                  round_=True, loose_edge=True)
    # spikes still set in the stubs
    for d, L, z, droop, turn in r["spikes"]:
        a = math.radians(d)
        h = math.radians(d + turn)
        x, y = cx + (ax + 0.2) * math.cos(a), cy + (ay + 0.2) * math.sin(a)
        # set into the stub below its broken top
        for tops in stubs:
            s = stub_top(tops, d)
            if s is not None:
                z = min(z, s - 0.3)
        cone(m, "steel", (x - math.cos(h) * 0.35, y - math.sin(h) * 0.35, z),
             (x + math.cos(h) * L, y + math.sin(h) * L, z - L * droop), 0.09)
    # spikes of the fallen inner ring, still pointing out of the heap
    for c0, c1 in r["inner_spikes"]:
        pixel_spike(m, c0, c1, heap)
    for cr in r["shards_at"]:
        x, y, z = on_surface(m, cr, heap)
        shard(m, x, y, heap)


def on_surface(m, cr, zfn, lift=0.0):
    """The point drawn at sprite pixel cr = (col, row) lying on zfn."""
    c, r = cr
    x = m.X(c)
    z = 0.0
    for _ in range(8):
        y = m.Y(r, z)
        z = zfn(x, y) + lift
    return x, m.Y(r, z), z


def pixel_spike(m, c0, c1, zfn, r=0.08):
    """A steel spike drawn from sprite pixel c0 (its root, on the heap) to c1
    (its tip, a little above the heap)."""
    b = on_surface(m, c0, zfn, 0.05)
    t = on_surface(m, c1, zfn, 0.25)
    cone(m, "steel", (b[0], b[1], b[2] - 0.1), t, r)


def tile_bit(m, x, y, z, s):
    """A broken slab of the tile ring lying on the heap: tiles on top, a
    black cone still standing on it."""
    rng = m.rng
    yaw = rng.uniform(0, math.pi)
    m.obox("red", x, y, z + 0.05, s, s * 0.7, 0.12, yaw=yaw, pitch=rng.uniform(-0.2, 0.2),
           roll=rng.uniform(-0.2, 0.2))
    cone(m, "stone3", (x, y, z + 0.08), (x, y, z + 0.5), 0.15, seg=5)


def tile_strip(m, cx, cy, a, b, d0, d1, stubs, zfn, width=1.0):
    """A curved strip of the tile ring left on a wall stub: from the stub's
    inner top edge it runs inward over the heap, its inner edge torn, with
    a row of stone cones on it. A slab, so its torn edge shows."""
    rng = m.rng
    n = max(3, int((d1 - d0) / 5))
    rows = []
    for i in range(n + 1):
        d = d0 + (d1 - d0) * i / n
        h = None
        for s in stubs:
            h = stub_top(s, d) if h is None else h
        u = math.radians(d)
        ca, sa = math.cos(u), math.sin(u)
        w = width * rng.uniform(0.6, 1.0) * (0.45 if i in (0, n) else 1.0)
        k = w / max(a, b)
        ox, oy = cx + a * ca, cy + b * sa
        ix, iy = cx + a * (1 - k) * ca, cy + b * (1 - k) * sa
        zo = max((h or 0.0) - 0.1, zfn(ox, oy)) + 0.06
        zi = max(zo + 0.25, zfn(ix, iy) + 0.08)
        rows.append(((ox, oy, zo), (ix, iy, zi)))
    for i in range(n):
        (oa, ia), (ob, ib) = rows[i], rows[i + 1]
        m.plate("red" if i % 3 else "red2", [oa, ob, ib, ia], (0.0, 0.0, -0.14))
        if i % 2 == 0:
            mx = [(oa[j] + ob[j] + ia[j] + ib[j]) / 4 for j in range(3)]
            cone(m, "stone3", (mx[0], mx[1], mx[2] - 0.05), (mx[0], mx[1], mx[2] + 0.45), 0.15, seg=5)


def shard(m, x, y, zfn, size=(0.15, 0.3)):
    """A small broken piece of red tile lying on whatever is under it."""
    rng = m.rng
    s = rng.uniform(*size)
    yaw = rng.uniform(0, math.pi)
    pitch, roll = rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25)
    # rest on the highest ground under it
    z = max(zfn(x + dx * s * 0.5, y + dy * s * 0.35) for dx in (-1, 0, 1) for dy in (-1, 0, 1)) - 0.04
    m.obox("red" if rng.random() < 0.6 else "red2", x, y, z + 0.02, s, s * 0.7, 0.06, yaw=yaw, pitch=pitch,
           roll=roll)


def seg_dist(x, y, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L2 = dx * dx + dy * dy or 1e-9
    u = max(0.0, min(1.0, ((x - a[0]) * dx + (y - a[1]) * dy) / L2))
    return math.hypot(a[0] + dx * u - x, a[1] + dy * u - y)


def wall_ruin(m, p):
    """A fallen Taros wall: boulder heaps where it still stands, each
    sloping to the ground inside the drawing's outline, an open breach
    between them strewn with small stones, long brown logs lying across it,
    steel spikes and a few crimson flecks, each placed where the drawing
    shows it."""
    tones = boulder_tones(m, **p.get("tones", {}))
    # the boulders drawn brightest keep their lit tops
    for key, rgb in zip(tones.keys[::-1], p.get("lit_tops", [])):
        m.col(key, rgb, 0.95)
    core = tones.keys[0]
    if p.get("gap"):
        # the cores dark as the gaps between the drawn boulders
        core = m.col("gap", p["gap"], 1.0)
    placed = LitTones(tones, q=p["lit_pick"], jitter=0.25) if p.get("lit_pick") else tones
    m.col("log", (82, 64, 51), 0.9)
    m.shade("log2", "log", 0.82, 0.9)
    heaps = []
    shells = {}
    smooth = []
    for k, hp in enumerate(p["heaps"]):
        a, b, reach, hh = hp[:4]
        c = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        lim = rubble.shape(c[0], c[1], lambda x, y, a=a, b=b: seg_dist(x, y, a, b) < reach)
        rfn = rubble.outline(m, c[0], c[1], lim, jag=0.05, seed=p["seed"] + k, margin=p.get("margin", 0.0))
        if p.get("shell") and reach > 0.7:
            # a shell of big round stones over a core sunk well inside it
            env = Envelope(m, c, rfn, a, b, reach, p["shell"].get("height", hh))
            shells[k] = env
            heaps.append(env)
            continue

        # a core the boulders mostly hide
        def hfn(x, y, a=a, b=b, reach=reach, hh=hh):
            d = seg_dist(x, y, a, b)
            return p.get("core", 0.8) * hh * max(0.0, 1 - (d / reach) ** 2) ** 0.6
        # a small bed needs fewer rings
        rings, seg = p.get("heap_res", (8, 40)) if reach > 0.7 else (4, 20)
        heaps.append(rubble.heap_mesh(m, tones, c[0], c[1], rfn, hfn, rings=rings, seg=seg, skirt=0.35,
                                      key=core, bumps=0.2, seed=p["seed"] + k))

    def zf(x, y):
        return max(h(x, y) for h in heaps)
    ins = p.get("inset", 0.0)

    def inside(x, y):
        # a boulder's sides stay inside the drawing, not only its middle
        if ins <= 0:
            return True
        z = zf(x, y) + 0.25
        return all(rubble.drawn(m, x + dx, y + dy, z) for dx, dy in ((ins, 0), (-ins, 0), (0, ins), (0, -ins)))
    for k, hp in enumerate(p["heaps"]):
        # optional per heap: its boulder count and their size range
        a, b, reach, hh = hp[:4]
        nk = hp[4] if len(hp) > 4 else p["n"] // len(p["heaps"])
        box = (min(a[0], b[0]) - reach, max(a[0], b[0]) + reach, min(a[1], b[1]) - reach, max(a[1], b[1]) + reach)
        def near(x, y, a=a, b=b, reach=reach):
            return seg_dist(x, y, a, b) < reach - 0.12 and inside(x, y)
        if k in shells:
            q = dict(p["shell"], tops=p["shell"].get("tops", {}).get(k, []))
            # big stones take a mid tone, so the light gives each a lit top
            # and a dark foot as drawn
            lp = q.get("lit_pick")
            st = LitTones(tones, q=lp, rad=q.get("rad", 3), jitter=q.get("jitter", 0.25)) if lp else placed
            fs = stone_shell(m, st, core, shells[k], q, p["seed"] + 31 * k)
            if q.get("smooth", True):
                smooth += fs
            continue
        if p.get("stack"):
            # piled boulders over a low core, up to the drawing's height
            stack(m, placed, zf, near, box, nk, hp[5] if len(hp) > 5 else (0.3, 0.75), p["seed"] + 13 * k)
        else:
            rubble.blocks(m, placed, box, nk, size=hp[5] if len(hp) > 5 else (0.3, 0.75), zfn=zf, near=near,
                          seed=p["seed"] + 13 * k, round_=True)
        if p.get("skirt_n") and reach > 0.7:
            # small stones down the flanks, out to the drawing's edge
            rubble.blocks(m, placed, box, p["skirt_n"], size=(0.2, 0.34), zfn=zf,
                          near=lambda x, y, a=a, b=b, reach=reach: reach * 0.55 < seg_dist(x, y, a, b) < reach - 0.12,
                          seed=p["seed"] + 13 * k + 5, round_=True, loose_edge=True)
    # small stones on the ground in the breach
    bx0, bx1, by0, by1 = p["breach"]
    rubble.blocks(m, tones, (bx0, bx1, by0, by1), p.get("pebbles", 14), size=(0.15, 0.35), zfn=zf,
                  seed=p["seed"] + 5, round_=True)
    # long logs lying across the breach, resting on what is under them
    for k, (c0, c1) in enumerate(p["logs"]):
        r_ = 0.15
        A = on_surface(m, c0, zf, r_ * 0.8)
        B = on_surface(m, c1, zf, r_ * 0.8)
        # lifted clear of anything in between
        lift = 0.0
        for f in (0.2, 0.4, 0.5, 0.6, 0.8):
            z = zf(A[0] + (B[0] - A[0]) * f, A[1] + (B[1] - A[1]) * f) + r_ * 0.8
            lift = max(lift, z - (A[2] + (B[2] - A[2]) * f))
        m.rod("log" if k % 2 == 0 else "log2", (A[0], A[1], A[2] + lift), (B[0], B[1], B[2] + lift), r_, seg=7)
    for c0, c1 in p["spikes"]:
        pixel_spike(m, c0, c1, zf)
    for cr in p["shards_at"]:
        x, y, z = on_surface(m, cr, zf)
        shard(m, x, y, zf)
    if p.get("stump"):
        stump(m, *p["stump"])
    if p.get("facet"):
        # faceted boulders, their edges catching the light as drawn
        for key in tones.keys + ["gap"]:
            if key in m.groups:
                for f in m.groups[key].faces:
                    f.smooth = False
    for f in smooth:
        f.smooth = True


class Envelope:
    """The outer surface of a stone heap over its plan outline rfn: a ridge
    along a to b falling off across reach, steepening at the outline, and
    kept under the drawing. Callable as a height, with normals."""

    def __init__(self, m, c, rfn, a, b, reach, hh, cell=0.06, rim=0.3):
        import numpy as np
        self.c, self.rfn, self.cell = c, rfn, cell
        self.a, self.b, self.reach = a, b, reach
        self.x0 = min(a[0], b[0]) - reach - 0.3
        self.y0 = min(a[1], b[1]) - reach - 0.3
        nx = int((max(a[0], b[0]) + reach + 0.3 - self.x0) / cell) + 2
        ny = int((max(a[1], b[1]) + reach + 0.3 - self.y0) / cell) + 2
        Z = np.zeros((ny, nx))
        for j in range(ny):
            for i in range(nx):
                x, y = self.x0 + i * cell, self.y0 + j * cell
                s = self.frac(x, y)
                if s >= 1.0:
                    continue
                d = seg_dist(x, y, a, b)
                h = hh * max(0.0, 1 - (d / reach) ** 2) ** 0.5
                t = min(1.0, (1.0 - s) / rim)
                h *= (3 * t * t - 2 * t * t * t) ** 0.5
                top = rubble.top_allowed(m, x, y, h + 0.05)
                Z[j, i] = max(0.0, min(h, top))
        for it in range(2):
            G = Z.copy()
            G[1:-1, 1:-1] = 0.5 * Z[1:-1, 1:-1] + 0.125 * (Z[:-2, 1:-1] + Z[2:, 1:-1] + Z[1:-1, :-2] + Z[1:-1, 2:])
            Z = np.minimum(Z, G) if it else G
        self.Z = Z
        self.laid = None

    def frac(self, x, y):
        dx, dy = x - self.c[0], y - self.c[1]
        return math.hypot(dx, dy) / max(1e-6, self.rfn(math.atan2(dy, dx) % (2 * math.pi)))

    def __call__(self, x, y):
        if self.laid is not None:
            return self.laid(x, y)
        return self.grid(self.Z, x, y)

    def grid(self, Z, x, y):
        fi, fj = (x - self.x0) / self.cell, (y - self.y0) / self.cell
        i, j = int(math.floor(fi)), int(math.floor(fj))
        if not (0 <= i < Z.shape[1] - 1 and 0 <= j < Z.shape[0] - 1):
            return 0.0
        u, v = fi - i, fj - j
        return float((Z[j, i] * (1 - u) + Z[j, i + 1] * u) * (1 - v) + (Z[j + 1, i] * (1 - u) + Z[j + 1, i + 1] * u) * v)

    def rim(self, step=0.04):
        """Points round the outline with their outward plan normal."""
        pts = []
        for j in range(360):
            a = 2 * math.pi * j / 360
            R = self.rfn(a)
            pts.append((self.c[0] + R * math.cos(a), self.c[1] + R * math.sin(a)))
        out, acc = [], 0.0
        for j in range(360):
            p0, p1 = pts[j], pts[(j + 1) % 360]
            acc += math.hypot(p1[0] - p0[0], p1[1] - p0[1])
            if acc >= step:
                acc = 0.0
                tx, ty = p1[0] - pts[j - 1][0], p1[1] - pts[j - 1][1]
                L = math.hypot(tx, ty) or 1.0
                out.append((p0, (ty / L, -tx / L)))
        return out


_OCTA = None


def _octa():
    """An octahedron split once: 18 corners on the unit sphere, 32 faces."""
    global _OCTA
    if _OCTA is None:
        V = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
        mids, F = {}, []

        def mid(i, j):
            k = (min(i, j), max(i, j))
            if k not in mids:
                q = [V[i][t] + V[j][t] for t in range(3)]
                L = math.sqrt(sum(w * w for w in q))
                V.append(tuple(w / L for w in q))
                mids[k] = len(V) - 1
            return mids[k]
        for sx in (0, 1):
            for sy in (2, 3):
                for sz in (4, 5):
                    ab, bc, ca = mid(sx, sy), mid(sy, sz), mid(sz, sx)
                    F += [(sx, ab, ca), (ab, sy, bc), (ca, bc, sz), (ab, bc, ca)]
        _OCTA = (V, F)
    return _OCTA


def round_stone(m, key, c, r, sz, yaw=0.0, tilt=(0.0, 0.0), jag=0.08, shade=None):
    """A big round stone: a once-split octahedron of radius r, sz of that
    high, its corners pulled about a little, flat under the ground,
    smooth shaded (a model may facet it). shade(nz) may give each face its
    own tone by how far it faces up, a lit top over a dark foot. Returns
    its faces."""
    from mathutils import Euler, Vector
    V, F = _octa()
    rot = Euler((tilt[0], tilt[1], yaw)).to_matrix()
    C = Vector(c)
    P = []
    for v in V:
        f = 1.0 + m.rng.uniform(-jag, jag)
        q = C + rot @ Vector((v[0] * r * f, v[1] * r * f, v[2] * r * sz * f))
        q.z = max(q.z, 0.0)
        P.append(q)
    made, fs = {}, []
    for t in F:
        a, b, d = (P[i] for i in t)
        n = (b - a).cross(d - a)
        if n.length < 1e-9:
            continue
        if n.dot((a + b + d) / 3 - C) < 0:
            t, n = t[::-1], -n
        k = shade(n.normalized().z) if shade else key
        bm = m.bm(k)
        vs = []
        for i in t:
            if (k, i) not in made:
                made[(k, i)] = bm.verts.new(P[i])
            vs.append(made[(k, i)])
        try:
            f = bm.faces.new(vs)
        except ValueError:
            continue
        f.smooth = True
        fs.append(f)
    return fs


def eroded(Z, cell, rad):
    """The heightfield Z sunk rad inside itself, square to its surface:
    the top of what a ball of that radius leaves when rolled under it."""
    import numpy as np
    ny, nx = Z.shape
    R = int(rad / cell)
    P = np.zeros((ny + 2 * R, nx + 2 * R))
    P[R:R + ny, R:R + nx] = Z
    out = np.full(Z.shape, np.inf)
    for dj in range(-R, R + 1):
        for di in range(-R, R + 1):
            d = math.hypot(di, dj) * cell
            if d > rad:
                continue
            out = np.minimum(out, P[R + dj:R + dj + ny, R + di:R + di + nx] - math.sqrt(rad * rad - d * d))
    return np.maximum(out, 0.0)


def stone_shell(m, tones, core, env, q, seed):
    """Big round stones laid over the plan of the envelope env: a ring of
    half-sunk stones on the outline, a row along the crest, then stones
    dropped lowest first, each resting on the core square to its slope and
    sunk a little into it, kept near the envelope and inside the drawing.
    The core is the envelope sunk q['core_in'] square to its surface, its
    crest rounded and its edge buried by the ring. Returns the faces."""
    import random
    import numpy as np
    rng = random.Random(seed)
    lo, hi = q.get("size", (0.32, 0.45))
    gapf = q.get("gapf", 0.85)
    placed = []
    why = {"fits": 0, "seen": 0, "cap": 0}
    x0, y0, cell = env.x0, env.y0, env.cell
    Zc = eroded(env.Z, cell, q.get("core_in", 0.55))
    # its crest rounded off, so stones can sit on it
    for _ in range(q.get("round", 3)):
        G = Zc.copy()
        G[1:-1, 1:-1] = 0.5 * Zc[1:-1, 1:-1] + 0.125 * (Zc[:-2, 1:-1] + Zc[2:, 1:-1] + Zc[1:-1, :-2] + Zc[1:-1, 2:])
        Zc = np.minimum(Zc, G)
    # the cap a stone's top may reach: the envelope's highest point under it
    R = int(q.get("reach_up", 0.3) / cell)
    P = np.zeros((Zc.shape[0] + 2 * R, Zc.shape[1] + 2 * R))
    P[R:-R, R:-R] = env.Z
    Zd = np.zeros(env.Z.shape)
    for dj in range(-R, R + 1):
        for di in range(-R, R + 1):
            if di * di + dj * dj <= R * R:
                Zd = np.maximum(Zd, P[R + dj:R + dj + Zd.shape[0], R + di:R + di + Zd.shape[1]])

    grid = env.grid
    faces = []
    Gy, Gx = np.gradient(Zc, cell)

    def top_of(x, y):
        # the highest stone surface over (x, y)
        h = 0.0
        for (D, s, sz) in placed:
            d2 = ((x - D[0]) ** 2 + (y - D[1]) ** 2) / (s * s)
            if d2 < 0.8:
                h = max(h, D[2] + s * sz * math.sqrt(1 - d2))
        return h

    def fits(C, r):
        for (D, s, _) in placed:
            if (C[0] - D[0]) ** 2 + (C[1] - D[1]) ** 2 + (C[2] - D[2]) ** 2 < (gapf * (r + s)) ** 2:
                why["fits"] += 1
                return False
        return True

    def seen(C, r, sz):
        x, y, z = C
        top = z + r * sz * 0.85
        e = q.get("edge", 0.75) * r
        pts = ((x, y, top), (x - e, y, z), (x + e, y, z), (x, y - e, max(0.0, z - 0.5 * r * sz)))
        ok = all(rubble.near_drawn(m, *p, px=q.get("px", 1)) for p in pts)
        if not ok:
            why["seen"] += 1
        return ok

    keys = tones.keys
    up, mid, down = q.get("lit", (1, 1, 2))

    def put(C, r, sz):
        key = tones.at(C[0], C[1], C[2] + r * sz * 0.6)
        i = keys.index(key)

        def shade(nz):
            # a lit top and a dark foot on every stone, as the drawing has them
            if nz > 0.8:
                return keys[min(q.get("topcap", len(keys) - 1), i + up)]
            if nz > 0.4:
                return keys[min(len(keys) - 1, i + mid)]
            if nz < q.get("foot", -0.15):
                return keys[max(0, i - down)]
            return keys[max(0, min(len(keys) - 1, i + q.get("side", 0)))]
        # built a little smaller than it is spaced, so dark gaps show round it
        faces.extend(round_stone(m, key, C, r * q.get("shrink", 1.0), sz, yaw=rng.uniform(0, math.pi),
                                 tilt=(rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2)), jag=q.get("jag", 0.08),
                                 shade=shade if q.get("lit") else None))
        placed.append((C, r, sz))
    # the ring on the outline, half sunk
    ring = env.rim()
    rng.shuffle(ring)
    for (Pt, n) in ring:
        done = False
        for r in (rng.uniform(lo, hi), lo * 0.85):
            sz = rng.uniform(0.8, 0.95)
            for k in (0.8, 1.05, 1.35):
                C = (Pt[0] - n[0] * k * r, Pt[1] - n[1] * k * r, r * sz * rng.uniform(0.05, 0.3))
                if fits(C, r) and seen(C, r, sz):
                    put(C, r, sz)
                    done = True
                    break
            if done:
                break
    nring = len(placed)
    sink = q.get("sink", 0.2)
    # a row of stones along the crest of the core, end to end
    (ax, ay), (bx, by) = env.a, env.b
    L = math.hypot(bx - ax, by - ay) or 1.0
    ux, uy = (bx - ax) / L, (by - ay) / L
    t = -env.reach
    while t < L + env.reach:
        best = None
        for w in range(-8, 9):
            x, y = ax + ux * t - uy * w * 0.05, ay + uy * t + ux * w * 0.05
            h = grid(Zc, x, y)
            if best is None or h > best[2]:
                best = (x, y, h)
        r = rng.uniform(lo, hi)
        sz = rng.uniform(0.8, 0.95)
        if best[2] > 0.3:
            C = (best[0], best[1], best[2] + (1 - 2 * sink) * r * sz)
            if C[2] + r * sz <= grid(Zd, best[0], best[1]) + q.get("cap", 0.08) and fits(C, r) and seen(C, r, sz):
                put(C, r, sz)
                t += 1.6 * r
                continue
        t += 0.1
    # the pile, dropped lowest first
    cand = [(x0 + i * cell, y0 + j * cell) for j in range(env.Z.shape[0]) for i in range(env.Z.shape[1])
            if env.Z[j, i] > 0.05]
    misses = 0
    while misses < q.get("misses", 6) and cand:
        best = None
        for _ in range(q.get("tries", 300)):
            x, y = rng.choice(cand)
            r = rng.uniform(lo, hi)
            sz = rng.uniform(0.8, 0.95)
            # each rests on the core square to its slope, sunk a little into it
            g = grid(Zc, x, y)
            nx_, ny_ = grid(Gx, x, y), grid(Gy, x, y)
            L = math.sqrt(nx_ * nx_ + ny_ * ny_ + 1.0)
            k = (1 - 2 * sink) * r
            C = (x - nx_ / L * k, y - ny_ / L * k, max(r * sz * 0.2, g + k * sz / L))
            if C[2] + r * sz > grid(Zd, x, y) + q.get("cap", 0.08):
                why["cap"] += 1
                continue
            if best is not None and C[2] >= best[0][2]:
                continue
            if fits(C, r) and seen(C, r, sz):
                best = (C, r, sz)
        if best is None:
            misses += 1
            continue
        misses = 0
        put(*best)
    # the stone the drawing crowns the column with, where it is drawn,
    # resting on what is under it
    for (col, row, r) in q.get("tops", []):
        sz = 0.9
        x, zc = m.X(col), r * sz
        for _ in range(8):
            y = m.Y(row, zc)
            zc = max(r * sz * 0.2, max(grid(Zc, x, y), top_of(x, y)) - sink * 2 * r * sz + r * sz)
        put((x, y, zc), r, sz)
    # the core, sunk square to the surface inside the stones
    st = q.get("core_step", 4)
    polys = []
    for j in range(0, Zc.shape[0] - st, st):
        for i in range(0, Zc.shape[1] - st, st):
            corners = ((j, i), (j, i + st), (j + st, i + st), (j + st, i))
            zs = [float(Zc[jj, ii]) for jj, ii in corners]
            if max(zs) < 0.01:
                continue
            polys.append([(x0 + ii * cell, y0 + jj * cell, zz) for (jj, ii), zz in zip(corners, zs)])
    m.faces(core, polys)
    print("WJ_SHELL", m.name, "ring", nring, "stones", len(placed), "core", len(polys), why, flush=True)
    # what lands on the heap now rests on the stones
    env.laid = lambda x, y: max(grid(Zc, x, y), top_of(x, y))
    return faces


def stack(m, tones, zf, near, box, n, size, seed, zmax=3.4, cell=0.1):
    """Boulders piled on one another over the core zf: each rests on the
    highest of the core and the boulders already under its middle, sunk a
    little into them, and none stands higher than the drawing shows."""
    import random
    rng = random.Random(seed)
    x0, x1, y0, y1 = box
    H, cap = {}, {}

    def key(x, y):
        return int((x - x0) / cell), int((y - y0) / cell)

    def ground(x, y, r):
        best = zf(x, y)
        i0, j0 = key(x - r, y - r)
        i1, j1 = key(x + r, y + r)
        for i in range(i0, i1 + 1):
            for j in range(j0, j1 + 1):
                if (i, j) in H and math.hypot(x0 + (i + 0.5) * cell - x, y0 + (j + 0.5) * cell - y) <= r:
                    best = max(best, H[(i, j)])
        return best

    def top(x, y):
        k = key(x, y)
        if k not in cap:
            cap[k] = rubble.top_allowed(m, x0 + (k[0] + 0.5) * cell, y0 + (k[1] + 0.5) * cell, zmax)
        return cap[k]
    placed = 0
    for _ in range(n * 40):
        if placed >= n:
            break
        x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
        if not near(x, y):
            continue
        s = rng.uniform(*size)
        sx, sy, sz = s * rng.uniform(0.9, 1.3), s * rng.uniform(0.8, 1.2), s * rng.uniform(0.65, 0.95)
        z = ground(x, y, 0.3 * min(sx, sy)) - 0.22 * sz
        if z + sz > top(x, y) - 0.05 or not rubble.drawn(m, x, y, z + 0.6 * sz):
            continue
        m.boulder(tones.at(x, y, z + 0.5 * sz), x, y, sx, sy, sz, z=max(0.0, z), yaw=rng.uniform(0, math.pi))
        R = 0.45 * min(sx, sy)
        i0, j0 = key(x - R, y - R)
        i1, j1 = key(x + R, y + R)
        for i in range(i0, i1 + 1):
            for j in range(j0, j1 + 1):
                d = math.hypot(x0 + (i + 0.5) * cell - x, y0 + (j + 0.5) * cell - y) / R
                if d <= 1:
                    H[(i, j)] = max(H.get((i, j), 0.0), z + sz * (0.95 - 0.45 * d * d))
        placed += 1
    return placed


def stump(m, x0, x1, yf, d, h):
    """A standing piece of the wall: big flat-faced blocks in two courses,
    the upper one broken short."""
    rng = m.rng
    m.col("wall", m.sample(20, 70, 50, 90, pick=lambda r, g, b: 45 < 0.3 * r + 0.59 * g + 0.11 * b < 140), 0.9)
    m.shade("wall2", "wall", 0.8, 0.9)
    m.box("dark", x0 + 0.06, x1 - 0.06, yf + 0.06, yf + d - 0.06, 0.0, h * 0.9)
    zc = h * 0.5
    for c, (z0, z1) in enumerate(((0.0, zc), (zc, h))):
        xs = [x0, x0 + (x1 - x0) * rng.uniform(0.35, 0.5), x1] if c == 0 else [x0, x0 + (x1 - x0) * rng.uniform(0.5, 0.65), x1]
        for i in range(len(xs) - 1):
            a, b = xs[i] + 0.025, xs[i + 1] - 0.025
            top = z1 - 0.02 if c == 0 else z1 - rng.uniform(0.0, 0.3)
            m.rock("wall" if i % 2 == 0 else "wall2", (a + b) / 2, yf + d / 2, b - a, d, top - z0 - 0.02,
                   z=z0 + 0.01, jag=0.06)


for _n, _r in T.items():
    kit.TABLES[_n] = T
    # too dark to tell body from cast shadow: placed by eye, not fitted
    kit.FITKEYS[_n] = []

    @kit.model(_n)
    def _b(m):
        p = kit.params(m.name, T)
        m.rng.seed(p["seed"])
        palette(m)
        {"tower": tower, "towerruin": tower_ruin, "wallruin": wall_ruin}[p["kind"]](m, p)
