#!/usr/bin/env python3
"""Check drop-in models the way Studio Mode does, with no Unity and no game files.

    python3 scripts/check-models.py --changed origin/main   what a branch adds or changes
    python3 scripts/check-models.py --all                   every committed model
    python3 scripts/check-models.py path/to/model.glb ...   any files

--sheet FILE draws the new and changed pictures into one PNG for review,
from the checked files alone. It needs Pillow.
"""
import argparse
import hashlib
import io
import json
import math
import os
import re
import struct
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools", "sprite-replace"))
import okpaint  # noqa: E402

FEATURES = "unity/Assets/Overrides/Features/"
UNITS = "unity/Assets/Overrides/Units/"
SAMPLES = "unity/Assets/Game/Studio/Samples/"
NEVER = ("unity/Assets/Overrides/Generated", "unity/Assets/Overrides/Drop")

# Studio Mode's advice (ModelCheck.cs), which reviewers see as notes, and the
# guards past which a model fails, set to catch mistakes rather than to
# budget. Sizes in map cells. A foundation may reach a quarter of the
# model's whole height below the ground.
FEATURE_TRIANGLES, UNIT_TRIANGLES, TEXTURE_SIDE = 3000, 6000, 1024
GUARD_TRIANGLES, GUARD_TEXTURE = 100000, 4096
BIGGEST, SMALLEST = 150.0, 1 / 16
FLOATS, FOUNDATION = 0.15, 0.25
MAX_BYTES = 16 << 20

# The original game's own kinds of file, as ContentCheck.GameFileKinds lists them.
GAME_FILES = {".hpi", ".ufo", ".ccx", ".gp3", ".gaf", ".3do", ".tnt", ".pcx", ".cob", ".fbi", ".gui",
              ".ota", ".kmp", ".tdf"}
# Model files the game does not read in a built copy, or Unity cannot import without Blender.
OTHER_MODELS = {".gltf", ".bin", ".fbx", ".obj", ".blend", ".blend1", ".dae", ".3ds", ".max", ".ma", ".mb"}
UNREADABLE = {"KHR_draco_mesh_compression", "EXT_meshopt_compression", "KHR_meshopt_compression",
              "KHR_mesh_quantization", "KHR_texture_basisu", "EXT_texture_webp", "EXT_texture_avif"}


class Model:
    def __init__(self, path):
        self.path = path
        self.problems, self.notes = [], []
        self.triangles = 0
        self.advice = 0
        self.size = None
        self.pictures = []


class Picture:
    def __init__(self, name, fmt, width, height, data, own):
        self.name, self.format, self.width, self.height, self.own = name, fmt, width, height, own
        self.data = data
        self.sha = hashlib.sha256(data).hexdigest()

    def describe(self):
        return "%s %dx%d %s" % (self.name, self.width, self.height, self.format) if self.format else "%s (unreadable)" % self.name


def git(*args):
    return subprocess.run(["git", "-C", ROOT] + list(args), capture_output=True, text=True, check=True).stdout


def rel(path):
    p = os.path.relpath(os.path.abspath(path), ROOT).replace("\\", "/")
    return p


def kind_of(path):
    """feature, unit, card, sample or None, from where the file sits."""
    folder, name = os.path.split(path)
    folder += "/"
    if folder == FEATURES:
        return "feature"
    if folder == UNITS:
        return "card" if os.path.exists(os.path.join(ROOT, os.path.splitext(path)[0] + ".json")) else "unit"
    if folder == SAMPLES:
        return "sample"
    return None


def read_glb(data, m):
    if len(data) < 20 or data[:4] != b"glTF":
        m.problems.append("is not a glTF binary. Export as glTF Binary (.glb).")
        return None, b""
    version, length = struct.unpack_from("<II", data, 4)
    if version != 2:
        m.problems.append("is glTF version %d, and the game reads version 2." % version)
        return None, b""
    if length != len(data):
        m.problems.append("is cut short or has extra bytes (its header says %d bytes, the file has %d)." % (length, len(data)))
        return None, b""
    n, kind = struct.unpack_from("<II", data, 12)
    if kind != 0x4E4F534A or 20 + n > len(data):
        m.problems.append("has no readable JSON chunk.")
        return None, b""
    try:
        j = json.loads(data[20:20 + n])
    except ValueError:
        m.problems.append("has JSON that does not read.")
        return None, b""
    rest = 20 + n
    bin_ = b""
    if rest + 8 <= len(data):
        bn, bk = struct.unpack_from("<II", data, rest)
        if bk == 0x004E4942:
            bin_ = data[rest + 8:rest + 8 + bn]
    return j, bin_


