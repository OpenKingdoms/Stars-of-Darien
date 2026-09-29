"""Parametric builders for the props family, run inside Blender: round
fountains, braziers, bonfires and fire rings, altars, two-wheeled
handcarts and corn patches.

Frame as handkit: one unit is one map cell, -Y toward the classic camera,
Z up, the origin on the feature's anchor. Colours are picture colours
(0-255 sRGB) lifted by GAIN; every part carries 'Col' vertex colours that
the game multiplies into its material (misckit.vmat). A model can name
parts in "painted" to take the original picture instead (paint_groups);
ship() exports those as okPaint materials with no pixels. Flame and ember
materials carry castShadows false in their glTF extras. sturdy() turns a
table entry's pixel fit into the built sizes, tall parts thicker and lower.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(os.path.dirname(HERE))
for _p in (TOOLS, os.path.join(TOOLS, "lodes")):
    if _p not in sys.path:
        sys.path.insert(0, _p)
import handkit as hk  # noqa: E402
import misckit as mk  # noqa: E402

OUT = r"D:\OKReplace\hand\props"
SPRITES = r"D:\OKReplace\sprites"
GAIN = 1.1  # the stage renders a top face at about its albedo, a side a little under

TAU = 2 * math.pi


# ---------------------------------------------------------------- colour

def lift(c, k=GAIN):
    return tuple(min(255.0, v * k) for v in c)


def brightest(*cols):
    """The channel-wise brightest of several colours, a material ref that
    every one of them fits under."""
    return tuple(max(c[i] for c in cols) for i in range(3))


def mat(name, srgb, rough=0.85, metal=0.0, emit=None, strength=0.0):
    """A vertex-coloured material; srgb is the brightest colour it will show."""
    return mk.vmat(name, lift(srgb), rough, metal, emit, strength)


def matte(m):
    """No specular sheen, which would lift dark greys and greens toward grey."""
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.0
    return m


def paint(ob, fn):
    """Vertex colours from fn(poly, co, normal, loop) -> picture sRGB, lifted."""
    return mk.vpaint(ob, lambda p, co, n, li: lift(fn(p, co, n, li)))


def mix(a, b, t):
    return mk.mixc(a, b, t)


def fbm(q):
    return 0.5 + 0.5 * noise.noise(q) + 0.25 * noise.noise(q * 2.7) + 0.12 * noise.noise(q * 6.1)


def mottle(ob, dark, light, scale=3.0, seed=0, top=None, foot=None, foot_h=0.2, crack=None, moss=None, side=0.0,
           band=None):
    """Stone: coherent mottling between dark and light, a brighter top, a
    darker foot, thin cracks (scale, width, colour) and moss (colour, amount);
    band(co) gives other (dark, light) colours for some corners, or None."""
    r = random.Random(seed)
    off = Vector((r.uniform(0, 90), r.uniform(0, 90), r.uniform(0, 90)))

    def fn(p, co, n, li):
        q = co * scale + off
        b = band(co) if band is not None else None
        c = mix(*(b or (dark, light)), fbm(q) - 0.25)
        if top is not None and n.z > 0.6:
            c = mix(c, top, 0.55)
        if side and abs(n.z) < 0.4:
            c = mix(c, dark, side)
        if moss is not None:
            m = noise.noise(co * 1.7 + off * 1.3) + 0.35 * noise.noise(co * 5.0 + off)
            if m > 0.45 - moss[1] and n.z > -0.2:
                c = mix(c, moss[0], min(1.0, (m - 0.45 + moss[1]) * 2.5) * (0.9 if n.z > 0.5 else 0.6))
        if crack is not None and abs(noise.noise(co * crack[0] + off * 0.7)) < crack[1]:
            c = mix(c, crack[2], 0.8)
        if foot is not None and co.z < foot_h:
            c = mix(c, foot, 0.65 * (1.0 - max(0.0, co.z) / foot_h))
        if n.z < -0.4:
            c = mix(c, dark, 0.6)
        return c
    return paint(ob, fn)


def grain(ob, dark, light, axis, seed=0, knots=0.0, end=None, char=None):
    """Wood: streaks along the grain axis, darker undersides; end is the end
    grain colour for faces facing along the axis, char=(colour, z-free
    function co -> amount) scorches."""
    r = random.Random(seed)
    off = Vector((r.uniform(0, 90), r.uniform(0, 90), r.uniform(0, 90)))
    A = Vector(axis).normalized()

    def fn(p, co, n, li):
        along = co.dot(A)
        across = co - A * along
        q = across * 14.0 + A * (along * 1.2) + off
        c = mix(dark, light, 0.5 + 0.6 * noise.noise(q) + 0.2 * noise.noise(q * 3.1))
        if knots and noise.noise(co * 6.0 + off) > 1.0 - knots:
            c = mix(c, dark, 0.7)
        if end is not None and abs(n.dot(A)) > 0.8:
            c = end
        if n.z < -0.4:
            c = mix(c, dark, 0.5)
        if char is not None:
            k = char[1](co)
            if k > 0:
                c = mix(c, char[0], min(1.0, k))
        return c
    return paint(ob, fn)


def flat(ob, colour):
    return paint(ob, lambda p, co, n, li: colour)


# ---------------------------------------------------------------- shapes

def ring(outline, seg=32, a0=0.0, a1=360.0, mat=None, name="ring", cx=0.0, cy=0.0):
    """Revolves a closed (r, z) outline round Z; a part turn gets end caps."""
    full = abs(a1 - a0) >= 359.99
    m = len(outline)
    cols = seg if full else seg + 1
    verts, faces = [], []
    for k in range(cols):
        a = math.radians(a0 + (a1 - a0) * k / seg)
        for (r, z) in outline:
            verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
    for k in range(seg):
        c0, c1 = k * m, ((k + 1) % cols) * m
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((c0 + j, c1 + j, c1 + j2, c0 + j2))
    if not full:
        faces.append(tuple(range(m - 1, -1, -1)))
        faces.append(tuple(seg * m + j for j in range(m)))
    ob = mk.mesh(name, verts, faces, mat)
    mk._fix_normals(ob)
    return ob


def lathe(profile, seg=24, mat=None, name="lathe"):
    """An (r, z) profile from the axis at the bottom to the axis at the top."""
    return mk.lathe(profile, seg=seg, mat=mat, name=name)


def disc(r, z, seg=32, mat=None, name="disc", cx=0.0, cy=0.0, wobble=0.0, seed=0):
    rnd = random.Random(seed)
    verts = [(cx + r * (1 + rnd.uniform(-wobble, wobble)) * math.cos(TAU * k / seg),
              cy + r * (1 + rnd.uniform(-wobble, wobble)) * math.sin(TAU * k / seg), z) for k in range(seg)]
    return mk.mesh(name, verts, [tuple(range(seg))], mat)


def disc_rings(r, z, seg=24, rings=3, mat=None, name="disc", cx=0.0, cy=0.0, wobble=0.0, seed=0, r_in=0.0,
               ragged=0.0):
    """A flat disc in concentric rings round a centre point, so vertex colours
    can change from the middle out; r_in > 0 leaves a hole (an annulus),
    its edge broken by up to ragged cells."""
    rnd = random.Random(seed)
    edge = [r * (1 + rnd.uniform(-wobble, wobble)) for _ in range(seg)]
    if r_in > 0:
        hole = [r_in + rnd.uniform(0.0, ragged) for _ in range(seg)]
        verts = []
        for k in range(rings + 1):
            f = k / rings
            for j in range(seg):
                a = TAU * j / seg
                rr = hole[j] + (edge[j] - hole[j]) * f
                verts.append((cx + rr * math.cos(a), cy + rr * math.sin(a), z))
        faces = [(k * seg + j, (k + 1) * seg + j, (k + 1) * seg + (j + 1) % seg, k * seg + (j + 1) % seg)
                 for k in range(rings) for j in range(seg)]
        return face_up(mk.mesh(name, verts, faces, mat))
    verts = [(cx, cy, z)]
    for k in range(1, rings + 1):
        for j in range(seg):
            a = TAU * j / seg
            verts.append((cx + edge[j] * k / rings * math.cos(a), cy + edge[j] * k / rings * math.sin(a), z))
    faces = [(0, 1 + j, 1 + (j + 1) % seg) for j in range(seg)]
    for k in range(1, rings):
        a0, b0 = 1 + (k - 1) * seg, 1 + k * seg
        faces += [(a0 + j, b0 + j, b0 + (j + 1) % seg, a0 + (j + 1) % seg) for j in range(seg)]
    return face_up(mk.mesh(name, verts, faces, mat))


def face_up(ob):
    """Turns every face of a flat piece to face up."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.normal_update()
    bmesh.ops.reverse_faces(bm, faces=[f for f in bm.faces if f.normal.z < 0])
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    return ob


def band(r, z0, z1, seg=32, mat=None, name="band"):
    """The outward side of a drum, open top and bottom."""
    verts = [(r * math.cos(TAU * j / seg), r * math.sin(TAU * j / seg), z) for z in (z0, z1) for j in range(seg)]
    faces = [(j, (j + 1) % seg, seg + (j + 1) % seg, seg + j) for j in range(seg)]
    return mk.mesh(name, verts, faces, mat)


def box(sx, sy, sz, loc=(0, 0, 0), rot=(0, 0, 0), mat=None, name="box", bevel=0.0):
    """A box centred on loc in x, y and sitting on loc z, then turned (degrees) round its own base."""
    ob = hk.box(sx, sy, sz, mat=mat, name=name)
    if bevel:
        hk.bevel(ob, width=bevel, segments=1)
    return mk.xform(ob, loc=loc, rot=rot)


def rod(p0, p1, r0, r1=None, seg=6, mat=None, name="rod"):
    return mk.rod(p0, p1, r0, r1, seg=seg, mat=mat, name=name)


def plank(p0, p1, w, t, up=(0, 0, 1), mat=None, name="plank"):
    """A board from p0 to p1 (centreline), w wide across, t thick along up."""
    P0, P1 = Vector(p0), Vector(p1)
    T = (P1 - P0)
    L = T.length
    T.normalize()
    U = Vector(up)
    U = (U - T * U.dot(T)).normalized()
    S = U.cross(T)
    ob = hk.box(L, w, t, z=-t / 2, mat=mat, name=name)
    M = Matrix((
        (T.x, S.x, U.x, 0), (T.y, S.y, U.y, 0), (T.z, S.z, U.z, 0), (0, 0, 0, 1)))
    return mk.matrix(ob, Matrix.Translation((P0 + P1) / 2) @ M)


def pebble(r, loc, scale=(1, 1, 0.6), rot=0.0, mat=None, name="pebble", seed=0, rough=0.12, subdiv=1):
    ob = mk.ico(r, subdiv=subdiv, mat=mat, name=name)
    mk.jitter(ob, r * rough, seed=seed)
    mk.xform(ob, rot=(0, 0, rot), scale=scale)
    lo = min(v.co.z for v in ob.data.vertices)
    return mk.xform(ob, loc=(loc[0], loc[1], loc[2] - lo * 0.85))


def place(parts, M):
    for p in parts:
        mk.matrix(p, M)
    return parts


def smooth(ob, angle=40):
    return hk.smooth(ob, angle)


def sweep_tube(path, radii, seg=8, mat=None, name="tube", flat=1.0):
    """A tube along a polyline, radius per point, capped."""
    P = [Vector(p) for p in path]
    n = len(P)
    verts, faces = [], []
    prevN = None
    for i, p in enumerate(P):
        T = (P[min(n - 1, i + 1)] - P[max(0, i - 1)]).normalized()
        if prevN is None:
            ref = Vector((0, 0, 1)) if abs(T.z) < 0.9 else Vector((1, 0, 0))
            N = T.cross(ref).normalized()
        else:
            N = (prevN - T * prevN.dot(T)).normalized()
        prevN = N
        B = T.cross(N).normalized()
        for j in range(seg):
            a = TAU * j / seg
            verts.append(p + (N * math.cos(a) + B * math.sin(a) * flat) * radii[i])
    for i in range(n - 1):
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append((i * seg + j, i * seg + j2, (i + 1) * seg + j2, (i + 1) * seg + j))
    faces.append(tuple(range(seg - 1, -1, -1)))
    faces.append(tuple((n - 1) * seg + j for j in range(seg)))
    ob = mk.mesh(name, verts, faces, mat)
    mk._fix_normals(ob)
    return ob


def along(vals, sub):
    """Per control point values spread over catmull(pts, sub)'s points."""
    out = []
    for i in range(len(vals) - 1):
        for k in range(sub):
            out.append(vals[i] + (vals[i + 1] - vals[i]) * k / sub)
    out.append(vals[-1])
    return out


def catmull(pts, sub=4):
    P = [Vector(p) for p in pts]
    out = []
    for i in range(len(P) - 1):
        p0, p1, p2, p3 = P[max(0, i - 1)], P[i], P[i + 1], P[min(len(P) - 1, i + 2)]
        for k in range(sub):
            t = k / sub
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(P[-1])
    return out


# ---------------------------------------------------------------- fire

FIRE = ((246, 192, 112), (236, 150, 78), (206, 110, 54), (140, 62, 22))


NO_SHADOW = "castShadows"


def glow_mat(name, srgb, rough=0.6, emit=None, strength=0.0):
    """A flame or ember material. It carries castShadows false in its glTF
    extras so the game turns its shadow off."""
    m = mat(name, srgb, rough=rough, emit=emit, strength=strength)
    m[NO_SHADOW] = False
    return m


def fire_mats():
    return (glow_mat("flame_core", FIRE[0], emit=(1.0, 0.66, 0.3), strength=0.9),
            glow_mat("flame", FIRE[1], emit=(1.0, 0.48, 0.14), strength=0.9),
            glow_mat("flame_tip", FIRE[2], emit=(0.9, 0.26, 0.06), strength=0.8))


def for_render(ob):
    """Readies the exported model for the check renders: the joined mesh's
    UV map made the one Cycles samples (a join onto a part without UVs
    leaves it unset), and the flames' shadows off."""
    if ob.data.uv_layers:
        ob.data.uv_layers[0].active_render = True
    return shadowless(ob)


def shadowless(ob):
    """For the renders only, after export: shadow rays pass through faces
    whose material is marked castShadows false, as the game will do."""
    for m in ob.data.materials:
        if m is None or m.get(NO_SHADOW, True) or not m.use_nodes:
            continue
        nt = m.node_tree
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        src = out.inputs["Surface"].links[0].from_socket
        lp = nt.nodes.new("ShaderNodeLightPath")
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        mx = nt.nodes.new("ShaderNodeMixShader")
        nt.links.new(lp.outputs["Is Shadow Ray"], mx.inputs["Fac"])
        nt.links.new(src, mx.inputs[1])
        nt.links.new(tr.outputs["BSDF"], mx.inputs[2])
        nt.links.new(mx.outputs["Shader"], out.inputs["Surface"])
    return ob


