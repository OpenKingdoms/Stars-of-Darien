"""Tests for check-models.py on small models made here.

    python3 scripts/test_check_models.py
"""
import importlib.util
import json
import os
import struct
import tempfile
import unittest
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("check_models", os.path.join(HERE, "check-models.py"))
cm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cm)

FEATURE = cm.FEATURES + "TestThing.glb"


def png_header(w, h):
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", w, h) + b"\x08\x06\x00\x00\x00"


def png(w, h, rgba=(200, 80, 40, 255)):
    """A whole PNG of one colour."""
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    rows = b"".join(b"\x00" + bytes(rgba) * w for _ in range(h))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def glb(triangles=12, lo=(-0.5, 0.0, -0.5), hi=(0.5, 1.0, 0.5), picture=None, extensions=(), material=None, node=None):
    """A model whose box runs from lo to hi, with the given triangle count."""
    j = {"asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": [0]}],
         "nodes": [dict(node or {}, mesh=0)],
         "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "material": 0}]}],
         "accessors": [{"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": list(lo), "max": list(hi)},
                       {"bufferView": 0, "componentType": 5125, "count": 3 * triangles, "type": "SCALAR"}],
         "materials": [material or {"name": "stone"}],
         "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": 36}],
         "buffers": [{"byteLength": max(128, 36 + len(picture or b""))}]}
    bin_ = bytearray(max(128, 36 + len(picture or b"")))
    if picture:
        j["bufferViews"].append({"buffer": 0, "byteOffset": 36, "byteLength": len(picture)})
        j["images"] = [{"name": "paint", "bufferView": 1, "mimeType": "image/png"}]
        j["textures"] = [{"source": 0}]
        j["materials"][0].setdefault("pbrMetallicRoughness", {})["baseColorTexture"] = {"index": 0}
        bin_[36:36 + len(picture)] = picture
    if extensions:
        j["extensionsUsed"] = j["extensionsRequired"] = list(extensions)
    js = json.dumps(j).encode()
    js += b" " * (-len(js) % 4)
    body = struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(bin_), 0x004E4942) + bytes(bin_)
    return b"glTF" + struct.pack("<II", 2, 12 + len(body)) + body


def problems(data, path=FEATURE):
    m = cm.Model(path)
    cm.check_glb(path, data, m)
    return m


