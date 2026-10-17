"""Helpers for remastered unit models, run inside Blender.

A unit's drop-in model replaces its original 3DO whole, and the unit's own
script still poses it: a node named like one of the original's pieces
follows that piece (docs/STUDIO.md, Pieces). The game moves a piece's
geometry from where it lies in the model at rest to where the script puts
it, turning about the original's pivot. So a remastered unit starts from
the original's rig, read here from the player's own game files: the piece
names, their hierarchy and each pivot. The kit then models each piece's
geometry in the model's rest pose, in handkit's frame (one unit a map cell
of 16 pixels, -Y toward the classic camera, Z up, the origin on the ground
under the unit), and hangs it under a node named for its piece, placed at
the piece's pivot. Nothing of the original goes into the model.

    import unitkit as uk
    rig = uk.Rig("ARAKING")                 # pieces, pivots, hierarchy
    nodes = rig.nodes()                      # an empty per piece, at its pivot
    part = uk.mesh("Torso", bm, [gold])      # geometry at rest, model space
    uk.attach(part, nodes["Torso"])
    uk.bake_atlas(parts, 1024)               # albedo and local AO in one atlas
    uk.export(nodes, "unity/Assets/Overrides/Units/ARAKING.glb")

A 3DO keeps x east, y up and z toward the back of the unit, in pixels, so
a point (x, y, z) lies at (x, z, y) / 16 here. Its front is -z, which is
-Y here and faces the classic camera, as every drop-in model does.

Materials are plain glTF PBR (GlbLoader.cs): a base colour picture,
metallic and roughness as numbers, and an emissive colour for gems. The
game reads no normal maps, so bevels and rims are real geometry, and the
shading a normal map would give is baked into the base colour as each
piece's own ambient occlusion. Every picture is generated here, from noise
and numbers and the model's own faces (okpaint.generated), never from the
original's art. A material named "teamcolor" takes each player's colour
in the game, the Studio and the OpenKingdoms 3D view, multiplied over its
base colour, so the cloth it covers is painted in light greys.

Review helpers, which read the player's files and never ship:
    original(rig)          the original model, textured, for side by sides
    Poses(rig_json)        the script's poses from the engine
                           (Tests/Editor/UnitRigDump.cs), to play the
                           real animations on both models
"""
import json
import math
import os
import struct
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SPRITE_TOOLS = os.path.join(REPO, "tools", "sprite-replace")
if SPRITE_TOOLS not in sys.path:
    sys.path.insert(0, SPRITE_TOOLS)
import hpi  # noqa: E402
import okpaint  # noqa: E402
from okpaint import generated  # noqa: E402,F401

PX = 1.0 / 16.0  # cells per pixel
GAME = os.environ.get("OK_GAME_DIR", "C:/GOG Games/Total Annihilation Kingdoms")
# Later archives win, as the game reads them: The Iron Plague over the base game.
ARCHIVES = ("data.hpi", "IPData.hpi")


def from_3do(x, y, z):
    """A 3DO point in pixels, as a point here in cells."""
    return Vector((x * PX, z * PX, y * PX))


# ---------------------------------------------------------------- the original's files

_archives = None


def _files():
    global _archives
    if _archives is None:
        _archives = [hpi.Archive(os.path.join(GAME, a)) for a in ARCHIVES if os.path.exists(os.path.join(GAME, a))]
    return _archives


def game_file(path):
    """A file from the player's archives, the last archive holding it winning."""
    for a in reversed(_files()):
        if path.lower() in a.files:
            return a.read(path)
    raise FileNotFoundError(path)


def _cstr(d, o):
    return d[o:d.index(b"\0", o)].decode("latin-1")


class Piece:
    def __init__(self, name, parent, offset, verts, prims, selection):
        self.name, self.parent, self.offset = name, parent, offset
        self.verts, self.prims, self.selection = verts, prims, selection
        self.pivot = Vector()  # model space at rest, cells
        self.children = []


class Rig:
    """The original's pieces in the order the engine numbers them."""

    def __init__(self, model):
        self.model = model.upper()
        d = game_file("objects3d/%s.3do" % model.lower())
        self.pieces = []
        self._read(d, 0, -1)
        for i, p in enumerate(self.pieces):
            off = from_3do(*p.offset)
            p.pivot = off + (self.pieces[p.parent].pivot if p.parent >= 0 else Vector())
            if p.parent >= 0:
                self.pieces[p.parent].children.append(i)
        self.by_name = {p.name.lower(): p for p in self.pieces}

    def _read(self, d, off, parent):
        # The root sits at offset 0, and 0 ends a list of siblings.
        while True:
            (_v, nv, npr, sel, x, y, z, oname, _u, ov, op, osib, ochild) = struct.unpack_from("<IIIiiiiIIIIII", d, off)
            verts = [tuple(c / 65536.0 for c in struct.unpack_from("<iii", d, ov + 12 * i)) for i in range(nv)]
            prims = []
            for k in range(npr):
                col, n, _r, _c, ovl, otex, _a, _b = struct.unpack_from("<IIHHIIII", d, op + 32 * k)
                prims.append((list(struct.unpack_from("<%dH" % n, d, ovl)) if n else [], _cstr(d, otex) if otex else "", col))
            self.pieces.append(Piece(_cstr(d, oname), parent, (x / 65536.0, y / 65536.0, z / 65536.0), verts, prims, sel != -1))
            me = len(self.pieces) - 1
            if ochild:
                self._read(d, ochild, me)
            if not osib:
                return
            off = osib

    def __getitem__(self, name):
        return self.by_name[name.lower()]

    def pivot(self, name):
        return self[name].pivot.copy()

    def point(self, name, x, y, z):
        """A point given in a piece's own 3DO frame, in model space here."""
        return self[name].pivot + from_3do(x, y, z)

    def nodes(self, collection=None):
        """An empty per piece at its pivot, parented as the original's."""
        out = {}
        for p in self.pieces:
            ob = bpy.data.objects.new(p.name, None)
            ob.empty_display_size = 0.08
            (collection or bpy.context.scene.collection).objects.link(ob)
            if p.parent >= 0:
                ob.parent = out[self.pieces[p.parent].name]
                ob.location = p.pivot - self.pieces[p.parent].pivot
            else:
                ob.location = p.pivot
            out[p.name] = ob
        bpy.context.view_layer.update()
        return out