def fire_colour(t, pal=FIRE):
    if t < 0.35:
        return mix(pal[0], pal[1], t / 0.35)
    if t < 0.7:
        return mix(pal[1], pal[2], (t - 0.35) / 0.35)
    return mix(pal[2], pal[3], (t - 0.7) / 0.3)


def tongue(base, h, w, lean=(0.0, 0.0), curl=0.05, twist=90.0, seed=1, rings=7, seg=6, mats=None,
           name="tongue", flatness=0.45, pal=FIRE, taper=1.15):
    """A flame tongue: a flattened twisting section swept up a bending path,
    swelling low and drawn to a point, yellow at the root and red at the
    tip; its normals point up so the game's sun lights it evenly."""
    r = random.Random(seed)
    bx, by, bz = base
    lx, ly = lean
    ll = math.hypot(lx, ly) or 1.0
    px, py = -ly / ll, lx / ll
    ph = r.uniform(0, math.pi)
    verts, ts = [], []
    for k in range(rings):
        t = k / rings
        cx = bx + lx * t ** 1.5 + px * curl * math.sin(1.6 * math.pi * t)
        cy = by + ly * t ** 1.5 + py * curl * math.sin(1.6 * math.pi * t)
        cz = bz + h * t
        rad = w * (0.6 + 1.7 * t) * (1.0 - t) ** taper
        ang = ph + math.radians(twist) * t
        for j in range(seg):
            a = TAU * j / seg
            u, v = math.cos(a) * rad, math.sin(a) * rad * flatness
            verts.append((cx + u * math.cos(ang) - v * math.sin(ang), cy + u * math.sin(ang) + v * math.cos(ang), cz))
            ts.append(t)
    tip = len(verts)
    verts.append((bx + lx + px * curl * math.sin(1.6 * math.pi), by + ly + py * curl * math.sin(1.6 * math.pi), bz + h))
    ts.append(1.0)
    faces = []
    for k in range(rings - 1):
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append((k * seg + j, k * seg + j2, (k + 1) * seg + j2, (k + 1) * seg + j))
    last = (rings - 1) * seg
    for j in range(seg):
        faces.append((last + j, last + (j + 1) % seg, tip))
    faces.append(tuple(range(seg - 1, -1, -1)))
    ob = mk.mesh(name, verts, faces)
    mk._fix_normals(ob)
    mats = mats or fire_mats()
    for m in mats:
        ob.data.materials.append(m)
    for p in ob.data.polygons:
        tm = sum(ts[i] for i in p.vertices) / len(p.vertices)
        p.material_index = 0 if tm < 0.3 else 1 if tm < 0.65 else 2
    cols = []
    for t in ts:
        c = fire_colour(t, pal)
        if 0.15 < t < 0.85 and r.random() < 0.2:
            c = mix(c, pal[3], 0.5)
        cols.append(c)
    _tint(ob, cols)
    return mk.up_normals(ob)


def _tint(ob, cols):
    """Per-vertex picture colours as ratios to each face's material ref."""
    me = ob.data
    refs = [m["ref"] for m in me.materials]
    at = me.color_attributes.get("Col") or me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for p in me.polygons:
        ref = refs[p.material_index]
        for li in p.loop_indices:
            c = lift(cols[me.loops[li].vertex_index])
            at.data[li].color = tuple(min(1.0, mk.lin(c[i]) / max(1e-4, mk.lin(ref[i]))) for i in range(3)) + (1.0,)


def blaze(base, height, width, seed=1, count=7, spread=None, mats=None, lean=(0.0, 0.0), pal=FIRE, taper=0.8,
          side=(0.28, 0.62)):
    """A fire: one tall central tongue and a ring of shorter ones round it."""
    rnd = random.Random(seed)
    mats = mats or fire_mats()
    spread = width * 0.9 if spread is None else spread
    bx, by, bz = base
    out = [tongue((bx, by, bz), height, width, lean=lean, curl=width * 0.5, twist=rnd.choice((-1, 1)) * 200,
                  seed=seed, rings=10, seg=6, mats=mats, name="flame0", pal=pal, taper=taper, flatness=0.55)]
    for k in range(count):
        a = TAU * k / count + rnd.uniform(-0.3, 0.3)
        d = spread * rnd.uniform(0.35, 1.0)
        h = height * rnd.uniform(*side)
        out.append(tongue((bx + d * math.cos(a), by + d * math.sin(a), bz + rnd.uniform(-0.05, 0.05)), h,
                          width * rnd.uniform(0.5, 0.8),
                          lean=(lean[0] * h / height - math.cos(a) * d * 0.3, lean[1] * h / height - math.sin(a) * d * 0.3),
                          curl=width * 0.3, twist=rnd.choice((-1, 1)) * rnd.uniform(60, 160), seed=seed * 31 + k,
                          rings=6, seg=5, mats=mats, name="flame%d" % (k + 1), pal=pal))
    return out


def log(p0, p1, r0, r1, bark, dark, seed=1, char=None, seg=7, end=(170, 130, 86), mat_=None, bend=0.04,
        name="log"):
    """A tapering, slightly bent log from p0 to p1; char=(colour, from t)
    blackens it from that fraction of its length on."""
    r = random.Random(seed)
    P0, P1 = Vector(p0), Vector(p1)
    D = P1 - P0
    L = D.length
    T = D.normalized()
    side = T.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))
    up = side.cross(T).normalized()
    if up.z < 0:
        up, side = -up, -side
    b1 = r.uniform(-1, 1) * bend
    nr = max(2, int(L / 0.25))
    lump = [r.uniform(0.88, 1.08) for _ in range(seg)]
    path = [P0 + D * (i / nr) + up * (b1 * math.sin(math.pi * i / nr) * L) for i in range(nr + 1)]
    radii = [(r0 + (r1 - r0) * i / nr) for i in range(nr + 1)]
    ob = sweep_tube(path, radii, seg=seg, mat=mat_, name=name)
    for v in ob.data.vertices:
        c = v.co
        t = max(0.0, min(1.0, (c - P0).dot(T) / L))
        c += (c - (P0 + D * t)) * (lump[v.index % seg] - 1.0)
    ob.data.update()

    def fn(p, co, n, li):
        t = max(0.0, min(1.0, (co - P0).dot(T) / L))
        c = mix(dark, bark, 0.5 + 0.6 * noise.noise(co * 9.0 + Vector((seed, 0, 0))))
        if abs(n.dot(T)) > 0.8 and t < 0.05:
            c = end
        if char is not None and t > char[1]:
            c = mix(c, char[0], min(1.0, (t - char[1]) / 0.12))
        if n.z < -0.4:
            c = mix(c, dark, 0.5)
        return c
    paint(ob, fn)
    return smooth(ob, 50)


# ---------------------------------------------------------------- wheel

def wheel(r, width, spokes=8, felloe=0.12, hub_r=0.13, hub_len=0.36, wood=None, dark=None, colours=None,
          seed=0, seg=16, broken=0):
    """A spoked cart wheel in its own frame: centre at the origin, axle along Y."""
    lt, dk = colours or ((110, 92, 72), (52, 43, 34))
    rim = ring([(r - felloe, -width / 2), (r, -width / 2), (r, width / 2), (r - felloe, width / 2)], seg=seg,
               mat=wood, name="felloe")
    hub = hk.cylinder(hub_r, hub_len, seg=8, z=-hub_len / 2, mat=wood, name="hub")
    parts = [rim, hub]
    rnd = random.Random(seed)
    for k in range(spokes):
        if broken and k in (1, 4)[:broken]:
            continue
        a = TAU * k / spokes + 0.2
        d = Vector((math.cos(a), math.sin(a), 0))
        s = rod(tuple(d * hub_r * 0.8), tuple(d * (r - felloe * 0.6)), 0.045, 0.035, seg=4, mat=wood,
                name="spoke%d" % k)
        parts.append(s)
    for p in parts:
        grain(p, dk, lt, (0, 0, 1) if p.name.startswith("hub") else (1, 1, 0), seed=rnd.randint(0, 999))
    # the wheel was built with its axle along Z; turn the axle onto Y
    place(parts, Matrix.Rotation(math.radians(90), 4, "X"))
    return parts


# ---------------------------------------------------------------- straw and leaves

def ribbon(points, widths, normal_hint=(0, 0, 1), mat=None, name="ribbon"):
    """A flat strip through points, widths per point (0 gives a point)."""
    P = [Vector(p) for p in points]
    n = len(P)
    verts, faces = [], []
    H = Vector(normal_hint)
    for i, p in enumerate(P):
        T = (P[min(n - 1, i + 1)] - P[max(0, i - 1)]).normalized()
        S = T.cross(H)
        if S.length < 1e-4:
            S = T.cross(Vector((1, 0, 0)))
        S.normalize()
        w = widths[i] / 2
        verts.append(tuple(p - S * w))
        verts.append(tuple(p + S * w))
    for i in range(n - 1):
        faces.append((2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2))
    ob = mk.mesh(name, verts, faces, mat)
    return ob


# ---------------------------------------------------------------- fountain

def fountain(p):
    """A round fountain: a sloped outer skirt under a ring of coping blocks,
    the pool, concentric stone lips each holding water a step higher, and a
    centre piece (a bronze serpent on a pedestal, or a green bronze orb).
    Ruins drop blocks (missing angle ranges), tumble others, crack the
    stone, drain the water and topple the centre piece."""
    C = p["colours"]
    rnd = random.Random(p.get("seed", 1))
    ruin = p.get("ruin", False)
    parts = []
    stone = mat("stone", C["stone"][1])
    skirt_m = mat("skirt", C["skirt"][1])
    crack = (2.2, 0.05, C.get("crack", (40, 36, 30))) if ruin else None

    rb, rt, h = p["skirt"]
    ci, co_, z0, z1, n = p["coping"]
    sk = ring([(ci, 0.0), (rb, 0.0), (rt, h * 0.8), (rt - 0.03, h), (ci, h)], seg=48, mat=skirt_m, name="skirt")
    mottle(sk, *C["skirt"], scale=2.5, seed=1, foot=C.get("foot"), foot_h=0.25, crack=crack)
    parts.append(smooth(sk, 35))
    lump, notch = p.get("lump", 0.0), p.get("notch", 0.0)
    bt = p.get("block_tone")
    # toned blocks get their own material, matte so their dark greys stay dark
    block_m = matte(mat("coping", (110, 108, 106))) if bt else stone
    if C.get("gap"):
        # dark packing in each joint between two blocks still in place
        gm = mat("gap", C["gap"])
        if bt:
            matte(gm)
        mids = [(360.0 * (k + 0.5) / n + p.get("phase", 0.0)) % 360 for k in range(n)]
        here = [not any(lo <= m_ <= hi for lo, hi in p.get("missing", ())) for m_ in mids]
        for k in range(n):
            if here[k] and here[(k + 1) % n]:
                a = 360.0 * (k + 1) / n + p.get("phase", 0.0)
                gh = z0 + p.get("gap_h", 0.03)
                j = ring([(ci + 0.04, z0 - 0.02), (co_ - 0.1, z0 - 0.02), (co_ - 0.1, gh), (ci + 0.04, gh)],
                         seg=1, a0=a - 2.5, a1=a + 2.5, mat=gm, name="gapfill%d" % k)
                parts.append(flat(j, C["gap"]))

    missing = p.get("missing", ())
    tumble = p.get("tumble", {})
    gap = p.get("gap", 1.4)
    for k in range(n):
        a0 = 360.0 * k / n + gap / 2 + p.get("phase", 0.0)
        a1 = 360.0 * (k + 1) / n - gap / 2 + p.get("phase", 0.0)
        mid = ((a0 + a1) / 2) % 360
        if any(lo <= mid <= hi for lo, hi in missing):
            continue
        top = z1 + rnd.uniform(-0.025, 0.025)
        e = 0.04
        # a notched outer edge: each block its own depth; a lump: its top domed
        ck = co_ + rnd.uniform(-notch, notch * 0.6)
        outline = [(ci, z0), (ck, z0), (ck, top - e), (ck - e, top), (ci + e, top), (ci, top - e)]
        if lump:
            top -= lump * 0.5
            outline = [(ci, z0), (ck, z0), (ck, top - 2 * e), (ck - 2 * e, top), ((ci + ck) / 2, top + lump),
                       (ci + 2 * e, top), (ci, top - 2 * e)]
        if p.get("coping_solid"):
            if k:
                continue
            a0, a1 = 0.0, 360.0
        b = ring(outline, seg=48 if p.get("coping_solid") else 4 if p.get("rough") else 3, a0=a0, a1=a1, mat=block_m,
                 name="coping%d" % k)
        if p.get("rough"):
            # weathered blocks: corners knocked about, tops no longer level
            for v in b.data.vertices:
                v.co += noise.noise_vector(v.co * 3.0 + Vector((k, 0, 0))) * p["rough"]
            b.data.update()
        if k in tumble:
            dz, tilt, slide = tumble[k]
            c = Vector((math.cos(math.radians(mid)), math.sin(math.radians(mid)), 0)) * (ci + co_) / 2
            M = (Matrix.Translation(c + Vector((0, 0, dz))) @ Matrix.Rotation(math.radians(tilt), 4,
                 Vector((-c.y, c.x, 0)).normalized()) @ Matrix.Translation(-c))
            mk.matrix(b, M)
            mk.matrix(b, Matrix.Translation(c.normalized() * slide))
        if bt:
            block_tone(b, bt, random.Random(1000 * p.get("seed", 1) + k), crack, ends=(a0, a1))
        else:
            mottle(b, *C["stone"], scale=3.0, seed=10 + k, crack=crack)
        parts.append(smooth(b, 50) if lump else b)

    for k, (lo, hi) in enumerate(missing if p.get("gap_rubble") else ()):
        # broken pieces lying where the fallen blocks stood
        for j in range(p["gap_rubble"]):
            a = math.radians(rnd.uniform(lo - 6, hi + 6))
            d = rnd.uniform(ci + 0.1, co_ - 0.1)
            sz = rnd.uniform(0.12, 0.2)
            q = mk.rock(sz * 1.4, sz, sz * 0.7, cuts=1, rough=sz * 0.15, seed=600 + 7 * k + j, mat=stone,
                        name="gaprubble%d_%d" % (k, j), taper=0.2)
            mk.xform(q, rot=(rnd.uniform(-20, 20), rnd.uniform(-20, 20), rnd.uniform(0, 180)),
                     loc=(d * math.cos(a), d * math.sin(a), h - 0.04))
            mottle(q, *C["stone"], scale=4.0, seed=610 + 7 * k + j)
            parts.append(q)

    if p.get("dry"):
        parts += dry_basin(p, C, rnd)
    water = None
    if p.get("pool_z") is not None and not p.get("dry"):
        water = mat("water", C["water"][1], rough=0.12)
        w = disc(ci + 0.01, p["pool_z"], seg=48, mat=water, name="pool")
        paint(w, lambda pp, co, nn, li: mix(C["water"][0], C["water"][1],
                                            0.5 + 0.5 * noise.noise(co * 1.5 + Vector((3, 1, 0)))))
        parts.append(w)
    if p.get("floor_z") is not None:
        fl = disc(ci + 0.01, p["floor_z"], seg=48, mat=skirt_m, name="floor")
        mottle(fl, *C["floor"], scale=2.0, seed=5, crack=(1.6, 0.06, C.get("crack", (40, 36, 30))))
        parts.append(fl)

    for i, t in enumerate(() if p.get("dry") else p.get("tiers", ())):
        r_in, r_out, zt, wz = t[:4]
        arc = t[4] if len(t) > 4 and t[4] is not None else (0.0, 360.0)
        e = 0.05
        lip = ring([(r_in, 0.0), (r_out, 0.0), (r_out, zt - e), (r_out - e, zt), (r_in + e, zt), (r_in, zt - e)],
                   seg=40 if arc[1] - arc[0] > 359 else 20, a0=arc[0], a1=arc[1], mat=stone, name="lip%d" % i)
        lc = C.get("lip", C["stone"])
        mottle(lip, *lc, scale=3.0, seed=40 + i, crack=crack)
        parts.append(smooth(lip, 35))
        if wz is not None and water is not None:
            w = disc(r_in + 0.01, wz, seg=32, mat=water, name="water%d" % i)
            wc = C.get("water_in", C["water"])
            if len(t) > 5 and t[5] is not None:
                wc = t[5]
            paint(w, lambda pp, co, nn, li, wc=wc: mix(wc[0], wc[1], 0.5 + 0.6 * noise.noise(co * 2.5 + Vector((i, 2, 0)))))
            parts.append(w)

    centre = p.get("centre")
    if centre == "serpent":
        parts += serpent_statue(p, C, fallen=False)
    elif centre == "serpent_fallen":
        parts += serpent_statue(p, C, fallen=True)
    elif centre in ("orb", "bowl_broken"):
        parts += orb_spout(p, C, broken=centre == "bowl_broken")

    for k, d in enumerate(p.get("debris", ())):
        x, y, s = d[:3]
        kind = d[3] if len(d) > 3 else "block"
        if kind == "finial":
            # the fallen finial: a smooth ball with a short spike
            m = mat("finial", C["finial"][1], rough=0.5)
            ball = pebble(s, (x, y, 0.0), scale=(1, 1, 1), mat=m, name="finial", seed=k, rough=0.0, subdiv=2)
            dv = Vector(p.get("finial_dir", (-0.7, -0.6, -0.25))).normalized()
            c = Vector((x, y, s * 0.85))
            spike = rod(tuple(c + dv * s * 0.8), tuple(c + dv * s * 1.9), s * 0.22, 0.015, seg=6, mat=m,
                        name="finial_spike")
            for q in (ball, spike):
                mottle(q, *C["finial"], scale=4.0, seed=70 + k)
                parts.append(smooth(q, 60))
            continue
        # a block that lands where the picture is clear, or is given its own
        # colour, gets plain stone, not paint
        sh = p.get("shift", (0.0, 0.0))
        own = kind if isinstance(kind, tuple) else None
        clear = own is not None or ("debris" in p.get("painted", ()) and not seen(p, x + sh[0], y + sh[1], s * 0.5))
        b = mk.rock(s * rnd.uniform(1.1, 1.6), s * rnd.uniform(0.8, 1.1), s * rnd.uniform(0.5, 0.75), cuts=1,
                    rough=s * 0.08, seed=80 + k, mat=stone, name=("rubble%d" if clear else "debris%d") % k, taper=0.1)
        mk.xform(b, rot=(rnd.uniform(-12, 12), rnd.uniform(-12, 12), rnd.uniform(0, 180)), loc=(x, y, -0.03))
        pal = (tuple(v * 0.85 for v in own), tuple(v * 1.15 for v in own)) if own else             C.get("rubble", C["stone"]) if clear else C["stone"]
        mottle(b, *pal, scale=3.0, seed=90 + k, crack=crack)
        parts.append(b)
    for k in range(p.get("specks", 0)):
        a = rnd.uniform(0, TAU)
        d = rb + rnd.uniform(-0.3, 0.7)
        s = rnd.uniform(0.05, 0.12)
        sp = pebble(s, (d * math.cos(a), d * math.sin(a), 0.0), mat=skirt_m,
                    name="speck%d" % k, seed=200 + k, subdiv=0 if s < 0.09 else 1)
        mottle(sp, *C["skirt"], seed=300 + k)
        parts.append(sp)
    if p.get("shift"):
        place(parts, Matrix.Translation((p["shift"][0], p["shift"][1], 0.0)))
    return paint_groups(parts, p)


