"""The Great Sage's machinery for sage.py: a steam automaton of riveted
copper, cast iron and brass on the original CRESAGE's pieces. A domed head
with a T-slot visor and a stern grille, a beard of brass rods fanned over
an enamelled chest, coil banks for shoulders, clevis elbows, mechanical
fists, a boiler and smokestack on the back, a flywheel shield and a blade
that splits into four jaws as the script opens it. Every part is modelled
at rest around the original's pivots (sage.py attaches them). Left parts
are mirrored to the right.
"""
import math

import bmesh
import bpy
from mathutils import Vector

import unitkit as uk
from elsin_armour import shell, tube_rings

V = Vector
X, Y, Z = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))
TAU = 2.0 * math.pi


def lerp(a, b, t):
    return a + (b - a) * t


def interp(rows, z):
    """Rows (key, a, b, ...) read at key z, linearly between rows."""
    if z <= rows[0][0]:
        return rows[0][1:]
    for a, b in zip(rows, rows[1:]):
        if z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            return tuple(lerp(p, q, t) for p, q in zip(a[1:], b[1:]))
    return rows[-1][1:]


# ---------------------------------------------------------------- shapes

def obj(name, bm, mats, smooth=40, outward=True):
    ob = uk.mesh(name, bm, mats, outward=outward)
    return uk.shade(ob, smooth) if smooth else ob


