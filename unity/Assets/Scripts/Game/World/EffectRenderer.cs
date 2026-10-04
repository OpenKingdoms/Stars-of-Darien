// EffectRenderer.cs - shots in flight, blasts and spells, drawn first as the
// original draws them (its frames, blend and pace), then remastered: eased
// frames, glow into the bloom, soft edges, trails, lights and scorch, and
// round each blast its parts in 3D (FxBlast): flash and light, rings along
// the ground, debris, smoke on the wind, sparks, embers and a shake.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class EffectRenderer
    {
        public const int MaxEffects = 2048;

        readonly IGameBackend backend;
        readonly ModelCache models;
        readonly EffectState[] effects = new EffectState[MaxEffects];
        readonly ProjectileState[] shots = new ProjectileState[EntityRenderer.MaxProjectiles];
        readonly PiecePose[] poses = new PiecePose[EntityRenderer.MaxPieces];
        readonly Dictionary<int, Strip> strips = new Dictionary<int, Strip>();
        readonly Dictionary<int, FxMesh> batches = new Dictionary<int, FxMesh>();
        readonly FxMesh glow = new FxMesh(), smoke = new FxMesh(), ground = new FxMesh(), groundGlow = new FxMesh();
        readonly InstancedDraws shotModels = new InstancedDraws();
        readonly FxLights lights = new FxLights();
        readonly FxTrails trails = new FxTrails();
        readonly FxDecals decals = new FxDecals();
        readonly FxParticles particles;
        readonly FxShock shock = new FxShock();
        readonly FxBlasts blasts;
        readonly FxMesh rings = new FxMesh();
        readonly FxStuck stuckShots = new FxStuck();
        int frameNo;
        float clock = -1f;
        int clockFrame = -1;
        readonly System.Diagnostics.Stopwatch blastWatch = new System.Diagnostics.Stopwatch();
        readonly Dictionary<int, (Strip strip, float height)> pictureOf = new Dictionary<int, (Strip, float)>();
        readonly Dictionary<int, bool> shotShown = new Dictionary<int, bool>();
        readonly HashSet<long> scorched = new HashSet<long>(), liveImpacts = new HashSet<long>();
        readonly List<int> evict = new List<int>();
        readonly List<Object> owned = new List<Object>();
        readonly Vector3[] corners = new Vector3[4];
        readonly Vector3[] beam = new Vector3[FxLook.BeamSegments + 1];
        Material glowMat, smokeMat, markMat;
        readonly System.Func<float, float, float> groundAt;
        GameCamera rig;
        Camera rigOf;
        float nextSweep;

        // The remaster's parts, each of which can be turned off to see the
        // original's look alone.
        public bool Smooth = true;          // frames eased and sampled smoothly
        public bool Glow = true;            // added art past white, for the bloom
        public bool Soft = true;            // soft where it meets the ground
        public bool Lights = true;
        public bool Trails = true;
        public bool Scorch = true;
        public bool Blasts = true;          // each blast's parts round its picture

        // True for what the local player cannot see: an impact at a point
        // (player -1), or a shot of a player's at a point. Null shows all.
        public System.Func<Vector3, int, bool> Hidden;

        // What the last frame drew, for tests and the profiler.
        public int Count { get; private set; }
        public int Shots { get; private set; }
        public int Quads { get; private set; }
        // Quads of every kind, ribbons counted by their vertex pairs, and model shot pieces.
        public int Drawn { get; private set; }
        public int Beams { get; private set; }
        // Effects drawn, the strip quads they took (two while a frame eases
        // into the next), and the glows drawn at beams' ends.
        public int Pictures { get; private set; }
        public int Sprites { get; private set; }
        public int BeamEnds { get; private set; }
        public int ModelShots { get; private set; }
        public int LightsLit => lights.Lit;
        public int LightsAsked => lights.Asked;
        // This frame's strongest and widest light, and the palest one's saturation.
        public float LightBrightest => lights.Brightest;
        public float LightWidest => lights.Widest;
        public float LightPalest => lights.Palest;
        public int LightToggles => lights.Toggles;
        public int Marks => decals.Count;
        public int TrailCount => trails.Count;
        public float LongestTrail => trails.Longest;
        public int ArtPending { get; private set; }
        public int StripsHeld => strips.Count;

        // The blasts' parts: blasts given a look, their lights asked this
        // frame, rings, debris, particles and smoke alive, and where the smoke is.
        public int BlastsPlayed => blasts.Played;
        public int FlashLights => blasts.LightsAsked;
        public int RingCount => shock.Count;
        public int DebrisCount => particles.ChunkCount;
        public int ParticleCount => particles.Count;
        public int SmokeCount => particles.SmokeCount;
        public Vector3 SmokeCentre => particles.SmokeCentre;
        public FxParticles Particles => particles;
        // Arrows, bolts and spears standing where they landed.
        public int StuckShots => stuckShots.Count;
        // The main thread's time for the blasts' parts in the last frame.
        public float BlastMs { get; private set; }
        // Steps the parts by the simulation's ticks alone, for tests and captures.
        public bool FixedClock;

        // Plays a kind of blast at a point, as if the backend had reported it.
        public void Play(BlastKind kind, Vector3 at, float radius, Vector3 direction) => blasts.Play(kind, at, radius, direction);

        // Burning scenery and the marks magic leaves on it, stepped with the
        // blasts and lit from the same pool, and whether they show.
        public FxFire Fire;
        public FxMagic Magic;
        public bool SceneryLooks = true;
        // The battle's clock the parts run on, in seconds.
        public float Clock => clock;
        // The wind the smoke drifts on, world units a second along the ground.
        public Vector3 Wind => particles.Wind;

        // Lets every part go at once.
        public void Clear()
        {
            particles.Clear();
            shock.Clear();
            blasts.Clear();
            stuckShots.Clear();
            Fire?.Clear();
            Magic?.Clear();
        }

        sealed class Strip
        {
            public FxArt Art;
            public Material Material;
            public EffectFrame[] Frames;
            public int FramesBuilt = -1;
            public bool HasAlpha;
            public float Used, RetryAt;
        }

        // The camera's place and axes, read once a frame.
        public readonly struct Eye
        {
            public readonly Vector3 Position, Right, Up;
            public Eye(Vector3 position, Vector3 right, Vector3 up) { Position = position; Right = right; Up = up; }
            public static Eye Of(Transform t) => new Eye(t.position, t.right, t.up);
        }

        public EffectRenderer(IGameBackend backend, ModelCache models = null)
        {
            this.backend = backend;
            this.models = models;
            groundAt = backend.GroundHeight;
            shotModels.CastShadows = true;
            particles = new FxParticles(backend.GroundHeight);
            particles.MakeActive();
            blasts = new FxBlasts(backend, particles, shock);
        }

        // How far an effect is drawn toward the camera from its point, shrunk
        // to look the same, so the ground beside it never cuts it.
        public const float Nudge = 1f;

        // An effect's picture as four corners, bottom left first and
        // anticlockwise seen from the camera: in the camera's plane and
        // standing on its point, so it shows its full height at any tilt.
        public static void Corners(in EffectState e, Transform cam, Vector3[] into) =>
            Corners(e.Position, e.Top, e.Bottom, e.OffsetX, e.Width, Eye.Of(cam), into);

        public static void Corners(Vector3 at, float top, float bottom, float offsetX, float width, in Eye eye, Vector3[] into)
        {
            Nudged(at, eye.Position, Nudge, out var pivot, out float k);
            var right = eye.Right * k;
            var up = eye.Up * k;
            var left = pivot - right * offsetX;
            var r = right * width;
            var lo = up * bottom;
            var hi = up * top;
            into[0] = left + lo; into[1] = left + r + lo; into[2] = left + r + hi; into[3] = left + hi;
        }

        // A point moved toward the camera by up to nudge, and the scale that
        // keeps a picture drawn there the same size on screen.
        public static void Nudged(Vector3 at, Transform cam, float nudge, out Vector3 pivot, out float scale) =>
            Nudged(at, cam.position, nudge, out pivot, out scale);

        public static void Nudged(Vector3 at, Vector3 eye, float nudge, out Vector3 pivot, out float scale)
        {
            var toCam = eye - at;
            float dist = toCam.magnitude;
            pivot = at;
            scale = 1f;
            if (dist < 1e-4f) return;
            float d = Mathf.Min(nudge, dist * 0.5f);
            pivot = at + toCam * (d / dist);
            scale = (dist - d) / dist;
        }

        public void Render(Camera cam)
        {
            if (cam == null) return;
            frameNo++;
            var eye = Eye.Of(cam.transform);
            float now = backend.Tick / (float)Mathf.Max(1, backend.TicksPerSecond);
            EnsureMaterials();
            foreach (var b in batches.Values) b.Clear();
            glow.Clear(); smoke.Clear(); ground.Clear(); groundGlow.Clear(); rings.Clear();
            shotModels.Clear();
            lights.Begin();
            pictureOf.Clear();
            liveImpacts.Clear();
            shotShown.Clear();
            Pictures = 0;
            BeamEnds = 0;
            PollArt();

            Count = Mathf.Min(backend.ReadEffects(effects), effects.Length);
            Shots = Mathf.Min(backend.ReadProjectiles(shots), shots.Length);
            // A side's own shots always show, the rest where the player sees.
            for (int i = 0; i < Shots; i++) shotShown[shots[i].Id] = Shows(shots[i].Position, shots[i].Player);
            for (int i = 0; i < Count; i++)
            {
                var e = effects[i];
                bool shows = e.IsProjectile && shotShown.TryGetValue(-1 - e.Id, out bool s) ? s : Shows(e.Position, -1);
                if (shows) AddEffect(e, eye, now);
            }
            scorched.IntersectWith(liveImpacts);

            if (Count + Shots > 0) lastShowing = Time.unscaledTime;
            PrepareCamera(cam, Time.unscaledTime - lastShowing < DepthLinger);
            Beams = 0;
            ModelShots = 0;
            for (int i = 0; i < Shots; i++) if (shotShown[shots[i].Id]) AddShot(shots[i], eye, now);

            if (Trails) trails.Draw(glow, smoke, eye.Position, now);
            if (Scorch) decals.Draw(ground, groundGlow, now);
            StepBlasts(cam);

            Quads = 0;
            Sprites = 0;
            int vertices = 0;
            foreach (var kv in batches)
            {
                if (kv.Value.Vertices == 0) continue;
                var s = strips[kv.Key];
                if (s.HasAlpha) kv.Value.SortQuads(eye.Position);
                kv.Value.Draw(s.Material);
                Quads += kv.Value.Quads;
                Sprites += kv.Value.Quads;
                vertices += kv.Value.Vertices;
            }
            ground.Draw(markMat);
            groundGlow.Draw(glowMat);
            rings.Draw(glowMat);
            smoke.Draw(smokeMat);
            glow.Draw(glowMat);
            shotModels.Draw();
            Quads += glow.Quads + smoke.Quads;
            vertices += ground.Vertices + groundGlow.Vertices + smoke.Vertices + glow.Vertices + rings.Vertices;
            Drawn = vertices / 4 + shotModels.Count + particles.Drawn + particles.ChunkCount;
            if (!Lights) lights.Begin();
            lights.Commit(Focus(cam), 40f, Time.unscaledDeltaTime);
            Sweep();
        }

        bool Shows(Vector3 at, int player) => Hidden == null || !Hidden(at, player);

        // ── Blasts ────────────────────────────────────────────────────

        // Reads the frame's blasts, steps every part on the game's clock and
        // draws the particles. Paused, everything holds still.
        void StepBlasts(Camera cam)
        {
            blastWatch.Restart();
            float dt = Step();
            var focus = Focus(cam);
            particles.Wind = WindNow();
            if (Blasts)
            {
                blasts.Hidden = Hidden;
                blasts.Update(dt, focus, lights);
            }
            if (SceneryLooks)
            {
                if (Fire != null) { Fire.Hidden = Hidden; Fire.Update(clock, dt, lights); }
                if (Magic != null) { Magic.Hidden = Hidden; Magic.Update(clock, dt); }
            }
            stuckShots.Settle(frameNo, blasts.ArrowLandings, Blasts ? FxQuality.Current.Debris / 8 : 0);
            stuckShots.Draw(dt, models, shotModels, Hidden);
            particles.Step(dt, cam);
            shock.Step(dt);
            shock.Draw(rings);
            particles.Draw(cam);
            if (rig != null) rig.shake = Blasts ? blasts.Shake : Vector3.zero;
            if (particles.Count + particles.ChunkCount + shock.Count > 0) lastShowing = Time.unscaledTime;
            BlastMs = (float)blastWatch.Elapsed.TotalMilliseconds;
        }

        // Seconds of the game since the last frame. The parts run smoothly
        // between the simulation's ticks but never ahead of it by more than one.
        float Step()
        {
            int tps = Mathf.Max(1, backend.TicksPerSecond);
            float sim = backend.Tick / (float)tps;
            if (clock < 0f || sim < clock - 1f) { clock = sim; return 0f; }
            // Drawn twice in one frame, the parts move once.
            if (!FixedClock && Time.frameCount == clockFrame) return 0f;
            clockFrame = Time.frameCount;
            float before = clock;
            clock = FixedClock ? sim : Mathf.Clamp(clock + Time.unscaledDeltaTime, sim - 0.1f, sim + 1f / tps);
            return Mathf.Max(0f, clock - before);
        }

        // A light breeze when the simulation has no wind of its own.
        public static readonly Vector3 Breeze = new Vector3(0.75f, 0f, 0.45f);

        // The simulation's wind as world units a second along the ground.
        Vector3 WindNow() => backend.ReadWind(out var w) ? w.Toward * (0.4f + 2.6f * w.Strength) : Breeze;

        // Where the player looks: the camera rig's focus, else ahead of the lens.
        Vector3 Focus(Camera cam)
        {
            if (rigOf != cam) { rigOf = cam; rig = cam.GetComponent<GameCamera>(); }
            return rig != null ? rig.focus : cam.transform.position + cam.transform.forward * 30f;
        }

        // ── Pictures ──────────────────────────────────────────────────

        void AddEffect(in EffectState e, in Eye eye, float now)
        {
            if (e.Width <= 0 || e.Top <= e.Bottom) return;
            var s = StripFor(e.Strip);
            if (s == null || s.Art == null || !s.Art.Ready) return;
            var batch = BatchFor(e.Strip);
            var frames = s.Frames;
            Pictures++;
            float phase = Smooth ? Mathf.Clamp01(e.Phase) : 0f;
            int next = -2;
            // Movers hold each frame as the original does, so a breath of
            // flame flickers and shows its gaps. Blasts and shots ease.
            bool hold = e.Loops && !e.IsProjectile;
            if (Smooth && !hold && frames != null && frames.Length > 1)
            {
                next = e.Frame + 1;
                if (next >= frames.Length) next = e.Loops ? 0 : -1;
                if (next >= 0 && frames[next].Width <= 0) next = -2;
            }
            else if (Smooth && !hold && frames != null && frames.Length == 1 && !e.Loops) next = -1;
            // Ease into the next frame, or fade the last one of a blast out.
            // Added light sums exactly, so it eases the whole frame. Alpha art
            // holds each frame and blends only at the end, so it never thins.
            float blend = e.Additive ? phase : Ease((phase - 0.7f) / 0.3f);
            float wNow = next == -2 ? 1f : 1f - blend;
            Sprite(batch, s, e.Position, e.Top, e.Bottom, e.OffsetX, e.Width, s.Art.Rect(e.Frame, e.UvMin, e.UvMax), e.Additive, wNow, eye);
            if (next >= 0 && blend > 0.004f)
            {
                var f = frames[next];
                Sprite(batch, s, e.Position, f.Top, f.Bottom, f.OffsetX, f.Width, s.Art.Rect(next, f.UvMin, f.UvMax), f.Additive, blend, eye);
            }

            float tall = e.Top - e.Bottom;
            if (e.IsProjectile) pictureOf[-1 - e.Id] = (s, tall);
            var size = FxLook.LightOf(e.Light);
            if (size != FxLight.None)
            {
                float life = 1f;
                if (!e.Loops && frames != null && frames.Length > 0) life = FxLook.LightEnvelope((e.Frame + phase) / frames.Length);
                float flicker = 0.92f + 0.08f * Mathf.Sin(now * 23f + e.Id * 1.7f);
                lights.Ask(e.Position + Vector3.up * Mathf.Max(1.2f, e.Bottom + tall * 0.45f), FxLook.LightTint(s.Art.Glow), size, life, e.Id, flicker);
            }

            if (!e.IsProjectile && !e.Loops)
            {
                long key = ((long)e.Id << 32) ^ ((long)e.Strip << 20) ^ (long)(Mathf.RoundToInt(e.Position.x * 4f) * 7919 + Mathf.RoundToInt(e.Position.z * 4f));
                liveImpacts.Add(key);
                // A blast grows from a small first frame, so it is asked each
                // frame until it is tall enough to scorch.
                if (Scorch && !scorched.Contains(key) && ScorchFor(e, s, tall))
                {
                    scorched.Add(key);
                    decals.Scorch(e.Position, Mathf.Clamp(e.Width * 0.75f, 1.5f, 7f), now, e.Id, groundAt);
                }
            }
        }

        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // A blast of fire on open ground leaves a scorch.
        bool ScorchFor(in EffectState e, Strip s, float tall)
        {
            if (!e.Additive || tall < FxLook.ScorchHeight) return false;
            var g = s.Art.Glow;
            if (g.r < 0.9f || g.b > 0.55f) return false;
            float under = backend.GroundHeight(e.Position.x, e.Position.z);
            var t = backend.Terrain;
            if (t != null && t.SeaLevel > 0 && under < t.SeaLevel) return false;
            return Mathf.Abs(e.Position.y - under) < 1.5f;
        }

        void Sprite(FxMesh batch, Strip s, Vector3 at, float top, float bottom, float offsetX, float width, Vector4 rect, bool additive, float weight, in Eye eye)
        {
            if (weight <= 0.004f || width <= 0f || top <= bottom) return;
            Corners(at, top, bottom, offsetX, width, eye, corners);
            var c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(weight) * 255f));
            var look = new Vector4(additive ? 1f : 0f, 1f, 1f, Smooth ? 1f : 0f);
            batch.Quad(corners[0], corners[1], corners[2], corners[3], rect, c, look);
        }

        // ── Shots ─────────────────────────────────────────────────────

        void AddShot(in ProjectileState p, in Eye eye, float now)
        {
            switch (p.Kind)
            {
                case ShotKind.Beam: AddBeam(p, eye); return;
                case ShotKind.Model: AddModelShot(p); break;
                case ShotKind.Dot: AddDot(p, eye); break;
            }
            if (Trails && TrailStyle(p, out var style)) trails.Track(p.Id, p.Position, style, now);
            if (p.Shadow)
            {
                float size = p.Kind == ShotKind.Picture && pictureOf.TryGetValue(p.Id, out var pic) ? Mathf.Clamp(pic.height * 0.8f, 0.35f, 3f) : 0.5f;
                decals.Shadow(ground, p.Position, size, 0.45f, groundAt);
            }
        }

        // The original draws no trails. A remastered one is faint, the art's
        // own colour, and no longer than the shot is tall. Orbs of water, ice
        // and lightning go without.
        bool TrailStyle(in ProjectileState p, out FxTrails.Style style)
        {
            style = default;
            float speed = p.Velocity.magnitude;
            if (p.Kind == ShotKind.Picture)
            {
                if (!pictureOf.TryGetValue(p.Id, out var pic)) return false;
                if (!AdditiveArt(pic.strip))
                {
                    style = new FxTrails.Style { Colour = new Color32(150, 144, 134, 40), Width = Mathf.Clamp(pic.height * 0.4f, 0.12f, 0.5f), Seconds = 0.25f, Glow = 1f, Length = 2.5f };
                    return true;
                }
                var mean = pic.strip.Art.Mean;
                if (pic.strip.Art.Brightness < 0.12f || !FxLook.Trails(mean)) return false;
                mean.a = FxLook.TrailAlpha;
                style = new FxTrails.Style
                {
                    Colour = mean, Width = Mathf.Clamp(pic.height * 0.25f, 0.1f, 0.45f),
                    Seconds = Mathf.Clamp(pic.height / Mathf.Max(0.01f, speed), 0.03f, 0.2f), Glow = 0.8f, Additive = true, Length = pic.height,
                };
                return true;
            }
            if (p.Kind == ShotKind.Dot)
            {
                style = new FxTrails.Style { Colour = new Color32(255, 196, 90, 110), Width = 0.2f, Seconds = 0.15f, Glow = 1f, Additive = true, Length = 1.5f };
                return true;
            }
            style = new FxTrails.Style { Colour = new Color32(236, 232, 220, 50), Width = 0.06f, Seconds = 0.1f, Glow = 1f };
            return true;
        }

        static bool AdditiveArt(Strip s)
        {
            if (s.Frames == null || s.Frames.Length == 0) return false;
            return s.Frames[0].Additive;
        }

        void AddModelShot(in ProjectileState p)
        {
            if (models == null) return;
            var m = models.Get(p.Model);
            if (m == null) return;
            int n = Mathf.Min(Mathf.Min(backend.ReadProjectilePose(p.Id, poses), m.Pieces.Length), poses.Length);
            for (int i = 0; i < n; i++)
            {
                var mesh = m.Pieces[i];
                if (mesh == null || poses[i].Hidden) continue;
                var mats = m.Materials[i];
                var world = poses[i].Matrix * m.Unscale;
                for (int k = 0; k < mats.Length; k++) if (mats[k] != null) shotModels.Add(mesh, k, mats[k], world);
            }
            if (n > 0) ModelShots++;
            if (Blasts && n > 0) stuckShots.Track(p.Id, p.Model, p.Position, p.Velocity, frameNo, poses, n, m.Unscale);
        }

        // A shot with no art: the classic client's small bright disc, here
        // a glowing ball with a hot core.
        void AddDot(in ProjectileState p, in Eye eye)
        {
            Glowing(p.Position, 0.55f, new Color32(255, 200, 96, 200), 0.8f, eye);
            Glowing(p.Position, 0.22f, new Color32(255, 236, 170, 255), 1.2f, eye);
            lights.Ask(p.Position, new Color(1f, 0.75f, 0.4f), FxLook.LightOf(p.Light), 0.45f, ShotKey(p.Id));
        }

        static int ShotKey(int id) => -1000000 - id;

        void Glowing(Vector3 at, float size, Color32 c, float bright, in Eye eye)
        {
            Nudged(at, eye.Position, 0.3f, out var pivot, out float k);
            var r = eye.Right * (size * 0.5f * k);
            var u = eye.Up * (size * 0.5f * k);
            var look = new Vector4(1f, Glow ? bright : Mathf.Min(1f, bright), 0.5f, 0f);
            glow.Quad(pivot - r - u, pivot + r - u, pivot + r + u, pivot - r + u, FxMesh.WholeRect, c, look);
        }

        // Rays shorter than this draw no glow at their ends, and shorter
        // than the least nothing at all, so a ray cut off at its source
        // never shows as a ball of light.
        public const float BeamGlowsFrom = 1f, BeamLeast = 0.5f;

        // A beam: a jagged ray from the firing piece to where it stopped, in
        // the weapon's three colours from a wide soft outer glow to a white
        // hot core, jagged afresh every 30 Hz frame as the original draws.
        void AddBeam(in ProjectileState p, in Eye eye)
        {
            var from = p.Source;
            var to = p.Position;
            if (p.Beam == BeamKind.Fire)
            {
                // The flame particles are the stream. The breath lights its way
                // when its weapon has a lightmap.
                lights.Ask(from + (to - from) * 0.3f, new Color(1f, 0.55f, 0.2f), FxLook.LightOf(p.Light), 0.8f, ShotKey(p.Id));
                return;
            }
            var dir = to - from;
            float length = dir.magnitude;
            if (length < BeamLeast) return;
            Beams++;
            Color32 inner = p.BeamInner, middle = p.BeamMiddle, outer = p.BeamOuter;
            if (inner.a == 0 && middle.a == 0 && outer.a == 0) FxLook.BeamColours(p.Beam, out inner, out middle, out outer);
            float fade = 1f;
            if (p.Life > 0)
            {
                float t = p.Age / (float)p.Life;
                fade = Mathf.Clamp01(p.Age / 2f) * Mathf.Clamp01((1f - t) / 0.2f);
            }
            if (fade <= 0.01f) return;
            BeamPath(from, to, p.Beam, p.Seed, FxLook.JagStep(backend.Tick, backend.TicksPerSecond), eye.Position, beam);
            bool straight = FxLook.Straight(p.Beam);
            bool ends = length >= BeamGlowsFrom;
            Ribbon(beam, straight ? 0.8f : 0.42f, outer, 0.5f * fade, 0.6f, eye, ends);
            Ribbon(beam, straight ? 0.3f : 0.16f, middle, 0.85f * fade, 0.9f, eye, ends);
            Ribbon(beam, straight ? 0.12f : 0.06f, inner, fade, 1.2f, eye, ends);
            if (ends)
            {
                BeamEnds++;
                Glowing(to, 1.1f, Fade(middle, 0.8f * fade), 1f, eye);
                Glowing(from, 0.5f, Fade(inner, 0.6f * fade), 1f, eye);
            }
            var size = FxLook.LightOf(p.Light);
            if (size != FxLight.None) lights.Ask(to + Vector3.up * 0.4f, FxLook.LightTint(middle), size, fade, ShotKey(p.Id));
        }

        // A beam's points from its source to its end, each thrown aside
        // across the view by the jag for this seed and step, the ends held.
        public static void BeamPath(Vector3 from, Vector3 to, BeamKind kind, int seed, int step, Vector3 eye, Vector3[] into)
        {
            var dir = to - from;
            float jag = FxLook.Straight(kind) ? 0f : FxLook.BeamJag;
            var side = Vector3.Cross(dir, eye - (from + to) * 0.5f);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.right;
            side.Normalize();
            int n = into.Length - 1;
            for (int s = 0; s <= n; s++)
                into[s] = from + dir * (s / (float)n) + side * (FxLook.Jag(seed, step, s) * jag);
        }

        static Color32 Fade(Color32 c, float a) => new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));

        void Ribbon(Vector3[] points, float width, Color32 colour, float alpha, float bright, in Eye eye, bool caps)
        {
            var c = Fade(colour, alpha);
            var look = new Vector4(1f, Glow ? bright : Mathf.Min(1f, bright), 0.4f, 0f);
            int prevA = -1, prevB = -1;
            int n = points.Length;
            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                var d = (i < n - 1 ? points[i + 1] : p) - (i > 0 ? points[i - 1] : p);
                var side = Vector3.Cross(d, eye.Position - p);
                if (side.sqrMagnitude < 1e-8f) side = eye.Right;
                side = side.normalized * (width * 0.5f);
                int a = glow.Vertex(p - side, c, new Vector2(0.5f, 0f), look);
                int b = glow.Vertex(p + side, c, new Vector2(0.5f, 1f), look);
                if (prevA >= 0) { glow.Triangle(prevA, b, prevB); glow.Triangle(prevA, a, b); }
                prevA = a; prevB = b;
            }
            if (!caps) return;
            // Round caps, so the joins at the ends do not show square.
            Glowing(points[0], width, c, bright, eye);
            Glowing(points[n - 1], width, c, bright, eye);
        }

        // ── Resources ─────────────────────────────────────────────────

        // Seconds a strip that failed to load waits before it is asked again,
        // and one unseen this long is let go.
        public const float RetrySeconds = 2f, KeepSeconds = 120f;
        // Textures made a frame for strips only being warmed.
        const int WarmUploads = 3;

        Strip StripFor(int id)
        {
            float clock = Time.unscaledTime;
            if (!strips.TryGetValue(id, out var s)) strips[id] = s = new Strip();
            s.Used = clock;
            var frames = backend.EffectFrames(id);
            s.Frames = frames;
            int n = frames != null ? frames.Length : 0;
            if (frames != null && s.FramesBuilt != n)
            {
                s.HasAlpha = false;
                foreach (var f in frames) s.HasAlpha |= !f.Additive;
            }
            if (s.Art != null && (s.FramesBuilt == n || n <= 1 && s.FramesBuilt >= 0))
            {
                if (s.Art.Pending && s.Art.Poll()) Apply(s);
                return s;
            }
            if (clock < s.RetryAt) return s;
            // Built again once the frame count is known, so each frame gets its own cell.
            var img = backend.EffectStrip(id);
            if (img == null) { s.RetryAt = clock + RetrySeconds; return s; }
            s.Art?.Dispose();
            s.Art = FxArt.Build(img, n);
            s.FramesBuilt = n;
            return s;
        }

        void Apply(Strip s)
        {
            if (s.Material == null) s.Material = NewMaterial(s.Art.Texture);
            else s.Material.mainTexture = s.Art.Texture;
        }

        // Textures for finished strips, a few a frame for those only warmed.
        void PollArt()
        {
            ArtPending = 0;
            int uploads = 0;
            foreach (var s in strips.Values)
            {
                if (s.Art == null || !s.Art.Pending) continue;
                if (uploads < WarmUploads && s.Art.Poll()) { Apply(s); uploads++; }
                if (s.Art.Pending) ArtPending++;
            }
        }

        // Waits for every strip being made and brings its texture in, for
        // tests and captures. False when one is still not done.
        public bool WaitForArt(int milliseconds = 10000)
        {
            bool all = true;
            foreach (var s in strips.Values)
            {
                if (s.Art == null || !s.Art.Pending) continue;
                if (s.Art.Wait(milliseconds)) Apply(s);
                all &= !s.Art.Pending;
            }
            return all;
        }

        // Starts making the strips a game will want, so the first sight of
        // a weapon costs no frame.
        public void Warm(IReadOnlyList<int> ids)
        {
            if (ids == null) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (int id in ids) StripFor(id);
            WarmMs = (float)watch.Elapsed.TotalMilliseconds;
            Warmed = ids.Count;
        }

        // How many strips the last Warm started and the main thread's time for it.
        public int Warmed { get; private set; }
        public float WarmMs { get; private set; }

        void Sweep()
        {
            float clock = Time.unscaledTime;
            if (clock < nextSweep) return;
            nextSweep = clock + 10f;
            evict.Clear();
            foreach (var kv in strips)
                if (kv.Value.Art != null && !kv.Value.Art.Pending && clock - kv.Value.Used > KeepSeconds) evict.Add(kv.Key);
            foreach (int id in evict)
            {
                var s = strips[id];
                s.Art.Dispose();
                s.Art = null;
                s.FramesBuilt = -1;
                if (s.Material != null) s.Material.mainTexture = null;
            }
        }

        FxMesh BatchFor(int strip)
        {
            if (!batches.TryGetValue(strip, out var b)) batches[strip] = b = new FxMesh();
            return b;
        }

        Material NewMaterial(Texture tex)
        {
            var m = new Material(Looks.Find("OkuEffect", "Sprites/Default")) { hideFlags = HideFlags.DontSave, mainTexture = tex };
            owned.Add(m);
            return m;
        }

        void EnsureMaterials()
        {
            if (glowMat != null) return;
            var glowTex = FxMesh.GlowTexture();
            owned.Add(glowTex);
            glowMat = NewMaterial(glowTex);
            smokeMat = NewMaterial(glowTex);
            var markTex = FxMesh.MarkTexture();
            owned.Add(markTex);
            markMat = new Material(Looks.Find("OkuFxDecal", "Sprites/Default")) { hideFlags = HideFlags.DontSave, mainTexture = markTex };
            owned.Add(markMat);
        }

        // Soft edges need the scene's depth and added art the picture under
        // it, asked for while effects show and a while after, so a quiet dry
        // map pays for no copies and a fight does not turn them on and off.
        public const float DepthLinger = 8f;
        float lastShowing = float.NegativeInfinity;

        void PrepareCamera(Camera cam, bool showing)
        {
            bool depth = false, under = false;
            if (showing && Looks.Urp != null)
            {
                // The depth also tells the sum where water hides the bed.
                var data = cam.GetUniversalAdditionalCameraData();
                if (data.requiresDepthOption != CameraOverrideOption.On) data.requiresDepthOption = CameraOverrideOption.On;
                if (data.requiresColorOption != CameraOverrideOption.On) data.requiresColorOption = CameraOverrideOption.On;
                depth = under = true;
                SetPost(data);
            }
            else if (showing && Soft)
            {
                cam.depthTextureMode |= DepthTextureMode.Depth;
                depth = true;
            }
            if (!under) Shader.SetGlobalFloat(PostId, 0f);
            Shader.SetGlobalFloat(DepthId, depth ? 1f : 0f);
            Shader.SetGlobalFloat(SoftId, depth && Soft ? 1f : 0f);
            Shader.SetGlobalFloat(OpaqueId, under ? 1f : 0f);
            Shader.SetGlobalFloat(HotId, Glow ? HotCore : 0f);
        }

        // The post chain's grade as the effects shader undoes it, so added
        // art lands on the screen as the byte sum the original makes. Only
        // URP's low range grade under the Neutral curve is known to it.
        static void SetPost(UniversalAdditionalCameraData data)
        {
            var stack = data.volumeStack ?? VolumeManager.instance.stack;
            var tone = stack?.GetComponent<Tonemapping>();
            bool known = data.renderPostProcessing && tone != null && tone.IsActive() && tone.mode.value == TonemappingMode.Neutral &&
                         Looks.Urp.colorGradingMode == ColorGradingMode.LowDynamicRange;
            Shader.SetGlobalFloat(PostId, known ? 1f : 0f);
            if (!known) return;
            var grade = stack.GetComponent<ColorAdjustments>();
            var balance = stack.GetComponent<WhiteBalance>();
            float exposure = 1f, contrast = 1f, saturation = 1f;
            if (grade != null && grade.IsActive())
            {
                exposure = Mathf.Pow(2f, grade.postExposure.value);
                contrast = grade.contrast.value / 100f + 1f;
                saturation = grade.saturation.value / 100f + 1f;
            }
            var lms = balance != null && balance.IsActive() ? ColorUtils.ColorBalanceToLMSCoeffs(balance.temperature.value, balance.tint.value) : Vector3.one;
            Shader.SetGlobalVector(GradeId, new Vector4(exposure, contrast, Mathf.Max(0.01f, saturation), 0f));
            Shader.SetGlobalVector(BalanceId, new Vector4(lms.x, lms.y, lms.z, 0f));
        }

        // How far past white the hottest added art goes, for the bloom.
        public const float HotCore = 0.5f;
        static readonly int SoftId = Shader.PropertyToID("_OkuFxSoft");
        static readonly int DepthId = Shader.PropertyToID("_OkuFxDepth");
        static readonly int HotId = Shader.PropertyToID("_OkuFxHot");
        static readonly int OpaqueId = Shader.PropertyToID("_OkuFxOpaque");
        static readonly int PostId = Shader.PropertyToID("_OkuFxPost");
        static readonly int GradeId = Shader.PropertyToID("_OkuFxGrade");
        static readonly int BalanceId = Shader.PropertyToID("_OkuFxBalance");

        public void Dispose()
        {
            foreach (var s in strips.Values) s.Art?.Dispose();
            strips.Clear();
            foreach (var b in batches.Values) b.Dispose();
            batches.Clear();
            glow.Dispose(); smoke.Dispose(); ground.Dispose(); groundGlow.Dispose(); rings.Dispose();
            particles.Dispose();
            stuckShots.Clear();
            shock.Clear();
            blasts.Clear();
            if (rig != null) rig.shake = Vector3.zero;
            lights.Dispose();
            trails.Clear();
            decals.Clear();
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            glowMat = smokeMat = markMat = null;
            Shader.SetGlobalFloat(SoftId, 0f);
            Shader.SetGlobalFloat(DepthId, 0f);
            Shader.SetGlobalFloat(OpaqueId, 0f);
            Shader.SetGlobalFloat(PostId, 0f);
        }
    }
}
