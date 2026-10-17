"""Lokken's robes, armour, hands and staff for lokken.py: a long black wool
robe trimmed in dark bronze, split down the middle on the original's legs
and frayed at the hem; a belt with a bronze skull for its buckle and a
second belt slung across it; a high flared collar edged in bone with bone
spurs, and a mantle with sharp, upturned shoulders in the player's colour,
its lapels open on a dark panel and a dark red stone on a chain; wide
sleeves; armour of blackened, pitted iron: toothed pauldrons with a bronze
skull and spikes, barbed bracers, plates on the backs of the hands,
greaves with spiked knee cops and pointed sabatons; skeletal hands with
long nails, the right closed on the staff, the left open to cast; a long
cape torn at its foot and rotted through; and a twisted staff, its strands
parted by a burning vein, with a skull under a forked bronze head holding a
crystal.

body(rig, idle, M, assets) returns {piece: [(part, frame)]}: a part built
at rest in model space ("rest"), or where the idle pose holds it ("idle").
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import elsin_armour as ea
import unitkit as uk

V = Vector


def lerp(a, b, t):
    return a + (b - a) * t


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def _g(a, s):
    return math.exp(-(a / s) ** 2)


def grid_ob(name, rows, mats, closed=False, thick=None, offset=-1.0, mat_offset=1, angle=40, mat_of=None, outward=False, hole=None):
    """A sheet through rows of points, optionally closed round and thickened;
    mat_of(r, c) picks each face's material, outward turns its faces out
    first, and hole(r, c) leaves a face out."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    n = len(rows[0])
    for r in range(len(rows) - 1):
        for c in range(n if closed else n - 1):
            if hole is not None and hole(r, c):
                continue
            d = (c + 1) % n
            f = bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][d], vs[r][d]))
            f.material_index = mat_of(r, c) if mat_of else 0
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    ob = uk.mesh(name, bm, mats, outward=outward)
    if thick:
        m = ob.modifiers.new("solid", "SOLIDIFY")
        m.thickness, m.offset, m.use_even_offset = thick, offset, True
        m.material_offset = mat_offset
        uk.apply(ob)
    return uk.shade(ob, angle)


def tube(name, path, radii, mat, n=8, cap=True, angle=50):
    bm = bmesh.new()
    uk.loft(bm, ea.tube_rings([V(p) for p in path], radii, n), cap0=cap, cap1=cap)
    return uk.shade(uk.mesh(name, bm, [mat]), angle)


# ---------------------------------------------------------------- the robe

# The robe's body at height z: (z, centre y, half width, front, back).
ROBE = ((2.32, 0.07, 0.39, 0.29, 0.29), (2.2, 0.08, 0.4, 0.3, 0.3), (1.95, 0.09, 0.46, 0.33, 0.33), (1.6, 0.09, 0.5, 0.35, 0.37),
        (1.3, 0.09, 0.54, 0.37, 0.4), (0.9, 0.09, 0.58, 0.39, 0.44), (0.5, 0.09, 0.62, 0.41, 0.47), (0.04, 0.09, 0.66, 0.43, 0.51))


def robe_at(z):
    for a, b in zip(ROBE, ROBE[1:]):
        if b[0] <= z <= a[0]:
            t = (a[0] - z) / (a[0] - b[0])
            return tuple(lerp(a[i], b[i], t) for i in range(1, 5))
    return ROBE[0][1:] if z > ROBE[0][0] else ROBE[-1][1:]


def robe_point(t, z, grow=0.0):
    """The robe's outside at angle t round from the front (toward +x
    positive) and height z: an ellipse deeper behind, in soft folds that
    deepen toward the hem."""
    cy, rx, ry_f, ry_b = robe_at(z)
    ct, st = math.cos(t), math.sin(t)
    ry = ry_f if ct > 0 else ry_b
    fold = (0.008 + 0.034 * smooth(2.2, 0.2, z)) * math.sin(10.0 * t + 0.8 * math.sin(3.0 * t) + 1.3)
    fold += 0.012 * smooth(1.8, 0.2, z) * math.sin(23.0 * t + 2.0)
    k = 1.0 + (fold + grow) / max(rx, ry)
    return V((st * rx * k, cy - ct * ry * k, z))


def hem_z(t):
    """The robe's frayed hem round the body: uneven, with torn tongues."""
    base = 0.05 + 0.025 * math.sin(7.0 * t + 0.4) + 0.015 * math.sin(17.0 * t + 1.1)
    tear = max(0.0, math.sin(5.0 * t + 0.9)) ** 8 * 0.12 + max(0.0, math.sin(11.0 * t + 2.7)) ** 12 * 0.08
    return base + tear


def robe_half(name, side, z_top, z_low, M, hem=False, tongue=0.0):
    """One side's half of the robe between two heights (side 1 his left,
    +x), lapping over the other half in front and under it behind. The
    bronze trim runs down beside the front's middle."""
    cols = 18
    ts = [lerp(-0.14, math.pi + 0.14, c / cols) for c in range(cols + 1)]
    rows_n = max(3, int(round((z_top - z_low) / 0.14)) + 1)
    zs = ([z_top + tongue] if tongue else []) + [lerp(z_top, z_low, r / (rows_n - 1)) for r in range(rows_n)]
    rows = []
    for r, z in enumerate(zs):
        row = []
        for c, t in enumerate(ts):
            zz = hem_z(t) if (hem and r == len(zs) - 1) else z
            over = 0.0
            if t < 0.0:
                over = 0.007 * side
            elif t > math.pi:
                over = -0.007 * side
            p = robe_point(t, zz, over + (0.004 if tongue and r == 0 else 0.0))
            row.append(V((p.x * side, p.y, p.z)))
        rows.append(row if side > 0 else row[::-1])
    # the faces nearest the front's middle are the dark panel, one column the trim
    trim_c = 2

    def mat_of(r, c):
        return 2 if (c if side > 0 else cols - 1 - c) == trim_c else 0

    mats = [M["robe"], M["robe_lining"], M["bronzethread"], M["bronzethread"]]
    return grid_ob(name, rows, mats, thick=0.012, mat_of=mat_of, angle=50)


