#!/usr/bin/env python3
"""Puts carved models where the game ships them, one <feature>.glb per
feature in unity/Assets/Overrides/Features, and lodestone cards as
<OBJECT>.glb with <OBJECT>.json in unity/Assets/Overrides/Units.

    python tools/sprite-replace/install.py [--tool batch.py] <models dir> ...
    python tools/sprite-replace/install.py --cards <models dir>

Every model must pass okpaint.check: geometry and recipes only. Each
feature model is stamped okCarved (with the tool that made it) in its
root node's extras, so a later install can replace it. A model already in
Features without that stamp is hand-built and wins: it is left alone. A
card's JSON takes replacesPiece and replacesTexture from its glb.
"""
import argparse
import json
import os
import shutil
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import okpaint  # noqa: E402

OVERRIDES = os.path.join(os.path.dirname(os.path.dirname(HERE)), "unity", "Assets", "Overrides")
CARVED = "okCarved"


def read_glb(path):
    data = open(path, "rb").read()
    n = struct.unpack_from("<I", data, 12)[0]
    return json.loads(data[20:20 + n]), data[20 + n:]


def write_glb(path, j, rest):
    body = json.dumps(j, separators=(",", ":")).encode()
    body += b" " * (-len(body) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(body) + len(rest)))
        f.write(struct.pack("<II", len(body), 0x4E4F534A))
        f.write(body)
        f.write(rest)


def carved(path):
    j, _ = read_glb(path)
    return any(CARVED in n.get("extras", {}) for n in j.get("nodes", []))


def install_features(dirs, tool, dest):
    added = replaced = kept = 0
    for d in dirs:
        for f in sorted(os.listdir(d)):
            if not f.lower().endswith(".glb"):
                continue
            src = os.path.join(d, f)
            problems = okpaint.check(src)
            if problems:
                raise SystemExit("; ".join(problems))
            out = os.path.join(dest, f)
            same = [g for g in os.listdir(dest) if g.lower() == f.lower()]
            if same:
                out = os.path.join(dest, same[0])
                if not carved(out):
                    kept += 1
                    continue
                replaced += 1
            else:
                added += 1
            j, rest = read_glb(src)
            roots = j.get("scenes", [{}])[j.get("scene", 0)].get("nodes", [0])
            for i in roots:
                j["nodes"][i].setdefault("extras", {})[CARVED] = tool
            write_glb(out, j, rest)
    print("INSTALL features: %d added, %d replaced, %d hand-built kept" % (added, replaced, kept))


def install_cards(dirs, dest):
    n = 0
    for d in dirs:
        for f in sorted(os.listdir(d)):
            if not f.lower().endswith(".glb"):
                continue
            src = os.path.join(d, f)
            problems = okpaint.check(src)
            if problems:
                raise SystemExit("; ".join(problems))
            j, _ = read_glb(src)
            extras = {}
            for node in j.get("nodes", []):
                extras.update({k: v for k, v in node.get("extras", {}).items() if k in ("replacesPiece", "replacesTexture")})
            if "replacesPiece" not in extras:
                raise SystemExit("%s names no piece it replaces" % f)
            name = os.path.splitext(f)[0].upper()
            shutil.copyfile(src, os.path.join(dest, name + ".glb"))
            with open(os.path.join(dest, name + ".json"), "w", newline="\n") as out:
                json.dump({"replacesPiece": extras["replacesPiece"], "replacesTexture": extras.get("replacesTexture", "")}, out, indent=2)
                out.write("\n")
            n += 1
    print("INSTALL cards: %d" % n)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("dirs", nargs="+")
    ap.add_argument("--tool", default="batch.py")
    ap.add_argument("--cards", action="store_true")
    ap.add_argument("--overrides", default=OVERRIDES)
    a = ap.parse_args()
    if a.cards:
        install_cards(a.dirs, os.path.join(a.overrides, "Units"))
    else:
        install_features(a.dirs, a.tool, os.path.join(a.overrides, "Features"))


if __name__ == "__main__":
    main()
