"""Walled Veruna compounds: VerBuild03, 05 and 06 and their ruins.

Positions are read off each sprite; F.X(col), F.Y(row, z) turn a point of
the intact drawing into cells, shifted onto a ruin's drawing by m.frame.
"""
import math

import kit
from kit import model


def light(rgb):
    """Plaster or pale stone in the drawing, sooted or not: not char,
    timber or tile."""
    r, g, b = rgb
    v = max(r, g, b)
    if v < 0.36 or Model_roofish(rgb) or (v - min(r, g, b)) > 0.3:
        return False
    return not (r > g * 1.12 and g > b * 1.08 and v < 0.55)


def Model_roofish(rgb):
    return kit.Model.roofish(rgb)


def ring(pts, d):
    """A counter-clockwise outline moved d inward."""
    return [kit._inset(pts, i, d) for i in range(len(pts))]


def flat_holed(m, key, pts, z, keep, t=0.2, cell=0.4):
    """A flat roof over any outline, left open where keep() refuses."""
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    inside = lambda x, y, zz: kit._inside(pts, x, y) and keep(x, y, zz)  # noqa: E731
    m.grid_slab(key, [(min(xs), min(ys), z), (max(xs), min(ys), z), (max(xs), max(ys), z), (min(xs), max(ys), z)],
                t, inside, cell)


def rect(x0, x1, y0, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


# ---- VerBuild03: a tall flat-roofed tower, two gabled wings, a walled yard --

def b03_colours(m):
    # a five-storey tower: build it lower and deeper
    m.sturdy(0.84)
    m.col("tower", (100, 88, 76))
    m.col("flat", (245, 230, 200))
    m.col("parapet", (214, 200, 174))
    m.col("ptop", (247, 231, 201))
    m.col("roof", (130, 50, 28), paint=True)
    m.col("wing", (96, 90, 80))
    m.col("yard", (150, 140, 122))
    m.col("yardtop", (240, 226, 198))
    m.col("dark", (24, 20, 18))
    m.col("door", (70, 40, 26))
    m.col("sill", (225, 220, 205))
    m.col("wood", (80, 56, 38))


def b03_plan(F):
    H = 4.85
    # counter-clockwise from the north-west corner, the south-west corner cut
    tower = [F.P(66, 1, H + 0.4), F.P(66, 40, H + 0.4), F.P(90, 59, H + 0.4), F.P(158, 59, H + 0.4),
             F.P(158, 1, H + 0.4)]
    return dict(
        H=H, tower=tower,
        lw=(F.X(3), F.X(45), F.Y(190, 1.5), F.Y(80, 1.5)), le=1.5, lr=0.6,
        rw=(F.X(97), F.X(150), F.Y(190, 2.0), tower[3][1]), re=2.0, rr=0.7,
        wh=1.2, gh=1.75,
        north=[(tower[0][0], F.Y(36, 1.2)), (F.X(5), F.Y(36, 1.2))],
        yw=F.X(5), ys=F.Y(266, 1.2), ye=F.X(149), gx=(F.X(40), F.X(58), F.X(95), F.X(112)),
        door=(F.X(62), F.X(88)), stub=(F.X(45), F.Y(233, 1.2)),
        hatch=(F.X(138), F.X(151), F.Y(54, H), F.Y(41, H)), ltop=(F.X(157), F.Y(28, H + 1.3), H + 1.3),
        win=[(F.X(c), z, w, h) for c, z, w, h in ((96, 4.1, 0.3, 0.35), (120, 4.2, 0.3, 0.35), (142, 4.0, 0.3, 0.35),
                                                  (107, 3.0, 0.7, 0.5), (98, 2.2, 0.45, 0.6), (135, 2.4, 0.45, 0.6))],
        gwin=(F.X(25), F.X(122), F.X(12)),
    )


def b03_hatch(m, P):
    H = P["H"]
    x0, x1, y0, y1 = P["hatch"]
    m.box("dark", x0, x1, y0, y1, H - 0.02, H + 0.03)
    m.ladder("wood", ((x0 + x1) / 2 + 0.1, (y0 + y1) / 2 - 0.1, H - 0.4), P["ltop"], w=0.45)


def b03_tower(m, P):
    H = P["H"]
    m.flat("tower", "flat", P["tower"], H, par_h=0.4, par_t=0.25, cap="parapet")
    m.wall("ptop", ring(P["tower"], 0.125), 0.27, 0.04, z0=H + 0.4, closed=True)
    b03_hatch(m, P)
    b03_windows(m, P)


def b03_windows(m, P, upto=9.0):
    ys = P["tower"][3][1]
    for x, z, w, h in P["win"]:
        if z < upto:
            m.window("dark", x, ys, z, w, h, sill="sill" if w > 0.6 else None)


def b03_wings(m, P, keep=None, rafters=None, walls=True):
    for (x0, x1, y0, y1), ze, rise in ((P["lw"], P["le"], P["lr"]), (P["rw"], P["re"], P["rr"])):
        if keep is None:
            m.box("wing", x0, x1, y0, y1, 0, ze)
        elif walls:
            m.wall("wing", ring(rect(x0, x1, y0, y1), 0.15), 0.3, ze, closed=True)
        m.roof("roof", x0, x1, y0, y1, ze, rise, "y", ("gable", "gable"), over=0.18,
               core=None if keep else "wing", keep=keep, rafters=rafters)
    if walls:
        a, b, c = P["gwin"]
        m.window("dark", a, P["lw"][2], 0.55, 0.6, 0.4, sill="sill")
        m.window("dark", b, P["rw"][2], 0.9, 0.6, 0.4, sill="sill")
        m.window("dark", c, P["lw"][2], 0.0, 0.7, 1.1)


def b03_south(m, P, prof=None, jag=0.0):
    """The south wall rising over its gatehouse."""
    wh, gh = P["wh"], P["gh"]
    yw, ys, ye = P["yw"], P["ys"], P["ye"]
    g0, g1, g2, g3 = P["gx"]
    L = ye - yw
    if prof is None:
        prof = [(0, wh), ((g0 - yw) / L, wh), ((g1 - yw) / L, gh), ((g2 - yw) / L, gh), ((g3 - yw) / L, wh),
                (1, wh)]
    m.bwall("yard", [(yw, ys), (ye, ys)], 0.32, prof, frac=True, jag=jag, cap="yardtop")


def b03_yard(m, P):
    wh = P["wh"]
    lw, rw = P["lw"], P["rw"]
    yw, ys, ye = P["yw"], P["ys"], P["ye"]
    for pts in ([P["north"][0], P["north"][1], (yw, lw[3])], [(yw, lw[2]), (yw, ys)], [(ye, ys), (ye, rw[2])],
                [(P["stub"][0], lw[2]), P["stub"]]):
        m.wall("yard", pts, 0.3, wh, cap="yardtop", cap_h=0.06, cap_over=0.02)
    b03_south(m, P)
    d0, d1 = P["door"]
    m.box("door", d0, d1, ys - 0.22, ys - 0.15, 0.0, 1.25)


def b03_yard_broken(m, P, profs, jag=0.15):
    """The yard walls broken to the heights read off the ruin's drawing:
    profs holds (s, h) keys for the north run, the west-south-east run and
    the stub, s from 0 to 1 along each."""
    for pts, prof in zip(b03_runs(P), profs):
        m.bwall("yard", pts, 0.3, prof, frac=True, jag=jag, cap="yardtop")


def b03_runs(P):
    lw, rw = P["lw"], P["rw"]
    yw, ys, ye = P["yw"], P["ys"], P["ye"]
    return ([P["north"][0], P["north"][1], (yw, lw[3])], [(yw, lw[2]), (yw, ys), (ye, ys), (ye, rw[2])],
            [(P["stub"][0], lw[2]), P["stub"]])


@model("VerBuild03")
def verbuild03(m):
    b03_colours(m)
    P = b03_plan(m.frame())
    b03_tower(m, P)
    b03_wings(m, P)
    b03_yard(m, P)


# the pieces of a burnt heap: charred timber over the stone of the walls
BURNT = dict(char=0.45, stone=0.25, wood=0.2, tile=0.1)


def tower_roof(m, key, pts, z, field, t=0.2, cell=0.2):
    """A flat roof over an outline, cut smoothly where field <= 0."""
    inside = m.region(pts, rag=0.0)
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    box = [(min(xs), min(ys), z), (max(xs), min(ys), z), (max(xs), max(ys), z), (min(xs), max(ys), z)]
    m.cut_slab(key, box, t, m.fmin(field, inside), cell=cell, bottom=False)


def burnt_joists(m, key, pts, z, field, n=8, seed=0):
    """Charred joists fallen askew across the open parts of a floor or roof."""
    import random
    rng = random.Random("%s:j%d" % (m.name, seed))
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    placed = tries = 0
    while placed < n and tries < n * 40:
        tries += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not kit._inside(pts, x, y) or field(x, y, z) > -0.05:
            continue
        a = rng.uniform(0, math.pi)
        L = rng.uniform(1.2, 2.4)
        dz = rng.uniform(-0.9, -0.2)
        p0 = (x - math.cos(a) * L / 2, y - math.sin(a) * L / 2, z + dz)
        p1 = (x + math.cos(a) * L / 2, y + math.sin(a) * L / 2, z + dz - rng.uniform(0.2, 1.0))
        m.beam(key, p0, p1, rng.uniform(0.14, 0.2))
        placed += 1


def tangle(m, keys, pts, zlo, zhi, n, seed=0, L=(1.0, 2.2), w=(0.12, 0.2), pitch=(20, 45), where=None):
    """A heap of charred timbers inside an outline, each crossing the last
    and pitched 20 to 45 degrees; zlo and zhi (numbers or fn(x, y)) bound
    their mid heights."""
    import random
    rng = random.Random("%s:tg%s" % (m.name, seed))
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    a0 = rng.uniform(0, math.pi)
    placed = tries = 0
    while placed < n and tries < n * 40:
        tries += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not kit._inside(pts, x, y) or (where and not where(x, y)):
            continue
        yaw = a0 + (math.pi / 2 if placed % 2 else 0.0) + rng.uniform(-0.5, 0.5)
        p = math.radians(rng.uniform(*pitch)) * rng.choice((-1, 1))
        Lb = rng.uniform(*L)
        lo = zlo(x, y) if callable(zlo) else zlo
        hi = zhi(x, y) if callable(zhi) else zhi
        zc = rng.uniform(lo, max(lo, hi))
        dx, dy, dz = math.cos(yaw) * math.cos(p) * Lb / 2, math.sin(yaw) * math.cos(p) * Lb / 2, math.sin(p) * Lb / 2
        z0, z1 = zc - dz, zc + dz
        lift = max(0.0, 0.04 - min(z0, z1))
        m.beam(rng.choice(keys), (x - dx, y - dy, z0 + lift), (x + dx, y + dy, z1 + lift), rng.uniform(*w))
        placed += 1


def streaks(m, key, pts, t, prof, n, seed=0, jag=0.15, w=(0.12, 0.3), both=True):
    """Soot running up the faces of a wall built by bwall along pts with
    the same (fractional) profile: both faces, or one at random."""
    import random
    rng = random.Random("%s:st%s" % (m.name, seed))
    segs = [(pts[i], pts[i + 1]) for i in range(len(pts) - 1)]
    lens = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in segs]
    total = sum(lens)
    prof = sorted((s * total, h) for s, h in prof)

    def H(s):
        for (s0, h0), (s1, h1) in zip(prof, prof[1:]):
            if s0 <= s <= s1:
                if h0 <= 0 or h1 <= 0:
                    return 0.0
                return h0 + (h1 - h0) * (s - s0) / max(1e-6, s1 - s0)
        return prof[-1][1]
    for _ in range(n):
        s = rng.uniform(0.1, total - 0.1)
        h = H(s) - jag
        if h < 0.25:
            continue
        i, r = 0, s
        while i < len(lens) - 1 and r > lens[i]:
            r -= lens[i]
            i += 1
        (ax, ay), (bx, by) = segs[i]
        ux, uy = (bx - ax) / lens[i], (by - ay) / lens[i]
        x, y = ax + ux * r, ay + uy * r
        yaw = math.atan2(uy, ux)
        z0 = rng.uniform(0.0, 0.35) * h
        for sg in (-1, 1) if both else (rng.choice((-1, 1)),):
            d = t / 2 + 0.012
            m.obox(key, x - uy * d * sg, y + ux * d * sg, (z0 + h) / 2, rng.uniform(*w), 0.02, h - z0, yaw)