class CheckModelsTests(unittest.TestCase):
    def test_a_plain_model_passes(self):
        m = problems(glb())
        self.assertEqual(m.problems, [])
        self.assertEqual(m.triangles, 12)

    def test_not_a_glb(self):
        self.assertIn("glTF binary", problems(b"solid cube\n").problems[0])

    def test_compression_fails(self):
        m = problems(glb(extensions=["KHR_draco_mesh_compression"]))
        self.assertTrue(any("Compression" in p for p in m.problems))

    def test_triangles_over_the_advice_are_a_note_and_fail_only_past_the_guard(self):
        m = problems(glb(triangles=3000))
        self.assertEqual((m.problems, m.notes, m.advice), ([], [], 3000))
        m = problems(glb(triangles=3001))
        self.assertEqual(m.problems, [])
        self.assertTrue(any("3,001 triangles" in n and "3,000" in n for n in m.notes), m.notes)
        unit = cm.UNITS + "TESTUNIT.glb"
        self.assertEqual(problems(glb(triangles=6000), unit).notes, [])
        self.assertTrue(any("6,000" in n for n in problems(glb(triangles=6001), unit).notes))
        self.assertEqual(problems(glb(triangles=100000)).problems, [])
        self.assertTrue(any("100,001 triangles" in p for p in problems(glb(triangles=100001)).problems))

    def test_pictures_over_the_advice_are_a_note_and_fail_only_past_the_guard(self):
        m = problems(glb(picture=png_header(1024, 512)))
        self.assertEqual((m.problems, m.notes), ([], []))
        self.assertEqual([(p.name, p.width, p.height, p.format) for p in m.pictures], [("paint", 1024, 512, "PNG")])
        m = problems(glb(picture=png_header(4096, 2048)))
        self.assertEqual(m.problems, [])
        self.assertTrue(any("4096 by 2048" in n for n in m.notes), m.notes)
        self.assertTrue(any("4097" in p for p in problems(glb(picture=png_header(4097, 16))).problems))
        self.assertTrue(any("PNG or JPEG" in p for p in problems(glb(picture=b"RIFF0000WEBPVP8 ")).problems))

    def test_an_artists_own_picture_passes_and_is_named_for_the_reviewer(self):
        m = problems(glb(picture=png(4, 4)))
        self.assertEqual(m.problems, [])
        self.assertTrue(m.pictures[0].own)
        self.assertIn("1 picture of the artist's own", cm.own_note(m))
        generated = problems(glb(picture=png(4, 4), material={"name": "bark", "extras": {"okGenerated": True}}))
        self.assertEqual((generated.problems, generated.pictures[0].own, cm.own_note(generated)), ([], False, ""))

    def test_a_carved_model_holds_no_picture_of_its_own(self):
        m = problems(glb(picture=png(4, 4), node={"extras": {"okCarved": "batch.py"}}))
        self.assertTrue(any("carved" in p for p in m.problems), m.problems)

    def test_the_summary_gives_the_advice_and_the_new_pictures(self):
        m = problems(glb(triangles=4000, picture=png(4, 4)))
        out = os.path.join(tempfile.mkdtemp(), "summary.md")
        cm.summary([m], out, cm.new_pictures([m]), 1)
        text = open(out, encoding="utf-8").read()
        self.assertIn("| 4,000 | 3,000 | paint 4x4 PNG (own) |", text)
        self.assertIn("A reviewer confirms the artist made it or may share it.", text)
        self.assertIn("### New and changed pictures", text)
        self.assertIn("paint 4x4 PNG, the artist's own", text)

    def test_new_pictures_leaves_out_the_ones_already_there(self):
        a, b = problems(glb(picture=png(4, 4))), problems(glb(picture=png(4, 4)), cm.FEATURES + "Other.glb")
        self.assertEqual(len(cm.new_pictures([a, b])), 1, "a picture shared by two models is drawn once")
        committed = next(m for m in (cm.check(p) for p in cm.everything() if p.endswith(".glb")) if m and m.pictures)
        self.assertEqual(cm.new_pictures([committed], "HEAD"), [], committed.path)

    @unittest.skipUnless(importlib.util.find_spec("PIL"), "the texture sheet needs Pillow")
    def test_the_sheet_draws_each_picture_from_the_file_alone(self):
        from PIL import Image
        good = problems(glb(picture=png(8, 4)))
        broken = problems(glb(picture=png_header(16, 16)), cm.FEATURES + "Broken.glb")
        out = os.path.join(tempfile.mkdtemp(), "sheet.png")
        self.assertEqual(cm.sheet(cm.new_pictures([good, broken]), out), 2)
        with Image.open(out) as img:
            self.assertEqual(img.size, (16 + 2 * (cm.TILE + 16), 16 + cm.TILE + 52 + 16))
            # the 8 by 4 picture drawn 24 times over in the middle of the first tile
            self.assertEqual(img.getpixel((16 + cm.TILE // 2, 16 + cm.TILE // 2))[:3], (200, 80, 40))

    def test_ground_and_foundation(self):
        self.assertTrue(any("floats" in p for p in problems(glb(lo=(-0.5, 0.3, -0.5), hi=(0.5, 1.3, 0.5))).problems))
        self.assertEqual(problems(glb(lo=(-0.5, -0.3, -0.5), hi=(0.5, 1.0, 0.5))).problems, [])
        self.assertTrue(any("into the ground" in p for p in problems(glb(lo=(-0.5, -0.8, -0.5), hi=(0.5, 1.0, 0.5))).problems))

    def test_node_transforms_count(self):
        m = problems(glb(node={"translation": [0, 0.5, 0]}))
        self.assertTrue(any("floats" in p for p in m.problems))
        m = problems(glb(node={"scale": [200, 200, 200]}))
        self.assertTrue(any("centimetres" in p for p in m.problems))

    def test_folder(self):
        self.assertTrue(any("folder the game reads" in p for p in problems(glb(), "unity/Assets/Models/Thing.glb").problems))
        self.assertTrue(any("folder the game reads" in p for p in problems(glb(), cm.FEATURES + "trees/Thing.glb").problems))

    def test_paint_recipes_and_review_copies(self):
        painted = {"name": "sprite", "extras": {"okPaint": {"kind": "feature", "name": "AraTree01", "pixels": [1] * 300}}}
        self.assertTrue(any("okPaint" in p for p in problems(glb(material=painted)).problems))
        review = glb(node={"extras": {"okFromPlayersFiles": True}})
        self.assertTrue(any("okFromPlayersFiles" in p for p in problems(review).problems))
        review = glb(picture=png(4, 4), node={"extras": {"okFromPlayersFiles": True}})
        self.assertTrue(any("okFromPlayersFiles" in p for p in problems(review).problems))

    def test_game_files_and_other_formats(self):
        self.assertTrue(cm.check("maps/Darien.tnt").problems)
        self.assertTrue(cm.check(cm.FEATURES + "Thing.fbx").problems)


if __name__ == "__main__":
    unittest.main()