# ---------------------------------------------------------------- belts

def belt(M, assets):
    """The belt at the waist with a skull of dark bronze for its buckle, and
    a second belt slung from his left hip across to his right thigh,
    studded with bronze."""
    parts = []
    z = 2.2
    ring = [robe_point(2 * math.pi * i / 32, z, 0.01) for i in range(32)]
    parts += ea.strap(V((0.0, 0.08)), ring, 0.09, 0.016, M["leather"], M["bronze"], buckle_at=0, name="belt")
    front = robe_point(0.0, z, 0.035)
    rows = []
    for k in range(5):
        a = lerp(-1.0, 1.0, k / 4)
        rows.append([front + V((a * 0.1 * (1 - 0.25 * b * b), -0.012 * (1 - a * a) - 0.008 * (1 - b * b), b * 0.075)) for b in (lerp(-1.0, 1.0, j / 4) for j in range(5))])
    parts.append(ea.shell("buckle", rows, [M["bronze"]], 0.02, 0.0))
    sk = skull_ob(assets, M, 0.15, ratio=0.025, mat=M["bronze"])
    sk.data.transform(Matrix.Translation(front + V((0.0, -0.05, 0.005))))
    parts.append(sk)
    parts.append(ea.rim([front + V((math.cos(a) * 0.105, -0.022, math.sin(a) * 0.08)) for a in (2 * math.pi * k / 16 for k in range(17))], 0.009, M["bronze"], "bucklerim"))
    # the slung belt
    pts = []
    for k in range(17):
        t = lerp(-0.25 * math.pi, 1.25 * math.pi, k / 16)
        zz = lerp(2.12, 1.95, 0.5 - 0.5 * math.cos(t + 0.25 * math.pi)) - 0.04 * math.sin(t)
        pts.append(robe_point(-t, zz, 0.035))
    rows = [[p + V((0, 0, 0.035)) for p in pts], [p - V((0, 0, 0.035)) for p in pts]]
    ob = grid_ob("slungbelt", rows, [M["leather"]], thick=0.012, offset=1.0)
    parts.append(ob)
    studs = []
    for k in range(1, 16, 2):
        p = pts[k]
        out = V((p.x, p.y - 0.08, 0)).normalized()
        studs.append((p + out * 0.016, out))
    parts.append(ea.rivets(studs, 0.017, M["bronze"], "studs"))
    return uk.join(parts, "belts")


# ---------------------------------------------------------------- the chest

CHEST = ((2.1, 0.08, 0.39, 0.29, 0.29), (2.35, 0.05, 0.41, 0.3, 0.28), (2.55, 0.0, 0.45, 0.32, 0.28), (2.75, -0.06, 0.47, 0.33, 0.28),
         (2.9, -0.1, 0.46, 0.31, 0.28), (3.0, -0.15, 0.36, 0.26, 0.23), (3.08, -0.2, 0.17, 0.14, 0.14))