def sag_thatch(m, keys, path, w, z_out, z_in, t=0.22, over=0.15, step=1.6, sag=0.35, droop=None, rag=0.22,
               seed=0, du=0.25, nv=4, post="post"):
    """A thick thatch lean-to along path, its outer edge on the path at
    z_out and its inner edge w to the right at z_in, sagging between the
    posts under the inner edge; droop(u) sinks it u of the way along. Its
    edges are ragged and keys[1] colours the folds between the posts."""
    P = [kit.Vector(p) for p in path]
    outer = kit._offsets(P, over, False)
    inner = kit._offsets(P, -w, False)
    lens = [(P[i + 1] - P[i]).length for i in range(len(P) - 1)]
    total = sum(lens)
    nz, nz2 = m.noise("th%s" % seed, 0.5), m.noise("th2%s" % seed, 0.5)
    cols, acc = [], 0.0
    for i, L in enumerate(lens):
        n = max(1, int(L / du))
        for k in range(n + (1 if i == len(lens) - 1 else 0)):
            cols.append((acc + L * k / n, i, k / n))
        acc += L

    def z_at(sv, v):
        dip = math.sin(math.pi * (sv % step) / step) ** 1.5
        d = droop(sv / total) if droop else 0.0
        return z_in if v is None else z_out + (z_in - z_out) * v - d * (0.3 + 0.7 * v) - sag * dip * (0.25 + 0.75 * v)
    G, val = {}, {}
    for a, (sv, i, u) in enumerate(cols):
        o, q = outer[i].lerp(outer[i + 1], u), inner[i].lerp(inner[i + 1], u)
        fold = 0.04 if math.sin(math.pi * (sv % step) / step) < 0.75 else -0.04
        for b in range(nv + 1):
            v = b / nv
            p = o.lerp(q, v)
            g = kit.Vector((p.x, p.y, z_at(sv, v)))
            G[a, b] = g
            lo, hi = rag * (0.6 + 0.5 * nz(p.x, p.y)), 1 - rag * (0.6 + 0.5 * nz2(p.x, p.y))
            val[round(g.x, 4), round(g.y, 4)] = (min(v - lo, hi - v), fold)
    down = kit.Vector((0, 0, -t))
    for k, sg in ((keys[0], 1), (keys[1], -1)):
        def f(x, y, z=0.0, sg=sg):
            r, fd = val[round(x, 4), round(y, 4)]
            return min(r, fd * sg)
        m.cut_grid(k, G, len(cols) - 1, nv, f, lambda p: down)
    # the posts under the inner edge
    for k in range(int(total / step + 1e-6) + 1):
        sv = k * step
        i, r = 0, sv
        while i < len(lens) - 1 and r > lens[i]:
            r -= lens[i]
            i += 1
        u = min(1.0, r / lens[i])
        c = P[i].lerp(P[i + 1], u)
        q = inner[i].lerp(inner[i + 1], u)
        pp = c + (q - c) * 0.88
        top = z_at(sv, 0.9) - t
        d = droop(sv / total) if droop else 0.0
        m.post(post, pp.x, pp.y, 0.0, top, 0.15, lean=0.12 * d)


