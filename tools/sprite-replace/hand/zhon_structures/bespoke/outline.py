"""Splits each mound sprite's top outline into the crown under it and the finger
peaks standing out of it, with the system Python: python outline.py [w]"""
import json
import os
import sys

S = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sprites.json")))


def body_cols(s, frac=0.55):
    up = s["top"]
    top = max(u for u in up if u is not None)
    ok = [u is not None and u > frac * top for u in up]
    return ok.index(True), len(ok) - 1 - ok[::-1].index(True)


def opened(s, w=4):
    """The outline with peaks narrower than 2w+1 columns shaved off (an opening
    over the body's own columns), so what is left is the crown's own skyline."""
    c0, c1 = body_cols(s)
    up = s["top"]
    ero = {c: min(up[j] for j in range(max(c0, c - w), min(c1, c + w) + 1)) for c in range(c0, c1 + 1)}
    return c0, c1, {c: max(ero[j] for j in range(max(c0, c - w), min(c1, c + w) + 1)) for c in range(c0, c1 + 1)}


def peaks(s, w=4, least=3):
    c0, c1, op = opened(s, w)
    up = s["top"]
    out, run = [], []
    for c in range(c0, c1 + 2):
        if c <= c1 and up[c] - op[c] >= least:
            run.append(c)
        elif run:
            best = max(run, key=lambda j: up[j])
            out.append((best, s["hy"] - up[best], run[0], run[-1]))
            run = []
    return out


if __name__ == "__main__":
    w = int(sys.argv[1]) if len(sys.argv) > 1 else 4
    for n, s in S.items():
        c0, c1, op = opened(s, w)
        print(n, "body cols", c0, c1)
        print("   crown rows", [s["hy"] - op[c] for c in range(c0, c1 + 1)])
        print("   peaks (col, row, from, to)", peaks(s, w))
