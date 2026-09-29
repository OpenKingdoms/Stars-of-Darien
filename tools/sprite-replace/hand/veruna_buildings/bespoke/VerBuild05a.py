"""VerBuild05a, the burnt town house, built on its own.

The house, the wing, the front breach and the yard walls are the family's
pieces as the judges passed them. The west lean-to is shaped here against
the drawing: one smooth sheet of straw falling from the outer wall over
its posts, coloured per vertex (COLOR_0), torn once near the house. The
east shed is a burnt remnant on the gable: charred frame, sooty torn
thatch and one lit strip of straw along the wall.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit
import models_compound as mc
from kit import model

MID = (156, 132, 74)
PALE = (188, 161, 104)
SOOT = (40, 32, 19)
FRINGE = (150, 126, 80)
T_SHEET = 0.2
SUN = Vector((0.32, -0.557, 0.766)).normalized()

# the outer (high) edge read off the drawing: (col, row, z); the tip turns the corner
OUTER = [(9, 125, 1.7), (8, 106, 1.7), (7, 91, 1.7), (6, 81, 1.66), (12, 73, 1.7), (20, 64, 1.72),
         (30, 57, 1.74), (39, 50, 1.75), (45, 44, 1.75), (51, 37, 1.75)]
TIP = 3
# the inner (low) eave in plan: the south end, the corner, the house wall
INNER = [(-3.65, -3.75), (-3.7, -0.5), (-2.45, 0.42)]


def sstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def mixc(a, b, w):
    return tuple(p + (q - p) * w for p, q in zip(a, b))


class Line:
    """A polyline measured by its length in plan."""

    def __init__(self, pts):
        self.p = [Vector(p) for p in pts]
        self.s = [0.0]
        for a, b in zip(self.p, self.p[1:]):
            self.s.append(self.s[-1] + (b - a).xy.length)
        self.L = self.s[-1]

    def at(self, s):
        s = max(0.0, min(self.L, s))
        for k in range(len(self.p) - 1):
            if s <= self.s[k + 1] or k == len(self.p) - 2:
                f = (s - self.s[k]) / max(1e-9, self.s[k + 1] - self.s[k])
                return self.p[k].lerp(self.p[k + 1], f)


class LeanTo:
    def __init__(self, m):
        self.m = m
        self.rng = random.Random(m.name + ":leanto")
        self.out = Line([(( c - m.hx) / 16.0, (m.hy - r - 8.0 * z) / 16.0, z) for c, r, z in OUTER])
        I = [Vector(p) for p in INNER]
        self.I = I
        self.LS, self.LD = (I[1] - I[0]).length, (I[2] - I[1]).length
        # supports under the eave (inner length t, top of the sheet there); the
        # second post has snapped and the span over it slumps
        self.snap = 1.35
        self.posts = [0.12, 2.45, self.LS, self.LS + 0.75]
        self.sup = [(0.12, 0.95), (2.45, 1.12), (self.LS, 1.12), (self.LS + 0.75, 1.13), (self.LS + self.LD, 1.2)]
        self.nz = m.noise("lt_z", 0.7)
        self.nt = m.noise("lt_tone", 0.9)
        self.nf = m.noise("lt_fold", 0.5)
        self.ns = m.noise("lt_soot", 0.35)

    def inner(self, s):
        """The eave point in plan and its length t for outer length s."""
        sT = self.out.s[TIP]
        I = self.I
        if s <= sT:
            f = s / sT
            return I[0].lerp(I[1], f), f * self.LS
        f = (s - sT) / (self.out.L - sT)
        return I[1].lerp(I[2], f), self.LS + f * self.LD

    def eave_z(self, t):
        S = self.sup
        if t <= S[0][0]:
            return S[0][1]
        for (ta, za), (tb, zb) in zip(S, S[1:]):
            if t <= tb:
                f = (t - ta) / (tb - ta)
                sag = 0.1 * (tb - ta) * math.sin(math.pi * f) ** 1.2
                slump = 0.2 * math.exp(-((t - self.snap) / 0.55) ** 2)
                return za + (zb - za) * f - sag - slump
        return S[-1][1]

    def tear(self, s, v):
        """Negative inside the ragged tear near the house, in cells."""
        ds, dv = (s - self.s_t) / 0.62, (v - self.v_t) / 0.42
        th = math.atan2(dv, ds)
        k = 1 + 0.3 * (0.55 * math.sin(3 * th + 1.1) + 0.3 * math.sin(5 * th + 2.3) + 0.15 * math.sin(8 * th + 0.4))
        return (math.hypot(ds, dv) / k - 1.0) * 0.5

    def build(self):
        m, rng = self.m, self.rng
        out = self.out
        du = 0.065
        n = int(out.L / du)
        nv = 5
        cols = []
        for i in range(n + 1):
            s = out.L * i / n
            o = out.at(s)
            q, t = self.inner(s)
            d = (q - o.xy)
            W = d.length
            d = d / W
            tip = math.exp(-((s - out.s[TIP]) / 0.3) ** 2)
            # the outer overhang frays, longest at the corner tip; the eave's
            # straw hangs in ragged strands
            ov = 0.14 + 0.4 * tip + 0.06 * rng.random()
            fr = 0.05 + 0.5 * rng.random() ** 1.7
            zi = self.eave_z(t)
            belly = 0.05 + 0.06 * W
            corr = rng.uniform(-1, 1) * 0.012
            streak = rng.uniform(-1, 1)
            pts = []
            # rows: one over the outer edge, nv + 1 across the sheet, two down the fringe
            for a in (1.0,):
                p = o.xy - d * (0.55 * ov * a)
                pts.append((Vector((p.x, p.y, o.z - 0.7 * ov * a + corr)), -ov * a / W, "over"))
            for j in range(nv + 1):
                v = j / nv
                p = o.xy + d * (W * v)
                z = (o.z + (zi - o.z) * v - belly * math.sin(math.pi * v)
                     + 0.06 * self.nz(p.x, p.y) * math.sin(math.pi * v) ** 0.5 + corr)
                pts.append((Vector((p.x, p.y, z)), v, "sheet"))
            for a in (0.45, 1.0):
                p = q + d * (0.3 * fr * a)
                pts.append((Vector((p.x, p.y, zi - 0.95 * fr * a + corr)), 1 + fr * a / W, "fringe"))
            cols.append(dict(s=s, t=t, W=W, d=d, pts=pts, streak=streak, fr=fr))
        self.cols = cols
        # the tear sits where the drawing's dark hole is, under the upper band near the house
        best = min(((m.scr(p.x, p.y, p.z)[0] - 43.0) ** 2 + (m.scr(p.x, p.y, p.z)[1] - 63.0) ** 2, c["s"], v)
                   for c in cols for p, v, kind in c["pts"] if kind == "sheet" and v >= 0.72)
        self.s_t, self.v_t = best[1], best[2]
        return self.sheet(cols)

    def colour(self, c, j, p, v, kind, nrm):
        """The straw at one vertex, sRGB 0-255."""
        m = self.m
        r = self.rng
        st = 0.08 * r.uniform(-1, 1)
        tone = 1.0 + 0.08 * self.nt(p.x, p.y) + 0.06 * (0.5 - min(1.0, max(0.0, v)))
        col = tuple(k * (tone + st) for k in MID)
        if kind == "sheet":
            lit = nrm.dot(SUN)
            fold = sstep(0.88, 0.96, lit) * sstep(0.7, 0.2, v) * (0.55 + 0.45 * self.nf(p.x, p.y))
            col = mixc(col, tuple(k * (1 + 0.5 * st) for k in PALE), min(1.0, 1.4 * fold))
            # the wide south run is burnt further up from its eave, raggedly
            v0 = 0.68 - 0.26 * sstep(1.2, 1.6, c["W"]) + 0.09 * self.ns(p.x, p.y)
            soot = sstep(v0, v0 + 0.3, v) * (0.85 + 0.15 * self.nt(p.y, p.x))
        elif kind == "fringe":
            col = tuple(k * (1 + st) for k in FRINGE)
            soot = 0.45 * (1 - (v - 1) * c["W"] / max(0.05, c["fr"])) + 0.1
        else:
            col = tuple(k * 0.92 for k in col)
            soot = 0.0
        d = self.tear(c["s"], v)
        soot = max(soot, sstep(0.22, 0.0, d))
        col = mixc(col, tuple(k * (1 + 0.6 * st) for k in SOOT), min(1.0, soot))
        return tuple(max(0.0, min(255.0, k)) for k in col)

    def sheet(self, cols):
        """The top surface, smooth, a skirt round every open edge and a
        coarse underside, all in one mesh with its colours."""
        bm = bmesh.new()
        lay = bm.loops.layers.float_color.new("Col")
        strip = bm.faces.layers.float.new("strip")
        n, nr = len(cols) - 1, len(cols[0]["pts"]) - 1
        P = [[c["pts"][j][0] for j in range(nr + 1)] for c in cols]

        def nrm(i, j):
            a = P[min(n, i + 1)][j] - P[max(0, i - 1)][j]
            b = P[i][min(nr, j + 1)] - P[i][max(0, j - 1)]
            N = a.cross(b)
            if N.z < 0:
                N = -N
            return N.normalized()
        top, bot, cv = {}, {}, {}
        for i, c in enumerate(cols):
            for j, (p, v, kind) in enumerate(c["pts"]):
                N = nrm(i, j)
                top[i, j] = bm.verts.new(p)
                th = T_SHEET if kind == "sheet" else 0.07
                bot[i, j] = bm.verts.new(p - N * th)
                rgb = self.colour(c, j, p, v, kind, N)
                cv[i, j] = (*kit.lin(rgb), 1.0)

        def keep(i, j):
            c0, c1 = cols[i], cols[i + 1]
            s = (c0["s"] + c1["s"]) / 2
            v = (c0["pts"][j][1] + c0["pts"][j + 1][1] + c1["pts"][j][1] + c1["pts"][j + 1][1]) / 4
            return self.tear(s, v) > 0
        kept = {(i, j) for i in range(n) for j in range(nr) if keep(i, j)}
        faces = []
        for (i, j) in kept:
            q = [top[i, j], top[i + 1, j], top[i + 1, j + 1], top[i, j + 1]]
            f = bm.faces.new(q)
            f.normal_update()
            if f.normal.z < 0:
                f.normal_flip()
            f.smooth = True
            faces.append(f)
            # each straw strip down the slope has its own tone, crisp at its sides
            f[strip] = 0.93 * cols[i]["streak"] + 0.4 * self.rng.uniform(-1, 1)
        vi = {v: k for k, v in top.items()}
        vi.update({v: k for k, v in bot.items()})
        # skirts on every edge only one kept cell has
        edges = {}
        for (i, j) in kept:
            ring = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            cen = sum((top[k].co for k in ring), Vector()) / 4
            for a in range(4):
                e = frozenset((ring[a], ring[(a + 1) % 4]))
                cnt, _ = edges.get(e, (0, None))
                edges[e] = (cnt + 1, cen)
        for e, (cnt, cen) in edges.items():
            a, b = tuple(e)
            # the hanging straw ends and the frayed outer edge, which faces
            # away from both cameras, are left thin
            if cnt != 1 or a[1] == b[1] == nr or a[1] == b[1] == 0:
                continue
            A, B = top[a], top[b]
            f = bm.faces.new((A, B, bot[b], bot[a]))
            f.normal_update()
            # face away from the cell the edge belongs to
            mid = (A.co + B.co) / 2
            if f.normal.xy.dot((mid - cen).xy) < 0:
                f.normal_flip()
            f.smooth = False
        for f in bm.faces:
            k = (1.0 + 0.48 * max(-1.2, min(1.2, f[strip]))) ** 2.2
            for lp in f.loops:
                c = cv[vi[lp.vert]]
                lp[lay] = (min(1.0, c[0] * k), min(1.0, c[1] * k), min(1.0, c[2] * k), 1.0)
        # loose straws lying down the slope, bright or dark, for the grain
        rng = random.Random(self.m.name + ":straws")
        for _ in range(120):
            i = rng.randrange(n)
            ja = rng.randint(1, nr - 4)
            jb = min(nr - 2, ja + rng.choice((1, 1, 2)))
            if not all((i, j) in kept for j in range(ja, jb)):
                continue
            f, w = rng.uniform(0.2, 0.8), 0.028
            k = (1.28 if rng.random() < 0.5 else 0.5) ** 2.2
            vs = []
            for j in range(ja, jb + 1):
                pa, pb = P[i][j], P[i + 1][j]
                side = (pb - pa).normalized() * w
                p = pa.lerp(pb, f) + nrm(i, j) * 0.014
                c = cv[i, j]
                vs.append((bm.verts.new(p - side), bm.verts.new(p + side),
                           (min(1.0, c[0] * k), min(1.0, c[1] * k), min(1.0, c[2] * k), 1.0)))
            for (a0, a1, ca), (b0, b1, cb) in zip(vs, vs[1:]):
                fs = bm.faces.new((a0, a1, b1, b0))
                fs.normal_update()
                if fs.normal.z < 0:
                    fs.normal_flip()
                fs.smooth = True
                for lp in fs.loops:
                    lp[lay] = ca if lp.vert in (a0, a1) else cb
        return bm

    def frame(self):
        """Posts under the eave, one snapped, the eave beam, the outer wall,
        the burnt floor, and the rafters under the tear."""
        m = self.m
        m.col("lt_post", (118, 102, 78))
        m.col("lt_wall", (92, 82, 67))
        m.col("lt_floor", (34, 28, 20), rough=1.0)
        I = self.I

        def eave_pt(t):
            if t <= self.LS:
                p = I[0].lerp(I[1], t / self.LS)
            else:
                p = I[1].lerp(I[2], (t - self.LS) / self.LD)
            # the beam sits under the sheet, a little in from its edge
            return p

        def inward(t):
            s = self._s_of_t(t)
            c = min(self.cols, key=lambda c: abs(c["s"] - s))
            return c["d"]
        beam_h = 0.16
        for t in self.posts:
            p = eave_pt(t) - inward(t) * 0.15
            top = self.eave_z(t) - T_SHEET - beam_h
            lean = 0.18 if t == self.posts[0] else 0.0
            m.post("r_char" if abs(t - self.inner(self.s_t)[1]) < 0.6 else "lt_post", p.x, p.y, 0.0, top, 0.16, lean=lean)
        # the snapped post: a leaning stump and its top lying in the straw below
        p = eave_pt(self.snap) - inward(self.snap) * 0.15
        top = self.eave_z(self.snap) - T_SHEET
        m.post("lt_post", p.x, p.y, 0.0, top * 0.55, 0.16, lean=0.42)
        m.beam("lt_post", (p.x + 0.2, p.y - 0.35, 0.1), (p.x + 0.5, p.y + 0.3, 0.12), 0.14)
        # the eave beam follows the sheet down into the slump
        ts = [0.12, 0.8, self.snap, 1.9, 2.45, self.LS, self.LS + 0.75, self.LS + self.LD - 0.1]
        t_tear = self.inner(self.s_t)[1]
        for ta, tb in zip(ts, ts[1:]):
            pa = eave_pt(ta) - inward(ta) * 0.15
            pb = eave_pt(tb) - inward(tb) * 0.15
            # charred where the tear burnt through
            m.beam("r_char" if abs((ta + tb) / 2 - t_tear) < 0.7 else "lt_post", (pa.x, pa.y, self.eave_z(ta) - T_SHEET - beam_h / 2),
                   (pb.x, pb.y, self.eave_z(tb) - T_SHEET - beam_h / 2), beam_h)
        # the outer wall the sheet rests on, a little in from its edge
        wall = []
        for c in self.cols[::12] + [self.cols[-1]]:
            o = c["pts"][1][0]
            p = o.xy + c["d"] * 0.12
            wall.append((p.x, p.y))
        m.wall("lt_wall", wall, 0.34, 1.32)
        # the burnt floor under the sheet, showing through the tear and between the strands
        fl = [(c["pts"][1][0].xy + c["d"] * 0.2) for c in self.cols[::12]]
        fl += [(c["pts"][-1][0].xy + c["d"] * 0.25) for c in self.cols[::-12]]
        if sum(a.x * b.y - b.x * a.y for a, b in zip(fl, fl[1:] + fl[:1])) < 0:
            fl = fl[::-1]
        m.slab("lt_floor", [(p.x, p.y, 0.04) for p in fl], 0.04)
        # two charred rafters across the tear
        for ds in (-0.24, 0.2):
            ca = min(self.cols, key=lambda cc: abs(cc["s"] - (self.s_t + ds)))
            a = ca["pts"][1][0]
            b = ca["pts"][1 + 5][0]
            m.beam("r_char", (a.x, a.y, a.z - T_SHEET - 0.08), (b.x, b.y, b.z - T_SHEET - 0.1), 0.1)

    def _s_of_t(self, t):
        if t <= self.LS:
            return self.out.s[TIP] * t / self.LS
        return self.out.s[TIP] + (self.out.L - self.out.s[TIP]) * (t - self.LS) / self.LD


def sheet_object(m, bm, part="thatchsheet", spec=None):
    me = bpy.data.meshes.new(m.name + "_" + part)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(m.name + "_" + part, me)
    bpy.context.collection.objects.link(ob)
    mat = bpy.data.materials.new(m.name + "_" + part)
    mat.use_nodes = True
    nt = mat.node_tree
    b = nt.nodes["Principled BSDF"]
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    nt.links.new(ca.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.95
    if spec is not None:
        b.inputs["Specular IOR Level"].default_value = spec
    me.materials.append(mat)
    m.tris[part] = sum(len(p.vertices) - 2 for p in me.polygons)
    return ob


def with_sheet(m, bm, shed=None):
    """m.done() that also makes the sheet (and the east shed's thatch, less
    glossy so its soot stays dark), squats them with the rest and gives
    every other part a white colour so the joined mesh multiplies by one."""
    orig = m.done

    def done():
        zk, m.zk = m.zk, 1.0
        objs = orig()
        m.zk = zk
        mine = [sheet_object(m, bm)]
        if shed is not None:
            mine.append(sheet_object(m, shed, "shedthatch", spec=0.15))
        objs += mine
        if zk != 1.0:
            m._squat(objs)
        for o in objs:
            if o in mine:
                continue
            a = o.data.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
            a.data.foreach_set("color", [1.0] * (4 * len(o.data.loops)))
        objs.sort(key=lambda o: 0 if o.data.uv_layers else 1)
        return objs
    m.done = done


def ribbon(bm, lay, secs, cols, t=0.1):
    """A torn thatch piece t thick: secs are sections of two top points
    (x, y, z), cols their sRGB colours."""
    top = [[bm.verts.new(p) for p in s] for s in secs]
    bot = [[bm.verts.new((p[0], p[1], p[2] - t)) for p in s] for s in secs]
    cv = {}
    for vs, bs, cs in zip(top, bot, cols):
        for v, b, c in zip(vs, bs, cs):
            cv[v] = (*kit.lin(c), 1.0)
            cv[b] = (*kit.lin(tuple(k * 0.55 for k in c)), 1.0)
    faces = []
    n = len(secs)
    for i in range(n - 1):
        f = bm.faces.new((top[i][0], top[i + 1][0], top[i + 1][1], top[i][1]))
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()
        faces.append(f)
    # skirts round the edges, facing out; no underside, like the west sheet
    cen = sum((v.co for s in top for v in s), Vector()) / (2 * n)
    rims = [(top[i][k], top[i + 1][k], bot[i + 1][k], bot[i][k]) for i in range(n - 1) for k in (0, 1)]
    rims += [(top[i][0], top[i][1], bot[i][1], bot[i][0]) for i in (0, n - 1)]
    for q in rims:
        f = bm.faces.new(q)
        f.normal_update()
        mid = (q[0].co + q[1].co) / 2
        if f.normal.xy.dot((mid - cen).xy) < 0:
            f.normal_flip()
        faces.append(f)
    for f in faces:
        f.smooth = False
        for lp in f.loops:
            lp[lay] = cv[lp.vert]


def east_shed(m):
    """The burnt shed on the main hall's east gable: the sooty back of its
    roof with a broken rafter poking east, a lit strip hanging along the
    wall, and the front half slumped down toward the wing."""
    bm = bmesh.new()
    lay = bm.loops.layers.float_color.new("Col")
    za = lambda x: 2.05 - 0.57 * (x - 3.94)  # noqa: E731
    # the roof's back remnant, its top edge ragged, cut off in a spike
    back = [(3.97, 1.95, 0.05), (4.30, 2.02, 0.09), (4.62, 2.06, 0.0), (4.92, 2.0, 0.07), (5.07, 1.99, 0.0),
            (5.40, 1.33, -0.02)]
    front = [(3.97, 1.40), (4.30, 1.33), (4.62, 1.30), (4.95, 1.17), (5.22, 1.12), (5.45, 1.25)]
    secs = [((bx, by, za(bx) + dz), (fx, fy, za(fx) - 0.02)) for (bx, by, dz), (fx, fy) in zip(back, front)]
    cols = [((26, 21, 9), (48, 38, 18)), ((36, 29, 12), (66, 53, 26)), ((22, 18, 8), (30, 25, 11)),
            ((40, 33, 15), (27, 22, 9)), ((27, 22, 9), (32, 26, 11)), ((30, 25, 11), (30, 25, 11))]
    ribbon(bm, lay, secs, cols, 0.12)
    # the lit strip along the wall, sagging toward the front
    secs = [((4.22, 1.48, 1.9), (4.58, 1.42, 1.64)), ((4.22, 1.05, 1.92), (4.52, 1.02, 1.63)),
            ((4.22, 0.62, 1.89), (4.66, 0.62, 1.58)), ((4.22, 0.18, 1.84), (4.55, 0.16, 1.56)),
            ((4.22, -0.28, 1.79), (4.60, -0.30, 1.51))]
    cols = [((112, 92, 50), (32, 26, 11)), ((190, 160, 92), (48, 38, 18)), ((186, 156, 88), (42, 34, 15)),
            ((194, 162, 94), (52, 41, 19)), ((176, 146, 82), (46, 37, 17))]
    ribbon(bm, lay, secs, cols, 0.11)
    # the front half, hinged off the strip and slumped down to the east
    secs = [((4.55, 0.78, 1.57), (4.80, 0.72, 1.40)), ((4.62, 0.45, 1.55), (5.18, 0.55, 0.95)),
            ((4.55, 0.10, 1.53), (5.25, 0.12, 0.80)), ((4.58, -0.28, 1.49), (5.15, -0.30, 0.70))]
    cols = [((96, 78, 40), (36, 29, 13)), ((176, 146, 84), (74, 60, 30)), ((158, 130, 74), (62, 50, 24)),
            ((120, 98, 54), (50, 40, 19))]
    ribbon(bm, lay, secs, cols, 0.1)
    # the charred frame: the wall plate, one rafter broken off east, one
    # fallen across the slump, two posts
    m.col("es_char", (46, 36, 25), rough=1.0)
    m.beam("es_char", (4.17, 1.9, 1.95), (4.17, -0.3, 1.84), 0.12)
    m.beam("es_char", (4.1, 1.3, 1.8), (5.38, 1.27, 1.13), 0.1)
    m.beam("es_char", (4.66, 0.32, 1.6), (5.24, 0.2, 1.0), 0.1)
    m.post("es_char", 5.05, 1.76, 0.0, za(5.05) - 0.14, 0.16)
    m.post("es_char", 5.1, -0.22, 0.0, 0.62, 0.16, lean=0.12)
    return bm


@model("VerBuild05a")
def verbuild05a(m):
    mc.b05_colours(m)
    keys = m.ruin_palette()
    T = mc.ruin_tones(m, (167, 152, 127), (132, 120, 100), (120, 110, 92), stone=(150, 138, 116))
    P = mc.b05_plan(m.frame(5, 5))
    m.col("house", m.sample(60, 62, 120, 78))
    m.col("wing", (112, 102, 88))
    m.col("yard", (140, 128, 108))
    m.col("yardtop", (167, 152, 127))
    m.col("char", (34, 28, 23))
    # the house and wing as passed: holed where drawn, the centre collapse
    # breaking down through the front wall into the yard
    roof = m.blobs_px([(140, 18, 4.0, 0.8, 0.65), (93, 46, 3.35, 0.6, 0.72), (138, 101, 1.9, 0.72, 0.42),
                       (101, 60, 3.0, 1.0, 0.5)], rag=0.3, seed=5)
    sa, sb = m.X(85) - P["main"][0] - 0.15, m.X(118) - P["main"][0] - 0.15
    breach = [(sa - 0.05, 3.0), (sa + 0.1, 2.3), (sa + 0.3, 1.5), (sa + 0.6, 1.0), (sa + 1.0, 0.7), (sa + 1.4, 1.1),
              (sa + 1.7, 0.8), (sb - 0.3, 1.3), (sb - 0.12, 2.2), (sb, 3.0)]
    m.col("eave", (98, 40, 24))
    T2 = dict(T, char=("r_char", "char"))
    for k, ze, zr, dz in (("main", P["me"], P["mr"], 0.9), ("wing", P["we"], P["wr"], 0.6)):
        x0, x1, y0, y1 = P[k]
        mc.eaved_ruin(m, "house" if k == "main" else "wing", x0, x1, y0, y1, ze, zr, roof, keys, dz,
                      breach=breach if k == "main" else None)
        inside = mc.rect(x0 + 0.3, x1 - 0.3, y0 + 0.3, y1 - 0.3)
        rz = lambda x, y, ze=ze: m.roof_z(x, y) or ze  # noqa: E731
        mc.tangle(m, T2["char"] + ("r_wood", "r_wood"), inside, lambda x, y: rz(x, y) - 1.1,
                  lambda x, y: rz(x, y) - 0.3, 14, seed=k, L=(0.9, 1.8), where=lambda x, y: roof(x, y, rz(x, y)) < 0.15)
    mc.b05a_front(m, P, T, (m.X(85), m.X(118)))
    m.window("dark", P["wing"][1] - 0.9, P["wing"][2], 0.5, 0.35, 0.3)
    # the west lean-to, shaped here
    lt = LeanTo(m)
    bm = lt.build()
    lt.frame()
    with_sheet(m, bm, east_shed(m))
    # the thick grey rubble yard wall, broken where drawn, and a little rubble spilled over the
    # yard (only four pieces, to stay near the triangle budget)
    m.col("fwall", (100, 90, 75))
    m.col("fwtop", (110, 99, 82))
    m.bwall("fwall", P["front"], 0.5, [(0, 1.0), (0.2, 0.7), (0.3, 0.35), (0.42, 0.4), (0.5, 0.8), (0.75, 1.0),
                                       (0.85, 0.5), (1, 0.9)], frac=True, jag=0.3, step=0.2, cap="fwtop")
    (ax, ay), (bx, by) = P["front"][0], P["front"][-1]
    yard = [(ax + 0.6, ay + 0.3), (bx, by + 0.3), (bx, P["main"][2] - 0.2), (ax + 1.5, P["main"][2] - 0.2)]
    m.debris(T, yard, 0.4, zfn=lambda x, y: 0.08, bed=False, density=1.3, maxn=4,
             mix=dict(stone=0.45, tile=0.3, char=0.15, wood=0.1), size=(0.2, 0.45), beam_len=(0.5, 1.2))
    m.scatter(keys, max_area=200)