def torn_thatch(m, keys, path, w, z_out, z_in, t=0.24, over=0.15, step=1.6, sag=0.4, droop=None, rag=0.22,
                tear=None, seed=0, du=0.25, nv=5, post="post", broken=1, strands=36):
    """A torn thatch lean-to as one sheet along path: its outer edge on the
    path at z_out, its inner edge w to the right at z_in on posts. Sag and
    tone wander by noise, keys (pale, mid, dark) in patches; tear(x, y, z)
    < 0 holes it over a dark flap, straw hangs off the outer edge and post
    number broken has snapped and leans, the sheet slumping over it."""
    import random
    rng = random.Random("%s:tt%s" % (m.name, seed))
    P = [kit.Vector(p) for p in path]
    outer = kit._offsets(P, over, False)
    inner = kit._offsets(P, -w, False)
    lens = [(P[i + 1] - P[i]).length for i in range(len(P) - 1)]
    total = sum(lens)
    e1, e2 = m.noise("te%s" % seed, 0.4), m.noise("te2%s" % seed, 0.4)
    slow, rip = m.noise("ts%s" % seed, 1.2), m.noise("tr%s" % seed, 0.25)
    tone, tone2 = m.noise("tt%s" % seed, 0.45), m.noise("tt2%s" % seed, 0.2)
    posts = [k * step for k in range(int(total / step + 1e-6) + 1)]
    sb = posts[broken] if broken is not None and broken < len(posts) else None

    def z_at(sv, v, x, y):
        d = droop(sv / total) if droop else 0.0
        bay = 0.35 * math.sin(math.pi * (sv % step) / step) ** 1.5
        s = sag * (0.45 + 0.35 * slow(x, y) + bay)
        slump = 0.45 * math.exp(-((sv - sb) / 0.9) ** 2) if sb is not None else 0.0
        return (z_out + (z_in - z_out) * v - d * (0.3 + 0.7 * v) - s * (0.25 + 0.75 * v) - slump * v
                + 0.05 * rip(x, y))

    def at(sv, v):
        i, r = 0, sv
        while i < len(lens) - 1 and r > lens[i]:
            r -= lens[i]
            i += 1
        u = max(0.0, min(1.0, r / lens[i]))
        o, q = outer[i].lerp(outer[i + 1], u), inner[i].lerp(inner[i + 1], u)
        p = o.lerp(q, v)
        d = (q - o).normalized()
        return kit.Vector((p.x, p.y, z_at(sv, v, p.x, p.y))), kit.Vector((d.x, d.y, 0.0))
    cols = []
    for i, L in enumerate(lens):
        n = max(1, int(L / du))
        cols += [sum(lens[:i]) + L * k / n for k in range(n + (1 if i == len(lens) - 1 else 0))]
    G, val = {}, {}
    for a, sv in enumerate(cols):
        for b in range(nv + 1):
            v = b / nv
            g, _ = at(sv, v)
            G[a, b] = g
            lo = rag * (0.5 + 0.9 * max(0.0, e1(g.x, g.y)))
            hi = 1 - rag * (0.6 + 0.5 * e2(g.x, g.y))
            edge = min(v - lo, hi - v)
            if tear is not None:
                edge = min(edge, tear(g.x, g.y, g.z))
            val[round(g.x, 4), round(g.y, 4)] = (edge, 0.75 * tone(g.x, g.y) + 0.25 * tone2(g.x, g.y))
    down = kit.Vector((0, 0, -t))
    bands = ((keys[0], lambda n: n - 0.12), (keys[1], lambda n: min(0.12 - n, n + 0.12)),
             (keys[2], lambda n: -0.12 - n))
    for k, band in bands:
        def f(x, y, z=0.0, band=band):
            e, n = val[round(x, 4), round(y, 4)]
            return min(e, band(n))
        m.cut_grid(k, G, len(cols) - 1, nv, f, lambda p: down)
    # the posts under the inner edge, one snapped and leaning, its top in the straw below
    for k, sv in enumerate(posts):
        c, _ = at(sv, 0.0)
        q, _ = at(sv, 1.0)
        pp = c.lerp(q, 0.88)
        top = z_at(sv, 0.9, pp.x, pp.y) - t
        d = droop(sv / total) if droop else 0.0
        if k == broken:
            m.post(post, pp.x, pp.y, 0.0, top * 0.55, 0.15, lean=0.42)
            m.beam(post, (pp.x + 0.2, pp.y - 0.35, 0.1), (pp.x + 0.5, pp.y + 0.3, 0.12), 0.14)
        else:
            m.post(post, pp.x, pp.y, 0.0, top, 0.15, lean=0.12 * d)
    # straw hanging off the frayed outer edge
    for _ in range(strands):
        sv = rng.uniform(0.05, total - 0.05)
        p, inw = at(sv, 0.0)
        p, inw = at(sv, rag * (0.5 + 0.9 * max(0.0, e1(p.x, p.y))) + 0.04)
        if tear is not None and tear(p.x, p.y, p.z) < 0.05:
            continue
        L = rng.uniform(0.3, 0.65)
        out = rng.uniform(0.08, 0.25)
        q = p - inw * out - kit.Vector((0, 0, L))
        m.beam(rng.choice((keys[0], keys[0], keys[1])), (p.x, p.y, p.z - t * 0.5), (q.x, q.y, max(0.05, q.z)),
               rng.uniform(0.08, 0.14), 0.04)
    return at


def eaved_ruin(m, wall, x0, x1, y0, y1, ze, rise, field, keys, dz, over=0.18, band=0.35, spacing=0.7, eave="eave",
               breach=None):
    """A gabled block with its roof holed where field <= 0, the rim of the
    roof a plain tiled band (eave) so no painted wall shows on it, bare
    rafters only in the holes and the fallen roof under them. breach lists
    (s, h) keys, s in cells from the south-west corner, where the south
    wall broke down."""
    box = rect(x0, x1, y0, y1)
    if breach:
        per = 2 * (x1 - x0 + y1 - y0) - 1.2
        m.bwall(wall, ring(box, 0.15), 0.3, [(0, ze)] + breach + [(per, ze)], closed=True, jag=0.03)
    else:
        m.wall(wall, ring(box, 0.15), 0.3, ze, closed=True)
    m.gables(wall, x0, x1, y0, y1, ze, rise, "x", t=0.24)
    ex0, ex1, ey0, ey1 = x0 - over, x1 + over, y0 - over, y1 + over

    def rim(x, y, z=0.0):
        return band - min(x - ex0, ex1 - x, y - ey0, ey1 - y)
    ends = ("gable", "gable")
    m.cut_roof("roof", x0, x1, y0, y1, ze, rise, "x", ends, over=over, field=m.fmin(field, lambda x, y, z=0.0: -rim(x, y)))
    m.cut_roof(eave, x0, x1, y0, y1, ze, rise, "x", ends, over=over, field=m.fmin(field, rim))
    for f in kit._roof_faces(x0, x1, y0, y1, ze, rise, ends, over, 0.12)[0]:
        m.open_rafters("char", f, field, spacing=spacing, w=0.12)
    m.fallen(keys, ring(box, 0.3), dz=dz, cell=0.4)


def ruin_tones(m, cap, face, soot, char=(40, 32, 26), tile=(118, 50, 34), wood=(86, 56, 38), stone=None,
               ash=(40, 34, 28), soot_share=0.4):
    """The sooted walls and the rubble materials of a burnt ruin."""
    m.col("wall", face)
    m.col("wtop", cap)
    m.col("wsoot", soot)
    m.soot["wtop"] = ("wsoot", soot_share)
    m.paint_gain = 1.15
    return m.rubble_tones(char=char, tile=tile, wood=wood, stone=stone or tuple(v * 0.8 for v in cap), ash=ash)


