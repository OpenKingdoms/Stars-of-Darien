// ScarMap.cs - the battlefield's scars, kept the whole battle: each blast
// becomes a stamp that waits in a capped queue, and a budget of them a
// frame is drawn into the scar map's textures and its CPU copy of the dips.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed partial class ScarMap
    {
        // The scar map of the battle being drawn, null when there is none.
        public static ScarMap Current { get; private set; }

        // How far the drawn ground lies from the engine's height at a world
        // point, in world units: below 0 in a crater, above 0 on its thrown
        // rim, 0 where nothing dented it or the quality draws no dips.
        public static float GroundOffset(float x, float z)
        {
            var m = Current;
            return m != null ? m.Offset(x, z) : 0f;
        }

        // How far to move something standing at a world point, a unit, a
        // feature, a corpse or rubble, so it sits on the dented ground.
        // Anything well above the engine's ground, a flyer or a shot, stays.
        public static float SinkY(Vector3 at)
        {
            var m = Current;
            float o = m != null ? m.Offset(at.x, at.z) : 0f;
            return o != 0f ? o * m.Standing(at) : 0f;
        }

        // SinkY as a move, to multiply onto a drawn matrix from the left.
        public static Matrix4x4 Sink(Vector3 at)
        {
            float y = SinkY(at);
            return y != 0f ? Matrix4x4.Translate(new Vector3(0f, y, 0f)) : Matrix4x4.identity;
        }

        // ── Budgets ───────────────────────────────────────────────────

        // Texels across the map at most.
        public static int MaxTexels(EffectsQuality level) => level == EffectsQuality.Ultra ? 4096 : 2048;

        // Stamps drawn in a frame, and how many may wait for later frames.
        public static int StampsPerFrame(EffectsQuality level) =>
            level == EffectsQuality.Low ? 24 : level == EffectsQuality.Medium ? 48 : level == EffectsQuality.Ultra ? 160 : 96;

        public static int QueueCap(EffectsQuality level) => StampsPerFrame(level) * 16;

        // World units between the ground's vertices where it dips.
        public static float MeshSpacing(EffectsQuality level) => level == EffectsQuality.Ultra ? 0.25f : 0.5f;

        // Dent texels written on the CPU in a frame, before the rest wait.
        public const int DentTexelsPerFrame = 16000;
        // A step of the fading textures, and the seconds a byte of them holds.
        public const float StepSeconds = 0.5f, FadeRange = 127.5f;

        // ── The battle's scar map ─────────────────────────────────────

        readonly IGameBackend backend;
        readonly TerrainView ground;
        public readonly string Climate;
        readonly Vector2 size;
        FxQuality quality;
        Vector4 soil, snow;

        public EffectsQuality Level { get; private set; }
        public ScarGround Ground => quality.Ground;
        public DentField Dents { get; private set; }

        readonly List<ScarStamp> pending = new List<ScarStamp>();
        readonly List<ScarStamp> batch = new List<ScarStamp>();
        readonly BlastEvent[] blasts = new BlastEvent[256];
        int lastBlast, marked;
        float now, steppedAt;

        // What it has done, for tests and the log.
        public int Pending => pending.Count;
        public int PeakPending { get; private set; }
        public int StampedLastFrame { get; private set; }
        public int PeakStamped { get; private set; }
        public long Stamped { get; private set; }
        public int Merged { get; private set; }
        public int Dropped { get; private set; }
        public int BlastsSeen { get; private set; }
        public double LastMs { get; private set; }
        public float Now => now;
        // Each stamp as it is drawn, for looks that follow the scars.
        public event System.Action<ScarStamp> Stamping;

        ScarMap(IGameBackend backend, TerrainView ground, string climate, EffectsQuality level)
        {
            this.backend = backend;
            this.ground = ground;
            Climate = climate ?? "";
            Level = level;
            quality = FxQuality.For(level);
            size = backend.Terrain != null ? backend.Terrain.Size : new Vector2(64f, 64f);
            (soil, snow) = Palette(Climate);
            Size(level);
            Dents = new DentField(TexW, TexH, size.x, size.y);
            MakeGpu();
            SetGlobals();
            Warm();
        }

        // Everything the first blast of the battle would run for the first
        // time done here, while it loads: every weapon sorted into its kind,
        // a stamp made, dented and drawn, and the backend's blasts read.
        void Warm()
        {
            var defs = backend.UnitDefs;
            if (defs != null)
                foreach (var d in defs)
                    foreach (int slot in WarmSlots)
                    {
                        var w = backend.Weapon(d.Id, slot);
                        if (w != null) FxKinds.Of(w, d.Name);
                    }
            // A stamp that leaves nothing, at the map's corner.
            var nothing = ScarStamps.Make(ScarKind.Gunpowder, Vector3.zero, 2f, Vector3.forward, 0f, 1);
            nothing.Dent = 0.5f;
            nothing.Depth = nothing.Rim = 0f;
            Dents.Stamp(nothing);
            batch.Clear();
            batch.Add(nothing);
            DrawStamps(batch);
            batch.Clear();
            ReadBlasts();
        }

        static readonly int[] WarmSlots = { 0, 1, 2, WeaponSlot.Death };

        // Starts the battle's scar map, the one GroundOffset reads.
        public static ScarMap Begin(IGameBackend backend, TerrainView ground, string climate, EffectsQuality level)
        {
            Current?.Dispose();
            var m = new ScarMap(backend, ground, climate, level);
            Current = m;
            return m;
        }

        public void Dispose()
        {
            ReleaseGpu();
            pending.Clear();
            if (Current != this) return;
            Current = null;
            ClearGlobals();
        }

        // A frame: the setting followed, new blasts read and queued, a
        // budget of stamps drawn, regions dented, and the fading stepped.
        // simSeconds is the battle's own clock, so a pause holds the fading.
        public void Update(float simSeconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int gcs = System.GC.CollectionCount(0);
            if (FxQuality.Current.Level != Level) Requalify(FxQuality.Current.Level);
            if (simSeconds < now) steppedAt = simSeconds;
            now = simSeconds;
            ReadBlasts();
            double read = clock.Elapsed.TotalMilliseconds;
            StampSome();
            double stamped = clock.Elapsed.TotalMilliseconds;
            string put = ground != null ? ground.LastPut : "";
            ground?.StepRefine();
            double refined = clock.Elapsed.TotalMilliseconds;
            if (ground != null && !ReferenceEquals(put, ground.LastPut)) put = ground.LastPut; else put = "";
            StepFading();
            SetGlobals();
            LastMs = clock.Elapsed.TotalMilliseconds;
            if (LastMs > WorstMs)
            {
                WorstMs = LastMs;
                WorstParts = $"read {read:0.00} ({blastsRead} blasts, {engineMs:0.00} in the backend) stamp {stamped - read:0.00} ({StampedLastFrame}) refine {refined - stamped:0.00}{(put.Length > 0 ? " [" + put + "]" : "")} fade {LastMs - refined:0.00}" +
                             (System.GC.CollectionCount(0) != gcs ? ", a collection ran" : "");
            }
        }

        // The slowest update so far and what it spent its time on, for tests.
        public double WorstMs { get; private set; }
        public string WorstParts { get; private set; } = "";
        public void ResetWorst() { WorstMs = 0; WorstParts = ""; }

        double engineMs;
        int blastsRead;

        void ReadBlasts()
        {
            engineMs = 0;
            blastsRead = 0;
            for (int round = 0; round < 8; round++)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int n = backend.ReadBlasts(lastBlast, blasts);
                engineMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                blastsRead += n;
                for (int i = 0; i < n; i++)
                {
                    lastBlast = Mathf.Max(lastBlast, blasts[i].Id);
                    Add(blasts[i]);
                }
                if (n < blasts.Length) break;
            }
        }

        // A blast's mark, queued.
        public void Add(in BlastEvent b)
        {
            BlastsSeen++;
            var t = backend.Terrain;
            float g = t != null ? t.Sample(b.Position.x, b.Position.z) : b.Position.y;
            var defs = backend.UnitDefs;
            string caster = b.Cause == BlastCause.Weapon && defs != null && b.Def >= 0 && b.Def < defs.Count ? defs[b.Def].Name : null;
            if (ScarStamps.Make(b, g, out var s, caster)) Enqueue(s);
        }

        // A mark of a kind at a world point, for looks that leave their own:
        // a burning feature's char, a spell's frost, holy light.
        public void Mark(ScarKind kind, Vector3 at, float radius, Vector3 direction = default, float strength = 1f)
        {
            if (kind == ScarKind.None) return;
            // Nothing marks the bed of the sea.
            var t = backend.Terrain;
            if (t != null && t.SeaLevel >= 0f && t.Sample(at.x, at.z) < t.SeaLevel) return;
            Enqueue(ScarStamps.Make(kind, at, radius, direction, strength, ++marked));
        }

        // Past the cap a stamp folds into a waiting one of its kind that it
        // overlaps, or the oldest waiting stamp gives way.
        void Enqueue(in ScarStamp s)
        {
            if (pending.Count >= QueueCap(Level))
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = pending.Count - 1, seen = 0; i >= 0 && seen < 128; i--, seen++)
                {
                    var p = pending[i];
                    if (p.Kind != s.Kind) continue;
                    float dx = p.X - s.X, dz = p.Z - s.Z, d = dx * dx + dz * dz;
                    if (d < bestD && d <= p.Reach * p.Reach) { bestD = d; best = i; }
                }
                if (best >= 0)
                {
                    var p = pending[best];
                    ScarStamps.Merge(ref p, s);
                    pending[best] = p;
                    Merged++;
                    return;
                }
                pending.RemoveAt(0);
                Dropped++;
            }
            pending.Add(s);
            if (pending.Count > PeakPending) PeakPending = pending.Count;
        }

        void StampSome()
        {
            batch.Clear();
            int most = StampsPerFrame(Level), texels = 0, k = 0;
            while (k < pending.Count && batch.Count < most && texels < DentTexelsPerFrame)
            {
                var s = pending[k++];
                batch.Add(s);
                Stamping?.Invoke(s);
                texels += Dents.Stamp(s);
                AskDips(s);
            }
            if (k > 0) pending.RemoveRange(0, k);
            if (batch.Count > 0) DrawStamps(batch);
            StampedLastFrame = batch.Count;
            if (batch.Count > PeakStamped) PeakStamped = batch.Count;
            Stamped += batch.Count;
        }

        // ── The ground ────────────────────────────────────────────────

        float Offset(float x, float z)
        {
            if (quality.Ground != ScarGround.Dips) return 0f;
            float h = Dents.Height(x, z);
            if (h == 0f) return 0f;
            // As the terrain shader: a crater by the water fills to just above it.
            var t = backend.Terrain;
            if (t != null) h = Mathf.Max(h, Mathf.Min(0f, t.SeaLevel + 0.05f - t.Sample(x, z)));
            return h;
        }

        float Standing(Vector3 at)
        {
            var t = backend.Terrain;
            if (t == null) return 1f;
            float above = at.y - t.Sample(at.x, at.z);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.5f, above));
        }

        // How many times a region's samples the ground is split where it dips.
        public int Refinement
        {
            get
            {
                var t = backend.Terrain;
                return t == null ? 1 : Mathf.Clamp(Mathf.RoundToInt(t.CellSize / MeshSpacing(Level)), 1, 8);
            }
        }

        // The regions under a crater get more vertices, so it can dip.
        void AskDips(in ScarStamp s)
        {
            if (ground == null || s.Dent <= 0f || quality.Ground != ScarGround.Dips) return;
            float reach = s.Dent * 1.5f + 1f;
            AskDips(s.X - reach, s.Z - reach, s.X + reach, s.Z + reach);
        }

        void AskDips(float x0, float z0, float x1, float z1)
        {
            var t = backend.Terrain;
            if (t == null) return;
            float span = TerrainBuilder.RegionBlocks * t.BlockSize;
            int factor = Refinement;
            int rx0 = Mathf.FloorToInt(x0 / span), rx1 = Mathf.FloorToInt(x1 / span);
            int ry0 = Mathf.FloorToInt(-z1 / span), ry1 = Mathf.FloorToInt(-z0 / span);
            for (int ry = ry0; ry <= ry1; ry++)
                for (int rx = rx0; rx <= rx1; rx++)
                    ground.Refine(rx, ry, factor);
        }

        // A new setting: the textures resized with what they hold, and the
        // dips asked for again where the ground now dips.
        void Requalify(EffectsQuality level)
        {
            Level = level;
            quality = FxQuality.For(level);
            int w = TexW, h = TexH;
            Size(level);
            if (w != TexW || h != TexH)
            {
                Resize();
                Dents = new DentField(TexW, TexH, size.x, size.y);
                var px = Read(shape);
                if (px != null) Dents.Load(px);
            }
            if (quality.Ground != ScarGround.Dips || ground == null || backend.Terrain == null) return;
            var t = backend.Terrain;
            float span = TerrainBuilder.RegionBlocks * t.BlockSize;
            for (float z = 0f; z > -size.y; z -= span)
                for (float x = 0f; x < size.x; x += span)
                    if (Dents.AnyIn(x, z - span, x + span, z)) AskDips(x + 1f, z - span + 1f, x + span - 1f, z - 1f);
        }

        // The soil the climate's craters show, and how wet their floors lie,
        // then dug snow, and 1 where snow covers the map.
        static (Vector4 soil, Vector4 snow) Palette(string climate)
        {
            var slush = new Vector4(0.2f, 0.18f, 0.16f, 0f);
            switch (climate)
            {
                case "desert": return (new Vector4(0.42f, 0.28f, 0.14f, 0f), slush);
                case "snow": return (new Vector4(0.07f, 0.05f, 0.035f, 0f), new Vector4(0.2f, 0.18f, 0.16f, 1f));
                case "swamp": return (new Vector4(0.035f, 0.028f, 0.016f, 0.8f), slush);
                case "volcanic": return (new Vector4(0.06f, 0.055f, 0.05f, 0f), slush);
                default: return (new Vector4(0.15f, 0.09f, 0.045f, 0f), slush);
            }
        }
    }
}
