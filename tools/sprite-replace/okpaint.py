"""Models ship as geometry only. A material painted with the original's art
names its picture in okPaint extras instead of holding it, and the game
paints it at load from the player's own files (GlbLoader.cs):

    "okPaint": {"kind": "feature" | "texture", "name": ..., "world": ...,
                "gain": 1.3, "bleed": true, "alpha": "opaque" | "mask",
                "size": [w, h]}

plus "border": true when a cut-out clears its outer ring, and "tint" when
the picture is multiplied. "okFallback" beside it is the colour drawn
without game files.

Two more keys make the picture from the player's pixels and the model's
own faces, never from anything shipped:

    "mask": {"cover": "others", "hotspot": [hx, hy]}

keeps a texel where the picture is opaque (its transparent colour clear)
and no face of the model in another material covers it, as the classic
camera sees it: a point (x, y, z) of the model in Blender's frame, one
unit a cell, lands on the picture at column hx + 16 x and row
hy - 16 y - 8 z. A ruin's rubble skirt drops what its walls stand on.

    "delit": {"hotspot": [hx, hy], "light": [x, y, z], "ambient": a,
              "direct": d, "stones": [{"grey": [r, g, b], "dark": k}, ...]}

is henge.py's standing stone paint: under each texel the face the classic
camera sees first gives its stone (TEXCOORD_1 u, rounded down) and its
light, a + d max(0, n.light), which is divided out, and each stone's
broad shading is flattened toward its grey, the carvings kept down to
dark of it.

An okPaint holds only such numbers, never a picture, so the check fails
one with keys it does not know or more than MAX_NUMBERS numbers. A texture made here from noise and a few numbers
(bark, leaves, tiles) is not the original's art and ships as it is, once
its maker marks it with generated().

Everything the tools here make must pass check(), since they work beside
the player's files. An artist's own picture in a model sent as a pull
request ships once the pull request is merged, and scripts/check-models.py
takes it with own=True. A carved model (okCarved) never holds one.

In Blender:

    okpaint.generated(img)                   mark a texture made from noise and numbers
    okpaint.export(ob, glb_path)             strip painted pictures, export, check

OK_KEEP_PIXELS=1 in the environment, or keep_pixels=True, exports the
pictures too, for review renders on this machine only; such a file is
stamped okFromPlayersFiles and never ships.

Outside Blender, the check alone, over files or folders:

    python okpaint.py <glb or folder> ...
"""
import json
import os
import struct
import sys

GENERATED = "okGenerated"
PAINT = "okPaint"
FALLBACK = "okFallback"
PLAYERS_FILES = "okFromPlayersFiles"
CARVED = "okCarved"
TEXTURE_SLOTS = ("baseColorTexture", "metallicRoughnessTexture")
PAINT_KEYS = ("kind", "name", "world", "gain", "bleed", "alpha", "size", "border", "tint", "mask", "delit")
MASK_KEYS = ("cover", "hotspot")
DELIT_KEYS = ("hotspot", "light", "ambient", "direct", "stones")
STONE_KEYS = ("grey", "dark")
MAX_NUMBERS = 256
MATERIAL_TEXTURES = ("normalTexture", "occlusionTexture", "emissiveTexture")


class PaintError(RuntimeError):
    pass


def keep_pixels_default():
    return os.environ.get("OK_KEEP_PIXELS", "") not in ("", "0")


def generated(img):
    """Marks a texture made from noise and numbers, which may ship."""
    img[GENERATED] = True
    return img


# ---------------------------------------------------------------- in Blender

def _images(m):
    if m is None or not m.use_nodes or m.node_tree is None:
        return []
    return [n.image for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image is not None]


