#!/bin/sh
# Builds the named models, remakes their compares and prints their numbers.
#   sh run.sh VerBuild01b VerHut04 ...
HERE=$(cd "$(dirname "$0")" && pwd)
LOG=/d/OKReplace/hand/veruna_buildings/work/run.log
/d/Blender/blender-5.2.2-windows-x64/blender.exe -b --factory-startup --python "$HERE/build.py" -- "$@" > "$LOG" 2>&1
grep -E "VB_OK|VB_FAIL|VB_DONE|Error|error:|Traceback|line [0-9]+" "$LOG" | grep -v "^Read\|Fra:" | head -40
python "$HERE/compare.py" "$@" > /dev/null
python "$HERE/stats.py" "$@"
python "$HERE/peek.py" "$@"