def chest_at(z):
    for a, b in zip(CHEST, CHEST[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            return tuple(lerp(a[i], b[i], t) for i in range(1, 5))
    return CHEST[0][1:] if z < CHEST[0][0] else CHEST[-1][1:]


def chest_point(t, z, grow=0.0):
    cy, rx, ry_f, ry_b = chest_at(z)
    ct, st = math.cos(t), math.sin(t)
    ry = ry_f if ct > 0 else ry_b
    k = 1.0 + grow / max(rx, ry)
    return V((st * rx * k, cy - ct * ry * k, z))


def chest(M):
    """The robe over the chest, closed round, a dark panel down its front
    between bronze trims, and the neck's opening."""
    n = 30
    zs = [c[0] for c in CHEST]
    zs = [lerp(zs[i], zs[i + 1], f) for i in range(len(zs) - 1) for f in (0.0, 0.5)] + [zs[-1]]
    rows = [[chest_point(2 * math.pi * i / n, z) for i in range(n)] for z in zs]

    def mat_of(r, c):
        t = 2 * math.pi * (c + 0.5) / n
        a = abs(math.atan2(math.sin(t), math.cos(t)))
        return 1 if a < 0.3 else (2 if a < 0.42 else 0)

    return grid_ob("chest", rows, [M["robe"], M["teamcolor_lining"], M["bronzethread"], M["robe_lining"], M["robe_lining"], M["robe_lining"]],
                   closed=True, thick=0.015, mat_offset=3, mat_of=mat_of, outward=True)


def neck(M):
    """The thin neck, from inside the robe's collar up into the head, which
    leans out ahead of him."""
    path = [V((0.0, -0.17, 2.86)), V((0.0, -0.26, 2.97)), V((0.0, -0.36, 3.04)), V((0.0, -0.44, 3.1))]
    return tube("neck", path, [(0.1, 0.1), (0.095, 0.1), (0.09, 0.095), (0.085, 0.09)], M["flesh"], n=12, angle=50)


def pendant(M):
    """A dark red stone in a bronze setting on a chain from the neck."""
    parts = []
    gem_at = chest_point(0.0, 2.68, 0.05)
    left = [chest_point(t, z, 0.03) for t, z in ((0.9, 3.01), (0.6, 2.92), (0.3, 2.8))]
    for s in (1, -1):
        path = [V((p.x * s, p.y, p.z)) for p in left] + [gem_at + V((0, 0, 0.06))]
        parts.append(tube("chain", path, [(0.009, 0.009)] * len(path), M["bronze"], n=5))
    parts.append(ea.dome(gem_at + V((0, -0.02, 0)), (0.045, 0.03, 0.06), 10, 8, [M["gem"]], "pendantgem"))
    parts.append(ea.rim([gem_at + V((math.cos(a) * 0.05, -0.012, math.sin(a) * 0.066)) for a in (2 * math.pi * k / 14 for k in range(15))], 0.01, M["bronze"], "pendantrim"))
    parts.append(ea.dome(gem_at + V((0, 0, -0.09)), (0.014, 0.012, 0.03), 6, 5, [M["bronze"]], "pendanttip"))
    return uk.join(parts, "pendant")


# ---------------------------------------------------------------- the mantle and collar

NECK_C = V((0.0, -0.2, 3.06))


def mantle_edge(t):
    """How low the mantle comes at angle t round from the front: down the
    chest at the lapels, short over the shoulders, down the back."""
    a = abs(t)
    front = lerp(2.52, 2.86, smooth(0.25, 1.2, a))
    return lerp(front, 2.5, smooth(1.9, 3.0, a))


def lapel(z):
    """The angle round from the front where the lapel's edge lies at z: they
    meet under the collar and part down the chest."""
    return lerp(0.12, 0.62, smooth(3.04, 2.55, z))


def wing(t, z):
    """The mantle's shoulder standing out to a sharp, upturned point."""
    a = abs(t)
    reach = 0.46 * _g(a - 1.45, 0.45) * smooth(3.05, 2.86, z)
    return reach


def mantle(M):
    """The mantle's left half (mirrored for the right): from the collar's
    foot over the shoulder to its point and down the back, its lapel open
    at the front, bronze along its edges."""
    rows_n, cols = 9, 22
    rows = []
    for r in range(rows_n):
        k = r / (rows_n - 1)
        row = []
        for c in range(cols + 1):
            s = c / cols
            z0 = 3.05
            t0 = lapel(lerp(z0, 2.55, k))
            t = lerp(t0, math.pi + 0.02, s)
            z = lerp(z0, mantle_edge(t), k ** 0.9)
            tip = _g(t - 1.45, 0.3) * k ** 2
            shoulder = _g(t - 1.5, 0.5)
            p = chest_point(t, z, 0.05 + 0.03 * k + 0.05 * shoulder * (1 - k) + wing(t, z) * k ** 0.8 + 0.06 * k * smooth(1.8, 3.0, t))
            p.z += 0.2 * tip                        # the point turns up
            if r == 0:
                p = NECK_C + (V((p.x, p.y, 0.0)) - V((NECK_C.x, NECK_C.y, 0.0))).normalized() * 0.21 + V((0, 0, 0.0))
                p.z = z0
            row.append(p)
        rows.append(row)

    def mat_of(r, c):
        return 2 if (r == rows_n - 2 or c == 0) else 0

    mats = [M["teamcolor"], M["teamcolor_lining"], M["bronze"], M["bronze"]]
    ob = grid_ob("mantle_L", rows, mats, thick=0.022, mat_of=mat_of, angle=45)
    edge = [rows[-1][c] for c in range(cols + 1)]
    lap = [rows[r][0] for r in range(rows_n)]
    trim = ea.rim(lap[::-1] + edge, 0.016, M["bronze"], "mantletrim", sides=5)
    return uk.join([ob, trim], "mantle_L")


def collar(M):
    """The high collar's left half: standing from the neck and flaring wide
    like a funnel, lowest behind, rising to a sharp point at the front of
    each side beside the face, lined dark, edged in bone with bone spurs
    along its top."""
    rows = []
    cols, rows_n = 18, 8
    for r in range(rows_n):
        v = r / (rows_n - 1)
        row = []
        for c in range(cols + 1):
            t = lerp(0.42, math.pi, c / cols)
            base = NECK_C + V((math.sin(t) * 0.2, -math.cos(t) * 0.19, -0.03))
            out = V((math.sin(t), -math.cos(t), 0.0))
            tip = _g(t - 0.68, 0.3)
            height = 0.36 + 0.18 * tip + 0.04 * _g(t - 1.8, 0.5)
            reach = 0.14 + 0.12 * _g(t - 1.5, 0.8) + 0.04 * tip
            p = base + out * (reach * v ** 1.6) + V((0.0, -0.04 * tip * v ** 2, height * v))
            row.append(p)
        rows.append(row)

    def mat_of(r, c):
        return 2 if r == rows_n - 2 else 0

    ob = grid_ob("collar_L", rows, [M["teamcolor"], M["teamcolor_lining"], M["bronze"], M["bronze"]], thick=0.02, offset=1.0, mat_of=mat_of, angle=40)
    trim = ea.rim(rows[-1] + [rows[r][0] for r in range(rows_n - 2, -1, -1)], 0.017, M["bone"], "collartrim", sides=5)
    spurs = []
    for c in range(2, cols, 3):
        p, q = rows[-1][c], rows[-2][c]
        d = (p - q).normalized()
        spurs.append(tube("spur", [p - d * 0.01, p + d * 0.04, p + d * 0.085], [(0.015, 0.012), (0.009, 0.007), (0.001, 0.001)], M["bone"], n=6))
    return uk.join([ob, trim] + spurs, "collar_L")


# ---------------------------------------------------------------- arms

def upper_sleeve(rig, M):
    """The upper sleeve, full, from under the mantle's shoulder to the elbow."""
    a = rig.pivot("luparm")
    b = rig.pivot("larm")
    d = (b - a)
    path = [a + V((-0.02, 0.0, 0.04)), a + d * 0.08, a + d * 0.5, b + d.normalized() * 0.06]
    return tube("uppersleeve_L", path, [(0.08, 0.08), (0.15, 0.15), (0.155, 0.15), (0.13, 0.13)], M["robe"], n=14, angle=50)


def bell_sleeve(elbow, wrist, M, name, droop=V((0, 0, -1)), size=1.0):
    """A wide sleeve from the elbow, opening past the wrist like a bell, its
    mouth hanging lower on the side droop points to, lined dark red and
    edged in bronze thread."""
    axis = (wrist - elbow)
    length = axis.length
    axis.normalize()
    ax, ay = uk.frame(axis)
    rings = []
    n = 18
    for k, (f, r) in enumerate(((0.0, 0.13), (0.35, 0.155), (0.7, 0.2 * size), (1.0, 0.25 * size), (1.15, 0.29 * size))):
        ring = []
        for i in range(n):
            th = 2 * math.pi * i / n
            dirn = ax * math.cos(th) + ay * math.sin(th)
            hang = max(0.0, dirn.dot(droop)) ** 1.5
            fold = 0.012 * math.sin(5 * th + k)
            ring.append(elbow + axis * (length * f + 0.26 * hang * f * f) + droop * (0.12 * hang * f * f) + dirn * (r + fold + 0.05 * hang * f))
        rings.append(ring)
    rows = rings
    ob = grid_ob(name, rows, [M["robe"], M["robe"]], closed=True, thick=0.014, offset=-1.0, angle=50, outward=True)
    cuff = ea.rim(rows[-1] + [rows[-1][0]], 0.016, M["bronzethread"], name + "trim", sides=5, closed=False)
    return uk.join([ob, cuff], name)


def wrist(elbow, wrist_p, M, name):
    """A thin, bony wrist inside the sleeve."""
    d = wrist_p - elbow
    return tube(name, [elbow + d * 0.6, wrist_p + d.normalized() * 0.02], [(0.045, 0.04), (0.038, 0.032)], M["flesh"], n=8)


# ---------------------------------------------------------------- armour

def spike(base, direction, length, r, mat, name="spike"):
    d = V(direction).normalized()
    return tube(name, [base - d * 0.01, base + d * length * 0.55, base + d * length], [(r, r), (r * 0.5, r * 0.5), (0.001, 0.001)], mat, n=6, angle=40)


def arm_frame(a, b):
    """The arm's axis from a to b, and the ways out from it: outward (+x)
    and forward."""
    d = (b - a).normalized()
    out = (V((1.0, 0.0, 0.0)) - d * d.x).normalized()
    return d, out, d.cross(out)


def pauldron(rig, M, assets):
    """His left pauldron below the mantle's point: three lames of
    blackened iron overlapping down the upper arm, their lower edges cut
    into jagged teeth, a small skull of dark bronze on the outside and two
    short spikes raking back."""
    a, b = rig.pivot("luparm"), rig.pivot("larm")
    d, out, fwd = arm_frame(a, b)
    parts = []
    cols = 14
    for k in range(3):
        s0 = -0.03 + 0.085 * k
        r = 0.18 + 0.014 * k
        rows = []
        for i, sv in enumerate((s0, s0 + 0.06, s0 + 0.12)):
            row = []
            for c in range(cols + 1):
                phi = lerp(-1.85, 1.85, c / cols)
                bulge = 0.018 if i == 1 else 0.0
                q = a + d * sv + (out * math.cos(phi) + fwd * math.sin(phi)) * (r + bulge)
                if i == 2 and c % 2 == 1:
                    q += d * 0.05
                row.append(q)
            rows.append(row)
        parts.append(grid_ob("lame", rows, [M["iron"]], thick=0.014, outward=True, angle=35))
        parts.append(ea.rim(rows[0], 0.008, M["iron"], "lamerim", sides=4))
    boss = skull_ob(assets, M, 0.085, ratio=0.012, mat=M["bronze"])
    face_out = Matrix((tuple(-fwd), tuple(-out), tuple(-d))).transposed().to_4x4()
    boss.data.transform(Matrix.Translation(a + d * 0.05 + out * 0.21) @ face_out)
    parts.append(boss)
    for phi in (-0.75, 0.75):
        base = a + d * 0.0 + (out * math.cos(phi) + fwd * math.sin(phi)) * 0.19
        parts.append(spike(base, out * 0.75 - d * 0.35 + fwd * (0.25 * math.copysign(1.0, phi)), 0.1, 0.02, M["iron"]))
    return uk.join(parts, "pauldron_L")


def bracer(e, w, M, name):
    """A bracer of blackened iron round the forearm above the wrist,
    flaring to a toothed cuff over the back of the hand, with barbs down its
    outer side."""
    d, out, fwd = arm_frame(e, w)
    length = (w - e).length
    rings = []
    n = 14
    for f, r in ((0.5, 0.064), (0.75, 0.066), (0.95, 0.072), (1.08, 0.092)):
        ring = []
        for i in range(n):
            phi = 2 * math.pi * i / n
            q = e + d * (length * f) + (out * math.cos(phi) + fwd * math.sin(phi)) * r
            if f > 1.0 and i % 2 == 0:
                q += d * 0.035
            ring.append(q)
        rings.append(ring)
    parts = [grid_ob(name, rings, [M["iron"]], closed=True, thick=0.01, outward=True, angle=35)]
    parts.append(ea.rim(rings[0] + [rings[0][0]], 0.009, M["iron"], name + "rim", sides=4))
    for f in (0.62, 0.8):
        base = e + d * (length * f) + out * 0.068
        parts.append(spike(base, out * 0.8 + d * 0.6, 0.06, 0.014, M["iron"], "barb"))
    return uk.join(parts, name)


def greave(rig, M):
    """His left greave under the robe: a ridged shin plate of blackened
    iron with a spiked knee cop."""
    k, f = rig.pivot("lleg"), rig.pivot("lfoot")
    d, out, fwd = arm_frame(k, f)
    length = (f - k).length
    rows = []
    for t in (0.02, 0.3, 0.6, 0.88):
        row = []
        for c in range(9):
            phi = lerp(-1.4, 1.4, c / 8)
            r = lerp(0.088, 0.07, t) + 0.016 * _g(phi, 0.25)
            row.append(k + d * (length * t) + (fwd * math.cos(phi) + out * math.sin(phi)) * r)
        rows.append(row)
    parts = [grid_ob("greave_L", rows, [M["iron"]], thick=0.012, outward=True, angle=35)]
    cop = k + fwd * 0.07 + d * 0.0
    parts.append(ea.dome(cop, (0.07, 0.05, 0.07), 10, 6, [M["iron"]], "kneecop"))
    parts.append(spike(cop + fwd * 0.03, fwd, 0.08, 0.02, M["iron"]))
    return uk.join(parts, "greave_L")


def hand_plate(w, along, across, outward, length, M, name):
    """A pointed plate of blackened iron over the back of a hand, ridged
    down its middle, from the wrist toward the knuckles."""
    rows = []
    for t in (0.0, 0.35, 0.7, 1.0):
        row = []
        half = 0.055 * (1.0 - 0.75 * max(0.0, t - 0.6) / 0.4)
        for c in range(5):
            u = lerp(-1.0, 1.0, c / 4)
            row.append(w + along * (length * t) + across * (u * half) + outward * (0.012 * (1 - u * u) + 0.004))
        rows.append(row)
    return grid_ob(name, rows, [M["iron"]], thick=0.01, offset=-1.0, angle=35, outward=True)


# ---------------------------------------------------------------- hands

def finger(base, direction, length, curl, M, r=0.016, side_ax=None, name="finger"):
    """A bony finger of three joints from base along direction, each joint
    bent by curl radians about side_ax, knuckles standing out, a long dark
    nail at its tip."""
    parts = []
    lens = (0.45, 0.32, 0.23)
    d = V(direction).normalized()
    ax = side_ax if side_ax is not None else uk.frame(d)[0]
    p = V(base)
    path, radii = [p.copy()], [(r, r * 0.9)]
    knuckles = []
    for k, f in enumerate(lens):
        d = Matrix.Rotation(curl, 3, ax) @ d
        q = p + d * length * f
        mid = (p + q) * 0.5
        path += [mid, q]
        rr = r * (1.0 - 0.18 * k)
        radii += [(rr * 0.78, rr * 0.7), (rr * 0.95, rr * 0.88)]
        knuckles.append((q, rr * 1.12))
        p = q
    parts.append(tube(name, path, radii, M["flesh"], n=7, angle=60))
    for c, rk in knuckles[:-1]:
        parts.append(ea.dome(c, (rk, rk, rk), 7, 5, [M["flesh"]], "knuckle"))
    tip = p
    nail = [tip - d * 0.01, tip + d * 0.045, tip + d * 0.07 + Matrix.Rotation(curl, 3, ax) @ d * 0.01]
    parts.append(tube("nail", nail, [(r * 0.7, r * 0.4), (r * 0.55, r * 0.3), (0.003, 0.002)], M["nail"], n=6, angle=40))
    return parts


def hand_open(rig, M):
    """His left hand, open to cast: a long, bony hand hanging from the
    wrist, the palm turned forward, the fingers parted and a little curled,
    the bones of its back standing out."""
    w = rig.pivot("lhand")
    parts = []
    rings = []
    for f, wid, th in ((0.0, 0.05, 0.034), (0.35, 0.074, 0.03), (0.75, 0.084, 0.027), (1.0, 0.08, 0.025)):
        c = w + V((0.0, -0.012 * f, -0.2 * f))
        rings.append(uk.ring(c, V((1, 0, 0)), V((0, 1, 0)), wid, th, 10, 2.5))
    bm = bmesh.new()
    uk.loft(bm, rings, cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("palm_L", bm, [M["flesh"]]), 50))
    curl_ax = V((-1.0, 0.0, 0.0))
    for k, x in enumerate((-0.054, -0.018, 0.018, 0.054)):
        parts.append(tube("tendon", [w + V((x * 0.7, 0.03, -0.01)), w + V((x, 0.026, -0.19))], [(0.009, 0.007), (0.011, 0.009)], M["flesh"], n=5))
        base = w + V((x * 1.05, -0.012, -0.205))
        direction = V((x * 0.9 - 0.02, -0.05, -1.0))
        length = (0.18, 0.225, 0.235, 0.2)[k]
        parts += finger(base, direction, length, 0.22 + 0.04 * k, M, r=0.016, side_ax=curl_ax, name="finger_L")
    parts += finger(w + V((0.065, -0.02, -0.06)), V((0.6, -0.5, -0.65)), 0.17, 0.18, M, r=0.017, side_ax=V((0.0, 0.6, -0.5)).normalized(), name="thumb_L")
    parts.append(hand_plate(w + V((0.0, 0.03, -0.01)), V((0.0, 0.0, -1.0)), V((1.0, 0.0, 0.0)), V((0.0, 1.0, 0.0)), 0.17, M, "handplate_L"))
    return uk.join(parts, "hand_L")


def hand_grip(rig, M):
    """His right hand closed on the staff, which runs fore and aft through
    it: the back of the hand outward, the fingers wrapped round under it,
    the nails showing, the thumb over the top."""
    w = rig.pivot("rhand")
    g = rig.pivot("staff")
    parts = []
    gx, gz = g.x, g.z
    rings = []
    for f, wid, th in ((0.0, 0.05, 0.035), (0.4, 0.075, 0.03), (0.8, 0.08, 0.028), (1.0, 0.076, 0.026)):
        c = w + V((-0.03 * f, -0.005 * f, -0.2 * f))
        rings.append(uk.ring(c, V((0, 1, 0)), V((1, 0, 0)), wid, th, 10, 2.5))
    bm = bmesh.new()
    uk.loft(bm, rings, cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("palm_R", bm, [M["flesh"]]), 50))
    staff_r = 0.055
    for k, y in enumerate((-0.054, -0.018, 0.018, 0.054)):
        parts.append(tube("tendon", [w + V((-0.035, y * 0.7, -0.01)), w + V((-0.062, y, -0.17))], [(0.009, 0.007), (0.011, 0.009)], M["flesh"], n=5))
        # round the staff: from the knuckle under the shaft and up its far side
        path = []
        for j in range(7):
            a = lerp(math.radians(150), math.radians(395), j / 6)
            rr = staff_r + 0.02
            path.append(V((gx + math.cos(a) * rr, w.y + y * 0.95, gz + math.sin(a) * rr * 0.95)))
        radii = [(0.017, 0.015)] * 2 + [(0.013, 0.012), (0.015, 0.014), (0.012, 0.011), (0.013, 0.012), (0.009, 0.008)]
        parts.append(tube("finger_R", path, radii, M["flesh"], n=7, angle=60))
        for j in (2, 4):
            parts.append(ea.dome(path[j], (0.016, 0.016, 0.016), 6, 4, [M["flesh"]], "knuckle"))
        parts.append(ea.dome(path[-1] + (path[-1] - path[-2]).normalized() * 0.012, (0.012, 0.01, 0.022), 6, 4, [M["nail"]], "nail"))
    thumb = [w + V((0.03, -0.07, -0.08)), V((gx + 0.02, w.y - 0.1, gz + 0.075)), V((gx + 0.05, w.y - 0.14, gz + 0.05))]
    parts.append(tube("thumb_R", thumb, [(0.02, 0.018), (0.016, 0.015), (0.012, 0.011)], M["flesh"], n=7))
    parts.append(ea.dome(thumb[-1] + V((0.008, -0.02, -0.008)), (0.012, 0.022, 0.01), 6, 4, [M["nail"]], "nail"))
    parts.append(hand_plate(w + V((-0.035, 0.0, -0.01)), V((-0.15, 0.0, -1.0)).normalized(), V((0.0, 1.0, 0.0)), V((-1.0, 0.0, 0.0)), 0.15, M, "handplate_R"))
    return uk.join(parts, "hand_R")


