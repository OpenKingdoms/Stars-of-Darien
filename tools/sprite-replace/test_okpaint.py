"""The export check (okpaint.check) on small glbs made here, and on every
model that ships in unity/Assets/Overrides/Features. System Python:

    python -m unittest tools/sprite-replace/test_okpaint.py
"""
import json
import os
import struct
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import okpaint  # noqa: E402

FEATURES = os.path.join(os.path.dirname(os.path.dirname(HERE)), "unity", "Assets", "Overrides", "Features")
PAINT = {"kind": "feature", "name": "AraTree01", "world": "aramon", "gain": 1.3, "bleed": True,
         "alpha": "opaque", "size": [45, 115]}


def glb(materials, images=0, node_extras=None):
    j = {"asset": {"version": "2.0"}, "materials": materials,
         "nodes": [{"name": "model", "extras": node_extras or {}}]}
    if images:
        j["images"] = [{"name": "img%d" % i, "mimeType": "image/png", "bufferView": 0} for i in range(images)]
        j["textures"] = [{"source": i} for i in range(images)]
    data = json.dumps(j).encode()
    data += b" " * (-len(data) % 4)
    f = tempfile.NamedTemporaryFile(suffix=".glb", delete=False)
    f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(data)) + struct.pack("<II", len(data), 0x4E4F534A) + data)
    f.close()
    return f.name


def textured(k, extras=None):
    m = {"name": "m%d" % k, "pbrMetallicRoughness": {"baseColorTexture": {"index": k}}}
    if extras:
        m["extras"] = extras
    return m


class CheckTests(unittest.TestCase):
    def problems(self, *a, **kw):
        path = glb(*a, **kw)
        try:
            return okpaint.check(path)
        finally:
            os.remove(path)

    def test_a_painted_material_without_a_picture_ships(self):
        self.assertEqual([], self.problems([{"name": "walls", "extras": {"okPaint": PAINT}}]))

    def test_a_painted_material_still_holding_its_picture_fails(self):
        p = self.problems([textured(0, {"okPaint": PAINT})], images=1)
        self.assertTrue(any("still holds a picture" in m for m in p), p)

    def test_a_generated_texture_ships(self):
        self.assertEqual([], self.problems([textured(0, {"okGenerated": True})], images=1))

    def test_a_picture_nobody_vouches_for_fails(self):
        p = self.problems([textured(0, {"okGenerated": True}), textured(1)], images=2)
        self.assertEqual(1, len(p), p)
        self.assertIn("img1", p[0])

    def test_a_model_stamped_as_the_players_fails(self):
        p = self.problems([], node_extras={"okFromPlayersFiles": True})
        self.assertTrue(any("okFromPlayersFiles" in m for m in p), p)

    def test_a_cover_mask_and_a_delit_recipe_ship(self):
        mask = dict(PAINT, alpha="mask", mask={"cover": "others", "hotspot": [21, 112]})
        delit = dict(PAINT, delit={"hotspot": [21, 112], "light": [-0.56, -0.1, 0.82], "ambient": 0.33,
                                   "direct": 0.64, "stones": [{"grey": [0.1, 0.1, 0.09], "dark": 0.15}]})
        self.assertEqual([], self.problems([{"name": "skirt", "extras": {"okPaint": mask}},
                                            {"name": "stone", "extras": {"okPaint": delit}}]))

    def test_a_mask_needs_alpha_mask_and_a_known_cover(self):
        p = self.problems([{"name": "skirt", "extras": {"okPaint": dict(PAINT, mask={"cover": "others", "hotspot": [1, 2]})}},
                           {"name": "rim", "extras": {"okPaint": dict(PAINT, alpha="mask", mask={"cover": "runs", "rows": [1]})}}])
        self.assertTrue(any("without alpha mask" in m for m in p), p)
        self.assertTrue(any("not a cover" in m for m in p), p)

    def test_texels_in_a_recipe_fail(self):
        rows = dict(PAINT, alpha="mask", mask={"cover": "others", "hotspot": list(range(300))})
        odd = dict(PAINT, pixels="iVBORw0KGgo" * 20)
        p = self.problems([{"name": "a", "extras": {"okPaint": rows}}, {"name": "b", "extras": {"okPaint": odd}}])
        self.assertTrue(any("numbers" in m for m in p), p)
        self.assertTrue(any("keys pixels" in m for m in p), p)

    @unittest.skipUnless(os.path.isdir(FEATURES), "no Overrides/Features beside the tools")
    def test_no_shipped_model_holds_the_originals_art(self):
        bad = []
        for f in sorted(os.listdir(FEATURES)):
            if f.lower().endswith(".glb"):
                bad += okpaint.check(os.path.join(FEATURES, f))
        self.assertEqual([], bad)


if __name__ == "__main__":
    unittest.main()
