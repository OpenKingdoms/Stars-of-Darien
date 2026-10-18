"""Thirsha's armour, wings and arms for thirsha.py: blackened plate with
rolled edges, lames down the shoulders, hips and feet, a moulded
breastplate and a high collar, mail where the joints open, leather
straps, a barbed spear and a round shield, and a bat's wings on the original's
wing pieces, an arm of bone with fingers fanning from the wrist and the
skin between them. Every part is modelled at rest around the original's
pivots (thirsha.py attaches them). Left parts are mirrored to the right.
"""
import math

import bmesh
from mathutils import Matrix, Vector

import unitkit as uk
from elsin_armour import dome, grow, mail_patch, rim, ring2, rivets, sheet, shell, strap, tube_rings

V = Vector


def lerp(a, b, t):
    return a + (b - a) * t


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def loft_ob(name, rings, mats, angle=40, cap0=False, cap1=False, closed=True):
    bm = bmesh.new()
    uk.loft(bm, rings, closed=closed, cap0=cap0, cap1=cap1)
    return uk.shade(uk.mesh(name, bm, mats), angle)


def lame(name, rings, mat, thick=0.012):
    """A plate between rings, thickened inward, its lower edge rolled."""
    bm = bmesh.new()
    uk.loft(bm, rings)
    ob = uk.mesh(name, bm, [mat], outward=False)
    m = ob.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = thick, -1.0
    uk.apply(ob)
    return uk.shade(ob, 40)


# ---------------------------------------------------------------- legs