# ---------------------------------------------------------------- feet

def boot(rig, M):
    """His left boot, long and pointed, dark leather, only its toe showing
    under the robe."""
    f = rig.pivot("lfoot")
    rings = []
    for y, w, h, z in ((0.1, 0.075, 0.09, 0.07), (0.0, 0.085, 0.09, 0.065), (-0.12, 0.08, 0.075, 0.055), (-0.24, 0.065, 0.055, 0.045),
                       (-0.34, 0.04, 0.035, 0.035), (-0.4, 0.012, 0.014, 0.03)):
        c = V((f.x, f.y + y, z))
        rings.append(uk.ring(c, V((1, 0, 0)), V((0, 0, 1)), w, h, 12, 2.3))
    bm = bmesh.new()
    uk.loft(bm, rings, cap0=True, cap1=True)
    ob = uk.shade(uk.mesh("boot_L", bm, [M["iron"]]), 45)
    lames = []
    for y, w, h, z in ((-0.06, 0.088, 0.088, 0.062), (-0.16, 0.078, 0.07, 0.052), (-0.26, 0.064, 0.052, 0.044)):
        ring = uk.ring(V((f.x, f.y + y, z)), V((1, 0, 0)), V((0, 0, 1)), w + 0.006, h + 0.006, 10, 2.3, start=-0.3, arc=math.pi + 0.6, closed=False)
        lames.append(ea.rim(ring, 0.007, M["iron"], "lame", sides=4))
    sole = tube("sole_L", [V((f.x, f.y + 0.1, 0.012)), V((f.x, f.y - 0.38, 0.016))], [(0.07, 0.012), (0.03, 0.01)], M["leather"], n=8)
    leg = tube("shin_L", [V((f.x, f.y + 0.03, 0.12)), V((f.x * 0.7, f.y - 0.05, 0.9))], [(0.07, 0.07), (0.08, 0.08)], M["robe"], n=8)
    return uk.join([ob, sole, leg] + lames, "boot_L")


