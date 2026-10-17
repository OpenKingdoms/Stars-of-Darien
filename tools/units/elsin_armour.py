"""Elsin's armour for elsin.py: gilded plate built as a smith would, each
plate a shell with a rolled edge, lames that overlap down the shoulders,
hips and feet, leather straps with buckles, rivets, and mail where the
joints open, and the cloth of the tabard and the cape with an embroidered
hem. Every part is modelled at rest around the original's pivots (elsin.py
attaches them to their pieces). Left parts are mirrored to the right.
"""
import math

import bmesh
from mathutils import Matrix, Vector

import unitkit as uk

V = Vector


def lerp(a, b, t):
    return a + (b - a) * t


# ---------------------------------------------------------------- shapes

def ring2(c, rx, ry_front, ry_back, z, n, power=2.0, ridge=0.0, ridge_w=0.25, start=0.0):
    """A level ring at height z round centre c (x, y): an ellipse deeper in
    front (-Y) than behind, with an optional ridge down the front's middle."""
    pts = []
    for i in range(n):
        t = start + 2 * math.pi * i / n
        ct, st = math.cos(t), math.sin(t)
        x = math.copysign(abs(ct) ** (2.0 / power), ct) * rx
        sy = math.copysign(abs(st) ** (2.0 / power), st)
        y = sy * (ry_front if sy < 0 else ry_back)
        if ridge and sy < 0:
            y -= ridge * math.exp(-(x / (ridge_w * rx)) ** 2) * (-sy)
        pts.append(V((c[0] + x, c[1] + y, z)))
    return pts


def grow(pts, c, d):
    out = []
    for p in pts:
        r = V((p.x - c[0], p.y - c[1], 0))
        out.append(p + r.normalized() * d if r.length > 1e-6 else p.copy())
    return out


def sheet(bm, grid, mat=0):
    vs = [[bm.verts.new(p) for p in row] for row in grid]
    for r in range(len(grid) - 1):
        for c in range(len(grid[0]) - 1):
            f = bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
            f.material_index = mat
    return vs


def tube_rings(path, radii, n, power=2.0):
    """Cross-sections along path, each turned on from the last, so a tube
    round a curve never twists."""
    rings, ax = [], None
    for k, (p, (rx, ry)) in enumerate(zip(path, radii)):
        axis = (V(path[min(len(path) - 1, k + 1)]) - V(path[max(0, k - 1)])).normalized()
        ax = uk.frame(axis)[0] if ax is None else (ax - axis * ax.dot(axis)).normalized()
        rings.append(uk.ring(V(p), ax, axis.cross(ax), rx, ry, n, power))
    return rings


def rim(points, r, mat, name="rim", sides=4, closed=False):
    """A rolled edge: a thin tube along points."""
    bm = bmesh.new()
    pts = list(points) + ([points[0]] if closed else [])
    rings = tube_rings(pts, [(r, r)] * len(pts), sides)
    uk.loft(bm, rings, cap0=not closed, cap1=not closed)
    return uk.shade(uk.mesh(name, bm, [mat]), 60)


def shell(name, grid, mats, thick, offset=-1.0, mat_offset=0, rim_mat=None, centre=None):
    """A plate from a grid of points, thick, its cut edges in rim_mat when
    given. With centre, a point or a vertical axis through (x, y), its faces
    turn away from it first, so an offset of -1 thickens it inward."""
    bm = bmesh.new()
    sheet(bm, grid)
    if centre is not None:
        bm.normal_update()
        k = 0.0
        for f in bm.faces:
            c = f.calc_center_median()
            o = V((centre[0], centre[1], c.z)) if len(centre) == 2 else V(centre)
            k += f.normal.dot(c - o)
        if k < 0:
            bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    ob = uk.mesh(name, bm, mats, outward=False)
    m = ob.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset, m.use_even_offset = thick, offset, True
    m.material_offset = mat_offset
    if rim_mat is not None:
        m.material_offset_rim = rim_mat
    uk.apply(ob)
    return uk.shade(ob, 40)


def dome(c, r, n, rings, mats, name):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=n, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co = V((c.x + v.co.x * r[0], c.y + v.co.y * r[1], c.z + v.co.z * r[2]))
    return uk.shade(uk.mesh(name, bm, mats), 55)


def rivets(points, r, mat, name="rivets"):
    """Small domed rivet heads at points (point, outward normal)."""
    bm = bmesh.new()
    for p, nrm in points:
        a, b = uk.frame(nrm)
        ring_ = [p + (a * math.cos(t) + b * math.sin(t)) * r for t in (2 * math.pi * k / 5 for k in range(5))]
        top = bm.verts.new(p + nrm * r * 0.7)
        vs = [bm.verts.new(q) for q in ring_]
        for k in range(5):
            bm.faces.new((top, vs[k], vs[(k + 1) % 5]))
    return uk.shade(uk.mesh(name, bm, [mat]), 70)


