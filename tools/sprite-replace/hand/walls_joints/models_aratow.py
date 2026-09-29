"""Ruined Aramon round towers: a ring of coursed blocks broken down to a
ragged top (prof gives the height standing at each bearing, degrees
anticlockwise from east, so 270 faces the camera), dark and choked with
rubble inside, rubble spilling out of the breach.

Every block rests on the course below it: a block stays only where the
wall under it still stands, so nothing floats.

slump=True (AraTow03a) builds a tower slumped into a mound instead: each
course set in from the one below so the courses make a dome, their tops
sagging inward and dipping toward the breach, a dark band along each
course's foot, and the rubble inside showing through the top and the
breach and spilling out of it.
"""
import math

import kit
import rubble

T = {
    "AraTow01a": dict(cx=0.0, cy=0.1, R=1.9, t=0.55, prof=[(0, 1.05), (25, 1.2), (50, 2.1), (90, 3.0), (150, 3.2),
                                                          (200, 2.9), (235, 2.3), (270, 1.8), (300, 1.45),
                                                          (325, 1.25), (360, 1.05)],
                      inner=1.1, spill=(355, 1.5, 2.3), n=60, seed=1, hollow=True, gain=1.02),
    "AraTow02a": dict(cx=0.0, cy=0.15, R=1.95, t=0.5, prof=[(0, 2.7), (60, 2.9), (120, 2.9), (180, 2.8), (240, 2.7),
                                                           (285, 2.2), (305, 1.1), (330, 1.4), (360, 2.7)],
                      inner=0.9, spill=(310, 1.2, 1.6), n=50, seed=2, hollow=True, gain=1.02),
    # slumped: the courses fall inward into a dome of sandy stone, broken
    # down to one course at the front-right where the rubble spills out;
    # prof is the height standing at each bearing, Zd the dome's height
    "AraTow03a": dict(cx=0.05, cy=0.1, R=1.95, t=0.55, Zd=3.2, course=0.4, slump=True,
                      prof=[(0, 1.9), (40, 2.4), (90, 2.75), (140, 2.6), (190, 2.3), (230, 2.1), (262, 1.8),
                            (285, 1.2), (303, 0.42), (328, 0.42), (348, 1.2), (360, 1.9)],
                      peak=3.0, spills=[(316, 1.0, 1.4), (228, 0.45, 0.9)], n=110, seed=3, gain=1.1, dip=0.45,
                      breach=315),
}
# a little lower than the drawing, like every tall thing in the remaster
STURDY = 0.9


def interp(prof, a):
    a %= 360
    for (a0, h0), (a1, h1) in zip(prof, prof[1:]):
        if a0 <= a <= a1:
            return h0 + (h1 - h0) * (a - a0) / max(1e-6, a1 - a0)
    return prof[-1][1]


