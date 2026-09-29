"""Models ship as geometry only. A material painted with the original's art
names its picture in okPaint extras instead of holding it, and the game
paints it at load from the player's own files (GlbLoader.cs):

    "okPaint": {"kind": "feature" | "texture", "name": ..., "world": ...,
                "gain": 1.3, "bleed": true, "alpha": "opaque" | "mask",
                "size": [w, h]}

plus "border": true when a cut-out clears its outer ring, and "tint" when
the picture is multiplied. "okFallback" beside it is the colour drawn
without game files. A texture made here from noise and a few numbers
(bark, leaves, tiles) is not the original's art and ships as it is, once
its maker marks it with generated().

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
TEXTURE_SLOTS = ("baseColorTexture", "metallicRoughnessTexture")
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


def check(path):
    """Why the file may not ship, as a list, empty when it may: every
    picture in it must belong to materials marked generated, a painted
    material holds no picture, and it carries no okFromPlayersFiles."""
    j = glb_json(path)
    name = os.path.basename(path)
    problems = []
    textures = j.get("textures", [])
    allowed = set()
    for m in j.get("materials", []):
        ex = m.get("extras", {})
        idx = [t for t in _textures_of(m) if t is not None and t < len(textures)]
        srcs = {textures[t].get("source") for t in idx}
        if PAINT in ex and idx:
            problems.append("%s: painted material %s still holds a picture" % (name, m.get("name")))
        elif ex.get(GENERATED):
            allowed |= srcs
    for k, im in enumerate(j.get("images", [])):
        if k not in allowed:
            problems.append("%s: picture %s is not from a material marked generated" % (name, im.get("name", k)))
    for nd in j.get("nodes", []) + j.get("meshes", []) + j.get("scenes", []):
        if PLAYERS_FILES in nd.get("extras", {}):
            problems.append("%s: stamped %s" % (name, PLAYERS_FILES))
    return problems


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
