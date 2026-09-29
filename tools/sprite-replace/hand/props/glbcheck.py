"""Lists what a props .glb carries, system Python:

    python glbcheck.py [file.glb ...]

For each file: triangles, vertex attributes, images, and each material's
extras. With no files, checks every model in models/ and fails if one
still holds an image.
"""
import glob
import json
import os
import struct
import sys

OUT = r"D:\OKReplace\hand\props"


def read(path):
    b = open(path, "rb").read()
    n = struct.unpack("<I", b[12:16])[0]
    return json.loads(b[20:20 + n])


def tris(js):
    t = 0
    for m in js["meshes"]:
        for p in m["primitives"]:
            t += js["accessors"][p["indices"]]["count"] // 3
    return t


def main():
    files = sys.argv[1:] or sorted(glob.glob(os.path.join(OUT, "models", "*.glb")))
    bad = []
    for f in files:
        js = read(f)
        attrs = sorted({a for m in js["meshes"] for p in m["primitives"] for a in p["attributes"]})
        imgs = len(js.get("images") or [])
        mats = {m["name"]: m.get("extras", {}) for m in js.get("materials", [])}
        paint = {k: v["okPaint"] for k, v in mats.items() if "okPaint" in v}
        noshadow = [k for k, v in mats.items() if v.get("castShadows") is False]
        node = js["nodes"][0].get("extras", {})
        print(os.path.basename(f), "tris", tris(js), "images", imgs, "attrs", ",".join(attrs))
        if paint:
            print("   okPaint", json.dumps(paint))
        if noshadow:
            print("   castShadows false", ",".join(noshadow))
        print("   node extras", json.dumps(node))
        if imgs:
            bad.append(f)
    if bad and not sys.argv[1:]:
        print("IMAGES LEFT IN", bad)
        sys.exit(1)


if __name__ == "__main__":
    main()
