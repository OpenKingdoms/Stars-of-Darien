"""Kirenna's armour, trident, cape and tail for kirenna.py: thin polished
steel plate fitted close to a woman's frame, with rolled edges and scales
etched on the cuisses and the plackart, a moulded breastplate drawn in at
a narrow waist under a gold silk sash from her right shoulder to a knot at
her left hip, a collar, small flat pauldrons enamelled in the player's
colour with a smooth fin plate rising beside the neck, a mail skirt with a
scalloped hem, gauntlets, one closed on the trident and one open to cast,
a barbed trident with a wave on its socket, and a cape in the player's
colour with a gold border. The pieces the script shows in deep water
carry a mermaid's tail with its flukes, and a fin down her back. Every
part is modelled at rest around the original's pivots (kirenna.py
attaches them). Left parts are mirrored to the right.
"""
import math

import bmesh
from mathutils import Euler, Matrix, Vector

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


def lame(name, rings, mat, thick=0.008):
    """A plate between rings, thickened inward."""
    bm = bmesh.new()
    uk.loft(bm, rings)
    ob = uk.mesh(name, bm, [mat], outward=False)
    m = ob.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = thick, -1.0
    uk.apply(ob)
    return uk.shade(ob, 40)


def fin(rays, mat, name, thick=0.006, sag=0.3, rows=3, ray_mat=None, ray_r=0.007):
    """Skin between rays (base, tip), its edge sagging between their tips,
    with each ray a tapering rod."""
    cols = []
    for k in range(len(rays) - 1):
        for s in (0.0, 0.5):
            b = rays[k][0].lerp(rays[k + 1][0], s)
            e = rays[k][1].lerp(rays[k + 1][1], s)
            e = e + (b - e) * (sag * math.sin(math.pi * s))
            cols.append([b.lerp(e, r / rows) for r in range(rows + 1)])
    cols.append([rays[-1][0].lerp(rays[-1][1], r / rows) for r in range(rows + 1)])
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in col] for col in cols]
    for c in range(len(cols) - 1):
        for r in range(rows):
            bm.faces.new((vs[c][r], vs[c + 1][r], vs[c + 1][r + 1], vs[c][r + 1]))
    web = uk.mesh(name, bm, [mat], outward=False)
    m = web.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = thick, 0.0
    uk.apply(web)
    parts = [uk.shade(web, 50)]
    for b, e in rays:
        bm = bmesh.new()
        uk.loft(bm, tube_rings([b, b.lerp(e, 0.5), e], [(ray_r, ray_r * 0.8), (ray_r * 0.7, ray_r * 0.6), (ray_r * 0.2, ray_r * 0.2)], 5), cap0=True, cap1=True)
        parts.append(uk.shade(uk.mesh(name + "ray", bm, [ray_mat or mat]), 50))
    return parts


# ---------------------------------------------------------------- legs

