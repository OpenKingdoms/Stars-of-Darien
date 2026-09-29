"""Trial builds, run inside Blender: builds models from the table (or with
overrides merged into one entry) into work/try instead of the real output.

    blender -b --factory-startup --python variants.py -- Name [Name ...]
    blender -b --factory-startup --python variants.py -- Name tag '{"key": value}'
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build  # noqa: E402
import models  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
cat = {r["name"]: r for r in json.load(open(build.CATALOG))}
out = os.path.join(build.kit.OUT, "work", "try")
if len(argv) == 3 and argv[2].startswith("{"):
    name, tag, over = argv[0], argv[1], json.loads(argv[2])
    models.MODELS[name] = dict(models.MODELS[name], **over)
    print("VARIANT", name, tag, build.build(name, cat[name], out, "_" + tag))
else:
    for name in argv:
        print("VARIANT", name, build.build(name, cat[name], out))