def block_tone(b, tone, r, crack=None, ends=None):
    """One coping block in its own grey: a luminance drawn from tone["lum"],
    a lighter top, and dark joints on the end faces that meet its neighbours
    and, by tone["edge"], along the ends of its top (ends in degrees)."""
    L = r.uniform(*tone["lum"])
    dark, light = (L * 0.8, L * 0.8, L * 0.84), (L * 1.12, L * 1.1, L * 1.08)
    top = tuple(min(tone.get("cap", 100.0), v * tone.get("top", 1.25)) for v in light)
    joint = tone["joint"]
    off = Vector((r.uniform(0, 90), r.uniform(0, 90), r.uniform(0, 90)))

    def fn(pp, co, n, li):
        c = mix(dark, light, fbm(co * 3.0 + off) - 0.25)
        if n.z > 0.6:
            c = mix(c, top, 0.6)
        radial = Vector((co.x, co.y, 0.0))
        if radial.length > 1e-6 and abs(n.z) < 0.6:
            # the end faces run along the ring's tangent, into the joint
            tang = abs(n.dot(Vector((-co.y, co.x, 0.0)) / radial.length))
            if tang > 0.6:
                c = mix(c, joint, 0.85)
        if ends and tone.get("edge"):
            a = math.degrees(math.atan2(co.y, co.x))
            near = min(abs((a - e + 180.0) % 360.0 - 180.0) for e in ends)
            if near < (ends[1] - ends[0]) / 8.0:
                c = mix(c, joint, tone["edge"])
        if crack is not None and abs(noise.noise(co * crack[0] + off * 0.7)) < crack[1]:
            c = mix(c, crack[2], 0.8)
        if n.z < -0.4:
            c = mix(c, dark, 0.6)
        return c
    return paint(b, fn)


_ALPHA = {}


def seen(p, x, y, z):
    """Whether the picture is opaque where the classic camera sees x, y, z."""
    path = p["_sprite"]
    if path not in _ALPHA:
        img = bpy.data.images.load(path)
        w, h = img.size
        px = img.pixels[:]
        _ALPHA[path] = (w, h, [[px[((h - 1 - r) * w + c) * 4 + 3] > 0.5 for c in range(w)] for r in range(h)])
    w, h, al = _ALPHA[path]
    hx, hy = p["_hotspot"]
    k = math.sin(math.atan(1.0 / hk.TILT))
    c, r = int(hx + 16 * x), int(hy - 16 * y * k - 8 * z * k)
    return 0 <= c < w and 0 <= r < h and al[r][c]


def dry_basin(p, C, rnd):
    """A drained basin: each lip stands on a drum of dry cracked stone a step
    above the last, a dark sunken pit with rubble where the centre piece
    stood, and cracks running out from it across the steps."""
    parts = []
    ci = p["coping"][0]
    dry = C["dry"]
    m = mat("drystone", brightest(dry[1], C["lip"][1]))
    crack_c = C.get("crack", (40, 34, 30))
    tiers = p.get("tiers", ())
    pit_r, pit_d = p.get("pit", (0.0, 0.0))
    # (radius, floor height, hole): every floor above the pit's floor keeps its hole
    steps_z = p.get("pool_z", 0.12)
    zin = tiers[-1][3] if tiers else floor_z
    holed = [z > zin - pit_d for z in [steps_z] + [t[3] for t in tiers]]
    steps = [(ci, steps_z, pit_r if holed[0] else 0.0)]
    for i, t in enumerate(tiers):
        steps.append((t[1], t[3], pit_r if holed[i + 1] else 0.0))
    for i, (r, z, hole) in enumerate(steps):
        top = disc_rings(r, z, seg=32, rings=3, mat=m, name="floor%d" % i, seed=i, r_in=hole, ragged=0.22)
        wall = band(r, 0.0, z, seg=32, mat=m, name="step%d" % i)
        for q in (top, wall):
            mottle(q, *dry, scale=2.4, seed=60 + i, crack=(2.6, 0.035, crack_c))
            parts.append(q)
    for i, t in enumerate(tiers):
        r_in, r_out, zt, wz = t[:4]
        arc = t[4] if len(t) > 4 and t[4] is not None else (0.0, 360.0)
        e = 0.05
        lip = ring([(r_in, wz - 0.02), (r_out, wz - 0.02), (r_out, zt - e), (r_out - e, zt), (r_in + e, zt),
                    (r_in, zt - e)], seg=40 if arc[1] - arc[0] > 359 else 16, a0=arc[0], a1=arc[1], mat=m,
                   name="lip%d" % i)
        mottle(lip, *C["lip"], scale=3.0, seed=40 + i, crack=(2.2, 0.05, crack_c))
        parts.append(smooth(lip, 35))

    def surf(r, a):
        z = steps[0][1]
        for (rr, zz, hole) in steps[1:]:
            if hole <= r <= rr:
                z = max(z, zz)
        for t in tiers:
            arc = t[4] if len(t) > 4 and t[4] is not None else (0.0, 360.0)
            ad = (math.degrees(a) - arc[0]) % 360.0
            if t[0] <= r <= t[1] and ad <= (arc[1] - arc[0]):
                z = max(z, t[2])
        return z

    if pit_r:
        # the crater runs out under the floor, whose broken edge is its rim
        pit = lathe([(0, zin - pit_d), (pit_r * 0.45, zin - pit_d + 0.02), (pit_r * 0.8, zin - pit_d * 0.55),
                     (pit_r + 0.06, zin - 0.12), (pit_r + 0.3, zin - 0.06)], seg=24, mat=m, name="pit")
        for v in pit.data.vertices:
            v.co += Vector(noise.noise_vector(v.co * 4.0 + Vector((9, 1, 0)))) * Vector((0.06, 0.06, 0.02))
        pit.data.update()
        face_up(pit)
        pal = C.get("pit", ((16, 17, 12), (46, 43, 36)))
        paint(pit, lambda pp, co, nn, li: mix(pal[0], pal[1], (math.hypot(co.x, co.y) / pit_r) ** 2
                                              + 0.3 * noise.noise(co * 5.0)))
        parts.append(smooth(pit, 50))
        rm = mat("rubble", brightest(dry[1], C["lip"][1], pal[1]))
        for k in range(p.get("pit_rubble", 7)):
            a = rnd.uniform(0, TAU)
            d = rnd.uniform(0.1, pit_r + 0.25)
            sz = rnd.uniform(0.1, 0.24)
            z = (zin - pit_d * (1 - (d / pit_r) ** 2) if d < pit_r else zin) - 0.03
            q = mk.rock(sz * rnd.uniform(1.0, 1.5), sz, sz * rnd.uniform(0.5, 0.8), cuts=1, rough=sz * 0.12,
                        seed=500 + k, mat=rm, name="pitrubble%d" % k, taper=0.15)
            mk.xform(q, rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), rnd.uniform(0, 180)),
                     loc=(d * math.cos(a), d * math.sin(a), z))
            dk = rnd.random() < 0.5
            mottle(q, *(pal if dk else dry), scale=4.0, seed=510 + k)
            parts.append(q)

    # cracks: dark strips draped over the steps, out from the pit
    cm = mat("crack", crack_c, rough=1.0)
    for k in range(p.get("cracks", 0)):
        a0 = TAU * k / p["cracks"] + rnd.uniform(-0.3, 0.3)
        r0 = max(0.2, pit_r - 0.05)
        r1 = rnd.uniform(ci * 0.55, ci - 0.05)
        pts, wid = [], []
        n = max(4, int((r1 - r0) / 0.1))
        a = a0
        for i in range(n + 1):
            r = r0 + (r1 - r0) * i / n
            a += rnd.uniform(-0.09, 0.09) / max(r, 0.5)
            pts.append((r * math.cos(a), r * math.sin(a), surf(r, a) + 0.012))
            wid.append(p.get("crack_w", 0.075) * (1 - i / n) ** 1.3 + 0.012)
        parts.append(flat(ribbon(pts, wid, mat=cm, name="crack%d" % k), crack_c))
        if rnd.random() < 0.6:
            # a branch off the crack
            j = rnd.randrange(n // 3, max(n // 3 + 1, 2 * n // 3))
            bx, by, _ = pts[j]
            rr, aa = math.hypot(bx, by), math.atan2(by, bx) + rnd.choice((-1, 1)) * 0.35
            bp, bw = [], []
            for i in range(6):
                r = rr + 0.1 * i
                aa += rnd.uniform(-0.1, 0.1) / max(r, 0.5)
                bp.append((r * math.cos(aa), r * math.sin(aa), surf(r, aa) + 0.012))
                bw.append(0.045 * (1 - i / 5) + 0.008)
            parts.append(flat(ribbon(bp, bw, mat=cm, name="crackb%d" % k), crack_c))
    return parts


def paint_groups(parts, p):
    """Project-paints the parts named in p["painted"] (by name prefix) with
    the original picture: brick paving, foam and such that geometry cannot
    carry. Each keeps its own object, is marked, and loses its vertex colours
    so the game shows the picture's colours as they are."""
    groups = tuple(p.get("painted", ()))
    if not groups:
        return parts
    # The pictures were drawn by a true tilted camera, a tenth shorter than
    # the oblique rule carve.paint maps by, so paint a copy squeezed to match.
    k = math.sin(math.atan(1.0 / hk.TILT))
    for q in parts:
        if q.name.startswith(groups):
            at = q.data.color_attributes.get("Col")
            if at is not None:
                q.data.color_attributes.remove(at)
            mk.matrix(q, Matrix.Diagonal((1.0, k, k, 1.0)))
            hk.project_paint(q, p["_sprite"], p["_hotspot"], p["_footprint"])
            mk.matrix(q, Matrix.Diagonal((1.0, 1.0 / k, 1.0 / k, 1.0)))
            if p.get("paint_tint"):
                tint(q.data.materials[0], p["paint_tint"])
            q["painted"] = True
    return parts


def tint(m, k):
    """Multiplies a painted material's picture by k (glTF keeps it as the
    base colour factor, so a repaint from the player's files keeps it)."""
    nt = m.node_tree
    if any(n.label == "tint" for n in nt.nodes):
        return m
    b = nt.nodes["Principled BSDF"]
    src = b.inputs["Base Color"].links[0].from_socket
    mx = nt.nodes.new("ShaderNodeMix")
    mx.label = "tint"
    mx.data_type, mx.blend_type = "RGBA", "MULTIPLY"
    mx.inputs["Factor"].default_value = 1.0
    nt.links.new(src, mx.inputs[6])
    mx.inputs[7].default_value = (k, k, k, 1.0)
    nt.links.new(mx.outputs[2], b.inputs["Base Color"])
    return m


def serpent_statue(p, C, fallen):
    """A bronze sea serpent coiled up a short pedestal, head raised; fallen,
    its body lies across the basin and over the rim with a stub left."""
    s = p["statue"]
    bronze = mat("bronze", C["bronze"][1], rough=0.55)
    parts = []
    ped_r, ped_z0, ped_z1 = s["pedestal"]
    top = ped_z1 if not fallen else ped_z0 + (ped_z1 - ped_z0) * 0.55
    if fallen and s.get("stub"):
        # what is left standing in the pit
        ped_z0, top = s["stub"]
    ped = lathe([(0, ped_z0), (ped_r * 1.25, ped_z0), (ped_r * 1.25, ped_z0 + 0.1), (ped_r, ped_z0 + 0.2),
                 (ped_r * 0.8, top - 0.12), (ped_r * 1.1, top - 0.05), (ped_r * 1.1, top), (0, top)],
                seg=12 if fallen else 16, mat=bronze, name="pedestal")
    mottle(ped, *C["bronze"], scale=4.0, seed=3)
    if fallen:
        for v in ped.data.vertices:
            if v.co.z > top - 0.13:
                v.co.z += noise.noise(v.co * 7.0) * 0.12
    if fallen and s.get("stub") is False:
        bpy.data.objects.remove(ped, do_unlink=True)
    else:
        parts.append(smooth(ped, 45))
    key = "fallen_path" if fallen else "path"
    path = catmull(s[key], sub=3)
    n = len(path)
    r0, rt, rh = s["radius"], s.get("tail_r", 0.05), s.get("head_r", s["radius"] * 0.8)
    if s.get(key + "_r"):
        radii = along(s[key + "_r"], 3)
    else:
        radii = []
        for i in range(n):
            t = i / (n - 1)
            body = rt + (r0 - rt) * min(1.0, t / 0.35)
            if t > 0.8:
                body = r0 + (rh - r0) * (t - 0.8) / 0.2
            radii.append(body)
    body = sweep_tube(path, radii, seg=8, mat=bronze, name="serpent")
    hi = C["bronze"][1]

    lit = s.get("lit", (0.35, 0.5))

    def scales(pp, co, nn, li):
        return mix(C["bronze"][0], hi, lit[0] + lit[1] * max(0.0, nn.z) + 0.25 * noise.noise(co * 9.0))
    paint(body, scales)
    parts.append(smooth(body, 60))
    tail = path
    if fallen and s.get("coil"):
        # more of the body lying along the rim, ending in the tail
        tail = catmull(s["coil"], sub=3)
        cl = sweep_tube(tail, along(s["coil_r"], 3), seg=8, mat=bronze, name="coil")
        parts.append(smooth(paint(cl, scales), 60))
        tail = list(reversed(tail))
    H = Vector(path[-1])
    D = (Vector(path[-1]) - Vector(path[-3])).normalized()
    if fallen and s.get("head"):
        # the fallen head: a rounded, lumpy mass on the ground past the rim
        hr, hc = s["head"]
        hd = mk.ico(1.0, subdiv=2, mat=bronze, name="head")
        for v in hd.data.vertices:
            v.co = Vector((v.co.x * hr * 1.25, v.co.y * hr, v.co.z * hr * 0.85))
            v.co += v.co.normalized() * noise.noise(v.co * 3.5 + Vector((4, 2, 0))) * hr * 0.22
        hd.data.update()
        mk.xform(hd, rot=(0, 0, math.degrees(math.atan2(D.y, D.x))), loc=tuple(hc))
        paint(hd, lambda pp, co, nn, li: mix(C["head"][0], C["head"][1],
                                             0.2 + 0.4 * max(0.0, nn.z) + 0.35 * noise.noise(co * 6.0)))
        parts.append(smooth(hd, 70))
    else:
        # the head: a snout along the last stretch, bent down, and a lower jaw
        snout_dir = (D + Vector((0, 0, -0.5))).normalized()
        sn = rod(tuple(H), tuple(H + snout_dir * s.get("snout", 0.55)), rh * 0.9, rh * 0.35, seg=7, mat=bronze,
                 name="snout")
        jaw_dir = (D + Vector((0, 0, -1.3))).normalized()
        jw = rod(tuple(H - D * 0.05 - Vector((0, 0, rh * 0.4))), tuple(H + jaw_dir * s.get("snout", 0.55) * 0.75),
                 rh * 0.5, rh * 0.2, seg=5, mat=bronze, name="jaw")
        parts += [paint(smooth(sn, 50), scales), paint(smooth(jw, 50), scales)]
    # a crest of spines along the back
    for i in range(3, n - 2, 2 if not fallen else 3):
        P = Vector(path[i])
        T = (Vector(path[i + 1]) - Vector(path[i - 1])).normalized()
        U = (Vector((0, 0, 1)) - T * T.z).normalized()
        sp = rod(tuple(P + U * radii[i] * 0.7), tuple(P + U * (radii[i] + 0.22) - T * 0.12), 0.06, 0.005, seg=3,
                 mat=bronze, name="spine%d" % i)
        parts.append(paint(sp, lambda pp, co, nn, li: C["bronze"][0]))
    # the tail fin: two flat lobes
    if fallen and not s.get("coil"):
        return parts
    T0 = Vector(tail[0])
    T1 = (Vector(tail[0]) - Vector(tail[2])).normalized()
    side = T1.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))
    fin = mk.mesh("fin", [tuple(T0), tuple(T0 + T1 * 0.45 + side * 0.3 + Vector((0, 0, 0.15))),
                          tuple(T0 + T1 * 0.25), tuple(T0 + T1 * 0.45 - side * 0.3 + Vector((0, 0, 0.15)))],
                  [(0, 1, 2), (0, 2, 3)], bronze)
    parts.append(paint(fin, lambda pp, co, nn, li: C["bronze"][0]))
    return parts


