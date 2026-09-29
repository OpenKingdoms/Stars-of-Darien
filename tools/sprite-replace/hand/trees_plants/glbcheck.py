"""What a built model's GLB carries, run with the system Python.

    python glbcheck.py Name [Name ...]

Prints triangles, whether the primitives carry vertex colours (COLOR_0) and
UVs, and each material's alpha mode, double-sidedness, texture and wrap,
so a model that would render wrong in the engine shows up here.
"""
import json
import struct
import sys

OUT = "D:/OKReplace/hand/trees_plants/models"


def read(name):
    d = open("%s/%s.glb" % (OUT, name), "rb").read()
    n = struct.unpack("<I", d[12:16])[0]
    return json.loads(d[20:20 + n])


def check(name):
    j = read(name)
    acc = j["accessors"]
    tris = 0
    prims = []
    for m in j["meshes"]:
        for p in m["primitives"]:
            tris += acc[p["indices"]]["count"] // 3
            a = p["attributes"]
            prims.append("%s%s%s" % (j["materials"][p["material"]]["name"] if "material" in p else "-",
                                     "+col" if "COLOR_0" in a else "", "+uv" if "TEXCOORD_0" in a else ""))
    mats = []
    for m in j.get("materials", []):
        pbr = m.get("pbrMetallicRoughness", {})
        tex = pbr.get("baseColorTexture")
        wrap = ""
        if tex is not None:
            t = j["textures"][tex["index"]]
            smp = j["samplers"][t["sampler"]] if "sampler" in t else {}
            wrap = "wrap=%s" % smp.get("wrapS", 10497)
        mats.append("%s[%s%s%s %s]" % (m["name"], m.get("alphaMode", "OPAQUE"), " 2side" if m.get("doubleSided") else "",
                                        " tex" if tex is not None else "", wrap))
    extras = j["nodes"][0].get("extras", {})
    print(name, "tris", tris, "|", " ".join(prims), "|", " ".join(mats), "|", extras)


if __name__ == "__main__":
    for n in sys.argv[1:]:
        check(n)
