"""Mixes a shot's sound log (shots/<name>.sounds.tsv, written by SoundTap.cs)
into a WAV that lines up with the shot's frames, by the game's own rules:
script sounds, impacts by hit class and the struck unit's body, order
acknowledgements by sound class, ambient emitters, eight channels with
priority stealing, the original's two-step volume and linear pan.

    python mix.py <shots dir> [name ...]

Writes <name>.wav (48 kHz stereo, 16 bit) and <name>.mix.tsv, the plays
with their clips, volumes and pans. Decoded game sounds stay in memory.
"""
import os
import random
import sys
import wave
import zlib

import numpy as np

from gamedata import GameFiles, Units, pick

RATE = 48000
FPS = 60
CHANNELS = 8
MASTER = 100 / 127.0      # the engine's default sound level
HEADROOM = 0.55           # many voices at full level, kept clear of clipping
MELEE_DELAY = 0.15        # a blade lands this long after its swing sounds


class Clips:
    def __init__(self, files):
        self.files = files
        self.cache = {}

    def get(self, name):
        key = name.lower()
        if key in self.cache:
            return self.cache[key]
        p = self.files.wav(name)
        data = None
        if p:
            try:
                data = decode(p)
            except Exception as e:  # a clip that will not read is left out
                print("  cannot read %s: %s" % (name, e))
        self.cache[key] = data
        return data


def decode(path):
    with wave.open(path, "rb") as w:
        ch, width, rate, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if width == 1:
        a = (np.frombuffer(raw, np.uint8).astype(np.float32) - 128.0) / 128.0
    elif width == 2:
        a = np.frombuffer(raw, "<i2").astype(np.float32) / 32768.0
    else:
        raise ValueError("%d byte samples" % width)
    if ch > 1:
        a = a.reshape(-1, ch).mean(axis=1)
    out_n = int(round(len(a) * RATE / rate))
    t = np.arange(out_n) * (rate / RATE)
    return np.interp(t, np.arange(len(a)), a).astype(np.float32)


class Mixer:
    def __init__(self, length):
        self.buf = np.zeros((length, 2), np.float32)
        self.voices = []   # [start, end, priority, serial, clip, gains, sample]
        self.serial = 0
        self.log = []

    def play(self, at, clip, name, volume, pan, priority, steal=True):
        if clip is None or at >= len(self.buf):
            return False
        live = [v for v in self.voices if v[1] > at]
        if len(live) >= CHANNELS:
            if not steal:
                return False
            lower = [v for v in live if v[2] < priority]
            if not lower:
                return False
            victim = min(lower, key=lambda v: (v[2], v[3]))
            self.cut(victim, at)
            live.remove(victim)
        self.voices = live
        g = volume / 127.0 * MASTER
        p = (pan - 64) / 64.0
        gains = (g * (1 - max(0.0, p)), g * (1 - max(0.0, -p)))
        end = at + len(clip)
        self.serial += 1
        self.voices.append([at, end, priority, self.serial, clip, gains, name])
        n = min(len(clip), len(self.buf) - at)
        self.buf[at:at + n, 0] += clip[:n] * gains[0]
        self.buf[at:at + n, 1] += clip[:n] * gains[1]
        self.log.append((at, name, volume, pan, priority))
        return True

    def cut(self, v, at):
        # Take back what the stolen voice would have played after `at`,
        # with a short fade so the cut does not click.
        start, end, _, _, clip, gains, _ = v
        fade = min(240, max(0, end - at))
        off = at - start
        tail = clip[off:]
        ramp = np.ones(len(tail), np.float32)
        if fade > 0:
            ramp[:fade] = np.linspace(0, 1, fade, dtype=np.float32)
        n = min(len(tail), len(self.buf) - at)
        if n > 0:
            self.buf[at:at + n, 0] -= tail[:n] * ramp[:n] * gains[0]
            self.buf[at:at + n, 1] -= tail[:n] * ramp[:n] * gains[1]
        v[1] = at