@model("VerBuild03a")
def verbuild03a(m):
    b03_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (140, 128, 108), (96, 88, 76), (92, 84, 72))
    m.col("tower", (78, 70, 62))
    m.col("towertop", (118, 108, 92))
    m.col("flat", (118, 106, 90), paint=True)
    m.col("char", (32, 26, 22))
    P = b03_plan(m.frame(17, -1))
    H = P["H"]
    Hf = H + 0.4
    tw = ring(P["tower"], 0.18)
    # the tower stands on the west, east and north; its south front broke in
    # over a heap and its south-west corner fell; the north-east corner is down a storey
    m.bwall("tower", tw + tw[:1], 0.36,
            [(0, Hf), (0.12, Hf), (0.15, Hf - 0.9), (0.19, 2.3), (0.24, 1.7), (0.27, 2.5), (0.31, 2.9), (0.36, 2.3),
             (0.41, 3.0), (0.45, 3.7), (0.47, Hf - 0.3), (0.5, Hf), (0.68, Hf), (0.7, Hf - 0.7), (0.73, Hf - 1.7),
             (0.76, Hf - 1.1), (0.79, Hf), (1, Hf)],
            frac=True, jag=0.28, step=0.25, cap="towertop")
    grey = m.pic_field(lambda c: max(c) > 0.3 and max(c) - min(c) < 0.2, sigma=3.0, bias=0.4)
    inner = ring(P["tower"], 0.3)
    tower_roof(m, "flat", inner, H, grey)
    burnt_joists(m, "char", inner, H, grey, n=9)
    # the floors and roof that fell, heaped in the tower
    cx = sum(p[0] for p in inner) / len(inner)
    cy = sum(p[1] for p in inner) / len(inner)
    m.debris(T, inner, 3.0, zfn=lambda x, y: 2.6 - 0.35 * math.hypot(x - cx, (y - cy) * 1.3), density=2.4,
             maxn=150, cell=0.45, mix=dict(char=0.45, stone=0.3, wood=0.15, tile=0.1))
    b03_windows(m, P, upto=2.0)
    # the wings keep their walls, their roofs are holed where the drawing shows it
    roof = m.pic_field(m.roofish, sigma=3.0, bias=0.3)
    for i, ((x0, x1, y0, y1), ze, rise) in enumerate(((P["lw"], P["le"], P["lr"]), (P["rw"], P["re"], P["rr"]))):
        m.wall("wing", ring(rect(x0, x1, y0, y1), 0.15), 0.3, ze, closed=True)
        m.gables("wing", x0, x1, y0, y1, ze, rise, "y", t=0.25)
        m.cut_roof("roof", x0, x1, y0, y1, ze, rise, "y", ("gable", "gable"), over=0.18, field=roof,
                   rafters=None if i == 0 else "char", spacing=0.75, rafter_w=0.12, cell=0.22 if i == 0 else 0.15)
        if i == 1:
            m.fallen(keys, rect(x0 + 0.2, x1 - 0.2, y0 + 0.2, y1 - 0.2), dz=0.6, n=14)
            continue
        # the west hall's open bay is heaped with its charred roof timbers and tiles
        inner = rect(x0 + 0.3, x1 - 0.3, y0 + 0.3, y1 - 0.3)
        opened = lambda x, y: roof(x, y, m.roof_z(x, y) or ze) <= 0.12  # noqa: E731
        m.col("b_ash", (17, 14, 12), rough=1.0)
        m.col("b_ash2", (13, 11, 9))
        m.col("b_ash3", (24, 20, 16))
        m.col("coal", (15, 13, 11), rough=1.0)
        Tb = dict(T, ash=("b_ash", "b_ash2", "b_ash3"), char=("coal", "char"))
        m.debris(Tb, inner, ze, lo=0.5, hi=0.7, density=2.2, maxn=40, cell=0.6, seed=11, where=opened,
                 bed_where=lambda x, y: True, mix=dict(char=0.55, tile=0.3, stone=0.05, wood=0.1), size=(0.2, 0.4),
                 beam_len=(0.8, 1.8))
        tangle(m, ("coal", "coal", "char", "r_wood"), inner, 0.8, 1.15, 20, seed=1, L=(1.0, 2.0), where=opened)
        # two rafters are left over it, broken and sagging into the heap
        xc = (x0 + x1) / 2
        ya, yb = m.Y(135, 1.8), m.Y(172, 1.8)
        m.beam("char", (x0 + 0.05, ya, ze - 0.05), (xc - 0.2, ya - 0.1, ze + rise * 0.35), 0.13)
        m.beam("char", (xc - 0.2, ya - 0.1, ze + rise * 0.35), (xc + 0.5, ya - 0.3, 0.95), 0.13)
        m.beam("char", (x1 - 0.05, yb, ze - 0.05), (xc + 0.3, yb + 0.15, ze - 0.15), 0.13)
    a, b, c = P["gwin"]
    m.window("dark", a, P["lw"][2], 0.55, 0.6, 0.4, sill="sill")
    m.window("dark", b, P["rw"][2], 0.9, 0.6, 0.4, sill="sill")
    # the yard walls hold, sooted and chipped, broken open at the south-east corner
    m.col("yard", (138, 132, 110))
    m.col("yardtop", (158, 152, 128))
    m.col("yardsoot", (96, 90, 76))
    m.col("streak", (66, 62, 54))
    m.soot["yardtop"] = ("yardsoot", 0.35)
    runs = b03_runs(P)
    L = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(runs[1], runs[1][1:])]
    fa = (L[0] + m.X(141) - P["yw"]) / sum(L)
    fb = (L[0] + L[1] + 0.5) / sum(L)
    south = [(0, 1.2), (0.2, 1.0), (0.25, 1.2), (0.4, 1.1), (0.5, 0.9), (0.6, 1.1), (0.75, 1.2), (0.85, 1.0),
             (0.9, 1.1), (1, 1.2)]
    # the raised, curved gate section still stands over the burst gate, chipped
    fx = lambda c: (L[0] + m.X(c) - P["yw"]) / sum(L)  # noqa: E731
    g0, g1, g2, g3 = fx(50), fx(64), fx(124), fx(138)
    gate = [(g0, 1.15), (g0 + (g1 - g0) * 0.5, 1.5), (g1, 1.72), (fx(80), 1.62), (fx(92), 1.75), (fx(104), 1.55),
            (fx(114), 1.7), (g2, 1.66), (g2 + (g3 - g2) * 0.5, 1.45), (g3, 1.15)]
    south = [p for p in south if not g0 - 0.01 < p[0] < g3 + 0.01] + gate
    south = [p for p in south if not fa - 0.03 < p[0] < fb + 0.03] + [(fa - 0.02, 0.8), (fa, 0), (fb, 0),
                                                                      (fb + 0.02, 0.7)]
    profs = ([(0, 1.2), (0.25, 1.1), (0.33, 0.8), (0.45, 1.0), (1, 1.2)], south, [(0, 1.2), (0.6, 1.0), (1, 0.8)])
    b03_yard_broken(m, P, profs, jag=0.12)
    for k, (pts, prof) in enumerate(zip(runs, profs)):
        streaks(m, "streak", pts, 0.3, prof, (3, 7, 1)[k], seed=k, both=False)
    # rubble at the foot of the break, both sides
    xb, ys = m.X(148), P["ys"]
    m.debris(T, rect(xb - 1.1, P["ye"] + 0.6, ys - 0.8, ys + 0.9), 0.5, zfn=lambda x, y: 0.12, bed=False,
             density=6.0, maxn=12, seed=12, mix=dict(stone=0.6, char=0.2, tile=0.1, wood=0.1), size=(0.2, 0.45))
    b03a_gate(m, ys)
    m.scatter(keys, max_area=260, min_area=5, skip=[(55, 265, 88, 299)])


def b03a_gate(m, ys):
    """The burst gate: its charred planks stand tilted just inside the gate
    section, tied by one crossbar, and one plank lies out on the ground.
    Tops are read off the drawing (render rows over 0.894, the ortho's squeeze)."""
    m.col("gplank", (95, 60, 48))
    m.col("gplank2", (80, 50, 40))
    m.col("gbar", (66, 34, 28))
    zk = 0.84
    true = lambda r: m.hy + (r - m.hy) / 0.894  # noqa: E731
    tops = {}
    # (foot col, top col, drawn top row, foot north of the wall, standing height)
    for k, (cf, ct, row, d, z) in enumerate(((75, 76, 232, 1.15, 1.2), (83, 85, 229, 1.2, 1.25),
                                              (94.5, 95, 238, 0.7, 1.1), (114.5, 114, 242, 0.55, 1.0))):
        foot = (m.X(cf), ys + d, 0.0)
        top = (m.X(ct), m.Y(true(row), zk * z), z)
        m.beam("gplank" if k != 1 else "gplank2", foot, top, 0.3, 0.12)
        tops[k] = (foot, top)

    def at(k, u):
        (fx, fy, fz), (tx, ty, tz) = tops[k]
        return (fx + (tx - fx) * u, fy + (ty - fy) * u - 0.1, fz + (tz - fz) * u)
    m.beam("gbar", at(0, 0.62), at(2, 0.78), 0.08, 0.06)
    # a charred jamb of the gateway left in the wall face
    m.window("dark", m.X(109.5), ys - 0.16, 0.0, 0.45, 1.05)
    # the plank thrown out onto the ground
    m.beam("gplank", (m.X(78), m.Y(true(273)), 0.12), (m.X(65), m.Y(true(294)), 0.07), 0.3, 0.12)


@model("VerBuild03b")
def verbuild03b(m):
    b03_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (150, 135, 114), (88, 80, 70), (96, 86, 74), stone=(112, 100, 84))
    m.col("yard", (100, 92, 80))
    m.col("yardtop", (150, 135, 114))
    m.soot["yardtop"] = ("wsoot", 0.35)
    m.col("tile_a", (108, 46, 32))
    m.col("tile_b", (84, 38, 28))
    P = b03_plan(m.frame(17, -19))
    tw = ring(P["tower"], 0.18)
    # only the tower's lower storey stands, full of its own floors
    m.bwall("wall", tw + tw[:1], 0.36,
            [(0, 2.6), (0.15, 2.0), (0.25, 1.4), (0.35, 2.2), (0.5, 2.8), (0.65, 2.3), (0.8, 3.0), (1, 2.6)],
            frac=True, jag=0.35, cap="wtop")
    m.debris(T, ring(P["tower"], 0.36), 2.6, lo=0.45, hi=0.75, bank=1.2, density=3.4, maxn=220, cell=0.5,
             mix=BURNT, size=(0.2, 0.5))
    for (x0, x1, y0, y1), ze, seed in ((P["lw"], 1.4, 3), (P["rw"], 1.6, 4)):
        box = ring(rect(x0, x1, y0, y1), 0.15)
        # the south wall went down under the roof that slid over it
        m.bwall("wall", box + box[:1], 0.3, [(0, 0.45), (0.12, 0.55), (0.15, ze * 0.8), (0.3, ze * 0.7), (0.55, ze),
                                              (0.8, ze * 0.8), (1, 0.5)],
                frac=True, jag=0.3, cap="wtop")
        # a length of the tiled roof slid down at the hall's south end, the heap behind it
        xc = (x0 + x1) / 2
        ys = y0 + 0.3
        m.debris(T, ring(rect(x0, x1, y0, y1), 0.3), ze, lo=0.35, hi=0.65, density=4.0, maxn=170, cell=0.5,
                 seed=seed, size=(0.2, 0.45), mix=BURNT, where=lambda x, y: y > ys + 0.9)
        m.tile_slab(("tile_a", "tile_b"), xc, ys, 0.55, x1 - x0 + 0.3, 2.3, 0.05, 0.08, 0.3, seed=seed)
    b03_yard_broken(m, P, (
        [(0, 0.9), (0.4, 0.6), (0.6, 0.9), (1, 0.8)],
        [(0, 0.8), (0.25, 0.9), (0.35, 0.6), (0.42, 0), (0.5, 0), (0.52, 0.5), (0.62, 0.8), (0.75, 0.9),
         (0.85, 0.4), (0.88, 0), (0.93, 0), (0.95, 0.6), (1, 0.7)],
        [(0, 0.9), (0.5, 0.5), (0.6, 0), (1, 0)]), jag=0.25)
    m.scatter(keys, max_area=260)