def orb_spout(p, C, broken):
    """A green bronze orb on a short turned stand; broken, a split hollow
    bowl lying tilted in the dry basin."""
    o = p["orb"]
    bronze = mat("verdigris", C["orb"][1], rough=0.6)
    parts = []
    r = o["r"]
    if not broken:
        z0, z1 = o["stand_z0"], o["stand_z1"]
        st = lathe([(0, z0), (r * 0.9, z0), (r * 0.7, z0 + 0.1), (r * 0.35, z0 + 0.2), (r * 0.3, z1 - 0.1),
                    (r * 0.55, z1), (0, z1)], seg=16, mat=bronze, name="stand")
        orb = pebble(r, (0, 0, z1 - 0.05), scale=(1, 1, 1), mat=bronze, name="orb", subdiv=2, rough=0.02)
        for q in (st, orb):
            mottle(q, *C["orb"], scale=5.0, seed=11)
            parts.append(smooth(q, 50))
        return parts
    rows = 6
    outline = []
    for i in range(rows + 1):
        a = math.pi / 2 * i / rows
        outline.append((r * math.sin(a) + 0.001, r * (1 - math.cos(a))))
    inner = [(max(0.001, x - 0.07), z + 0.06) for (x, z) in reversed(outline)]
    shell = ring(outline + inner, seg=12, a0=o.get("a0", 20), a1=o.get("a1", 280), mat=bronze, name="bowl")
    for v in shell.data.vertices:
        if v.co.z > r * 0.8:
            v.co.z -= abs(noise.noise(v.co * 6.0)) * 0.25
    mk.xform(shell, rot=o.get("tilt", (25, 0, 30)), loc=o.get("loc", (0, 0, p.get("floor_z", 0.1))))
    mottle(shell, *C["orb"], scale=5.0, seed=12)
    parts.append(smooth(shell, 50))
    return parts


# ---------------------------------------------------------------- brazier

def shard(rc, top, a, hgt, lean, rnd, m, name):
    """A jagged shard of a broken column's wall: a curved plate standing on
    the rim at angle a (degrees), leaning out by lean as it rises hgt, its
    tip drifting sideways and narrowing to a point."""
    g = math.radians(a)
    half = rnd.uniform(0.25, 0.6)  # half its width, in radians of the rim
    t = 0.07
    secs = []
    for f, wk in ((0.0, 1.0), (0.5, rnd.uniform(0.6, 0.85)), (1.0, rnd.uniform(0.3, 0.5))):
        c = g + rnd.uniform(-0.25, 0.25) * f
        r = rc + lean * f ** 1.3
        z = top - 0.08 + hgt * f
        # the top edge is ragged: its three points at different heights
        jag = [rnd.uniform(-0.45, 0.0) * hgt * f for _ in range(3)]
        jag[rnd.randrange(3)] = 0.0
        ring_ = []
        for u, dz in zip((-1.0, 0.0, 1.0), jag if f == 1.0 else (0.0, 0.0, 0.0)):
            aa = c + u * half * wk
            ring_.append((r, aa, z + dz))
        secs.append(ring_)
    verts = []
    for sec in secs:
        for (r, aa, z) in sec:
            verts.append((r * math.cos(aa), r * math.sin(aa), z))
        for (r, aa, z) in reversed(sec):
            verts.append(((r - t) * math.cos(aa), (r - t) * math.sin(aa), z))
    n = 6
    faces = []
    for i in range(len(secs) - 1):
        for j in range(n):
            j2 = (j + 1) % n
            faces.append((i * n + j, i * n + j2, (i + 1) * n + j2, (i + 1) * n + j))
    faces.append(tuple(range(n - 1, -1, -1)))
    faces.append(tuple((len(secs) - 1) * n + j for j in range(n)))
    ob = mk.mesh(name, verts, faces, m)
    mk._fix_normals(ob)
    return ob


def bowl_fragment(x, y, z, rot, arc, r, mat_=None, name="frag"):
    """A curved piece of the broken bowl: arc degrees of its shallow dish of
    radius r, turned (degrees) and set down with its middle at x, y, z."""
    outline = [(r * 0.45, 0.0), (r * 0.8, 0.07), (r, 0.16), (r * 1.03, 0.2), (r * 0.97, 0.21),
               (r * 0.78, 0.12), (r * 0.44, 0.05)]
    fr = ring(outline, seg=5, a0=-arc / 2, a1=arc / 2, mat=mat_, name=name)
    for v in fr.data.vertices:
        v.co.x -= r * 0.75
        v.co += noise.noise_vector(v.co * 5.0 + Vector((x, y, 0))) * 0.02
    fr.data.update()
    return mk.xform(fr, rot=rot, loc=(x, y, z))


def brazier(p):
    """A standing brazier: a round stone base, a dark turned column with a
    knob, a wide shallow bowl with a lip, coals and a tall flame; broken, a
    jagged column stub on the base."""
    C = p["colours"]
    parts = []
    base_m = mat("base", C["base"][1])
    col_m = mat("column", C["collar"][1] if C.get("collar") else C["column"][1], rough=0.6)
    rb, hb = p["base"]
    base = lathe([(0, 0), (rb, 0), (rb, 0.1), (rb * 0.92, 0.18), (rb * 0.72, 0.24), (rb * 0.66, 0.34),
                  (rb * 0.5, 0.4), (rb * 0.45, hb), (0, hb)], seg=24, mat=base_m, name="base")
    mottle(base, *C["base"], scale=4.0, seed=1, top=C.get("base_top"), moss=C.get("base_moss"))
    parts.append(smooth(base, 35))
    rc = p["column_r"]
    kz, kr = p["knob"]
    top = p["column_top"]
    # a taper swells the column's foot, a baluster's lower third
    tp = p.get("column_taper", 1.0)
    prof = [(0, hb - 0.05), (rc * 1.25 * tp, hb - 0.05), (rc * 1.25 * tp, hb + 0.12), (rc * tp, hb + 0.22),
            (rc * (1 + (tp - 1) * 0.4), hb + (kz - hb) * 0.55), (rc, kz - 0.35),
            (kr * 0.85, kz - 0.22), (kr, kz), (kr * 0.85, kz + 0.22), (rc, kz + 0.35)]
    if p.get("broken"):
        # the stub is hollow: a dark socket inside a ragged rim
        prof += [(rc * 1.02, top), (rc * 0.72, top - 0.02), (rc * 0.6, top - 0.3), (0, top - 0.32)]
    else:
        prof += [(rc, top - 0.3), (rc * 1.25, top - 0.15), (rc * 1.6, top), (0, top)]
    col = lathe(prof, seg=16, mat=col_m, name="column")
    if p.get("broken"):
        for v in col.data.vertices:
            if v.co.z > top - 0.05:
                v.co.z += noise.noise(v.co * 6.0 + Vector((p.get("seed", 4), 0, 0))) * 0.12
        col.data.update()

    def band(co):
        # a pale stone collar at the knob where the picture has one, a black socket
        if C.get("collar") and abs(co.z - kz) <= 0.3:
            return C["collar"]
        if p.get("broken") and co.z > top - 0.4 and math.hypot(co.x, co.y) < rc * 0.9:
            return ((6, 6, 5), (20, 18, 15))
        return None
    mottle(col, *C["column"], scale=5.0, seed=2, band=band)
    parts.append(smooth(col, 40))
    if p.get("broken"):
        # a jagged break: shards of the column wall splaying out round the rim
        rnd = random.Random(p.get("seed", 4))
        shard_c = C.get("shard", C["column"])
        for k, (a, hgt, lean) in enumerate(p["shards"]):
            sh = shard(rc, top, a, hgt, lean, rnd, col_m, "shard%d" % k)
            mottle(sh, *shard_c, scale=6.0, seed=20 + k)
            parts.append(sh)
        bowl_m = mat("bowl", C["bowl"][1], rough=0.55)
        for k, f in enumerate(p.get("bowl_frags", ())):
            fr = bowl_fragment(*f, mat_=bowl_m, name="frag%d" % k)
            mottle(fr, *C.get("frag", C["bowl"]), scale=5.0, seed=50 + k, top=C.get("frag", C["bowl"])[1])
            parts.append(smooth(fr, 45))
        for k, (x, y, s) in enumerate(p.get("chips", ())):
            ch = mk.rock(s, s * 0.7, s * 0.4, cuts=0, rough=s * 0.2, seed=30 + k, mat=col_m, name="chip%d" % k)
            mk.xform(ch, rot=(0, 0, rnd.uniform(0, 180)), loc=(x, y, 0.0))
            mottle(ch, *C["column"], seed=40 + k)
            parts.append(ch)
        return parts
    zr, rr, depth = p["bowl"]
    bowl_m = mat("bowl", C["bowl"][1], rough=0.55)
    zb = top
    bowl = lathe([(0, zb - 0.02), (rc * 1.6, zb - 0.02), (rr * 0.5, zr - depth * 0.55), (rr * 0.85, zr - depth * 0.2),
                  (rr, zr - 0.05), (rr * 1.02, zr + 0.04), (rr * 0.97, zr + 0.08), (rr * 0.9, zr + 0.02),
                  (rr * 0.6, zr - depth * 0.45), (rr * 0.25, zr - depth * 0.8), (0, zr - depth * 0.85)],
                 seg=32, mat=bowl_m, name="bowl")

    def bowl_col(pp, co, nn, li):
        rad = math.hypot(co.x, co.y)
        if nn.z > 0.3 and rad < rr * 0.92:
            return mix(C["dish"][0], C["dish"][1], 0.45 + 0.3 * rad / rr + 0.6 * noise.noise(co * 6.0))
        return mix(C["bowl"][0], C["bowl"][1], 0.5 + 0.5 * noise.noise(co * 3.0) + 0.3 * nn.z)
    paint(bowl, bowl_col)
    parts.append(smooth(bowl, 35))
    # coals heaped in the dish
    ember = glow_mat("ember", (236, 120, 40), rough=0.8, emit=(1.0, 0.3, 0.05), strength=1.5)
    coal = mat("coal", (70, 50, 40), rough=1.0)
    rnd = random.Random(7)
    for k in range(p.get("coals", 14)):
        a = rnd.uniform(0, TAU)
        d = rnd.uniform(0, rr * 0.45)
        z = zr - depth * 0.8 + (1 - d / (rr * 0.5)) * depth * 0.35
        hot = rnd.random() < 0.5
        c = pebble(rnd.uniform(0.1, 0.18), (d * math.cos(a), d * math.sin(a), z), scale=(1, 1, 0.6),
                   mat=ember if hot else coal, name="coal%d" % k, seed=k, subdiv=0)
        if hot:
            paint(c, lambda pp, co, nn, li: mix((150, 40, 10), (236, 120, 40), rnd.random()))
            mk.up_normals(c)
        else:
            paint(c, lambda pp, co, nn, li: mix((24, 18, 14), (70, 50, 40), rnd.random()))
        parts.append(c)
    fh, fw = p["flame"]
    parts += blaze((0.0, 0.0, zr - depth * 0.55), fh, fw, seed=p.get("seed", 5), count=p.get("tongues", 5),
                   spread=fw * p.get("spread", 0.8), lean=p.get("lean", (0.05, 0.05)), taper=p.get("taper", 0.7),
                   side=p.get("side", (0.2, 0.45)))
    return parts