def strap(centre, ring_pts, width, thick, leather, gold, buckle_at=0, name="strap"):
    """A leather strap round a ring of points (a closed loop on a surface),
    standing off it by thick, with a gold buckle at one point."""
    n = len(ring_pts)
    top = [p + V((0, 0, width * 0.5)) for p in ring_pts]
    low = [p - V((0, 0, width * 0.5)) for p in ring_pts]
    top = grow(top, centre, thick)
    low = grow(low, centre, thick)
    bm = bmesh.new()
    sheet(bm, [top + [top[0]], low + [low[0]]])
    s = uk.mesh(name, bm, [leather], outward=False)
    m = s.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = thick * 0.8, 1.0
    uk.apply(s)
    p = ring_pts[buckle_at % n]
    out = V((p.x - centre[0], p.y - centre[1], 0)).normalized()
    side = V((-out.y, out.x, 0))
    bp = p + out * (thick * 2.2)
    bm = bmesh.new()
    frame_pts = [bp + side * x + V((0, 0, z)) for x, z in ((-0.03, -width * 0.65), (0.03, -width * 0.65), (0.03, width * 0.65), (-0.03, width * 0.65))]
    rings = tube_rings(frame_pts + [frame_pts[0]], [(0.007, 0.007)] * 5, 4)
    uk.loft(bm, rings)
    buckle = uk.shade(uk.mesh(name + "buckle", bm, [gold]), 50)
    return [uk.shade(s, 50), buckle]


def mail_patch(grid, mail, name="mail"):
    bm = bmesh.new()
    sheet(bm, grid)
    ob = uk.mesh(name, bm, [mail], outward=False)
    m = ob.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.012, 0.0
    uk.apply(ob)
    return uk.shade(ob, 60)


# ---------------------------------------------------------------- legs

