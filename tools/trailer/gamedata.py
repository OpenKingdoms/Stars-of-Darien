"""The game files the trailer's sound needs, read from the player's own
Total Annihilation: Kingdoms: unit FBIs and COB scripts, the sound classes,
the ambient emitters and the WAVs.

The archives are unpacked once into a cache outside the repository
(D:/OKBuild/trailer-cache by default). Nothing read here is ever committed.
Archives stack as the engine mounts them: sorted by name, a later one wins.
"""
import os
import re
import struct
import subprocess
import sys

GAME_DIR = os.environ.get("OK_GAME_DIR", "C:/GOG Games/Total Annihilation Kingdoms")
CACHE = os.environ.get("OKU_TRAILER_CACHE", "D:/OKBuild/trailer-cache")
EXTRACTOR = os.environ.get("OK_HPI_EXTRACT", "C:/Projects/OpenKingdoms/scripts/hpi_extract.py")

# Map, terrain and mission archives carry no units, scripts or sounds.
SKIP = {"maps", "terrain", "missions", "ipmissions", "sections", "ipsections", "boneyards", "boneyards2", "meta"}

OP_SLEEP = 0x10013000
OP_PUSH_CONSTANT = 0x10021001
OP_WAIT_FOR_TURN = 0x10011000
OP_WAIT_FOR_MOVE = 0x10012000
OP_PLAY_SOUND = 0x10072000


class GameFiles:
    def __init__(self, game_dir=GAME_DIR, cache=CACHE):
        self.root = os.path.join(cache, "hpi")
        archives = sorted((f for f in os.listdir(game_dir) if f.lower().endswith(".hpi")), key=str.lower)
        self.index = {}
        for name in archives:
            stem = name[:-4]
            if stem.lower() in SKIP:
                continue
            out = os.path.join(self.root, stem)
            if not os.path.isdir(out):
                subprocess.run([sys.executable, EXTRACTOR, os.path.join(game_dir, name), "-o", out],
                               check=True, stdout=subprocess.DEVNULL)
            for d, _, files in os.walk(out):
                for f in files:
                    full = os.path.join(d, f)
                    rel = os.path.relpath(full, out).replace("\\", "/").lower()
                    self.index[rel] = full
        self._tdf = {}
        self._cob = {}

    def find(self, rel):
        return self.index.get(rel.replace("\\", "/").lower())

    def under(self, folder, ext):
        folder = folder.lower().rstrip("/") + "/"
        return sorted(k for k in self.index if k.startswith(folder) and k.endswith(ext))

    def text(self, rel):
        p = self.find(rel)
        if not p:
            return None
        with open(p, "rb") as f:
            return f.read().decode("latin-1")

    def tdf(self, rel):
        if rel not in self._tdf:
            t = self.text(rel)
            self._tdf[rel] = parse_tdf(t) if t is not None else None
        return self._tdf[rel]

    def wav(self, name):
        n = name.strip().lower()
        if not n.endswith(".wav"):
            n += ".wav"
        return self.find("sounds/" + n)

    def cob_sounds(self, unit):
        unit = unit.lower()
        if unit not in self._cob:
            p = self.find("scripts/%s.cob" % unit)
            self._cob[unit] = cob_sounds(open(p, "rb").read()) if p else {}
        return self._cob[unit]