# ---------------------------------------------------------------- bonfire

def bonfire(p):
    """A ring of rounded stones round a trodden pit, logs laid in a star
    leaning in to a low peak, charred toward the middle; lit, a glowing
    bed and a tall flame; burnt out, black logs in grey ash."""
    C = p["colours"]
    rnd = random.Random(p.get("seed", 1))
    parts = []
    stone_m = mat("stone", C["stone"][1])
    earth_m = mat("earth", C["earth"][1], rough=1.0)
    cx, cy = p.get("centre", (0.0, 0.0))
    R, n, s = p["ring"]
    for k in range(n):
        a = TAU * k / n + rnd.uniform(-0.08, 0.08)
        rr = R + rnd.uniform(-0.08, 0.08)
        sz = s * rnd.uniform(0.8, 1.15)
        st = pebble(sz, (cx + rr * math.cos(a), cy + rr * math.sin(a), 0.0),
                    scale=(rnd.uniform(1.0, 1.3), rnd.uniform(0.8, 1.0), rnd.uniform(0.6, 0.8)),
                    rot=math.degrees(a) + rnd.uniform(-20, 20), mat=stone_m, name="stone%d" % k, seed=k, rough=0.18)
        mottle(st, *C["stone"], scale=4.0, seed=k, foot=C["stone"][0], foot_h=0.12)
        parts.append(smooth(st, 60))
    pit = disc_rings(R - s * 0.3, 0.02, seg=24, rings=4, mat=earth_m, name="pit", cx=cx, cy=cy, wobble=0.05, seed=3)

    def pit_col(pp, co, nn, li):
        # trodden dark earth, scorched in the middle, redder toward the stones
        d = math.hypot(co.x - cx, co.y - cy) / R
        return mix(C["earth"][0], C["earth"][1], d ** 2.5 + 0.25 * noise.noise(co * 3.0))
    paint(pit, pit_col)
    parts.append(pit)
    bark_m = mat("bark", C["bark"][1], rough=0.9)
    L = p["logs"]
    nl, r_out, apex, lr = L["count"], L["r_out"], L["apex"], L["r"]
    for k in range(nl):
        a = TAU * k / nl + rnd.uniform(-0.12, 0.12) + L.get("phase", 0.0)
        ro = r_out * rnd.uniform(0.85, 1.05)
        ri = rnd.uniform(0.05, 0.25)
        zi = apex * rnd.uniform(0.75, 1.0)
        p0 = (cx + ro * math.cos(a), cy + ro * math.sin(a), lr * 0.8)
        p1 = (cx + ri * math.cos(a + 0.3), cy + ri * math.sin(a + 0.3), zi)
        parts.append(log(p0, p1, lr * rnd.uniform(0.9, 1.15), lr * 0.75, C["bark"][1], C["bark"][0], seed=k,
                         char=(C["char"], L.get("char_from", 0.45)), mat_=bark_m, end=C.get("end", (150, 120, 84)),
                         name="log%d" % k))
    for k in range(p.get("sticks", 0)):
        a = rnd.uniform(0, TAU)
        ro = r_out * rnd.uniform(0.5, 0.9)
        p0 = (cx + ro * math.cos(a), cy + ro * math.sin(a), 0.05)
        b = a + rnd.uniform(1.5, 2.5)
        p1 = (cx + 0.35 * math.cos(b), cy + 0.35 * math.sin(b), apex * rnd.uniform(0.4, 0.7))
        parts.append(log(p0, p1, lr * 0.5, lr * 0.4, C["bark"][1], C["bark"][0], seed=50 + k,
                         char=(C["char"], 0.3), mat_=bark_m, seg=5, name="stick%d" % k))
    if p.get("ash"):
        ash_m = mat("ash", C["ash"][1], rough=1.0)
        for k in range(p["ash"]):
            a = rnd.uniform(0, TAU)
            d = rnd.uniform(0, r_out * 0.8)
            q = pebble(rnd.uniform(0.12, 0.25), (cx + d * math.cos(a), cy + d * math.sin(a), 0.0),
                       scale=(1.3, 1, 0.35), mat=ash_m, name="ash%d" % k, seed=400 + k, subdiv=1)
            mottle(q, *C["ash"], scale=5.0, seed=k)
            parts.append(q)
    if p.get("flame"):
        fh, fw = p["flame"]
        ember = glow_mat("ember", (240, 140, 50), rough=0.8, emit=(1.0, 0.3, 0.05), strength=1.5)
        glow = pebble(0.55, (cx, cy, 0.0), scale=(1.1, 1.0, 0.55), mat=ember, name="glow", seed=9, rough=0.2)
        paint(glow, lambda pp, co, nn, li: mix((190, 70, 20), (240, 140, 50), 0.5 + 0.5 * noise.noise(co * 5.0)))
        parts.append(mk.up_normals(glow))
        off = p.get("flame_off", (0.0, 0.0))
        parts += blaze((cx + off[0], cy + off[1], apex * 0.45), fh, fw, seed=p.get("seed", 1) + 3,
                       count=p.get("tongues", 6), spread=fw * p.get("spread", 1.0), lean=p.get("lean", (0.05, 0.1)),
                       taper=p.get("taper", 0.7), side=p.get("side", (0.15, 0.4)))
    return parts


# ---------------------------------------------------------------- altar

def gridbox(sx, sy, sz, step=0.2, mat=None, name="block", bottom=False, hidden=0.45):
    """A box on z=0 centred on x, y, its top and front cut into a grid about
    step wide (the faces the classic camera hardly sees, hidden wide) so
    vertex colours can mottle it and darken its edges."""
    faces_def = [((-sx / 2, -sy / 2, sz), (sx, 0, 0), (0, sy, 0)),    # top
                 ((-sx / 2, -sy / 2, 0), (sx, 0, 0), (0, 0, sz)),     # front
                 ((sx / 2, sy / 2, 0), (-sx, 0, 0), (0, 0, sz)),      # back
                 ((-sx / 2, sy / 2, 0), (0, -sy, 0), (0, 0, sz)),     # left
                 ((sx / 2, -sy / 2, 0), (0, sy, 0), (0, 0, sz))]      # right
    if bottom:
        faces_def.append(((-sx / 2, sy / 2, 0), (sx, 0, 0), (0, -sy, 0)))
    verts, faces = [], []
    for f, (o, u, v) in enumerate(faces_def):
        U, V, O = Vector(u), Vector(v), Vector(o)
        st_ = step if f < 2 else hidden
        nu, nv = max(1, round(U.length / st_)), max(1, round(V.length / st_))
        base = len(verts)
        for j in range(nv + 1):
            for i in range(nu + 1):
                verts.append(tuple(O + U * (i / nu) + V * (j / nv)))
        for j in range(nv):
            for i in range(nu):
                a = base + j * (nu + 1) + i
                faces.append((a, a + 1, a + nu + 2, a + nu + 1))
    ob = mk.mesh(name, verts, faces, mat)
    ob["size"] = (sx, sy, sz)
    return ob


def block_paint(ob, C, pal, tone, seed, at=(0, 0, 0), moss=None, edge=0.06, hole=None):
    """Paints a fresh gridbox in its own frame: a wide mottle between the
    palette's dark and light (top faces from 'top', the rest from its own
    'side'), edges darkened toward the joint colour, moss patches on tops
    (world-coherent, at is the block's place) and the basin's dark walls."""
    sx, sy, sz = ob["size"]
    r = random.Random(seed)
    off = Vector((r.uniform(0, 90), r.uniform(0, 90), r.uniform(0, 90)))
    joint = C["joint"]
    A = Vector(at)

    def fn(p, co, n, li):
        w = co + A
        t = 0.5 + 2.2 * (fbm(co * 2.8 + off) - 0.5)
        if n.z > 0.6:
            c = mix(*pal["top"], t)
            d = min(co.x + sx / 2, sx / 2 - co.x, co.y + sy / 2, sy / 2 - co.y)
        else:
            c = mix(*pal["side"], t)
            if abs(n.y) > abs(n.x):
                d = min(co.x + sx / 2, sx / 2 - co.x, co.z, sz - co.z)
            else:
                d = min(co.y + sy / 2, sy / 2 - co.y, co.z, sz - co.z)
        c = tuple(v * tone for v in c)
        if moss is not None and n.z > 0.6:
            m = noise.noise(w * 1.6 + Vector((7, 3, 1))) + 0.4 * noise.noise(w * 4.5 + Vector((1, 9, 2)))
            if m > moss[1]:
                c = mix(c, mix(*moss[0], 0.5 + 0.5 * noise.noise(w * 6.0)), min(1.0, (m - moss[1]) * 4.0))
        if d < edge:
            c = mix(c, joint, 0.75 * (1.0 - d / edge))
        if hole is not None and n.z < 0.6 and abs(w.x) < hole + 0.02 and abs(w.y) < hole + 0.02:
            c = mix(c, C["basin"], 0.7)
        return c
    return paint(ob, fn)


def altar(p):
    """A long low slab laid in two rows of big blocks with broken joints on
    a plinth course, a square basin sunk into the middle of its top, and
    taller square piers near both ends on base blocks; moss and an offering
    where the picture has them. The blocks are cut into grids so each can
    carry its own mottle and dark edges, over a dark core that fills the
    joints."""
    C = p["colours"]
    rnd = random.Random(p.get("seed", 2))
    parts = []
    st = mat("stone", brightest(*C["top"], *C["side"], *C["pier_top"], *C["pier_side"], *C["moss"]))
    L, D, H = p["size"]
    pw, px = p["pier_w"], p["pier_x"]
    moss = (C["moss"], p["moss"]) if p.get("moss") else None
    body = {"top": C["top"], "side": C["side"]}
    pier_pal = {"top": C["pier_top"], "side": C["pier_side"]}
    plinth_pal = {"top": C["side"], "side": C["side"]}
    tlo, thi = p.get("tones", (0.9, 1.1))

    def place_block(sx, sy, sz, loc, rot, pal, name, hole=None):
        b = gridbox(sx, sy, sz, mat=st, name=name)
        block_paint(b, C, pal, rnd.uniform(tlo, thi), rnd.randrange(9999), at=loc, moss=moss, hole=hole)
        parts.append(mk.xform(b, rot=rot, loc=loc))

    place_block(L, D + 0.1, 0.3, (0, 0, 0), (0, 0, 0), plinth_pal, "plinth")
    core = gridbox(L - 0.2, D - 0.2, H - 0.3 - 0.05, step=0.5, mat=st, name="core")
    parts.append(mk.xform(flat(core, C["joint"]), loc=(0, 0, 0.3)))
    rows = p["joints"]
    dd = D / len(rows)
    bw = p.get("basin", 0.0) / 2
    for r_i, cuts in enumerate(rows):
        y0, y1 = -D / 2 + dd * r_i + 0.02, -D / 2 + dd * (r_i + 1) - 0.02
        xs = [-L / 2 + 0.05] + [(-L / 2 + L * c) for c in cuts] + [L / 2 - 0.05]
        for i in range(len(xs) - 1):
            a, b = xs[i] + 0.022, xs[i + 1] - 0.022
            hh = H - 0.3 + rnd.uniform(-0.04, 0.03)
            rot = (0, 0, rnd.uniform(-0.6, 0.6))
            pieces = [(a, b, y0, y1)]
            if bw and a < bw and b > -bw:
                # the basin is cut into the blocks it falls on
                pieces = [(a, -bw, y0, y1), (bw, b, y0, y1)]
                ya, yb = (y0, -bw) if y1 <= 0.03 else (bw, y1)
                pieces.append((max(a, -bw), min(b, bw), ya, yb))
            for k, (qa, qb, qy0, qy1) in enumerate(pieces):
                if qb - qa < 0.05 or qy1 - qy0 < 0.05:
                    continue
                place_block(qb - qa, qy1 - qy0, hh, ((qa + qb) / 2, (qy0 + qy1) / 2, 0.3), rot, body,
                            "slab%d_%d_%d" % (r_i, i, k), hole=bw or None)
    if bw:
        depth = p.get("basin_depth", 0.3)
        well = gridbox(2 * bw, 2 * bw, H - 0.3 - depth, step=0.2, mat=st, name="well")
        parts.append(mk.xform(flat(well, C["basin"]), loc=(0, 0, 0.3)))
    fb = p.get("pier_front", 0.0)
    for side, ph in ((-1, p["pier_h"][0]), (1, p["pier_h"][1])):
        x = side * px
        place_block(pw + 0.14, D + 0.14 + fb, 0.34, (x, -fb / 2, 0), (0, 0, 0), pier_pal, "pierbase")
        place_block(pw, D, ph - 0.34 - 0.14, (x, 0, 0.34), (0, 0, rnd.uniform(-1, 1)), pier_pal, "pier")
        place_block(pw + 0.1, D + 0.1, 0.16, (x, 0, ph - 0.16), (0, 0, 0), pier_pal, "piercap")
    for k, o in enumerate(p.get("offerings", ())):
        x, y, s = o
        pot_m = mat("pot", C["pot"][1])
        pot = lathe([(0, 0), (s * 0.6, 0), (s, s * 0.6), (s * 0.8, s * 1.1), (s * 0.7, s * 1.25), (0, s * 1.2)],
                    seg=10, mat=pot_m, name="pot%d" % k)
        mk.xform(pot, loc=(x, y, 0))
        mottle(pot, *C["pot"], scale=6.0, seed=60 + k)
        parts.append(smooth(pot, 50))
    return parts