# ---- VerBuild05: a two-storey gabled house, a low wing, a thatched yard ----

def b05_colours(m):
    # two storeys under a steep roof: build it lower and deeper
    m.sturdy(0.87)
    m.col("roof", (127, 49, 24), paint=True)
    m.col("house", (104, 95, 82))
    m.col("wing", (140, 131, 112))
    m.col("thatch", (214, 172, 100))
    m.col("post", (60, 44, 30))
    m.col("yard", (170, 160, 140))
    m.col("yardtop", (220, 208, 184))
    m.col("dark", (24, 20, 18))
    m.col("sill", (225, 220, 205))
    m.col("ledge", (190, 180, 160))
    m.col("shutter", (96, 66, 44))
    m.col("barrel", (78, 58, 38))
    m.col("band", (50, 42, 34))


def b05_plan(F):
    return dict(
        main=(F.X(49), F.X(150), F.Y(77), F.Y(77) + 3.0), me=3.0, mr=1.2,
        wing=(F.X(116), F.X(171), F.Y(118), F.Y(118) + 2.5), we=1.6, wr=0.9,
        # the thatched lean-to runs up the west side of the yard to the house
        west=[F.P(4, 128, 1.1), F.P(4, 80, 1.1), F.P(48, 38, 1.1)],
        east=[F.P(169, 36, 1.3), F.P(169, 72, 1.3)],
        front=[F.P(28, 119, 1.1), F.P(75, 105, 1.1), F.P(117, 103, 1.1)],
        door=F.P(50, 118, 1.1), mdoor=F.X(84),
        barrels=[F.P(76, 116, 0.35), F.P(89, 114, 0.35), F.P(83, 123, 0.35)],
        up=[F.X(c) for c in (63, 83, 104)], low=[(F.X(68), True), (F.X(98), False)],
    )


def b05_house(m, P, keep=None, rafters=None):
    x0, x1, y0, y1 = P["main"]
    ze, zr = P["me"], P["mr"]
    if keep is None:
        m.box("house", x0, x1, y0, y1, 0, ze)
    else:
        m.wall("house", ring(rect(x0, x1, y0, y1), 0.15), 0.3, ze, closed=True)
    m.roof("roof", x0, x1, y0, y1, ze, zr, "x", ("gable", "gable"), over=0.2,
           core=None if keep else "house", keep=keep, rafters=rafters)
    # two storeys of windows, a string course between them, the door
    for x in P["up"]:
        m.window("dark", x, y0, 2.15, 0.55, 0.4, sill="sill")
    for x, shut in P["low"]:
        m.window("dark", x, y0, 0.55, 0.8, 0.5, sill=None if shut else "sill")
        if shut:
            m.obox("shutter", x, y0 - 0.09, 0.8, 0.85, 0.04, 0.45)
    m.box("ledge", x0, x1, y0 - 0.12, y0 + 0.01, 1.9, 2.0)
    m.window("dark", P["mdoor"], y0, 0.0, 0.55, 1.1)
    m.box("house", P["mdoor"] - 0.4, P["mdoor"] + 0.4, y0 - 0.45, y0, 0.0, 0.15)


def b05_wing(m, P, keep=None, rafters=None):
    x0, x1, y0, y1 = P["wing"]
    if keep is None:
        m.box("wing", x0, x1, y0, y1, 0, P["we"])
    else:
        m.wall("wing", ring(rect(x0, x1, y0, y1), 0.15), 0.3, P["we"], closed=True)
    m.roof("roof", x0, x1, y0, y1, P["we"], P["wr"], "x", ("gable", "gable"), over=0.18,
           core=None if keep else "wing", keep=keep, rafters=rafters)
    m.window("dark", m.X(153) if False else x1 - 0.9, y0, 0.5, 0.35, 0.3)


def b05_yard(m, P, keep=None, sag=0.0, front=None):
    m.leanto("thatch", P["west"], 1.3, 1.1, 1.55, post="post", wall="yard", keep=keep, sag=sag)
    m.leanto("thatch", P["east"], 1.0, 1.3, 1.7, post="post", keep=keep, sag=sag)
    if front is None:
        m.wall("yard", P["front"], 0.3, 1.1, cap="yardtop", cap_h=0.06, cap_over=0.02)
        dx, dy = P["door"]
        (ax, ay), (bx, by) = P["front"][0], P["front"][1]
        import math
        yaw = math.atan2(by - ay, bx - ax)
        m.obox("dark", dx, dy, 0.5, 0.7, 0.34, 1.0, yaw)
    else:
        m.bwall("yard", P["front"], 0.3, front, frac=True, jag=0.2, cap="yardtop")


@model("VerBuild05")
def verbuild05(m):
    b05_colours(m)
    P = b05_plan(m.frame())
    b05_house(m, P)
    b05_wing(m, P)
    b05_yard(m, P)
    for x, y in P["barrels"]:
        m.barrel("barrel", x, y, 0.3, 0.62, band="band")


def b05_windows(m, P):
    x0, x1, y0, y1 = P["main"]
    for x in P["up"]:
        m.window("dark", x, y0, 2.15, 0.55, 0.4, sill="sill")
    for x, shut in P["low"]:
        m.window("dark", x, y0, 0.55, 0.8, 0.5, sill=None if shut else "sill")
    m.box("ledge", x0, x1, y0 - 0.12, y0 + 0.01, 1.9, 2.0)
    m.window("dark", P["mdoor"], y0, 0.0, 0.55, 1.1)


@model("VerBuild05a")
def verbuild05a(m):
    b05_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (167, 152, 127), (132, 120, 100), (120, 110, 92), stone=(150, 138, 116))
    P = b05_plan(m.frame(5, 5))
    m.col("house", m.sample(60, 62, 120, 78))
    m.col("wing", (112, 102, 88))
    m.col("thatch", (197, 167, 103))
    m.col("thatch2", (170, 142, 88))
    m.col("yard", (140, 128, 108))
    m.col("yardtop", (167, 152, 127))
    m.col("char", (34, 28, 23))
    # the eaves hold: the roofs are holed only where drawn, at the top right
    # and the centre-south of the house and the lower middle of the wing
    # the centre collapse runs down through the eave and the front wall
    roof = m.blobs_px([(140, 18, 4.0, 0.8, 0.65), (93, 46, 3.35, 0.6, 0.72), (138, 101, 1.9, 0.72, 0.42),
                       (101, 60, 3.0, 1.0, 0.5)], rag=0.3, seed=5)
    sa, sb = m.X(85) - P["main"][0] - 0.15, m.X(118) - P["main"][0] - 0.15
    breach = [(sa - 0.05, 3.0), (sa + 0.1, 2.3), (sa + 0.3, 1.5), (sa + 0.6, 1.0), (sa + 1.0, 0.7), (sa + 1.4, 1.1),
              (sa + 1.7, 0.8), (sb - 0.3, 1.3), (sb - 0.12, 2.2), (sb, 3.0)]
    m.col("eave", (98, 40, 24))
    T2 = dict(T, char=("r_char", "char"))
    for k, ze, zr, dz in (("main", P["me"], P["mr"], 0.9), ("wing", P["we"], P["wr"], 0.6)):
        x0, x1, y0, y1 = P[k]
        eaved_ruin(m, "house" if k == "main" else "wing", x0, x1, y0, y1, ze, zr, roof, keys, dz,
                   breach=breach if k == "main" else None)
        inside = rect(x0 + 0.3, x1 - 0.3, y0 + 0.3, y1 - 0.3)
        rz = lambda x, y, ze=ze: m.roof_z(x, y) or ze  # noqa: E731
        tangle(m, T2["char"] + ("r_wood", "r_wood"), inside, lambda x, y: rz(x, y) - 1.1, lambda x, y: rz(x, y) - 0.3, 14,
               seed=k, L=(0.9, 1.8), where=lambda x, y: roof(x, y, rz(x, y)) < 0.15)
    b05a_front(m, P, T, (m.X(85), m.X(118)))
    m.window("dark", P["wing"][1] - 0.9, P["wing"][2], 0.5, 0.35, 0.3)
    # the west lean-to still stands, one torn sheet of sooty thatch in
    # patches, a big hole near its top, straw hanging off it, one post snapped
    m.col("th_pale", (208, 178, 114))
    m.col("thatch", (126, 104, 60))
    m.col("thatch_dk", (44, 35, 21))
    m.col("tear_dk", (30, 25, 18), rough=1.0)
    tear = m.blobs_px([(45, 62, 1.35, 0.5, 0.4)], rag=0.45, seed=9)
    torn_thatch(m, ("th_pale", "thatch", "thatch_dk"), P["west"], 1.6, 1.1, 1.6, sag=0.4, t=0.25,
                droop=lambda u: 0.15 + 0.9 * max(0.0, 0.45 - u) / 0.45, tear=tear, broken=1, strands=36)
    # under the hole: the torn flap hanging dark and two charred rafters across it
    cx, cy = m.X(45), m.Y(62, 1.35)
    r = kit.Vector((0.69, -0.72, 0.0))
    m.shard("tear_dk", cx, cy, 0.75, 0.95, 0.8, yaw=0.8, pitch=0.45, t=0.08, rag=0.3, seed=3)
    for off in (-0.2, 0.22):
        a, b = (cx - r.x * 0.55 + 0.72 * off, cy - r.y * 0.55 + 0.69 * off), (cx + r.x * 0.55 + 0.72 * off,
                                                                                cy + r.y * 0.55 + 0.69 * off)
        m.beam("r_char", (a[0], a[1], 1.1), (b[0], b[1], 1.42), 0.11)
    m.wall("yard", P["west"], 0.25, 0.9)
    m.leanto("thatch2", P["east"], 1.0, 1.3, 1.7, post="post", droop=lambda u: 0.1 + 0.25 * u)
    # the thick grey rubble yard wall, broken where drawn, and the rubble spilled over the yard
    m.col("fwall", (100, 90, 75))
    m.col("fwtop", (110, 99, 82))
    m.bwall("fwall", P["front"], 0.5, [(0, 1.0), (0.2, 0.7), (0.3, 0.35), (0.42, 0.4), (0.5, 0.8), (0.75, 1.0),
                                       (0.85, 0.5), (1, 0.9)], frac=True, jag=0.3, step=0.2, cap="fwtop")
    (ax, ay), (bx, by) = P["front"][0], P["front"][-1]
    yard = [(ax + 0.6, ay + 0.3), (bx, by + 0.3), (bx, P["main"][2] - 0.2), (ax + 1.5, P["main"][2] - 0.2)]
    m.debris(T, yard, 0.4, zfn=lambda x, y: 0.08, bed=False, density=1.3, maxn=45,
             mix=dict(stone=0.45, tile=0.3, char=0.15, wood=0.1), size=(0.2, 0.45), beam_len=(0.5, 1.2))
    m.scatter(keys, max_area=200)


