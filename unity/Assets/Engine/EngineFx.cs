// EngineFx.cs - the engine's shots and effects as the contract wants them,
// with what okx API 20 does not report filled in: each strip's frames,
// blend and pace, blasts played to their end, and breath told from lightning.
using System.Collections.Generic;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineFx
    {
        const float S = EngineSettings.PxToUnits;
        // The engine's own pace for every picture, ticks a frame.
        public const int EngineTicksPerFrame = 2;
        // Ticks a new beam waits for its first flames before it is called lightning.
        public const int BreathWait = 1;
        // Blasts kept playing after the engine lets them go, at most.
        public const int MaxKept = 768;

        sealed class StripInfo
        {
            public int W, H, N;
            public FxLook.ArtRule Rule;
            public bool Known;
            public EffectFrame[] Frames;
        }

        sealed class Blast
        {
            public int OutId, Strip, EngineId;
            public Vector3 At;
            public uint Born, Seen;
            public bool Moving, Kept;
            public OkxEffect Last;
        }

        sealed class Flight
        {
            public uint Born, Seen;
            public Vector3 At;
            public bool Breath;
        }

        readonly System.Func<int, Vector2Int> stripSize;
        readonly Dictionary<int, StripInfo> strips = new Dictionary<int, StripInfo>();
        readonly Dictionary<int, Blast> live = new Dictionary<int, Blast>();
        readonly List<Blast> kept = new List<Blast>();
        readonly Dictionary<int, Flight> flights = new Dictionary<int, Flight>();
        readonly Dictionary<int, int> pictureStrip = new Dictionary<int, int>();
        readonly List<int> drop = new List<int>();
        readonly List<Vector3> flameSpawns = new List<Vector3>();
        readonly List<EffectState> outEffects = new List<EffectState>();
        readonly List<ProjectileState> outShots = new List<ProjectileState>();
        int nextId = 1;
        uint tick, prevTick;
        int tps = 60;

        // stripSize gives a strip's width and height in pixels, 0 when unknown.
        public EngineFx(System.Func<int, Vector2Int> stripSize) => this.stripSize = stripSize;

        public void Reset()
        {
            strips.Clear(); live.Clear(); kept.Clear(); flights.Clear(); pictureStrip.Clear();
            outEffects.Clear(); outShots.Clear();
            nextId = 1;
        }

        // One tick's worth of the engine's records.
        public void Update(uint now, int ticksPerSecond, OkxEffect[] fx, int fxCount, OkxProjectile[] shots, int shotCount)
        {
            if (now < tick) Reset();
            prevTick = tick;
            tick = now;
            tps = Mathf.Max(1, ticksPerSecond);
            outEffects.Clear();
            outShots.Clear();
            pictureStrip.Clear();
            flameSpawns.Clear();

            for (int i = 0; i < fxCount; i++) Learn(fx[i]);
            for (int i = 0; i < fxCount; i++)
            {
                var e = fx[i];
                if (e.kind == OkEngine.EffectProjectile) { pictureStrip[e.id] = e.sprite; continue; }
                SeeBlast(e);
            }
            // Blasts the engine has let go keep playing, and moving ones end with it.
            drop.Clear();
            foreach (var kv in live)
            {
                var b = kv.Value;
                if (b.Seen == tick) continue;
                drop.Add(kv.Key);
                if (!b.Moving) Keep(b);
            }
            foreach (int k in drop) live.Remove(k);
            foreach (var b in live.Values) EmitBlast(b);
            for (int i = kept.Count - 1; i >= 0; i--) if (!EmitBlast(kept[i])) kept.RemoveAt(i);

            for (int i = 0; i < fxCount; i++)
                if (fx[i].kind == OkEngine.EffectProjectile) EmitPicture(fx[i]);

            drop.Clear();
            foreach (var kv in flights) if (kv.Value.Seen < prevTick) drop.Add(kv.Key);
            foreach (int k in drop) flights.Remove(k);
            for (int i = 0; i < shotCount; i++) EmitShot(shots[i]);
        }

        public int Effects(EffectState[] into)
        {
            int n = Mathf.Min(outEffects.Count, into?.Length ?? 0);
            for (int i = 0; i < n; i++) into[i] = outEffects[i];
            return outEffects.Count;
        }

        public int Projectiles(ProjectileState[] into)
        {
            int n = Mathf.Min(outShots.Count, into?.Length ?? 0);
            for (int i = 0; i < n; i++) into[i] = outShots[i];
            return outShots.Count;
        }

        public EffectFrame[] Frames(int strip) => strips.TryGetValue(strip, out var s) ? s.Frames : null;

        public int KeptCount => kept.Count;

        // ── Strips ────────────────────────────────────────────────────

        StripInfo Strip(int id)
        {
            if (strips.TryGetValue(id, out var s)) return s;
            var size = stripSize(id);
            strips[id] = s = new StripInfo { W = size.x, H = size.y, Rule = new FxLook.ArtRule { Duration = FxLook.DefaultDuration } };
            return s;
        }

        // Frame f sits at u0 = f / N, so any frame past the first tells the
        // count, as does the strip's size when it is retail art.
        void Learn(in OkxEffect e)
        {
            var s = Strip(e.sprite);
            if (s.N == 0)
            {
                if (e.frame > 0 && e.u0 > 1e-6f) s.N = Mathf.RoundToInt(e.frame / e.u0);
                else if (e.frame == 0 && e.u1 >= 0.999f) s.N = 1;
                else s.N = RetailCount(s.W, s.H);
                if (s.N > 0)
                {
                    s.Known = s.W > 0 && s.W % s.N == 0 && FxLook.KnownArt(s.W / s.N, s.H, s.N, out s.Rule);
                    if (!s.Known) s.Rule = new FxLook.ArtRule { Additive = false, Duration = FxLook.DefaultDuration };
                    s.Frames = new EffectFrame[s.N];
                    int ticks = FxLook.FrameTicks(s.Rule.Duration, tps);
                    for (int f = 0; f < s.N; f++) s.Frames[f] = new EffectFrame { Ticks = ticks, Additive = s.Rule.Additive };
                }
            }
            if (s.Frames != null && e.frame >= 0 && e.frame < s.N && s.Frames[e.frame].Width <= 0)
            {
                var f = s.Frames[e.frame];
                f.Top = (e.top - e.y) * S; f.Bottom = (e.bottom - e.y) * S;
                f.OffsetX = e.offX * S; f.Width = e.w * S;
                f.UvMin = new Vector2(e.u0, 0f); f.UvMax = new Vector2(e.u1, e.v1);
                s.Frames[e.frame] = f;
            }
        }

        // The one frame count that makes a strip of this size retail art, else 0.
        static int RetailCount(int w, int h)
        {
            int found = 0;
            for (int n = 2; n <= 64 && n <= w; n++)
                if (w % n == 0 && FxLook.KnownArt(w / n, h, n, out _)) { if (found != 0) return 0; found = n; }
            return found;
        }

        int TicksPerFrame(StripInfo s) => FxLook.FrameTicks(s.Rule.Duration, tps);

        // ── Blasts ────────────────────────────────────────────────────

        void SeeBlast(in OkxEffect e)
        {
            var at = EngineSettings.ToUnity(e.x, e.y, e.z);
            if (live.TryGetValue(e.id, out var b))
            {
                // A slot taken by a new effect: another picture, a one-shot
                // starting over, or a mover that jumped further than any flies.
                bool jumped = (b.At - at).magnitude > 1.2f * Mathf.Max(1, (int)(tick - b.Seen));
                bool other = b.Strip != e.sprite || (e.frame < b.Last.frame && (!b.Moving || jumped)) || (jumped && e.frame <= 1);
                if (other)
                {
                    if (!b.Moving) Keep(b);
                    b = null;
                }
                else if ((b.At - at).sqrMagnitude > 1e-4f) b.Moving = true;
            }
            if (b == null)
            {
                b = new Blast { OutId = nextId++, Strip = e.sprite, EngineId = e.id, Born = tick - (uint)(Mathf.Max(0, e.frame) * EngineTicksPerFrame) };
                if (nextId == int.MaxValue) nextId = 1;
                live[e.id] = b;
            }
            b.At = at;
            b.Seen = tick;
            b.Last = e;
            // A flame particle leaves the muzzle, which is how a breath is known.
            if (tick - b.Born <= EngineTicksPerFrame) flameSpawns.Add(at);
        }

        void Keep(Blast b)
        {
            if (b.Kept) return;
            b.Kept = true;
            if (kept.Count >= MaxKept) kept.RemoveAt(0);
            kept.Add(b);
        }

        // The blast at its own pace. False once it has played out.
        bool EmitBlast(Blast b)
        {
            var s = Strip(b.Strip);
            int frame, age = (int)(tick - b.Born);
            float phase = 0f;
            bool loops = b.Moving;
            if (s.N > 0)
            {
                int tpf = TicksPerFrame(s);
                frame = age / tpf;
                phase = (age % tpf) / (float)tpf;
                if (frame >= s.N)
                {
                    // Played out at the original's pace, whatever the engine still shows.
                    if (!loops) return false;
                    frame %= s.N;
                }
            }
            else
            {
                if (b.Kept) return false;
                frame = b.Last.frame;
            }
            var q = QuadFor(s, ref frame, b.Last, out bool fromTable);
            if (!fromTable && b.Kept) return true;
            outEffects.Add(new EffectState
            {
                Id = b.OutId, Strip = b.Strip, IsProjectile = false, Position = b.At,
                Top = q.Top, Bottom = q.Bottom, OffsetX = q.OffsetX, Width = q.Width, UvMin = q.UvMin, UvMax = q.UvMax,
                Frame = fromTable ? frame : b.Last.frame, Phase = fromTable ? phase : 0f, Loops = loops,
                Additive = s.Rule.Additive, Light = FxLight.Auto, Follow = -1, Struck = -1, Age = age,
            });
            return true;
        }

        // A frame's quad from what has been learned, the nearest earlier
        // frame when that one never showed, else the engine's latest.
        static EffectFrame QuadFor(StripInfo s, ref int frame, in OkxEffect last, out bool fromTable)
        {
            fromTable = false;
            if (s.Frames != null && frame >= 0 && frame < s.Frames.Length)
            {
                int f = frame;
                while (f > 0 && s.Frames[f].Width <= 0) f--;
                if (s.Frames[f].Width > 0) { frame = f; fromTable = true; return s.Frames[f]; }
            }
            return new EffectFrame
            {
                Top = (last.top - last.y) * S, Bottom = (last.bottom - last.y) * S, OffsetX = last.offX * S, Width = last.w * S,
                UvMin = new Vector2(last.u0, 0f), UvMax = new Vector2(last.u1, last.v1),
            };
        }

        // ── Shots ─────────────────────────────────────────────────────

        Flight FlightOf(int slot, Vector3 at)
        {
            if (!flights.TryGetValue(slot, out var f) || (f.At - at).sqrMagnitude > 64f)
                flights[slot] = f = new Flight { Born = tick };
            f.At = at;
            f.Seen = tick;
            return f;
        }

        void EmitPicture(in OkxEffect e)
        {
            var at = EngineSettings.ToUnity(e.x, e.y, e.z);
            var f = FlightOf(e.id, at);
            var s = Strip(e.sprite);
            int age = (int)(tick - f.Born), frame = e.frame;
            float phase = 0f;
            bool fromTable = false;
            if (s.N > 0)
            {
                int tpf = TicksPerFrame(s);
                frame = (age / tpf) % s.N;
                phase = (age % tpf) / (float)tpf;
            }
            var q = QuadFor(s, ref frame, e, out fromTable);
            if (!fromTable) { frame = e.frame; phase = 0f; }
            outEffects.Add(new EffectState
            {
                Id = -1 - e.id, Strip = e.sprite, IsProjectile = true, Position = at,
                Top = q.Top, Bottom = q.Bottom, OffsetX = q.OffsetX, Width = q.Width, UvMin = q.UvMin, UvMax = q.UvMax,
                Frame = frame, Phase = phase, Loops = true, Additive = s.Rule.Additive, Light = FxLight.Auto,
                Follow = -1, Struck = -1, Age = age,
            });
        }

        void EmitShot(in OkxProjectile p)
        {
            var at = EngineSettings.ToUnity(p.x, p.y, p.z);
            var shot = new ProjectileState
            {
                Id = p.id, Player = p.player, Colour = p.color, Kind = p.kind, Model = p.model,
                Position = at,
                // Pixels a tick to units a second.
                Velocity = new Vector3(p.vx, p.vy, -p.vz) * (S * tps),
                Heading = p.heading * Mathf.Rad2Deg, Pitch = p.pitch * Mathf.Rad2Deg, Roll = p.roll * Mathf.Rad2Deg,
                Light = FxLight.Auto,
            };
            if (p.kind == OkEngine.ProjBeam)
            {
                // The engine gives the ground under the shooter and under the
                // aim, and the 3D view raises them to the body.
                shot.Source = EngineSettings.ToUnity(p.fromX, p.fromY + 12f, p.fromZ);
                shot.Position = at + Vector3.up * (8f * S);
                var f = FlightOf(p.id, shot.Source);
                shot.Age = (int)(tick - f.Born);
                shot.Seed = p.id * 7919 + (int)f.Born;
                // A breath stays one, and a new beam waits a tick for its first flames.
                if (Breath(shot.Source)) f.Breath = true;
                if (!f.Breath && shot.Age < BreathWait) return;
                shot.Beam = f.Breath ? BeamKind.Fire : BeamKind.Lightning;
            }
            else
            {
                var f = FlightOf(p.id, at);
                shot.Age = (int)(tick - f.Born);
                shot.Seed = p.id * 7919 + (int)f.Born;
                // Cannon balls and other alpha pictures throw the original's shadow.
                if (p.kind == OkEngine.ProjSprite && pictureStrip.TryGetValue(p.id, out int strip))
                {
                    var s = Strip(strip);
                    shot.Shadow = s.N > 0 && !s.Rule.Additive;
                }
            }
            outShots.Add(shot);
        }

        bool Breath(Vector3 source)
        {
            foreach (var at in flameSpawns)
            {
                var d = at - source;
                if (d.x * d.x + d.z * d.z < 0.64f * 0.64f && Mathf.Abs(d.y) < 1.2f) return true;
            }
            return false;
        }
    }
}