# ---------------------------------------------------------------- cart

def plank_wheel(R, t, C, mats, planks=3, gap=0.022, tyre=0.045, hub=(0.12, 0.1), battens=True, seed=0, seg=18):
    """A solid wheel of upright planks cut round, with two battens across
    them, an iron tyre and a hub, in its own frame: centre at the origin,
    axle along Y, outer face toward -Y."""
    wood_m, iron_m = mats
    rnd = random.Random(seed)
    Ri = R - tyre
    parts = []
    w = 2 * Ri / planks
    for k in range(planks):
        u0 = -Ri + k * w + gap / 2
        u1 = -Ri + (k + 1) * w - gap / 2
        xs = [u0 + (u1 - u0) * i / 4 for i in range(5)]
        top = [(x, math.sqrt(max(0.0, Ri * Ri - x * x))) for x in xs]
        poly = top + [(x, -y) for (x, y) in reversed(top)]
        n = len(poly)
        verts = [(x, y, t / 2) for (x, y) in poly] + [(x, y, -t / 2) for (x, y) in poly]
        faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
        faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        ob = mk.mesh("wplank%d" % k, verts, faces, wood_m)
        mk._fix_normals(ob)
        grain(ob, C["wood"][0], C["wood"][1], (0, 1, 0), seed=rnd.randint(0, 999), knots=0.1)
        parts.append(ob)
    if battens:
        for s in (-1, 1):
            b = box(Ri * 1.55, 0.1, 0.035, loc=(0, s * Ri * 0.42, t / 2), mat=wood_m, name="batten")
            grain(b, C["wood"][0], C["wood"][1], (1, 0, 0), seed=rnd.randint(0, 999))
            parts.append(b)
    ty = ring([(Ri - 0.004, -t / 2 - 0.006), (R, -t / 2 - 0.006), (R, t / 2 + 0.006), (Ri - 0.004, t / 2 + 0.006)],
              seg=seg, mat=iron_m, name="tyre")
    mottle(ty, C["iron"][0], C["iron"][1], scale=6.0, seed=rnd.randint(0, 99))
    parts.append(smooth(ty, 30))
    hb = hk.cylinder(hub[0], t + 0.05 + hub[1], seg=8, z=-t / 2 - 0.05, mat=wood_m, name="hub")
    grain(hb, C["wood"][0], C["wood"][1], (0, 0, 1), seed=rnd.randint(0, 999), end=C["wood"][0])
    parts.append(hb)
    cap = hk.cylinder(hub[0] * 0.55, 0.05, seg=6, z=t / 2 + hub[1], mat=iron_m, name="linchpin")
    flat(cap, C["iron"][0])
    parts.append(cap)
    return place(parts, Matrix.Rotation(math.radians(90), 4, "X"))


def straw(parts, C, m, anchors, rnd, n, length=(0.15, 0.35), width=0.035):
    """Loose stalks of straw poking out of a load: thin blades from points
    on its surface, outward along the given normals."""
    verts, faces = [], []
    for k in range(n):
        co, nrm = anchors[rnd.randrange(len(anchors))]
        d = (Vector(nrm) + Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), rnd.uniform(-0.2, 0.5))))
        d.normalize()
        s = d.cross(Vector((0, 0, 1)))
        s = s.normalized() if s.length > 1e-3 else Vector((1, 0, 0))
        L = rnd.uniform(*length)
        b = Vector(co) - d * 0.04
        i = len(verts)
        verts += [tuple(b - s * width / 2), tuple(b + s * width / 2), tuple(b + d * L + s * 0.004)]
        faces.append((i, i + 1, i + 2))
    ob = mk.mesh("straw", verts, faces, m)
    paint(ob, lambda pp, co, nn, li: mix(C["hay"][0], C["hay_hi"], rnd.uniform(0.3, 1.0)))
    parts.append(ob)


def loaf(L, W, h, blunt, x0=0.0, y0=0.0, rings=16, arc=10, m=None, name="hay"):
    """A long loaf on the bed: a full, even cross-section (steep sides, a low
    crown) down its whole length, rounded off over blunt at each end to a
    squared end face; its base at z 0, open underneath."""
    Lh = L / 2
    xs = [-Lh + L * i / rings for i in range(rings + 1)]
    xs = sorted(set(xs + [-Lh + blunt * 0.5, -Lh + blunt, Lh - blunt, Lh - blunt * 0.5]))
    verts, faces = [], []
    for x in xs:
        e = max(0.0, abs(x) - (Lh - blunt)) / blunt
        k = 0.62 + 0.38 * math.sqrt(max(0.0, 1.0 - e * e))
        for j in range(arc + 1):
            t = math.pi * j / arc
            c, s_ = math.cos(t), math.sin(t)
            verts.append((x0 + x, y0 + math.copysign(abs(c) ** 0.55, c) * W / 2 * (0.9 + 0.1 * k),
                          (s_ ** 0.7) * h * k))
    n = arc + 1
    for i in range(len(xs) - 1):
        for j in range(arc):
            a = i * n + j
            faces.append((a, a + n, a + n + 1, a + 1))
    faces.append(tuple(range(arc, -1, -1)))
    faces.append(tuple((len(xs) - 1) * n + j for j in range(n)))
    ob = mk.mesh(name, verts, faces, m)
    mk._fix_normals(ob)
    return ob