# ---------------------------------------------------------------- building geometry

def mesh(name, bm, mats=(), smooth=None, outward=True):
    """An object from a bmesh, its vertices in model space at rest, its
    faces turned to face out (open sheets keep the winding they were made
    with when outward is False)."""
    me = bpy.data.meshes.new(name)
    if outward:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if smooth is not None:
        shade(ob, smooth)
    return ob


def shade(ob, angle=35):
    """Smooth shading, with edges sharper than angle kept hard."""
    for p in ob.data.polygons:
        p.use_smooth = True
    ob.data.set_sharp_from_angle(angle=math.radians(angle))
    return ob


def ring(c, ax, ay, rx, ry, n, power=2.0, start=0.0, arc=2 * math.pi, closed=True):
    """n points round centre c in the plane of the unit axes ax, ay, a
    superellipse of radii rx, ry: power 2 is an ellipse, higher squarer."""
    pts = []
    count = n if closed else n + 1
    for i in range(count):
        t = start + arc * i / n
        ct, st = math.cos(t), math.sin(t)
        x = math.copysign(abs(ct) ** (2.0 / power), ct) * rx
        y = math.copysign(abs(st) ** (2.0 / power), st) * ry
        pts.append(c + ax * x + ay * y)
    return pts


def loft(bm, rings, closed=True, cap0=False, cap1=False, mat=0):
    """Quads between successive rings of equal length; caps fan the ends."""
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    faces = []
    for a, b in zip(vs, vs[1:]):
        for i in range(n if closed else n - 1):
            j = (i + 1) % n
            f = bm.faces.new((a[i], a[j], b[j], b[i]))
            f.material_index = mat
            faces.append(f)
    for cap, r, flip in ((cap0, vs[0], True), (cap1, vs[-1], False)):
        if cap:
            f = bm.faces.new(list(reversed(r)) if flip else r)
            f.material_index = mat
            faces.append(f)
    return vs, faces


def frame(axis):
    """Two unit axes square to axis, the first as near to X as can be."""
    axis = Vector(axis).normalized()
    ref = Vector((1, 0, 0)) if abs(axis.x) < 0.9 else Vector((0, 1, 0))
    ax = (ref - axis * ref.dot(axis)).normalized()
    return ax, axis.cross(ax)


def tube(bm, a, b, radii, n=12, power=2.0, cap0=True, cap1=True, mat=0, twist=0.0):
    """A tube from a to b, its cross-section radii [(rx, ry), ...] spread
    evenly along it, rx across the tube's X-most side."""
    a, b = Vector(a), Vector(b)
    axis = b - a
    ax, ay = frame(axis)
    rs = []
    for k, (rx, ry) in enumerate(radii):
        t = k / (len(radii) - 1)
        rs.append(ring(a + axis * t, ax, ay, rx, ry, n, power, start=twist))
    return loft(bm, rs, cap0=cap0, cap1=cap1, mat=mat)


