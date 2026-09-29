"""Tarbuild01 and its ruins: a square paved platform ringed with green
crystal merlons, and on it a tower wound by a spiral ramp of green gravel
up to a crystal crown."""
import math
import random

from mathutils import Matrix, Vector

import kit as K

PAVE = (70, 63, 56)
PAVE_JOINT = (38, 37, 30)
WALL = (52, 51, 44)
WALL_DARK = (34, 33, 29)
FACE = (40, 39, 34)
LIP = (128, 112, 90)
GRAVEL = (50, 64, 46)
GRAVEL_LIGHT = (150, 146, 132)
GRAVEL_DARK = (18, 46, 18)
CRYSTAL = (6, 58, 14)
RUBBLE = (27, 26, 23)


def pave():
    return K.texmat("zg_pave", lambda: K.tex_cobble(PAVE, PAVE_JOINT, n=256, count=34, seed=41, var=0.3,
                                                     vein_w=5.0, dark=(22, 21, 18)), rough=0.95)


def tower_wall():
    return K.texmat("zg_wall", lambda: K.tex_mottle(WALL, WALL_DARK, n=128, seed=42, amt=0.7), rough=0.9)


def gravel():
    return K.texmat("zg_gravel", lambda: K.tex_speckle(GRAVEL, [(GRAVEL_LIGHT, 0.8), (GRAVEL_DARK, 0.55)], n=128,
                                                        seed=43, density=0.7, size=2), rough=1.0)


def lip():
    return K.mat("zg_lip", LIP, rough=0.8)


def crystal():
    return K.mat("zg_crystal", CRYSTAL, rough=0.35, spec=0.6)


def face():
    return K.texmat("zg_face", lambda: K.tex_courses(FACE, (20, 20, 18), n=128, rows=4, per_row=3, seed=44,
                                                      var=0.15, jw=2.0), rough=0.95)


def rubble_mat():
    return K.texmat("zg_rubble", lambda: K.tex_rubble((40, 38, 33), (4, 4, 3), (70, 64, 55), n=256, count=34,
                                                       seed=45), rough=1.0)


def rubble_stones():
    return [K.mat("zg_stone_a", (50, 48, 42), rough=0.95), K.mat("zg_stone_b", (34, 33, 29), rough=0.95),
            K.texmat("zg_stone_c", lambda: K.tex_mottle((66, 60, 52), (30, 29, 25), n=64, seed=46), rough=0.95),
            tower_wall()]


# ---- the platform --------------------------------------------------------------

def platform(cx, cy, w, d, h, rz, merlon_step=1.9, broken=None, seed=1, rim_gone=None, horns=None, keep=None):
    """The paved plinth: dark walls with pilasters, a rim, a green crystal
    merlon every merlon_step along each edge and a crystal horn at each
    corner. broken(x, y) -> True drops a merlon or pilaster there,
    keep(u, v) -> False drops one by its place on the plinth, and horns
    gives each corner's horn a length (front-left first, anticlockwise):
    1 whole, 0 gone, between a snapped stub."""
    rnd = random.Random(seed)
    parts = []
    slab = K.block(w, d, h, cx, cy, 0, rz, face(), "plinth")
    K.two_tone(slab, pave())
    K.uv_box(slab, 1 / 2.2)
    parts.append(slab)
    c, s = math.cos(math.radians(rz)), math.sin(math.radians(rz))

    def P(u, v):
        return (cx + u * c - v * s, cy + u * s + v * c)

    cr = crystal()
    rim = K.mat("zg_rim", (70, 64, 56), rough=0.9)
    corners = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
    for k in range(4):
        a, b = corners[k], corners[(k + 1) % 4]
        pa, pb = P(*a), P(*b)
        if rim_gone is None:
            parts += K.wall_run(pa, pb, h, 0.18, 0.5, rim, name="rim")
        else:
            parts += K.wall_run(pa, pb, h, lambda t, pa=pa, pb=pb: 0.0 if rim_gone(
                pa[0] + (pb[0] - pa[0]) * t, pa[1] + (pb[1] - pa[1]) * t) else 0.18, 0.5, rim, seg=2.0, name="rim")
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        n = max(2, int(round(L / merlon_step)))
        ux, uy = (b[0] - a[0]) / L, (b[1] - a[1]) / L
        nx, ny = uy, -ux  # outward for a counter-clockwise run
        for i in range(n):
            t = (i + 0.5) / n
            u, v = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            x, y = P(u, v)
            if (broken and broken(x, y)) or (keep and not keep(u, v)):
                continue
            ang = math.degrees(math.atan2(uy, ux)) + rz
            parts.append(K.block(0.9, 0.62, 0.7, x, y, h + 0.1, ang + rnd.uniform(-4, 4), cr, "merlon",
                                 top=(0.62, 0.42)))
            # a pilaster down the outer face under each merlon
            px, py = P(u + nx * 0.12, v + ny * 0.12)
            pil = K.block(0.7, 0.3, h + 0.05, px, py, 0, ang, face(), "pilaster")
            K.uv_box(pil, 0.5)
            parts.append(pil)
    for k, (u, v) in enumerate(corners):
        if broken and broken(*P(u, v)):
            continue
        du, dv = (1 if u > 0 else -1), (1 if v > 0 else -1)
        path = [(u - du * 0.2, v - dv * 0.2, h - 0.3), (u + du * 0.5, v + dv * 0.5, h + 0.3),
                (u + du * 1.1, v + dv * 1.1, h + 1.2), (u + du * 1.5, v + dv * 1.5, h + 2.3)]
        path = [(*P(pu, pv), pz) for pu, pv, pz in path]
        f = 1.0 if horns is None else horns[k]
        if f <= 0:
            continue
        if f >= 1:
            parts.append(K.tube(path, [0.52, 0.42, 0.26, 0.0], seg=5, sub=2, mt=cr, name="horn", shade=30))
            continue
        # snapped: the root and a short run toward the next point, flat at the break
        a_, b_ = Vector(path[1]), Vector(path[2])
        end = a_ + (b_ - a_) * f
        parts.append(K.tube([path[0], path[1], tuple(end)], [0.52, 0.45, 0.4 - 0.1 * f], seg=5, sub=1, mt=cr,
                            name="horn_stub", shade=30))
    return parts