def parse_tdf(text):
    """TDF sections as nested dicts, keys lower case. Each section keeps its
    entries in order under the key '__order__' as (key, value) pairs."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    pos = 0
    root = {"__order__": []}
    stack = [root]
    pending = None
    token = re.compile(r"\s*(\[[^\]]*\]|\{|\}|[^;{}\[\]]+;?)", re.S)
    while pos < len(text):
        m = token.match(text, pos)
        if not m:
            break
        pos = m.end()
        t = m.group(1).strip()
        if not t:
            continue
        if t.startswith("["):
            pending = t[1:-1].strip()
        elif t == "{":
            sec = {"__order__": []}
            name = (pending or "").lower()
            stack[-1][name] = sec
            stack[-1]["__order__"].append((name, sec))
            stack.append(sec)
            pending = None
        elif t == "}":
            if len(stack) > 1:
                stack.pop()
        elif "=" in t:
            k, v = t.rstrip(";").split("=", 1)
            k, v = k.strip().lower(), v.strip()
            stack[-1][k] = v
            stack[-1]["__order__"].append((k, v))
    return root


def cob_sounds(data):
    """Each script function's sound plays, as (sound, category, ms from the
    function's start). The time adds up sleeps and guesses a turn or move."""
    h = struct.unpack_from("<13I", data, 0)
    _, ns, _, clen, _, _, off_offsets, off_names, _, off_code, off_snd, _, nsnd = h
    code = struct.unpack_from("<%dI" % clen, data, off_code)
    offs = struct.unpack_from("<%dI" % ns, data, off_offsets)
    nptr = struct.unpack_from("<%dI" % ns, data, off_names)
    names = [data[p:data.index(b"\0", p)].decode("latin-1") for p in nptr]
    sptr = struct.unpack_from("<%dI" % nsnd, data, off_snd) if 0 < nsnd <= 512 else ()
    snames = [data[p:data.index(b"\0", p)].decode("latin-1") for p in sptr]
    order = sorted(range(ns), key=lambda i: offs[i])
    out = {}
    for k, i in enumerate(order):
        a = offs[i]
        b = offs[order[k + 1]] if k + 1 < len(order) else clen
        ms = 0
        for pc in range(a, min(b, clen) - 1):
            w = code[pc]
            pushed = code[pc - 1] if pc >= 2 and code[pc - 2] == OP_PUSH_CONSTANT else None
            if w == OP_SLEEP and pushed is not None and pushed < 60000:
                ms += pushed
            elif w in (OP_WAIT_FOR_TURN, OP_WAIT_FOR_MOVE):
                ms += 120
            elif w == OP_PLAY_SOUND and code[pc + 1] < len(snames):
                cat = (pushed if pushed is not None else 4) & 7
                out.setdefault(names[i].lower(), []).append((snames[code[pc + 1]], cat, ms))
    return out


class Units:
    """Unit facts by internal name: its FBI, sound class and weapons."""

    def __init__(self, files):
        self.files = files
        self.fbi = {}
        self.classes = {}
        for rel in files.under("gamedata/soundclasses", ".tdf"):
            t = files.tdf(rel)
            for name, sec in (t or {}).get("__order__", []):
                if isinstance(sec, dict):
                    self.classes.setdefault(name, sec)

    def info(self, unit):
        u = unit.lower()
        if u not in self.fbi:
            t = self.files.tdf("units/%s.fbi" % u)
            self.fbi[u] = t
        return self.fbi[u]

    def field(self, unit, key, default=""):
        t = self.info(unit)
        if not t:
            return default
        return t.get("unitinfo", {}).get(key, default)

    def weapons(self, unit):
        t = self.info(unit)
        if not t:
            return []
        return [sec for name, sec in t["__order__"] if name.startswith("weapon") and isinstance(sec, dict)]

    def bodytype(self, unit):
        return self.field(unit, "bodytype", "default").lower() if unit and unit != "-" else None

    def sound_class(self, unit):
        return self.classes.get(self.field(unit, "soundcategory", "").lower())


def pick(section, rng):
    """A wav from a sound class section: weighted when the class says so."""
    items = [(k, v) for k, v in section.get("__order__", []) if not isinstance(v, dict)]
    if not items:
        return None
    if all(k.startswith("sound") and k[5:].isdigit() for k, _ in items):
        return items[int(rng() * len(items)) % len(items)][1]
    weights = []
    for k, v in items:
        try:
            weights.append(max(0.0, float(v)))
        except ValueError:
            weights.append(1.0)
    total = sum(weights) or 1.0
    r = rng() * total
    for (k, _), w in zip(items, weights):
        r -= w
        if r <= 0:
            return k
    return items[-1][0]
