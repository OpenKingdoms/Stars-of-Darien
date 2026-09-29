"""Triangles and size (cells, x wide, y deep, z tall) of built mounds: python glbsize.py [dir]"""
import glob
import json
import os
import struct
import sys

d0 = sys.argv[1] if len(sys.argv) > 1 else "D:/OKReplace/hand/zhon_structures/models"
for f in sorted(glob.glob(os.path.join(d0, "ZonTmound0*.glb"))):
    d = open(f, "rb").read()
    n = struct.unpack("<I", d[12:16])[0]
    j = json.loads(d[20:20 + n])
    tris, lo, hi = 0, [9e9] * 3, [-9e9] * 3
    for m in j["meshes"]:
        for p in m["primitives"]:
            a = j["accessors"][p["attributes"]["POSITION"]]
            lo = [min(u, v) for u, v in zip(lo, a["min"])]
            hi = [max(u, v) for u, v in zip(hi, a["max"])]
            tris += j["accessors"][p["indices"]]["count"] // 3
    # glTF is y up, -z toward the classic camera
    print(os.path.basename(f)[:-4], "tris", tris, "wide %.2f deep %.2f tall %.2f" % (hi[0] - lo[0], hi[2] - lo[2], hi[1] - lo[1]))