# ---------------------------------------------------------------- the cape

CAPE_HINGES = ("cape1", "cape2", "cape3", "cape4")


def cape_point(u, z, idle):
    """The cape's outside where the idle pose hangs it, at u across (1 his
    left edge, -1 his right) and height z: wrapped round the shoulders under
    the mantle, falling behind him in deep folds to a trailing foot."""
    top = 2.86
    drop = smooth(top, top - 0.5, z)
    width = lerp(0.5, 0.92, smooth(2.86, 0.3, z))
    w = u + 0.03 * math.sin(3.1 * u + 0.4)
    deep = (0.015 + 0.1 * smooth(2.7, 0.4, z)) * (0.3 + 0.7 * drop)
    ph = 2.0 * math.pi * 2.2 * w + 0.5 * math.sin(1.3 * z)
    x = u * width + 0.35 * deep * math.sin(ph)
    back = lerp(0.1, 0.46, smooth(2.86, 2.2, z)) + 0.2 * smooth(2.0, 0.0, z)
    y = back + deep * (1.0 + math.cos(ph)) - (0.3 * u * u) * (1.0 - 0.45 * smooth(2.2, 0.5, z))
    return V((x, y, z))


def cape_hem(u):
    """The cape's foot, torn into ragged tongues, some of them long."""
    base = 0.04 + 0.025 * math.sin(6.3 * u + 0.7) + 0.02 * math.sin(17.0 * u + 2.1)
    tear = (max(0.0, math.sin(9.0 * u + 1.3)) ** 6 * 0.38 + max(0.0, math.sin(4.0 * u + 0.2)) ** 10 * 0.3
            + max(0.0, math.sin(13.0 * u + 0.4)) ** 12 * 0.22)
    return base + tear


