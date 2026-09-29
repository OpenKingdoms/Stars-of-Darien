"""Aramon wall ruins: the intact piece's line (its fitted placement, made
sturdy like the intact walls), the wall standing only as high as the
ruin's drawing reaches over it, courses broken block by block with the
pale fill showing on the broken top, rubble heaped against it and strewn
on it, and fallen lengths of the wall top lying tilted on the heap.

prof: (t along the wall from its front cut, height standing) in the
pixel-exact frame, scaled like the wall. A section is (t, off, length,
yaw, pitch, roll): a fallen length of wall top centred t along the wall
and off across it (+ left), turned yaw degrees from the wall's line and
tilted pitch (along it) and roll (across it), dropped onto the heap.

style="stump" (AraWall06a) builds a wall worn down to a course or two
instead: the old walk a dark channel down its top, a buttress stump on
one face, small broken blocks heaped on both sides and piled over the
far end, and a few merlon caps lying flat on the heap.
"""
import math

import arawall as aw
import kit
import models_ara
import rubble

T = {
    "AraWall05a": dict(base="AraWall05", prof=[(0, 2.6), (1.0, 2.5), (1.5, 1.9), (1.9, 1.2), (3.0, 0.9), (4.2, 1.3),
                                               (5.5, 0.6)],
                       heap=3.0, spread=2.2, n=110, seed=5, sections=[]),
    # a stump a course or two high with the old walk a dark channel down
    # its middle, a buttress stump against its south-west face, small
    # broken blocks heaped on both sides and piled over its far end, and a
    # few merlon caps lying flat on the heap
    "AraWall06a": dict(base="AraWall06", style="stump", yf=-1.45, stand=1.25, groove=0.68,
                       butt=(2.9, 1, 1.8, 1.1, 0.95, 1.4), heap=0.85, spread=1.9,
                       pile=(5.0, 0.15, 1.5, 2.6), n=200, size=(0.2, 0.5), caps=[(1.1, -1.1, 20), (3.6, 1.1, -35),
                                                                            (5.2, -0.6, 70)], seed=6),
    "AraWall07a": dict(base="AraWall07", prof=[(0, 3.0), (0.5, 2.8), (1.0, 2.3), (1.6, 1.7), (2.2, 1.1), (2.8, 0.9),
                                               (3.6, 1.6), (4.4, 2.8), (5.2, 1.2), (6.0, 0.6)],
                       heap=2.6, spread=2.0, n=110, seed=7, sections=[]),
}


def interp(prof, ext):
    def f(t):
        t -= ext
        if t <= prof[0][0]:
            return prof[0][1]
        for (a, ha), (b, hb) in zip(prof, prof[1:]):
            if a <= t <= b:
                return ha + (hb - ha) * (t - a) / (b - a)
        return prof[-1][1]
    return f


def build(m, r):
    p = models_ara.sturdy(kit.params(r["base"], models_ara.T))
    k = aw.STURDY_H
    # the intact palette for the standing masonry, the ruin's own tones for rubble
    aw.palette(m)
    tones = rubble.palette(m, k=5, prefix="rub", gain=1.0, floor=30, warm=(1.04, 1.0, 0.94))
    dx, dy = math.cos(math.radians(p["ang"])), math.sin(math.radians(p["ang"]))
    pts = [(p["x"] - dx * 4, p["yf"] - dy * 4), (p["x"] + dx * 10, p["yf"] + dy * 10)]
    m.clip(0.0, p["yf"], 0.0, -1.0)
    m.clip(0.0, p["yb"], 0.0, 1.0)
    H = p["H"]
    base = interp([(t, h * k) for t, h in r["prof"]], 4.0)
    hfn = lambda t: min(H, base(t))  # noqa: E731
    butts = []
    for b in p.get("butt", []):
        t = b[0] + 4.0
        butts.append((t,) + tuple(b[1:5]) + (min(b[5], max(0.6, hfn(t) * 1.05)),))
    aw.build(m, pts, buttresses=butts, hfn=hfn, H=H, D=p["D"], cap=True)
    # the heap along the wall, highest against it, kept inside the drawing
    line = [pts[0], pts[1]]

    def heap(x, y):
        dd = rubble.line_dist(line, x, y)
        if y < p["yf"] - 1.5 or y > p["yb"] + 1.0:
            return 0.0
        return r["heap"] * k * max(0.0, 1.0 - dd / r["spread"]) ** 1.3
    xs = [p["x"] - 4, p["x"] + dx * (p["yb"] - p["yf"]) / max(0.2, abs(dy)) + 4]
    box = (min(xs) - 1, max(xs) + 1, p["yf"] - 2.0, p["yb"] + 1.5)
    Z = rubble.mound(m, tones, box, heap, cell=0.25, seed=r["seed"])
    zheap = lambda x, y: rubble.height_at(Z, x, y)  # noqa: E731

    def t_of(x, y):
        return (x - pts[0][0]) * dx + (y - pts[0][1]) * dy

    def ground(x, y):
        # what a stone lands on: the heap, or the broken wall top under it
        z = zheap(x, y)
        if rubble.line_dist(line, x, y) < p["D"] / 2 - 0.05:
            z = max(z, aw.ruin_top(H, hfn(t_of(x, y))) + 0.03)
        return z
    for t, off, ln, yaw, pitch, roll in r["sections"]:
        cx = pts[0][0] + dx * (t + 4.0) - dy * off
        cy = pts[0][1] + dy * (t + 4.0) + dx * off
        aw.section(m, ground, cx, cy, math.radians(p["ang"] + yaw), math.radians(pitch), math.radians(roll),
                   ln, p["D"])
    rubble.blocks(m, tones, box, r["n"], size=(0.12, 0.34), zfn=ground, seed=r["seed"])