def build(m, p):
    rng = m.rng
    rng.seed(p["seed"])
    tones = rubble.palette(m, k=5, prefix="rub", gain=p["gain"], floor=30)
    m.col("dark", (30, 27, 24), 1.0)
    cx, cy, R, th = p["cx"], p["cy"], p["R"], p["t"]
    ri = R - th
    course = 0.34
    prof = [(a, h * STURDY) for a, h in p["prof"]]
    ncourse = int(max(h for _, h in prof) / course) + 1
    # the heights each column of blocks still reaches, course by course
    stand = {}
    ring_top = {}
    for c in range(ncourse):
        z0, z1 = c * course, (c + 1) * course
        n = max(8, int(2 * math.pi * R / 0.72))
        off = 0.5 if c % 2 else 0.0
        for i in range(n):
            a0 = 2 * math.pi * (i + off) / n
            a1 = 2 * math.pi * (i + off + 1) / n
            am = math.degrees((a0 + a1) / 2)
            h = interp(prof, am) + rng.uniform(-0.35, 0.25)
            if z1 > h + 0.2:
                continue
            # a block needs the course under it
            below = [k for k in stand.get(c - 1, []) if abs(((k - am + 180) % 360) - 180) < 360 / n]
            if c > 0 and not below:
                continue
            # the last course is broken: stones short and ragged, not a
            # row of merlons, and nothing stands on them
            broken = z1 > h - course * 0.6
            if not broken:
                stand.setdefault(c, []).append(am)
            g = 0.02
            a0g, a1g = a0 + g / R, a1 - g / R
            pts = []
            seg = 2
            for k in range(seg + 1):
                a = a0g + (a1g - a0g) * k / seg
                pts.append((cx + R * math.cos(a), cy + R * math.sin(a)))
            for k in range(seg, -1, -1):
                a = a0g + (a1g - a0g) * k / seg
                pts.append((cx + ri * math.cos(a), cy + ri * math.sin(a)))
            am_r = (a0 + a1) / 2
            key = tones.at(cx + R * math.cos(am_r), cy + R * math.sin(am_r), z1, rad=1)
            if broken and rng.random() < 0.3:
                continue
            if broken:
                # a sloped, snapped top rather than a square merlon
                ha, hb = z0 + course * rng.uniform(0.2, 1.0), z0 + course * rng.uniform(0.2, 1.0)
                top = max(ha, hb)
                ns = len(pts) // 2
                hs = [ha + (hb - ha) * k / (ns - 1) for k in range(ns)]
                hs = hs + hs[::-1]
                bot = [(x, y, z0 + 0.005) for x, y in pts]
                up = [(x, y, zz) for (x, y), zz in zip(pts, hs)]
                n2 = len(pts)
                m.solid(key, [bot[::-1], up] + [[bot[q], bot[(q + 1) % n2], up[(q + 1) % n2], up[q]]
                                                for q in range(n2)])
            else:
                top = z1 - 0.005
                m.prism(key, pts, z0 + 0.005, top)
            for deg in range(int(math.floor(math.degrees(a0))), int(math.ceil(math.degrees(a1))) + 1):
                ring_top[deg % 360] = max(ring_top.get(deg % 360, 0.0), top)

    def rim_at(am):
        """The standing top of the ring at a bearing (degrees)."""
        return ring_top.get(int(round(am)) % 360, 0.0)
    if p["hollow"]:
        # a dark lining on the inner face: the inside of the ring is in shadow
        ns = 24
        for i in range(ns):
            a0, a1 = 2 * math.pi * i / ns, 2 * math.pi * (i + 1) / ns
            am = math.degrees((a0 + a1) / 2)
            top = min(rim_at(math.degrees(a0)), rim_at(math.degrees(a1))) - 0.12
            if top < 0.2:
                continue
            pts = [(cx + (ri + 0.005) * math.cos(a0), cy + (ri + 0.005) * math.sin(a0)),
                   (cx + (ri + 0.005) * math.cos(a1), cy + (ri + 0.005) * math.sin(a1)),
                   (cx + (ri - 0.03) * math.cos(a1), cy + (ri - 0.03) * math.sin(a1)),
                   (cx + (ri - 0.03) * math.cos(a0), cy + (ri - 0.03) * math.sin(a0))]
            m.prism("dark", pts, 0.0, top)
        # the dark floor inside and the rubble choking it
        m.cyl("dark", cx, cy, 0.0, ri - 0.02, 0.06, seg=24, smooth=False)

        def inner(x, y):
            d = math.hypot(x - cx, y - cy)
            return p["inner"] * max(0.0, 1 - (d / (ri - 0.05)) ** 2) if d < ri - 0.05 else 0.0
    else:
        # filled to the rim: a dome of rubble meeting the ring's broken top
        def inner(x, y):
            d = math.hypot(x - cx, y - cy)
            if d > ri + 0.1:
                return 0.0
            rim = rim_at(math.degrees(math.atan2(y - cy, x - cx))) - 0.1
            return max(0.2, rim + p["inner"] * (1 - (min(d, ri) / ri) ** 2))
    box = (cx - R - 2.5, cx + R + 2.5, cy - R - 2.5, cy + R + 2.5)
    Zi = rubble.mound(m, tones, (cx - ri - 0.15, cx + ri + 0.15, cy - ri - 0.15, cy + ri + 0.15), inner, cell=0.25,
                      seed=p["seed"])
    # rubble spilling out through the breach and down the outer face
    sa, sh, sl = p["spill"]
    sx, sy = cx + (R + 0.3) * math.cos(math.radians(sa)), cy + (R + 0.3) * math.sin(math.radians(sa))

    def spill(x, y):
        d = math.hypot(x - sx, y - sy)
        return sh * max(0.0, 1 - d / sl) ** 1.5
    Zo = rubble.mound(m, tones, box, spill, cell=0.25, seed=p["seed"] + 1)

    def ground(x, y):
        """What a loose stone lands on: the heaps, or the ring's own top."""
        z = max(rubble.height_at(Zo, x, y), rubble.height_at(Zi, x, y))
        d = math.hypot(x - cx, y - cy)
        if ri - 0.05 < d < R + 0.05:
            z = max(z, rim_at(math.degrees(math.atan2(y - cy, x - cx))))
        return z
    rubble.blocks(m, tones, box, p["n"], size=(0.1, 0.3), zfn=ground,
                  near=lambda x, y: (math.hypot(x - cx, y - cy) < ri - 0.1 or
                                     math.hypot(x - sx, y - sy) < sl + 0.3), seed=p["seed"])