# Where the cloth has rotted through, low on the cape: (u across, height, radius).
CAPE_HOLES = ((0.5, 0.62, 0.17), (-0.42, 1.05, 0.14), (-0.7, 0.38, 0.15))


def rotten(u, z):
    """True where the cloth at u across and height z has rotted away."""
    return any(math.hypot((u - hu) * 0.9, z - hz) < r for hu, hz, r in CAPE_HOLES)


def fray(u, z):
    """A point near a hole drawn out to its ragged edge, so the holes are
    round and torn, not square."""
    for hu, hz, r in CAPE_HOLES:
        du, dz = (u - hu) * 0.9, z - hz
        d = math.hypot(du, dz)
        if 1e-6 < d < r + 0.06:
            a = math.atan2(dz, du)
            edge = r + 0.025 * math.sin(5.0 * a + hu * 9.0) + 0.015 * math.sin(11.0 * a)
            k = edge / d
            return hu + du * k / 0.9, hz + dz * k
    return u, z


def cape(idle, M):
    """The cape in the original's four pieces, each hinged where the
    original's is, built hanging as the idle pose holds them: wool in the
    player's colour, lined darker, torn and frayed at the foot."""
    piv = [idle.pivot(n) for n in CAPE_HINGES]
    cols = 28
    us = [lerp(1.0, -1.0, c / cols) for c in range(cols + 1)]
    out = {}
    tops = [2.86, piv[1].z, piv[2].z, piv[3].z]
    for k, name in enumerate(CAPE_HINGES):
        last = k == 3
        hi = tops[k]
        lo = piv[k + 1].z if not last else 0.0
        n = max(3, int(round((hi - lo) / 0.16)))
        rows = []
        zs = [lerp(hi, lo, r / n) for r in range(n + 1)]
        if k:
            zs = [hi + 0.12, hi + 0.04] + zs
        for z in zs:
            row = []
            for c, u in enumerate(us):
                zz = z
                if last and z == lo:
                    zz = cape_hem(u) + (0.05 if c % 2 else 0.0)
                fu, fz = fray(u, zz)
                p = cape_point(fu, fz, idle)
                if k and z > hi:
                    p.y -= 0.018
                if abs(u) > 0.9 and zz < 1.4:
                    p.x *= 1.0 - 0.06 * (0.5 + 0.5 * math.sin(31.0 * zz)) * (1.4 - zz)   # the free edges ragged
                row.append(p)
            rows.append(row)

        def hole(r, c, zs=zs):
            return rotten(us[c] * 0.5 + us[c + 1] * 0.5, (zs[r] + zs[r + 1]) * 0.5)

        ob = grid_ob(name, rows, [M["teamcolor"], M["teamcolor_lining"]], thick=0.014, offset=-1.0, angle=45, hole=hole)
        out[name] = ob
    return out