def picture(b):
    """(format, width, height) from a picture's first bytes."""
    if b[:8] == b"\x89PNG\r\n\x1a\n" and len(b) >= 24:
        w, h = struct.unpack(">II", b[16:24])
        return "PNG", w, h
    if b[:2] == b"\xff\xd8":
        i = 2
        while i + 9 < len(b):
            if b[i] != 0xFF:
                i += 1
                continue
            marker = b[i + 1]
            if marker in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", b[i + 5:i + 9])
                return "JPEG", w, h
            if marker in (0xD8, 0x01) or 0xD0 <= marker <= 0xD7:
                i += 2
                continue
            i += 2 + struct.unpack(">H", b[i + 2:i + 4])[0]
        return "JPEG", 0, 0
    return None, 0, 0


def picture_bytes(j, bin_, im):
    views = j.get("bufferViews", [])
    if not isinstance(im, dict) or not isinstance(im.get("bufferView"), int) or not 0 <= im["bufferView"] < len(views):
        return None
    v = views[im["bufferView"]]
    start = v.get("byteOffset", 0)
    return bin_[start:start + v.get("byteLength", 0)]


def matmul(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def local_matrix(node):
    if "matrix" in node:
        c = node["matrix"]
        return [[c[col * 4 + row] for col in range(4)] for row in range(4)]
    tx, ty, tz = node.get("translation", [0, 0, 0])
    qx, qy, qz, qw = node.get("rotation", [0, 0, 0, 1])
    sx, sy, sz = node.get("scale", [1, 1, 1])
    r = [[1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy - qz * qw), 2 * (qx * qz + qy * qw)],
         [2 * (qx * qy + qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz - qx * qw)],
         [2 * (qx * qz - qy * qw), 2 * (qy * qz + qx * qw), 1 - 2 * (qx * qx + qy * qy)]]
    return [[r[0][0] * sx, r[0][1] * sy, r[0][2] * sz, tx],
            [r[1][0] * sx, r[1][1] * sy, r[1][2] * sz, ty],
            [r[2][0] * sx, r[2][1] * sy, r[2][2] * sz, tz],
            [0, 0, 0, 1]]


def measure(j, m):
    """Triangles and the box round them, as the Studio counts and measures them."""
    nodes, meshes, accessors = j.get("nodes", []), j.get("meshes", []), j.get("accessors", [])
    scenes = j.get("scenes", [])
    if scenes:
        roots = scenes[j.get("scene", 0) if j.get("scene", 0) < len(scenes) else 0].get("nodes", [])
    else:
        children = {c for n in nodes for c in n.get("children", [])}
        roots = [i for i in range(len(nodes)) if i not in children]
    lo, hi = [math.inf] * 3, [-math.inf] * 3
    seen = set()
    stack = [(r, [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]) for r in roots]
    while stack:
        i, parent = stack.pop()
        if i in seen or i >= len(nodes):
            continue
        seen.add(i)
        node = nodes[i]
        world = matmul(parent, local_matrix(node))
        if "mesh" in node and node["mesh"] < len(meshes):
            for p in meshes[node["mesh"]].get("primitives", []):
                if p.get("mode", 4) != 4 or "POSITION" not in p.get("attributes", {}):
                    continue
                pos = accessors[p["attributes"]["POSITION"]]
                count = accessors[p["indices"]]["count"] if "indices" in p else pos["count"]
                m.triangles += count // 3
                if "min" not in pos or "max" not in pos:
                    m.problems.append("has a mesh with no min and max on its positions. Export it again from Blender.")
                    continue
                for k in range(8):
                    c = [pos["max"][a] if k >> a & 1 else pos["min"][a] for a in range(3)]
                    w = [sum(world[r][a] * c[a] for a in range(3)) + world[r][3] for r in range(3)]
                    lo = [min(x, y) for x, y in zip(lo, w)]
                    hi = [max(x, y) for x, y in zip(hi, w)]
        stack += [(c, world) for c in node.get("children", [])]
    return (lo, hi) if lo[0] <= hi[0] else None