def _stripped(m):
    """A copy of painted material m without its picture: the fallback colour
    (or the tint) as base colour, and m's other settings and extras."""
    import bpy
    m2 = bpy.data.materials.new(m.name + "_ship")
    m2.use_nodes = True
    b2 = m2.node_tree.nodes["Principled BSDF"]
    b = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if b is not None:
        for k in ("Roughness", "Metallic"):
            b2.inputs[k].default_value = b.inputs[k].default_value
    paint = m[PAINT]
    fb = m.get(FALLBACK)
    tint = float(paint.get("tint", 1.0))
    b2.inputs["Base Color"].default_value = (*fb[:3], 1.0) if fb is not None else (tint, tint, tint, 1.0)
    m2.use_backface_culling = m.use_backface_culling
    for k in m.keys():
        m2[k] = m[k]
    return m2


def export(ob, glb_path, keep_pixels=None):
    """Exports ob alone as glb_path. Each material with okPaint loses its
    picture; any other picture must be marked generated, or this raises.
    Returns how many materials the game paints."""
    import bpy
    keep = keep_pixels_default() if keep_pixels is None else keep_pixels
    me = ob.data
    swapped, marked, painted, local = [], [], 0, False
    for i, m in enumerate(me.materials):
        imgs = _images(m)
        if m is not None and PAINT in m:
            painted += 1
            if not keep and imgs:
                m2 = _stripped(m)
                name = m.name
                m.name = name + "_px"
                m2.name = name
                me.materials[i] = m2
                swapped.append((i, m, m2, name))
            continue
        art = [im.name for im in imgs if not im.get(GENERATED)]
        local = local or bool(art)
        if art and not keep:
            raise PaintError("%s: material %s holds %s, which is neither marked generated nor painted at load"
                             % (os.path.basename(glb_path), m.name, ", ".join(art)))
        if imgs and not art:
            m[GENERATED] = True
            marked.append(m)
    flag = ob.pop(PLAYERS_FILES, None)
    if keep and (painted or local):
        ob[PLAYERS_FILES] = True
    try:
        os.makedirs(os.path.dirname(os.path.abspath(glb_path)), exist_ok=True)
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.gltf(filepath=glb_path, export_format="GLB", use_selection=True,
                                  export_yup=True, export_extras=True)
    finally:
        for i, m, m2, name in swapped:
            me.materials[i] = m
            m2.name = name + "_ship"
            m.name = name
            bpy.data.materials.remove(m2)
        for m in marked:
            del m[GENERATED]
        ob.pop(PLAYERS_FILES, None)
        if flag is not None:
            ob[PLAYERS_FILES] = flag
    if not keep:
        problems = check(glb_path)
        if problems:
            raise PaintError("; ".join(problems))
    print("OKPAINT_EXPORT", glb_path, painted, "painted at load", "with pixels" if keep else "", flush=True)
    return painted


# ---------------------------------------------------------------- anywhere

def glb_json(path):
    with open(path, "rb") as f:
        data = f.read(20)
        if len(data) < 20 or data[:4] != b"glTF":
            raise PaintError(path + " is not a glb")
        n = struct.unpack_from("<I", data, 12)[0]
        return json.loads(f.read(n))


def _textures_of(m):
    pbr = m.get("pbrMetallicRoughness", {})
    refs = [pbr[k] for k in TEXTURE_SLOTS if k in pbr] + [m[k] for k in MATERIAL_TEXTURES if k in m]
    return [r.get("index") for r in refs if isinstance(r, dict) and "index" in r]


def _numbers(v, n=None):
    ok = isinstance(v, list) and all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in v)
    return ok and (n is None or len(v) == n)


def _count(v):
    if isinstance(v, dict):
        return sum(_count(x) for x in v.values())
    if isinstance(v, list):
        return sum(_count(x) for x in v)
    return 1 if isinstance(v, (int, float)) and not isinstance(v, bool) else 0