# ---- the spiral tower ---------------------------------------------------------------

FLAT = 0.45  # each turn runs level for this much of the way round, then climbs


def _interp(vals, t, flat=FLAT):
    """Level from the front round the left side, then a climb over the back
    and right to the next level, the way the picture's terraces read."""
    k = min(int(t), len(vals) - 2)
    f = t - k
    f = 0.0 if f < flat else min(1.0, (f - flat) / (1 - flat))
    f = f * f * (3 - 2 * f)
    return vals[k] * (1 - f) + vals[k + 1] * f


def spiral(x0, y0, zb, radii, heights, widths, per_turn=36, lip_h=0.14, lip_w=0.16, keep=None, start=-90.0,
           cap_r=None, tail=FLAT, wall_top=None, wall_m=None):
    """A tower wound by a ramp: radii, heights and widths are the ramp's
    outer radius, level and width at each whole turn (turn 0 starts on the
    ground at `start` degrees and winds clockwise seen from above).
    keep(t, x, y, z) -> False leaves a step of the ramp and the wall above
    it out, for ruins, and wall_top(x, y, z) -> z breaks the wall above
    each kept step down to a ragged top."""
    T = len(radii) - 1
    N = int((T + tail) * per_turn)
    ramp_m, wall_m, lip_m = gravel(), wall_m or tower_wall(), lip()

    def ang(t):
        return math.radians(start) - 2 * math.pi * t

    def pt(t, r, z):
        a = ang(t)
        return (x0 + r * math.cos(a), y0 + r * math.sin(a), z)

    def out_dir(c):
        return (c.x - x0, c.y - y0, 0.0)

    def up(c):
        return (0, 0, 1)

    ts = [j / per_turn for j in range(N + 1)]
    R = [_interp(radii, t) for t in ts]
    Z = [_interp(heights, t) for t in ts]
    W = [_interp(widths, t) for t in ts]
    capR = cap_r if cap_r is not None else radii[-1] - widths[-1]
    zc = heights[-1]
    # the rings each strip is drawn between
    O = [pt(t, r, z) for t, r, z in zip(ts, R, Z)]
    Ol = [pt(t, r, z + lip_h) for t, r, z in zip(ts, R, Z)]
    Il = [pt(t, r - lip_w, z + lip_h) for t, r, z in zip(ts, R, Z)]
    Ir = [pt(t, r - lip_w, z) for t, r, z in zip(ts, R, Z)]
    I = [pt(t, r - w, z) for t, r, w, z in zip(ts, R, W, Z)]
    above = []
    for j, t in enumerate(ts):
        if j + per_turn <= N:
            above.append(O[j + per_turn])
        else:
            above.append(pt(t, capR, zc))
    if wall_top is not None:
        above = [(ax_, ay_, max(i_[2] + 0.15, wall_top(ax_, ay_, az_))) for (ax_, ay_, az_), i_ in zip(above, I)]
    below = [pt(t, R[j] + 0.25 * (1 - t), zb) for j, t in enumerate(ts[:per_turn + 1])]
    runs = []  # contiguous kept spans
    cur = []
    for j in range(N):
        t = ts[j]
        ok = keep is None or keep(t, *O[j])
        if ok:
            cur.append(j)
        elif cur:
            runs.append(cur)
            cur = []
    if cur:
        runs.append(cur)
    parts = []
    for run in runs:
        a, b = run[0], run[-1] + 2
        sl = slice(a, b)
        parts.append(K.ribbon(I[sl], above[sl], wall_m, out_dir, "tower_wall", smooth=30))
        parts.append(K.ribbon(Ir[sl], I[sl], ramp_m, up, "ramp"))
        parts.append(K.ribbon(O[sl], Ol[sl], lip_m, out_dir, "lip_face"))
        parts.append(K.ribbon(Ol[sl], Il[sl], lip_m, up, "lip_top"))
        parts.append(K.ribbon(Ir[sl], Il[sl], lip_m, lambda c: (x0 - c.x, y0 - c.y, 0), "lip_in"))
        if a < per_turn:
            e = min(b, per_turn + 1)
            if e - a >= 2:
                parts.append(K.ribbon(below[a:e], O[a:e], wall_m, out_dir, "skirt", smooth=30))
    for p in parts:
        if p.name.startswith(("tower_wall", "skirt")):
            K.uv_cyl(p, 0.35, x0, y0)
        else:
            K.uv_box(p, 0.6)
    if keep is None:
        parts.append(K.cyl(capR + 0.05, 0.08, seg=24, x=x0, y=y0, z=zc - 0.06, mt=wall_m, name="tower_cap"))
    # close the foot of the ramp where the first turn meets the second
    if keep is None or keep(0, *O[0]):
        parts.append(K.mesh("ramp_end", [I[0], O[per_turn], pt(0, R[per_turn], zb)], [[0, 1, 2]], wall_m))
    return parts