def bevel(ob, width, segments=1, angle=40, harden=True):
    """Rounds the hard edges, as the light needs with no normal maps."""
    m = ob.modifiers.new("bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle)
    m.harden_normals = harden
    m.miter_outer = "MITER_ARC"
    return apply(ob)


def solidify(ob, thickness, offset=-1.0, rim=True):
    m = ob.modifiers.new("solid", "SOLIDIFY")
    m.thickness = thickness
    m.offset = offset
    m.use_rim = rim
    m.use_even_offset = True
    return apply(ob)


def subdivide(ob, levels=1):
    m = ob.modifiers.new("subd", "SUBSURF")
    m.levels = levels
    m.render_levels = levels
    return apply(ob)


def apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    return ob


def join(obs, name=None):
    """Joins objects into the first, keeping every material."""
    obs = [o for o in obs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    if name:
        ob.name = name
        ob.data.name = name
    return ob


def mirror_x(ob, name):
    """A copy of ob mirrored through x = 0, faces kept facing out."""
    me = ob.data.copy()
    me.name = name
    bm = bmesh.new()
    bm.from_mesh(me)
    for v in bm.verts:
        v.co.x = -v.co.x
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def transform(ob, m):
    ob.data.transform(m)
    ob.data.update()
    return ob


def attach(ob, node):
    """Hangs a part made in model space under its piece's node, keeping
    where it lies, so its vertices sit in the node's own frame."""
    bpy.context.view_layer.update()
    mw = node.matrix_world.copy()
    ob.data.transform(mw.inverted())
    ob.parent = node
    ob.matrix_parent_inverse = Matrix.Identity(4)
    ob.matrix_basis = Matrix.Identity(4)
    return ob


def triangles(obs):
    n = 0
    for o in obs:
        if o.type == "MESH":
            for p in o.data.polygons:
                n += len(p.vertices) - 2
    return n


# ---------------------------------------------------------------- materials and pictures

def material(name, rgb=(0.8, 0.8, 0.8), metal=0.0, rough=0.6, emit=None, strength=1.0, double=False):
    """A glTF PBR material. The base colour comes from the atlas once baked."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1.0)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit is not None:
        b.inputs["Emission Color"].default_value = (*emit, 1.0)
        b.inputs["Emission Strength"].default_value = strength
    m.use_backface_culling = not double
    m["uk_rgb"] = list(rgb)
    return m


def image(name, rgba):
    """A generated picture from an (h, w, 3 or 4) float array in 0..1,
    row 0 at the bottom as Blender keeps it."""
    h, w = rgba.shape[:2]
    px = np.ones((h, w, 4), np.float32)
    px[:, :, :rgba.shape[2]] = rgba
    img = bpy.data.images.new(name, w, h, alpha=rgba.shape[2] == 4)
    img.pixels.foreach_set(px.ravel())
    img.pack()
    return generated(img)


def noise2(size, cells, seed):
    """Tileable value noise in 0..1, cells blobs across."""
    rng = np.random.default_rng(seed)
    g = rng.random((cells, cells))
    t = np.arange(size) * cells / size
    i0 = np.floor(t).astype(int) % cells
    i1 = (i0 + 1) % cells
    f = t - np.floor(t)
    f = f * f * (3 - 2 * f)
    rows = g[i0] * (1 - f)[:, None] + g[i1] * f[:, None]
    return rows[:, i0] * (1 - f)[None, :] + rows[:, i1] * f[None, :]


def fbm(size, seed, octaves=((4, 0.5), (9, 0.3), (23, 0.2))):
    return sum(noise2(size, c, seed + k) * w for k, (c, w) in enumerate(octaves))


# ---------------------------------------------------------------- the atlas

AO_DISTANCE = 0.25  # cells


class Nodes:
    """Shader nodes for an albedo, one call a node."""

    def __init__(self, nt):
        self.nt = nt

    def new(self, kind):
        return self.nt.nodes.new(kind)

    def link(self, a, b):
        self.nt.links.new(a, b)

    def coord(self, kind="Object"):
        return self.new("ShaderNodeTexCoord").outputs[kind]

    def geometry(self, kind):
        return self.new("ShaderNodeNewGeometry").outputs[kind]

    def rgb(self, c):
        n = self.new("ShaderNodeRGB")
        n.outputs[0].default_value = (*c, 1.0)
        return n.outputs[0]

    def value(self, v):
        n = self.new("ShaderNodeValue")
        n.outputs[0].default_value = v
        return n.outputs[0]

    def scale(self, vec, s):
        """vec times s per axis."""
        n = self.new("ShaderNodeVectorMath")
        n.operation = "MULTIPLY"
        self.link(vec, n.inputs[0])
        n.inputs[1].default_value = s
        return n.outputs[0]

    def noise(self, vec, scale, detail=2.0, rough=0.5, kind="Fac"):
        n = self.new("ShaderNodeTexNoise")
        self.link(vec, n.inputs["Vector"])
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough
        return n.outputs[kind]

    def wave(self, vec, scale, axis="X", distortion=0.0, profile="SIN"):
        n = self.new("ShaderNodeTexWave")
        n.wave_type = "BANDS"
        n.bands_direction = axis
        n.wave_profile = profile
        self.link(vec, n.inputs["Vector"])
        n.inputs["Scale"].default_value = scale
        n.inputs["Distortion"].default_value = distortion
        return n.outputs["Fac"]

    def math(self, op, a, b=None, clamp=False):
        n = self.new("ShaderNodeMath")
        n.operation = op
        n.use_clamp = clamp
        for k, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[k].default_value = v
            else:
                self.link(v, n.inputs[k])
        return n.outputs[0]

    def range(self, v, a, b, lo=0.0, hi=1.0, smooth=True):
        n = self.new("ShaderNodeMapRange")
        n.interpolation_type = "SMOOTHSTEP" if smooth else "LINEAR"
        self.link(v, n.inputs["Value"])
        n.inputs["From Min"].default_value = a
        n.inputs["From Max"].default_value = b
        n.inputs["To Min"].default_value = lo
        n.inputs["To Max"].default_value = hi
        return n.outputs["Result"]

    def mix(self, a, b, fac, blend="MIX"):
        n = self.new("ShaderNodeMix")
        n.data_type = "RGBA"
        n.blend_type = blend
        for sock, v in ((n.inputs["A"], a), (n.inputs["B"], b), (n.inputs["Factor"], fac)):
            if isinstance(v, (int, float)):
                sock.default_value = v
            elif isinstance(v, tuple):
                sock.default_value = (*v, 1.0) if len(v) == 3 else v
            else:
                self.link(v, sock)
        return n.outputs["Result"]

    def mul(self, col, k):
        """A colour times a factor, a socket or a number."""
        n = self.new("ShaderNodeMix")
        n.data_type = "RGBA"
        n.blend_type = "MULTIPLY"
        n.inputs["Factor"].default_value = 1.0
        self.link(col, n.inputs["A"])
        if isinstance(k, (int, float)):
            n.inputs["B"].default_value = (k, k, k, 1.0)
        else:
            c = self.new("ShaderNodeCombineColor")
            for i in range(3):
                self.link(k, c.inputs[i])
            self.link(c.outputs[0], n.inputs["B"])
        return n.outputs["Result"]

    def image(self, img, uv="proj"):
        """A picture through a UV map of its own, its colour and alpha."""
        m = self.new("ShaderNodeUVMap")
        m.uv_map = uv
        t = self.new("ShaderNodeTexImage")
        t.image = img
        t.extension = "CLIP"
        self.link(m.outputs["UV"], t.inputs["Vector"])
        return t.outputs["Color"], t.outputs["Alpha"]


def project_uv(ob, name, origin, u_axis, v_axis, size):
    """A UV map laid by projecting the part along the third axis: a point
    p lands at ((p - origin) . u_axis / size[0], (p - origin) . v_axis / size[1]).
    For pictures the bake reads, such as painted eyes; it is gone before
    export."""
    me = ob.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.new(name=name)
    for loop in me.loops:
        p = me.vertices[loop.vertex_index].co - Vector(origin)
        uv.data[loop.index].uv = (p.dot(Vector(u_axis)) / size[0], p.dot(Vector(v_axis)) / size[1])
    me.uv_layers.active_index = 0
    return uv


def unwrap(obs, margin=0.002, scale=None):
    """One UV layout over every part: each part's islands are unwrapped by
    angle, then all packed together by their shapes, margin apart as a share
    of the picture's side. scale {object name: k} gives a part k times the
    texels of the rest, for a face that is looked at."""
    meshes = [o for o in obs if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        o.data.uv_layers.active_index = 0
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in meshes:
        k = (scale or {}).get(o.name, 1.0)
        if k != 1.0:
            for d in o.data.uv_layers[0].data:
                d.uv = d.uv * k
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(margin=margin, rotate=True, scale=True, shape_method="CONCAVE", margin_method="FRACTION")
    bpy.ops.object.mode_set(mode="OBJECT")


def cycles(samples, gpu=True):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    if gpu:
        try:
            prefs = bpy.context.preferences.addons["cycles"].preferences
            for kind in ("OPTIX", "CUDA"):
                try:
                    prefs.compute_device_type = kind
                    break
                except TypeError:
                    continue
            prefs.get_devices()
            for d in prefs.devices:
                d.use = d.type != "CPU"
            scene.cycles.device = "GPU" if any(d.use for d in prefs.devices) else "CPU"
        except Exception as e:  # a machine without a GPU bakes on the CPU
            print("UNITKIT_CPU", e)
    return scene


# Looks by material name, read by the bake: albedo(n) a colour socket,
# height(n) a bump in cells for the normal map, rough and metal sockets or
# numbers for the metal and roughness map; bump is the height's distance
# in cells, ao how much the part's own occlusion darkens its colour.
LOOKS = {}


def look(name, albedo=None, height=None, rough=0.6, metal=0.0, bump=1.0, ao=1.0):
    LOOKS[name] = dict(albedo=albedo, height=height, rough=rough, metal=metal, bump=bump, ao=ao)


def bake_maps(obs, size=2048, name="atlas", samples=48, ao=0.6, margin=8):
    """Bakes three pictures through the parts' one UV layout: the albedo,
    darkened in each part's own creases, a tangent-space normal map from
    each look's height, and metal (blue) and roughness (green). Then each
    material is made plain glTF PBR reading them. Returns the pictures."""
    meshes = [o for o in obs if o.type == "MESH"]
    scene = cycles(samples)
    imgs = {}
    for k in ("albedo", "normal", "orm"):
        im = bpy.data.images.new("%s_%s" % (name, k), size, size, alpha=False)
        if k != "albedo":
            im.colorspace_settings.name = "Non-Color"
        imgs[k] = im
    mats = sorted({m for o in meshes for m in o.data.materials if m is not None}, key=lambda m: m.name)
    rigs = {}
    for m in mats:
        nt = m.node_tree
        n = Nodes(nt)
        L = LOOKS.get(m.name, {})
        col = L["albedo"](n) if L.get("albedo") else n.rgb(tuple(m.get("uk_rgb", (0.8, 0.8, 0.8))))
        rough, metal = L.get("rough", 0.6), L.get("metal", 0.0)
        rough = rough(n) if callable(rough) else n.value(rough)
        metal = metal(n) if callable(metal) else n.value(metal)
        bsdf = next(x for x in nt.nodes if x.type == "BSDF_PRINCIPLED")
        out = next(x for x in nt.nodes if x.type == "OUTPUT_MATERIAL")
        if L.get("height"):
            bump = n.new("ShaderNodeBump")
            bump.inputs["Strength"].default_value = 1.0
            bump.inputs["Distance"].default_value = L.get("bump", 1.0)
            n.link(L["height"](n), bump.inputs["Height"])
            n.link(bump.outputs["Normal"], bsdf.inputs["Normal"])
        occ = n.new("ShaderNodeAmbientOcclusion")
        occ.only_local = True
        occ.samples = 16
        occ.inputs["Distance"].default_value = AO_DISTANCE
        k = ao * L.get("ao", 1.0)
        dark = n.math("ADD", n.math("MULTIPLY", occ.outputs["AO"], k), 1.0 - k)
        albedo = n.mul(col, dark)
        orm = n.new("ShaderNodeCombineColor")
        orm.inputs[0].default_value = 1.0
        n.link(rough, orm.inputs[1])
        n.link(metal, orm.inputs[2])
        emit = n.new("ShaderNodeEmission")
        tex = n.new("ShaderNodeTexImage")
        nt.nodes.active = tex
        rigs[m] = dict(n=n, bsdf=bsdf, out=out, albedo=albedo, orm=orm.outputs[0], emit=emit, tex=tex)
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    scene.render.bake.margin = margin
    scene.render.bake.use_clear = True
    for kind, source in (("normal", None), ("albedo", "albedo"), ("orm", "orm")):
        for m, r in rigs.items():
            r["tex"].image = imgs[kind]
            r["n"].nt.nodes.active = r["tex"]
            if source is None:
                r["n"].link(r["bsdf"].outputs[0], r["out"].inputs["Surface"])
            else:
                r["n"].link(r[source], r["emit"].inputs["Color"])
                r["n"].link(r["emit"].outputs[0], r["out"].inputs["Surface"])
        scene.cycles.samples = samples if kind == "albedo" else 8
        if kind == "normal":
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
        else:
            bpy.ops.object.bake(type="EMIT")
        print("UNITKIT_BAKED", kind, size, flush=True)
    for k, im in imgs.items():
        if k != "normal":
            # colour and metal-roughness go out as JPEG, a third of the size; the normal map stays exact
            path = os.path.join(bpy.app.tempdir, "%s_%s.jpg" % (name, k))
            im.file_format = "JPEG"
            im.save(filepath=path, quality=92)
            im.filepath = path
            im.source = "FILE"
            im.reload()
        im.pack()
        generated(im)
    # only the atlas's own UVs go out; the game reads TEXCOORD_0 alone
    for o in meshes:
        while len(o.data.uv_layers) > 1:
            o.data.uv_layers.remove(o.data.uv_layers[1])
    for m in mats:
        plain(m, imgs)
    return imgs


def plain(m, imgs):
    """The material as it ships: Principled reading the three pictures."""
    nt = m.node_tree
    b0 = next(x for x in nt.nodes if x.type == "BSDF_PRINCIPLED")
    emit = tuple(b0.inputs["Emission Color"].default_value)
    strength = b0.inputs["Emission Strength"].default_value
    for x in list(nt.nodes):
        nt.nodes.remove(x)
    n = Nodes(nt)
    out = n.new("ShaderNodeOutputMaterial")
    b = n.new("ShaderNodeBsdfPrincipled")
    b.inputs["Emission Color"].default_value = emit
    b.inputs["Emission Strength"].default_value = strength
    t = n.new("ShaderNodeTexImage")
    t.image = imgs["albedo"]
    n.link(t.outputs["Color"], b.inputs["Base Color"])
    t = n.new("ShaderNodeTexImage")
    t.image = imgs["orm"]
    sep = n.new("ShaderNodeSeparateColor")
    n.link(t.outputs["Color"], sep.inputs[0])
    n.link(sep.outputs["Green"], b.inputs["Roughness"])
    n.link(sep.outputs["Blue"], b.inputs["Metallic"])
    t = n.new("ShaderNodeTexImage")
    t.image = imgs["normal"]
    nm = n.new("ShaderNodeNormalMap")
    n.link(t.outputs["Color"], nm.inputs["Color"])
    n.link(nm.outputs["Normal"], b.inputs["Normal"])
    n.link(b.outputs[0], out.inputs["Surface"])
    m[okpaint.GENERATED] = True


# ---------------------------------------------------------------- export

def export(root, path):
    """Exports the rig under root, every piece node and its parts, as the
    game reads it, and checks it may ship (okpaint.check)."""
    bpy.ops.object.select_all(action="DESELECT")
    stack = [root]
    while stack:
        o = stack.pop()
        o.select_set(True)
        stack += list(o.children)
    bpy.context.view_layer.objects.active = root
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True,
                              export_extras=True, export_apply=True, export_vertex_color="NONE",
                              export_tangents=False, export_image_format="AUTO", export_materials="EXPORT")
    patch_glb(path, _alpha_modes)
    problems = okpaint.check(path)
    if problems:
        raise okpaint.PaintError("; ".join(problems))
    print("UNITKIT_EXPORT", path, os.path.getsize(path), "bytes")
    return path


def _alpha_modes(j):
    """Materials marked uk_mask in their extras cut out at that alpha, seen
    from both sides."""
    for m in j.get("materials", []):
        cut = m.get("extras", {}).get("uk_mask")
        if cut is not None:
            m["alphaMode"] = "MASK"
            m["alphaCutoff"] = cut
            m["doubleSided"] = True


def patch_glb(path, fn):
    """Rewrites a glb's JSON chunk through fn(json)."""
    data = open(path, "rb").read()
    n = struct.unpack_from("<I", data, 12)[0]
    j = json.loads(data[20:20 + n])
    fn(j)
    body = json.dumps(j, separators=(",", ":")).encode("utf-8")
    body += b" " * (-len(body) % 4)
    rest = data[20 + n:]
    head = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(body) + len(rest)) + struct.pack("<II", len(body), 0x4E4F534A)
    open(path, "wb").write(head + body + rest)


# ---------------------------------------------------------------- poses from the engine

class Poses:
    """The script's poses as the engine gives them (UnitRigDump.cs): for an
    animation and a frame (15 a second), where each piece's rest geometry
    goes, as a matrix in this frame, and whether it is hidden."""

    def __init__(self, path):
        d = json.load(open(path))
        self.scale = d["scale"]
        self.names = [p["name"] for p in d["pieces"]]
        self.rest = []
        for p in d["pieces"]:
            o = Vector(p["offset"]) * self.scale
            self.rest.append(o + (self.rest[p["parent"]] if p["parent"] >= 0 else Vector()))
        self.anims = d["animations"]

    @staticmethod
    def _m(a):
        return Matrix(((a[0], a[1], a[2], a[3]), (a[4], a[5], a[6], a[7]), (a[8], a[9], a[10], a[11]), (0, 0, 0, 1)))

    def frames(self, anim):
        return len(self.anims[anim])

    def at(self, anim, frame):
        """{piece name: (matrix, hidden)}; matrices map rest to posed here."""
        f = self.anims[anim][min(frame, len(self.anims[anim]) - 1)]
        unscale = Matrix.Diagonal((1 / self.scale, 1 / self.scale, 1 / self.scale, 1))
        posed = [self._m(e["m"]) @ unscale for e in f]
        root = posed[0].inverted()
        swap = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
        out = {}
        for i, name in enumerate(self.names[:len(f)]):
            d = root @ posed[i] @ Matrix.Translation(-self.rest[i])
            out[name] = (swap @ d @ swap, bool(f[i]["h"]))
        return out


def pose(nodes, rig, poses, anim, frame):
    """Puts the piece nodes where the script has them; a hidden piece's
    parts are hidden too."""
    p = poses.at(anim, frame)
    for piece in rig.pieces:
        ob = nodes.get(piece.name)
        if ob is None or piece.name not in p:
            continue
        m, hidden = p[piece.name]
        ob.matrix_world = m @ Matrix.Translation(piece.pivot)
        bpy.context.view_layer.update()
        for c in ob.children:
            if c.type == "MESH":
                c.hide_render = hidden


def rest(nodes, rig):
    for piece in rig.pieces:
        ob = nodes.get(piece.name)
        if ob is not None:
            ob.matrix_world = Matrix.Translation(piece.pivot)
            bpy.context.view_layer.update()
            for c in ob.children:
                if c.type == "MESH":
                    c.hide_render = False


# ---------------------------------------------------------------- a human head (CC0)

# Blender Studio's Human Base Meshes, CC0 (docs/ASSET_CREDITS.md). Fetched
# into the assets folder on first use; never kept in the repository.
HUMAN_BUNDLE = "human-base-meshes-bundle-v1.4.1"
HUMAN_BUNDLE_URL = "https://mirror.blender.org/demo/asset-bundles/human-base-meshes/" + HUMAN_BUNDLE + ".zip"
HUMAN_HEAD = "GEO-head_animation_realistic"


def human_bundle(assets):
    """The bundle's .blend, downloaded and unpacked into assets if missing."""
    import urllib.request
    import zipfile
    blend = os.path.join(assets, HUMAN_BUNDLE, "human_base_meshes_bundle.blend")
    if not os.path.exists(blend):
        os.makedirs(assets, exist_ok=True)
        z = os.path.join(assets, HUMAN_BUNDLE + ".zip")
        if not os.path.exists(z):
            urllib.request.urlretrieve(HUMAN_BUNDLE_URL, z)
        zipfile.ZipFile(z).extractall(assets)
    return blend


def human_head(assets, eye_z, neck=(0.0, 0.03), scale=2.35, cut_z=None, shape=None):
    """The bundle's realistic head, its neck's axis standing at neck (x, y)
    and its eyes at height eye_z, scale cells to a metre. shape(p, marks)
    moves a vertex given in the bundle's metres, centred between the eyes,
    for a face of the unit's own. Returns the head and its eyes as
    (centre, radius) pairs, in this frame; the neck is cut at cut_z."""
    path = human_bundle(assets)
    names = [HUMAN_HEAD, HUMAN_HEAD + ".sclera.L", HUMAN_HEAD + ".sclera.R"]
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in names]
    objs = {o.name: o for o in dst.objects}
    for o in objs.values():
        bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.update()
    head = objs[HUMAN_HEAD]
    for m in list(head.modifiers):
        head.modifiers.remove(m)
    mw = head.matrix_world.copy()
    eyes = []
    for side in ("L", "R"):
        e = objs[HUMAN_HEAD + ".sclera." + side]
        ws = [e.matrix_world @ v.co for v in e.data.vertices]
        c = sum(ws, Vector()) / len(ws)
        r = max((w - c).length for w in ws)
        eyes.append((c, r))
        bpy.data.objects.remove(e)
    me = head.data
    me.transform(mw)
    head.matrix_world = Matrix.Identity(4)
    centre = (eyes[0][0] + eyes[1][0]) / 2
    ring = [v.co.copy() for v in me.vertices if abs(v.co.z - (centre.z - 0.14)) < 0.008]
    neck_c = sum(ring, Vector()) / max(1, len(ring))
    marks = {"eye": centre, "eyes": [e[0] - centre for e in eyes], "neck": neck_c - centre}
    for v in me.vertices:
        p = v.co - centre
        if shape is not None:
            p = shape(p, marks)
        v.co = p
    # into this frame: scaled, Y forward as here, the neck's axis at neck
    t = Matrix.Translation(Vector((neck[0], neck[1], eye_z)) - Vector((marks["neck"].x, marks["neck"].y, 0.0)) * scale)
    me.transform(t @ Matrix.Scale(scale, 4))
    me.update()
    out_eyes = [(t @ (Matrix.Scale(scale, 4) @ (c - centre)), r * scale) for c, r in eyes]
    if cut_z is not None:
        bm = bmesh.new()
        bm.from_mesh(me)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, cut_z), plane_no=(0, 0, 1), clear_inner=True)
        bm.to_mesh(me)
        bm.free()
    for p in me.polygons:
        p.use_smooth = True
    head.name = "humanhead"
    return head, out_eyes