def b05a_front(m, P, T, gap):
    """The house front with its windows, ledge and door, left out where the
    centre collapse broke the wall between gap[0] and gap[1], and the
    timbers and plaster spilled out of the breach into the yard."""
    x0, x1, y0, y1 = P["main"]
    g0, g1 = gap
    out = lambda x: g0 - 0.3 < x < g1 + 0.3  # noqa: E731
    for x in P["up"]:
        if not out(x):
            m.window("dark", x, y0, 2.15, 0.55, 0.4, sill="sill")
    for x, shut in P["low"]:
        if not out(x):
            m.window("dark", x, y0, 0.55, 0.8, 0.5, sill=None if shut else "sill")
    m.box("ledge", x0, g0 - 0.05, y0 - 0.12, y0 + 0.01, 1.9, 2.0)
    m.box("ledge", g1 + 0.05, x1, y0 - 0.12, y0 + 0.01, 1.9, 2.0)
    if not out(P["mdoor"]):
        m.window("dark", P["mdoor"], y0, 0.0, 0.55, 1.1)
    ys = y0 - 1.4
    heap = rect(g0 - 0.1, g1 + 0.1, ys, y0 + 0.7)
    Tp = dict(T, ash=("r_stone2", "r_ash3", "r_stone2"), stone=("r_stone", "r_stone3", "r_stone3"))
    m.debris(Tp, heap, 0.9, zfn=lambda x, y: 0.08 + 0.85 * max(0.0, min(1.0, (y - ys) / 1.9)) ** 1.4, density=5.0,
             maxn=40, cell=0.4, seed=21, mix=dict(stone=0.65, char=0.25, wood=0.1), size=(0.25, 0.5),
             beam_len=(0.7, 1.4))
    for k, (u, zt, yb, zb) in enumerate(((0.15, 1.9, -1.2, 0.1), (0.4, 1.4, -1.5, 0.15), (0.62, 2.2, -1.0, 0.2),
                                          (0.85, 1.6, -1.3, 0.1))):
        x = g0 + (g1 - g0) * u
        m.beam("r_char" if k % 2 else "r_char2", (x + 0.2 * (k - 1.5), y0 + 0.5, zt), (x - 0.3, y0 + yb, zb), 0.15)


@model("VerBuild05b")
def verbuild05b(m):
    b05_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (136, 121, 99), (84, 76, 64), (92, 82, 68), stone=(108, 96, 80))
    m.col("thatch", (114, 98, 74))
    m.col("thatch2", (96, 82, 62))
    m.col("yard", (96, 88, 76))
    m.col("yardtop", (136, 121, 99))
    m.soot["yardtop"] = ("wsoot", 0.4)
    m.col("tile_a", (108, 46, 32))
    m.col("tile_b", (84, 38, 28))
    P = b05_plan(m.frame(1, -12))
    for key, zt, seed in (("main", 1.6, 1), ("wing", 1.1, 2)):
        x0, x1, y0, y1 = P[key]
        box = ring(rect(x0, x1, y0, y1), 0.15)
        m.bwall("wall", box + box[:1], 0.3, [(0, zt), (0.2, zt * 0.6), (0.4, zt), (0.6, zt * 0.7), (0.8, zt * 1.1),
                                              (1, zt)], frac=True, jag=0.35, cap="wtop")
        m.debris(T, ring(rect(x0, x1, y0, y1), 0.3), zt, lo=0.4, hi=0.7, density=4.0, maxn=170, cell=0.5,
                 seed=seed, size=(0.2, 0.45), mix=BURNT)
    # the yard between the house and its front wall is a field of the same
    x0, x1, y0, y1 = P["main"]
    (ax, ay), (bx, by) = P["front"][0], P["front"][-1]
    yard = [(ax + 0.4, ay + 0.25), (bx - 0.2, by + 0.25), (P["wing"][0] - 0.1, y0 - 0.1), (x0 - 0.3, y0 - 0.1)]
    m.debris(T, yard, 0.5, zfn=lambda x, y: 0.25, density=1.8, maxn=80, cell=0.5, seed=5, size=(0.2, 0.45),
             mix=BURNT)
    m.bwall("yard", P["front"], 0.3, [(0, 0.5), (0.3, 0.3), (0.45, 0.45), (0.6, 0.2), (0.75, 0.4), (1, 0.35)],
            frac=True, jag=0.15, cap="yardtop")
    # the west lean-to fell: straw slabs tilted across its broken posts, the low curved wall under them
    m.bwall("yard", P["west"], 0.28, [(0, 0.5), (0.3, 0.7), (0.6, 0.45), (1, 0.6)], frac=True, jag=0.15,
            cap="yardtop")
    # three big sooty slabs, lapped over one another, heaped against the
    # wall's inner face and tilted 15 to 30 degrees down onto broken posts
    m.col("thatch", (66, 52, 30))
    m.col("thatch2", (48, 38, 22))
    import random
    rng = random.Random(m.name)
    W = [kit.Vector(p) for p in P["west"]]
    lens = [(W[i + 1] - W[i]).length for i in range(len(W) - 1)]
    total = sum(lens)

    def along(sv):
        i = 0
        while i < len(lens) - 1 and sv > lens[i]:
            sv -= lens[i]
            i += 1
        d = (W[i + 1] - W[i]).normalized()
        return W[i] + d * min(sv, lens[i]), d
    for k, (sv, L, tilt) in enumerate(((0.19, 2.7, 24), (0.42, 2.4, 17), (0.64, 2.8, 27))):
        c, d = along(sv * total)
        right = kit.Vector((d.y, -d.x))
        r = math.radians(tilt)
        wd = 1.4
        p = c + right * (0.2 + wd / 2 * math.cos(r))
        yaw = math.atan2(d.y, d.x) + rng.uniform(-0.15, 0.15)
        m.shard("thatch" if k != 1 else "thatch2", p.x, p.y, wd / 2 * math.sin(r) + 0.12 + 0.06 * k, L, wd, yaw, 0.0, r,
                t=0.14, rag=0.3, seed=k)
    # the broken posts stand up through the heap
    for k, (sv, ph) in enumerate(((0.12, 0.5), (0.33, 0.7), (0.55, 0.42), (0.76, 0.62))):
        c, d = along(sv * total)
        right = kit.Vector((d.y, -d.x))
        p = c + right * 1.25
        m.post("post", p.x, p.y, 0.0, ph, 0.17, lean=rng.uniform(-0.25, 0.25))
    # charred rafters from the lean-to lie among the slabs
    for k in range(6):
        c, d = along(rng.uniform(0.08, 0.75) * total)
        right = kit.Vector((d.y, -d.x))
        a, b = c + right * 0.35, c + right * 1.6 + d * rng.uniform(-0.8, 0.8)
        m.beam(rng.choice(("r_char", "r_char2")), (a.x, a.y, 0.72), (b.x, b.y, 0.12), 0.13)
    m.tile_slab(("tile_a", "tile_b"), (x0 + x1) / 2 - 1.0, y0 + 0.9, 1.1, 2.0, 1.4, 0.2, 0.15, 0.3)
    m.scatter(keys, max_area=200)