def loft_rings(name, rings, mats, cap0=False, cap1=False, smooth=40, mat_rows=None, cap_mats=(None, None)):
    """Quads between rings, mat_rows giving each band's material index and
    cap_mats the caps'."""
    bm = bmesh.new()
    _vs, faces = uk.loft(bm, rings, cap0=cap0, cap1=cap1)
    n = len(rings[0])
    body = n * (len(rings) - 1)
    if mat_rows:
        for i, f in enumerate(faces[:body]):
            f.material_index = mat_rows[min(i // n, len(mat_rows) - 1)]
    caps = faces[body:]
    k = 0
    for on, m in zip((cap0, cap1), cap_mats):
        if on:
            if m is not None:
                caps[k].material_index = m
            k += 1
    return obj(name, bm, mats, smooth)


def revolve(name, c, axis, profile, mats, n=24, power=2.0, cap0=True, cap1=True, smooth=40, mat_rows=None, cap_mats=(None, None), start=0.0):
    """A body of revolution about axis through c: profile [(radius, distance
    along the axis)]."""
    axis = V(axis).normalized()
    ax, ay = uk.frame(axis)
    rings = [uk.ring(V(c) + axis * h, ax, ay, r, r, n, power, start=start) for r, h in profile]
    return loft_rings(name, rings, mats, cap0, cap1, smooth, mat_rows, cap_mats)


def cyl(name, a, b, r, mats, n=16, cap0=True, cap1=True, smooth=40):
    d = V(b) - V(a)
    return revolve(name, a, d, [(r, 0.0), (r, d.length)], mats, n, cap0=cap0, cap1=cap1, smooth=smooth)


def pipe(name, path, r, mats, n=8, cap0=True, cap1=True, smooth=50):
    rings = tube_rings([V(p) for p in path], [(r, r)] * len(path), n)
    return loft_rings(name, rings, mats, cap0, cap1, smooth)


def torus(name, c, axis, R, w, t, mats, n=32, sides=6, smooth=50):
    """A ring round axis: radius R, half width w along the axis and half
    thickness t across it, for bands and rolled rims."""
    axis = V(axis).normalized()
    ax, ay = uk.frame(axis)
    bm = bmesh.new()
    rows = []
    for k in range(n):
        a = TAU * k / n
        u = ax * math.cos(a) + ay * math.sin(a)
        p = V(c) + u * R
        rows.append([bm.verts.new(p + u * math.cos(s) * t + axis * math.sin(s) * w) for s in (TAU * (j + 0.5) / sides for j in range(sides))])
    for k in range(n):
        a, b = rows[k], rows[(k + 1) % n]
        for j in range(sides):
            jj = (j + 1) % sides
            bm.faces.new((a[j], b[j], b[jj], a[jj]))
    return obj(name, bm, mats, smooth)


def loop_rim(name, pts, r, mats, sides=5, smooth=50):
    """A rolled rim along a closed loop of points."""
    n = len(pts)
    bm = bmesh.new()
    rows = []
    c = sum(pts, V()) / n
    for k in range(n):
        tan = (pts[(k + 1) % n] - pts[k - 1]).normalized()
        out = (pts[k] - c)
        out = (out - tan * out.dot(tan)).normalized()
        up = tan.cross(out).normalized()
        rows.append([bm.verts.new(pts[k] + (out * math.cos(s) + up * math.sin(s)) * r) for s in (TAU * j / sides for j in range(sides))])
    for k in range(n):
        a, b = rows[k], rows[(k + 1) % n]
        for j in range(sides):
            jj = (j + 1) % sides
            bm.faces.new((a[j], b[j], b[jj], a[jj]))
    return obj(name, bm, mats, smooth)


def gear(name, c, axis, r_root, r_tip, teeth, width, mats, smooth=30, start=0.0):
    """A spur gear across axis: teeth from r_root to r_tip, width along the axis."""
    axis = V(axis).normalized()
    ax, ay = uk.frame(axis)
    prof = []
    for k in range(teeth):
        for f, r in ((0.0, r_root), (0.2, r_root), (0.34, r_tip), (0.66, r_tip), (0.8, r_root)):
            a = start + TAU * (k + f) / teeth
            prof.append((math.cos(a) * r, math.sin(a) * r))
    rings = [[V(c) + axis * h + ax * x + ay * y for x, y in prof] for h in (-width / 2, width / 2)]
    return loft_rings(name, rings, mats, True, True, smooth)


def studs(points, r, mat, name="rivets", sides=5):
    """Domed rivet heads at points (point, outward normal): a low cone,
    shaded round."""
    bm = bmesh.new()
    for p, nrm in points:
        nrm = V(nrm).normalized()
        a, b = uk.frame(nrm)
        base = [bm.verts.new(p + (a * math.cos(t) + b * math.sin(t)) * r - nrm * r * 0.3) for t in (TAU * k / sides for k in range(sides))]
        top = bm.verts.new(p + nrm * r * 0.6)
        for k in range(sides):
            bm.faces.new((base[k], base[(k + 1) % sides], top))
    return obj(name, bm, [mat], 80)


def nuts(points, r, h, mat, name="nuts"):
    """Hexagon nuts at points (point, outward normal)."""
    bm = bmesh.new()
    for p, nrm in points:
        nrm = V(nrm).normalized()
        a, b = uk.frame(nrm)
        low = [bm.verts.new(p + (a * math.cos(t) + b * math.sin(t)) * r - nrm * h * 0.3) for t in (TAU * k / 6 for k in range(6))]
        high = [bm.verts.new(p + (a * math.cos(t) + b * math.sin(t)) * r * 0.92 + nrm * h) for t in (TAU * k / 6 for k in range(6))]
        for k in range(6):
            kk = (k + 1) % 6
            bm.faces.new((low[k], low[kk], high[kk], high[k]))
        bm.faces.new(high)
    return obj(name, bm, [mat], 30)


def rbox(name, c, half, mats, bev=0.02, segs=2):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    for v in bm.verts:
        v.co = V((c[0] + v.co.x * half[0], c[1] + v.co.y * half[1], c[2] + v.co.z * half[2]))
    ob = uk.mesh(name, bm, mats)
    uk.bevel(ob, bev, segs, angle=30)
    return uk.shade(ob, 35)


def ball(name, c, r, mats, n=10, rings=6):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=n, v_segments=rings, radius=r)
    for v in bm.verts:
        v.co += V(c)
    return obj(name, bm, mats, 60)


def spiral(c, a_axis, b_axis, r0, r1, turns, k=28, phase=0.0):
    """Points of a flat spiral round c in the plane of a_axis and b_axis."""
    pts = []
    for i in range(k + 1):
        t = i / k
        a = phase + TAU * turns * t
        r = lerp(r0, r1, t)
        pts.append(V(c) + (V(a_axis) * math.cos(a) + V(b_axis) * math.sin(a)) * r)
    return pts


def rect_tube(name, sections, mats):
    """A closed tube through cross-sections of four corners each, capped:
    a cutter for recesses."""
    bm = bmesh.new()
    uk.loft(bm, sections, cap0=True, cap1=True)
    return uk.mesh(name, bm, mats)


def pocket(ob, cutter, wall, floor, out_fn, k=0.55):
    """Cuts cutter out of ob as a recess, its walls in wall and its floor,
    the faces looking out (out_fn(point) the outward way), in floor."""
    me = ob.data
    for m in (wall, floor):
        if m.name not in [s.name for s in me.materials if s]:
            me.materials.append(m)
    cutter.data.materials.clear()
    cutter.data.materials.append(wall)
    mod = ob.modifiers.new("pocket", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    mod.solver = "EXACT"
    try:
        mod.material_mode = "TRANSFER"
    except (AttributeError, TypeError):
        pass
    uk.apply(ob)
    bpy.data.objects.remove(cutter, do_unlink=True)
    names = [s.name if s else "" for s in ob.data.materials]
    wi, fi = names.index(wall.name), names.index(floor.name)
    for p in ob.data.polygons:
        if p.material_index == wi and p.normal.dot(out_fn(p.center)) > k:
            p.material_index = fi
    return ob


class Casing:
    """A body lofted through level superellipse sections round a vertical
    line (cx, cy): rows (z, half width, depth in front, depth behind)."""

    def __init__(self, cx, cy, rows, power=3.0):
        self.cx, self.cy, self.rows, self.p = cx, cy, rows, power

    def sec(self, z):
        return interp(self.rows, z)

    def at(self, t, z, d=0.0):
        """The point at angle t (0 at +X, -pi/2 the front), d further out."""
        rx, yf, yb = self.sec(z)
        ct, st = math.cos(t), math.sin(t)
        x = math.copysign(abs(ct) ** (2.0 / self.p), ct) * (rx + d)
        sy = math.copysign(abs(st) ** (2.0 / self.p), st)
        return V((self.cx + x, self.cy + sy * ((yf if sy < 0 else yb) + d), z))

    def normal(self, t, z):
        e = 1e-3
        a, b = self.at(t - e, z), self.at(t + e, z)
        tan = b - a
        return V((tan.y, -tan.x, 0.0)).normalized()

    def ring(self, z, n, d=0.0, start=0.0):
        return [self.at(start + TAU * i / n, z, d) for i in range(n)]

    def body(self, name, zs, mats, n=32, cap0=True, cap1=True, mat_rows=None, smooth=40):
        return loft_rings(name, [self.ring(z, n) for z in zs], mats, cap0, cap1, smooth, mat_rows)

    def grid(self, t0, t1, z0, z1, d, nu, nv):
        return [[self.at(lerp(t0, t1, i / nu), lerp(z0, z1, j / nv), d) for i in range(nu + 1)] for j in range(nv + 1)]

    def panel(self, name, t0, t1, z0, z1, mat, d=0.016, thick=0.022, nu=10, nv=6):
        """A plate standing off the surface, thick inward from d."""
        return shell(name, self.grid(t0, t1, z0, z1, d, nu, nv), [mat], thick, centre=(self.cx, self.cy))

    def outline(self, t0, t1, z0, z1, d, nu, nv):
        """The panel's border, round the loop."""
        g = self.grid(t0, t1, z0, z1, d, nu, nv)
        return g[0] + [row[-1] for row in g[1:-1]] + g[-1][::-1] + [row[0] for row in g[1:-1]][::-1]

    def rivet_line(self, t0, t1, z0, z1, d, count):
        out = []
        for i in range(count):
            f = (i + 0.5) / count
            t, z = lerp(t0, t1, f), lerp(z0, z1, f)
            out.append((self.at(t, z, d), self.normal(t, z)))
        return out

    def framed(self, name, t0, t1, z0, z1, M, enamel="teamcolor_enamel", frame="copper", rivet="brass", nu=10, nv=6, every=0.14):
        """An enamelled panel in a rolled frame with rivets round it."""
        parts = [self.panel(name, t0, t1, z0, z1, M[enamel], nu=nu, nv=nv)]
        parts.append(loop_rim(name + "frame", self.outline(t0, t1, z0, z1, 0.022, nu, nv), 0.017, [M[frame]], sides=4))
        pts = []
        for (ta, za), (tb, zb) in (((t0, z0), (t1, z0)), ((t1, z0), (t1, z1)), ((t1, z1), (t0, z1)), ((t0, z1), (t0, z0))):
            length = (self.at(ta, za) - self.at(tb, zb)).length
            pts += self.rivet_line(ta, tb, za, zb, 0.03, max(2, int(length / every)))
        parts.append(studs(pts, 0.016, M[rivet], name + "rivets"))
        return parts


# ---------------------------------------------------------------- the body

TORSO = Casing(0.0, 0.12, [
    (2.40, 0.80, 0.38, 0.50),
    (2.56, 0.84, 0.42, 0.54),
    (2.90, 0.86, 0.46, 0.58),
    (3.30, 0.89, 0.48, 0.60),
    (3.70, 0.89, 0.46, 0.60),
    (3.92, 0.86, 0.40, 0.56),
    (4.02, 0.79, 0.32, 0.48),
    (4.07, 0.69, 0.24, 0.40),
    (4.09, 0.60, 0.18, 0.34),
], 3.0)
FRONT = -math.pi / 2
BACK = math.pi / 2

HEAD_C = V((0.0, -0.1, 0.0))
HEAD_DOME = 3.86
DOME_H = 0.37
HEAD_ROWS = [(3.06, 0.30), (3.12, 0.40), (3.22, 0.45), (3.45, 0.47), (3.72, 0.465), (HEAD_DOME, 0.455)]


GRILLE_W = 0.3


def grille_z(x):
    """The grille's middle at x: level, turned down a little at its ends."""
    return 3.4 - 0.03 * (x / GRILLE_W) ** 4


def head_r(z):
    if z >= HEAD_DOME:
        s = min(1.0, (z - HEAD_DOME) / DOME_H)
        return 0.455 * math.sqrt(max(0.0, 1.0 - s * s))
    return interp(HEAD_ROWS, z)[0]


def head_front_y(x, z, d=0.0):
    r = head_r(z) + d
    return HEAD_C.y - math.sqrt(max(1e-6, r * r - x * x))


def torso(rig, M):
    """The chest: a riveted copper body over a waist band, an enamelled
    front and flanks, a top plate, the boiler and smokestack behind and an
    exhaust bell each side of the head."""
    T = TORSO
    parts = []
    zs = [2.40, 2.48, 2.56, 2.90, 3.30, 3.70, 3.82, 3.92, 3.98, 4.02, 4.05, 4.07, 4.09]
    parts.append(T.body("chest", zs, [M["copper"]], n=48))
    # an iron waist band, riveted
    band = [T.ring(z, 40, 0.018) for z in (2.41, 2.43, 2.53, 2.55)]
    band[0] = T.ring(2.41, 40, -0.01)
    band[-1] = T.ring(2.55, 40, -0.01)
    parts.append(loft_rings("waistband", band + [band[0]], [M["iron"]], smooth=40))
    parts.append(studs([(T.at(TAU * k / 28, 2.48, 0.03), T.normal(TAU * k / 28, 2.48)) for k in range(28)], 0.018, M["iron"], "waistrivets"))
    # the enamelled chest under the beard, the flanks beside the head, and the back above the boiler
    parts += T.framed("chestpanel", FRONT - 0.95, FRONT + 0.95, 2.62, 3.16, M, nu=14, nv=6)
    for s in (1, -1):
        parts += T.framed("flank%d" % s, FRONT + s * 0.62, FRONT + s * 1.32, 3.26, 3.86, M, nu=8, nv=6)
        parts += T.framed("backpanel%d" % s, BACK - s * 0.14, BACK - s * 0.95, 3.32, 3.86, M, nu=8, nv=5)
    # the top plate inside a rolled rim
    top = Casing(0.0, 0.12, [(4.09, 0.60, 0.18, 0.34)], 3.0)
    bm = bmesh.new()
    vs = [bm.verts.new(p + V((0, 0, 0.012))) for p in top.ring(4.09, 40, -0.03)]
    bm.faces.new(vs)
    lo = [bm.verts.new(p) for p in top.ring(4.09, 40, -0.03)]
    for k in range(40):
        bm.faces.new((lo[k], lo[(k + 1) % 40], vs[(k + 1) % 40], vs[k]))
    parts.append(obj("topplate", bm, [M["teamcolor_enamel"]], 40))
    parts.append(loop_rim("toprim", T.ring(4.06, 36, 0.0), 0.022, [M["copper"]], sides=4))
    parts.append(studs([(top.at(TAU * k / 22, 4.09, -0.07) + V((0, 0, 0.012)), Z) for k in range(22)], 0.014, M["brass"], "toprivets"))
    # seams down the sides, riveted
    for s in (1, -1):
        t = 0.0 if s > 0 else math.pi
        parts.append(studs([(T.at(t + dt, z, 0.008), T.normal(t + dt, z)) for z in (2.7, 2.9, 3.1, 3.3, 3.5, 3.7) for dt in (-0.42, 0.42)],
                           0.017, M["iron"], "siderivets%d" % s))
    parts += boiler(M)
    parts += exhausts(M)
    parts += beard(M)
    return parts


def boiler(M):
    """The boiler drum across the back, its firebox door, the smokestack
    and the steam pipes up to the exhaust bells."""
    parts = []
    c, r = V((0.0, 0.86, 2.94)), 0.42
    prof = [(0.06, -0.86), (0.24, -0.85), (0.36, -0.82), (0.41, -0.78), (r, -0.76), (r, 0.76), (0.41, 0.78), (0.36, 0.82), (0.24, 0.85), (0.06, 0.86)]
    parts.append(revolve("boiler", c, X, prof, [M["copper"], M["brass"]], n=28, mat_rows=[1, 1, 1, 1, 0, 1, 1, 1, 1], cap_mats=(1, 1)))
    for x in (-0.7, 0.0, 0.7):
        parts.append(torus("boilerband", c + X * x, X, r + 0.012, 0.03, 0.016, [M["iron"]], n=22, sides=4))
        parts.append(studs([(c + X * x + (Y * math.cos(a) + Z * math.sin(a)) * (r + 0.03), Y * math.cos(a) + Z * math.sin(a))
                            for a in (TAU * k / 10 for k in range(10))], 0.015, M["iron"], "boilerrivets"))
    for s in (1, -1):
        parts.append(nuts([(c + X * s * 0.865, X * s)], 0.05, 0.03, M["brass"], "boilerhub"))
        parts.append(nuts([(c + X * s * 0.84 + (Y * math.cos(a) + Z * math.sin(a)) * 0.2, X * s) for a in (TAU * k / 8 for k in range(8))],
                          0.02, 0.016, M["brass"], "boilerbolts"))
    # the firebox door, glowing through its grate
    d = c + Y * (r + 0.005) + Z * -0.08
    parts.append(revolve("firedoor", d, Y, [(0.17, -0.02), (0.17, 0.03), (0.15, 0.05), (0.1, 0.05)], [M["iron"], M["ember"]], n=24, cap_mats=(None, 1)))
    for k in range(5):
        x = (k - 2) * 0.045
        parts.append(cyl("grate", d + V((x, 0.06, -0.1)), d + V((x, 0.06, 0.1)), 0.012, [M["iron"]], n=6))
    parts.append(torus("doorrim", d + Y * 0.04, Y, 0.165, 0.015, 0.018, [M["brass"]], n=24))
    parts.append(pipe("doorhandle", [d + V((0.12, 0.06, 0.04)), d + V((0.2, 0.11, 0.04)), d + V((0.25, 0.11, 0.04))], 0.014, [M["brass"]], n=6))
    # the smokestack, rising from the drum and flaring to a crown
    s = V((0.0, 0.86, 0.0))
    prof = [(0.16, 3.25), (0.16, 3.6), (0.155, 4.2), (0.18, 4.36), (0.22, 4.46), (0.36, 4.66), (0.43, 4.8), (0.45, 4.86),
            (0.41, 4.87), (0.3, 4.8), (0.13, 4.6)]
    parts.append(revolve("stack", s, Z, prof, [M["copper"], M["brass"], M["soot"], M["ember"]], n=24, power=2.4,
                         mat_rows=[0, 0, 0, 0, 0, 0, 1, 1, 2, 2], cap_mats=(None, 3)))
    for z in (3.55, 3.95, 4.3):
        parts.append(torus("stackband", s + Z * z, Z, 0.165, 0.025, 0.014, [M["brass"]], n=20, sides=4))
        parts.append(studs([(s + Z * z + V((math.cos(a), math.sin(a), 0)) * 0.18, V((math.cos(a), math.sin(a), 0))) for a in (TAU * k / 10 for k in range(10))],
                           0.012, M["brass"], "stackrivets"))
    parts.append(torus("crownrim", s + Z * 4.865, Z, 0.43, 0.012, 0.022, [M["brass"]], n=24, sides=4))
    # steam pipes from the drum to the bells
    for side in (1, -1):
        path = [V((side * 0.5, 1.14, 3.08)), V((side * 0.54, 1.12, 3.4)), V((side * 0.56, 0.98, 3.75)), V((side * 0.55, 0.74, 3.98)), V((side * 0.52, 0.54, 4.04))]
        parts.append(pipe("steampipe", path, 0.034, [M["copper"]], n=8))
        parts.append(torus("flange", path[0] + V((0, 0.0, 0.04)), path[1] - path[0], 0.045, 0.012, 0.02, [M["brass"]], n=12))
    return parts


EXHAUST = [(1, V((0.5, 0.5, 3.98))), (-1, V((-0.5, 0.5, 3.98)))]


def exhausts(M):
    """An exhaust bell each side of the head, as the original's two ports,
    flaring forward and up, their throats glowing."""
    parts = []
    for side, base in EXHAUST:
        axis = V((side * 0.25, -0.3, 1.0)).normalized()
        prof = [(0.1, 0.0), (0.1, 0.16), (0.12, 0.24), (0.17, 0.32), (0.22, 0.38), (0.235, 0.4), (0.21, 0.395), (0.15, 0.33), (0.08, 0.24)]
        parts.append(revolve("bell", base, axis, prof, [M["copper"], M["brass"], M["soot"], M["ember"]], n=16,
                             mat_rows=[0, 0, 0, 0, 1, 1, 2, 2], cap_mats=(None, 3)))
        parts.append(torus("bellband", base + axis * 0.13, axis, 0.105, 0.025, 0.014, [M["brass"]], n=16, sides=4))
        parts.append(torus("bellfoot", base + axis * 0.01, axis, 0.13, 0.02, 0.03, [M["iron"]], n=16, sides=4))
    return parts


def head(M):
    """The head: a riveted copper dome on a broad face, a T-slot visor and
    a frowning grille cut in, both glowing inside, a brass band round the
    dome, plate seams over it and a toothed boss each side."""
    parts = []
    zs = [3.04, 3.06, 3.12, 3.22, 3.34, 3.45, 3.56, 3.62, 3.72, 3.80, HEAD_DOME]
    zs += [HEAD_DOME + DOME_H * math.sin(math.radians(a)) for a in (6, 12, 18, 26, 36, 48, 60, 72, 82, 88)]
    rings = [uk.ring(V((HEAD_C.x, HEAD_C.y, z)), X, Y, max(0.012, head_r(z)), max(0.012, head_r(z)), 48, 2.0, start=-math.pi / 2) for z in zs]
    face = loft_rings("head", rings, [M["copper"]], cap0=True, cap1=True, smooth=40)

    def radial(p):
        return V((p.x - HEAD_C.x, p.y - HEAD_C.y, 0.0)).normalized()

    # the visor slot round the dome's front
    secs = []
    for i in range(25):
        a = math.radians(lerp(-58, 58, i / 24))
        u = V((math.sin(a), -math.cos(a), 0.0))
        c = V((HEAD_C.x, HEAD_C.y, 0.0))
        secs.append([c + u * 0.38 + Z * 3.945, c + u * 0.6 + Z * 3.945, c + u * 0.6 + Z * 4.01, c + u * 0.35 + Z * 4.01])
    pocket(face, rect_tube("visorcut", secs, [M["soot"]]), M["soot"], M["ember"], radial, 0.5)
    # the slit down the middle of the T
    secs = []
    for z in (3.62, 3.96):
        y = head_front_y(0.0, z)
        secs.append([V((-0.032, y - 0.15, z)), V((0.032, y - 0.15, z)), V((0.032, y + 0.05, z)), V((-0.032, y + 0.05, z))])
    pocket(face, rect_tube("slitcut", secs, [M["soot"]]), M["soot"], M["ember"], lambda p: -Y, 0.7)
    # the grille, a frown
    secs = []
    for i in range(17):
        x = lerp(-GRILLE_W, GRILLE_W, i / 16)
        zc = grille_z(x)
        y = head_front_y(x, zc)
        u = radial(V((x, y, 0)))
        secs.append([V((x, y, zc - 0.055)) - u * 0.15, V((x, y, zc - 0.055)) + u * 0.06, V((x, y, zc + 0.055)) + u * 0.06, V((x, y, zc + 0.055)) - u * 0.15])
    pocket(face, rect_tube("grillecut", secs, [M["soot"]]), M["soot"], M["ember"], radial, 0.5)
    parts.append(face)
    for i in range(11):
        x = lerp(-GRILLE_W + 0.04, GRILLE_W - 0.04, i / 10)
        zc = grille_z(x)
        y = head_front_y(x, zc) + 0.022
        parts.append(cyl("grillebar", V((x, y, zc - 0.065)), V((x, y, zc + 0.065)), 0.012, [M["iron"]], n=6))
    # a heavy brow over the visor, and a ridge over the dome, riveted
    secs = []
    for i in range(21):
        a = math.radians(lerp(-68, 68, i / 20)) - math.pi / 2
        u = V((math.cos(a), math.sin(a), 0.0))
        p = V((HEAD_C.x, HEAD_C.y, 0.0)) + u * head_r(4.04) + Z * 4.04
        secs.append(p)
    rings = []
    for k, p in enumerate(secs):
        tan = (secs[min(k + 1, 20)] - secs[max(k - 1, 0)]).normalized()
        u = V((p.x - HEAD_C.x, p.y - HEAD_C.y, 0.0)).normalized()
        up = (Z * 0.9 + u * 0.4).normalized()
        out = tan.cross(up).normalized() if tan.cross(up).dot(u) > 0 else -tan.cross(up).normalized()
        rings.append(uk.ring(p + u * 0.012, out, up, 0.03, 0.022, 6, 3.0))
    parts.append(loft_rings("brow", rings, [M["iron"]], True, True, 40))
    pts, ridge = [], []
    for e in range(-10, 191, 10):
        el = math.radians(e)
        p = V((HEAD_C.x, HEAD_C.y - math.cos(el) * 0.455, HEAD_DOME + DOME_H * max(0.0, math.sin(el))))
        ridge.append(uk.ring(p + V((0.0, -math.cos(el), math.sin(el))).normalized() * 0.005, X, V((0.0, -math.cos(el) * 0.4, math.sin(el))).normalized(), 0.035, 0.02, 6, 3.0))
        if 50 <= e <= 150 and e % 20 == 10:
            pts.append((p + V((0.0, -math.cos(el), math.sin(el))).normalized() * 0.022, V((0.0, -math.cos(el) * 0.4, math.sin(el))).normalized()))
    parts.append(loft_rings("crest", ridge[5:-4], [M["iron"]], True, True, 40))
    parts.append(studs(pts, 0.014, M["brass"], "crestrivets"))
    # brows over the slot, a raised lip
    secs = [uk.ring(V((HEAD_C.x, HEAD_C.y, z)), X, Y, head_r(z) + d, head_r(z) + d, 32, 2.0, start=-math.pi / 2) for z, d in ((3.84, 0.0), (3.845, 0.02), (3.875, 0.02), (3.88, 0.0))]
    parts.append(loft_rings("domeband", secs + [secs[0]], [M["brass"]], smooth=40))
    parts.append(studs([(V((HEAD_C.x + math.cos(a) * 0.478, HEAD_C.y + math.sin(a) * 0.478, 3.86)), V((math.cos(a), math.sin(a), 0)))
                        for a in (TAU * (k + 0.5) / 24 for k in range(24))], 0.013, M["brass"], "bandrivets"))
    # the collar where the head sits in the chest
    parts.append(torus("headcollar", V((HEAD_C.x, HEAD_C.y, 3.1)), Z, 0.4, 0.03, 0.04, [M["iron"]], n=24, sides=4))
    # plate seams over the dome, riveted
    for a in (-135, -45, 45, 135):
        a = math.radians(a) - math.pi / 2
        u = V((math.cos(a), math.sin(a), 0.0))
        pts = []
        for e in range(4, 80, 8):
            el = math.radians(e)
            z = HEAD_DOME + DOME_H * math.sin(el)
            r = head_r(z) + 0.004
            pts.append((V((HEAD_C.x, HEAD_C.y, z)) + u * r, (u * math.cos(el) + Z * math.sin(el)).normalized()))
        parts.append(studs(pts, 0.013, M["copper"], "domerivets"))
    # a riveted seam along the jaw
    pts = []
    for a in range(-80, 81, 16):
        t = math.radians(a) - math.pi / 2
        u = V((math.cos(t), math.sin(t), 0.0))
        pts.append((V((HEAD_C.x, HEAD_C.y, 3.24)) + u * (head_r(3.24) + 0.004), u))
    parts.append(studs(pts, 0.014, M["brass"], "jawrivets"))
    # toothed bosses where ears would be
    for s in (1, -1):
        c = V((HEAD_C.x + s * 0.47, HEAD_C.y + 0.02, 3.58))
        parts.append(gear("earboss", c, X * s, 0.1, 0.125, 10, 0.05, [M["iron"]]))
        parts.append(revolve("earhub", c + X * s * 0.02, X * s, [(0.065, 0.0), (0.065, 0.03), (0.04, 0.05), (0.01, 0.055)], [M["brass"]], n=12))
    return parts


def beard(M):
    """Brass rods fanned over the chest below the chin like a beard, from a
    toothed hub, with spring curls at the ends of some."""
    T = TORSO
    parts = []
    hub = V((0.0, 0.0, 2.97))

    def on_chest(x, z, d):
        rx, yf, _yb = T.sec(z)
        q = max(0.0, 1.0 - abs(x / (rx + d)) ** T.p)
        return V((x, T.cy - (yf + d) * q ** (1.0 / T.p), z))

    hc = on_chest(0.0, hub.z, 0.05)
    parts.append(gear("beardhub", hc, -Y, 0.085, 0.11, 12, 0.05, [M["brass"]]))
    parts.append(nuts([(hc - Y * 0.025, -Y)], 0.04, 0.03, M["iron"], "hubnut"))
    rods = []
    for k, (ang, length) in enumerate(((-6, 0.62), (-15, 0.6), (-24, 0.56), (-33, 0.5), (-42, 0.44), (-51, 0.36))):
        a = math.radians(ang)
        for s in (1, -1):
            pts = []
            for i in range(4):
                d = lerp(0.1, length, i / 3)
                x, z = s * math.cos(a) * d, hub.z + math.sin(a) * d
                pts.append(on_chest(x, z, 0.03))
            rods.append(pipe("rod", pts, 0.019, [M["brass"]], n=6))
            end = pts[-1]
            if k % 2 == 1:
                n_ = (end - V((0, T.cy, end.z))).normalized()
                side = V((s * math.cos(a), 0, math.sin(a)))
                up = n_.cross(side).normalized()
                parts.append(pipe("curl", spiral(end + side * 0.045, -side, up * s, 0.045, 0.012, 1.6, 10), 0.011, [M["brass"]], n=4))
            else:
                parts.append(nuts([(end, (end - V((0, T.cy, end.z))).normalized())], 0.03, 0.02, M["brass"], "rodend"))
    parts += rods
    return parts


def hip(rig, M):
    """The crankcase under the chest, riveted, with a toothed boss in front."""
    parts = []
    P = Casing(0.0, 0.24, [(2.16, 0.6, 0.38, 0.44), (2.22, 0.68, 0.44, 0.5), (2.4, 0.72, 0.46, 0.52), (2.46, 0.7, 0.44, 0.5)], 3.0)
    parts.append(P.body("crankcase", [2.16, 2.22, 2.31, 2.4, 2.46], [M["iron"]], n=32))
    parts.append(studs([(P.at(TAU * k / 26, 2.3, 0.012), P.normal(TAU * k / 26, 2.3)) for k in range(26)], 0.017, M["brass"], "caserivets"))
    c = P.at(FRONT, 2.3, 0.02)
    parts.append(gear("hipgear", c, -Y, 0.12, 0.15, 14, 0.05, [M["brass"]]))
    parts.append(nuts([(c - Y * 0.025, -Y)], 0.05, 0.03, M["iron"], "hipnut"))
    return parts


# ---------------------------------------------------------------- legs

def leg(rig, M):
    """The left leg: a cast casing hinged at a toothed drum, enamelled
    panels down the front and the outer side, a brass knee boss and a ram
    behind."""
    a = rig.pivot("lupleg")
    cx = 0.35
    L = Casing(cx, 0.3, [(0.74, 0.23, 0.38, 0.4), (0.86, 0.25, 0.42, 0.45), (1.3, 0.28, 0.47, 0.5), (1.9, 0.29, 0.5, 0.53), (2.12, 0.28, 0.48, 0.5), (2.2, 0.25, 0.42, 0.46)], 3.2)
    parts = [L.body("legcase_L", [0.74, 0.8, 0.86, 1.3, 1.9, 2.05, 2.12, 2.2], [M["iron"]], n=20)]
    # the hip drum, which turns as the leg swings
    hc = V((cx, a.y, a.z))
    parts.append(revolve("hipdrum_L", hc - X * 0.25, X, [(0.17, 0.0), (0.2, 0.0), (0.2, 0.5), (0.17, 0.5)], [M["iron"]], n=16))
    parts.append(gear("hipteeth_L", hc + X * 0.25, X, 0.17, 0.215, 10, 0.035, [M["brass"]]))
    parts.append(nuts([(hc + X * 0.27, X)], 0.06, 0.03, M["brass"], "hipbolt_L"))
    # enamel down the outer side
    parts += L.framed("legpanel_L", -0.62, 0.62, 0.98, 1.84, M, nu=6, nv=6, every=0.12)
    # enamel down the front and a brass knee boss
    parts += L.framed("legfront_L", FRONT - 0.6, FRONT + 0.6, 0.98, 1.84, M, nu=6, nv=6, every=0.14)
    k = L.at(FRONT, 1.52, 0.05)
    parts.append(revolve("knee_L", k, -Y, [(0.13, -0.02), (0.13, 0.02), (0.1, 0.05), (0.04, 0.07)], [M["brass"]], n=16))
    # a ram down the back
    b = L.at(BACK, 1.0, 0.06)
    parts.append(cyl("ram_L", b + Z * 0.95, b + Z * 0.3, 0.07, [M["iron"]], n=12))
    parts.append(cyl("ramrod_L", b + Z * 0.3, b - Z * 0.12, 0.035, [M["steel"]], n=10))
    parts.append(torus("ramband_L", b + Z * 0.88, Z, 0.072, 0.02, 0.012, [M["brass"]], n=12, sides=4))
    parts.append(loop_rim("legrim_L", L.ring(0.8, 20, 0.01), 0.022, [M["brass"]], sides=4))
    return parts


def foot(rig, M):
    """The left foot: an iron boot on a thick sole, a copper instep plate,
    hinged at an ankle drum."""
    a = rig.pivot("lfoot")
    cx = 0.34
    parts = []
    rows = [(0.9, 0.22, 0.4), (0.82, 0.28, 0.52), (0.55, 0.31, 0.6), (0.25, 0.32, 0.58), (0.02, 0.32, 0.48), (-0.1, 0.31, 0.4)]
    rings = []
    for y, w, top in rows:
        r = uk.ring(V((cx, y, top / 2.0)), X, Z, w, top / 2.0, 20, power=3.6)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    parts.append(loft_rings("boot_L", rings, [M["iron"]], cap0=True, cap1=True, smooth=40))
    sole = [V((cx + math.copysign(abs(math.cos(t)) ** 0.5, math.cos(t)) * 0.31, 0.38 + math.copysign(abs(math.sin(t)) ** 0.5, math.sin(t)) * 0.52, 0.0)) for t in (TAU * k / 24 for k in range(24))]
    bm = bmesh.new()
    lo = [bm.verts.new(p - Z * 0.01) for p in sole]
    hi = [bm.verts.new(p + Z * 0.07) for p in sole]
    bm.faces.new(lo[::-1])
    bm.faces.new(hi)
    for k in range(24):
        bm.faces.new((lo[k], lo[(k + 1) % 24], hi[(k + 1) % 24], hi[k]))
    parts.append(obj("sole_L", bm, [M["iron"]], 40))
    # the instep plate
    grid = []
    for y in (0.66, 0.5, 0.34, 0.18, 0.02):
        w, top = interp([(r_[0], r_[1], r_[2]) for r_ in rows[::-1]], y)
        row = []
        for f in (-0.8, -0.4, 0.0, 0.4, 0.8):
            x = f * w
            z = top / 2.0 + (top / 2.0) * (1.0 - abs(f) ** 3.6) ** (1 / 3.6) + 0.012
            row.append(V((cx + x, y, z)))
        grid.append(row)
    parts.append(shell("instep_L", grid, [M["copper"]], 0.025, centre=(cx, 0.3, 0.2)))
    parts.append(studs([(p + Z * 0.02, Z) for p in (grid[0][0], grid[0][-1], grid[2][0], grid[2][-1], grid[-1][0], grid[-1][-1])], 0.018, M["brass"], "insteprivets_L"))
    # the ankle drum
    parts.append(revolve("ankle_L", V((0.1, a.y, a.z)), X, [(0.15, 0.0), (0.18, 0.0), (0.18, 0.48), (0.15, 0.48)], [M["iron"]], n=16))
    parts.append(torus("anklering_L", V((0.58, a.y, a.z)), X, 0.17, 0.02, 0.025, [M["brass"]], n=16, sides=4))
    parts.append(nuts([(V((0.6, a.y, a.z)), X), (V((0.08, a.y, a.z)), -X)], 0.055, 0.03, M["brass"], "anklebolt_L"))
    return parts


def toe(rig, M):
    """The left toe cap, three iron toes under a brass edge."""
    cx = 0.33
    parts = []
    rows = [(0.02, 0.3, 0.36), (-0.18, 0.3, 0.32), (-0.38, 0.28, 0.24), (-0.5, 0.24, 0.15), (-0.56, 0.17, 0.08)]
    rings = []
    for y, w, top in rows:
        r = uk.ring(V((cx, y, top / 2.0)), X, Z, w, top / 2.0, 20, power=3.0)
        rings.append([V((p.x, p.y, max(0.0, p.z))) for p in r])
    parts.append(loft_rings("toecap_L", rings, [M["iron"]], cap0=True, cap1=True, smooth=40))
    for x in (-0.09, 0.09):
        pts = []
        for y, w, top in rows[:-1]:
            pts.append(V((cx + x, y, top * (1.0 - abs(x / w) ** 3.0) ** (1 / 3.0) * 0.5 + top * 0.5 + 0.005)))
        parts.append(pipe("toeridge_L", pts, 0.02, [M["copper"]], n=6))
    edge = [V((cx + math.cos(t) * 0.2, -0.38 + math.sin(t) * 0.17, 0.035)) for t in (math.radians(a) for a in range(200, 341, 20))]
    parts.append(pipe("toeedge_L", edge, 0.03, [M["brass"]], n=6))
    parts.append(studs([(V((cx + x, -0.2, 0.33)), Z) for x in (-0.18, 0.0, 0.18)], 0.018, M["brass"], "toerivets_L"))
    return parts


# ---------------------------------------------------------------- arms

def shoulder(rig, M):
    """The left upper arm: a coil bank for a shoulder, copper windings
    between brass rings on an iron core, turning with the arm, then an iron
    upper arm under a copper plate, ending in the clevis of the elbow."""
    s = rig.pivot("luparm")
    e = rig.pivot("larm")
    parts = []
    c = V((0.88, s.y, s.z + 0.02))
    prof, rows = [(0.29, 0.0)], []
    h = 0.0
    for k in range(4):
        prof += [(0.385, h), (0.385, h + 0.03), (0.36, h + 0.03)]
        rows += [1, 1, 1]
        h += 0.03
        if k < 3:
            prof += [(0.36, h + 0.157)]
            rows += [2]
            h += 0.157
    prof += [(0.33, h + 0.04), (0.24, h + 0.08), (0.08, h + 0.1)]
    rows += [1, 1, 1]
    parts.append(revolve("coilbank_L", c, X, prof, [M["iron"], M["brass"], M["coil"]], n=16, mat_rows=rows, cap_mats=(0, 1)))
    tip = c + X * (h + 0.1)
    parts.append(nuts([(tip, X)], 0.06, 0.035, M["iron"], "coilhub_L"))
    parts.append(nuts([(c + X * (h + 0.06) + (Y * math.cos(a) + Z * math.sin(a)) * 0.2, (X + (Y * math.cos(a) + Z * math.sin(a)) * 0.5).normalized())
                       for a in (TAU * k / 6 for k in range(6))], 0.022, 0.016, M["iron"], "coilbolts_L"))
    # the upper arm, from inside the drum down to the clevis
    U = Casing(1.12, s.y, [(2.92, 0.17, 0.22, 0.22), (3.1, 0.19, 0.24, 0.24), (3.4, 0.18, 0.22, 0.22)], 3.0)
    parts.append(U.body("upperarm_L", [2.92, 3.0, 3.1, 3.25, 3.4], [M["iron"]], n=24))
    parts.append(U.panel("armplate_L", -0.95, 0.95, 2.98, 3.32, M["copper"], d=0.018, thick=0.025, nu=6, nv=4))
    parts.append(studs(U.rivet_line(-0.85, 0.85, 3.27, 3.27, 0.045, 5) + U.rivet_line(-0.85, 0.85, 3.03, 3.03, 0.045, 5), 0.015, M["brass"], "armrivets_L"))
    # the clevis: a round cheek each side of the elbow, a bolt through
    for x, out in ((0.905, -1), (1.31, 1)):
        cc = V((x, e.y, e.z))
        parts.append(revolve("cheek_L", cc - X * 0.025, X, [(0.29, 0.0), (0.305, 0.012), (0.305, 0.038), (0.29, 0.05)], [M["brass"]], n=14))
        if out > 0:
            parts.append(nuts([(cc + X * out * 0.03 + (Y * math.cos(a) + Z * math.sin(a)) * 0.18, X * out) for a in (TAU * (k + 0.5) / 4 for k in range(4))],
                              0.02, 0.014, M["iron"], "cheekbolts_L"))
    parts.append(cyl("elbowpin_L", V((0.9, e.y, e.z)), V((1.36, e.y, e.z)), 0.06, [M["steel"]], n=12))
    parts.append(nuts([(V((1.36, e.y, e.z)), X)], 0.075, 0.035, M["brass"], "elbownut_L"))
    return parts


def forearm(rig, M):
    """The left forearm: a toothed knuckle turning between the cheeks, a
    copper casing, two rams down the front and a brass wrist ring."""
    e = rig.pivot("larm")
    parts = []
    parts.append(gear("knuckle_L", V((1.11, e.y, e.z)), X, 0.19, 0.225, 12, 0.3, [M["iron"]]))
    F = Casing(1.08, e.y, [(2.12, 0.17, 0.22, 0.22), (2.4, 0.19, 0.24, 0.24), (2.72, 0.16, 0.2, 0.2)], 3.0)
    parts.append(F.body("forearm_L", [2.12, 2.2, 2.3, 2.4, 2.55, 2.72], [M["copper"]], n=24))
    parts.append(loop_rim("wrist_L", F.ring(2.15, 20, 0.012), 0.024, [M["brass"]], sides=4))
    parts.append(loop_rim("forearmband_L", F.ring(2.5, 20, 0.008), 0.018, [M["iron"]], sides=4))
    for x in (-0.07, 0.07):
        b = F.at(FRONT, 2.4, 0.04) + X * x
        parts.append(cyl("forearmram_L", V((b.x, b.y, 2.66)), V((b.x, b.y, 2.38)), 0.032, [M["iron"]], n=10))
        parts.append(cyl("forearmrod_L", V((b.x, b.y, 2.38)), V((b.x, b.y, 2.18)), 0.016, [M["steel"]], n=8))
    parts.append(studs(F.rivet_line(0.0, 0.0, 2.24, 2.62, 0.01, 4) + F.rivet_line(math.pi, math.pi, 2.24, 2.62, 0.01, 4), 0.016, M["brass"], "forearmrivets_L"))
    return parts


GRIP_L = V((1.06, 0.13, 1.5))


def fist(rig, M, grip=GRIP_L):
    """The left fist round a bar along Y at grip: an iron palm under a
    copper back plate, four jointed fingers curled under the bar on a
    hinge rod, and a thumb over it at the front."""
    parts = []
    g = V(grip)
    P = Casing(g.x + 0.01, g.y, [(1.62, 0.17, 0.25, 0.25), (1.7, 0.19, 0.26, 0.26), (1.9, 0.18, 0.24, 0.24), (2.08, 0.15, 0.2, 0.2)], 3.5)
    parts.append(P.body("palm_L", [1.62, 1.66, 1.7, 1.85, 2.0, 2.08], [M["iron"]], n=16))
    parts.append(P.panel("backplate_L", -0.85, 0.85, 1.7, 2.02, M["copper"], d=0.016, thick=0.022, nu=6, nv=4))
    parts.append(studs(P.rivet_line(-0.75, -0.75, 1.72, 2.0, 0.04, 2) + P.rivet_line(0.75, 0.75, 1.72, 2.0, 0.04, 2), 0.016, M["brass"], "palmrivets_L"))
    # four fingers, each three jointed segments round the bar
    for y in (-0.19, -0.065, 0.06, 0.185):
        for a0, a1 in ((70, -40), (-50, -180)):
            pts = [V((g.x + math.cos(math.radians(a)) * 0.13, g.y + y, g.z + math.sin(math.radians(a)) * 0.13)) for a in (a0, (a0 + a1) / 2, a1)]
            rings = tube_rings(pts, [(0.05, 0.055)] * 3, 6, power=2.6)
            parts.append(loft_rings("finger_L", rings, [M["iron"]], cap0=True, cap1=True, smooth=45))
    for a in (-45,):
        p = V((g.x + math.cos(math.radians(a)) * 0.13, g.y, g.z + math.sin(math.radians(a)) * 0.13))
        parts.append(cyl("knucklerod_L", p - Y * 0.26, p + Y * 0.26, 0.026, [M["brass"]], n=8))
    # the thumb, from the palm's inner side over the bar
    pts = [V((g.x - 0.16, g.y - 0.2, 1.76)), V((g.x - 0.15, g.y - 0.26, 1.64)), V((g.x - 0.05, g.y - 0.27, 1.6))]
    parts.append(loft_rings("thumb_L", tube_rings(pts, [(0.055, 0.055), (0.05, 0.05), (0.045, 0.045)], 8), [M["iron"]], True, True, 45))
    parts.append(ball("thumbjoint_L", pts[1], 0.04, [M["brass"]], n=8, rings=5))
    return parts


SHIELD_N = V((0.8, 0.0, -0.6))
SHIELD_C = V((1.13, 0.13, 1.47)) + SHIELD_N * 0.22


def shield(rig, M):
    """A flywheel for a shield, held at the side as the original holds
    its own: a toothed iron wheel, a domed copper face over six spokes, a
    hub bearing Creon's key (key_emblem), and a handle the fist grips
    behind."""
    n, c = SHIELD_N, SHIELD_C
    parts = []
    up = V((0.6, 0.0, 0.8))
    parts.append(gear("shieldwheel", c, n, 0.86, 0.94, 26, 0.1, [M["iron"]]))
    parts.append(torus("shieldrim", c + n * 0.055, n, 0.84, 0.02, 0.035, [M["brass"]], n=40, sides=4))

    def dome_h(r):
        return 0.09 * (1.0 - (r / 0.84) ** 2)

    prof = [(0.84, 0.03)] + [(r, 0.05 + dome_h(r)) for r in (0.84, 0.74, 0.6, 0.45, 0.3, 0.15, 0.02)]
    parts.append(revolve("shieldface", c, n, prof, [M["copper"]], n=36))
    for k in range(6):
        a = TAU * k / 6
        u = Y * math.cos(a) + up * math.sin(a)
        w = n.cross(u).normalized()
        rings = [uk.ring(c + u * r + n * (0.05 + dome_h(r) + 0.012), w, n, 0.045, 0.025, 6, 3.0) for r in (0.42, 0.6, 0.8)]
        parts.append(loft_rings("spoke", rings, [M["iron"]], True, True, 40))
        parts.append(studs([(c + u * r + n * (0.05 + dome_h(r) + 0.04), n) for r in (0.56, 0.72)], 0.016, M["brass"], "spokerivets"))
    parts.append(studs([(c + (Y * math.cos(a) + up * math.sin(a)) * 0.79 + n * 0.065, n) for a in (TAU * (k + 0.5) / 24 for k in range(24))], 0.015, M["iron"], "facerivets"))
    parts += key_emblem(c + n * 0.1, n, Y, up, M)
    # the handle behind, on two brackets
    g = GRIP_L
    back = c - n * 0.05
    parts.append(cyl("handle", g - Y * 0.24, g + Y * 0.5, 0.05, [M["brass"]], n=10))
    for y in (-0.2, 0.46):
        p = g + Y * y
        depth = (p - back).dot(n)
        parts.append(cyl("bracketarm", p, p - n * depth, 0.035, [M["iron"]], n=8))
    return parts


def relief(name, outline, base, u, v, n, h0, h1, mat, bev=0.006):
    """A raised plate: the 2D outline [(u, v)] about base, from h0 to h1
    along n, its edges rounded to catch the light."""
    bm = bmesh.new()
    rings = [[base + u * x + v * y + n * h for x, y in outline] for h in (h0, h1)]
    uk.loft(bm, rings, cap0=True, cap1=True)
    ob = uk.mesh(name, bm, [mat])
    uk.bevel(ob, bev, 1, angle=30)
    return uk.shade(ob, 30)


EMBLEM_R = 0.46


def key_emblem(c, n, u, v, M):
    """Creon's sigil as the hub of the shield, our own drawing of it: an
    upright key cast in brass, its bow a small cog and its bit stepped
    toward u, raised from an iron field inside a riveted brass rim. c is
    the middle of the field's back, n its outward normal, v its up."""
    parts = []
    top = 0.075
    parts.append(revolve("emblemfield", c, n, [(EMBLEM_R - 0.03, 0.0), (EMBLEM_R - 0.03, top), (0.01, top)], [M["iron"]], n=32))
    parts.append(torus("emblemrim", c + n * top, n, EMBLEM_R - 0.02, 0.022, 0.03, [M["brass"]], n=28, sides=4))
    parts.append(studs([(c + n * (top + 0.02) + (u * math.cos(a) + v * math.sin(a)) * (EMBLEM_R - 0.02), n)
                        for a in (TAU * (k + 0.5) / 12 for k in range(12))], 0.015, M["brass"], "emblemrivets"))
    f, k = c + n * top, 1.15
    up = 0.035
    # the bow, a cog with a dark eye
    bow = f + v * (-0.17 * k)
    parts.append(gear("keybow", bow + n * (up * 0.5), n, 0.1 * k, 0.128 * k, 8, up, [M["brass"]], smooth=30, start=math.pi / 8))
    parts.append(revolve("keyeye", bow + n * (up - 0.004), n, [(0.045 * k, 0.0), (0.04 * k, 0.012)], [M["soot"]], n=12))
    # the shaft, its collar and the bit with two wards
    parts.append(relief("keyshaft", [(-0.026, -0.08), (0.026, -0.08), (0.026, 0.3), (-0.026, 0.3)], f, u * k, v * k, n, 0.0, up, M["brass"]))
    parts.append(relief("keycollar", [(-0.05, -0.065), (0.05, -0.065), (0.05, -0.025), (-0.05, -0.025)], f, u * k, v * k, n, 0.0, up * 1.25, M["brass"]))
    bit = [(0.02, 0.15), (0.15, 0.15), (0.15, 0.19), (0.11, 0.19), (0.11, 0.225), (0.15, 0.225), (0.15, 0.3), (0.02, 0.3)]
    parts.append(relief("keybit", bit, f, u * k, v * k, n, 0.0, up, M["brass"]))
    parts.append(relief("keytip", [(-0.04, 0.3), (0.04, 0.3), (0.03, 0.33), (-0.03, 0.33)], f, u * k, v * k, n, 0.0, up, M["brass"]))
    return parts


# ---------------------------------------------------------------- the blade

GRIP_R = V((-1.06, 0.13, 1.5))
BLADE_X, SPLIT_Z = -1.07, 1.52


def hilt(rig, M):
    """The sword's grip in the right fist, a toothed pommel and a guard of
    two fuel tanks either side of the blade's collar."""
    parts = []
    g = GRIP_R
    parts.append(cyl("grip", g - Y * 0.43, g + Y * 0.56, 0.052, [M["iron"]], n=10))
    for y in (-0.33, 0.33):
        parts.append(torus("gripring", g + Y * y, Y, 0.055, 0.012, 0.01, [M["brass"]], n=10, sides=4))
    parts.append(gear("pommel", g + Y * 0.62, Y, 0.075, 0.095, 10, 0.09, [M["brass"]]))
    parts.append(revolve("pommelcap", g + Y * 0.665, Y, [(0.07, 0.0), (0.05, 0.04), (0.015, 0.055)], [M["brass"]], n=12))
    yg = -0.36
    for z0, z1 in ((1.0, 1.36), (1.64, 2.0)):
        a, b = V((g.x, yg, z0)), V((g.x, yg, z1))
        parts.append(revolve("tank", a, Z, [(0.04, -0.02), (0.08, 0.0), (0.1, 0.04), (0.1, z1 - z0 - 0.04), (0.08, z1 - z0), (0.04, z1 - z0 + 0.02)],
                             [M["iron"]], n=12))
        for z in (z0 + 0.06, z1 - 0.06):
            parts.append(torus("tankband", V((g.x, yg, z)), Z, 0.102, 0.016, 0.01, [M["brass"]], n=12, sides=4))
        parts.append(nuts([(V((g.x, yg, z0 - 0.02)), -Z), (V((g.x, yg, z1 + 0.02)), Z)], 0.035, 0.03, M["brass"], "tankcap"))
    parts.append(rbox("guardspine", (g.x, yg + 0.06, 1.5), (0.05, 0.035, 0.5), [M["iron"]], bev=0.012, segs=1))
    parts.append(revolve("collarplate", V((g.x, yg + 0.06, 1.5)), -Y, [(0.19, 0.0), (0.19, 0.05), (0.16, 0.07)], [M["iron"]], n=20))
    return parts


def spindle_parts(rig, M):
    """The toothed collar the blade rolls in."""
    p = rig.pivot("spindle")
    parts = [gear("collar", V((p.x, p.y - 0.11, p.z)), Y, 0.15, 0.18, 14, 0.07, [M["brass"]])]
    parts.append(revolve("collarhub", V((p.x, p.y - 0.15, p.z)), -Y, [(0.14, 0.0), (0.13, 0.03), (0.1, 0.05)], [M["iron"]], n=16))
    return parts


def _jaw_section(y, w, h, top):
    """Half a blade's section at y, flat on the split and rising to the
    edge, above the split for top 1 and below it for -1."""
    s = top
    pts = [(-w * 0.7, 0.0), (w * 0.7, 0.0), (w, 0.06 * h), (w * 0.9, 0.4 * h), (w * 0.3, 0.88 * h), (0.0, h), (-w * 0.3, 0.88 * h), (-w * 0.9, 0.4 * h), (-w, 0.06 * h)]
    out = [V((BLADE_X + x, y, SPLIT_Z + s * z)) for x, z in pts]
    return out if s > 0 else out[::-1]


def jaws(rig, M):
    """The blade in its four pieces: each half of it splits from the other
    along the flat and hinges again at the middle, so the script opens it
    into jaws. Steel, sooted where they part, brass knuckles at the hinges."""
    out = {}
    for top, (inner, outer) in ((1, ("swordtop_1", "swordtop_2")), (-1, ("swordbot_1", "swordbot_2"))):
        rows = [(-0.5, 0.085, 0.22), (-0.6, 0.085, 0.22), (-1.2, 0.08, 0.22), (-1.53, 0.08, 0.22)]
        rings = [_jaw_section(y, w, h, top) for y, w, h in rows]
        a = loft_rings(inner, rings, [M["steel"], M["soot"]], cap0=True, cap1=True, smooth=30)
        rows = [(-1.57, 0.08, 0.22), (-2.0, 0.078, 0.215), (-2.6, 0.07, 0.19), (-3.0, 0.055, 0.14), (-3.25, 0.03, 0.07), (-3.38, 0.006, 0.012)]
        rings = [_jaw_section(y, w, h, top) for y, w, h in rows]
        b = loft_rings(outer, rings, [M["steel"], M["soot"]], cap0=True, cap1=True, smooth=30)
        for ob in (a, b):
            for p in ob.data.polygons:
                if abs(p.normal.z) > 0.95 and abs(p.center.z - SPLIT_Z) < 0.003:
                    p.material_index = 1
        hz = SPLIT_Z + top * 0.21
        knuckle = cyl(outer + "knuckle", V((BLADE_X - 0.1, -1.55, hz)), V((BLADE_X + 0.1, -1.55, hz)), 0.045, [M["brass"]], n=12)
        pins = nuts([(V((BLADE_X + s * 0.1, -1.55, hz)), X * s) for s in (1, -1)], 0.03, 0.02, M["iron"], outer + "pins")
        bolts = nuts([(V((BLADE_X + s * 0.078, y, SPLIT_Z + top * 0.08)), X * s) for s in (1, -1) for y in (-0.75, -1.05, -1.35)], 0.02, 0.014, M["brass"], inner + "bolts")
        out[inner] = [a, bolts]
        out[outer] = [b, knuckle, pins]
    return out
