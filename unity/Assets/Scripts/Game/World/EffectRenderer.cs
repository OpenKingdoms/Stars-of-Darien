// EffectRenderer.cs - shots in flight, blasts and spells, drawn first as the
// original draws them (its frames, blend and pace), then remastered: eased
// frames, glow into the bloom, soft edges, trails, lights and scorch.
using System.Collections.Generic;
using UnityEngine;
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
        readonly Dictionary<int, (Strip strip, float height)> pictureOf = new Dictionary<int, (Strip, float)>();
        readonly HashSet<long> seenImpacts = new HashSet<long>(), liveImpacts = new HashSet<long>();
        readonly List<Object> owned = new List<Object>();
        readonly Vector3[] corners = new Vector3[4];
        readonly Vector3[] beam = new Vector3[FxLook.BeamSegments + 1];
        Material glowMat, smokeMat, markMat;

        // The remaster's parts, each of which can be turned off to see the
        // original's look alone.
        public bool Smooth = true;          // frames eased and enlarged
        public bool Glow = true;            // added art past white, for the bloom
        public bool Soft = true;            // soft where it meets the ground
        public bool Lights = true;
        public bool Trails = true;
        public bool Scorch = true;

        // What the last frame drew, for tests and the profiler.
        public int Count { get; private set; }
        public int Shots { get; private set; }
        public int Quads { get; private set; }
        // Quads of every kind, ribbons counted by their vertex pairs, and model shot pieces.
        public int Drawn { get; private set; }
        public int Beams { get; private set; }
        public int ModelShots { get; private set; }
        public int LightsLit => lights.Lit;
        public int Marks => decals.Count;
        public int TrailCount => trails.Count;
        public int ArtPending { get; private set; }

        sealed class Strip
        {
            public FxArt Art;
            public Material Material;
            public EffectFrame[] Frames;
            public int FramesBuilt;
        }

        public EffectRenderer(IGameBackend backend, ModelCache models = null)
        {
            this.backend = backend;
            this.models = models;
            shotModels.CastShadows = true;
        }

        // How far an effect is drawn toward the camera from its point, shrunk
        // to look the same, so the ground beside it never cuts it.
        public const float Nudge = 1f;

        // An effect's picture as four corners, bottom left first and
        // anticlockwise seen from the camera: in the camera's plane and
        // standing on its point, so it shows its full height at any tilt.
        public static void Corners(in EffectState e, Transform cam, Vector3[] into) =>
            Corners(e.Position, e.Top, e.Bottom, e.OffsetX, e.Width, cam, into);

        public static void Corners(Vector3 at, float top, float bottom, float offsetX, float width, Transform cam, Vector3[] into)
        {
            Nudged(at, cam, Nudge, out var pivot, out float k);
            var right = cam.right * k;
            var up = cam.up * k;
            var left = pivot - right * offsetX;
            var r = right * width;
            var lo = up * bottom;
            var hi = up * top;
            into[0] = left + lo; into[1] = left + r + lo; into[2] = left + r + hi; into[3] = left + hi;
        }

        // A point moved toward the camera by up to nudge, and the scale that
        // keeps a picture drawn there the same size on screen.
        public static void Nudged(Vector3 at, Transform cam, float nudge, out Vector3 pivot, out float scale)
        {
            var toCam = cam.position - at;
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
            var eye = cam.transform;
            float now = backend.Tick / (float)Mathf.Max(1, backend.TicksPerSecond);
            EnsureMaterials();
            foreach (var b in batches.Values) b.Clear();
            glow.Clear(); smoke.Clear(); ground.Clear(); groundGlow.Clear();
            shotModels.Clear();
            lights.Begin();
            pictureOf.Clear();
            liveImpacts.Clear();
            ArtPending = 0;
            foreach (var s in strips.Values)
            {
                if (s.Art == null) continue;
                if (s.Art.Poll() && s.Material != null) s.Material.mainTexture = s.Art.Texture;
                if (s.Art.Pending) ArtPending++;
            }

            Count = Mathf.Min(backend.ReadEffects(effects), effects.Length);
            for (int i = 0; i < Count; i++) AddEffect(effects[i], eye, now);
            seenImpacts.IntersectWith(liveImpacts);

            Shots = Mathf.Min(backend.ReadProjectiles(shots), shots.Length);
            if (Count + Shots > 0) lastShowing = Time.unscaledTime;
            PrepareDepth(cam, Time.unscaledTime - lastShowing < DepthLinger);
            Beams = 0;
            ModelShots = 0;
            for (int i = 0; i < Shots; i++) AddShot(shots[i], eye, now);

            if (Trails) trails.Draw(glow, smoke, eye.position, now);
            if (Scorch) decals.Draw(ground, groundGlow, backend.GroundHeight, now);

            Quads = 0;
            int vertices = 0;
            foreach (var kv in batches)
            {
                if (kv.Value.Vertices == 0) continue;
                var s = strips[kv.Key];
                kv.Value.Draw(s.Material);
                Quads += kv.Value.Quads;
                vertices += kv.Value.Vertices;
            }
            ground.Draw(markMat);
            groundGlow.Draw(glowMat);
            smoke.Draw(smokeMat);
            glow.Draw(glowMat);
            shotModels.Draw();
            Quads += glow.Quads + smoke.Quads;
            vertices += ground.Vertices + groundGlow.Vertices + smoke.Vertices + glow.Vertices;
            Drawn = vertices / 4 + shotModels.Count;
            if (!Lights) lights.Begin();
            lights.Commit(cam.transform.position + cam.transform.forward * 30f, 40f);
        }

        // ── Pictures ──────────────────────────────────────────────────

        void AddEffect(in EffectState e, Transform eye, float now)
        {
            if (e.Width <= 0 || e.Top <= e.Bottom) return;
            var s = StripFor(e.Strip);
            if (s == null || s.Art == null) return;
            var batch = BatchFor(e.Strip);
            var frames = s.Frames;
            float phase = Smooth ? Mathf.Clamp01(e.Phase) : 0f;
            int next = -2;
            if (Smooth && frames != null && frames.Length > 1)
            {
                next = e.Frame + 1;
                if (next >= frames.Length) next = e.Loops ? 0 : -1;
                if (next >= 0 && frames[next].Width <= 0) next = -2;
            }
            else if (Smooth && frames != null && frames.Length == 1 && !e.Loops) next = -1;
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
            var size = e.Light == FxLight.Auto ? FxLook.AutoLight(e.Additive, e.IsProjectile, tall, s.Art.Brightness) : e.Light;
            // A crowd of looping sprites, a ring or a rain, lights only when told to.
            if (e.Light == FxLight.Auto && e.Loops && !e.IsProjectile) size = FxLight.None;
            if (size != FxLight.None)
            {
                float life = 1f;
                if (!e.Loops && frames != null && frames.Length > 0) life = FxLook.LightEnvelope((e.Frame + phase) / frames.Length);
                float flicker = 0.92f + 0.08f * Mathf.Sin(now * 23f + e.Id * 1.7f);
                lights.Ask(e.Position + Vector3.up * Mathf.Max(1.2f, e.Bottom + tall * 0.45f), s.Art.Glow, size, life * flicker);
            }

            if (!e.IsProjectile && !e.Loops)
            {
                long key = ((long)e.Id << 32) ^ ((long)e.Strip << 20) ^ (long)(Mathf.RoundToInt(e.Position.x * 4f) * 7919 + Mathf.RoundToInt(e.Position.z * 4f));
                liveImpacts.Add(key);
                if (seenImpacts.Add(key) && Scorch && ScorchFor(e, s, tall)) decals.Scorch(e.Position, Mathf.Clamp(e.Width * 0.75f, 1.5f, 7f), now, e.Id);
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
            float ground = backend.GroundHeight(e.Position.x, e.Position.z);
            var t = backend.Terrain;
            if (t != null && t.SeaLevel > 0 && ground < t.SeaLevel) return false;
            return Mathf.Abs(e.Position.y - ground) < 1.5f;
        }

        void Sprite(FxMesh batch, Strip s, Vector3 at, float top, float bottom, float offsetX, float width, Vector4 rect, bool additive, float weight, Transform eye)
        {
            if (weight <= 0.004f || width <= 0f || top <= bottom) return;
            Corners(at, top, bottom, offsetX, width, eye, corners);
            var c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(weight) * 255f));
            var look = new Vector4(additive ? 1f : 0f, 1f, 1f, 0f);
            batch.Quad(corners[0], corners[1], corners[2], corners[3], rect, c, look);
        }

        // ── Shots ─────────────────────────────────────────────────────

        void AddShot(in ProjectileState p, Transform eye, float now)
        {
            switch (p.Kind)
            {
                case ShotKind.Beam: AddBeam(p, eye); return;
                case ShotKind.Model: AddModelShot(p); break;
                case ShotKind.Dot: AddDot(p, eye); break;
            }
            if (Trails) trails.Track(p.Id, p.Position, TrailStyle(p), now);
            if (p.Shadow)
            {
                float size = p.Kind == ShotKind.Picture && pictureOf.TryGetValue(p.Id, out var pic) ? Mathf.Clamp(pic.height * 0.8f, 0.35f, 3f) : 0.5f;
                decals.Shadow(ground, p.Position, size, 0.45f, backend.GroundHeight);
            }
        }

        FxTrails.Style TrailStyle(in ProjectileState p)
        {
            if (p.Kind == ShotKind.Picture && pictureOf.TryGetValue(p.Id, out var pic))
            {
                if (pic.strip.Art.Brightness > 0.12f && AdditiveArt(pic.strip))
                {
                    var g = pic.strip.Art.Glow;
                    return new FxTrails.Style { Colour = new Color(g.r, g.g, g.b, 0.45f), Width = Mathf.Clamp(pic.height * 0.28f, 0.15f, 0.7f), Seconds = 0.2f, Glow = 0.9f, Additive = true };
                }
                return new FxTrails.Style { Colour = new Color32(150, 144, 134, 60), Width = Mathf.Clamp(pic.height * 0.4f, 0.12f, 0.5f), Seconds = 0.35f, Glow = 1f };
            }
            if (p.Kind == ShotKind.Dot)
                return new FxTrails.Style { Colour = new Color32(255, 196, 90, 140), Width = 0.2f, Seconds = 0.15f, Glow = 1f, Additive = true };
            return new FxTrails.Style { Colour = new Color32(236, 232, 220, 50), Width = 0.06f, Seconds = 0.1f, Glow = 1f };
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
        }

        // A shot with no art: the classic client's small bright disc, here
        // a glowing ball with a hot core.
        void AddDot(in ProjectileState p, Transform eye)
        {
            Glowing(p.Position, 0.55f, new Color32(255, 200, 96, 200), 0.8f, eye);
            Glowing(p.Position, 0.22f, new Color32(255, 236, 170, 255), 1.2f, eye);
            lights.Ask(p.Position, new Color(1f, 0.75f, 0.4f), FxLight.Small, 0.45f);
        }

        void Glowing(Vector3 at, float size, Color32 c, float bright, Transform eye)
        {
            Nudged(at, eye, 0.3f, out var pivot, out float k);
            var r = eye.right * (size * 0.5f * k);
            var u = eye.up * (size * 0.5f * k);
            var look = new Vector4(1f, Glow ? bright : Mathf.Min(1f, bright), 0.5f, 0f);
            glow.Quad(pivot - r - u, pivot + r - u, pivot + r + u, pivot - r + u, FxMesh.WholeRect, c, look);
        }

        // A beam: a jagged ray from the firing piece to where it stopped, in
        // the weapon's three colours from a wide soft outer glow to a white
        // hot core, jagged afresh every 30 Hz frame as the original draws.
        void AddBeam(in ProjectileState p, Transform eye)
        {
            var from = p.Source;
            var to = p.Position;
            if (p.Beam == BeamKind.Fire)
            {
                // The flame particles are the stream. The breath lights its way.
                lights.Ask(from + (to - from) * 0.3f, new Color(1f, 0.55f, 0.2f), FxLight.Small, 0.8f);
                return;
            }
            var dir = to - from;
            if (dir.sqrMagnitude < 1e-4f) return;
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
            BeamPath(from, to, p.Beam, p.Seed, FxLook.JagStep(backend.Tick, backend.TicksPerSecond), eye.position, beam);
            bool straight = FxLook.Straight(p.Beam);
            Ribbon(beam, straight ? 0.8f : 0.42f, outer, 0.5f * fade, 0.6f, eye);
            Ribbon(beam, straight ? 0.3f : 0.16f, middle, 0.85f * fade, 0.9f, eye);
            Ribbon(beam, straight ? 0.12f : 0.06f, inner, fade, 1.2f, eye);
            Glowing(to, 1.1f, Fade(middle, 0.8f * fade), 1f, eye);
            Glowing(from, 0.5f, Fade(inner, 0.6f * fade), 1f, eye);
            var size = p.Light == FxLight.Auto ? FxLight.Small : p.Light;
            if (size != FxLight.None) lights.Ask(to + Vector3.up * 0.4f, (Color)middle, size, fade);
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

        void Ribbon(Vector3[] points, float width, Color32 colour, float alpha, float bright, Transform eye)
        {
            var c = Fade(colour, alpha);
            var look = new Vector4(1f, Glow ? bright : Mathf.Min(1f, bright), 0.4f, 0f);
            int prevA = -1, prevB = -1;
            int n = points.Length;
            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                var d = (i < n - 1 ? points[i + 1] : p) - (i > 0 ? points[i - 1] : p);
                var side = Vector3.Cross(d, eye.position - p);
                if (side.sqrMagnitude < 1e-8f) side = eye.right;
                side = side.normalized * (width * 0.5f);
                int a = glow.Vertex(p - side, c, new Vector2(0.5f, 0f), look);
                int b = glow.Vertex(p + side, c, new Vector2(0.5f, 1f), look);
                if (prevA >= 0) { glow.Triangle(prevA, b, prevB); glow.Triangle(prevA, a, b); }
                prevA = a; prevB = b;
            }
            // Round caps, so the joins at the ends do not show square.
            Glowing(points[0], width, c, bright, eye);
            Glowing(points[n - 1], width, c, bright, eye);
        }

        // ── Resources ─────────────────────────────────────────────────

        Strip StripFor(int id)
        {
            if (!strips.TryGetValue(id, out var s))
            {
                s = new Strip();
                strips[id] = s;
            }
            var frames = backend.EffectFrames(id);
            s.Frames = frames;
            int n = frames != null ? frames.Length : 0;
            if (s.Art != null && (n <= 1 || n == s.FramesBuilt)) return s;
            // Built again once the frame count is known, so each frame gets its own cell.
            var img = backend.EffectStrip(id);
            if (img == null) return s;
            s.Art?.Dispose();
            s.Art = FxArt.Build(img, n, Smooth);
            s.FramesBuilt = n;
            if (s.Art == null) return s;
            if (s.Material == null)
            {
                s.Material = NewMaterial(s.Art.Texture);
            }
            else s.Material.mainTexture = s.Art.Texture;
            return s;
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

        // Soft edges need the scene's depth, asked for while effects show and
        // a while after, so a quiet dry map pays for no copy and a fight does
        // not turn it on and off.
        public const float DepthLinger = 8f;
        float lastShowing = float.NegativeInfinity;

        void PrepareDepth(Camera cam, bool showing)
        {
            bool have = false;
            if (Soft && showing)
            {
                if (Looks.Urp != null)
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data.requiresDepthOption != CameraOverrideOption.On) data.requiresDepthOption = CameraOverrideOption.On;
                    have = true;
                }
                else
                {
                    cam.depthTextureMode |= DepthTextureMode.Depth;
                    have = true;
                }
            }
            Shader.SetGlobalFloat(SoftId, have ? 1f : 0f);
            Shader.SetGlobalFloat(HotId, Glow ? HotCore : 0f);
        }

        // How far past white the hottest added art goes, for the bloom.
        public const float HotCore = 0.5f;
        static readonly int SoftId = Shader.PropertyToID("_OkuFxSoft");
        static readonly int HotId = Shader.PropertyToID("_OkuFxHot");

        public void Dispose()
        {
            foreach (var s in strips.Values) s.Art?.Dispose();
            strips.Clear();
            foreach (var b in batches.Values) b.Dispose();
            batches.Clear();
            glow.Dispose(); smoke.Dispose(); ground.Dispose(); groundGlow.Dispose();
            lights.Dispose();
            trails.Clear();
            decals.Clear();
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            glowMat = smokeMat = markMat = null;
            Shader.SetGlobalFloat(SoftId, 0f);
        }
    }
}
