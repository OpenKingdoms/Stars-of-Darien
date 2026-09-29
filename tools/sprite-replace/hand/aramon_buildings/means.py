"""Mean drawn colour of sprite and classic render, per name.

    python means.py name ...
"""
import sys

import look

for n in sys.argv[1:]:
    ms, mr = look.means(n)
    print("%-12s sprite %3d %3d %3d  model %3d %3d %3d" % ((n,) + tuple(ms) + tuple(mr)))
