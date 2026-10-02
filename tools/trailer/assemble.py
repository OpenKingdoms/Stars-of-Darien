"""Cuts the trailer from the filmed shots: title cards drawn in the game's
fonts, each shot trimmed with its mixed sound, dissolves between them, and
the sound levelled. Writes the MP4, a shot list with timestamps and stills.

    python assemble.py [--timeline timeline.json] [--shots DIR] [--out MP4]
                       [--discord LINK] [--music FILE] [--music-db -14]
                       [--stills DIR] [--remix]

Cards are text, so a new Discord link re-renders in seconds:
    python assemble.py --discord https://discord.gg/abcdef
--music lays a track under the game's sounds, off by default.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
FONTS = os.path.join(ROOT, "unity", "Assets", "Game", "Resources", "Fonts")
DEFAULT_FFMPEG = "D:/OKBuild/tools/vidvenv/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe"
FPS = 60
W, H = 1920, 1080
RATE = 48000
GOLD = "0xE8C67E"
PALE = "0xDCD3BE"
BACK = "0x0A0806"


def ffmpeg_path():
    p = os.environ.get("FFMPEG") or shutil.which("ffmpeg") or DEFAULT_FFMPEG
    return p


def run(args):
    r = subprocess.run(args, capture_output=True, text=True)
    if r.returncode != 0:
        sys.stderr.write(r.stderr[-4000:])
        raise SystemExit("ffmpeg failed: %s" % " ".join(args[:6]))
    return r


def esc_path(p):
    # drawtext's option syntax: forward slashes, and the drive colon escaped.
    return p.replace("\\", "/").replace(":", "\\:")


def card(ff, seg, path, work, discord):
    """A title card: lines of text centred on near black, fading in and out."""
    secs = float(seg["seconds"])
    fade = float(seg.get("fade", 0.6))
    lines = seg["card"]
    filters = ["vignette=angle=PI/3.2"]
    total = sum(int(l.get("size", 60)) * 1.35 for l in lines)
    y = (H - total) / 2
    for i, l in enumerate(lines):
        text = l["text"].replace("[DISCORD LINK]", discord or "[DISCORD LINK]")
        tf = os.path.join(work, "card-%s-%d.txt" % (os.path.basename(path)[:-4], i))
        with open(tf, "w", encoding="utf-8") as f:
            f.write(text)
        size = int(l.get("size", 60))
        font = os.path.join(FONTS, {"title": "UncialAntiqua.ttf", "caps": "Cinzel.ttf", "body": "EBGaramond.ttf"}[l.get("font", "caps")])
        colour = l.get("colour", GOLD if l.get("font", "caps") == "title" else PALE)
        delay = float(l.get("delay", 0))
        a = "if(lt(t,{d}),0,if(lt(t,{d}+{f}),(t-{d})/{f},if(gt(t,{s}-{f}),max(0,({s}-t)/{f}),1)))".format(d=delay, f=fade, s=secs)
        filters.append(
            "drawtext=fontfile='{font}':textfile='{tf}':fontsize={size}:fontcolor={c}:x=(w-text_w)/2:y={y}:"
            "alpha='{a}':shadowcolor=0x000000@0.8:shadowx=0:shadowy=3".format(
                font=esc_path(font), tf=esc_path(tf), size=size, c=colour, y=int(y), a=a))
        y += size * 1.35
        if l.get("rule"):
            filters.append("drawbox=x=(iw-{w})/2:y={y}:w={w}:h=2:color={c}@0.55:t=fill".format(w=int(l["rule"]), y=int(y - size * 0.15), c=GOLD))
    frames = int(round(secs * FPS))
    run([ff, "-y", "-loglevel", "error",
         "-f", "lavfi", "-i", "color=c=%s:s=%dx%d:r=%d:d=%.4f" % (BACK, W, H, FPS, frames / FPS),
         "-f", "lavfi", "-i", "anullsrc=r=%d:cl=stereo" % RATE,
         "-vf", ",".join(filters) + ",format=yuv420p", "-frames:v", str(frames),
         "-af", "atrim=end_sample=%d" % (frames * RATE // FPS),
         "-c:v", "libx264", "-preset", "medium", "-crf", "14", "-c:a", "pcm_s16le", path])
    return frames


def shot(ff, seg, path, shots):
    """A shot trimmed to its from and to (seconds), with its mixed sound."""
    name = seg["shot"]
    src = os.path.join(shots, name + ".mp4")
    wav = os.path.join(shots, name + ".wav")
    f0 = int(round(float(seg.get("from", 0)) * FPS))
    total = int(open(os.path.join(shots, name + ".frames")).read().strip())
    f1 = int(round(float(seg["to"]) * FPS)) if "to" in seg else total
    f1 = min(f1, total)
    frames = f1 - f0
    vf = "trim=start_frame=%d:end_frame=%d,setpts=PTS-STARTPTS" % (f0, f1)
    if seg.get("slow"):
        vf += ",setpts=%s*PTS" % seg["slow"]
    vf += ",format=yuv420p"
    args = [ff, "-y", "-loglevel", "error", "-i", src]
    if os.path.exists(wav):
        args += ["-i", wav]
        af = "atrim=start_sample=%d:end_sample=%d,asetpts=PTS-STARTPTS,apad,atrim=end_sample=%d" % (
            f0 * RATE // FPS, f1 * RATE // FPS, frames * RATE // FPS)
        if seg.get("db"):
            af += ",volume=%sdB" % seg["db"]
    else:
        args += ["-f", "lavfi", "-i", "anullsrc=r=%d:cl=stereo" % RATE]
        af = "atrim=end_sample=%d" % (frames * RATE // FPS)
    args += ["-vf", vf, "-af", af, "-map", "0:v", "-map", "1:a", "-frames:v", str(frames),
             "-r", str(FPS), "-c:v", "libx264", "-preset", "medium", "-crf", "12", "-c:a", "pcm_s16le", path]
    run(args)
    return frames


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--timeline", default=os.path.join(HERE, "timeline.json"))
    ap.add_argument("--shots", default="D:/OKBuild/video/trailer-work/shots")
    ap.add_argument("--out", default="D:/OKBuild/video/darien-reforged-alpha-trailer.mp4")
    ap.add_argument("--discord", default=os.environ.get("DISCORD_LINK", ""))
    ap.add_argument("--music", default="")
    ap.add_argument("--music-db", default="-14")
    ap.add_argument("--stills", default="D:/OKBuild/video/stills")
    ap.add_argument("--remix", action="store_true", help="mix every shot's sound again first")
    ap.add_argument("--crf", default="16", help="x264 quality, higher for a smaller file")
    a = ap.parse_args()
    ff = ffmpeg_path()
    os.environ["FFMPEG"] = ff
    tl = json.load(open(a.timeline, encoding="utf-8"))
    work = os.path.join(os.path.dirname(a.shots), "segments")
    os.makedirs(work, exist_ok=True)
    if a.remix:
        sys.path.insert(0, HERE)
        import mix
        names = sorted({s["shot"] for s in tl["segments"] if "shot" in s})
        mix.main(["mix", a.shots] + names)

    segs = []
    for i, seg in enumerate(tl["segments"]):
        path = os.path.join(work, "%02d.mkv" % i)
        frames = card(ff, seg, path, work, a.discord) if "card" in seg else shot(ff, seg, path, a.shots)
        segs.append((seg, path, frames))
        print("segment %02d %-28s %5.2fs" % (i, seg.get("shot") or seg["card"][0]["text"][:28], frames / FPS))

    # Dissolves: each segment's "trans" seconds overlap the one before.
    args = [ff, "-y", "-loglevel", "error"]
    for _, p, _ in segs:
        args += ["-i", p]
    parts, vparts, starts = [], [], []
    v, au = "[0:v]", "[0:a]"
    t = segs[0][2] / FPS
    starts.append(0.0)
    for i in range(1, len(segs)):
        seg, _, frames = segs[i]
        d = max(2.0 / FPS, float(seg.get("trans", 0.5)))
        style = seg.get("style", "fade")
        off = t - d
        starts.append(off)
        vparts.append("%s[%d:v]xfade=transition=%s:duration=%.4f:offset=%.4f[v%d]" % (v, i, style, d, off, i))
        parts.append("%s[%d:a]acrossfade=d=%.4f:c1=tri:c2=tri[a%d]" % (au, i, d, i))
        v, au = "[v%d]" % i, "[a%d]" % i
        t = off + frames / FPS
    music_in = len(segs)
    if a.music:
        args += ["-stream_loop", "-1", "-i", a.music]
        parts.append("[%d:a]atrim=end=%.3f,afade=t=in:d=1.5,afade=t=out:st=%.3f:d=2.5,volume=%sdB[m]" % (music_in, t, max(0, t - 2.5), a.music_db))
        parts.append("%s[m]amix=inputs=2:duration=first:normalize=0[am]" % au)
        au = "[am]"
    # Loudness in two passes: measure the whole, then one steady gain, so
    # quiet moments stay quiet and the battles keep their weight.
    target = "I=-16:TP=-1.5:LRA=20"
    probe = run(["info" if x == "error" else x for x in args] + ["-filter_complex", ";".join(parts + ["%sloudnorm=%s:print_format=json[aout]" % (au, target)]),
                        "-map", "[aout]", "-f", "null", "-"]).stderr
    m = json.loads(probe[probe.rindex("{"):probe.rindex("}") + 1])
    level = "loudnorm=%s:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:offset=%s:linear=true" % (
        target, m["input_i"], m["input_tp"], m["input_lra"], m["input_thresh"], m["target_offset"])
    if "inf" in m["input_i"]:
        level = "anull"   # all silent
    print("loudness: measured %s LUFS, peak %s dBTP" % (m["input_i"], m["input_tp"]))
    parts.append("%s%s,aresample=%d[aout]" % (au, level, RATE))
    vparts.append("%sformat=yuv420p[vout]" % v)
    args += ["-filter_complex", ";".join(vparts + parts), "-map", "[vout]", "-map", "[aout]",
             "-r", str(FPS), "-c:v", "libx264", "-preset", "slow", "-crf", a.crf, "-profile:v", "high",
             "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-ar", str(RATE),
             "-movflags", "+faststart", "-t", "%.4f" % t, a.out]
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    run(args)

    # The shot list, with each segment's start in the finished trailer.
    lines = ["# Darien Reforged, Alpha 1 trailer: shot list", "", "Length %d:%05.2f, %dx%d at %d fps." % (int(t // 60), t % 60, W, H, FPS), ""]
    for (seg, _, frames), s in zip(segs, starts):
        what = ("Card: " + " / ".join(l["text"] for l in seg["card"])) if "card" in seg else seg.get("note", seg["shot"])
        lines.append("%d:%05.2f  %-6s %s" % (int(s // 60), s % 60, "(%.1fs)" % (frames / FPS), what.replace("[DISCORD LINK]", a.discord or "[DISCORD LINK]")))
    shotlist = os.path.splitext(a.out)[0] + "-shotlist.txt"
    with open(shotlist, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))

    # Stills for the store page, from the shots at full quality.
    if tl.get("stills"):
        os.makedirs(a.stills, exist_ok=True)
        review = os.path.join(os.path.dirname(a.shots), "review")
        for st in tl["stills"]:
            out = os.path.join(a.stills, st["name"] + ".png")
            # The director's own lossless picture of that frame when there is
            # one, else the frame from the shot's video.
            frame = int(st["frame"]) if "frame" in st else int(round(float(st.get("at", 0)) * FPS))
            png = os.path.join(review, "%s-%04d.png" % (st["shot"], frame))
            if os.path.exists(png):
                shutil.copyfile(png, out)
            else:
                run([ff, "-y", "-loglevel", "error", "-i", os.path.join(a.shots, st["shot"] + ".mp4"),
                     "-vf", r"select=eq(n\,%d)" % frame, "-frames:v", "1", out])
        print("stills in", a.stills)
    print("wrote", a.out)


if __name__ == "__main__":
    main()