# ---------------------------------------------------------------- the staff

def staff(rig, M, assets):
    """The staff as long as the original's, along -Y from the right fist at
    rest: two dark strands twisted about a burning vein, an iron foot, a
    bone skull below a bronze collar, and a forked bronze head holding a long
    crystal where the script's weapon point is."""
    g = rig.pivot("staff")
    gem = rig.pivot("emitwpn")
    y_butt = g.y + 1.64
    y_head = rig.pivot("emitfx4").y + 0.02
    parts = []
    # the twisted shaft: two strands round a thin vein
    n = 46
    for strand in range(2):
        path, radii = [], []
        for k in range(n + 1):
            f = k / n
            y = lerp(y_butt - 0.04, y_head, f)
            a = f * 2 * math.pi * 6.5 + strand * math.pi
            r_off = 0.024
            path.append(V((g.x + math.cos(a) * r_off, y, g.z + math.sin(a) * r_off)))
            rr = 0.034 * (1.0 + 0.12 * math.sin(f * 37.0 + strand))
            radii.append((rr, rr))
        parts.append(tube("strand", path, radii, M["wood"], n=7, angle=60))
    parts.append(tube("vein", [V((g.x, y_butt, g.z)), V((g.x, y_head, g.z))], [(0.026, 0.026), (0.026, 0.026)], M["vein"], n=8))
    # the iron foot
    parts.append(tube("ferrule", [V((g.x, y_butt - 0.12, g.z)), V((g.x, y_butt + 0.05, g.z)), V((g.x, y_butt + 0.14, g.z))],
                      [(0.05, 0.05), (0.04, 0.04), (0.008, 0.008)], M["iron"], n=8))
    # the bronze collar under the skull
    parts.append(tube("staffcollar", [V((g.x, y_head + 0.07, g.z)), V((g.x, y_head + 0.02, g.z)), V((g.x, y_head - 0.03, g.z))],
                      [(0.05, 0.05), (0.066, 0.066), (0.05, 0.05)], M["bronze"], n=10))
    # the skull, facing forward when the staff stands (rest -z)
    sk = skull_ob(assets, M, ratio=0.04)
    face_down = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()  # its -y face to -z, its +z top to -y
    m = Matrix.Translation(V((g.x, y_head - 0.14, g.z))) @ face_down @ Matrix.Scale(1.0, 4)
    sk.data.transform(m)
    parts.append(sk)
    # the forked head: three prongs curving out round the crystal and in at their tips
    base_y = y_head - 0.24
    for i in range(3):
        a = 2 * math.pi * i / 3 + math.pi / 2
        path = []
        for j in range(7):
            f = j / 6
            r = 0.05 + 0.1 * math.sin(math.pi * min(1.0, f * 0.95)) ** 0.8 - 0.03 * f
            y = base_y - f * (base_y - (gem.y - 0.24))
            path.append(V((g.x + math.cos(a) * r, y, g.z + math.sin(a) * r)))
        radii = [(0.026, 0.02), (0.024, 0.018), (0.02, 0.016), (0.018, 0.014), (0.015, 0.012), (0.011, 0.009), (0.004, 0.004)]
        parts.append(tube("prong", path, radii, M["bronze"], n=6, angle=50))
    parts.append(tube("forkbase", [V((g.x, base_y + 0.05, g.z)), V((g.x, base_y, g.z)), V((g.x, base_y - 0.04, g.z))],
                      [(0.045, 0.045), (0.06, 0.06), (0.05, 0.05)], M["bronze"], n=10))
    # the crystal: a long six-sided stone, pointed at both ends
    bm = bmesh.new()
    c = V((g.x, gem.y, g.z))
    rings = [uk.ring(c + V((0, dy, 0)), V((1, 0, 0)), V((0, 0, 1)), r, r, 6) for dy, r in ((0.17, 0.0005), (0.08, 0.06), (-0.1, 0.065), (-0.24, 0.0005))]
    uk.loft(bm, rings)
    parts.append(uk.shade(uk.mesh("crystal", bm, [M["crystal"]]), 20))
    return uk.join(parts, "staff")