def foot(rig, M):
    """The left sabaton: a pointed toe, four lames over the instep with
    rolled edges, a heel cup and an ankle strap, on a leather sole."""
    a = rig.pivot("FootL")
    cx, y0 = a.x, a.y - 0.05
    parts = []

    def sec(y):
        t = min(1.0, max(0.0, -y / 0.36))
        return 0.11 * (1 - t ** 3.0) + 0.027, 0.22 * (1 - t ** 1.5) + 0.045

    bm = bmesh.new()
    rings = []
    for y, hw, top in ((0.23, 0.07, 0.24), (0.19, 0.1, 0.3), (0.09, 0.114, 0.3), (0.0, 0.118, 0.25)):
        h = top / 2.0
        r = uk.ring(V((cx, y0 + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw, h, 10, power=3.0, start=-math.pi / 2)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    uk.loft(bm, rings, cap0=True, cap1=True)
    heel = uk.mesh("heel_L", bm, [M["plate"], M["leather"]])
    for p in heel.data.polygons:
        if p.normal.z < -0.6 or p.center.z < 0.02:
            p.material_index = 1
    parts.append(uk.shade(heel, 40))
    for k, (ya, yb) in enumerate(((0.0, -0.06), (-0.06, -0.12), (-0.12, -0.18), (-0.18, -0.24))):
        rings = []
        for y, g in ((ya + 0.012, 0.0), (yb, 0.006)):
            hw, top = sec(y)
            h = top / 2.0
            r = uk.ring(V((cx, y0 + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw + g, h + g, 10, power=3.0, start=-math.pi / 2)
            rings.append([V((p.x, p.y, max(0.012, p.z))) for p in r])
        parts.append(lame("sabaton%d_L" % k, rings, M["plate"]))
        parts.append(rim([p for p in rings[-1] if p.z > 0.02], 0.006, M["gold"], "sabatonrim", sides=4))
    rings = []
    for y in (-0.24, -0.29, -0.33, -0.36):
        hw, top = sec(y)
        h = max(0.018, top / 2.0)
        r = uk.ring(V((cx, y0 + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw, h, 10, power=3.0, start=-math.pi / 2)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    rings.append([V((cx, y0 - 0.4, 0.015))] * len(rings[-1]))
    parts.append(loft_ob("toe_L", rings, [M["plate"]], 40))
    bm = bmesh.new()
    sole = [V((cx + math.cos(t) * 0.11, y0 - 0.08 + math.sin(t) * 0.31, 0.0)) for t in (2 * math.pi * k / 16 for k in range(16))]
    vs = [bm.verts.new(p) for p in sole] + [bm.verts.new(p + V((0, 0, 0.025))) for p in sole]
    bm.faces.new(vs[:16][::-1])
    for k in range(16):
        bm.faces.new((vs[k], vs[(k + 1) % 16], vs[16 + (k + 1) % 16], vs[16 + k]))
    parts.append(uk.shade(uk.mesh("sole_L", bm, [M["leather"]]), 50))
    c = (cx, y0 + 0.11)
    parts += strap(c, ring2(c, 0.108, 0.118, 0.108, 0.23, 14), 0.028, 0.008, M["leather"], M["gold"], buckle_at=0, name="anklestrap_L")
    return uk.join(parts, "foot_L")


def shin(rig, M):
    """The left greave, slim at the ankle and full at the calf with a ridge
    down the shin, and a domed poleyn over the knee with a fin-shaped wing
    on its outer side."""
    k = rig.pivot("LegLL")
    a = rig.pivot("FootL")
    top_ = k + V((0.0, 0.03, -0.03))
    bot = a + V((0.0, -0.02, 0.1))
    ts = (0.0, 0.08, 0.35, 0.6, 0.82, 0.88, 1.0)
    radii = ((0.086, 0.09, 0.09), (0.095, 0.098, 0.098), (0.11, 0.112, 0.138), (0.124, 0.118, 0.158),
             (0.122, 0.118, 0.146), (0.126, 0.12, 0.136), (0.117, 0.115, 0.11))
    path = [lerp(bot, top_, t) for t in ts]
    rings = [ring2((p.x, p.y), rx, ryf, ryb, p.z, 12, power=2.2, ridge=0.014, ridge_w=0.35) for p, (rx, ryf, ryb) in zip(path, radii)]
    lip = [V((p.x, p.y, p.z - 0.012)) for p in grow(rings[0], (bot.x, bot.y), -0.02)]
    parts = [loft_ob("shin_L", [lip] + rings, [M["plate"]], 38, cap1=True)]
    parts.append(rim(rings[0] + [rings[0][0]], 0.007, M["gold"], "greaverim"))
    for t in (0.3, 0.7):
        p = lerp(bot, top_, t)
        rx, ryf, ryb = radii[2] if t < 0.5 else radii[4]
        c = (p.x, p.y)
        parts += strap(c, ring2(c, rx, ryf, ryb, p.z, 16), 0.03, 0.007, M["leather"], M["gold"], buckle_at=4, name="greavestrap_L")
    c = k + V((0.01, -0.06, 0.03))
    rings = []
    for lat, sc in ((0.0, 1.0), (0.3, 0.98), (0.65, 0.88), (1.0, 0.66), (1.3, 0.34), (1.5, 0.08)):
        rings.append(uk.ring(c + V((0, -math.sin(lat) * 0.09, 0)), V((1, 0, 0)), V((0, 0, 1)), 0.142 * sc, 0.13 * sc, 12, 2.2))
    lip = [c + (p - c) * 1.06 + V((0, 0.018, 0)) for p in rings[0]]
    parts.append(loft_ob("poleyn_L", [lip] + rings, [M["plate"]], 40, cap1=True))
    parts.append(rim(lip + [lip[0]], 0.007, M["gold"], "poleynrim"))
    # the wing: a fin of steel ribs fanning back from the knee's outer side
    w = c + V((0.132, 0.02, 0.0))
    rays = [(w + V((0.0, 0.0, dz * 0.5)), w + V((0.03, 0.08 + 0.04 * math.cos(dz * 7), dz))) for dz in (-0.095, -0.03, 0.03, 0.095)]
    parts += fin(rays, M["plate"], "kneefin_L", thick=0.007, sag=0.25, ray_mat=M["gold"], ray_r=0.008)
    return uk.join(parts, "shin_L")


def thigh(rig, M):
    """The left cuisse of steel etched with scales, full at the hip and
    tapering to the knee, rolled at its foot and strapped behind."""
    h = rig.pivot("LegUL")
    k = rig.pivot("LegLL")
    bot = k + V((0.0, 0.02, 0.05))
    top_ = h + V((0.0, 0.0, 0.08))
    ts = (0.0, 0.1, 0.4, 0.7, 0.9, 1.0)
    radii = ((0.118, 0.126, 0.126), (0.128, 0.136, 0.136), (0.146, 0.158, 0.158), (0.158, 0.17, 0.17), (0.16, 0.172, 0.172), (0.154, 0.166, 0.166))
    path = [lerp(bot, top_, t) for t in ts]
    rings = [ring2((p.x, p.y), rx, ryf, ryb, p.z, 14, power=2.2, ridge=0.012, ridge_w=0.3) for p, (rx, ryf, ryb) in zip(path, radii)]
    lip = [V((p.x, p.y, p.z - 0.014)) for p in grow(rings[0], (bot.x, bot.y), -0.022)]
    parts = [loft_ob("thigh_L", [lip] + rings, [M["etched"]], 38, cap1=True)]
    parts.append(rim(rings[0] + [rings[0][0]], 0.007, M["gold"], "cuisserim"))
    p = lerp(bot, top_, 0.55)
    c = (p.x, p.y)
    parts += strap(c, ring2(c, 0.153, 0.165, 0.165, p.z, 16), 0.034, 0.007, M["leather"], M["gold"], buckle_at=8, name="cuissestrap_L")
    return uk.join(parts, "thigh_L")


# ---------------------------------------------------------------- hips and torso

TORSO = ((2.29, 0.285, 0.235, 0.22), (2.4, 0.268, 0.228, 0.212), (2.52, 0.285, 0.235, 0.218), (2.66, 0.325, 0.258, 0.232),
         (2.8, 0.355, 0.276, 0.244), (2.94, 0.365, 0.278, 0.25), (3.06, 0.355, 0.26, 0.245), (3.16, 0.33, 0.232, 0.232),
         (3.25, 0.28, 0.195, 0.202), (3.31, 0.2, 0.16, 0.168))
WAIST_Y = 0.02  # the torso's axis, a little behind the hips'


def torso_at(z):
    for (z0, *r0), (z1, *r1) in zip(TORSO, TORSO[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / max(1e-6, z1 - z0)
            return [lerp(x, y, t) for x, y in zip(r0, r1)]
    return list(TORSO[0][1:]) if z < TORSO[0][0] else list(TORSO[-1][1:])


def bust(x, z):
    """How far the breastplate stands forward of its plain ellipse: a
    moulded chest, shaped as an armourer would for a woman."""
    return 0.055 * sum(math.exp(-((x - s * 0.15) / 0.105) ** 2) for s in (-1, 1)) * math.exp(-((z - 2.87) / 0.12) ** 2)


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
        pts.append(V((x, y + WAIST_Y, z)))
    return pts


def surface(phi, z, out=0.0):
    """The cuirass's outside seen from its axis at height z, phi round
    from straight ahead (-Y) toward +X."""
    # torso_ring's angle runs from +X toward +Y, so straight ahead is 270 degrees there
    t = (math.pi * 1.5 + phi) % (2 * math.pi)
    rx, ryf, ryb = torso_at(z)
    ct, st = math.cos(t), math.sin(t)
    x = math.copysign(abs(ct) ** (2.0 / 2.3), ct) * (rx + out)
    sy = math.copysign(abs(st) ** (2.0 / 2.3), st)
    y = sy * ((ryf if sy < 0 else ryb) + out)
    if sy < 0:
        y -= bust(x, z) * (-sy) ** 0.5
    return V((x, y + WAIST_Y, z))


def hip(rig, M):
    """The belt, two lames of faulds flaring over the hips with rivets,
    and a skirt of mail under them with a scalloped hem."""
    c = (0.0, 0.03)
    n = 24
    parts = []
    for k, (z0, z1, r0, r1, dip) in enumerate(((2.36, 2.22, (0.3, 0.25, 0.235), (0.33, 0.275, 0.256), 0.02),
                                              (2.24, 2.11, (0.325, 0.27, 0.25), (0.355, 0.295, 0.272), 0.03))):
        top = ring2(c, *r0, z0, n)
        low = ring2(c, *r1, z1, n)
        for p in low:
            p.z -= dip * max(0.0, -(p.y - c[1]) / r1[1]) ** 2
        parts.append(shell("fauld%d" % k, [top + [top[0]], low + [low[0]]], [M["plate"]], 0.008, -1.0, centre=c))
        parts.append(rim(grow(low, c, 0.004), 0.007, M["gold"], "fauldrim", closed=True))
        pts = []
        for i in (n // 2 - 3, n // 2 + 3, 1, n - 1, 3, n - 3, n // 4, 3 * n // 4):
            p = lerp(top[i], low[i], 0.45)
            nrm = V((p.x - c[0], p.y - c[1], 0)).normalized()
            pts.append((p + nrm * 0.006, nrm))
        parts.append(rivets(pts, 0.01, M["gold"], "fauldrivets"))
    # the mail, falling to a hem cut in scallops
    cols = 40
    rows = []
    for z, rx, ryf, ryb in ((2.14, 0.35, 0.3, 0.268), (2.0, 0.37, 0.315, 0.282), (1.88, 0.385, 0.328, 0.294), (1.79, 0.395, 0.336, 0.302)):
        r = ring2(c, rx, ryf, ryb, z, cols)
        rows.append(r + [r[0]])
    hem = []
    for i, p in enumerate(rows[-1]):
        u = (i % 4) / 4.0
        drop = 0.07 * math.sqrt(max(0.0, 1.0 - (2 * u - 1) ** 2)) if i % 4 else 0.0
        hem.append(V((p.x * 1.01, p.y * 1.01, p.z - 0.03 - drop)))
    rows.append(hem)
    parts.append(mail_patch(rows, M["mail"], "mailskirt"))
    rings = [ring2(c, 0.298, 0.25, 0.235, z, n) for z in (2.44, 2.435, 2.335, 2.33)]
    rings[1], rings[2] = grow(rings[1], c, 0.012), grow(rings[2], c, 0.012)
    parts.append(loft_ob("belt", rings, [M["leather"]], 50))
    bm = bmesh.new()
    fr = [V((x, -0.238, z)) for x, z in ((-0.05, 2.34), (0.05, 2.34), (0.05, 2.435), (-0.05, 2.435))]
    uk.loft(bm, tube_rings(fr + [fr[0]], [(0.01, 0.01)] * 5, 5))
    parts.append(uk.shade(uk.mesh("buckle", bm, [M["gold"]]), 50))
    return uk.join(parts, "hip")


def torso(rig, M):
    """The cuirass, moulded at the chest and drawn in at the waist, with
    rolled gilt edges at the neck and arms, a plackart etched with scales
    over the belly rising to a point, a high collar standing about the
    throat and flaring higher behind, mail in the armpits, and the gold
    silk sash from the right shoulder to a knot at the left hip."""
    parts = []
    n = 28
    rings = [torso_ring(z, n) for z, *_ in TORSO]
    parts.append(loft_ob("cuirass", rings, [M["plate"]], 32, cap0=True, cap1=True))
    parts.append(rim(grow(rings[-1], (0.0, WAIST_Y), 0.003), 0.008, M["gold"], "neckrim", closed=True))
    parts.append(rim(grow(rings[0], (0.0, WAIST_Y), 0.004), 0.008, M["gold"], "waistrim", closed=True))
    # the plackart, a little proud of the breastplate, its top a point under the chest
    grid = []
    cols, rows = 12, 5
    for r in range(rows + 1):
        row = []
        for k in range(cols + 1):
            ph = lerp(-1.2, 1.2, k / cols)
            top = 2.7 - 0.22 * (abs(ph) / 1.2) ** 1.2
            z = lerp(2.31, top, r / rows)
            row.append(surface(ph, z, 0.01))
        grid.append(row)
    parts.append(shell("plackart", grid, [M["etched"]], 0.008, -1.0, centre=(0.0, WAIST_Y)))
    parts.append(rim([p + (p - V((0, WAIST_Y, p.z))).normalized() * 0.006 for p in grid[-1]], 0.007, M["gold"], "plackartrim"))
    rv = []
    for k in (2, cols // 2, cols - 2):
        p = lerp(grid[1][k], grid[2][k], 0.5)
        nrm = (p - V((0, WAIST_Y, p.z))).normalized()
        rv.append((p + nrm * 0.009, nrm))
    parts.append(rivets(rv, 0.01, M["gold"], "plackartrivets"))
    # the collar: two lames, high at the sides and higher behind, low under the chin
    r0, r1 = 0.2, 0.17  # the collar's radii, clear of the neck the head piece cuts
    for j, (z0, h0) in enumerate(((3.28, 0.1), (3.35, 0.115))):
        lo, hi = [], []
        for i in range(24):
            t = 2 * math.pi * i / 24
            front = max(0.0, -math.sin(t))  # 1 straight ahead (-Y)
            back = max(0.0, math.sin(t))
            hgt = h0 * (1.0 - 0.58 * front ** 2 + 0.22 * back ** 2)
            ra = r0 - 0.01 * j
            rb = r1 * (1.0 + 0.08 * j) + 0.02 * back ** 2
            lo.append(V((math.cos(t) * ra, math.sin(t) * ra * 0.95 + 0.02, z0 - 0.02 * front)))
            hi.append(V((math.cos(t) * rb, math.sin(t) * rb * 0.95 + 0.02 + 0.03 * back, z0 + hgt)))
        parts.append(shell("collar%d" % j, [lo + [lo[0]], hi + [hi[0]]], [M["plate"]], 0.008, -1.0, centre=(0.0, 0.02)))
        parts.append(rim(grow(hi, (0.0, 0.02), 0.002), 0.006, M["gold"], "collarrim", closed=True))
    for side in (-1, 1):
        grid = []
        for r in range(3):
            z = lerp(3.02, 2.88, r / 2)
            rx = torso_at(z)[0]
            grid.append([V((side * (rx + 0.002), lerp(-0.09, 0.11, k / 4) + WAIST_Y, z)) for k in range(5)])
        parts.append(mail_patch(grid, M["mail"], "armpit"))
    parts += sash(M)
    return uk.join(parts, "torso")


def sash(M):
    """The gold silk sash: a band over the right shoulder, across the
    breast and round the back to a knot at the left hip, softly folded,
    and two tails hanging from the knot."""
    def centre(phi):
        """The sash's middle at phi round: a tilted loop over the cuirass."""
        z = 2.86
        for _ in range(6):
            p = surface(phi, z, 0.0)
            z = 2.85 - 1.18 * p.x + 0.04 * math.cos(phi)
            z = min(z, 3.2)
        return surface(phi, z, 0.022 + 0.012 * smooth(3.0, 3.2, z)), z

    n = 44
    rows_in = []
    for i in range(n):
        phi = 2 * math.pi * i / n
        p, z = centre(phi)
        q, _ = centre(phi + 0.02)
        along = (q - p).normalized()
        out = V((p.x, p.y - WAIST_Y, 0.0)).normalized()
        across = along.cross(out).normalized()
        if across.z < 0:
            across = -across
        w = 0.075 + 0.015 * math.sin(3 * phi)
        fold = 0.012 * math.sin(phi * 9.0)
        a = p - across * w + out * fold
        b = p + across * w + out * (0.008 - fold * 0.5)
        mid = p + out * (0.012 + 0.01 * math.sin(phi * 5.0))
        rows_in.append((a, mid, b))
    bm = bmesh.new()
    grid = [[r[k] for r in rows_in] + [rows_in[0][k]] for k in range(3)]
    sheet(bm, grid)
    band = uk.mesh("sash", bm, [M["sash"]], outward=False)
    m = band.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.01, 1.0
    uk.apply(band)
    parts = [uk.shade(band, 55)]
    # the knot at the left hip and its tails
    k = surface(math.radians(70), 2.48, 0.04)
    parts.append(dome(k, (0.065, 0.035, 0.05), 10, 7, [M["sash"]], "sashknot"))
    for sgn in (-1, 1):
        lp = k + V((sgn * 0.06, 0, 0.035))
        parts.append(dome(lp, (0.055, 0.03, 0.035), 8, 6, [M["sash"]], "sashloop"))
    out = V((k.x, k.y - WAIST_Y, 0)).normalized()
    side = V((-out.y, out.x, 0))
    for j, (L, swing) in enumerate(((0.42, -0.05), (0.34, 0.06))):
        grid = []
        for r in range(6):
            t = r / 5
            c = k + V((0, 0, -L * t)) + side * (swing * t + 0.03 * (j - 0.5)) + out * (0.02 + 0.03 * t * t)
            w = 0.05 + 0.025 * t
            grid.append([c - side * w + out * 0.006 * math.sin(t * 7 + j), c + out * 0.012, c + side * w])
        bm = bmesh.new()
        sheet(bm, grid)
        tail = uk.mesh("sashtail", bm, [M["sash"]], outward=False)
        mm = tail.modifiers.new("solid", "SOLIDIFY")
        mm.thickness, mm.offset = 0.008, 0.0
        uk.apply(tail)
        parts.append(uk.shade(tail, 55))
    return parts


# ---------------------------------------------------------------- shoulders and arms

def pauldron(rig, M):
    """The left pauldron, small and close to the shoulder: a flat cop
    enamelled in the player's colour, as the original's shoulders are, one
    steel lame below it, each with a gilt rolled edge, and rising from the
    cop beside the neck a single smooth fin plate in the player's colour,
    higher behind, edged in gold."""
    s = rig.pivot("ShoulderL")
    c = V((s.x + 0.09, s.y, s.z - 0.1))
    parts = []

    def plate(a0, a1, rad, ys, lift, name, mats, cols=7, flat=0.55):
        grid = []
        ymax = max(abs(ys[0]), abs(ys[-1])) + 0.012
        for y in ys:
            f = math.sqrt(max(0.0, 1.0 - (y / ymax) ** 2))
            row = []
            for j in range(cols):
                a = lerp(a0, a1, j / (cols - 1))
                r = rad * max(0.2, f) ** 0.75
                row.append(V((c.x + math.sin(a) * r, c.y + y, c.z + math.cos(a) * r * flat - lift)))
            grid.append(row)
        sh = shell(name, grid, mats, 0.011, -1.0, 0, 1, centre=tuple(c))
        return sh, [row[-1] for row in grid], grid

    ys = [lerp(-0.24, 0.24, k / 8) for k in range(9)]
    cop, edge, grid = plate(-0.3, 1.22, 0.215, ys, 0.0, "pauldron_L", [M["teamcolor_enamel"], M["plate"]], 8)
    parts += [cop, rim(edge, 0.007, M["gold"], "copedge"), rim([row[0] for row in grid], 0.007, M["gold"], "copinner")]
    ys = [lerp(-0.21, 0.21, j / 6) for j in range(7)]
    lm, edge, _ = plate(0.98, 1.55, 0.228, ys, 0.045, "pauldronlame_L", [M["plate"], M["plate"]], 5)
    parts += [lm, rim(edge, 0.006, M["gold"], "lameedge")]
    parts.append(rivets([(grid[g][2] + V((0, 0, 0.01)), V((0, 0, 1))) for g in (2, 6)], 0.01, M["gold"], "coprivets"))
    # the fin plate: from the cop's inner top, leaning in a little and raked
    # back, its top edge rising to a point behind
    cols = 9
    base, top = [], []
    for k in range(cols):
        t = k / (cols - 1)
        y = lerp(-0.16, 0.19, t)
        f = math.sqrt(max(0.0, 1.0 - (y / 0.252) ** 2))
        a = -0.08
        r = 0.215 * max(0.2, f) ** 0.75
        b = V((c.x + math.sin(a) * r - 0.012, c.y + y, c.z + math.cos(a) * r * 0.55 - 0.012))
        h = 0.03 + 0.15 * smooth(0.0, 0.85, t) ** 1.3 - 0.06 * smooth(0.85, 1.0, t)
        base.append(b)
        top.append(b + V((-0.12 * h, 0.22 * h, h)))
    rows = [base] + [[bb.lerp(tt, r / 3) for bb, tt in zip(base, top)] for r in (1, 2, 3)]
    parts.append(shell("finplate_L", rows, [M["teamcolor_enamel"]], 0.009, 0.0))
    parts.append(rim(top, 0.006, M["gold"], "finplateedge", sides=5))
    return uk.join(parts, "pauldron_L")


def upper_arm(rig, M):
    """The left rerebrace, stepped by a lame at the top, rolled at its foot."""
    u = rig.pivot("ArmUL")
    e = rig.pivot("ArmLL")
    path = [lerp(u + V((0.04, 0.0, 0.06)), e + V((0.0, -0.005, 0.03)), t) for t in (0.0, 0.2, 0.21, 0.6, 1.0)]
    rings = tube_rings(path, [(0.134, 0.13), (0.136, 0.132), (0.141, 0.137), (0.13, 0.127), (0.117, 0.114)], 14)
    parts = [loft_ob("upperarm_L", rings, [M["plate"]], 40, cap0=True)]
    for idx in (2, 4):
        parts.append(rim(rings[idx] + [rings[idx][0]], 0.006, M["gold"], "rerebracerim"))
    return uk.join(parts, "upperarm_L")


def forearm(rig, M):
    """The left vambrace with a strap, the couter over the elbow with its
    fan, and mail in the crook of the arm."""
    e = rig.pivot("ArmLL")
    w = rig.pivot("HandL")
    path = [lerp(e + V((0, 0, 0.03)), w + V((0, 0, 0.05)), t) for t in (0.0, 0.35, 0.7, 1.0)]
    rings = tube_rings(path, [(0.116, 0.114), (0.117, 0.113), (0.104, 0.099), (0.093, 0.09)], 14)
    parts = [loft_ob("forearm_L", rings, [M["plate"]], 40, cap0=True)]
    parts.append(rim(rings[-1] + [rings[-1][0]], 0.007, M["gold"], "vambracerim"))
    p = lerp(path[0], path[-1], 0.6)
    rx = lerp(0.117, 0.093, 0.6) + 0.003
    parts += strap((p.x, p.y), ring2((p.x, p.y), rx, rx, rx, p.z, 14), 0.026, 0.006, M["leather"], M["gold"], buckle_at=0, name="vambracestrap_L")
    parts.append(dome(e + V((0.03, 0.055, 0.0)), (0.12, 0.103, 0.103), 12, 7, [M["plate"]], "couter_L"))
    f = e + V((0.125, 0.03, 0.0))
    grid = []
    for r in range(3):
        t = r / 2
        grid.append([f + V((0.016 + 0.036 * t, -0.056 + 0.12 * t - 0.036 * (1 - s_ * s_) * t, s_ * 0.09 * (1 - 0.3 * t)))
                     for s_ in (lerp(-1.0, 1.0, j / 6) for j in range(7))])
    parts.append(shell("couterfan_L", grid, [M["plate"]], 0.008, 0.0))
    parts.append(rim(grid[-1], 0.005, M["gold"], "couterfanrim"))
    grid = []
    for r in range(3):
        p = lerp(e + V((0, 0, 0.09)), e + V((0, 0, -0.07)), r / 2)
        grid.append([p + V((math.cos(a) * 0.112, math.sin(a) * 0.112 - 0.016, 0)) for a in (lerp(2.2, 4.0, j / 4) for j in range(5))])
    parts.append(mail_patch(grid, M["mail"], "elbowmail"))
    return uk.join(parts, "forearm_L")


def cuff(w, M, name):
    rings = tube_rings([w + V((0.0, 0.0, 0.1)), w, w + V((0.0, 0.0, -0.075))], [(0.116, 0.111), (0.096, 0.091), (0.08, 0.077)], 14)
    return [loft_ob(name, rings, [M["plate"]], 40), rim(rings[0] + [rings[0][0]], 0.007, M["gold"], name + "rim")]


def finger(base, d, curl, L, r, M, name, plates=True):
    """A gauntleted finger from base along d, curling toward curl, in three
    joints, each with a plate over its back."""
    pts = [base]
    cur = d.normalized()
    for k, seg in enumerate((0.42, 0.33, 0.25)):
        cur = (cur + curl.normalized() * (0.28 + 0.12 * k)).normalized()
        pts.append(pts[-1] + cur * L * seg)
    bm = bmesh.new()
    uk.loft(bm, tube_rings(pts, [(r, r * 0.9), (r * 0.95, r * 0.85), (r * 0.85, r * 0.78), (r * 0.6, r * 0.55)], 7), cap0=True, cap1=True)
    parts = [uk.shade(uk.mesh(name, bm, [M["leather"]]), 50)]
    if plates:
        back = -curl.normalized()
        for a, b in zip(pts, pts[1:]):
            m = a.lerp(b, 0.5) + back * r * 0.55
            parts.append(dome(m, (r * 1.0, r * 1.0, r * 1.0), 6, 3, [M["plate"]], name + "plate"))
    return parts


def open_hand(rig, M):
    """The left gauntlet open, hanging relaxed at rest with its palm to
    her side, the fingers a little curled, so the script can turn it palm
    up to cast: a flared cuff, plates over the back of the hand, and
    jointed fingers."""
    w = rig.pivot("HandL")
    parts = cuff(w, M, "cuff_L")
    # the hand's body, a flattened block from the wrist to the knuckles
    k0 = w + V((-0.025, 0.0, -0.17))
    rings = []
    for t, hw, th in ((0.0, 0.07, 0.05), (0.4, 0.095, 0.045), (0.8, 0.1, 0.04), (1.0, 0.09, 0.035)):
        p = w.lerp(k0, t) + V((0, -0.01, -0.03 * (1 - t)))
        rings.append(uk.ring(p, V((0, 1, 0)), V((1, 0, 0)), hw, th, 12, power=2.6))
    parts.append(loft_ob("palm_L", rings, [M["leather"]], 45, cap0=True, cap1=True))
    rows = [[w.lerp(k0, t) + V((0.042 - 0.008 * a * a, a * 0.09 - 0.01, 0.0)) for a in (lerp(-1.0, 1.0, i / 6) for i in range(7))] for t in (0.15, 0.55, 0.95)]
    parts.append(shell("handplate_L", rows, [M["plate"]], 0.012, 0.0))
    parts.append(rim(rows[-1], 0.006, M["gold"], "handplaterim"))
    curl = V((-1.0, 0.0, 0.25))
    for i, (dy, L) in enumerate(((-0.075, 0.17), (-0.025, 0.19), (0.025, 0.18), (0.072, 0.15))):
        b = k0 + V((0.0, dy, 0.0))
        parts += finger(b, V((0.05, dy * 0.4, -1.0)), curl, L, 0.022 - 0.0015 * abs(i - 1.5), M, "finger_L")
    tb = w + V((-0.04, -0.08, -0.08))
    parts += finger(tb, V((-0.3, -0.8, -0.6)), V((-0.6, 0.6, -0.2)), 0.14, 0.024, M, "thumb_L")
    return uk.join(parts, "hand_L")


def grip_hand(rig, M):
    """The right gauntlet closed round the trident's shaft, which runs fore
    and aft through the trident's pivot: a flared cuff, the hand's back
    plated down to the fist, the knuckles domed, a plated thumb."""
    w = rig.pivot("HandR")
    g = rig.pivot("Trident")
    gx, gz = g.x - 0.01, g.z
    parts = cuff(w, M, "cuff_R")
    # the back of the hand from the wrist to the fist
    fist_c = V((gx - 0.06, w.y, gz + 0.02))
    rings = []
    for t, hw, th in ((0.0, 0.07, 0.05), (0.5, 0.095, 0.05), (1.0, 0.1, 0.06)):
        p = w.lerp(fist_c, t)
        rings.append(uk.ring(p, V((0, 1, 0)), V((1, 0, 0)), hw, th, 12, power=2.6))
    parts.append(loft_ob("handback_R", rings, [M["leather"]], 45, cap0=True))
    rows = [[w.lerp(fist_c, t) + V((-0.045, a * 0.09, 0.0)) for a in (lerp(-1.0, 1.0, i / 6) for i in range(7))] for t in (0.1, 0.5, 0.9)]
    parts.append(shell("handplate_R", rows, [M["plate"]], 0.012, 0.0))
    # the fist round the shaft
    rings = []
    for y, s, kn in ((-0.1, 0.8, 0.0), (-0.08, 1.0, 0.011), (-0.045, 1.0, 0.004), (-0.035, 1.0, 0.011), (0.0, 1.0, 0.004),
                     (0.011, 0.99, 0.011), (0.045, 0.96, 0.004), (0.056, 0.93, 0.009), (0.09, 0.72, 0.0)):
        r = []
        for i in range(12):
            t = 2 * math.pi * i / 12
            bump = kn * max(0.0, math.cos(t - 2.5)) ** 2
            r.append(V((gx - 0.007 + math.cos(t) * (0.075 * s + bump), w.y + y, gz + math.sin(t) * (0.068 * s + bump))))
        rings.append(r)
    parts.append(loft_ob("glove_R", rings, [M["leather"]], 45, cap0=True, cap1=True))
    for k in range(4):
        parts.append(dome(V((gx - 0.06, w.y - 0.067 + k * 0.045, gz - 0.05)), (0.026, 0.02, 0.023), 8, 5, [M["plate"]], "knuckle"))
    path = [V((gx + 0.03, w.y + 0.035, gz + 0.062)), V((gx + 0.06, w.y - 0.027, gz + 0.053)), V((gx + 0.05, w.y - 0.08, gz + 0.027))]
    bm = bmesh.new()
    uk.loft(bm, tube_rings(path, [(0.029, 0.027), (0.027, 0.025), (0.021, 0.019)], 8), cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("thumb_R", bm, [M["leather"]]), 45))
    parts.append(dome(V((gx + 0.06, w.y - 0.018, gz + 0.074)), (0.026, 0.038, 0.013), 8, 4, [M["plate"]], "thumbplate"))
    return uk.join(parts, "hand_R")


# ---------------------------------------------------------------- the trident

def trident(rig, M, spread=(1.0, 0.0, 0.0)):
    """A long trident as long as the original's, held at rest fore and aft
    through the right fist: a dark steel shaft bound in gold, a socket
    with a wave running round it, and three barbed prongs, the outer two
    flaring out and curving forward. spread is the way the prongs fan."""
    g = rig.pivot("Trident")
    x, z = g.x, g.z
    ax = V((0.0, -1.0, 0.0))
    sp = V(spread).normalized()
    side = ax.cross(sp).normalized()
    o = V((x, 0.0, z))
    parts = []

    def at(yy, s=0.0, w=0.0):
        return o + ax * (-yy) + sp * s + side * w

    tip, base = -3.1, -2.36
    shaft = [uk.ring(at(y), side, sp, r, r, 10) for y, r in ((base + 0.05, 0.036), (-1.0, 0.037), (0.6, 0.038), (1.27, 0.036))]
    parts.append(loft_ob("shaft", shaft, [M["steel"]], 50, cap0=True, cap1=True))
    grip = [uk.ring(at(y), side, sp, 0.043, 0.043, 10) for y in (g.y - 0.15, g.y + 0.17)]
    parts.append(loft_ob("grip", grip, [M["leather"]], 50, cap0=True, cap1=True))
    for y in (g.y - 0.19, g.y + 0.21, -1.2, 0.7):
        parts.append(loft_ob("band", [uk.ring(at(y + d), side, sp, r, r, 10) for d, r in ((-0.02, 0.042), (0.0, 0.048), (0.02, 0.042))], [M["gold"]], 40, cap0=True, cap1=True))
    butt = [uk.ring(at(y), side, sp, r, r, 10) for y, r in ((1.25, 0.036), (1.31, 0.044), (1.36, 0.03))]
    butt.append([at(1.43)] * 10)
    parts.append(loft_ob("butt", butt, [M["gold"]], 40, cap0=True))
    # the socket, swelling, with a wave rolling round it in gold
    sock = [uk.ring(at(y), side, sp, r, r, 14) for y, r in ((base + 0.2, 0.034), (base + 0.12, 0.05), (base + 0.04, 0.056), (base - 0.02, 0.05))]
    parts.append(loft_ob("socket", sock, [M["steel"]], 40, cap0=True, cap1=True))
    wave = []
    for i in range(41):
        t = 2 * math.pi * i / 40
        yy = base + 0.08 + 0.03 * math.sin(3 * t)
        wave.append(at(yy) + (side * math.cos(t) + sp * math.sin(t)) * 0.055)
    parts.append(rim(wave, 0.008, M["gold"], "socketwave", sides=5))
    # the crossbar the prongs rise from, a crescent
    cb = [at(base - 0.03 - 0.1 * (s / 0.36) ** 2, s) for s in (lerp(-0.36, 0.36, k / 10) for k in range(11))]
    bm = bmesh.new()
    uk.loft(bm, tube_rings(cb, [(0.03, 0.025)] * len(cb), 8), cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("crossbar", bm, [M["steel"]]), 40))

    def prong(path, name):
        rings = []
        for k, p in enumerate(path):
            a = path[max(0, k - 1)]
            b = path[min(len(path) - 1, k + 1)]
            d = (b - a).normalized()
            n1 = side
            n2 = d.cross(n1).normalized()
            t = k / (len(path) - 1)
            w = lerp(0.03, 0.016, t)
            rings.append([p + n1 * w * 0.6, p + n2 * w, p - n1 * w * 0.6, p - n2 * w])
        rings.append([path[-1] + (path[-1] - path[-2]).normalized() * 0.03] * 4)
        return loft_ob(name, rings, [M["steel"]], 30, cap0=True)

    def barb(at_, back, out, name, size=0.1):
        bm = bmesh.new()
        pts = [at_, at_ + back * size * 0.2 + out * size * 0.15, at_ + back * size + out * size * 0.55, at_ + back * size * 0.5 + out * size * 0.15]
        th = side * 0.007
        top = [bm.verts.new(p + th) for p in pts]
        bot = [bm.verts.new(p - th) for p in pts]
        bm.faces.new(top)
        bm.faces.new(list(reversed(bot)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            bm.faces.new((top[i], bot[i], bot[j], top[j]))
        return uk.shade(uk.mesh(name, bm, [M["steel"]]), 30)

    # the middle prong, a barbed blade
    mid = [at(lerp(base - 0.1, tip + 0.29, t)) for t in (0.0, 0.33, 0.66, 1.0)]
    parts.append(prong(mid, "midprong"))
    prof = ((0.0, 0.0), (0.06, 0.04), (0.15, 0.07), (0.25, 0.055), (0.33, 0.0))
    bm = bmesh.new()
    rows = []
    for t, hw in prof:
        p = at(tip + t)
        hw = max(hw, 0.002)
        th = 0.016 * min(1.0, 0.3 + t * 5)
        rows.append([p + sp * hw, p + side * th * 0.5, p - sp * hw, p - side * th * 0.5])
    uk.loft(bm, rows, cap1=True)
    parts.append(uk.shade(uk.mesh("bladehead", bm, [M["steel"]]), 28))
    for sgn in (-1, 1):
        parts.append(barb(at(tip + 0.28, sgn * 0.02), ax * -1.0, sp * sgn, "midbarb", 0.12))
    # the outer prongs, flaring out from the crossbar's ends and curving forward
    for sgn in (-1, 1):
        path = []
        for k in range(7):
            t = k / 6
            yy = lerp(base - 0.13, tip + 0.12, t)
            s = sgn * (0.36 + 0.14 * math.sin(math.pi * t * 0.8) - 0.12 * t * t)
            path.append(at(yy, s))
        parts.append(prong(path, "outerprong"))
        parts.append(barb(path[-1], ax * -1.0, sp * sgn, "outerbarb", 0.13))
        parts.append(barb(path[-3], ax * -1.0, sp * sgn, "outerbarb2", 0.08))
    return uk.join(parts, "trident")


# ---------------------------------------------------------------- the cape

# The script's idle pose turns these pieces on their pivots, in degrees
# about X, Y and Z, each from its parent (vermage.cob's restore_x, read from
# the engine's pose dump): the cape's hem and the braid. Each is built turned
# the other way, so the cape and the braid hang whole while she stands.
IDLE = {"Cape4": (-4.219, 0.0, 0.0), "Pony2": (7.734, 0.0, 0.0), "Pony3": (-6.328, 0.0, 0.0), "Pony4": (0.0, 4.57, 0.0)}


def idle_turn(rig, name):
    """Where the idle pose moves a piece's rest geometry, as a matrix in
    model space: its own turn and its ancestors' in IDLE."""
    chain = []
    p = rig[name]
    while True:
        chain.append(p.name)
        if p.parent < 0:
            break
        p = rig.pieces[p.parent]
    m = Matrix.Identity(4)
    for nm in reversed(chain):
        if nm in IDLE:
            piv = rig.pivot(nm)
            r = Euler([math.radians(a) for a in IDLE[nm]]).to_matrix().to_4x4()
            m = m @ Matrix.Translation(piv) @ r @ Matrix.Translation(-piv)
    return m


def cape(rig, M):
    """The cape in the original's four pieces, each hinged where the
    original's is: wool in the player's colour with a darker lining,
    gathered in small pleats under a gold bar across the shoulders, the
    pleats running into folds that fan out down the back to a hem bordered
    in gold thread."""
    names = ["Cape1", "Cape2", "Cape3", "Cape4"]
    piv = [rig.pivot(nm) for nm in names]
    top = 3.24
    inner, edge = 22, 0.94
    us = [1.0] + [edge - 2.0 * edge * c / inner for c in range(inner + 1)] + [-1.0]
    mats = [M["teamcolor"], M["teamcolor_lining"], M["goldthread"], M["goldthread"]]
    hem_z = 0.5

    def half_width(z):
        if z > 2.3:
            return 0.42 + 0.28 * min(1.0, (top - z) / 0.9) ** 0.5
        return 0.7 + 0.06 * (2.3 - z) / 1.8

    def hem(u):
        return hem_z + 0.02 * math.sin(5.3 * u + 0.8) - 0.02 * u + 0.08 * smooth(0.8, 1.0, abs(u))

    def at(u, z):
        drop = smooth(top, top - 0.6, z)
        swing = min(1.0, max(0.0, (1.6 - z) / 1.1)) ** 2
        gather = smooth(top, top - 0.12, z)
        w = u + 0.04 * math.sin(3.1 * u + 0.4) + 0.025 * math.sin(7.7 * u + 1.9)
        ph = 2.0 * math.pi * 2.2 * w + 0.6 * math.sin(1.1 * z + 0.5) + 0.7 * swing
        deep = 0.08 * (0.15 + 0.85 * smooth(3.2, 1.7, z)) * (1.0 - 0.45 * math.exp(-((z - 2.1) / 0.22) ** 2))
        deep *= (0.8 + 0.2 * math.sin(2.0 * math.pi * 1.6 * u + 1.1)) * (1.0 + 0.3 * swing) * gather
        pleat = 0.013 * smooth(2.6, 3.1, z) * gather
        x = u * half_width(z) + 0.33 * deep * math.sin(ph) - 0.04 * swing
        y = max(lerp(0.29, 0.43, drop), torso_at(z)[2] + WAIST_Y + 0.015 + 0.05 * gather)
        y += 0.05 * smooth(2.5, 2.0, z) + 0.06 * swing
        y += deep * math.cos(ph) + pleat * math.cos(2.0 * math.pi * 4.0 * w)
        y -= (0.15 * u * u + 0.06 * smooth(0.8, 1.0, abs(u))) * drop
        rip = smooth(0.7, 1.0, abs(u)) * drop
        y += 0.035 * rip * math.sin(6.0 * z + 1.7 * u)
        x += 0.02 * rip * math.cos(4.4 * z + u) * (1.0 if u > 0 else -1.0)
        return V((x, y, z))

    out = {}
    for k, name in enumerate(names):
        last = k == len(names) - 1
        hi = top if k == 0 else piv[k].z
        low = (lambda u: hem(u) + 0.075) if last else (lambda u, b=piv[k + 1].z: b)
        n = max(3, int(round((hi - low(0.0)) / (0.12 if k == 0 else 0.16))))
        zs = [lambda u, r=r: lerp(hi, low(u), r / n) for r in range(n + 1)]
        if k:
            zs = [lambda u, d=d: hi + d for d in (0.12, 0.03)] + zs
        if last:
            zs.append(hem)
        rows = []
        for f in zs:
            row = []
            for u in us:
                z = f(u)
                p = at(u, z)
                if k and z > hi:
                    p.y -= 0.02 * smooth(hi, hi + 0.03, z)
                row.append(p)
            rows.append(row)
        bm = bmesh.new()
        vs = [[bm.verts.new(p) for p in row] for row in rows]
        for r in range(len(rows) - 1):
            for c in range(len(us) - 1):
                f = bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
                border = c == 0 or c == len(us) - 2 or (last and r == len(rows) - 2)
                f.material_index = 2 if border else 0
        ob = uk.mesh("cape%d" % (k + 1), bm, mats, outward=False)
        m = ob.modifiers.new("solid", "SOLIDIFY")
        m.thickness, m.offset, m.use_even_offset = 0.01, -1.0, True
        m.material_offset = 1
        uk.apply(ob)
        parts = [uk.shade(ob, 40)]
        if k == 0:
            hw = half_width(top - 0.02)
            path = [V((u * hw, at(u, top - 0.02).y + 0.012, top - 0.02)) for u in (0.97, 0.5, 0.0, -0.5, -0.97)]
            bm = bmesh.new()
            uk.loft(bm, tube_rings(path, [(0.026, 0.026)] * 5, 8), cap0=True, cap1=True)
            parts.append(uk.shade(uk.mesh("capebar", bm, [M["gold"]]), 50))
            for p in (path[0], path[-1]):
                parts.append(dome(p, (0.05, 0.03, 0.05), 8, 6, [M["gold"]], "capeboss"))
        out[name] = uk.join(parts, "cape%d" % (k + 1))
        out[name].data.transform(idle_turn(rig, name).inverted())
    return out


# ---------------------------------------------------------------- the mermaid

def tail(rig, M):
    """The mermaid the script makes of her in deep water, on the pieces it
    shows there: a scaled join under the skirt (HideHip), the tail's upper
    and lower runs on the right leg's pieces (HideLegU, HideLegL), the
    flukes (HideFoot), and a fin down her back (HideTorso). Each run
    tucks a little into the one above, so no gap opens as they bend."""
    out = {}
    c = V((0.0, 0.05, 0.0))

    def run(name, zs, mat=M["tailscale"]):
        rings = [ring2((c.x + dx, c.y + dy), rx, ryf, ryb, z, 18) for z, rx, ryf, ryb, dx, dy in zs]
        return loft_ob(name, rings, [mat], 40, cap0=True, cap1=True)

    out["HideHip"] = uk.join([run("tailhip", ((2.1, 0.33, 0.27, 0.25, 0, 0), (1.95, 0.38, 0.3, 0.28, 0, 0), (1.82, 0.4, 0.29, 0.27, 0, 0)))], "tailhip")
    out["HideLegU"] = uk.join([run("tailupper", ((1.88, 0.4, 0.28, 0.27, 0, 0), (1.6, 0.39, 0.27, 0.26, 0, 0), (1.32, 0.35, 0.25, 0.23, 0, 0),
                                                 (1.06, 0.31, 0.22, 0.2, 0, 0)))], "tailupper")
    lower = [run("taillower", ((1.12, 0.315, 0.22, 0.2, 0, 0), (0.85, 0.26, 0.19, 0.17, 0, 0), (0.6, 0.2, 0.15, 0.14, 0, 0),
                               (0.38, 0.15, 0.115, 0.11, 0, 0), (0.18, 0.11, 0.09, 0.085, 0, 0)))]
    for sx in (-1, 1):
        b0 = V((sx * 0.22, c.y + 0.02, 0.85))
        rays = [(b0 + V((0, 0, dz)), b0 + V((sx * (0.16 - 0.25 * dz), 0.03, dz - 0.14))) for dz in (0.1, 0.03, -0.04, -0.1)]
        lower += fin(rays, M["tailfin"], "sidefin", thick=0.008, sag=0.25)
    out["HideLegL"] = uk.join(lower, "taillower")
    fl = [run("tailstalk", ((0.24, 0.12, 0.095, 0.09, 0, 0), (0.12, 0.09, 0.07, 0.07, 0, 0), (0.02, 0.07, 0.05, 0.05, 0, 0)))]
    for sx in (-1, 1):
        root = V((sx * 0.04, c.y + 0.03, 0.08))
        rays = []
        for k in range(6):
            t = k / 5
            ang = math.radians(lerp(50, -85, t))
            L = lerp(0.22, 0.76, math.sin(math.pi * t) ** 0.6)
            tip = root + V((sx * math.cos(ang) * L, 0.0, math.sin(ang) * L * 0.8))
            rays.append((root + V((0, 0, 0.05 - 0.1 * t)), tip))
        fl += fin(rays, M["tailfin"], "fluke", thick=0.012, sag=0.07, rows=4)
    out["HideFoot"] = uk.join(fl, "flukes")
    # the fin down her back, standing off the backplate
    rays = []
    for k in range(7):
        t = k / 6
        z = lerp(3.12, 2.5, t)
        b = surface(math.pi, z, 0.0) + V((0, -0.01, 0))
        L = lerp(0.14, 0.24, math.sin(math.pi * min(1.0, 0.2 + t)) ** 0.7) * (1.0 - 0.5 * t * t)
        rays.append((b, b + V((0.0, 0.6, -0.8)).normalized() * L + V((0, L * 0.5, 0))))
    out["HideTorso"] = uk.join(fin(rays, M["tailfin"], "backfin", thick=0.01, sag=0.22) + [rim([r[0] for r in rays], 0.014, M["tailscale"], "backfinroot", sides=5)], "backfin")
    return out