def foot(rig, M):
    """The left sabaton: a long pointed toe, four lames over the instep with
    rolled edges, a heel cup and an ankle strap, on a leather sole."""
    a = rig.pivot("foot_L")
    cx, y0 = a.x, a.y - 0.06
    parts = []

    def sec(y):
        # half width and height of the foot at y (from the heel at +0.24 to the toe at -0.42)
        t = min(1.0, max(0.0, -y / 0.42))
        return 0.13 * (1 - t ** 3.0) + 0.03, 0.25 * (1 - t ** 1.5) + 0.05

    bm = bmesh.new()
    rings = []
    for y, hw, top in ((0.25, 0.085, 0.26), (0.21, 0.12, 0.33), (0.1, 0.135, 0.33), (0.0, 0.14, 0.27)):
        h = top / 2.0
        r = uk.ring(V((cx, y0 + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw, h, 10, power=3.0, start=-math.pi / 2)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    uk.loft(bm, rings, cap0=True, cap1=True)
    heel = uk.mesh("heel_L", bm, [M["plate"], M["leather"]])
    for p in heel.data.polygons:
        if p.normal.z < -0.6 or p.center.z < 0.02:
            p.material_index = 1
    parts.append(uk.shade(heel, 40))
    for k, (ya, yb) in enumerate(((0.0, -0.07), (-0.07, -0.14), (-0.14, -0.21), (-0.21, -0.28))):
        rings = []
        for y, g in ((ya + 0.012, 0.0), (yb, 0.006)):
            hw, top = sec(y)
            h = top / 2.0
            r = uk.ring(V((cx, y0 + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw + g, h + g, 10, power=3.0, start=-math.pi / 2)
            rings.append([V((p.x, p.y, max(0.012, p.z))) for p in r])
        parts.append(lame("sabaton%d_L" % k, rings, M["plate"]))
        parts.append(rim([p for p in rings[-1] if p.z > 0.02], 0.006, M["plate"], "sabatonrim", sides=4))
    rings = []
    for y in (-0.28, -0.34, -0.39, -0.43):
        hw, top = sec(y)
        h = max(0.018, top / 2.0)
        r = uk.ring(V((cx, y0 + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw, h, 10, power=3.0, start=-math.pi / 2)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    tip = V((cx, y0 - 0.47, 0.015))
    rings.append([tip.copy() for _ in rings[-1]])
    parts.append(loft_ob("toe_L", rings, [M["plate"]], 40))
    bm = bmesh.new()
    sole = [V((cx + math.cos(t) * 0.13, y0 - 0.1 + math.sin(t) * 0.36, 0.0)) for t in (2 * math.pi * k / 16 for k in range(16))]
    vs = [bm.verts.new(p) for p in sole] + [bm.verts.new(p + V((0, 0, 0.025))) for p in sole]
    bm.faces.new(vs[:16][::-1])
    for k in range(16):
        bm.faces.new((vs[k], vs[(k + 1) % 16], vs[16 + (k + 1) % 16], vs[16 + k]))
    parts.append(uk.shade(uk.mesh("sole_L", bm, [M["leather"]]), 50))
    c = (cx, y0 + 0.12)
    parts += strap(c, ring2(c, 0.13, 0.14, 0.13, 0.25, 14), 0.03, 0.01, M["leather"], M["trim"], buckle_at=0, name="anklestrap_L")
    return uk.join(parts, "foot_L")


def shin(rig, M):
    """The left greave, slim at the ankle and full at the calf with a ridge
    down the shin and two straps, and a domed poleyn over the knee with a
    fan on its outer side."""
    k = rig.pivot("leglow_L")
    a = rig.pivot("foot_L")
    top_ = k + V((0.0, 0.03, -0.03))
    bot = a + V((0.0, -0.02, 0.12))
    ts = (0.0, 0.08, 0.35, 0.58, 0.8, 0.87, 1.0)
    radii = ((0.1, 0.104, 0.104), (0.11, 0.114, 0.114), (0.128, 0.13, 0.158), (0.148, 0.138, 0.188),
             (0.146, 0.138, 0.172), (0.15, 0.142, 0.16), (0.138, 0.136, 0.13))
    path = [lerp(bot, top_, t) for t in ts]
    rings = [ring2((p.x, p.y), rx, ryf, ryb, p.z, 12, power=2.2, ridge=0.014, ridge_w=0.35) for p, (rx, ryf, ryb) in zip(path, radii)]
    lip = [V((p.x, p.y, p.z - 0.012)) for p in grow(rings[0], (bot.x, bot.y), -0.02)]
    parts = [loft_ob("shin_L", [lip] + rings, [M["plate"]], 38, cap1=True)]
    parts.append(rim(rings[0] + [rings[0][0]], 0.008, M["plate"], "greaverim"))
    for t in (0.3, 0.7):
        p = lerp(bot, top_, t)
        rx, ryf, ryb = radii[2] if t < 0.5 else radii[4]
        c = (p.x, p.y)
        parts += strap(c, ring2(c, rx, ryf, ryb, p.z, 16), 0.035, 0.009, M["leather"], M["trim"], buckle_at=3, name="greavestrap_L")
    c = k + V((0.01, -0.075, 0.03))
    rings = []
    for lat, sc in ((0.0, 1.0), (0.3, 0.98), (0.65, 0.88), (1.0, 0.66), (1.3, 0.34), (1.5, 0.08)):
        rings.append(uk.ring(c + V((0, -math.sin(lat) * 0.115, 0)), V((1, 0, 0)), V((0, 0, 1)), 0.175 * sc, 0.16 * sc, 12, 2.2))
    lip = [c + (p - c) * 1.08 + V((0, 0.022, 0)) for p in rings[0]]
    parts.append(loft_ob("poleyn_L", [lip] + rings, [M["plate"]], 40, cap1=True))
    parts.append(rim(lip + [lip[0]], 0.008, M["plate"], "poleynrim"))
    f = c + V((0.165, 0.045, 0.0))
    grid = []
    for r in range(4):
        t = r / 3
        grid.append([f + V((0.02 + 0.055 * t * (1 - abs(s) * 0.4), -0.045 + 0.18 * t - 0.05 * (1 - s * s) * t, s * 0.13 * (1 - 0.35 * t)))
                     for s in (lerp(-1.0, 1.0, j / 6) for j in range(7))])
    parts.append(shell("fan_L", grid, [M["plate"]], 0.012, 0.0))
    parts.append(rivets([(c + V((0.15, 0.0, 0.0)), V((1, 0, 0)))], 0.013, M["trim"], "fanrivet"))
    return uk.join(parts, "shin_L")


def thigh(rig, M):
    """The left cuisse, tapering to the knee and strapped behind, and three
    tassets hanging from the hip over the front and side of the thigh,
    riding it so the leg never passes through them."""
    h = rig.pivot("legup_L")
    k = rig.pivot("leglow_L")
    bot = k + V((0.0, 0.02, 0.04))
    top_ = h + V((0.02, 0.0, 0.0))
    ts = (0.0, 0.12, 0.4, 0.7, 0.86, 0.87, 1.0)
    radii = ((0.14, 0.15, 0.15), (0.152, 0.164, 0.164), (0.18, 0.198, 0.198), (0.203, 0.222, 0.222), (0.208, 0.228, 0.228),
             (0.216, 0.236, 0.236), (0.21, 0.226, 0.226))
    path = [lerp(bot, top_, t) for t in ts]
    rings = [ring2((p.x, p.y), rx, ryf, ryb, p.z, 12, power=2.2, ridge=0.015, ridge_w=0.3) for p, (rx, ryf, ryb) in zip(path, radii)]
    lip = [V((p.x, p.y, p.z - 0.014)) for p in grow(rings[0], (bot.x, bot.y), -0.022)]
    parts = [loft_ob("thigh_L", [lip] + rings, [M["plate"]], 38, cap1=True)]
    for idx in (0, 4):
        parts.append(rim(rings[idx] + [rings[idx][0]], 0.007, M["plate"], "cuisserim"))
    p = lerp(bot, top_, 0.5)
    c = (p.x, p.y)
    parts += strap(c, ring2(c, 0.195, 0.215, 0.215, p.z, 16), 0.04, 0.01, M["leather"], M["trim"], buckle_at=8, name="cuissestrap_L")
    # the tassets, each lapping over the one below, cut square across the thigh
    for j, (z0, z1, r0) in enumerate(((2.2, 1.98, 0.3), (2.0, 1.79, 0.31), (1.81, 1.6, 0.315))):
        grid = []
        for z, rr in ((z0, r0), (z1, r0 + 0.035)):
            row = []
            for i in range(9):
                ang = lerp(-0.55, 1.45, i / 8)
                row.append(V((h.x + 0.02 + math.sin(ang) * rr, h.y - math.cos(ang) * (rr + 0.02), z)))
            grid.append(row)
        parts.append(shell("tasset%d_L" % j, grid, [M["plate"]], 0.014, -1.0, centre=(h.x, h.y)))
        parts.append(rim(grow(grid[1], (h.x, h.y), 0.004), 0.008, M["plate"], "tassetrim"))
        mid = grid[0][4].lerp(grid[1][4], 0.35)
        nrm = V((mid.x - h.x, mid.y - h.y, 0)).normalized()
        parts.append(rivets([(mid + nrm * 0.012, nrm)], 0.012, M["trim"], "tassetrivet"))
    return uk.join(parts, "thigh_L")


# ---------------------------------------------------------------- hips and torso

TORSO = ((2.32, 0.29, 0.23, 0.22), (2.45, 0.295, 0.235, 0.225), (2.58, 0.32, 0.25, 0.235), (2.72, 0.37, 0.28, 0.25),
         (2.86, 0.42, 0.3, 0.265), (2.98, 0.44, 0.29, 0.27), (3.07, 0.4, 0.26, 0.255), (3.14, 0.32, 0.22, 0.225), (3.19, 0.22, 0.18, 0.18))


def torso_at(z):
    for (z0, *r0), (z1, *r1) in zip(TORSO, TORSO[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / max(1e-6, z1 - z0)
            return [lerp(x, y, t) for x, y in zip(r0, r1)]
    return list(TORSO[0][1:]) if z < TORSO[0][0] else list(TORSO[-1][1:])


def bust(x, z):
    """How far the breastplate stands forward of its plain ellipse: a modest
    moulded chest, the plates shaped as an armourer would for a woman."""
    return 0.05 * sum(math.exp(-((x - s * 0.15) / 0.1) ** 2) for s in (-1, 1)) * math.exp(-((z - 2.86) / 0.11) ** 2)


def torso_ring(z, n=28, grow_=0.0):
    rx, ryf, ryb = torso_at(z)
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        ct, st = math.cos(t), math.sin(t)
        x = math.copysign(abs(ct) ** (2.0 / 2.3), ct) * (rx + grow_)
        sy = math.copysign(abs(st) ** (2.0 / 2.3), st)
        y = sy * ((ryf if sy < 0 else ryb) + grow_)
        if sy < 0:
            y -= bust(x, z) * (-sy) ** 0.5
        pts.append(V((x, y, z)))
    return pts


def hip(rig, M):
    """The belt, two lames of faulds flaring over the hips with rivets, a
    short skirt of mail under them, and a panel of the player's colour
    hanging in front from the belt."""
    c = (0.0, 0.02)
    n = 22
    parts = []
    for k, (z0, z1, r0, r1, dip) in enumerate(((2.36, 2.24, (0.32, 0.26, 0.245), (0.37, 0.31, 0.28), 0.02),
                                              (2.26, 2.13, (0.365, 0.305, 0.275), (0.415, 0.355, 0.31), 0.035))):
        top = ring2(c, *r0, z0, n)
        low = ring2(c, *r1, z1, n)
        for p in low:
            p.z -= dip * max(0.0, -(p.y - c[1]) / r1[1]) ** 2
        parts.append(shell("fauld%d" % k, [top + [top[0]], low + [low[0]]], [M["plate"]], 0.012, -1.0, centre=c))
        parts.append(rim(grow(low, c, 0.005), 0.008, M["plate"], "fauldrim", closed=True))
        pts = []
        for i in (n // 2 - 2, n // 2 + 2, 0, n - 2, 2):
            p = lerp(top[i], low[i], 0.45)
            nrm = V((p.x - c[0], p.y - c[1], 0)).normalized()
            pts.append((p + nrm * 0.008, nrm))
        parts.append(rivets(pts, 0.012, M["trim"], "fauldrivets"))
    mail = []
    for z, rx, ryf, ryb in ((2.15, 0.4, 0.34, 0.3), (1.98, 0.42, 0.36, 0.32), (1.86, 0.43, 0.37, 0.33)):
        r = ring2(c, rx, ryf, ryb, z, n)
        mail.append(r + [r[0]])
    parts.append(mail_patch(mail, M["mail"], "mailskirt"))
    rings = [ring2(c, 0.3, 0.24, 0.23, z, n) for z in (2.45, 2.445, 2.355, 2.35)]
    rings[1], rings[2] = grow(rings[1], c, 0.015), grow(rings[2], c, 0.015)
    parts.append(loft_ob("belt", rings, [M["oxblood"]], 50))
    bm = bmesh.new()
    fr = [V((x, -0.262, z)) for x, z in ((-0.06, 2.35), (0.06, 2.35), (0.06, 2.45), (-0.06, 2.45))]
    uk.loft(bm, tube_rings(fr + [fr[0]], [(0.011, 0.011)] * 5, 5))
    parts.append(uk.shade(uk.mesh("buckle", bm, [M["trim"]]), 50))
    # the panel: wool in the player's colour, falling in a few folds, its hem worn
    grid = []
    cols, rows = 8, 6
    for r in range(rows + 1):
        t = r / rows
        z = lerp(2.36, 1.66, t)
        row = []
        for ci in range(cols + 1):
            s = lerp(-1.0, 1.0, ci / cols)
            hw = lerp(0.12, 0.15, t)
            x = s * hw
            y = -0.262 - 0.016 - 0.13 * t ** 1.3 - 0.012 * math.cos(s * math.pi * 1.5) * t
            row.append(V((x, y, z - 0.02 * t * (1 - s * s))))
        grid.append(row)
    parts.append(shell("panel", grid, [M["teamcolor"], M["teamcolor_lining"]], 0.012, 1.0, 1))
    return uk.join(parts, "hip")


def torso(rig, M):
    """The cuirass, moulded at the chest and drawn in at the waist, with
    rolled edges at the neck and arms, a plackart over the belly rising to a
    point, a high collar that stands about the throat and flares at its
    lip, mail in the armpits, and slots in the backplate for the wings."""
    parts = []
    n = 28
    rings = [torso_ring(z, n) for z, *_ in TORSO]
    parts.append(loft_ob("cuirass", rings, [M["plate"]], 32, cap0=True, cap1=True))
    parts.append(rim(grow(rings[-1], (0.0, 0.0), 0.004), 0.01, M["plate"], "neckrim", closed=True))
    # the plackart, a little proud of the breastplate, its top a point under the chest
    grid = []
    cols, rows = 10, 5
    for r in range(rows + 1):
        row = []
        for k in range(cols + 1):
            ph = lerp(-1.25, 1.25, k / cols)
            top = 2.7 - 0.24 * (abs(ph) / 1.25) ** 1.2
            z = lerp(2.34, top, r / rows)
            rx, ryf, ryb = torso_at(z)
            x = math.sin(ph) * (rx + 0.02)
            y = -math.cos(ph) * (ryf + 0.02) - bust(x, z) * math.cos(ph) ** 0.5
            row.append(V((x, y, z)))
        grid.append(row)
    parts.append(shell("plackart", grid, [M["plate"]], 0.012, -1.0, centre=(0.0, 0.0)))
    parts.append(rim([p + (p - V((0, 0, p.z))).normalized() * 0.01 for p in grid[-1]], 0.01, M["plate"], "plackartrim"))
    rv = []
    for k in (2, cols // 2, cols - 2):
        p = lerp(grid[1][k], grid[2][k], 0.5)
        nrm = (p - V((0, 0, p.z))).normalized()
        rv.append((p + nrm * 0.013, nrm))
    parts.append(rivets(rv, 0.012, M["trim"], "plackartrivets"))
    # the collar: two lames, high at the sides and back, lower under the chin
    for j, (z0, h0, r0, r1) in enumerate(((3.13, 0.11, 0.215, 0.18), (3.21, 0.13, 0.18, 0.172))):
        lo, hi = [], []
        for i in range(24):
            t = 2 * math.pi * i / 24
            front = max(0.0, -math.sin(t))  # 1 straight ahead (-Y)
            back = max(0.0, math.sin(t))
            hgt = h0 * (1.0 - 0.6 * front ** 2 - 0.15 * back ** 2)
            lo.append(V((math.cos(t) * r0, math.sin(t) * r0 * 0.93 + 0.01, z0)))
            hi.append(V((math.cos(t) * r1 * (1.0 + 0.14 * j), math.sin(t) * r1 * 0.93 * (1.0 + 0.14 * j) + 0.01, z0 + hgt)))
        parts.append(shell("collar%d" % j, [lo + [lo[0]], hi + [hi[0]]], [M["plate"]], 0.012, -1.0, centre=(0.0, 0.01)))
        parts.append(rim(grow(hi, (0.0, 0.01), 0.003), 0.008, M["plate"], "collarrim", closed=True))
    for side in (-1, 1):
        grid = []
        for r in range(3):
            z = lerp(2.98, 2.74, r / 2)
            rx = torso_at(z)[0]
            grid.append([V((side * (rx * 0.97 + 0.01), lerp(-0.16, 0.16, k / 4), z)) for k in range(5)])
        parts.append(mail_patch(grid, M["mail"], "armpit"))
        # a rolled slot in the backplate where each wing comes out
        c = V((side * 0.12, 0.0, 2.94))
        ring_ = []
        for i in range(10):
            t = 2 * math.pi * i / 10
            p = c + V((math.cos(t) * 0.1, 0.0, math.sin(t) * 0.13))
            ring_.append(V((p.x, torso_at(p.z)[2] + 0.012, p.z)))
        parts.append(rim(ring_, 0.014, M["plate"], "wingslot", closed=True))
    return uk.join(parts, "torso")


# ---------------------------------------------------------------- shoulders and arms

def pauldron(rig, M):
    """The left pauldron: a blackened cop over the shoulder, rising to a
    ridge, and two lames below it enamelled in the player's colour, each
    with a rolled edge, stepping down the arm."""
    s = rig.pivot("gardbrace_L")
    c = V((s.x + 0.04, s.y, s.z - 0.11))
    parts = []

    def plate(a0, a1, rad, ys, lift, name, mats, cols=7):
        grid = []
        ymax = max(abs(ys[0]), abs(ys[-1])) + 0.012
        for y in ys:
            f = math.sqrt(max(0.0, 1.0 - (y / ymax) ** 2))
            row = []
            for j in range(cols):
                a = lerp(a0, a1, j / (cols - 1))
                r = rad * max(0.2, f) ** 0.75
                row.append(V((c.x + math.sin(a) * r, c.y + y, c.z + math.cos(a) * r * 0.8 - lift)))
            grid.append(row)
        sh = shell(name, grid, mats, 0.018, -1.0, 0, 1, centre=tuple(c))
        return sh, [row[-1] for row in grid], grid

    ys = [lerp(-0.29, 0.29, k / 8) for k in range(9)]
    cop, edge, grid = plate(-0.25, 1.3, 0.26, ys, 0.0, "pauldron_L", [M["plate"], M["plate"]], 8)
    parts += [cop, rim(edge, 0.011, M["plate"], "copedge"), rim([row[0] for row in grid], 0.011, M["plate"], "copinner")]
    for k in range(2):
        ys = [lerp(-0.26 + 0.025 * k, 0.26 - 0.025 * k, j / 6) for j in range(7)]
        lm, edge, _ = plate(0.95 + 0.1 * k, 1.6 + 0.06 * k, 0.27 + 0.012 * k, ys, 0.045 + 0.06 * k, "pauldronlame_L",
                            [M["teamcolor_enamel"], M["plate"]], 5)
        parts += [lm, rim(edge, 0.01, M["plate"], "lameedge")]
    # a ridge along the cop, highest over the middle, leaning out
    base, tip = [], []
    for k in range(9):
        y = lerp(-0.22, 0.22, k / 8)
        a = 0.55
        f = math.sqrt(max(0.0, 1.0 - (y / 0.302) ** 2))
        r = 0.26 * max(0.2, f) ** 0.75
        p = V((c.x + math.sin(a) * r, c.y + y, c.z + math.cos(a) * r * 0.8))
        hgt = 0.015 + 0.04 * math.cos(y / 0.22 * math.pi / 2) ** 2
        base.append(p - V((0.01, 0, 0.01)))
        tip.append(p + V((0.5 * hgt, 0, hgt)))
    parts.append(shell("ridge_L", [base, tip], [M["plate"]], 0.014, 0.0))
    parts.append(rim(tip, 0.007, M["plate"], "ridgerim"))
    parts.append(rivets([(grid[g][1] + V((0, 0, 0.012)), V((0, 0, 1))) for g in (1, 4, 7)], 0.012, M["trim"], "coprivets"))
    return uk.join(parts, "pauldron_L")


def upper_arm(rig, M):
    """The left rerebrace, stepped by a lame at the top, rolled at its foot."""
    u = rig.pivot("armup_L")
    e = rig.pivot("armlow_L")
    path = [lerp(u + V((0.05, 0.0, 0.06)), e + V((0.0, -0.005, 0.03)), t) for t in (0.0, 0.2, 0.21, 0.6, 1.0)]
    rings = tube_rings(path, [(0.165, 0.16), (0.168, 0.163), (0.175, 0.17), (0.162, 0.158), (0.146, 0.143)], 12)
    parts = [loft_ob("upperarm_L", rings, [M["plate"]], 40, cap0=True)]
    for idx in (2, 4):
        parts.append(rim(rings[idx] + [rings[idx][0]], 0.007, M["plate"], "rerebracerim"))
    return uk.join(parts, "upperarm_L")


def forearm(rig, M):
    """The left vambrace with a strap, the couter over the elbow with its
    fan, and mail in the crook of the arm."""
    e = rig.pivot("armlow_L")
    w = rig.pivot("hand_L")
    path = [lerp(e + V((0, 0, 0.03)), w + V((0, 0, 0.05)), t) for t in (0.0, 0.35, 0.7, 1.0)]
    rings = tube_rings(path, [(0.145, 0.143), (0.146, 0.14), (0.13, 0.124), (0.117, 0.113)], 12)
    parts = [loft_ob("forearm_L", rings, [M["plate"]], 40, cap0=True)]
    parts.append(rim(rings[-1] + [rings[-1][0]], 0.008, M["plate"], "vambracerim"))
    p = lerp(path[0], path[-1], 0.6)
    rx = lerp(0.146, 0.117, 0.6) + 0.004
    parts += strap((p.x, p.y), ring2((p.x, p.y), rx, rx, rx, p.z, 14), 0.03, 0.008, M["leather"], M["trim"], buckle_at=0, name="vambracestrap_L")
    parts.append(dome(e + V((0.04, 0.07, 0.0)), (0.15, 0.128, 0.128), 12, 7, [M["plate"]], "couter_L"))
    f = e + V((0.155, 0.035, 0.0))
    grid = []
    for r in range(3):
        t = r / 2
        grid.append([f + V((0.02 + 0.045 * t, -0.07 + 0.15 * t - 0.045 * (1 - s_ * s_) * t, s_ * 0.115 * (1 - 0.3 * t)))
                     for s_ in (lerp(-1.0, 1.0, j / 6) for j in range(7))])
    parts.append(shell("couterfan_L", grid, [M["plate"]], 0.012, 0.0))
    grid = []
    for r in range(3):
        p = lerp(e + V((0, 0, 0.09)), e + V((0, 0, -0.07)), r / 2)
        grid.append([p + V((math.cos(a) * 0.14, math.sin(a) * 0.14 - 0.02, 0)) for a in (lerp(2.2, 4.0, j / 4) for j in range(5))])
    parts.append(mail_patch(grid, M["mail"], "elbowmail"))
    return uk.join(parts, "forearm_L")


def hand(rig, M, grip, turn=0.0):
    """The left gauntlet closed round a grip running fore and aft through
    grip (x, z): a flared cuff, a leather glove, plates over the back of the
    hand and knuckles, a plated thumb. turn swings the fist about the
    upright through the grip, for a fist that holds a shield's bar."""
    w = rig.pivot("hand_L")
    gx, gz = grip
    parts = []
    rings = tube_rings([w + V((0.0, 0.0, 0.12)), w, w + V((0.0, 0.0, -0.09))], [(0.145, 0.138), (0.118, 0.112), (0.095, 0.09)], 14)
    parts.append(loft_ob("cuff_L", rings, [M["plate"]], 40))
    parts.append(rim(rings[0] + [rings[0][0]], 0.009, M["plate"], "cuffrim"))
    fist = []
    rings = []
    for y, s, kn in ((-0.1, 0.8, 0.0), (-0.08, 1.0, 0.011), (-0.045, 1.0, 0.004), (-0.035, 1.0, 0.011), (0.0, 1.0, 0.004),
                     (0.011, 0.99, 0.011), (0.045, 0.96, 0.004), (0.056, 0.93, 0.009), (0.09, 0.72, 0.0)):
        r = []
        for i in range(12):
            t = 2 * math.pi * i / 12
            bump = kn * max(0.0, math.cos(t - 0.6)) ** 2
            r.append(V((gx + 0.007 + math.cos(t) * (0.08 * s + bump), w.y + y, gz - 0.005 + math.sin(t) * (0.071 * s + bump))))
        rings.append(r)
    fist.append(loft_ob("glove_L", rings, [M["leather"]], 45, cap0=True, cap1=True))
    for k, (z0, z1) in enumerate(((w.z - 0.01, w.z - 0.065), (w.z - 0.055, w.z - 0.11))):
        rows = [[V((gx + 0.07 + 0.01 * k - 0.022 * a * a, w.y + a * 0.095, z)) for a in (lerp(-1.0, 1.0, i / 6) for i in range(7))] for z in (z0, z1)]
        fist.append(shell("handplate_L", rows, [M["plate"]], 0.012, 0.0))
        fist.append(rim(rows[-1], 0.005, M["plate"], "handplaterim"))
    for k in range(4):
        fist.append(dome(V((gx + 0.054, w.y - 0.067 + k * 0.045, gz - 0.062)), (0.027, 0.02, 0.023), 8, 5, [M["plate"]], "knuckle"))
    path = [V((gx - 0.035, w.y + 0.035, gz + 0.062)), V((gx - 0.062, w.y - 0.027, gz + 0.053)), V((gx - 0.053, w.y - 0.08, gz + 0.027))]
    bm = bmesh.new()
    uk.loft(bm, tube_rings(path, [(0.03, 0.028), (0.028, 0.026), (0.022, 0.02)], 8), cap0=True, cap1=True)
    fist.append(uk.shade(uk.mesh("thumb_L", bm, [M["leather"]]), 45))
    fist.append(dome(V((gx - 0.062, w.y - 0.018, gz + 0.076)), (0.027, 0.04, 0.013), 8, 4, [M["plate"]], "thumbplate"))
    if turn:
        m = Matrix.Translation(V((gx, w.y, 0))) @ Matrix.Rotation(math.radians(turn), 4, "Z") @ Matrix.Translation(V((-gx, -w.y, 0)))
        for ob in fist:
            uk.transform(ob, m)
    return uk.join(parts + fist, "hand_L")


# ---------------------------------------------------------------- the spear and the shield

def spear(rig, M):
    """A long spear as long as the original's, held at rest fore and aft
    through the left fist: a broad leaf head with a midrib and two barbs
    swept back at its foot, a socket with langets nailed to an ash shaft,
    a leather grip, and a butt spike."""
    s = rig.pivot("spear")
    x, z = s.x, s.z
    parts = []
    # the head, a leaf in the xy plane edge up, thick along its midrib
    prof = ((0.0, 0.0), (0.06, 0.035), (0.16, 0.075), (0.3, 0.088), (0.44, 0.07), (0.56, 0.035), (0.64, 0.0))
    tip_y, base_y = -2.07, -2.07 + 0.64
    bm = bmesh.new()
    rows = []
    for t, hw in prof:
        y = tip_y + t
        th = 0.022 * (0.4 + 0.6 * min(1.0, t / 0.25)) if t > 0 else 0.002
        hw = max(hw, 0.002)
        rows.append([V((x, y, z + hw)), V((x + th * 0.35, y, z + hw * 0.45)), V((x + th * 0.5, y, z)), V((x + th * 0.35, y, z - hw * 0.45)),
                     V((x, y, z - hw)), V((x - th * 0.35, y, z - hw * 0.45)), V((x - th * 0.5, y, z)), V((x - th * 0.35, y, z + hw * 0.45))])
    uk.loft(bm, rows, cap1=True)
    parts.append(uk.shade(uk.mesh("spearhead", bm, [M["steel"]]), 28))
    # two barbs at the foot of the leaf, swept back
    for sgn in (-1, 1):
        bm = bmesh.new()
        pts = [V((x, base_y - 0.08, z + sgn * 0.03)), V((x, base_y + 0.02, z + sgn * 0.12)), V((x, base_y + 0.13, z + sgn * 0.16)),
               V((x, base_y + 0.05, z + sgn * 0.08)), V((x, base_y + 0.04, z + sgn * 0.03))]
        top = [bm.verts.new(p + V((0.008, 0, 0))) for p in pts]
        bot = [bm.verts.new(p - V((0.008, 0, 0))) for p in pts]
        bm.faces.new(top)
        bm.faces.new(list(reversed(bot)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            bm.faces.new((top[i], bot[i], bot[j], top[j]))
        parts.append(uk.shade(uk.mesh("barb", bm, [M["steel"]]), 30))
    # socket and langets
    sock = [uk.ring(V((x, y, z)), V((1, 0, 0)), V((0, 0, 1)), r, r, 10) for y, r in ((base_y - 0.03, 0.022), (base_y + 0.06, 0.03), (base_y + 0.2, 0.034), (base_y + 0.22, 0.04), (base_y + 0.25, 0.034))]
    parts.append(loft_ob("socket", sock, [M["trim"]], 40, cap0=True, cap1=True))
    for sgn in (-1, 1):
        g = [[V((x + sgn * 0.034, base_y + 0.25 + 0.15 * t, z + dz)) for dz in (-0.012, 0.012)] for t in (0.0, 1.0)]
        parts.append(shell("langet", g, [M["trim"]], 0.006, 0.0))
    # the shaft, ash, with a leather grip under the fist and a spike at the butt
    shaft = [uk.ring(V((x, y, z)), V((1, 0, 0)), V((0, 0, 1)), r, r, 10) for y, r in ((base_y + 0.24, 0.028), (-0.6, 0.03), (0.6, 0.031), (1.28, 0.03))]
    parts.append(loft_ob("shaft", shaft, [M["wood"]], 50, cap0=True, cap1=True))
    grip = [uk.ring(V((x, y, z)), V((1, 0, 0)), V((0, 0, 1)), 0.036, 0.036, 10) for y in (s.y - 0.16, s.y + 0.18)]
    parts.append(loft_ob("grip", grip, [M["leather"]], 50, cap0=True, cap1=True))
    spike = [uk.ring(V((x, y, z)), V((1, 0, 0)), V((0, 0, 1)), r, r, 8) for y, r in ((1.24, 0.034), (1.3, 0.036), (1.36, 0.022))]
    spike.append([V((x, 1.45, z))] * 8)
    parts.append(loft_ob("buttspike", spike, [M["trim"]], 40, cap0=True))
    return uk.join(parts, "spear")


def shield(rig, M):
    """A round shield held by its bar in the right fist, facing ahead: a
    shallow dome with a rolled rim, blackened at its heart round the boss
    and enamelled in the player's colour in a broad ring outside, cut by
    six blackened spokes like a wheel."""
    w = rig.pivot("hand_R")
    c = V((w.x - 0.04, -0.33, 1.75))
    R = 0.47
    parts = []

    def face_at(r, t, dz=0.0):
        k = r / R
        return c + V((math.cos(t) * r, -0.07 * (1.0 - k * k) - dz, math.sin(t) * r))

    n = 36
    rings = [[face_at(R * k, 2 * math.pi * i / n) for i in range(n)] for k in (1.0, 0.85, 0.63, 0.6, 0.4, 0.2)]
    rings.append([c + V((0, -0.07, 0))] * n)
    field = uk.mesh("shieldface", _disc(rings), [M["teamcolor_enamel"], M["plate"]], outward=False)
    for p in field.data.polygons:
        q = p.center - c
        p.material_index = 0 if math.hypot(q.x, q.z) > 0.615 * R else 1
    m = field.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.02, 1.0
    uk.apply(field)
    parts.append(uk.shade(field, 50))
    edge = [face_at(R, 2 * math.pi * i / n, 0.006) for i in range(n)]
    parts.append(rim(edge, 0.026, M["plate"], "shieldrim", sides=6, closed=True))
    for k in range(6):
        t = 2 * math.pi * k / 6 + math.pi / 2
        a, b = face_at(0.1, t, 0.026), face_at(R - 0.02, t, 0.026)
        bm = bmesh.new()
        side = V((-math.sin(t), 0, math.cos(t)))
        sheet(bm, [[a - side * 0.022, b - side * 0.032], [a + side * 0.022, b + side * 0.032]])
        sp = uk.mesh("spoke", bm, [M["plate"]], outward=False)
        mm = sp.modifiers.new("solid", "SOLIDIFY")
        mm.thickness, mm.offset = 0.012, 1.0
        uk.apply(sp)
        parts.append(uk.shade(sp, 40))
        parts.append(rivets([(face_at(R - 0.06, t, 0.036), V((0, -1, 0)))], 0.014, M["trim"], "shieldrivet"))
    parts.append(dome(c + V((0, -0.085, 0)), (0.12, 0.07, 0.12), 14, 7, [M["plate"]], "boss"))
    parts.append(rim([c + V((math.cos(t) * 0.125, -0.075, math.sin(t) * 0.125)) for t in (2 * math.pi * i / 16 for i in range(16))],
                     0.012, M["plate"], "bossrim", closed=True))
    # the bar the fist holds, behind the boss
    bm = bmesh.new()
    uk.loft(bm, tube_rings([c + V((-0.11, 0.06, 0)), c + V((-0.08, 0.1, 0)), c + V((0.08, 0.1, 0)), c + V((0.11, 0.06, 0))], [(0.016, 0.016)] * 4, 6), cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("shieldbar", bm, [M["leather"]]), 50))
    return uk.join(parts, "shield")


def _disc(rings):
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in r] for r in rings[:-1]]
    centre = bm.verts.new(rings[-1][0])
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    for i in range(n):
        bm.faces.new((vs[-1][i], vs[-1][(i + 1) % n], centre))
    return bm


# ---------------------------------------------------------------- the wings

def bone(path, radii, mat, name, n=8):
    bm = bmesh.new()
    rings = tube_rings(path, [(r, r * 0.85) for r in radii], n)
    uk.loft(bm, rings, cap0=True, cap1=True)
    return uk.shade(uk.mesh(name, bm, [mat]), 50)


def claw(base, direction, length, curl, mat, name, r=0.03):
    """A horn claw from base along direction, hooking toward curl."""
    d = direction.normalized()
    path = [base + d * length * t + curl.normalized() * length * 0.35 * t * t for t in (0.0, 0.35, 0.7, 1.0)]
    return bone(path, [r, r * 0.75, r * 0.4, 0.002], mat, name, 6)


def membrane(W, rays, M, name, depth=0.0, rows=5):
    """Skin stretched between rays from the wrist W: each ray (angle in the
    wing's plane from straight out, length), the edge between tips scalloped
    toward the wrist, billowing back between the fingers and curving back
    toward the tips. depth sets it a little behind another membrane."""
    bm = bmesh.new()
    cols = 4
    grid = []
    tips = [W + V((math.cos(a) * L, 0.0, math.sin(a) * L)) for a, L in rays]
    for k in range(len(rays) - 1):
        for ci in range(cols + (1 if k == len(rays) - 2 else 0)):
            s = ci / cols
            e = tips[k].lerp(tips[k + 1], s)
            e = e + (W - e) * (0.11 * math.sin(math.pi * s))
            col = []
            for r in range(rows + 1):
                t = 0.04 + 0.96 * r / rows
                p = W + (e - W) * t
                p.y += depth + 0.05 * t * t + 0.05 * math.sin(math.pi * s) * math.sin(math.pi * min(1.0, t * 1.1))
                col.append(p)
            grid.append(col)
    vs = [[bm.verts.new(p) for p in col] for col in grid]
    for c in range(len(grid) - 1):
        for r in range(rows):
            bm.faces.new((vs[c][r], vs[c + 1][r], vs[c + 1][r + 1], vs[c][r + 1]))
    ob = uk.mesh(name, bm, [M["teamcolor_membrane"]], outward=False)
    m = ob.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.008, 0.0
    uk.apply(ob)
    return uk.shade(ob, 60), tips


def finger(W, a, L, M, name, depth=0.0):
    """A finger bone from the wrist along angle a, a knuckle partway."""
    d = V((math.cos(a), 0.0, math.sin(a)))
    pts = [W + d * L * t + V((0, depth + 0.05 * t * t, 0)) for t in (0.02, 0.4, 0.42, 0.75, 1.0)]
    return bone(pts, [0.03, 0.022, 0.026, 0.016, 0.006], M["wingskin"], name, 7)


# The wing at rest, its fingers' angles in degrees in the wing's plane from
# straight out (up positive), and lengths in cells. The script fans wing2's
# and wing3's fingers about the wrist, so each piece's skin runs a little
# under the next one's, and no gap opens as they fold.
WING2 = ((6, 1.45, True), (-25, 2.05, True), (-43, 1.8, False))
WING3 = ((-39, 1.85, False), (-55, 2.0, True), (-88, 1.4, True), (-114, 1.15, False))


def wing(rig, M):
    """The left wing on its four pieces: a knot of skin and muscle where it
    leaves the backplate (winganchor), the arm from the shoulder blade to
    the wrist with the skin between it and the body and a thumb claw
    (wing1), and the fingers with their skin (wing2, the leading fingers;
    wing3, the trailing ones)."""
    R = rig.pivot("wing1_L")
    W = rig.pivot("wing2_L")
    anchor = rig.pivot("winganchor_L")
    out = {}
    out["winganchor_L"] = uk.join([bone([V((anchor.x, 0.2, anchor.z)), V((R.x, R.y - 0.05, R.z)), R + V((0.02, 0.02, 0.0))],
                                        [0.085, 0.08, 0.07], M["wingskin"], "wingroot_L", 10)], "wingroot_L")
    E = R + V((0.4, 0.035, 0.08))
    arm = [bone([R, R.lerp(E, 0.5), E], [0.07, 0.055, 0.058], M["wingskin"], "humerus_L"),
           bone([E, E.lerp(W, 0.3), E.lerp(W, 0.7), W], [0.05, 0.04, 0.035, 0.04], M["wingskin"], "forearmbone_L"),
           dome(E, (0.065, 0.06, 0.06), 10, 6, [M["wingskin"]], "elbow_L"), dome(W, (0.06, 0.055, 0.06), 10, 6, [M["wingskin"]], "wrist_L")]
    arm.append(claw(W + V((0.0, -0.01, 0.03)), W - E + V((0, 0, 0.3)), 0.24, V((0.3, -0.2, -0.6)), M["claw"], "thumbclaw_L"))
    # the skin from the body along the arm to the wrist, down to under the wrist
    lead = [R + V((0.02, -0.01, 0.07)), R.lerp(E, 0.5) + V((0, 0, 0.05)), E + V((0, 0, 0.045)), E.lerp(W, 0.5) + V((0, 0, 0.03)), W]
    bottom = V((R.x + 0.08, R.y - 0.01, R.z - 0.3))
    under = W + V((math.cos(math.radians(-96)) * 1.2, 0.03, math.sin(math.radians(-96)) * 1.2))
    trail = []
    for i in range(len(lead)):
        s = i / (len(lead) - 1)
        p = bottom.lerp(under, s)
        p.z += 0.16 * math.sin(math.pi * s)
        trail.append(p)
    grid = [[lead[i].lerp(trail[i], r / 5) + V((0, 0.03 * math.sin(math.pi * r / 5) * math.sin(math.pi * i / 4), 0)) for r in range(6)] for i in range(len(lead))]
    bm = bmesh.new()
    sheet(bm, grid)
    skin = uk.mesh("armskin_L", bm, [M["teamcolor_membrane"]], outward=False)
    m = skin.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.008, 0.0
    uk.apply(skin)
    arm.append(uk.shade(skin, 60))
    out["wing1_L"] = uk.join(arm, "wingarm_L")
    for piece, rays, depth in (("wing2_L", WING2, 0.0), ("wing3_L", WING3, 0.012)):
        skin_, tips = membrane(W, [(math.radians(a), L) for a, L, _ in rays], M, piece + "_skin", depth)
        bones = [finger(W, math.radians(a), L, M, piece + "_finger", depth) for a, L, b in rays if b]
        out[piece] = uk.join([skin_] + bones, piece)
    return out
