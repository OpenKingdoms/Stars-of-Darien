"""Tests for check-models.py on small models made here.

    python3 scripts/test_check_models.py
"""
import importlib.util
import json
import os
import struct
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("check_models", os.path.join(HERE, "check-models.py"))
cm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cm)

FEATURE = cm.FEATURES + "TestThing.glb"


def png_header(w, h):
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", w, h) + b"\x08\x06\x00\x00\x00"


def glb(triangles=12, lo=(-0.5, 0.0, -0.5), hi=(0.5, 1.0, 0.5), picture=None, extensions=(), material=None, node=None):
    """A model whose box runs from lo to hi, with the given triangle count."""
    j = {"asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": [0]}],
         "nodes": [dict(node or {}, mesh=0)],
         "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "material": 0}]}],
         "accessors": [{"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": list(lo), "max": list(hi)},
                       {"bufferView": 0, "componentType": 5125, "count": 3 * triangles, "type": "SCALAR"}],
         "materials": [material or {"name": "stone"}],
         "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": 36}],
         "buffers": [{"byteLength": 128}]}
    bin_ = bytearray(128)
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

    def test_triangle_budgets(self):
        self.assertEqual(problems(glb(triangles=3000)).problems, [])
        self.assertTrue(any("triangles" in p for p in problems(glb(triangles=3001)).problems))
        unit = cm.UNITS + "TESTUNIT.glb"
        self.assertEqual(problems(glb(triangles=6000), unit).problems, [])
        self.assertTrue(any("triangles" in p for p in problems(glb(triangles=6001), unit).problems))

    def test_pictures(self):
        self.assertEqual(problems(glb(picture=png_header(1024, 512))).problems, [])
        self.assertEqual(problems(glb(picture=png_header(1024, 512))).pictures, 1)
        self.assertTrue(any("1024" in p for p in problems(glb(picture=png_header(2048, 2048))).problems))
        self.assertTrue(any("PNG or JPEG" in p for p in problems(glb(picture=b"RIFF0000WEBPVP8 ")).problems))

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

    def test_game_files_and_other_formats(self):
        self.assertTrue(cm.check("maps/Darien.tnt").problems)
        self.assertTrue(cm.check(cm.FEATURES + "Thing.fbx").problems)


if __name__ == "__main__":
    unittest.main()