# ---------------------------------------------------------------- renders

def stage(sky=(0.42, 0.47, 0.55), strength=0.9, sun=3.2, sun_dir=(-35, 0, -150), ground=True, rim=1.1, rim_from=(-0.6, 0.75, 0.5)):
    """A plain daylight stage: a warm sun from the camera's left and
    above, a cool light from behind the other side, a blue sky and,
    optionally, a ground that catches shadows."""
    scene = bpy.context.scene
    w = bpy.data.worlds.new("uk_world")
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes["Background"]
    bg.inputs["Strength"].default_value = strength
    # paler at the horizon and deeper overhead, so metal has something to reflect
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(nt.nodes.new("ShaderNodeTexCoord").outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    nt.links.new(sep.outputs["Z"], ramp.inputs[0])
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    stops = ((0.0, (0.2, 0.21, 0.18)), (0.03, (0.78, 0.8, 0.8)), (0.3, tuple(c * 1.05 for c in sky)), (1.0, tuple(c * 0.7 for c in sky)))
    els = ramp.color_ramp.elements
    els[0].position, els[0].color = stops[0][0], (*stops[0][1], 1.0)
    els[1].position, els[1].color = stops[-1][0], (*stops[-1][1], 1.0)
    for pos, col in stops[1:-1]:
        els.new(pos).color = (*col, 1.0)
    scene.world = w
    s = bpy.data.objects.new("uk_sun", bpy.data.lights.new("uk_sun", "SUN"))
    s.data.energy = sun
    s.data.color = (1.0, 0.95, 0.86)
    s.data.angle = math.radians(2.5)
    s.rotation_euler = [math.radians(a) for a in sun_dir]
    scene.collection.objects.link(s)
    if rim:
        r = bpy.data.objects.new("uk_rim", bpy.data.lights.new("uk_rim", "SUN"))
        r.data.energy = rim
        r.data.color = (0.82, 0.88, 1.0)
        r.data.angle = math.radians(8.0)
        r.rotation_euler = (-Vector(rim_from)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(r)
    if ground:
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=30)
        g = mesh("uk_ground", bm, [material("uk_ground", (0.32, 0.36, 0.26), rough=0.95)])
        g.is_shadow_catcher = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    return scene


def camera(name, target, yaw, pitch, dist, lens=50.0, ortho=None):
    """A camera looking at target from yaw degrees round from the front
    (0 is the front, -Y, positive toward +X) and pitch degrees up."""
    cam = bpy.data.objects.get(name)
    if cam is None:
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        bpy.context.scene.collection.objects.link(cam)
    y, p = math.radians(yaw), math.radians(pitch)
    d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
    if ortho:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho
    else:
        cam.data.type = "PERSP"
        cam.data.lens = lens
    cam.data.clip_end = 500
    return cam


# The classic camera: the original draws a point x east, y north and z up
# at column 16 x and row -16 y - 8 z, which is an orthographic view down
# the line (0, 1, -2), from the south.
CLASSIC_PITCH = math.degrees(math.atan(2.0))


def render(path, cam, width=800, height=800, samples=48, transparent=True):
    scene = cycles(samples)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = transparent
    scene.cycles.use_denoising = True
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


# ---------------------------------------------------------------- the original, for review only

_GAF_PALETTES = (("ara", "ara_textures.pcx"), ("tar", "tar_textures.pcx"), ("ver", "ver_textures.pcx"),
                 ("zon", "zon_textures.pcx"), ("zho", "zon_textures.pcx"), ("cre", "cre_textures.pcx"))


def _gaf_index():
    idx = {}
    names = sorted({f for a in _files() for f in a.files if f.endswith(".gaf")},
                   key=lambda f: (not f.startswith("textures/"), f))
    for g in names:
        d = game_file(g)
        if len(d) < 12:
            continue
        for e in range(struct.unpack_from("<I", d, 4)[0]):
            eo = struct.unpack_from("<I", d, 12 + 4 * e)[0]
            name = d[eo + 8:eo + 40].split(b"\0")[0].decode("latin-1").lower()
            idx.setdefault(name, (g, eo))
    return idx


def _gaf_frame(d, fo):
    w, h, ox, oy, key, comp, subs, _u, px = struct.unpack_from("<HHhhBBHII", d, fo)
    out = bytearray([key]) * (w * h)
    if comp == 0:
        out[:] = d[px:px + w * h]
    else:
        p = px
        for row in range(h):
            count = struct.unpack_from("<H", d, p)[0]
            p += 2
            end, x = p + count, 0
            while p < end:
                b = d[p]
                p += 1
                if b & 1:
                    x += b >> 1
                elif b & 3 == 0:
                    n = (b >> 2) + 1
                    for i in range(n):
                        if x + i < w:
                            out[row * w + x + i] = d[p + i]
                    p += n
                    x += n
                else:
                    n = (b >> 2) + 1
                    for i in range(n):
                        if x + i < w:
                            out[row * w + x + i] = d[p]
                    p += 1
                    x += n
            p = end
    return w, h, key, bytes(out)


def original(rig, colour=0, suffix="_orig"):
    """The original model at rest, textured from the player's files, one
    object per piece under nodes of its own. For review renders only: it
    holds the original's art and is never exported."""
    idx = _gaf_index()
    pals, imgs, mats = {}, {}, {}
    coll = bpy.data.collections.new("original")
    bpy.context.scene.collection.children.link(coll)

    def picture(tex):
        key = tex.lower()
        if key in imgs:
            return imgs[key]
        if key not in idx:
            imgs[key] = None
            return None
        g, eo = idx[key]
        d = game_file(g)
        frames = struct.unpack_from("<H", d, eo)[0]
        fo = struct.unpack_from("<I", d, eo + 40 + 8 * min(colour, frames - 1))[0]
        w, h, k, px = _gaf_frame(d, fo)
        base = os.path.basename(g)[:3].lower()
        pal = next((p for pre, p in _GAF_PALETTES if base == pre), "gameart.pcx")
        if pal not in pals:
            raw = game_file("palettes/" + pal)[-768:]
            pals[pal] = np.frombuffer(raw, np.uint8).reshape(256, 3) / 255.0
        a = np.frombuffer(px, np.uint8).reshape(h, w)
        rgba = np.ones((h, w, 4), np.float32)
        rgba[:, :, :3] = pals[pal][a] ** 2.2
        rgba[:, :, 3] = (a != k)
        im = bpy.data.images.new(key + suffix, w, h, alpha=True, float_buffer=True)
        # the colour space first: changing it later makes the picture again, black
        im.colorspace_settings.name = "Linear Rec.709"
        im.pixels.foreach_set(rgba[::-1].ravel())
        im.pack()
        imgs[key] = im
        return im

    def mat(tex):
        if tex in mats:
            return mats[tex]
        m = bpy.data.materials.new((tex or "flat") + suffix)
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Roughness"].default_value = 0.8
        im = picture(tex) if tex else None
        if im is not None:
            t = m.node_tree.nodes.new("ShaderNodeTexImage")
            t.image = im
            t.interpolation = "Closest"
            m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
            m.node_tree.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
        else:
            b.inputs["Base Color"].default_value = (0.25, 0.25, 0.27, 1)
        mats[tex] = m
        return m

    nodes = {}
    for p in rig.pieces:
        node = bpy.data.objects.new(p.name + suffix, None)
        coll.objects.link(node)
        if p.parent >= 0:
            node.parent = nodes[rig.pieces[p.parent].name]
            node.location = p.pivot - rig.pieces[p.parent].pivot
        else:
            node.location = p.pivot
        nodes[p.name] = node
        prims = [pr for pr in p.prims[(1 if p.selection else 0):] if len(pr[0]) >= 3]
        if not prims:
            continue
        bm = bmesh.new()
        uv = bm.loops.layers.uv.new("UVMap")
        vs = [bm.verts.new(from_3do(*v)) for v in p.verts]
        slots = []
        corners = ((0, 0), (1, 0), (1, 1), (0, 1))
        for ids, tex, _col in prims:
            try:
                f = bm.faces.new([vs[i] for i in ids])
            except ValueError:
                continue
            if tex not in slots:
                slots.append(tex)
            f.material_index = slots.index(tex)
            for k, loop in enumerate(f.loops):
                u, v = corners[k if k < 4 else 2]
                loop[uv].uv = (u, 1 - v)
        me = bpy.data.meshes.new(p.name + suffix)
        bm.to_mesh(me)
        bm.free()
        for tex in slots:
            me.materials.append(mat(tex))
        ob = bpy.data.objects.new(p.name + suffix + "_mesh", me)
        coll.objects.link(ob)
        ob.parent = node
    bpy.context.view_layer.update()
    return nodes, coll