def paint_problems(p):
    """Why an okPaint is not a recipe this game reads, as a list."""
    if not isinstance(p, dict):
        return ["okPaint is not an object"]
    out = []
    odd = sorted(set(p) - set(PAINT_KEYS))
    if odd:
        out.append("okPaint has keys %s" % ", ".join(odd))
    if p.get("kind") not in ("feature", "texture"):
        out.append("okPaint kind %r" % p.get("kind"))
    for k in ("kind", "name", "world", "alpha"):
        if k in p and (not isinstance(p[k], str) or len(p[k]) > 64):
            out.append("okPaint %s is not a short name" % k)
    mask = p.get("mask")
    if mask is not None:
        if (not isinstance(mask, dict) or set(mask) - set(MASK_KEYS) or mask.get("cover") != "others"
                or not _numbers(mask.get("hotspot"), 2)):
            out.append("okPaint mask is not a cover by the model's own faces")
        if p.get("alpha") != "mask":
            out.append("okPaint mask without alpha mask")
    delit = p.get("delit")
    if delit is not None:
        stones = delit.get("stones") if isinstance(delit, dict) else None
        if (not isinstance(delit, dict) or set(delit) - set(DELIT_KEYS) or not _numbers(delit.get("hotspot"), 2)
                or not _numbers(delit.get("light"), 3) or not _numbers([delit.get("ambient"), delit.get("direct")])
                or not isinstance(stones, list) or not stones
                or any(not isinstance(st, dict) or set(st) - set(STONE_KEYS) or not _numbers(st.get("grey"), 3)
                       or not _numbers([st.get("dark", 0.15)]) for st in stones)):
            out.append("okPaint delit is not a list of stones and a light")
    n = _count(p)
    if n > MAX_NUMBERS:
        out.append("okPaint holds %d numbers, more than a recipe needs (%d)" % (n, MAX_NUMBERS))
    return out


def _extras(o):
    return o.get("extras") if isinstance(o, dict) and isinstance(o.get("extras"), dict) else {}


def own_pictures(j):
    """The indices of the pictures in no material marked generated or
    painted at load: the artist's own."""
    textures = j.get("textures", [])
    made = set()
    for m in j.get("materials", []):
        ex = _extras(m)
        if PAINT in ex or ex.get(GENERATED):
            made |= {textures[t].get("source") for t in _textures_of(m) if isinstance(t, int) and 0 <= t < len(textures)}
    return [k for k in range(len(j.get("images", []))) if k not in made]


def content_problems(j, own=False):
    """Why a model's glTF JSON may not ship, as a list, empty when it may:
    a painted material holds no picture and only a recipe, nothing is
    stamped okFromPlayersFiles, and every picture is marked generated,
    except that with own the artist's own pictures ship unless the model
    is carved."""
    problems = []
    textures = j.get("textures", [])
    for m in j.get("materials", []):
        ex = _extras(m)
        if PAINT in ex:
            problems += ["material %s: %s" % (m.get("name"), w) for w in paint_problems(ex[PAINT])]
            if any(isinstance(t, int) and 0 <= t < len(textures) for t in _textures_of(m)):
                problems.append("painted material %s still holds a picture" % m.get("name"))
    carved = any(CARVED in _extras(nd) for nd in j.get("nodes", []))
    if carved or not own:
        images = j.get("images", [])
        why = "a carved model holds only generated pictures" if carved else "it is not from a material marked generated"
        for k in own_pictures(j):
            label = images[k].get("name", k) if isinstance(images[k], dict) else k
            problems.append("picture %s may not ship, since %s" % (label, why))
    for nd in j.get("nodes", []) + j.get("meshes", []) + j.get("scenes", []):
        if PLAYERS_FILES in _extras(nd):
            problems.append("stamped %s, a review copy holding the player's pixels" % PLAYERS_FILES)
            break
    return problems


def check(path, own=False):
    """Why the file may not ship, as a list, empty when it may, by
    content_problems."""
    return ["%s: %s" % (os.path.basename(path), p) for p in content_problems(glb_json(path), own)]


def main(paths):
    files = []
    for p in paths:
        if os.path.isdir(p):
            files += sorted(os.path.join(p, f) for f in os.listdir(p) if f.lower().endswith(".glb"))
        else:
            files.append(p)
    bad = 0
    for f in files:
        for msg in check(f):
            print(msg)
            bad += 1
    print("OKPAINT_CHECK %d files, %d problems" % (len(files), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