def arc_block(m, key, cx, cy, a0, a1, zb, rb_out, rb_in, zt_out, rt_out, zt_in, rt_in, seg=2):
    """A curved block between bearings a0 and a1 (radians): its bottom
    from rb_in to rb_out at zb, its top from rt_in (at zt_in) to rt_out
    (at zt_out), so the top can slope and the faces lean."""
    def arc(r, z):
        return [(cx + r * math.cos(a0 + (a1 - a0) * k / seg), cy + r * math.sin(a0 + (a1 - a0) * k / seg), z)
                for k in range(seg + 1)]
    bo, bi = arc(rb_out, zb), arc(rb_in, zb)
    to, ti = arc(rt_out, zt_out), arc(rt_in, zt_in)
    polys = []
    for k in range(seg):
        polys.append([bo[k], bi[k], bi[k + 1], bo[k + 1]])
        polys.append([to[k], to[k + 1], ti[k + 1], ti[k]])
        polys.append([bo[k], bo[k + 1], to[k + 1], to[k]])
        polys.append([bi[k], ti[k], ti[k + 1], bi[k + 1]])
    polys.append([bo[0], to[0], ti[0], bi[0]])
    polys.append([bo[seg], bi[seg], ti[seg], to[seg]])
    m.solid(key, polys)