def foot(rig, M):
    """The left sabaton: a pointed toe cap and five lames over the instep,
    each with a rolled edge, a heel cup, an ankle strap, on a leather sole."""
    a = rig.pivot("FootL")
    cx = a.x + 0.02
    parts = []
    secs = [(0.30, 0.10, 0.30), (0.25, 0.14, 0.39), (0.12, 0.158, 0.38), (0.0, 0.165, 0.3)]
    lames = [(-0.0, -0.075), (-0.075, -0.15), (-0.15, -0.225), (-0.225, -0.3), (-0.3, -0.37)]
    bm = bmesh.new()
    rings = []
    for y, hw, top in secs:
        h = top / 2.0
        r = uk.ring(V((cx, a.y + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw * 1.06, h, 10, power=3.0, start=-math.pi / 2)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    uk.loft(bm, rings, cap0=True, cap1=True)
    heel = uk.mesh("heel_L", bm, [M["gold"], M["leather"]])
    for p in heel.data.polygons:
        if p.normal.z < -0.6 or p.center.z < 0.025:
            p.material_index = 1
    parts.append(uk.shade(heel, 40))

    def sec(y):
        t = min(1.0, max(0.0, -y / 0.47))
        return 0.15 * (1 - t ** 3.0) + 0.035, 0.27 * (1 - t ** 1.6) + 0.06

    for k, (y0, y1) in enumerate(lames):
        bm = bmesh.new()
        rings = []
        for y, grow_ in ((y0 + 0.012, 0.0), (y1, 0.006)):
            hw, top = sec(y)
            h = top / 2.0
            r = uk.ring(V((cx, a.y + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw * 1.06 + grow_, h + grow_, 10, power=3.0, start=-math.pi / 2)
            rings.append([V((p.x, p.y, max(0.012, p.z))) for p in r])
        uk.loft(bm, rings)
        lame = uk.mesh("sabaton%d_L" % k, bm, [M["gold"]], outward=False)
        mm = lame.modifiers.new("solid", "SOLIDIFY")
        mm.thickness, mm.offset = 0.012, -1.0
        uk.apply(lame)
        parts.append(uk.shade(lame, 40))
        parts.append(rim([p for p in rings[-1] if p.z > 0.02], 0.007, M["gold"], "sabatonrim", sides=4))
    # the toe and the sole under it all
    bm = bmesh.new()
    rings = []
    for y in (-0.37, -0.42, -0.46, -0.49):
        hw, top = sec(y)
        h = max(0.02, top / 2.0)
        r = uk.ring(V((cx, a.y + y, h)), V((1, 0, 0)), V((0, 0, 1)), hw * 1.06, h, 10, power=3.0, start=-math.pi / 2)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    uk.loft(bm, rings, cap1=True)
    parts.append(uk.shade(uk.mesh("toe_L", bm, [M["gold"]]), 40))
    bm = bmesh.new()
    sole = [V((cx + math.cos(t) * 0.15, a.y - 0.08 + math.sin(t) * 0.4, 0.0)) for t in (2 * math.pi * k / 16 for k in range(16))]
    vs = [bm.verts.new(p) for p in sole] + [bm.verts.new(p + V((0, 0, 0.03))) for p in sole]
    bm.faces.new(vs[:16][::-1])
    for k in range(16):
        bm.faces.new((vs[k], vs[(k + 1) % 16], vs[16 + (k + 1) % 16], vs[16 + k]))
    parts.append(uk.shade(uk.mesh("sole_L", bm, [M["leather"]]), 50))
    c = (cx, a.y + 0.13)
    parts += strap(c, ring2(c, 0.15, 0.16, 0.15, 0.28, 14), 0.035, 0.012, M["leather"], M["gold"], buckle_at=0, name="anklestrap_L")
    return uk.join(parts, "foot_L")


def shin(rig, M):
    """The left greave, slim at the ankle and full at the calf with a ridge
    down the shin and two straps, a lame under the knee, and the poleyn: a
    domed knee cop with a heart-shaped fan on the outer side."""
    k = rig.pivot("LegLL")
    a = rig.pivot("FootL")
    top_ = k + V((0.015, 0.02, -0.03))
    bot = a + V((0.015, -0.02, 0.13))
    ts = (0.0, 0.08, 0.35, 0.58, 0.8, 0.86, 0.87, 1.0)
    radii = ((0.11, 0.114, 0.114), (0.122, 0.126, 0.126), (0.14, 0.142, 0.172), (0.164, 0.152, 0.207),
             (0.162, 0.152, 0.192), (0.16, 0.152, 0.174), (0.172, 0.164, 0.182), (0.154, 0.152, 0.144))
    path = [lerp(bot, top_, t) for t in ts]
    rings = [ring2((p.x, p.y), rx, ryf, ryb, p.z, 12, power=2.2, ridge=0.015, ridge_w=0.35) for p, (rx, ryf, ryb) in zip(path, radii)]
    lip = [V((p.x, p.y, p.z - 0.014)) for p in grow(rings[0], (bot.x, bot.y), -0.022)]
    bm = bmesh.new()
    uk.loft(bm, [lip] + rings, cap1=True)
    parts = [uk.shade(uk.mesh("shin_L", bm, [M["gold"]]), 38)]
    parts.append(rim(rings[0] + [rings[0][0]], 0.009, M["gold"], "greaverim"))
    for t, buckle in ((0.3, 3), (0.68, 3)):
        p = lerp(bot, top_, t)
        rx, ryf, ryb = (lerp(radii[2][i], radii[3][i], 0.3) for i in range(3)) if t < 0.5 else radii[4]
        c = (p.x, p.y)
        parts += strap(c, ring2(c, rx, ryf, ryb, p.z, 16), 0.04, 0.01, M["leather"], M["gold"], buckle_at=buckle, name="greavestrap_L")
    c = k + V((0.012, -0.08, 0.035))
    bm = bmesh.new()
    rings = []
    for lat, sc in ((0.0, 1.0), (0.3, 0.98), (0.65, 0.88), (1.0, 0.66), (1.3, 0.34), (1.5, 0.08)):
        rings.append(uk.ring(c + V((0, -math.sin(lat) * 0.13, 0)), V((1, 0, 0)), V((0, 0, 1)), 0.2 * sc, 0.18 * sc, 12, 2.2))
    lip = [c + (p - c) * 1.08 + V((0, 0.025, 0)) for p in rings[0]]
    uk.loft(bm, [lip] + rings, cap1=True)
    parts.append(uk.shade(uk.mesh("poleyn_L", bm, [M["gold"]]), 40))
    parts.append(rim(lip + [lip[0]], 0.009, M["gold"], "poleynrim"))
    # the fan: heart-shaped, on the outer side
    f = c + V((0.185, 0.05, 0.0))
    grid = []
    for r in range(4):
        t = r / 3
        row = []
        for j in range(7):
            s = lerp(-1.0, 1.0, j / 6)
            z = s * 0.15 * (1 - 0.35 * t)
            y = -0.05 + 0.2 * t - 0.06 * (1 - s * s) * t
            row.append(f + V((0.02 + 0.06 * t * (1 - abs(s) * 0.4), y, z)))
        grid.append(row)
    parts.append(shell("fan_L", grid, [M["gold"]], 0.014, 0.0))
    parts.append(rivets([(c + V((0.17, 0.0, 0.0)), V((1, 0, 0)))], 0.014, M["gold"], "fanrivet"))
    return uk.join(parts, "shin_L")


def thigh(rig, M):
    """The left cuisse, tapering to the knee, stepped by two lames at the
    top and strapped behind, and the left half of the tabard over it."""
    h = rig.pivot("LegUL")
    k = rig.pivot("LegLL")
    bot = k + V((0.0, 0.015, 0.04))
    top_ = h + V((0.025, 0.0, 0.02))
    ts = (0.0, 0.12, 0.4, 0.66, 0.74, 0.75, 0.86, 0.87, 0.97, 1.03)
    radii = ((0.16, 0.172, 0.172), (0.174, 0.188, 0.188), (0.207, 0.228, 0.228), (0.232, 0.258, 0.258), (0.236, 0.262, 0.262),
             (0.248, 0.274, 0.274), (0.25, 0.276, 0.276), (0.262, 0.288, 0.288), (0.258, 0.284, 0.284), (0.24, 0.262, 0.262))
    path = [lerp(bot, top_, t) for t in ts]
    rings = [ring2((p.x, p.y), rx, ryf, ryb, p.z, 12, power=2.2, ridge=0.017, ridge_w=0.3) for p, (rx, ryf, ryb) in zip(path, radii)]
    lip = [V((p.x, p.y, p.z - 0.016)) for p in grow(rings[0], (bot.x, bot.y), -0.024)]
    bm = bmesh.new()
    uk.loft(bm, [lip] + rings, cap1=True)
    parts = [uk.shade(uk.mesh("thigh_L", bm, [M["gold"]]), 38)]
    for idx in (0, 4, 6):
        parts.append(rim(rings[idx] + [rings[idx][0]], 0.008, M["gold"], "cuisserim"))
    p = lerp(bot, top_, 0.5)
    c = (p.x, p.y)
    parts += strap(c, ring2(c, 0.225, 0.25, 0.25, p.z, 16), 0.045, 0.011, M["leather"], M["gold"], buckle_at=8, name="cuissestrap_L")
    parts.append(tabard_flap(rig, M))
    return uk.join(parts, "thigh_L")


def tabard_flap(rig, M):
    """The left half of the tabard: player-colour cloth from under the
    faulds to above the knee, falling in soft folds round the front of the
    thigh, its hem and inner edge embroidered in gold. It rides the thigh,
    so the legs never pass through it."""
    h = rig.pivot("LegUL")
    cols, rows = 10, 6
    top, bottom = h.z + 0.07, h.z - 0.66
    grid = []
    for r in range(rows + 1):
        t = r / rows
        row = []
        for c in range(cols + 1):
            s = c / cols
            ang = lerp(-0.14, 1.22, s)
            rad = 0.31 + 0.06 * t + 0.02 * math.sin(s * math.pi * 3.0 + 0.5) * t
            row.append(V((h.x + 0.02 + math.sin(ang) * rad, h.y - math.cos(ang) * (rad + 0.02), lerp(top, bottom, t))))
        grid.append(row)
    parts = [shell("flap_L", grid, [M["teamcolor"], M["teamcolor_lining"]], 0.016, -1.0, 1)]
    hem = [[p + V((0, 0, 0.06)) for p in grid[rows]], [p + V((0, 0, -0.006)) for p in grid[rows]]]
    out = [p + (p - V((h.x, h.y, p.z))).normalized() * 0.004 for p in hem[0]]
    hem = [out, [p + (p - V((h.x, h.y, p.z))).normalized() * 0.004 for p in hem[1]]]
    parts.append(shell("flaphem_L", hem, [M["goldthread"]], 0.022, 0.0))
    inner = [grid[r][0] for r in range(rows + 1)]
    parts.append(shell("flapedge_L", [[p + V((0.0, -0.004, 0)) for p in inner], [p + V((0.055, -0.01, 0)) for p in inner]], [M["goldthread"]], 0.022, 0.0))
    return uk.join(parts, "flap_L")


# ---------------------------------------------------------------- hip and torso

TORSO = ((2.42, 0.37, 0.31, 0.28, 0.0), (2.52, 0.38, 0.32, 0.285, 0.005), (2.53, 0.395, 0.335, 0.29, 0.006),
         (2.62, 0.415, 0.35, 0.295, 0.012), (2.63, 0.43, 0.365, 0.3, 0.014), (2.78, 0.48, 0.415, 0.31, 0.032),
         (2.95, 0.52, 0.45, 0.32, 0.042), (3.07, 0.51, 0.43, 0.315, 0.036), (3.16, 0.45, 0.35, 0.29, 0.02),
         (3.23, 0.33, 0.25, 0.24, 0.0))


def torso_at(z):
    for (z0, *r0), (z1, *r1) in zip(TORSO, TORSO[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / max(1e-6, z1 - z0)
            return [lerp(x, y, t) for x, y in zip(r0, r1)]
    return list(TORSO[0][1:]) if z < TORSO[0][0] else list(TORSO[-1][1:])


def hip(rig, M):
    """The belt, four lames of faulds flaring over the hips with rivets at
    their sides, and a short skirt of mail under them."""
    c = (0.0, 0.02)
    n = 22
    parts = []
    lames = ((2.47, 2.35, (0.39, 0.325, 0.295), (0.44, 0.375, 0.33), 0.02), (2.37, 2.25, (0.435, 0.37, 0.325), (0.485, 0.42, 0.36), 0.035),
             (2.27, 2.14, (0.48, 0.415, 0.355), (0.53, 0.465, 0.39), 0.05), (2.16, 2.03, (0.525, 0.46, 0.385), (0.575, 0.51, 0.42), 0.07))
    for k, (z0, z1, r0, r1, dip) in enumerate(lames):
        top = ring2(c, *r0, z0, n)
        low = ring2(c, *r1, z1, n)
        for p in low:
            p.z -= dip * max(0.0, -(p.y - c[1]) / r1[1]) ** 2
        parts.append(shell("fauld%d" % k, [top + [top[0]], low + [low[0]]], [M["gold"]], 0.012, -1.0, centre=c))
        roll = grow(low, c, 0.006)
        parts.append(rim(roll, 0.009, M["gold"], "fauldrim", closed=True))
        pts = []
        for i in (n // 2 - 2, n // 2 + 2, 0, n - 2, 2):
            p = lerp(top[i], low[i], 0.45)
            nrm = V((p.x - c[0], p.y - c[1], 0)).normalized()
            pts.append((p + nrm * 0.008, nrm))
        parts.append(rivets(pts, 0.013, M["gold"], "fauldrivets"))
    # mail under the faulds, short enough for the thighs to swing under it
    mail = []
    for z, rx, ryf, ryb in ((2.12, 0.5, 0.44, 0.38), (1.95, 0.52, 0.46, 0.4), (1.85, 0.53, 0.47, 0.41)):
        r = ring2(c, rx, ryf, ryb, z, n)
        mail.append(r + [r[0]])
    parts.append(mail_patch(mail, M["mail"], "mailskirt"))
    # the belt and its buckle
    bm = bmesh.new()
    rings = [ring2(c, 0.385, 0.325, 0.295, z, n) for z in (2.565, 2.56, 2.455, 2.45)]
    rings[1], rings[2] = grow(rings[1], c, 0.016), grow(rings[2], c, 0.016)
    uk.loft(bm, rings)
    parts.append(uk.shade(uk.mesh("belt", bm, [M["leather"]]), 50))
    bm = bmesh.new()
    fr = [V((x, -0.352, z)) for x, z in ((-0.075, 2.45), (0.075, 2.45), (0.075, 2.565), (-0.075, 2.565))]
    uk.loft(bm, tube_rings(fr + [fr[0]], [(0.012, 0.012)] * 5, 5))
    parts.append(uk.shade(uk.mesh("buckle", bm, [M["gold"]]), 50))
    studs = []
    for i in range(n):
        if i in (n * 3 // 4, n * 3 // 4 - 1, n * 3 // 4 + 1):
            continue
        p = ring2(c, 0.404, 0.343, 0.313, 2.508, n)[i]
        studs.append((p, V((p.x - c[0], p.y - c[1], 0)).normalized()))
    parts.append(rivets(studs[::2], 0.011, M["gold"], "beltstuds"))
    return uk.join(parts, "hip")


def torso(rig, M):
    """Breastplate and backplate with a keel down the middle and rolled
    edges at the neck and arms, a plackart over the belly rising to a
    point, a gorget of two lames, and mail in the armpits."""
    c = (0.0, 0.0)
    n = 26
    parts = []
    bm = bmesh.new()
    rings = [ring2(c, rx, ryf, ryb, z, n, power=2.3, ridge=ridge, ridge_w=0.22) for z, rx, ryf, ryb, ridge in TORSO]
    uk.loft(bm, rings, cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("torso", bm, [M["gold"]]), 32))
    parts.append(rim(grow(rings[-2], c, 0.004), 0.011, M["gold"], "neckrim", closed=True))
    # the plackart, standing a little proud of the breastplate
    grid = []
    cols, rows = 10, 5
    for r in range(rows + 1):
        row = []
        for k in range(cols + 1):
            ph = lerp(-1.3, 1.3, k / cols)
            top = 2.92 - 0.32 * (abs(ph) / 1.3) ** 1.2
            z = lerp(2.445, top, r / rows)
            rx, ryf, ryb, ridge = torso_at(z)
            x = math.sin(ph) * (rx + 0.022)
            y = -math.cos(ph) * (ryf + 0.022) - ridge * math.exp(-(x / (0.22 * rx)) ** 2) * math.cos(ph)
            row.append(V((x, y, z)))
        grid.append(row)
    parts.append(shell("plackart", grid, [M["gold"]], 0.014, -1.0, centre=c))
    parts.append(rim([p + (p - V((0, 0, p.z))).normalized() * 0.012 for p in grid[-1]], 0.012, M["gold"], "plackartrim"))
    rv = []
    for k in (2, cols // 2, cols - 2):
        p = lerp(grid[1][k], grid[2][k], 0.5)
        rv.append((p + (p - V((0, 0, p.z))).normalized() * 0.015, (p - V((0, 0, p.z))).normalized()))
    parts.append(rivets(rv, 0.014, M["gold"], "plackartrivets"))
    # the gorget: two lames, each rolled at its top
    for z0, z1, r0, r1 in ((3.17, 3.25, 0.292, 0.258), (3.235, 3.31, 0.255, 0.222)):
        lo = ring2(c, r0, r0 * 0.95, r0 * 0.92, z0, 20)
        hi = ring2(c, r1, r1 * 0.95, r1 * 0.92, z1, 20)
        parts.append(shell("gorget", [lo + [lo[0]], hi + [hi[0]]], [M["gold"]], 0.012, -1.0, centre=c))
        parts.append(rim(grow(hi, c, 0.003), 0.009, M["gold"], "gorgetrim", closed=True))
    # mail in each armpit, under where the pauldrons ride
    for side in (-1, 1):
        grid = []
        for r in range(3):
            z = lerp(3.02, 2.78, r / 2)
            row = []
            for k in range(5):
                y = lerp(-0.18, 0.18, k / 4)
                rx = torso_at(z)[0]
                row.append(V((side * (rx * 0.97 + 0.012), y, z)))
            grid.append(row)
        parts.append(mail_patch(grid, M["mail"], "armpit"))
    return uk.join(parts, "torso")


# ---------------------------------------------------------------- shoulders and arms

def pauldron(rig, M):
    """The left pauldron: a cop over the shoulder and three lames stepping
    down the arm like shingles, each enamelled in the player's colour with
    a gilded rolled edge, a gilded crest rising to a point over the outside
    as the original's spike does, and a strap over the shoulder."""
    s = rig.pivot("ShoulderL")
    c = V((s.x + 0.14, s.y, s.z - 0.085))
    parts = []

    def plate(a0, a1, rad, ys, lift, name, cols=7):
        grid = []
        ymax = max(abs(ys[0]), abs(ys[-1])) + 0.012
        for y in ys:
            f = math.sqrt(max(0.0, 1.0 - (y / ymax) ** 2))
            row = []
            for j in range(cols):
                a = lerp(a0, a1, j / (cols - 1))
                r = rad * max(0.18, f) ** 0.8
                row.append(V((c.x + math.sin(a) * r, c.y + y, c.z + math.cos(a) * r * 0.82 - lift)))
            grid.append(row)
        sh = shell(name, grid, [M["teamcolor_enamel"], M["gold"]], 0.02, -1.0, 0, 1, centre=tuple(c))
        edge = [row[-1] for row in grid]
        return sh, edge, grid

    ys = [lerp(-0.39, 0.39, k / 8) for k in range(9)]
    cop, edge, grid = plate(-0.2, 1.3, 0.35, ys, 0.0, "pauldron_L", 8)
    parts += [cop, rim(edge, 0.012, M["gold"], "copedge")]
    parts.append(rim([row[0] for row in grid], 0.012, M["gold"], "copinner"))
    for k in range(3):
        ys = [lerp(-0.35 + 0.035 * k, 0.35 - 0.035 * k, j / 6) for j in range(7)]
        lame, edge, _ = plate(0.95 + 0.1 * k, 1.62 + 0.06 * k, 0.36 + 0.012 * k, ys, 0.05 + 0.07 * k, "pauldronlame_L", 5)
        parts += [lame, rim(edge, 0.01, M["gold"], "lameedge")]
    # the crest, highest over the middle, its tip leaning out like the original's spike
    base, tip = [], []
    for k in range(9):
        y = lerp(-0.28, 0.28, k / 8)
        a = 0.62
        f = math.sqrt(max(0.0, 1.0 - (y / 0.402) ** 2))
        r = 0.35 * max(0.18, f) ** 0.8
        p = V((c.x + math.sin(a) * r, c.y + y, c.z + math.cos(a) * r * 0.82))
        hgt = 0.03 + 0.11 * math.cos(y / 0.28 * math.pi / 2) ** 2
        base.append(p - V((0.012, 0, 0.012)))
        tip.append(p + V((0.45 * hgt, 0, hgt)))
    parts.append(shell("crest_L", [base, tip], [M["gold"]], 0.018, 0.0))
    parts.append(rim(tip, 0.008, M["gold"], "crestrim"))
    return uk.join(parts, "pauldron_L")


def upper_arm(rig, M):
    """The left rerebrace: a tube from under the pauldron to the elbow,
    stepped by two lames at the top, strapped, rolled at its foot."""
    u = rig.pivot("ArmUL")
    e = rig.pivot("ArmLL")
    top = u + V((0.07, 0.0, 0.08))
    end = e + V((0.0, -0.005, 0.03))
    ts = (0.0, 0.18, 0.19, 0.34, 0.35, 0.65, 1.0)
    radii = [(0.19, 0.184), (0.192, 0.188), (0.2, 0.196), (0.196, 0.192), (0.204, 0.2), (0.186, 0.182), (0.168, 0.165)]
    path = [lerp(top, end, t) for t in ts]
    rings = tube_rings(path, radii, 12)
    bm = bmesh.new()
    uk.loft(bm, rings, cap0=True)
    parts = [uk.shade(uk.mesh("upperarm_L", bm, [M["gold"]]), 40)]
    for idx in (2, 4, 6):
        parts.append(rim(rings[idx] + [rings[idx][0]], 0.008, M["gold"], "rerebracerim"))
    return uk.join(parts, "upperarm_L")


def forearm(rig, M):
    """The left vambrace with two straps, the couter over the elbow with
    its fan, and mail in the crook of the arm."""
    e = rig.pivot("ArmLL")
    w = rig.pivot("HandL")
    path = [lerp(e + V((0, 0, 0.03)), w + V((0, 0, 0.05)), t) for t in (0.0, 0.35, 0.7, 1.0)]
    rings = tube_rings(path, [(0.167, 0.165), (0.168, 0.16), (0.15, 0.142), (0.134, 0.13)], 12)
    bm = bmesh.new()
    uk.loft(bm, rings, cap0=True)
    parts = [uk.shade(uk.mesh("forearm_L", bm, [M["gold"]]), 40)]
    parts.append(rim(rings[-1] + [rings[-1][0]], 0.009, M["gold"], "vambracerim"))
    for t in (0.35, 0.75):
        p = lerp(path[0], path[-1], t)
        rx = lerp(0.168, 0.134, t) + 0.004
        c = (p.x, p.y)
        parts += strap(c, ring2(c, rx, rx, rx, p.z, 14), 0.035, 0.009, M["leather"], M["gold"], buckle_at=0, name="vambracestrap_L")
    cop = dome(e + V((0.045, 0.08, 0.0)), (0.17, 0.145, 0.145), 12, 7, [M["gold"]], "couter_L")
    parts.append(cop)
    f = e + V((0.18, 0.04, 0.0))
    grid = []
    for r in range(3):
        t = r / 2
        row = []
        for j in range(7):
            s_ = lerp(-1.0, 1.0, j / 6)
            row.append(f + V((0.02 + 0.05 * t, -0.08 + 0.17 * t - 0.05 * (1 - s_ * s_) * t, s_ * 0.13 * (1 - 0.3 * t))))
        grid.append(row)
    parts.append(shell("couterfan_L", grid, [M["gold"]], 0.014, 0.0))
    grid = []
    for r in range(3):
        row = []
        for j in range(5):
            a = lerp(2.2, 4.0, j / 4)
            p = lerp(e + V((0, 0, 0.1)), e + V((0, 0, -0.08)), r / 2)
            row.append(p + V((math.cos(a) * 0.16, math.sin(a) * 0.16 - 0.02, 0)))
        grid.append(row)
    parts.append(mail_patch(grid, M["mail"], "elbowmail"))
    return uk.join(parts, "forearm_L")


def hand(rig, M):
    """The left gauntlet: a flared cuff, a leather glove closed round a grip
    that runs fore and aft, gilded plates over the back of the hand and the
    knuckles, and a plated thumb, as the right one closes round the sword."""
    w = rig.pivot("HandL")
    g = rig.pivot("Sword")
    gx, gz = -g.x, g.z
    parts = []
    path = [w + V((0.0, 0.0, 0.13)), w + V((0.0, 0.0, 0.0)), w + V((0.0, 0.0, -0.1))]
    rings = tube_rings(path, [(0.165, 0.158), (0.134, 0.128), (0.107, 0.102)], 14)
    bm = bmesh.new()
    uk.loft(bm, rings)
    parts.append(uk.shade(uk.mesh("cuff_L", bm, [M["gold"]]), 40))
    parts.append(rim(rings[0] + [rings[0][0]], 0.01, M["gold"], "cuffrim"))
    bm = bmesh.new()
    rings = []
    for y, s, kn in ((-0.11, 0.8, 0.0), (-0.09, 1.0, 0.012), (-0.05, 1.0, 0.004), (-0.04, 1.0, 0.012), (0.0, 1.0, 0.004),
                     (0.012, 0.99, 0.012), (0.05, 0.96, 0.004), (0.062, 0.93, 0.01), (0.1, 0.72, 0.0)):
        r = []
        for i in range(12):
            t = 2 * math.pi * i / 12
            bump = kn * max(0.0, math.cos(t - 0.6)) ** 2
            r.append(V((gx + 0.008 + math.cos(t) * (0.09 * s + bump), w.y + y, gz - 0.006 + math.sin(t) * (0.08 * s + bump))))
        rings.append(r)
    uk.loft(bm, rings, cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("glove_L", bm, [M["leather"]]), 45))
    for k, (z0, z1) in enumerate(((w.z - 0.01, w.z - 0.07), (w.z - 0.06, w.z - 0.12), (w.z - 0.11, w.z - 0.16))):
        rows = []
        for z in (z0, z1):
            rows.append([V((gx + 0.078 + 0.012 * k - 0.025 * a * a, w.y + a * 0.105, z)) for a in (lerp(-1.0, 1.0, i / 6) for i in range(7))])
        parts.append(shell("handplate_L", rows, [M["gold"]], 0.014, 0.0))
        parts.append(rim(rows[-1], 0.006, M["gold"], "handplaterim"))
    for k in range(4):
        y = -0.075 + k * 0.05
        parts.append(dome(V((gx + 0.06, w.y + y, gz - 0.07)), (0.03, 0.022, 0.026), 8, 5, [M["gold"]], "knuckle"))
    path = [V((gx - 0.04, w.y + 0.04, gz + 0.07)), V((gx - 0.07, w.y - 0.03, gz + 0.06)), V((gx - 0.06, w.y - 0.09, gz + 0.03))]
    bm = bmesh.new()
    uk.loft(bm, tube_rings(path, [(0.033, 0.031), (0.031, 0.029), (0.025, 0.023)], 8), cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("thumb_L", bm, [M["leather"]]), 45))
    parts.append(dome(V((gx - 0.07, w.y - 0.02, gz + 0.085)), (0.03, 0.045, 0.014), 8, 4, [M["gold"]], "thumbplate"))
    return uk.join(parts, "hand_L")


# ---------------------------------------------------------------- the sword

def sword(rig, M):
    """A longsword as long as the original's, held edge up at rest along -Y
    from the right fist: a fullered blade with a ricasso, a gilded cross
    with flared quillons and a langet, a leather grip bound with gilded
    wire, and a round gilded pommel."""
    g = rig.pivot("Sword")
    guard_y = g.y - 0.234
    parts = []
    bm = bmesh.new()
    rows = []
    for t, half, thick in ((0.0, 0.085, 0.034), (0.05, 0.085, 0.032), (0.06, 0.1, 0.03), (0.5, 0.09, 0.026), (0.82, 0.072, 0.022),
                           (0.93, 0.045, 0.016), (0.985, 0.012, 0.006), (1.0, 0.0, 0.002)):
        y = guard_y - 0.03 - t * (2.58 - 0.03)
        rows.append([V((g.x, y, g.z + half)), V((g.x + thick * 0.55, y, g.z + half * 0.4)), V((g.x + thick * 0.5, y, g.z)),
                     V((g.x + thick * 0.55, y, g.z - half * 0.4)), V((g.x, y, g.z - half)), V((g.x - thick * 0.55, y, g.z - half * 0.4)),
                     V((g.x - thick * 0.5, y, g.z)), V((g.x - thick * 0.55, y, g.z + half * 0.4))])
    uk.loft(bm, rows, cap0=True)
    parts.append(uk.shade(uk.mesh("blade", bm, [M["blade"]]), 25))
    bm = bmesh.new()
    path = [V((g.x, guard_y, g.z + z)) for z in (-0.37, -0.33, -0.2, -0.06, 0.0, 0.06, 0.2, 0.33, 0.37)]
    rings = []
    for p, r in zip(path, (0.026, 0.044, 0.036, 0.04, 0.046, 0.04, 0.036, 0.044, 0.026)):
        rings.append(uk.ring(p, V((1, 0, 0)), V((0, 1, 0)), r * 0.9, r * 1.1, 10))
    uk.loft(bm, rings, cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("guard", bm, [M["gold"]]), 45))
    parts.append(dome(V((g.x, guard_y - 0.05, g.z)), (0.02, 0.05, 0.05), 8, 5, [M["gold"]], "langet"))
    bm = bmesh.new()
    rings = []
    for k in range(11):
        y = lerp(guard_y + 0.02, g.y + 0.6, k / 10)
        r = 0.041 + 0.004 * math.sin(k * 1.7)
        rings.append(uk.ring(V((g.x, y, g.z)), V((1, 0, 0)), V((0, 0, 1)), r, r * 1.15, 8))
    uk.loft(bm, rings, cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("grip", bm, [M["leather"]]), 50))
    for y in (guard_y + 0.03, g.y + 0.59):
        ring_ = [V((g.x + math.cos(t) * 0.048, y, g.z + math.sin(t) * 0.055)) for t in (2 * math.pi * k / 10 for k in range(10))]
        parts.append(rim(ring_, 0.008, M["gold"], "gripring", closed=True))
    parts.append(dome(V((g.x, g.y + 0.69, g.z)), (0.065, 0.06, 0.082), 12, 8, [M["gold"]], "pommel"))
    return uk.join(parts, "sword")


# ---------------------------------------------------------------- the cape

# The script's idle pose turns the cape's lower pieces back on their hinges
# by these degrees, each from the one above (araking.cob's restore, read from
# the engine's pose dump). Each piece is built turned the other way, so the
# cape hangs whole while he stands and bends at its hinges only as he moves.
IDLE_TURN = {"Cape2": 5.625, "Cape3": 0.352, "Cape4": -4.219}


def idle_turn(rig, name):
    """Where the idle pose moves a cape piece, as a matrix in model space."""
    m = Matrix.Identity(4)
    for k, deg in IDLE_TURN.items():
        p = rig.pivot(k)
        m = m @ Matrix.Translation(p) @ Matrix.Rotation(math.radians(deg), 4, "X") @ Matrix.Translation(-p)
        if k == name:
            return m
    return Matrix.Identity(4)


def cape(rig, M):
    """The cape in the original's four pieces, each hinged where the
    original's is: heavy wool in the player's colour with a darker lining,
    gathered in small pleats under a gold bar across the shoulders. The
    pleats run together into deep folds that fan out down the back, ease
    where the cloth lies on the hips, and open again to a hem that swings
    out behind. Its sides turn forward round the body, and an embroidered
    border runs down them and along the hem."""
    names = ["Cape1", "Cape2", "Cape3", "Cape4"]
    piv = [rig.pivot(nm) for nm in names]
    top = 3.24
    inner, edge = 28, 0.94
    us = [1.0] + [edge - 2.0 * edge * c / inner for c in range(inner + 1)] + [-1.0]
    mats = [M["teamcolor"], M["teamcolor_lining"], M["goldthread"], M["goldthread"]]

    def smooth(a, b, x):
        t = min(1.0, max(0.0, (x - a) / (b - a)))
        return t * t * (3.0 - 2.0 * t)

    def half_width(z):
        if z > 2.24:
            return 0.5 + 0.38 * min(1.0, (top - z) / 1.0) ** 0.5
        return 0.88 + 0.06 * (2.24 - z) / 2.24

    def hem(u):
        """The hem's height across: uneven, and lifting at the corners."""
        return 0.07 + 0.02 * math.sin(5.3 * u + 0.8) - 0.025 * u + 0.09 * smooth(0.8, 1.0, abs(u))

    def at(u, z):
        """The cloth's outside at u across (1 at its left edge, -1 at its right) and height z."""
        drop = smooth(top, top - 0.6, z)
        swing = min(1.0, max(0.0, (1.5 - z) / 1.45)) ** 2
        gather = smooth(top, top - 0.12, z)
        w = u + 0.04 * math.sin(3.1 * u + 0.4) + 0.025 * math.sin(7.7 * u + 1.9)
        ph = 2.0 * math.pi * 2.2 * w + 0.6 * math.sin(1.1 * z + 0.5) + 0.7 * swing
        deep = 0.09 * (0.15 + 0.85 * smooth(3.2, 1.6, z)) * (1.0 - 0.45 * math.exp(-((z - 2.05) / 0.22) ** 2))
        deep *= (0.8 + 0.2 * math.sin(2.0 * math.pi * 1.6 * u + 1.1)) * (1.0 + 0.3 * swing) * gather
        pleat = 0.014 * smooth(2.6, 3.1, z) * gather
        x = u * half_width(z) + 0.33 * deep * math.sin(ph) - 0.05 * swing
        y = max(lerp(0.27, 0.5, drop), torso_at(z)[2] + 0.012 + 0.05 * gather)  # clear of the backplate
        y += 0.05 * smooth(2.5, 2.0, z) + 0.07 * swing
        y += deep * math.cos(ph) + pleat * math.cos(2.0 * math.pi * 4.0 * w)
        y -= (0.16 * u * u + 0.07 * smooth(0.8, 1.0, abs(u))) * drop
        # the free edges ripple as they fall
        rip = smooth(0.7, 1.0, abs(u)) * drop
        y += 0.04 * rip * math.sin(6.0 * z + 1.7 * u)
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
            # a tongue tucked in behind the piece above, so no gap opens as they swing
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
            path = [V((u * hw, 0.282, top - 0.02)) for u in (0.97, 0.5, 0.0, -0.5, -0.97)]
            bm = bmesh.new()
            uk.loft(bm, tube_rings(path, [(0.028, 0.028)] * 5, 8), cap0=True, cap1=True)
            parts.append(uk.shade(uk.mesh("capebar", bm, [M["gold"]]), 50))
            for p in (path[0], path[-1]):
                parts.append(dome(p, (0.05, 0.03, 0.05), 8, 6, [M["gold"]], "capeboss"))
        out[name] = uk.join(parts, "cape%d" % (k + 1))
        out[name].data.transform(idle_turn(rig, name).inverted())
    return out