def skull_ob(assets, M, size=0.2, ratio=0.06, mat=None):
    """The bundle's CC0 skull, made light, about size cells high, centred at
    the origin, facing -y, its crown up +z."""
    import lokken_head as lh
    with bpy.data.libraries.load(uk.human_bundle(assets), link=False) as (src, dst):
        dst.objects = [lh.SKULL]
    ob = dst.objects[0]
    bpy.context.scene.collection.objects.link(ob)
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    bpy.context.view_layer.update()
    ob.data.transform(ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)
    vs = [v.co for v in ob.data.vertices]
    lo = V([min(v[i] for v in vs) for i in range(3)])
    hi = V([max(v[i] for v in vs) for i in range(3)])
    c = (lo + hi) / 2
    k = size / (hi.z - lo.z)
    ob.data.transform(Matrix.Scale(k, 4) @ Matrix.Translation(-c))
    dec = ob.modifiers.new("lighter", "DECIMATE")
    dec.ratio = ratio
    uk.apply(ob)
    ob.data.materials.clear()
    ob.data.materials.append(mat or M["bone"])
    ob.name = "skull"
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# ---------------------------------------------------------------- the body

def body(rig, idle, M, assets):
    """Every part but the head: {piece: [(part, frame)]}."""
    out = {}

    def add(piece, ob, frame="rest"):
        out.setdefault(piece, []).append((ob, frame))

    # the robe: a band on the hips, then each side's half on its thigh and shin
    hip = robe_half("robehip_L", 1, 2.34, 1.9, M)
    add("hip", uk.join([hip, robe_half("robehip_R", -1, 2.34, 1.9, M)], "robehip"))
    add("hip", belt(M, assets))
    for side, up, low in ((1, "lupleg", "lleg"), (-1, "rupleg", "rleg")):
        tag = "L" if side > 0 else "R"
        add(up, robe_half("robethigh_" + tag, side, 1.96, 1.3, M, tongue=0.1))
        add(low, robe_half("robeshin_" + tag, side, 1.38, 0.05, M, hem=True, tongue=0.08))
    b = boot(rig, M)
    add("lfoot", b)
    add("rfoot", uk.mirror_x(b, "boot_R"))
    g = greave(rig, M)
    add("lleg", g)
    add("rleg", uk.mirror_x(g, "greave_R"))
    # the chest, mantle, collar and pendant
    add("torso", chest(M))
    add("torso", neck(M))
    add("torso", pendant(M))
    for make in (mantle, collar):
        ob = make(M)
        add("torso", ob)
        add("torso", uk.mirror_x(ob, ob.name[:-2] + "_R"))
    # the arms
    s = upper_sleeve(rig, M)
    add("luparm", s)
    add("ruparm", uk.mirror_x(s, "uppersleeve_R"))
    pl = pauldron(rig, M, assets)
    add("luparm", pl)
    add("ruparm", uk.mirror_x(pl, "pauldron_R"))
    e, w = rig.pivot("larm"), rig.pivot("lhand")
    add("larm", bell_sleeve(e, w, M, "sleeve_L", droop=V((0.3, 0.6, -1.0)).normalized()))
    add("larm", wrist(e, w, M, "wrist_L"))
    add("larm", bracer(e, w, M, "bracer_L"))
    add("lhand", hand_open(rig, M))
    e, w = idle.pivot("rarm"), idle.pivot("rhand")
    add("rarm", bell_sleeve(e, w, M, "sleeve_R", droop=V((-0.1, 0.35, -1.0)).normalized(), size=0.62), "idle")
    add("rarm", wrist(rig.pivot("rarm"), rig.pivot("rhand"), M, "wrist_R"))
    add("rarm", uk.mirror_x(bracer(V((-rig.pivot("rarm").x, rig.pivot("rarm").y, rig.pivot("rarm").z)),
                                   V((-rig.pivot("rhand").x, rig.pivot("rhand").y, rig.pivot("rhand").z)), M, "bracer_x"), "bracer_R"))
    add("rhand", hand_grip(rig, M))
    add("staff", staff(rig, M, assets))
    for name, ob in cape(idle, M).items():
        add(name, ob, "idle")
    return out