def crown(x, y, z, s=1.0, rz=0.0):
    """The crystal crown: a green block and a sheaf of crystal spikes."""
    cr = crystal()
    # built squat: a broad base and a spike wider and lower than the picture's
    parts = [K.block(2.1 * s, 2.1 * s, 0.8 * s, x, y, z - 0.2, rz, cr, "crown_base", top=(1.8 * s, 1.8 * s))]
    parts.append(K.pyramid(x, y, z + 0.5 * s, 1.35 * s, 1.35 * s, 3.1 * s, rz + 45, cr, "crown_spike"))
    for k in range(6):
        a = math.radians(rz + 30 + 60 * k)
        bx, by = x + 0.65 * s * math.cos(a), y + 0.65 * s * math.sin(a)
        tip = (x + 1.3 * s * math.cos(a), y + 1.3 * s * math.sin(a))
        parts.append(K.pyramid(bx, by, z + 0.4 * s, 0.62 * s, 0.62 * s, (1.3 + 0.55 * (k % 2)) * s, rz + 60 * k, cr,
                               "crown_shard", apex=tip))
    return parts


# ---- the models ------------------------------------------------------------------

PLAT = dict(cx=-0.3, cy=-0.3, w=14.4, d=14.1, h=1.6, rz=5.0)
# each level's outer radius and height, read off the terraces' front and
# left edges in the picture (their backs all line up, a steep back face)
AXIS = (-0.1, 0.59)
# built sturdier than the picture: every level wider and the tower lower
TOWER_GIRTH = 1.06
TOWER_RISE = 0.88


def rise(z):
    """A height read off the picture, lowered for the sturdy tower."""
    return PLAT["h"] + (z - PLAT["h"]) * TOWER_RISE


RADII = [r * TOWER_GIRTH for r in (5.85, 4.75, 3.75, 3.0, 2.25, 1.7, 1.2)]
HEIGHTS = [rise(z) for z in (1.7, 4.6, 6.95, 8.8, 10.1, 11.3, 12.4)]
WIDTHS = [w * TOWER_GIRTH for w in (1.1, 1.0, 0.75, 0.75, 0.55, 0.5, 0.35)]


def tarbuild01(name="Tarbuild01"):
    parts = platform(**PLAT)
    parts += spiral(AXIS[0], AXIS[1], PLAT["h"], RADII, HEIGHTS, WIDTHS)
    parts += crown(AXIS[0], AXIS[1], HEIGHTS[-1], 1.3, 10)
    return parts