# ---- VerBuild06: two gabled houses and a tall flat block round a yard -----

def b06_colours(m):
    # the flat block stands four storeys: build it lower and deeper
    m.sturdy(0.88)
    m.col("roof", (125, 48, 26), paint=True)
    m.col("house", (96, 90, 80))
    m.col("block", (104, 96, 84))
    m.col("flat", (245, 230, 200))
    m.col("parapet", (214, 200, 174))
    m.col("ptop", (247, 231, 201))
    m.col("yard", (150, 140, 122))
    m.col("yardtop", (240, 226, 198))
    m.col("dark", (24, 20, 18))
    m.col("sill", (225, 220, 205))


def b06_plan(F):
    H = 3.75
    # the block, counter-clockwise from its north-west corner, that corner cut
    block = [F.P(200, 42, H), F.P(183, 62, H), F.P(183, 160), F.P(256, 160), F.P(256, 42, H)]
    return dict(
        H=H, block=block,
        north=(F.X(63), F.X(180), F.Y(55), F.Y(55) + 2.2), ne=1.9, nr=0.8,
        south=(F.X(63), F.X(153), F.Y(137, 1.2), F.Y(137, 1.2) + 3.0), se=1.2, sr=1.0,
        annex=(F.X(154), F.X(172), F.Y(137, 1.2), F.Y(137, 1.2) + 2.0), ae=1.0, ar=0.55,
        wh=1.3,
        yn=F.Y(6, 1.3), yw=F.X(7), ysouth=F.Y(137, 1.2), xe=F.X(236),
        nwin=[F.X(c) for c in (84, 104, 130)], bwin=[(F.X(230), 2.3), (F.X(225), 0.6)],
    )


def b06_block(m, P):
    H = P["H"]
    m.flat("block", "flat", P["block"], H, par_h=0.35, par_t=0.25, cap="parapet")
    m.wall("ptop", ring(P["block"], 0.125), 0.27, 0.04, z0=H + 0.35, closed=True)
    # the west wall's sloped foot
    b = P["block"]
    m.batter("block", b[1], b[2], H * 0.85, 0.55)
    for x, z in P["bwin"]:
        m.window("dark", x, b[2][1], z, 0.6, 0.45, sill="sill")


def b06_houses(m, P, keep=None, rafters=None, walls=True, which=("north", "south", "annex")):
    for k, ze, rise in (("north", P["ne"], P["nr"]), ("south", P["se"], P["sr"]), ("annex", P["ae"], P["ar"])):
        if k not in which:
            continue
        x0, x1, y0, y1 = P[k]
        if keep is None:
            m.box("house", x0, x1, y0, y1, 0, ze)
        elif walls:
            m.wall("house", ring(rect(x0, x1, y0, y1), 0.15), 0.3, ze, closed=True)
        m.roof("roof", x0, x1, y0, y1, ze, rise, "x", ("gable", "gable"), over=0.18,
               core=None if keep else "house", keep=keep, rafters=rafters)
    if walls and "north" in which:
        for x in P["nwin"]:
            m.window("dark", x, P["north"][2], 0.55, 0.6, 0.45, sill="sill")


def b06_yard_runs(P):
    yn, yw, ys, xe = P["yn"], P["yw"], P["ysouth"], P["xe"]
    nx0, nx1 = P["north"][0], P["north"][1]
    return [
        [(nx0, yn), (yw, yn), (yw, ys), (P["south"][0], ys)],
        [(nx1, yn), (xe, yn), (xe, P["block"][0][1])],
        [(P["annex"][1], ys), (P["block"][1][0], ys)],
    ]


def b06_yard(m, P, profs=None, jag=0.15):
    for i, pts in enumerate(b06_yard_runs(P)):
        if profs is None:
            m.wall("yard", pts, 0.3, P["wh"], cap="yardtop", cap_h=0.06, cap_over=0.02)
        else:
            m.bwall("yard", pts, 0.3, profs[i], frac=True, jag=jag, cap="yardtop")


@model("VerBuild06")
def verbuild06(m):
    b06_colours(m)
    P = b06_plan(m.frame())
    b06_block(m, P)
    b06_houses(m, P)
    b06_yard(m, P)


def yard_litter(m, T, pieces, seed=0):
    """Broken beams and crates lying in a yard: ("beam", c0, r0, c1, r1, w)
    runs between two drawn points on the ground, ("crate", c, r, s, yaw)
    stands a box, ("planks", c, r, L, yaw, n) lays a few boards side by side."""
    import random
    rng = random.Random("%s:y%d" % (m.name, seed))
    for p in pieces:
        if p[0] == "beam":
            _, c0, r0, c1, r1, w = p
            m.beam(rng.choice(T["char"] + T["wood"]), (m.X(c0), m.Y(r0, w / 2), w / 2),
                   (m.X(c1), m.Y(r1, w / 2), w / 2 + rng.uniform(0.0, 0.25)), w)
        elif p[0] == "crate":
            _, c, r, s, yaw = p
            x, y = m.X(c), m.Y(r, s / 2)
            m.crate("crate", x, y, s=s, h=s * 0.8, yaw=yaw, edge="crate_edge")
        else:
            _, c, r, L, yaw, n = p
            x, y = m.X(c), m.Y(r, 0.05)
            for i in range(n):
                d = (i - (n - 1) / 2) * 0.3
                m.obox(rng.choice(T["wood"] + T["char"]), x - math.sin(yaw) * d, y + math.cos(yaw) * d,
                       0.04 + 0.05 * (i % 2), L * rng.uniform(0.8, 1.0), 0.26, 0.07, yaw + rng.uniform(-0.1, 0.1),
                       0.0, rng.uniform(-0.08, 0.08))


WEST_YARD = [("beam", 2, 55, 28, 64, 0.22), ("planks", 50, 66, 1.9, 0.9, 3), ("crate", 15, 86, 0.55, 0.3),
             ("crate", 54, 93, 0.55, -0.4), ("crate", 34, 86, 0.5, 0.2), ("beam", 62, 106, 88, 90, 0.16),
             ("beam", 58, 124, 70, 116, 0.14), ("crate", 23, 97, 0.45, 0.6)]