def build_stump(m, r):
    """A wall worn down to a stump along the intact piece's line: its top
    course split by the dark channel of the old walk, a buttress stump on
    one face, a heap of small broken blocks on both sides of it and piled
    over its far end, loose merlon caps lying flat on the heap."""
    p = models_ara.sturdy(kit.params(r["base"], models_ara.T))
    # the worn stump takes the drawing's grey-tan, not the intact wall's dark ashlar
    aw.palette(m, stone=tuple(v * 0.85 for v in m.sample(0, 0, m.w, m.h, pick=lambda r_, g, b: 60 < 0.3 * r_ + 0.59 * g
                                                         + 0.11 * b < 120)))
    m.col("groove", (30, 27, 24), 1.0)
    tones = rubble.palette(m, k=5, prefix="rub", gain=1.0, floor=30, warm=(1.04, 1.0, 0.94))
    dx, dy = math.cos(math.radians(p["ang"])), math.sin(math.radians(p["ang"]))
    pts = [(p["x"] - dx * 4, p["yf"] - dy * 4), (p["x"] + dx * 10, p["yf"] + dy * 10)]
    yf = r.get("yf", p["yf"])
    m.clip(0.0, yf, 0.0, -1.0)
    m.clip(0.0, p["yb"], 0.0, 1.0)
    H, D = p["H"], p["D"]
    stand = r["stand"]
    aw.build(m, pts, buttresses=[(r["butt"][0] + 4.0,) + tuple(r["butt"][1:])], hfn=lambda t: stand, H=H, D=D,
             groove=r["groove"])
    zt = aw.ruin_top(H, stand)
    zg = aw.top_course(H, stand)[0]
    line = [pts[0], pts[1]]

    def t_of(x, y):
        return (x - pts[0][0]) * dx + (y - pts[0][1]) * dy - 4.0

    def off_of(x, y):
        return -(x - pts[0][0]) * dy + (y - pts[0][1]) * dx

    # rubble up to the stump's top against both faces, down to the ground
    # spread out; a pile over the far end; all kept inside the drawing
    pt, po, pr, ph = r["pile"]

    def heap(x, y):
        dd = rubble.line_dist(line, x, y)
        t = t_of(x, y)
        h = 0.0
        if -0.5 < t:
            h = r["heap"] * zt * max(0.0, 1.0 - max(0.0, dd - D / 2) / r["spread"]) ** 1.2
        q = math.hypot(t - pt, off_of(x, y) - po)
        h = max(h, ph * max(0.0, 1.0 - q / pr) ** 0.8)
        if dd < D / 2 + 0.05 and t < pt - pr * 0.5:
            # inside the stump, under the channel's floor
            h = min(h, zg - 0.05)
        bt, bs, bw = r["butt"][0], r["butt"][1], r["butt"][2]
        if abs(t - bt) < bw / 2 + 0.3 and 0 < bs * off_of(x, y) < D / 2 + r["butt"][4] + 0.2:
            # the buttress stands clear of the heap
            h = min(h, 0.6)
        return h
    L = (p["yb"] - yf) / abs(dy)
    xs = [p["x"] + dx * (yf - p["yf"]) / dy, p["x"] + dx * (p["yb"] - p["yf"]) / dy]
    box = (min(xs) - 3.0, max(xs) + 3.0, yf - 1.5, p["yb"] + 1.8)
    # one smooth heap round the stump's middle, its edge the drawing's
    mx = pts[0][0] + dx * (4.0 + L / 2 + 0.4)
    my = pts[0][1] + dy * (4.0 + L / 2 + 0.4)
    zheap = rubble.heap_mesh(m, tones, mx, my, rubble.outline(m, mx, my, lambda a: 4.5, jag=0.04, seed=r["seed"]),
                             heap, rings=10, seg=56, skirt=0.25, rad=2, bumps=0.08, seed=r["seed"])

    def ground(x, y):
        z = zheap(x, y)
        if rubble.line_dist(line, x, y) < D / 2 - 0.05 and -0.1 < t_of(x, y) < L + 0.1:
            z = max(z, zt + 0.03)
        return z

    def clear(x, y):
        # the channel, the stump's top and the buttress stay clear of loose stones
        t = t_of(x, y)
        bt, bs, bw = r["butt"][0], r["butt"][1], r["butt"][2]
        if abs(t - bt) < bw / 2 + 0.2 and 0 < bs * off_of(x, y) < D / 2 + r["butt"][4] + 0.1:
            return False
        return not (rubble.line_dist(line, x, y) < D / 2 + 0.1 and t < pt - pr * 0.6)
    rubble.blocks(m, tones, box, r["n"], size=r["size"], zfn=ground, near=clear, seed=r["seed"])
    # merlon caps knocked off, lying flat on the heap
    for t, off, yaw in r["caps"]:
        cx = pts[0][0] + dx * (t + 4.0) - dy * off
        cy = pts[0][1] + dy * (t + 4.0) + dx * off
        a = math.radians(p["ang"] + yaw)
        ca, sa = math.cos(a), math.sin(a)
        z = max(ground(cx + ca * u - sa * v, cy + sa * u + ca * v) for u in (-0.4, 0, 0.4) for v in (-0.12, 0.12))
        m.obox("stone3", cx, cy, z + 0.12, 0.85, 0.3, 0.3, yaw=a)
        m.obox("cap", cx, cy, z + 0.3, 0.9, 0.36, 0.08, yaw=a)


for _n in T:
    kit.TABLES[_n] = T
    kit.FITKEYS[_n] = []

    @kit.model(_n)
    def _b(m):
        r = kit.params(m.name, T)
        (build_stump if r.get("style") == "stump" else build)(m, r)
