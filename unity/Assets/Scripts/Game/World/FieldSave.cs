// FieldSave.cs - the battlefield's look, kept beside a saved game. The
// engine's save holds the battle. This file, the same name ending in
// .okfield, holds what Stars of Darien draws over it: the scar map's
// textures and dips, the dented ground, the snow over old scars, the marks
// fire and magic left on scenery and the fires still burning. It is
// versioned, made of sections a reader skips when it does not know them,
// and deflated. Flying debris is left out, as it settles in seconds.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    // One piece of scenery's marks as a saved game keeps them, with the
    // feature they belong to: its index in the list, its def and its place.
    public struct LookState
    {
        public int Index, Def;
        public Vector3 At;
        public float Char, Bare, Split, SplitAngle, WitherTo, WitherAt;
        public float FrostPeak, FrostAt, SheenPeak, SheenAt, WetPeak, WetAt, GlowPeak, GlowAt, GlowFor;
        public float ShakeAmp, ShakeAt, GustAmp, GustAt;
        public Vector2 ShakeWay, GustWay;

        public static LookState Of(int index, int def, Vector3 at, SceneryLook l) => new LookState
        {
            Index = index, Def = def, At = at,
            Char = l.Char, Bare = l.Bare, Split = l.Split, SplitAngle = l.SplitAngle, WitherTo = l.WitherTo, WitherAt = l.WitherAt,
            FrostPeak = l.FrostPeak, FrostAt = l.FrostAt, SheenPeak = l.SheenPeak, SheenAt = l.SheenAt, WetPeak = l.WetPeak, WetAt = l.WetAt,
            GlowPeak = l.GlowPeak, GlowAt = l.GlowAt, GlowFor = l.GlowFor,
            ShakeAmp = l.ShakeAmp, ShakeAt = l.ShakeAt, ShakeWay = l.ShakeWay, GustAmp = l.GustAmp, GustAt = l.GustAt, GustWay = l.GustWay,
        };

        // Puts the marks on a look, their times moved by shift onto this battle's clock.
        public void ApplyTo(SceneryLook l, float shift)
        {
            l.Char = Char; l.Bare = Bare; l.Split = Split; l.SplitAngle = SplitAngle;
            l.WitherTo = WitherTo; l.WitherAt = WitherAt + shift;
            l.FrostPeak = FrostPeak; l.FrostAt = FrostAt + shift;
            l.SheenPeak = SheenPeak; l.SheenAt = SheenAt + shift;
            l.WetPeak = WetPeak; l.WetAt = WetAt + shift;
            l.GlowPeak = GlowPeak; l.GlowAt = GlowAt + shift; l.GlowFor = GlowFor;
            l.ShakeAmp = ShakeAmp; l.ShakeAt = ShakeAt + shift; l.ShakeWay = ShakeWay;
            l.GustAmp = GustAmp; l.GustAt = GustAt + shift; l.GustWay = GustWay;
        }
    }

    // Everything the sidecar holds.
    public sealed class FieldState
    {
        // The engine save it goes with, its length and its hash.
        public long SaveLength;
        public ulong SaveHash;
        public string Map = "";
        public uint Tick;
        // The battle's clock when it was saved: the simulation's, which the
        // scars keep time by, and the effects', which the scenery's marks do.
        public float Clock, FxClock;
        public ScarState Scars;
        public SnowState Snow;
        public readonly List<LookState> Looks = new List<LookState>();
        public readonly List<FireState> Fires = new List<FireState>();
    }

    public static class FieldSave
    {
        public const string Extension = ".okfield";
        // 1: the scar map and its regions, the snow, the scenery's marks and the fires.
        public const int Version = 1;
        static readonly byte[] Magic = { (byte)'O', (byte)'K', (byte)'F', (byte)'I', (byte)'E', (byte)'L', (byte)'D', 0x1A };

        static readonly uint HeadTag = Tag("HEAD"), ScarTag = Tag("SCAR"), MarksTag = Tag("MRKS"), ShapeTag = Tag("SHAP"),
            FadeTag = Tag("FADE"), DepthTag = Tag("DPTH"), RimTag = Tag("RIMS"), SnowTag = Tag("SNOW"), LookTag = Tag("LOOK"),
            FireTag = Tag("FIRE"), EndTag = Tag("END.");

        // A section's four letters as the file holds them.
        public static uint Tag(string four) => (uint)four[0] | (uint)four[1] << 8 | (uint)four[2] << 16 | (uint)four[3] << 24;

        // The sidecar of a save.
        public static string PathFor(string save) => Path.ChangeExtension(save, Extension);

        // ── A save ────────────────────────────────────────────────────

        // What the world draws now.
        public static FieldState Capture(WorldView world, IGameBackend backend, string map = null)
        {
            var f = new FieldState { Map = map ?? "", Tick = backend != null ? backend.Tick : 0 };
            float clock = backend != null ? backend.Tick / (float)Mathf.Max(1, backend.TicksPerSecond) : 0f;
            f.Clock = world?.Scars != null ? world.Scars.Now : clock;
            f.FxClock = world?.Effects != null && world.Effects.Clock >= 0f ? world.Effects.Clock : clock;
            if (world == null) return f;
            f.Scars = world.Scars?.Capture();
            f.Snow = world.Snow?.Capture();
            if (world.Entities == null) return f;
            var looks = new List<(int index, int def, Vector3 at, SceneryLook look)>();
            world.Entities.MarkedLooks(looks);
            var place = new Dictionary<SceneryLook, int>();
            foreach (var (index, def, at, look) in looks)
            {
                place[look] = f.Looks.Count;
                f.Looks.Add(LookState.Of(index, def, at, look));
            }
            world.Fire?.Capture(f.Fires, l => place.TryGetValue(l, out int k) ? k : -1);
            return f;
        }

        // Writes the sidecar for a save the engine has just written. Returns
        // its size in bytes, or -1 when it could not be written, which
        // leaves the save itself as good as it was.
        public static long Save(string save, WorldView world, IGameBackend backend, string map = null)
        {
            string file = PathFor(save), tmp = file + ".tmp";
            try
            {
                var f = Capture(world, backend, map);
                f.SaveHash = Hash(save, out f.SaveLength);
                using (var s = File.Create(tmp)) Write(s, f);
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
                return new FileInfo(file).Length;
            }
            catch (Exception e)
            {
                Debug.LogWarning("The battlefield's look was not saved with the game: " + e.Message);
                Delete(tmp);
                Delete(file);
                return -1;
            }
        }

        // Drops the sidecar of a save about to be written over.
        public static void Forget(string save)
        {
            Delete(PathFor(save));
            Delete(PathFor(save) + ".tmp");
        }

        static void Delete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // FNV-1a over a file, and its length.
        public static ulong Hash(string file, out long length)
        {
            ulong h = 14695981039346656037UL;
            using (var s = File.OpenRead(file))
            {
                length = s.Length;
                var buf = new byte[1 << 16];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++) { h ^= buf[i]; h *= 1099511628211UL; }
            }
            return h;
        }

        // ── A load ────────────────────────────────────────────────────

        // The sidecar of a save, or null and why: there is none, a newer game
        // wrote it, it is damaged, or it was kept for another save.
        public static FieldState Load(string save, out string why)
        {
            string file = PathFor(save);
            if (!File.Exists(file)) { why = "no field was kept beside it"; return null; }
            FieldState f;
            try
            {
                using (var s = File.OpenRead(file)) f = Read(s, out why);
            }
            catch (Exception e) { why = "its field could not be read: " + e.Message; return null; }
            if (f == null) return null;
            try
            {
                ulong hash = Hash(save, out long length);
                if (hash != f.SaveHash || length != f.SaveLength) { why = "the field kept beside it belongs to another save"; return null; }
            }
            catch (Exception e) { why = "the save could not be read: " + e.Message; return null; }
            why = null;
            return f;
        }

        // The scars and the snow onto a world built for the save, with the
        // battle's clock now. The dented ground follows in ScarMap.RestoreGround.
        public static void RestoreScars(WorldView world, FieldState f, float clock)
        {
            if (world == null || f == null) return;
            world.Scars?.Restore(f.Scars, clock);
            world.Snow?.Restore(f.Snow, clock);
        }

        // The marks on scenery and the fires, once the scenery is built.
        // Returns how many looks found their feature.
        public static int RestoreLooks(WorldView world, FieldState f, float clock)
        {
            if (world?.Entities == null || f == null) return 0;
            float shift = clock - f.FxClock;
            var restored = new SceneryLook[f.Looks.Count];
            int found = 0;
            for (int i = 0; i < f.Looks.Count; i++)
            {
                var s = f.Looks[i];
                var l = world.Entities.LookAt(s.Index, s.Def, s.At);
                if (l == null) continue;
                s.ApplyTo(l, shift);
                if (world.Magic != null) world.Magic.Resume(l, clock);
                else l.Step(clock);
                restored[i] = l;
                found++;
            }
            world.Fire?.Restore(f.Fires, k => k >= 0 && k < restored.Length ? restored[k] : null, shift);
            return found;
        }

        // ── The file ──────────────────────────────────────────────────

        // The magic, the version and the deflated sections. extra adds
        // sections a later version might, for tests of an older reader.
        public static void Write(Stream to, FieldState f, IEnumerable<(string tag, byte[] payload)> extra = null)
        {
            to.Write(Magic, 0, Magic.Length);
            to.WriteByte((byte)(Version & 0xFF));
            to.WriteByte((byte)(Version >> 8));
            to.WriteByte(0);
            to.WriteByte(0);
            using (var z = new DeflateStream(to, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(z, Encoding.UTF8, true))
            {
                Small(w, HeadTag, b =>
                {
                    b.Write(f.SaveLength);
                    b.Write(f.SaveHash);
                    b.Write(f.Map ?? "");
                    b.Write(f.Tick);
                    b.Write(f.Clock);
                    b.Write(f.FxClock);
                });
                if (extra != null)
                    foreach (var (tag, payload) in extra)
                    {
                        w.Write(Tag(tag));
                        w.Write(payload.Length);
                        w.Write(payload);
                    }
                var sc = f.Scars;
                if (sc != null)
                {
                    Small(w, ScarTag, b => WriteScarHead(b, sc));
                    Plane(w, MarksTag, sc.Marks, sc.TexW, sc.TexH, 4);
                    Plane(w, ShapeTag, sc.Shape, sc.TexW, sc.TexH, 4);
                    Plane(w, FadeTag, sc.Fade, sc.TexW, sc.TexH, 4);
                    Plane(w, DepthTag, sc.Depth, sc.TexW, sc.TexH, 1);
                    Plane(w, RimTag, sc.Rim, sc.TexW, sc.TexH, 1);
                }
                if (f.Snow != null)
                    Small(w, SnowTag, b =>
                    {
                        var sn = f.Snow;
                        b.Write(sn.W);
                        b.Write(sn.H);
                        b.Write(sn.Now);
                        b.Write(sn.Since);
                        int n = sn.Scarred != null ? sn.Scarred.Length : 0;
                        b.Write(n);
                        for (int i = 0; i < n; i++) b.Write(sn.Scarred[i]);
                    });
                Small(w, LookTag, b =>
                {
                    b.Write(f.Looks.Count);
                    foreach (var l in f.Looks) WriteLook(b, l);
                });
                Small(w, FireTag, b =>
                {
                    b.Write(f.Fires.Count);
                    foreach (var fire in f.Fires) WriteFire(b, fire);
                });
                w.Write(EndTag);
                w.Write(0);
            }
        }

        // A sidecar from a stream, or null and why.
        public static FieldState Read(Stream from, out string why)
        {
            var magic = new byte[Magic.Length + 4];
            if (Fill(from, magic) < magic.Length) { why = "it is too short to be a kept field"; return null; }
            for (int i = 0; i < Magic.Length; i++)
                if (magic[i] != Magic[i]) { why = "it is not a kept field"; return null; }
            int version = magic[8] | magic[9] << 8;
            if (version > Version) { why = $"a newer Stars of Darien kept it (version {version}, this reads {Version})"; return null; }
            if (version < 1) { why = $"it has no version this game knows ({version})"; return null; }
            var f = new FieldState();
            bool head = false;
            try
            {
                using (var z = new DeflateStream(from, CompressionMode.Decompress, true))
                using (var r = new BinaryReader(z, Encoding.UTF8, true))
                {
                    while (true)
                    {
                        uint tag = r.ReadUInt32();
                        int length = r.ReadInt32();
                        if (length < 0 || length > MaxSection) throw new InvalidDataException($"a section of {length} bytes");
                        if (tag == EndTag) break;
                        var sc = f.Scars;
                        if (tag == HeadTag)
                        {
                            var b = Body(r, length);
                            f.SaveLength = b.ReadInt64();
                            f.SaveHash = b.ReadUInt64();
                            f.Map = b.ReadString();
                            f.Tick = b.ReadUInt32();
                            f.Clock = b.ReadSingle();
                            f.FxClock = b.ReadSingle();
                            head = true;
                        }
                        else if (tag == ScarTag) f.Scars = ReadScarHead(Body(r, length));
                        else if (tag == MarksTag && sc != null) sc.Marks = Plane(r, length, sc.TexW, sc.TexH, 4);
                        else if (tag == ShapeTag && sc != null) sc.Shape = Plane(r, length, sc.TexW, sc.TexH, 4);
                        else if (tag == FadeTag && sc != null) sc.Fade = Plane(r, length, sc.TexW, sc.TexH, 4);
                        else if (tag == DepthTag && sc != null) sc.Depth = Plane(r, length, sc.TexW, sc.TexH, 1);
                        else if (tag == RimTag && sc != null) sc.Rim = Plane(r, length, sc.TexW, sc.TexH, 1);
                        else if (tag == SnowTag)
                        {
                            var b = Body(r, length);
                            var sn = new SnowState { W = b.ReadInt32(), H = b.ReadInt32(), Now = b.ReadSingle(), Since = b.ReadSingle() };
                            int n = b.ReadInt32();
                            if (n < 0 || n > (1 << 24)) throw new InvalidDataException("the snow's size");
                            sn.Scarred = new float[n];
                            for (int i = 0; i < n; i++) sn.Scarred[i] = b.ReadSingle();
                            f.Snow = sn;
                        }
                        else if (tag == LookTag)
                        {
                            var b = Body(r, length);
                            int n = b.ReadInt32();
                            for (int i = 0; i < n; i++) f.Looks.Add(ReadLook(b));
                        }
                        else if (tag == FireTag)
                        {
                            var b = Body(r, length);
                            int n = b.ReadInt32();
                            for (int i = 0; i < n; i++) f.Fires.Add(ReadFire(b));
                        }
                        else Skip(r, length);
                    }
                }
            }
            catch (Exception e)
            {
                why = "it is damaged: " + e.Message;
                return null;
            }
            if (!head) { why = "it does not say which save it belongs to"; return null; }
            why = null;
            return f;
        }

        // ── Sections ──────────────────────────────────────────────────

        // A section small enough to be made whole before it is written.
        static void Small(BinaryWriter w, uint tag, Action<BinaryWriter> body)
        {
            var ms = new MemoryStream();
            using (var b = new BinaryWriter(ms, Encoding.UTF8, true)) body(b);
            w.Write(tag);
            w.Write((int)ms.Length);
            w.Write(ms.GetBuffer(), 0, (int)ms.Length);
        }

        // The most a section holds: a texture at Ultra's size, 4096 texels across.
        const int MaxSection = 4096 * 4096 * 4;

        static BinaryReader Body(BinaryReader r, int length)
        {
            if (length > 64 << 20) throw new InvalidDataException($"a section of {length} bytes");
            var bytes = r.ReadBytes(length);
            if (bytes.Length < length) throw new EndOfStreamException("a section is cut short");
            return new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        }

        static void Skip(BinaryReader r, int length)
        {
            var buf = new byte[Math.Min(length, 1 << 16)];
            while (length > 0)
            {
                int n = r.Read(buf, 0, Math.Min(buf.Length, length));
                if (n <= 0) throw new EndOfStreamException("a section is cut short");
                length -= n;
            }
        }

        static int Fill(Stream s, byte[] into)
        {
            int got = 0;
            while (got < into.Length)
            {
                int n = s.Read(into, got, into.Length - got);
                if (n <= 0) break;
                got += n;
            }
            return got;
        }

        // A texture's texels a channel at a time, each byte written as its
        // difference from the one before it in its row, so the smooth slopes
        // of a crater deflate to little. Absent planes are left out.
        static void Plane(BinaryWriter w, uint tag, byte[] px, int width, int height, int channels)
        {
            if (px == null || px.Length != width * height * channels) return;
            w.Write(tag);
            w.Write(px.Length);
            var row = new byte[width];
            for (int c = 0; c < channels; c++)
                for (int y = 0; y < height; y++)
                {
                    int k = y * width * channels + c;
                    byte prev = 0;
                    for (int x = 0; x < width; x++, k += channels)
                    {
                        byte v = px[k];
                        row[x] = (byte)(v - prev);
                        prev = v;
                    }
                    w.Write(row);
                }
        }

        static byte[] Plane(BinaryReader r, int length, int width, int height, int channels)
        {
            if (length != width * height * channels) { Skip(r, length); return null; }
            var px = new byte[length];
            for (int c = 0; c < channels; c++)
                for (int y = 0; y < height; y++)
                {
                    var row = r.ReadBytes(width);
                    if (row.Length < width) throw new EndOfStreamException("a texture is cut short");
                    int k = y * width * channels + c;
                    byte prev = 0;
                    for (int x = 0; x < width; x++, k += channels)
                    {
                        prev = (byte)(prev + row[x]);
                        px[k] = prev;
                    }
                }
            return px;
        }

        static void WriteScarHead(BinaryWriter b, ScarState s)
        {
            b.Write((byte)s.Level);
            b.Write(s.TexW);
            b.Write(s.TexH);
            b.Write(s.SizeX);
            b.Write(s.SizeZ);
            b.Write(s.Now);
            b.Write(s.SteppedAt);
            b.Write(s.Marked);
            var pending = s.Pending ?? new ScarStamp[0];
            b.Write(pending.Length);
            foreach (var p in pending) WriteStamp(b, p);
            var regions = s.Regions ?? new (int, int, int)[0];
            b.Write(regions.Length);
            foreach (var (x, y, factor) in regions)
            {
                b.Write(x);
                b.Write(y);
                b.Write(factor);
            }
        }

        static ScarState ReadScarHead(BinaryReader b)
        {
            var s = new ScarState
            {
                Level = (EffectsQuality)b.ReadByte(), TexW = b.ReadInt32(), TexH = b.ReadInt32(), SizeX = b.ReadSingle(), SizeZ = b.ReadSingle(),
                Now = b.ReadSingle(), SteppedAt = b.ReadSingle(), Marked = b.ReadInt32(),
            };
            if (s.TexW <= 0 || s.TexH <= 0 || s.TexW > 4096 || s.TexH > 4096) throw new InvalidDataException("the scar map's size");
            int n = b.ReadInt32();
            if (n < 0 || n > 1 << 20) throw new InvalidDataException("the stamps waiting");
            s.Pending = new ScarStamp[n];
            for (int i = 0; i < n; i++) s.Pending[i] = ReadStamp(b);
            n = b.ReadInt32();
            if (n < 0 || n > 1 << 20) throw new InvalidDataException("the dented regions");
            s.Regions = new (int, int, int)[n];
            for (int i = 0; i < n; i++) s.Regions[i] = (b.ReadInt32(), b.ReadInt32(), b.ReadInt32());
            return s;
        }

        static void WriteStamp(BinaryWriter b, in ScarStamp s)
        {
            b.Write((byte)s.Kind);
            foreach (float v in new[] { s.X, s.Z, s.DirX, s.DirZ, s.Reach, s.Length, s.Dent, s.Depth, s.Rim, s.Floor, s.Stretch, s.Lobes,
                         s.Char, s.Soil, s.Blight, s.Stone, s.Crack, s.Frost, s.Wet, s.Holy, s.Heat })
                b.Write(v);
            b.Write(s.Seed);
        }

        static ScarStamp ReadStamp(BinaryReader b) => new ScarStamp
        {
            Kind = (ScarKind)b.ReadByte(), X = b.ReadSingle(), Z = b.ReadSingle(), DirX = b.ReadSingle(), DirZ = b.ReadSingle(),
            Reach = b.ReadSingle(), Length = b.ReadSingle(), Dent = b.ReadSingle(), Depth = b.ReadSingle(), Rim = b.ReadSingle(),
            Floor = b.ReadSingle(), Stretch = b.ReadSingle(), Lobes = b.ReadSingle(), Char = b.ReadSingle(), Soil = b.ReadSingle(),
            Blight = b.ReadSingle(), Stone = b.ReadSingle(), Crack = b.ReadSingle(), Frost = b.ReadSingle(), Wet = b.ReadSingle(),
            Holy = b.ReadSingle(), Heat = b.ReadSingle(), Seed = b.ReadInt32(),
        };

        static void WriteLook(BinaryWriter b, in LookState l)
        {
            b.Write(l.Index);
            b.Write(l.Def);
            foreach (float v in new[] { l.At.x, l.At.y, l.At.z, l.Char, l.Bare, l.Split, l.SplitAngle, l.WitherTo, l.WitherAt,
                         l.FrostPeak, l.FrostAt, l.SheenPeak, l.SheenAt, l.WetPeak, l.WetAt, l.GlowPeak, l.GlowAt, l.GlowFor,
                         l.ShakeAmp, l.ShakeAt, l.ShakeWay.x, l.ShakeWay.y, l.GustAmp, l.GustAt, l.GustWay.x, l.GustWay.y })
                b.Write(v);
        }

        static LookState ReadLook(BinaryReader b) => new LookState
        {
            Index = b.ReadInt32(), Def = b.ReadInt32(), At = new Vector3(b.ReadSingle(), b.ReadSingle(), b.ReadSingle()),
            Char = b.ReadSingle(), Bare = b.ReadSingle(), Split = b.ReadSingle(), SplitAngle = b.ReadSingle(), WitherTo = b.ReadSingle(),
            WitherAt = b.ReadSingle(), FrostPeak = b.ReadSingle(), FrostAt = b.ReadSingle(), SheenPeak = b.ReadSingle(), SheenAt = b.ReadSingle(),
            WetPeak = b.ReadSingle(), WetAt = b.ReadSingle(), GlowPeak = b.ReadSingle(), GlowAt = b.ReadSingle(), GlowFor = b.ReadSingle(),
            ShakeAmp = b.ReadSingle(), ShakeAt = b.ReadSingle(), ShakeWay = new Vector2(b.ReadSingle(), b.ReadSingle()),
            GustAmp = b.ReadSingle(), GustAt = b.ReadSingle(), GustWay = new Vector2(b.ReadSingle(), b.ReadSingle()),
        };

        static void WriteFire(BinaryWriter b, in FireState f)
        {
            foreach (float v in new[] { f.At.x, f.At.y, f.At.z, f.From.x, f.From.y, f.From.z, f.Way.x, f.Way.y, f.Way.z })
                b.Write(v);
            b.Write(f.Def);
            b.Write(f.Seed);
            b.Write(f.Marks);
            foreach (float v in new[] { f.Start, f.Seconds, f.SparkAt, f.OutAt, f.DousedAt, f.FanUntil, f.SmoulderFor, f.WispSize })
                b.Write(v);
            b.Write((byte)((f.Out ? 1 : 0) | (f.Doused ? 2 : 0) | (f.Sparked ? 4 : 0) | (f.Wisp ? 8 : 0)));
            b.Write(f.Look);
        }

        static FireState ReadFire(BinaryReader b)
        {
            var f = new FireState
            {
                At = new Vector3(b.ReadSingle(), b.ReadSingle(), b.ReadSingle()), From = new Vector3(b.ReadSingle(), b.ReadSingle(), b.ReadSingle()),
                Way = new Vector3(b.ReadSingle(), b.ReadSingle(), b.ReadSingle()), Def = b.ReadInt32(), Seed = b.ReadInt32(), Marks = b.ReadInt32(),
                Start = b.ReadSingle(), Seconds = b.ReadSingle(), SparkAt = b.ReadSingle(), OutAt = b.ReadSingle(), DousedAt = b.ReadSingle(),
                FanUntil = b.ReadSingle(), SmoulderFor = b.ReadSingle(), WispSize = b.ReadSingle(),
            };
            int flags = b.ReadByte();
            f.Out = (flags & 1) != 0;
            f.Doused = (flags & 2) != 0;
            f.Sparked = (flags & 4) != 0;
            f.Wisp = (flags & 8) != 0;
            f.Look = b.ReadInt32();
            return f;
        }
    }
}