def build_slump(m, p):
    """A tower slumped into a mound: rings of sandy blocks, each course
    set in from the one below so the courses make a dome, their tops
    sagging inward, each dark along its foot; broken to a ragged rim
    that is highest at the back, down to one course where the rubble
    inside spills out. Every block rests on the course below it."""
    rng = m.rng
    rng.seed(p["seed"])
    tones = rubble.palette(m, k=5, prefix="rub", gain=p["gain"], floor=30)
    # the drawing's sandy stone, lit and in the dark course bands
    m.col("ring", (138, 121, 106), 0.95)
    m.col("ring2", (122, 107, 94), 0.95)
    m.col("ring3", (152, 134, 117), 0.95)
    m.col("band", (54, 45, 40), 1.0)
    cx, cy, R, th, Zd, ch = p["cx"], p["cy"], p["R"], p["t"], p["Zd"], p["course"]

    def Ro(z):
        return R * math.sqrt(max(0.0, 1 - (z / Zd) ** 2))
    prof = p["prof"]
    stand = {}
    # per degree, the top of the highest block: (outer radius, z there, inner radius, z there)
    tops = {}
    c = 0
    while True:
        z0, z1 = c * ch, (c + 1) * ch
        r0, r1 = Ro(z0), Ro(z1)
        if r1 < th + 0.25:
            break
        n = max(8, int(2 * math.pi * r0 / 0.72))
        off = 0.5 if c % 2 else 0.0
        for i in range(n):
            a0 = 2 * math.pi * (i + off) / n
            a1 = 2 * math.pi * (i + off + 1) / n
            am = math.degrees((a0 + a1) / 2) % 360
            h = interp(prof, am) + rng.uniform(-0.4, 0.2)
            if z1 > h + 0.15:
                continue
            below = [k for k in stand.get(c - 1, []) if abs(((k - am + 180) % 360) - 180) < 360 / n]
            if c > 0 and not below:
                continue
            broken = z1 > h - ch * 0.5
            if broken and rng.random() < 0.25:
                continue
            if not broken:
                stand.setdefault(c, []).append(am)
            g = 0.018
            a0g, a1g = a0 + g / r0, a1 - g / r0
            # the top sags inward, more so higher up, and the courses dip
            # toward the breach
            sag = 0.05 + 0.14 * z0 / Zd
            dip = -p.get("dip", 0.0) * (z0 / Zd) * max(0.0, math.cos(math.radians(am - p.get("breach", 315)))) ** 2
            zt = z1 - 0.01 if not broken else z0 + ch * rng.uniform(0.45, 0.95)
            f = (zt - z0) / ch
            rt = r0 + (r1 - r0) * f
            jr = rng.uniform(-0.04, 0.04)
            key = rng.choice(("ring", "ring", "ring2", "ring3"))
            # a dark band along the foot of each course, set in a little
            zb = z0 + 0.004 + dip
            zband = z0 + ch * 0.4 + dip
            zt += dip
            if zt > zband + 0.05:
                rbb = r0 + (r1 - r0) * 0.4
                arc_block(m, "band", cx, cy, a0g, a1g, zb, r0 - 0.06 + jr, r0 - th, zband, rbb - 0.06 + jr, zband,
                          rbb - th, seg=1)
                arc_block(m, key, cx, cy, a0g, a1g, zband, rbb + jr, rbb - th, zt, rt + jr, zt - sag, rt - th, seg=1)
            else:
                arc_block(m, "band", cx, cy, a0g, a1g, zb, r0 - 0.06 + jr, r0 - th, zt, rt - 0.06 + jr, zt - sag,
                          rt - th, seg=1)
            for deg in range(int(math.floor(math.degrees(a0))), int(math.ceil(math.degrees(a1))) + 1):
                d = deg % 360
                if d not in tops or tops[d][1] < zt:
                    tops[d] = (rt, zt, rt - th, zt - sag)
        c += 1

    def ring_z(x, y):
        """The top of the ring's highest block over (x, y), or 0."""
        d = math.hypot(x - cx, y - cy)
        deg = int(round(math.degrees(math.atan2(y - cy, x - cx)))) % 360
        if deg not in tops:
            return 0.0
        ro, zo, ri, zi = tops[deg]
        if not (ri - 0.02 <= d <= ro + 0.02):
            return 0.0
        u = (ro - d) / max(1e-6, ro - ri)
        return zo + (zi - zo) * max(0.0, min(1.0, u))

    # the rubble inside: a dome just under the courses, showing through the
    # top and the breach, peaking a little above the top course
    def dome(x, y):
        d = math.hypot(x - cx, y - cy)
        z = Zd * math.sqrt(max(0.0, 1 - ((d + th * 0.6) / R) ** 2)) - 0.12
        return min(p["peak"], z)
    rin = rubble.radial([R - th * 0.4] * 8)
    Zi = rubble.heap_mesh(m, tones, cx, cy + 0.05, rin, dome, rings=9, seg=48, skirt=0.0, clip=False, rad=2,
                          bumps=0.06, seed=p["seed"])
    # rubble spilling out through the breach and down the outer face
    spills = []
    for k, (sa, sh, sl) in enumerate(p["spills"]):
        sx, sy = cx + (R + 0.1) * math.cos(math.radians(sa)), cy + (R + 0.1) * math.sin(math.radians(sa))

        def spill(x, y, sx=sx, sy=sy, sh=sh, sl=sl):
            return sh * max(0.0, 1 - math.hypot(x - sx, y - sy) / sl) ** 1.3
        spills.append(rubble.heap_mesh(m, tones, sx, sy, rubble.radial([sl] * 8), spill, rings=6, seg=32,
                                       skirt=0.2, rad=2, bumps=0.1, seed=p["seed"] + k + 1))

    def ground(x, y):
        """What a loose stone lands on: the heaps, or the ring's own top."""
        return max([ring_z(x, y), Zi(x, y)] + [s(x, y) for s in spills])

    def exposed(x, y):
        """Where the rubble shows: over the top, in the breach, on the spills."""
        d = math.hypot(x - cx, y - cy)
        deg = int(round(math.degrees(math.atan2(y - cy, x - cx)))) % 360
        if d < Ro(interp(prof, deg)) - th + 0.15:
            return True
        return interp(prof, deg) < 1.0 and d < R + 0.9
    box = (cx - R - 1.5, cx + R + 1.5, cy - R - 1.5, cy + R + 1.0)
    rubble.blocks(m, tones, box, p["n"], size=(0.1, 0.3), zfn=ground, near=exposed, seed=p["seed"])
    # bigger broken blocks choking the breach and tumbling down the spill
    sa = math.radians(p.get("breach", 315))

    def breach(x, y):
        bx, by = cx + R * math.cos(sa), cy + R * math.sin(sa)
        return math.hypot(x - bx, y - by) < 1.3
    rubble.blocks(m, tones, box, p.get("n_breach", 40), size=(0.18, 0.42), zfn=ground, near=breach,
                  seed=p["seed"] + 9)


for _n in T:
    kit.TABLES[_n] = T
    kit.FITKEYS[_n] = [("cx", 0.15), ("cy", 0.15), ("R", 0.15)]

    @kit.model(_n)
    def _b(m):
        p = kit.params(m.name, T)
        (build_slump if p.get("slump") else build)(m, p)