def check_glb(path, data, m):
    kind = kind_of(path)
    if kind is None:
        m.problems.append("is not in a folder the game reads. Put it straight in %s (scenery) or %s (units and cards)." % (FEATURES, UNITS))
    name = os.path.splitext(os.path.basename(path))[0]
    if not re.fullmatch(r"[A-Za-z0-9_\-]+", name):
        m.notes.append("its name has spaces or signs, and the game finds a model by the exact name of what it replaces.")
    if len(data) > MAX_BYTES:
        m.problems.append("is %.1f MB, more than a model for the map needs (%d MB at most)." % (len(data) / 1048576, MAX_BYTES >> 20))
    j, bin_ = read_glb(data, m)
    if j is None:
        return
    used = set(j.get("extensionsUsed", [])) | set(j.get("extensionsRequired", []))
    for e in sorted(used & UNREADABLE):
        m.problems.append("uses %s, which the game cannot read. In Blender's glTF export, leave Compression unticked." % e)
    for e in sorted(set(j.get("extensionsRequired", [])) - UNREADABLE):
        m.problems.append("requires the glTF extension %s, which the game does not read." % e)
    for b in j.get("buffers", []):
        if "uri" in b:
            m.problems.append("keeps its data in a separate file. Export as glTF Binary (.glb) so everything travels inside.")
            break

    own = set(okpaint.own_pictures(j))
    for k, im in enumerate(j.get("images", [])):
        label = im.get("name") or "picture %d" % k
        data = picture_bytes(j, bin_, im)
        if data is None:
            m.problems.append("names the picture %s outside the file. Export as glTF Binary (.glb) so pictures travel inside." % label)
            continue
        fmt, w, h = picture(data)
        m.pictures.append(Picture(label, fmt, w, h, data, k in own))
        if fmt is None:
            m.problems.append("has the picture %s in a format the game cannot read. Use PNG or JPEG." % label)
        elif max(w, h) > GUARD_TEXTURE:
            m.problems.append("has the picture %s at %d by %d, more than %d on a side, which no model in the game needs. Scale it down to %d or less."
                              % (label, w, h, GUARD_TEXTURE, TEXTURE_SIDE))
        elif max(w, h) > TEXTURE_SIDE:
            m.notes.append("its picture %s is %d by %d. Studio Mode suggests %d on a side, which is plenty at the game's zoom."
                           % (label, w, h, TEXTURE_SIDE))

    box = measure(j, m)
    if box is None or m.triangles == 0:
        m.problems.append("has nothing to draw. Check that the export included the meshes.")
        return
    m.advice = UNIT_TRIANGLES if kind == "unit" else FEATURE_TRIANGLES
    what = {"unit": "a unit", "card": "a unit card"}.get(kind, "scenery")
    if m.triangles > GUARD_TRIANGLES:
        m.problems.append("has %s triangles, more than the %s any model may have. So many usually means a modifier such as "
                          "Subdivision Surface was applied by mistake." % (format(m.triangles, ","), format(GUARD_TRIANGLES, ",")))
    elif m.triangles > m.advice:
        m.notes.append("it has %s triangles, more than the %s Studio Mode suggests for %s, since a map can show hundreds at once."
                       % (format(m.triangles, ","), format(m.advice, ","), what))
    lo, hi = box
    size = [h - l for l, h in zip(lo, hi)]
    m.size = size
    across, tall = max(size[0], size[2]), size[1]
    if max(across, tall) > BIGGEST:
        m.problems.append("is %.0f cells big. It was probably exported in centimetres. One Blender unit is one map cell." % max(across, tall))
    elif max(across, tall) < SMALLEST:
        m.problems.append("is smaller than one pixel of the original. One Blender unit is one map cell, 16 pixels.")
    if lo[1] > FLOATS:
        m.problems.append("floats %.2f cells above the ground. Put its lowest point at the origin's height." % lo[1])
    elif -lo[1] > max(FLOATS, FOUNDATION * size[1]) + 1e-6:
        m.problems.append("reaches %.2f cells into the ground, more than the quarter of its height (%.2f) a foundation may take."
                          % (-lo[1], FOUNDATION * size[1]))
    off = math.hypot((lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2)
    if off > max(0.3, 0.2 * across):
        m.notes.append("its middle is %.1f cells from the origin. The origin should be the middle of its base." % off)

    # okpaint.py's rules: recipes only, nothing made from the player's own
    # files, and the artist's own pictures, which ship once merged.
    m.problems += okpaint.content_problems(j, own=True)


def check_json(path, data, m):
    try:
        j = json.loads(data)
    except ValueError:
        m.problems.append("does not read as JSON.")
        return
    if os.path.basename(path) == "flight.json":
        return
    if not os.path.exists(os.path.join(ROOT, os.path.splitext(path)[0] + ".glb")):
        m.problems.append("has no model of the same name beside it.")
    if not isinstance(j, dict) or not isinstance(j.get("replacesPiece"), str) or not j["replacesPiece"]:
        m.problems.append("needs replacesPiece, the card piece the model stands in for.")
    elif "replacesTexture" in j and not isinstance(j["replacesTexture"], str):
        m.problems.append("has a replacesTexture that is not a name.")


def check(path):
    m = Model(path)
    full = os.path.join(ROOT, path)
    ext = os.path.splitext(path)[1].lower()
    if ext in GAME_FILES:
        m.problems.append("is one of the original game's own kinds of file, which never go in the repository.")
        return m
    if path.startswith(NEVER):
        m.problems.append("is in a folder that stays on your own computer. Use Studio Mode's Use in game to put a model in the game.")
        return m
    if ext in OTHER_MODELS and path.startswith("unity/Assets/Overrides/"):
        m.problems.append("is not a .glb. Export it as glTF Binary (.glb), and keep the source files outside the Unity project.")
        return m
    if not os.path.isfile(full):
        return None
    with open(full, "rb") as f:
        data = f.read()
    if ext == ".glb":
        check_glb(path, data, m)
    elif ext == ".json" and path.startswith(UNITS):
        check_json(path, data, m)
    else:
        return None
    if path.startswith((FEATURES, UNITS)) and not os.path.isfile(full + ".meta"):
        m.problems.append("has no .meta file beside it. Open the project in Unity once and commit the %s.meta it makes." % os.path.basename(path))
    return m


def changed(base):
    out = git("diff", "--name-only", "--diff-filter=ACMR", base + "...HEAD")
    return [p for p in out.splitlines() if p]


def everything():
    out = git("ls-files", "--", "unity/Assets/Overrides", SAMPLES)
    return [p for p in out.splitlines() if p.lower().endswith((".glb", ".json"))]


def pictures_at(rev, path):
    """The hashes of the pictures in path as it was at rev."""
    r = subprocess.run(["git", "-C", ROOT, "show", "%s:%s" % (rev, path)], capture_output=True)
    if r.returncode != 0 or not path.lower().endswith(".glb"):
        return set()
    j, bin_ = read_glb(r.stdout, Model(path))
    if j is None:
        return set()
    return {hashlib.sha256(d).hexdigest() for d in (picture_bytes(j, bin_, im) for im in j.get("images", [])) if d is not None}


def new_pictures(models, base=None):
    """(model, picture) for each picture that is not in the model's file at
    base's merge base, each picture once."""
    rev = git("merge-base", base, "HEAD").strip() if base else None
    seen, out = set(), []
    for m in models:
        old = pictures_at(rev, m.path) if rev else set()
        for p in m.pictures:
            if p.sha not in old and p.sha not in seen:
                seen.add(p.sha)
                out.append((m, p))
    return out


def own_note(m):
    n = sum(p.own for p in m.pictures)
    if not n:
        return ""
    them = "them" if n > 1 else "it"
    return "%d picture%s of the artist's own. A reviewer confirms the artist made %s or may share %s." % (n, "s" if n > 1 else "", them, them)


def summary(models, out, fresh=None, drawn=0):
    lines = ["## Model check", "",
             "| Model | Result | Triangles | Studio's advice | Pictures | Size in cells (across, deep, tall) | Notes |",
             "| --- | --- | --- | --- | --- | --- | --- |"]
    for m in models:
        size = "%.2f, %.2f, %.2f" % (m.size[0], m.size[2], m.size[1]) if m.size else ""
        pics = ", ".join(p.describe() + (" (own)" if p.own else "") for p in m.pictures)
        notes = " ".join(n[0].upper() + n[1:] for n in m.notes + [own_note(m)] if n)
        result = "fails: " + " ".join(m.problems) if m.problems else "passes"
        cells = (m.path, result, format(m.triangles, ",") if m.triangles else "", format(m.advice, ",") if m.advice else "",
                 pics, size, notes)
        lines.append("| `%s` | %s | %s | %s | %s | %s | %s |" % tuple(c.replace("|", "/") for c in cells))
    if not models:
        lines.append("| | No models changed | | | | | |")
    lines += ["", "Studio Mode suggests %s triangles for scenery and unit cards, %s for units, and pictures of at most %d on a side. "
              "A model fails only past %s triangles or with a picture over %d on a side."
              % (format(FEATURE_TRIANGLES, ","), format(UNIT_TRIANGLES, ","), TEXTURE_SIDE, format(GUARD_TRIANGLES, ","), GUARD_TEXTURE)]
    if fresh is not None:
        lines += ["", "### New and changed pictures", ""]
        if not fresh:
            lines.append("None.")
        for m, p in fresh:
            lines.append("- `%s`: %s, %s" % (m.path, p.describe(), "the artist's own" if p.own else "marked as made by the project's generators"))
        if drawn:
            lines += ["", "The texture sheet draws %s from the checked files alone." % ("them" if drawn == len(fresh) else "the first %d" % drawn)]
    with open(out, "a", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


SHEET_MOST, TILE = 120, 192


def sheet(fresh, out):
    """Draws each picture from its own bytes over a checkerboard, so
    see-through parts show, with its model, name and size under it.
    Returns how many it drew."""
    from PIL import Image, ImageDraw, ImageFont
    Image.MAX_IMAGE_PIXELS = GUARD_TEXTURE * GUARD_TEXTURE
    fresh = fresh[:SHEET_MOST]
    try:
        font = ImageFont.load_default(size=13)
    except TypeError:
        font = ImageFont.load_default()
    cols = min(6, len(fresh))
    rows = (len(fresh) + cols - 1) // cols
    gap, label = 16, 52
    img = Image.new("RGB", (gap + cols * (TILE + gap), gap + rows * (TILE + label + gap)), (36, 36, 40))
    d = ImageDraw.Draw(img)

    def fit(text):
        while len(text) > 1 and d.textlength(text, font=font) > TILE:
            text = text[:-2] + "~"
        return text

    for i, (m, p) in enumerate(fresh):
        x, y = gap + i % cols * (TILE + gap), gap + i // cols * (TILE + label + gap)
        for cy in range(0, TILE, 16):
            for cx in range(0, TILE, 16):
                d.rectangle([x + cx, y + cy, x + cx + 15, y + cy + 15], fill=(150, 150, 150) if (cx + cy) // 16 % 2 else (110, 110, 110))
        try:
            if p.format is None or max(p.width, p.height) > GUARD_TEXTURE:
                raise ValueError(p.name)
            pic = Image.open(io.BytesIO(p.data))
            pic.load()
            pic = pic.convert("RGBA")
            k = TILE / max(pic.width, pic.height)
            if k >= 2:
                pic = pic.resize((pic.width * int(k), pic.height * int(k)), Image.NEAREST)
            else:
                pic.thumbnail((TILE, TILE), Image.LANCZOS)
            img.paste(pic, (x + (TILE - pic.width) // 2, y + (TILE - pic.height) // 2), pic)
        except Exception:
            d.text((x + 8, y + TILE // 2 - 8), "cannot be drawn", fill=(255, 120, 120), font=font)
        d.text((x, y + TILE + 3), fit(os.path.basename(m.path)), fill=(235, 235, 235), font=font)
        d.text((x, y + TILE + 19), fit(p.name), fill=(200, 200, 200), font=font)
        d.text((x, y + TILE + 35), fit("%dx%d %s, %s" % (p.width, p.height, p.format or "?", "own" if p.own else "generated")),
               fill=(255, 210, 120) if p.own else (160, 200, 160), font=font)
    img.save(out, optimize=True)
    return len(fresh)


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--changed", metavar="BASE", help="check the files changed since BASE")
    ap.add_argument("--all", action="store_true", help="check every committed model")
    ap.add_argument("--sheet", metavar="FILE", help="draw the new and changed pictures into FILE, a PNG")
    ap.add_argument("paths", nargs="*")
    a = ap.parse_args(argv)
    paths = changed(a.changed) if a.changed else everything() if a.all else [rel(p) for p in a.paths]
    models = [m for m in (check(p) for p in sorted(set(paths))) if m is not None]
    bad = 0
    for m in models:
        for p in m.problems:
            print("FAIL %s: %s" % (m.path, p))
        for n in m.notes + [own_note(m)]:
            if n:
                print("note %s: %s" % (m.path, n))
        bad += bool(m.problems)
    print("MODEL_CHECK %d files, %d failed" % (len(models), bad))
    fresh = new_pictures(models, a.changed) if a.changed or a.sheet else None
    drawn = 0
    if a.sheet and fresh:
        try:
            drawn = sheet(fresh, a.sheet)
            print("SHEET %d of %d new or changed pictures in %s" % (drawn, len(fresh), a.sheet))
        except ImportError:
            print("note: the texture sheet needs Pillow (python3 -m pip install pillow)")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        summary(models, os.environ["GITHUB_STEP_SUMMARY"], fresh, drawn)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