def spatial(vx, vy, vz):
    inside = vz > 0 and 0 <= vx <= 1 and 0 <= vy <= 1
    x = vx if vz > 0 else 1 - vx
    return (0x7F if inside else 0x40), max(0, min(127, int(round(64 + 64 * (x - 0.5)))))


def seeded(*parts):
    r = random.Random(zlib.crc32("|".join(str(p) for p in parts).encode()))
    return r.random


def read_log(path):
    events = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            if not line.strip() or line.startswith("#"):
                continue
            events.append(line.rstrip("\n").split("\t"))
    return events


def mix_shot(shots, name, files, units, clips, frames=None):
    events = read_log(os.path.join(shots, name + ".sounds.tsv"))
    if frames is None:
        frames = count_frames(shots, name)
    length = int(frames * RATE // FPS)
    m = Mixer(length)
    plays = []   # (sample, order, clip name, volume, pan, priority, steal)

    def at(frame, seconds=0.0):
        return int(int(frame) * RATE // FPS + seconds * RATE)

    def weapon_hit(shooter, struck, water, melee):
        for w in units.weapons(shooter):
            is_melee = w.get("type", "").lower() == "melee"
            if is_melee != melee:
                continue
            if water:
                return w.get("soundwater")
            cls = w.get("soundhitclass")
            if cls:
                hit = units.classes.get(cls.lower())
                if hit is None:
                    hit = hit_classes(files).get(cls.lower())
                if hit is None:
                    return w.get("soundhit")
                body = units.bodytype(struck) if struck and struck != "-" else None
                sec = hit.get(body) if body else None
                if not isinstance(sec, dict):
                    sec = next((s for _, s in hit["__order__"] if isinstance(s, dict)), None)
                return pick(sec, seeded(shooter, struck, len(plays))) if sec else None
            if w.get("soundhit"):
                return w.get("soundhit")
        return None

    ambient = {}
    for e in events:
        kind, frame = e[0], int(e[1])
        if kind == "U":
            sid, unit, fn = e[2], e[3], e[4].lower()
            vol, pan = spatial(float(e[5]), float(e[6]), float(e[7]))
            near = e[8] if len(e) > 8 else "-"
            cues = files.cob_sounds(unit).get(fn, [])
            last = 0
            for snd, cat, ms in sorted(set(cues), key=lambda c: c[2]):
                last = max(last, ms)
                if cat in (0, 1):
                    continue     # only for a selected unit, and the trailer selects none
                if fn == "dying" and seeded(sid, snd)() >= 0.25:
                    continue     # the shipped scripts cry one death in four
                if cat == 7:
                    plays.append((at(frame, ms / 1000.0), snd, 0x7F, 64, 7, True))
                else:
                    plays.append((at(frame, ms / 1000.0), snd, vol, pan, cat, True))
            if fn.startswith("attack") and near != "-":
                snd = weapon_hit(unit, near, False, True)
                if snd:
                    plays.append((at(frame, last / 1000.0 + MELEE_DELAY), snd, vol, pan, 4, True))
        elif kind == "H":
            shooter, struck, water = e[3], e[4], e[5] == "1"
            vol, pan = spatial(float(e[6]), float(e[7]), float(e[8]))
            if shooter == "-":
                continue
            snd = weapon_hit(shooter, struck, water, False)
            if snd:
                plays.append((at(frame), snd, vol, pan, 4, True))
        elif kind == "A":
            unit, action = e[2], e[3].lower()
            cls = units.sound_class(unit)
            if cls:
                sec = cls.get(action)
                if not isinstance(sec, dict):
                    sec = next((s for _, s in cls["__order__"] if isinstance(s, dict)), None)
                weighted = cls.get("prioritized", "0") == "1"
                snd = pick_class(sec, weighted, seeded(unit, action, frame)) if sec else None
                if snd:
                    plays.append((at(frame), snd, 0x7F, 64, 7, True))
        elif kind == "I":
            plays.append((at(frame), e[2], 0x7F, 64, 7, True))
        elif kind == "F":
            ambient.setdefault(frame, []).append((e[2], int(e[3])))
    plays += ambient_plays(ambient, files, at)
    plays.sort(key=lambda p: p[0])
    for sample, snd, vol, pan, pri, steal in plays:
        m.play(sample, clips.get(snd), snd, vol, pan, pri, steal)
    write_wav(os.path.join(shots, name + ".wav"), m.buf)
    with open(os.path.join(shots, name + ".mix.tsv"), "w", encoding="utf-8") as f:
        f.write("# seconds\tclip\tvolume\tpan\tpriority\n")
        for sample, snd, vol, pan, pri in m.log:
            f.write("%.3f\t%s\t%d\t%d\t%d\n" % (sample / RATE, snd, vol, pan, pri))
    return len(m.log), len(plays)


def pick_class(sec, weighted, rng):
    items = [(k, v) for k, v in sec.get("__order__", []) if not isinstance(v, dict)]
    # The game picks between a kingdom's tone and the unit's own voice by
    # weight. The trailer takes the voice where there is one.
    voices = [(k, v) for k, v in items if not k.lower().startswith("tone")]
    if voices:
        items = voices
        sec = {"__order__": voices}
    if not items:
        return None
    if not weighted:
        k = items[int(rng() * len(items)) % len(items)][0]
    else:
        k = pick(sec, rng)
    return k


_hits = {}


def hit_classes(files):
    if not _hits:
        t = files.tdf("gamedata/soundclasses/soundclasses.tdf") or {"__order__": []}
        for name, sec in t["__order__"]:
            if isinstance(sec, dict):
                _hits[name] = sec
    return _hits


def ambient_plays(steps, files, at):
    """Each emitter in view counts down from SoundDelay plus or minus
    SoundVariance seconds, then plays its class flat at 0x40, priority 1,
    and only into a free channel."""
    defs = feature_sounds(files)
    amb = files.tdf("gamedata/soundclasses/ambient.tdf") or {}
    timers = {}
    plays = []
    step = 20
    for frame in sorted(steps):
        seen = set()
        for name, count in steps[frame]:
            d = defs.get(name.lower())
            if not d:
                continue
            cls, delay, var = d
            sec = amb.get(cls.lower())
            if not isinstance(sec, dict):
                continue
            for k in range(count):
                key = (name.lower(), k)
                seen.add(key)
                rng = seeded(name, k, frame)
                if key not in timers:
                    timers[key] = max(1, int((delay + (rng() * 2 - 1) * var) * FPS)) * rng()
                timers[key] -= step
                if timers[key] <= 0:
                    timers[key] = max(1, int((delay + (rng() * 2 - 1) * var) * FPS))
                    inner = next((s for _, s in sec["__order__"] if isinstance(s, dict)), None)
                    snd = pick(inner, rng) if inner else None
                    if snd:
                        plays.append((at(frame), snd, 0x40, 64, 1, False))
        for key in list(timers):
            if key not in seen:
                del timers[key]
    return plays


_features = {}


def feature_sounds(files):
    if not _features:
        for rel in files.under("features", ".tdf"):
            t = files.tdf(rel)
            for name, sec in (t or {}).get("__order__", []):
                if isinstance(sec, dict) and "soundclass" in sec:
                    try:
                        _features[name] = (sec["soundclass"], float(sec.get("sounddelay", "2")), float(sec.get("soundvariance", "0")))
                    except ValueError:
                        pass
    return _features


def write_wav(path, buf):
    a = np.tanh(buf * HEADROOM / 0.9) * 0.9
    pcm = (np.clip(a, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def count_frames(shots, name):
    with open(os.path.join(shots, name + ".frames")) as f:
        return int(f.read().strip())


def main(argv):
    shots = argv[1]
    names = argv[2:] or sorted(f[:-len(".sounds.tsv")] for f in os.listdir(shots) if f.endswith(".sounds.tsv"))
    files = GameFiles()
    units = Units(files)
    clips = Clips(files)
    for n in names:
        played, wanted = mix_shot(shots, n, files, units, clips)
        print("%s: %d of %d sounds played" % (n, played, wanted))


if __name__ == "__main__":
    main(sys.argv)
