"""Bounds of built models from their GLB accessors (Blender exports Y up),
run with the system Python: python glbsize.py Name [Name ...]"""
import json
import struct
import sys

for n in sys.argv[1:]:
    p = n if n.endswith(".glb") else "D:/OKReplace/hand/trees_plants/models/%s.glb" % n
    b = open(p, "rb").read()
    ln = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + ln])
    lo, hi = [1e9] * 3, [-1e9] * 3
    for m in j["meshes"]:
        for pr in m["primitives"]:
            a = j["accessors"][pr["attributes"]["POSITION"]]
            lo = [min(x, y) for x, y in zip(lo, a["min"])]
            hi = [max(x, y) for x, y in zip(hi, a["max"])]
    # glTF: x right, y up, z toward -Y of Blender... report width x, depth, height
    print("%-16s height %.2f  x %.2f..%.2f  depth %.2f..%.2f" % (n.split("/")[-1], hi[1], lo[0], hi[0], lo[2], hi[2]))