@model("VerBuild06a")
def verbuild06a(m):
    b06_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (170, 156, 132), (104, 96, 84), (100, 92, 80), stone=(150, 138, 118), ash=(88, 80, 68))
    m.col("block", (92, 84, 74))
    m.col("flat", (200, 188, 164), paint=True)
    m.col("towertop", (176, 164, 142))
    m.col("wood", (80, 56, 38))
    m.col("char", (32, 26, 22))
    m.col("crate", (96, 70, 46))
    m.col("crate_edge", (62, 46, 32))
    P = b06_plan(m.frame(22, 0))
    H = P["H"]
    bw = ring(P["block"], 0.18)
    # the block's west and south-west fell outward; its north and east walls stand
    m.bwall("block", bw + bw[:1], 0.36,
            [(0, H), (0.09, H - 0.3), (0.13, H - 1.3), (0.18, 1.7), (0.24, 1.0), (0.3, 0.8), (0.36, 1.3), (0.42, 1.8),
             (0.47, 2.7), (0.52, H - 0.4), (0.57, H), (1, H)], frac=True, jag=0.3, step=0.25, cap="towertop")
    pale = m.pic_field(lambda c: max(c) > 0.38 and max(c) - min(c) < 0.2, sigma=3.0, bias=0.4)
    inner = ring(P["block"], 0.3)
    tower_roof(m, "flat", inner, H, pale)
    burnt_joists(m, "char", inner, H, pale, n=8)
    m.debris(T, inner, 2.4, zfn=lambda x, y: 1.9, density=2.0, maxn=90, cell=0.5, mix=BURNT, size=(0.25, 0.55),
             where=lambda x, y: pale(x, y, H) < 0.05, bed_where=lambda x, y: pale(x, y, H) < 0.3)
    # the fall: masonry and charred beams slope from the block's west wall over the hall's east end
    wx = P["block"][2][0]
    hx0 = m.X(150)
    y_s, y_n = P["south"][2] - 0.4, P["block"][1][1] - 0.3
    slope = [(hx0, y_s), (wx + 0.6, y_s), (wx + 0.6, y_n), (hx0 + 1.0, y_n)]
    nz = m.noise("slope", 0.8)
    m.debris(T, slope, 2.6, zfn=lambda x, y: max(0.15, 0.25 + 2.3 * (x - hx0) / (wx + 0.6 - hx0)) * (1 + 0.25 * nz(x, y)),
             density=3.2, maxn=170, cell=0.45, seed=3, mix=dict(stone=0.6, char=0.2, wood=0.12, tile=0.08),
             size=(0.3, 0.65), beam_len=(0.6, 1.5))
    keep = m.pic_field(m.roofish, sigma=3.0, bias=0.3)
    for k in ("north", "south"):
        x0, x1, y0, y1 = P[k]
        ze, rise = (P["ne"], P["nr"]) if k == "north" else (P["se"], P["sr"])
        if k == "south":
            x1 = min(x1, hx0 + 0.5)
        if k == "south":
            m.gabled_ruin("house", "roof", x0, x1, y0, y1, ze, rise, "x", keep, keys, dz=0.6)
            continue
        # the north hall: walls, gables and the holed roof on a coarser grid, no bare rafters
        m.wall("house", ring(rect(x0, x1, y0, y1), 0.15), 0.3, ze, closed=True)
        m.gables("house", x0, x1, y0, y1, ze, rise, "x", t=0.24)
        m.cut_roof("roof", x0, x1, y0, y1, ze, rise, "x", ("gable", "gable"), over=0.18, field=keep, cell=0.24)
    # the north hall's hole is a collapse of crossing charred timbers and
    # plaster lumps, spilling out over the front wall at its west-centre
    x0, x1, y0, y1 = P["north"]
    ze = P["ne"]
    m.col("coal", (22, 18, 15), rough=1.0)
    m.col("plaster", (196, 184, 158))
    m.col("plaster2", (150, 140, 120))
    Tn = dict(T, char=("coal", "r_char"), stone=("plaster", "plaster2", "r_stone"),
              ash=("n_ash", "n_ash", "n_ash2"))
    m.col("n_ash", (30, 24, 20), rough=1.0)
    m.col("n_ash2", (40, 32, 26), rough=1.0)
    inner = rect(x0 + 0.3, x1 - 0.3, y0 + 0.3, y1 - 0.3)
    rz = lambda x, y: m.roof_z(x, y) or ze  # noqa: E731
    opened = lambda x, y: keep(x, y, rz(x, y)) < 0.1  # noqa: E731
    m.debris(Tn, inner, ze, lo=0.55, hi=0.8, density=2.5, maxn=22, cell=0.55, seed=21, where=opened,
             bed_where=lambda x, y: True, mix=dict(char=0.4, stone=0.45, tile=0.15), size=(0.25, 0.5))
    tangle(m, ("coal", "coal", "r_char", "r_wood"), inner, lambda x, y: ze * 0.7, lambda x, y: rz(x, y) - 0.55, 14,
           seed=3, L=(1.1, 2.0), where=opened)
    xs0, xs1 = m.X(112), m.X(142)
    yo = y0 - 0.9
    spill = rect(xs0, xs1, yo, y0 + 0.6)
    Ts = dict(Tn, ash=("plaster2", "n_ash2", "r_stone"))
    m.debris(Ts, spill, ze, zfn=lambda x, y: max(0.08, ze * 0.95 * min(1.0, (y - yo) / (y0 - yo)) ** 1.5)
             * (1 - 0.6 * abs(2 * (x - xs0) / (xs1 - xs0) - 1)), density=4.0, maxn=18, cell=0.4, seed=22,
             mix=dict(stone=0.6, char=0.3, tile=0.1), size=(0.25, 0.5), beam_len=(0.8, 1.6))
    tangle(m, ("coal", "r_char"), rect(xs0 + 0.3, xs1 - 0.3, y0 - 0.6, y0 + 0.4), 0.6, 1.5, 4, seed=4,
           L=(1.4, 2.2), pitch=(25, 40))
    # the west-centre window is buried under it
    for x in P["nwin"]:
        if not xs0 < x < xs1:
            m.window("dark", x, P["north"][2], 0.55, 0.6, 0.45, sill="sill")
    # tile shards thrown out in front
    import random
    rng = random.Random(m.name + "tiles")
    for i in range(10):
        c, r = rng.uniform(98, 146), rng.uniform(64, 79)
        m.obox(rng.choice(T["tile"]), m.X(c), m.Y(r, 0.04), 0.04, rng.uniform(0.3, 0.5), rng.uniform(0.2, 0.32), 0.05,
               rng.uniform(0, math.pi), rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2))
    # the yard walls sooted, dark streaks down their faces
    m.col("yard", (132, 122, 102))
    m.col("yardtop", (172, 160, 130))
    m.col("yardsoot", (112, 102, 84))
    m.col("streak", (62, 56, 48))
    m.soot["yardtop"] = ("yardsoot", 0.3)
    profs = ([(0, 1.2), (0.15, 1.3), (0.35, 1.1), (0.45, 0.4), (0.5, 0), (0.62, 0), (0.66, 1.0), (0.85, 1.2),
              (0.9, 0.6), (1, 1.0)],
             [(0, 1.3), (0.4, 1.2), (0.6, 0.7), (0.75, 1.2), (1, 1.1)],
             [(0, 0.6), (1, 0.3)])
    b06_yard(m, P, profs=profs)
    for k, (pts, prof) in enumerate(zip(b06_yard_runs(P), profs)):
        streaks(m, "streak", pts, 0.3, prof, (7, 4, 0)[k], seed=k, both=False)
    yard_litter(m, T, WEST_YARD)
    m.scatter(keys, max_area=260, min_area=5, skip=[(0, 40, 95, 130)])


@model("VerBuild06b")
def verbuild06b(m):
    b06_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (145, 130, 108), (84, 76, 66), (92, 82, 70), stone=(112, 100, 84))
    m.col("yard", (100, 92, 80))
    m.col("yardtop", (145, 130, 108))
    m.soot["yardtop"] = ("wsoot", 0.4)
    m.col("crate", (82, 60, 40))
    m.col("crate_edge", (54, 40, 28))
    m.col("tile_a", (108, 46, 32))
    m.col("tile_b", (84, 38, 28))
    P = b06_plan(m.frame(22, 1))
    bw = ring(P["block"], 0.18)
    m.bwall("wall", bw + bw[:1], 0.36,
            [(0, 1.8), (0.1, 1.2), (0.25, 1.0), (0.4, 1.5), (0.55, 1.1), (0.7, 1.9), (0.85, 1.4), (1, 1.8)],
            frac=True, jag=0.35, cap="wtop")
    m.debris(T, ring(P["block"], 0.36), 1.8, lo=0.45, hi=0.7, density=3.6, maxn=230, cell=0.5, mix=BURNT,
             size=(0.2, 0.5), seed=1)
    # the north house keeps a scrap of roof at its west end
    x0, x1, y0, y1 = P["north"]
    m.tile_slab(("tile_a", "tile_b"), x0 + 1.1, (y0 + y1) / 2 + 0.2, 1.0, 2.0, 2.4, 0.1, 0.25, 0.2, seed=1)
    for k, zt, seed in (("north", 1.3, 2), ("south", 1.0, 3), ("annex", 0.8, 4)):
        x0, x1, y0, y1 = P[k]
        box = ring(rect(x0, x1, y0, y1), 0.15)
        m.bwall("wall", box + box[:1], 0.3, [(0, zt), (0.3, zt * 0.6), (0.5, zt), (0.75, zt * 0.7), (1, zt)],
                frac=True, jag=0.3, cap="wtop")
        m.debris(T, ring(rect(x0, x1, y0, y1), 0.3), zt, lo=0.45, hi=0.7, density=3.6, maxn=150, cell=0.5,
                 mix=BURNT, size=(0.2, 0.45), seed=seed)
    b06_yard(m, P, profs=(
        [(0, 1.0), (0.15, 1.1), (0.35, 0.9), (0.45, 0.3), (0.5, 0), (0.62, 0), (0.66, 0.8), (0.85, 1.0),
         (0.9, 0.5), (1, 0.8)],
        [(0, 1.0), (0.4, 0.9), (0.6, 0.5), (0.75, 1.0), (1, 0.9)],
        [(0, 0.4), (1, 0.2)]), jag=0.25)
    yard_litter(m, T, WEST_YARD)
    m.scatter(keys, max_area=260, skip=[(0, 40, 95, 130)])