def hay_load(C, L, W, top, h, rnd, over=(1.05, 1.1), strands=70, lying=260, blunt=None, x0=0.0, y0=0.0):
    """A load of hay heaped on the bed, lumpy and streaked along the cart,
    with straws sticking out all round: an egg by default, or with blunt a
    full-width loaf with squared ends (loaf())."""
    m = mat("hay", C["hay_hi"], rough=1.0)
    off = Vector((rnd.uniform(0, 50), rnd.uniform(0, 50), 0))
    if blunt:
        ob = loaf(L * over[0], W * over[1], h, blunt, x0=x0, y0=y0, m=m)
        for v in ob.data.vertices:
            zz = min(1.0, max(0.0, v.co.z / h))
            q = Vector((v.co.x, v.co.y, top + v.co.z))
            n_ = noise.noise_vector(q * 2.2 + off)
            f = noise.noise_vector(q * 9.0 + off)
            q += Vector((n_.x * 0.06 + f.x * 0.04, n_.y * 0.05 * zz + f.y * 0.03 * zz,
                         (n_.z * 0.08 + f.z * 0.05) * zz))
            v.co = q
    else:
        ob = mk.ico(1.0, subdiv=3, mat=m, name="hay")
        for v in ob.data.vertices:
            x, y, z = v.co
            sx = math.copysign(abs(x) ** 0.6, x)
            sy = math.copysign(abs(y) ** 0.75, y)
            zz = max(z, 0.0) ** 0.7
            q = Vector((sx * L / 2 * over[0], sy * W / 2 * over[1], top + zz * h))
            n_ = noise.noise_vector(q * 2.2 + off)
            f = noise.noise_vector(q * 9.0 + off)
            q += Vector((n_.x * 0.06 + f.x * 0.04, n_.y * 0.06 + f.y * 0.04, (n_.z * 0.08 + f.z * 0.05) * zz))
            v.co = q
    ob.data.update()
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    kill = [f for f in bm.faces if all(v.co.z <= top + 1e-4 for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    # anchors: random points on the heap's faces, weighted by area
    tris_ = []
    for f in ob.data.polygons:
        vs = [ob.data.vertices[i].co for i in f.vertices]
        if min(v.z for v in vs) > top + 0.03:
            for j in range(1, len(vs) - 1):
                tris_.append((vs[0], vs[j], vs[j + 1], f.normal.copy(), f.area / (len(vs) - 2)))
    total = sum(t[4] for t in tris_)
    anchors = []
    for k in range(1200):
        u = rnd.uniform(0, total)
        for a_, b_, c_, n_, ar in tris_:
            u -= ar
            if u <= 0:
                break
        r1, r2 = rnd.random(), rnd.random()
        if r1 + r2 > 1:
            r1, r2 = 1 - r1, 1 - r2
        anchors.append((tuple(a_ + (b_ - a_) * r1 + (c_ - a_) * r2), tuple(n_)))
    hay = C["hay"]

    def fn(pp, co, nn, li):
        q = Vector((co.x * 1.5, co.y * 9.0, co.z * 9.0)) + off
        c = mix(hay[0], hay[1], 0.7 + 0.8 * noise.noise(q) + 0.4 * noise.noise(q * 2.3))
        if noise.noise(co * 5.0 + off) > 0.35:
            c = mix(c, C["hay_hi"], 0.6)
        if co.z < top + 0.12:
            c = mix(c, hay[0], 0.6)
        return c
    paint(ob, fn)
    parts = [ob]  # left faceted: the facets read as tufts of straw
    straw(parts, C, m, anchors, rnd, strands)
    # straws lying over the heap, mostly along the cart, for its streaky look
    verts, faces, cols = [], [], []
    for k in range(lying):
        co, nrm = anchors[rnd.randrange(len(anchors))]
        N = Vector(nrm)
        a = rnd.uniform(-0.7, 0.7) + (math.pi if rnd.random() < 0.5 else 0.0)
        T = Vector((math.cos(a), math.sin(a) * 0.6, 0))
        T = (T - N * T.dot(N)).normalized()
        S = N.cross(T).normalized()
        ln = rnd.uniform(0.2, 0.5)
        b = Vector(co) + N * 0.015
        i = len(verts)
        verts += [tuple(b - T * ln / 2 - S * 0.028), tuple(b - T * ln / 2 + S * 0.028), tuple(b + T * ln / 2 + N * 0.03)]
        faces.append((i, i + 1, i + 2))
        cols.append(mix(hay[0], C["hay_hi"], rnd.choice((-0.2, 0.0, 0.3, 0.7, 0.9, 1.0, 1.0))))
    ly = mk.mesh("straw_lying", verts, faces, m)
    paint(ly, lambda pp, co, nn, li: cols[pp.index])
    parts.append(ly)
    return parts


def sack(C, loc, size, rot=0.0, tilt=(0, 0), rnd=None, m=None, name="sack"):
    """A full sack lying on its side: a lumpy pillow pinched and tied at one end."""
    sx, sy, sz = size
    ob = mk.ico(1.0, subdiv=2, mat=m, name=name)
    off = Vector((rnd.uniform(0, 50), rnd.uniform(0, 50), 0))
    for v in ob.data.vertices:
        x, y, z = v.co
        pinch = 1.0 - 0.55 * max(0.0, x - 0.55) / 0.45
        z = max(z, -0.65)
        q = Vector((x * sx, y * sy * pinch, (z + 0.65) * sz / 1.65 * (pinch if z > 0 else 1.0)))
        q += noise.noise_vector(q * 4.0 + off) * 0.05
        v.co = q
    ob.data.update()
    mk.xform(ob, rot=(tilt[0], tilt[1], rot), loc=loc)
    s = C["sack"]

    def fn(pp, co, nn, li):
        c = mix(s[0], s[1], 0.45 + 1.1 * noise.noise(co * 7.0 + off) + 0.35 * nn.z)
        if noise.noise(co * 13.0 + off) > 0.3:
            c = mix(c, C["sack_hi"], 0.85)  # a fleck of the coarse weave
        if abs(noise.noise(co * 5.0 + off * 2.0)) < C.get("crease_w", 0.2):
            c = mix(c, C.get("sack_crease", s[0]), 0.9)  # a crease
        if nn.z < C.get("fold_z", 0.35):
            c = mix(c, C.get("sack_crease", s[0]), 0.75)  # the dark fold where two sacks meet
        return c
    paint(ob, fn)
    return smooth(ob, 70)


def tuft(C, loc, rnd, m, blades=6, h=(0.3, 0.6), name="tuft"):
    """A clump of long grass blades arching out from one root."""
    parts = []
    g = C["grass"]
    for k in range(blades):
        a = rnd.uniform(0, TAU)
        L = rnd.uniform(*h)
        d = Vector((math.cos(a), math.sin(a), 0))
        b = Vector(loc)
        pts = [b, b + d * L * 0.15 + Vector((0, 0, L * 0.55)), b + d * L * 0.45 + Vector((0, 0, L * 0.8)),
               b + d * L * 0.75 + Vector((0, 0, L * 0.7))]
        rb = ribbon([tuple(q) for q in pts], [0.08, 0.07, 0.045, 0.008], normal_hint=(-d.y, d.x, 0), mat=m,
                    name="%s%d" % (name, k))
        tone = rnd.uniform(-0.2, 0.2)
        paint(rb, lambda pp, co, nn, li, b=b, tone=tone: mix(g[0], g[1], min(1.0, (co.z - b.z) / 0.3) + tone))
        parts.append(rb)
    return parts


def board(L, w, t, loc=(0, 0, 0), rot=(0, 0, 0), mat=None, name="board", strips=3, segs=3):
    """A board like box(), length along x, width along y, thickness along z,
    with both broad faces cut into strips along its length (for grain) and a
    few segments (for knots); its long edges are marked for seams()."""
    verts, faces = [], []
    n = (segs + 1) * (strips + 1)
    for z in (t, 0.0):
        for j in range(strips + 1):
            for i in range(segs + 1):
                verts.append((-L / 2 + L * i / segs, -w / 2 + w * j / strips, z))

    def v(z, i, j):
        return (0 if z else n) + j * (segs + 1) + i
    for j in range(strips):
        for i in range(segs):
            faces.append((v(1, i, j), v(1, i + 1, j), v(1, i + 1, j + 1), v(1, i, j + 1)))
            faces.append((v(0, i, j + 1), v(0, i + 1, j + 1), v(0, i + 1, j), v(0, i, j)))
    for i in range(segs):
        faces.append((v(0, i, 0), v(0, i + 1, 0), v(1, i + 1, 0), v(1, i, 0)))
        faces.append((v(1, i, strips), v(1, i + 1, strips), v(0, i + 1, strips), v(0, i, strips)))
    for j in range(strips):
        faces.append((v(0, 0, j + 1), v(0, 0, j), v(1, 0, j), v(1, 0, j + 1)))
        faces.append((v(1, segs, j + 1), v(1, segs, j), v(0, segs, j), v(0, segs, j + 1)))
    ob = mk.mesh(name, verts, faces, mat)
    mk._fix_normals(ob)
    ob["edge_w"] = w
    return mk.xform(ob, loc=loc, rot=rot)


def edge_seams(ob, dark, w, k=0.5):
    """Darkens a fresh board's long edges (before it is moved) so the joints
    between boards read as dark lines."""
    me = ob.data
    at = me.color_attributes["Col"]
    ref = [max(1e-4, mk.lin(c)) for c in me.materials[0]["ref"]]
    d = lift(dark)
    for p in me.polygons:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if abs(abs(co.y) - w / 2) < 1e-4:
                c = at.data[li].color
                at.data[li].color = tuple(c[i] * (1 - k) + k * mk.lin(d[i]) / ref[i] for i in range(3)) + (1.0,)
    return ob


def seams(ob, dark, k=0.55):
    """Darkens a bevelled board's rounded edges so the joints between boards read."""
    me = ob.data
    at = me.color_attributes["Col"]
    ref = [max(1e-4, mk.lin(c)) for c in me.materials[0]["ref"]]
    d = lift(dark)
    for p in me.polygons:
        if 0.15 < abs(p.normal.z) < 0.97:
            for li in p.loop_indices:
                c = at.data[li].color
                at.data[li].color = tuple(c[i] * (1 - k) + k * mk.lin(d[i]) / ref[i] for i in range(3)) + (1.0,)
    return ob


def shade(ob, towards, dark, k=0.7):
    """Darkens the faces turned toward a direction, the inside of a box of
    boards that the open sky sees less of."""
    me = ob.data
    at = me.color_attributes["Col"]
    ref = [max(1e-4, mk.lin(c)) for c in me.materials[0]["ref"]]
    d = lift(dark)
    T = Vector(towards).normalized()
    for p in me.polygons:
        if p.normal.dot(T) > 0.7:
            for li in p.loop_indices:
                c = at.data[li].color
                at.data[li].color = tuple(c[i] * (1 - k) + k * mk.lin(d[i]) / ref[i] for i in range(3)) + (1.0,)
    return ob


def pick(d, k, default):
    """d[k] for a table keyed by int in models.py or by string in JSON."""
    if not d:
        return default
    return d.get(k, d.get(str(k), default))


def cart(p):
    """A two-wheeled wooden handcart: a plank deck on two side beams that run
    on forward as the shafts, a bolster on the axle, solid plank wheels,
    and whichever of slat rails, side boards, a headboard, a hay load or
    sacks the picture shows. It rests on its wheels and shaft tips; the
    wreck drops a wheel, splays the deck and lies in the grass."""
    C = p["colours"]
    rnd = random.Random(p.get("seed", 1))
    wood_m = mat("wood", C["wood"][1], rough=0.9)
    iron_m = mat("iron", C["iron"][1], rough=0.6)
    L, W = p["bed"]
    R = p["wheel"]
    ax = p.get("axle_x", 0.0)
    bw = p.get("beam", 0.11)
    zb = R + p.get("clear", 0.06)
    parts = []

    def wd(ob, axis, knots=0.12):
        grain(ob, C["wood"][0], C["wood"][1], axis, seed=rnd.randint(0, 999), knots=knots, end=C.get("end"))
        return ob

    ys = W / 2 - bw / 2
    S = p["shafts"]
    sw = p.get("shaft_gap", W - bw)
    tip_r = p.get("shaft_t", bw * 0.8) / 2
    # shafts off the side beams, or rooted under the bed and running parallel
    root = p.get("shaft_root")
    sz = zb - tip_r if root else zb + bw / 2
    for s in (-1, 1):
        parts.append(wd(box(L, bw, bw, loc=(0, s * ys, zb), mat=wood_m, name="beam"), (1, 0, 0)))
        if S > 0 and p.get("no_shaft") != s:
            a = Vector((L / 2 - root, s * sw / 2, sz)) if root else Vector((L / 2 - 0.2, s * ys, sz))
            b = Vector((L / 2 + S, s * sw / 2, sz))
            sh = plank(tuple(a), tuple(b), p.get("shaft_w", bw * 1.1), p.get("shaft_t", bw * 0.8), mat=wood_m,
                       name="shaft")
            parts.append(wd(sh, tuple(b - a)))
    for sx in (-1, 1):
        parts.append(wd(box(bw, W - 2 * bw, bw * 0.9, loc=(sx * (L / 2 - bw / 2), 0, zb), mat=wood_m,
                            name="endbeam"), (0, 1, 0)))
    dz = zb + bw
    dt = p.get("deck_t", 0.06)
    n = p.get("planks", 5)
    pw = W / n
    splay = p.get("splay", 0.0)
    for i in range(n):
        if i in p.get("missing_planks", ()):
            continue
        y = -W / 2 + pw * (i + 0.5) + rnd.uniform(-splay, splay)
        ln = L * rnd.uniform(0.96, 1.0) * pick(p.get("short"), i, 1.0)
        x = rnd.uniform(-0.03, 0.03) + (L - ln) / 2 * (1 if i % 2 else -1)
        # slats with ground between them, their ends staggered, where asked
        wdt = pw - (rnd.uniform(*p["plank_gap"]) if p.get("plank_gap") else 0.02)
        if p.get("stagger"):
            x += rnd.uniform(-p["stagger"], p["stagger"])
        pl = edge_seams(wd(board(ln, wdt, dt, mat=wood_m, name="deck%d" % i), (1, 0, 0)), C["wood"][0], wdt)
        warp = p.get("warp", 0.0)
        mk.xform(pl, loc=(x, y, dz + rnd.uniform(-0.006, 0.006)),
                 rot=(rnd.uniform(-warp, warp), rnd.uniform(-warp, warp) * 0.3,
                      rnd.uniform(-0.6, 0.6) + rnd.uniform(-splay, splay) * 25))
        parts.append(pl)
    top = dz + dt
    if p.get("cross"):
        # a cross beam over the slats near the far end
        f, rz = p["cross"]
        cb = wd(box(0.17, W + 0.12, 0.09, loc=(f * L / 2, 0, top - 0.01), rot=(0, 0, rz), mat=wood_m,
                    name="crossbeam"), (0, 1, 0))
        parts.append(cb)
    if p.get("shear"):
        # the wreck's bed racked into a parallelogram, its slats slanting
        k = math.tan(math.radians(p["shear"]))
        place(parts, Matrix(((1, 0, 0, 0), (-k, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))))

    # axle, the bolster on it and the wheels
    yw = W / 2 + p.get("hub_off", 0.12)
    axle = rod((ax, -yw - 0.12, R), (ax, yw + 0.12, R), 0.05, 0.05, seg=6, mat=iron_m, name="axle")
    flat(axle, C["iron"][0])
    parts.append(axle)
    parts.append(wd(box(0.2, W + 0.1, zb - R + 0.04, loc=(ax, 0, R - 0.02), mat=wood_m, name="bolster"), (0, 1, 0)))
    wt = p.get("wheel_t", 0.1)
    for s in p.get("wheels", (-1, 1)):
        wp = plank_wheel(R, wt, C, (wood_m, iron_m), planks=p.get("wheel_planks", 3), seed=rnd.randint(0, 999))
        M = Matrix.Translation((ax, s * yw, R))
        if s > 0:
            M = M @ Matrix.Rotation(math.pi, 4, "Z")
        if pick(p.get("wheel_yaw"), s, 0):
            M = M @ Matrix.Rotation(math.radians(pick(p["wheel_yaw"], s, 0)), 4, "Z")
        if pick(p.get("wheel_lean"), s, 0):
            # degrees off upright about the cart's length (the cart's roll adds to it)
            M = M @ Matrix.Rotation(math.radians(pick(p["wheel_lean"], s, 0)), 4, "X")
        parts += place(wp, M)

    # rails of stakes and a top rail, solid side boards, head and tail boards
    rh = p.get("rail_h", 0.42)
    for s in p.get("rails", ()):
        y = s * (W / 2 - 0.04)
        k = p.get("posts", 6)
        for i in range(k):
            x = -L / 2 + 0.08 + (L - 0.16) * i / (k - 1)
            sk = p.get("stake", 0.06)
            parts.append(wd(box(sk, sk, rh, loc=(x, y, top), mat=wood_m, name="stake"), (0, 0, 1)))
        parts.append(wd(box(L + 0.04, 0.07, 0.06, loc=(0, y, top + rh - 0.03), mat=wood_m, name="toprail"), (1, 0, 0)))
        if p.get("mid_rail"):
            parts.append(wd(box(L, 0.05, 0.045, loc=(0, y, top + rh * 0.45), mat=wood_m, name="midrail"), (1, 0, 0)))
    bh = p.get("board_h", 0.4)
    nb = p.get("board_n", 2)
    for s in p.get("boards", ()):
        y = s * (W / 2 - 0.03)
        f0, f1 = pick(p.get("board_span"), s, (0.0, 1.0))
        x0, x1 = -L / 2 + L * f0, -L / 2 + L * f1
        for k in range(nb):
            hk_ = bh / nb - 0.012
            sb = edge_seams(wd(board(x1 - x0, hk_, 0.05, mat=wood_m, name="sideboard"), (1, 0, 0)), C["wood"][0], hk_)
            # stand it on edge: its width becomes height, its inner face toward the bed
            mk.xform(sb, rot=(-90 * s, 0, 0), loc=((x0 + x1) / 2, y - s * 0.025, top + k * bh / nb + hk_ / 2))
            parts.append(shade(sb, (0, -s, 0), C["wood"][0]))
        if not p.get("rails") or s not in p["rails"]:
            for x in (x0 + 0.1, (x0 + x1) / 2, x1 - 0.1):
                parts.append(wd(box(0.07, 0.05, bh + 0.05, loc=(x, y + s * 0.045, top - 0.03), mat=wood_m,
                                    name="cleat"), (0, 0, 1)))
    for key, sx in (("head", 1), ("tail", -1)):
        if p.get(key):
            hh = p[key]
            x = sx * (L / 2 - 0.03)
            for k in range(nb):
                hk_ = hh / nb - 0.012
                eb = edge_seams(wd(board(W, hk_, 0.05, mat=wood_m, name=key + "board"), (1, 0, 0)), C["wood"][0], hk_)
                mk.xform(eb, rot=(90, 0, 90), loc=(x - 0.025, 0, top + k * hh / nb + hk_ / 2))
                parts.append(shade(eb, (-sx, 0, 0), C["wood"][0]))

    # the load
    if p.get("hay"):
        hy = p["hay"]
        parts += hay_load(C, hy.get("L", L - 0.1), hy.get("W", W), top - 0.02, hy["h"], rnd,
                          over=hy.get("over", (1.05, 1.1)), strands=hy.get("strands", 70),
                          lying=hy.get("lying", 260), blunt=hy.get("blunt"), x0=hy.get("x", 0.0),
                          y0=hy.get("y", 0.0))
    if p.get("sacks"):
        sm = mat("sack", brightest(C["sack"][1], C["sack_hi"]), rough=1.0)
        for k, sk in enumerate(p["sacks"]):
            # (x, y, turn, size, tilt[, lift]): a lifted sack lies on the others
            x, y, rz, size, tilt = sk[:5]
            z = top - 0.02 + (sk[5] if len(sk) > 5 else 0.0)
            parts.append(sack(C, (x, y, z), size, rot=rz, tilt=tilt, rnd=rnd, m=sm, name="sack%d" % k))

    # pose: pitch round the axle till the shaft tips reach the ground, or
    # tipped back till the tail does
    if "pitch" in p:
        beta = math.radians(p["pitch"])
    elif p.get("rest") == "tail":
        A, B = L / 2 + ax, zb - R
        beta = math.asin(max(-1.0, -R / math.hypot(A, B))) - math.atan2(B, A)
    else:
        dx = L / 2 + S - ax
        dzz = sz - R
        A = math.hypot(dx, dzz)
        beta = math.acos(max(-1.0, min(1.0, (tip_r - R) / A))) - math.atan2(dx, dzz)
    M = Matrix.Translation((ax, 0, R)) @ Matrix.Rotation(beta, 4, "Y") @ Matrix.Translation((-ax, 0, -R))
    if p.get("roll"):
        M = Matrix.Rotation(math.radians(p["roll"]), 4, "X") @ M
    at = p.get("at", (0.0, 0.0))
    M = Matrix.Translation((at[0], at[1], p.get("lift", 0.0))) @ Matrix.Rotation(math.radians(p["yaw"]), 4, "Z") @ M
    place(parts, M)

    # loose pieces, placed in the world frame
    for k, lw in enumerate(p.get("loose_wheels", ())):
        x, y, z, rx, ry, rz = lw
        wp = plank_wheel(R, wt, C, (wood_m, iron_m), planks=p.get("wheel_planks", 3), seed=100 + k)
        E = (Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, "Z")
             @ Matrix.Rotation(math.radians(ry), 4, "Y") @ Matrix.Rotation(math.radians(rx), 4, "X"))
        parts += place(wp, E)
    for k, (a, b, w, t) in enumerate(p.get("loose_planks", ())):
        pl = plank(a, b, w, t, mat=wood_m, name="looseplank%d" % k)
        parts.append(wd(pl, tuple(Vector(b) - Vector(a))))
    for k, (a, b) in enumerate(p.get("loose_shafts", ())):
        sh = plank(a, b, p.get("shaft_w", bw * 1.1), p.get("shaft_t", bw * 0.8), mat=wood_m, name="looseshaft%d" % k)
        parts.append(wd(sh, tuple(Vector(b) - Vector(a))))
    if p.get("tufts"):
        gm = mat("grass", C["grass"][1], rough=0.8)
        for k, t in enumerate(p["tufts"]):
            parts += tuft(C, (t[0], t[1], 0.0), rnd, gm, blades=t[2] if len(t) > 2 else 6, name="tuft%d_" % k)
    return parts


# ---------------------------------------------------------------- corn

def corn(p):
    """A patch of standing corn: rows of stalks on hilled dark earth, each
    stalk with arching leaves in alternate directions (along the row with
    "along"), a pale tassel on top and now and then an ear; the rows'
    tassels make the pale bands across the picture."""
    C = p["colours"]
    rnd = random.Random(p.get("seed", 1))
    parts = []
    soil_m = mat("soil", C["soil"][1], rough=1.0)
    stalk_m = mat("stalk", C["stalk"][1], rough=0.8)
    # (from, amount): leaves above from * h turn toward the lime ramp C["lime"], fully by lit_to * h
    lit = p.get("lit")
    leaf_m = mat("leaf", brightest(C["leaf"][3], *C.get("lime", ())), rough=p.get("leaf_rough", 0.7))
    if p.get("matte"):
        # leaves and stalks take no sheen, which would grey their green
        for m_ in (leaf_m, stalk_m):
            m_.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.0
    tassel_m = mat("tassel", C["tassel"][1], rough=0.9)
    ys = p["rows"]
    h = p["h"]
    spans = p.get("spans") or [p["span"]] * len(ys)
    # the earth runs on behind the back row and a little past the row ends,
    # so the ground seen between the plants is dark soil as in the picture
    side = p.get("soil_side", -0.06)
    x_lo = min(s[0] for s in spans) - side
    x_hi = max(s[1] for s in spans) + side
    y_lo, y_hi = min(ys) - p.get("soil_front", 0.1), max(ys) + p.get("soil_back", 0.12)
    sp = p.get("spacing", 0.6)
    # hilled earth: a grid raised along each row, down to the ground at its edge
    nx, ny = 10, max(6, 3 * len(ys))
    verts, faces = [], []
    order = sorted(zip(ys, spans))

    def span_at(y):
        # the earth follows each row's own ends, between rows the nearer's
        if y <= order[0][0]:
            return order[0][1]
        for (y0, s0), (y1, s1) in zip(order, order[1:]):
            if y <= y1:
                return s0 if y - y0 < y1 - y else s1
        return order[-1][1]
    for j in range(ny + 1):
        y = y_lo + (y_hi - y_lo) * j / ny
        xa, xb = span_at(y)
        xa, xb = (xa - side, xb + side) if p.get("spans") else (x_lo, x_hi)
        for i in range(nx + 1):
            x = xa + (xb - xa) * i / nx
            inner = min(i, nx - i, j, ny - j) > 0
            ridge = max(math.cos(math.pi * (y - r) / sp) ** 2 if abs(y - r) < sp / 2 else 0.0 for r in ys)
            z = (0.02 + 0.06 * ridge + rnd.uniform(-0.01, 0.01)) if inner else 0.0
            if not inner:
                # a ragged edge, pulled in at the corners
                x += (0.12 if i == 0 else -0.12 if i == nx else 0.0) * rnd.uniform(0.2, 1.0)
                rag = p.get("soil_rag", 0.1)
                y += (rag if j == 0 else -rag if j == ny else 0.0) * rnd.uniform(0.2, 1.0)
            verts.append((x + (rnd.uniform(-0.05, 0.05) if inner else 0.0), y, z))
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    soil = mk.mesh("soil", verts, faces, soil_m)
    paint(soil, lambda pp, co, nn, li: mix(C["soil"][0], C["soil"][1], 0.4 + 0.6 * noise.noise(co * 3.0)))
    parts.append(soil)

    pal = C["leaf"]
    per = p["per_row"]
    canopy = p.get("canopy", 0.0)

    def under(c, z):
        # the rows shade their own lower leaves and stalks
        k = 1.0 - canopy * (1.0 - min(1.0, max(0.0, z / h)) ** 0.8)
        return tuple(v * k for v in c)

    def sunlit(c, z, s, up=1.0):
        # the upper leaves catch the sun, lime along their length; lit[2] keeps
        # that share of it off the faces turned edge-on to the sky
        if not lit:
            return c
        f = lit[1] * min(1.0, max(0.0, (z / h - lit[0]) / (p.get("lit_to", 1.0) - lit[0])))
        if len(lit) > 2:
            f *= 1.0 - lit[2] * (1.0 - up)
        return mix(c, mix(C["lime"][0], C["lime"][1], s), f)
    y_first, y_last = min(ys), max(ys)
    # (row index, -1 left or 1 right): edge plants that spread no extra leaves
    no_edge = {tuple(e) for e in p.get("no_edge", ())}
    for ri, (y0, (xa, xb)) in enumerate(zip(ys, spans)):
        n = max(2, int(round(per * (xb - xa) / (spans[0][1] - spans[0][0]))))
        for k in range(n):
            # plants on the patch's edge turn their leaves outward, over the soil's edge
            out = Vector(((k == n - 1) - (k == 0), (y0 == y_last) - (y0 == y_first), 0))
            # the end plants stand on the span's ends, their leaves over the soil's edge
            x = xa + (xb - xa) * k / (n - 1) + rnd.uniform(-0.07, 0.07)
            y = y0 + rnd.uniform(-0.07, 0.07)
            hh = h * rnd.uniform(0.84, 1.06)
            base = Vector((x, y, 0.03))
            topv = base + Vector((rnd.uniform(-0.08, 0.08), rnd.uniform(-0.08, 0.08), hh))
            sr = p.get("stalk_r", (0.04, 0.02))
            st = rod(tuple(base), tuple(topv), sr[0], sr[1], seg=p.get("stalk_seg", 3), mat=stalk_m, name="stalk")
            paint(st, lambda pp, co, nn, li: under(mix(C["stalk"][0], C["stalk"][1], co.z / h), co.z))
            parts.append(st)
            phi = rnd.uniform(0, TAU)
            if p.get("along") is not None:
                # leaves spread along the row, so each row reads as one hedge
                phi = rnd.choice((0.0, math.pi)) + rnd.uniform(-p["along"], p["along"])
            if p.get("edge_out") and out.length:
                phi = math.atan2(out.y, out.x) + rnd.uniform(-p["edge_out"], p["edge_out"])
            nl = p.get("leaves", 5)
            # an edge plant spreads a few more long leaves out over the patch's edge
            ne = p.get("edge_leaves", 0) if out.length and (ri, int(out.x)) not in no_edge else 0
            for q in range(nl + ne):
                if q < nl:
                    t = 0.18 + 0.62 * q / (nl - 1) + rnd.uniform(-0.04, 0.04)
                    a = phi + math.pi * q + rnd.uniform(-0.6, 0.6)
                    far = 1.0
                else:
                    t = rnd.uniform(0.2, 0.55)
                    a = math.atan2(out.y, out.x) + rnd.uniform(-1.0, 1.0)
                    far = p.get("edge_len", 1.25)
                node = base + (topv - base) * t
                d = Vector((math.cos(a), math.sin(a), 0))
                ln = p.get("leaf_len", 0.62) * (1.1 - 0.45 * t) * rnd.uniform(0.8, 1.15) * far
                rise = rnd.uniform(*p.get("rise", (0.15, 0.35)))
                wl = p.get("leaf_w", 0.1)
                if p.get("leaf_pts", 4) == 3:
                    # one bend: up and out, then down to the tip
                    pts = [node, node + d * ln * 0.45 + Vector((0, 0, ln * rise * 1.1)),
                           node + d * ln + Vector((0, 0, ln * (rise * 0.3 - 0.25)))]
                    wd_ = [wl * 0.55, wl, wl * 0.12]
                else:
                    pts = [node, node + d * ln * 0.25 + Vector((0, 0, ln * rise)),
                           node + d * ln * 0.6 + Vector((0, 0, ln * rise * 0.9)),
                           node + d * ln + Vector((0, 0, ln * (rise * 0.3 - 0.25)))]
                    wd_ = [wl * 0.5, wl, wl * 0.75, wl * 0.12]
                lf = ribbon([tuple(v) for v in pts], wd_, mat=leaf_m, name="leaf")
                tone = rnd.random()
                t0, t1 = p.get("leaf_mix", (0.3, 0.65))
                c0 = pal[0] if tone < t0 else pal[1] if tone < t1 else pal[2]
                c1 = pal[1] if tone < t0 else pal[2] if tone < t1 else pal[3]
                # the sun lights the upper leaves over the rows' shade, the flatter faces most
                paint(lf, lambda pp, co, nn, li, n0=node, c0=c0, c1=c1, ln=ln:
                      sunlit(under(mix(c0, c1, (co - n0).length / ln), co.z), co.z, (co - n0).length / ln,
                             abs(nn.z)))
                parts.append(lf)
            # tassel: a few pale spikes fanning up from the top
            tv, tf = [], []
            tw = p.get("tassel_w", 0.09)
            nt = p.get("tassel_n", 6)
            for q in range(nt):
                a = phi + TAU * q / nt + rnd.uniform(-0.4, 0.4)
                d = Vector((math.cos(a), math.sin(a), 0))
                s = Vector((-d.y, d.x, 0))
                tl = p.get("tassel", 0.26) * rnd.uniform(0.75, 1.2)
                up = rnd.uniform(*p.get("tassel_up", (0.35, 0.9)))
                b0 = topv - Vector((0, 0, 0.05))
                i0 = len(tv)
                tv += [tuple(b0 - s * tw / 2), tuple(b0 + s * tw / 2),
                       tuple(b0 + d * tl * (1.0 - up * 0.5) + Vector((0, 0, tl * up)))]
                tf.append((i0, i0 + 1, i0 + 2))
            ts = mk.mesh("tassel", tv, tf, tassel_m)
            paint(ts, lambda pp, co, nn, li: mix(C["tassel"][0], C["tassel"][1], rnd.uniform(0.3, 1.0)))
            parts.append(ts)
            if rnd.random() < p.get("ears", 0.35):
                a = phi + math.pi / 2 + rnd.uniform(-0.5, 0.5)
                d = Vector((math.cos(a), math.sin(a), 0))
                e0 = base + (topv - base) * rnd.uniform(0.45, 0.6)
                ear = rod(tuple(e0), tuple(e0 + d * 0.12 + Vector((0, 0, 0.26))), 0.05, 0.015, seg=4, mat=tassel_m,
                          name="ear")
                paint(ear, lambda pp, co, nn, li: mix(C["ear"][0], C["ear"][1], rnd.uniform(0.2, 1.0)))
                parts.append(smooth(ear, 60))
    for k, (x, y, a, ln) in enumerate(p.get("strays", ())):
        d = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        node = Vector((x, y, 0.35))
        pts = [node, node + d * ln * 0.3 + Vector((0, 0, 0.12)), node + d * ln * 0.7 + Vector((0, 0, 0.05)),
               node + d * ln + Vector((0, 0, -0.2))]
        lf = ribbon([tuple(v) for v in pts], [0.06, 0.1, 0.08, 0.015], mat=leaf_m, name="stray%d" % k)
        paint(lf, lambda pp, co, nn, li: mix(pal[1], pal[3], 0.5))
        parts.append(lf)
    return parts


# ---------------------------------------------------------------- sturdiness

# The pictures draw height at half the scale of width, so a pixel-exact fit
# leaves tall parts thin in true 3D. The tables hold that fit; sturdy() builds
# tall parts thicker by girth and lower by height (owner's rule, 2026-09-27).
STURDY = {"girth": 1.18, "height": 0.85, "flame": (0.78, 1.28)}


def _up(z, z0, h):
    """A height above z0 lowered by h."""
    return z0 + (z - z0) * h


def sturdy(p):
    """A copy of the model's parameters with its tall parts sturdier. The
    entry's "sturdy" dict overrides the family factors: girth and height for
    columns, piers, statues, stalks and stakes, flame=(height, width) for
    fire, and a few per-kind extras (base, knob, bowl, coil, body, depth, side)."""
    s = dict(STURDY, **p.get("sturdy", {}))
    q = dict(p)
    g, h = s["girth"], s["height"]
    fh, fw = s["flame"]
    kind = p["kind"]
    if kind == "brazier":
        rb, hb = p["base"]
        q["base"] = (rb * s.get("base", g), hb)
        q["column_r"] = p["column_r"] * g
        q["knob"] = (_up(p["knob"][0], hb, h), p["knob"][1] * s.get("knob", g))
        q["column_top"] = _up(p["column_top"], hb, h)
        if "bowl" in p:
            zr, rr, depth = p["bowl"]
            q["bowl"] = (_up(zr, hb, h), rr * s.get("bowl", 1.0), depth)
        if "flame" in p:
            q["flame"] = (p["flame"][0] * fh, p["flame"][1] * fw)
            q["side"] = s.get("side", p.get("side", (0.2, 0.45)))
        if "shards" in p:
            q["shards"] = [(a, hg * h, lean * g) for (a, hg, lean) in p["shards"]]
        if "bowl_frags" in p:
            q["bowl_frags"] = [(x * g, y * g, _up(z, hb, h) if z > hb else z, rot, arc, r * g)
                               for (x, y, z, rot, arc, r) in p["bowl_frags"]]
    elif kind == "bonfire":
        if "flame" in p:
            q["flame"] = (p["flame"][0] * fh, p["flame"][1] * fw)
            q["side"] = s.get("side", p.get("side", (0.15, 0.4)))
            q["spread"] = s.get("spread", p.get("spread", 1.0))
    elif kind == "fountain" and p.get("centre") == "serpent":
        st = dict(p["statue"])
        r, z0, z1 = st["pedestal"]
        st["pedestal"] = (r * g, z0, _up(z1, z0, h))
        st["radius"] = st["radius"] * g
        c = s.get("coil", 1.0)
        st["path"] = [(x * c, y * c, _up(z, z0, h)) for (x, y, z) in st["path"]]
        q["statue"] = st
    elif kind == "altar":
        L, D, H = p["size"]
        q["size"] = (L, D * s.get("depth", 1.0), H * s.get("body", 1.0))
        q["pier_w"] = p["pier_w"] * g
        q["pier_h"] = tuple(v * h for v in p["pier_h"])
    elif kind == "cart":
        for k, d in (("beam", 0.11), ("shaft_w", None), ("shaft_t", None), ("wheel_t", 0.1), ("stake", 0.06)):
            if d is not None or k in p:
                q[k] = p.get(k, d) * g
        for k in ("rail_h", "board_h", "head", "tail"):
            if p.get(k):
                q[k] = p[k] * h
        if p.get("hay"):
            q["hay"] = dict(p["hay"], h=p["hay"]["h"] * h)
    elif kind == "corn":
        q["h"] = p["h"] * h
        q["stalk_r"] = tuple(v * g for v in p.get("stalk_r", (0.04, 0.02)))
        q["stalk_seg"] = s.get("stalk_seg", p.get("stalk_seg", 3))
    return q


# ---------------------------------------------------------------- shipping

def ship(ob, glb_path, rec):
    """Exports the model as it ships, geometry only: each material painted
    from the picture loses the picture and names it in okPaint extras, so the
    game paints it from the player's own files (D:/OKBuild/geometry-only-spec.md).
    A tint on the picture stays as the base colour factor. The copy with the
    pixels that finish() wrote is for review only."""
    me = ob.data
    swapped = []
    for i, m in enumerate(me.materials):
        if m is None or not m.use_nodes:
            continue
        tex = [n for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image and n.outputs["Color"].is_linked]
        if not tex:
            continue
        img = tex[0].image
        tint = next((n.inputs[7].default_value[0] for n in m.node_tree.nodes if n.label == "tint"), 1.0)
        m2 = bpy.data.materials.new(m.name + "_ship")
        m2.use_nodes = True
        b = m2.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (tint, tint, tint, 1.0)
        b.inputs["Roughness"].default_value = m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value
        m2["okPaint"] = {"kind": "feature", "name": rec.get("seq", rec["name"]), "world": rec["world"],
                         "gain": hk.carve.ALBEDO_GAIN, "bleed": True, "alpha": "opaque",
                         "size": [int(img.size[0]), int(img.size[1])]}
        name = m.name
        m.name = name + "_px"
        m2.name = name
        me.materials[i] = m2
        swapped.append((i, m, m2, name))
    flag = ob.pop(hk.carve.PLAYERS_FILES, None)
    os.makedirs(os.path.dirname(os.path.abspath(glb_path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=glb_path, export_format="GLB", use_selection=True,
                              export_yup=True, export_extras=True)
    print("PROPS_SHIP", glb_path, len(swapped), "materials painted at load")
    for i, m, m2, name in swapped:
        me.materials[i] = m
        m2.name = name + "_ship"
        m.name = name
        bpy.data.materials.remove(m2)
    if flag is not None:
        ob[hk.carve.PLAYERS_FILES] = flag
    return ob
