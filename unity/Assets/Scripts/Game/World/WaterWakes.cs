// WaterWakes.cs - foam behind and around ships afloat: a ribbon along the
// way each ship's stern has come that opens into the Kelvin wedge and fades
// over a few seconds, and a collar round the hull, heaped at the bow as it
// gets under way. Both grow with how fast the ship moves forward through
// the water, read over a few tenths of a second, so a ship coming round on
// the spot throws none. The ribbons and collars are drawn from above into a
// small picture over the part of the sea in view (OkuWake keeps the most
// any of them gives a texel, so nothing stacks), and the sea reads it and
// draws the foam itself with its own lace and light.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class WaterWakes
    {
        public const int MaxSamples = 64, Size = 1024;
        public const float Life = 5f, Spacing = 0.3f, MaxSpan = 320f;
        // Forward speeds in units a second. Under the first a hull throws no
        // foam, and from the second its wake is whole. A ship turning on the
        // spot crawls slower than the first.
        const float FoamFrom = 1f, FoamFull = 2.4f;
        // The engine moves a ship in whole pixels on its own ticks, so its
        // speed is read over this long a stretch of its way, from a point
        // kept every Every seconds.
        const float Window = 0.5f, Every = 1f / 30f;
        const int Kept = 24;
        static readonly float Spread = Mathf.Tan(19.47f * Mathf.Deg2Rad);
        static readonly int RectId = Shader.PropertyToID("_OkuWakeRect"), TexId = Shader.PropertyToID("_OkuWakeTex");

        struct Sample { public Vector2 At; public float Time, Speed; }

        sealed class Trail
        {
            public readonly List<Sample> Samples = new List<Sample>();
            // Where the ship stood lately (x, z and when), oldest at First.
            public readonly Vector3[] Recent = new Vector3[Kept];
            public int First, Count;
            public Vector2 Stern, LastSample, Middle, Bow;
            public float Then, Speed;
            public ShipHull Hull = new ShipHull(0f, 0.3f, 0.5f, 1.5f, 0f);
            public bool Seen;
        }

        readonly Dictionary<int, Trail> trails = new Dictionary<int, Trail>();
        readonly List<int> gone = new List<int>();
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector4> uvs = new List<Vector4>();
        readonly List<Vector2> sizes = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        readonly List<(Vector2 p, float age, float speed)> points = new List<(Vector2, float, float)>();
        readonly List<Vector2> dirs = new List<Vector2>();
        readonly List<bool> live = new List<bool>();
        readonly List<float> alongs = new List<float>(), halves = new List<float>();
        readonly Vector3[] corners = new Vector3[4];
        Mesh mesh;
        Material material;
        RenderTexture target;
        CommandBuffer cmd;
        Vector2 lo, hi;

        public int Count => trails.Count;
        public Mesh Mesh => mesh;
        // Where the picture lies: x, z of its corner, 1 / width, 1 / depth. Zero with no wakes.
        public Vector4 Rect { get; private set; }
        public RenderTexture Texture => target;

        // The way a ship's stern has come, newest first, starting where it is.
        public List<Vector2> PathOf(int handle)
        {
            var list = new List<Vector2>();
            if (!trails.TryGetValue(handle, out var t)) return list;
            list.Add(t.Stern);
            for (int i = t.Samples.Count - 1; i >= 0; i--) list.Add(t.Samples[i].At);
            return list;
        }

        public float SpeedOf(int handle) => trails.TryGetValue(handle, out var t) ? t.Speed : 0f;

        public void Update(EntityRenderer entities, IGameBackend backend, float seaLevel, float now)
        {
            foreach (var t in trails.Values) t.Seen = false;
            var defs = backend.UnitDefs;
            for (int i = 0; i < entities.UnitCount; i++)
            {
                var u = entities.Units[i];
                if (u.Def < 0 || u.Def >= defs.Count || defs[u.Def].Float != FloatKind.Ship) continue;
                if ((u.Flags & UnitFlags.Dying) != 0 || !entities.Hulls.TryGetValue(u.Handle, out var drawn)) continue;
                if (backend.GroundHeight(u.Position.x, u.Position.z) > seaLevel - 0.2f) continue;
                var at = new Vector2(u.Position.x, u.Position.z);
                if (!trails.TryGetValue(u.Handle, out var tr)) trails[u.Handle] = tr = new Trail { Then = now };
                tr.Hull = drawn.hull;
                tr.Middle = drawn.middle;
                tr.Bow = drawn.bow;
                tr.Seen = true;
                // The wake leaves the hull a little inside its stern.
                tr.Stern = tr.Middle - tr.Bow * (Mathf.Clamp(tr.Hull.HalfLength, 0.5f, 6f) * 0.6f);
                if (tr.Count == 0) tr.LastSample = tr.Stern;
                Remember(tr, at, now);
                float dt = now - tr.Then;
                if (dt > 1e-3f)
                {
                    // Only the way made along the bow: a hull swinging round
                    // or nudged aside makes no bow wave.
                    float forward = Mathf.Max(0f, Vector2.Dot(Velocity(tr, now), tr.Bow));
                    tr.Speed = Mathf.Lerp(tr.Speed, forward, 1f - Mathf.Exp(-dt / 0.5f));
                    tr.Then = now;
                }
                if ((tr.Stern - tr.LastSample).magnitude >= Spacing)
                {
                    tr.Samples.Add(new Sample { At = tr.Stern, Time = now, Speed = tr.Speed });
                    if (tr.Samples.Count > MaxSamples) tr.Samples.RemoveAt(0);
                    tr.LastSample = tr.Stern;
                }
                while (tr.Samples.Count > 0 && now - tr.Samples[0].Time > Life) tr.Samples.RemoveAt(0);
            }
            // A ship out of sight leaves no trail to give it away.
            gone.Clear();
            foreach (var kv in trails) if (!kv.Value.Seen) gone.Add(kv.Key);
            foreach (int h in gone) trails.Remove(h);
            Build(now);
        }

        // Keeps where a ship stands every Every seconds, back to the last
        // point at least Window old.
        static void Remember(Trail tr, Vector2 at, float now)
        {
            while (tr.Count > 1 && now - tr.Recent[(tr.First + 1) % Kept].z >= Window)
            {
                tr.First = (tr.First + 1) % Kept;
                tr.Count--;
            }
            if (tr.Count > 0 && now - tr.Recent[(tr.First + tr.Count - 1) % Kept].z < Every) return;
            if (tr.Count == Kept)
            {
                tr.First = (tr.First + 1) % Kept;
                tr.Count--;
            }
            tr.Recent[(tr.First + tr.Count) % Kept] = new Vector3(at.x, at.y, now);
            tr.Count++;
        }

        // A ship's velocity over the last Window: the line best fitted to
        // where it stood, so its whole-pixel steps even out. Zero until a
        // tenth of a second of its way is known.
        static Vector2 Velocity(Trail tr, float now)
        {
            if (tr.Count < 2 || now - tr.Recent[tr.First].z < 0.1f) return Vector2.zero;
            float mt = 0;
            var mp = Vector2.zero;
            for (int i = 0; i < tr.Count; i++)
            {
                var e = tr.Recent[(tr.First + i) % Kept];
                mt += e.z;
                mp += new Vector2(e.x, e.y);
            }
            mt /= tr.Count;
            mp /= tr.Count;
            float tt = 0;
            var tp = Vector2.zero;
            for (int i = 0; i < tr.Count; i++)
            {
                var e = tr.Recent[(tr.First + i) % Kept];
                float d = e.z - mt;
                tt += d * d;
                tp += d * (new Vector2(e.x, e.y) - mp);
            }
            return tt > 1e-8f ? tp / tt : Vector2.zero;
        }

        // How much of its wake a hull moving forward at a speed throws, 0 to 1.
        static float Foam(float speed) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FoamFrom, FoamFull, speed));

        void Build(float now)
        {
            verts.Clear(); uvs.Clear(); sizes.Clear(); tris.Clear();
            lo = new Vector2(float.MaxValue, float.MaxValue);
            hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var tr in trails.Values)
            {
                float beam = Mathf.Clamp(tr.Hull.HalfBeam, 0.25f, 2f), length = Mathf.Clamp(tr.Hull.HalfLength, 0.5f, 6f);
                // The ribbon: from the stern back along the way it came. uv x
                // runs across (-1 to 1), y along in units from the stern, z
                // strength, w age. uv1 is half the width and the hull's half beam.
                points.Clear();
                points.Add((tr.Stern, 0f, tr.Speed));
                for (int i = tr.Samples.Count - 1; i >= 0; i--)
                {
                    var s = tr.Samples[i];
                    if ((s.At - points[points.Count - 1].p).sqrMagnitude > 0.0025f) points.Add((s.At, now - s.Time, s.Speed));
                }
                if (points.Count >= 2)
                {
                    // The ribbon runs only between points the ship was under
                    // way at. Where its stern swung round on the spot it
                    // laid no wake, so the wake it lays after starts afresh.
                    live.Clear();
                    for (int j = 0; j < points.Count; j++) live.Add(Foam(points[j].speed) > 0.02f);
                    // Directions smoothed over a few samples, so a jittering
                    // heading does not fold the ribbon.
                    dirs.Clear();
                    for (int j = 0; j < points.Count; j++)
                    {
                        var sum = Vector2.zero;
                        for (int k = Mathf.Max(0, j - 2); k < Mathf.Min(points.Count - 1, j + 2); k++)
                        {
                            if (!live[k] || !live[k + 1]) continue;
                            var seg = points[k].p - points[k + 1].p;
                            if (seg.sqrMagnitude > 1e-8f) sum += seg.normalized;
                        }
                        dirs.Add(sum.sqrMagnitude > 1e-8f ? sum.normalized : (j > 0 ? dirs[j - 1] : tr.Bow));
                    }
                    alongs.Clear();
                    halves.Clear();
                    float along = 0;
                    for (int j = 0; j < points.Count; j++)
                    {
                        float seg = j > 0 ? (points[j].p - points[j - 1].p).magnitude : 0f;
                        along += seg;
                        float half = beam + along * Spread;
                        // No wider than the path's bend allows, or the ribbon
                        // folds over itself on the inside of a turn.
                        if (j > 0 && seg > 1e-4f && live[j - 1] && live[j])
                        {
                            float turn = Vector2.Angle(dirs[j - 1], dirs[j]) * Mathf.Deg2Rad;
                            if (turn > 1e-3f) half = Mathf.Min(half, Mathf.Max(seg / turn * 0.9f, beam));
                        }
                        alongs.Add(along);
                        halves.Add(half);
                    }
                    // A bend narrows the ribbon only as fast as the wedge
                    // widens, either way along it, or a point held in and the
                    // next let out saw its edges.
                    for (int j = 1; j < halves.Count; j++) halves[j] = Mathf.Min(halves[j], halves[j - 1] + (alongs[j] - alongs[j - 1]) * Spread);
                    for (int j = halves.Count - 2; j >= 0; j--) halves[j] = Mathf.Min(halves[j], halves[j + 1] + (alongs[j + 1] - alongs[j]) * Spread);
                    int first = verts.Count;
                    for (int j = 0; j < points.Count; j++)
                    {
                        var (p, age, speed) = points[j];
                        var side = new Vector2(dirs[j].y, -dirs[j].x);
                        float half = halves[j];
                        float fade = Mathf.Clamp01(1f - age / Life);
                        float strength = Foam(speed) * Mathf.Pow(fade, 1.2f);
                        Put(new Vector2(p.x - side.x * half, p.y - side.y * half), new Vector4(-1, alongs[j], strength, age), new Vector2(half, beam));
                        Put(new Vector2(p.x + side.x * half, p.y + side.y * half), new Vector4(1, alongs[j], strength, age), new Vector2(half, beam));
                    }
                    for (int k = 0; k + 1 < points.Count; k++)
                    {
                        if (!live[k] || !live[k + 1]) continue;
                        int a = first + 2 * k;
                        tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                        tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                    }
                }
                // The collar: a box round the hull's waterline and a little
                // past it, turned with the hull. uv is the point across and
                // along from the hull's middle in units, z how much foam the
                // speed throws, w -1 to mark it, uv1 the hull's half beam and
                // half length there.
                int c0 = verts.Count;
                var fwd = tr.Bow;
                var right = new Vector2(fwd.y, -fwd.x);
                float hl = length + 0.7f, hw = beam + 0.7f;
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k == 1 || k == 2) ? 1 : -1, sz = k >= 2 ? 1 : -1;
                    Put(tr.Middle + right * (sx * hw) + fwd * (sz * hl), new Vector4(sx * hw, sz * hl, Foam(tr.Speed), -1), new Vector2(beam, length));
                }
                tris.Add(c0); tris.Add(c0 + 2); tris.Add(c0 + 1);
                tris.Add(c0); tris.Add(c0 + 3); tris.Add(c0 + 2);
            }
            if (mesh == null)
            {
                mesh = new Mesh { name = "wakes", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
            }
            mesh.Clear();
            if (verts.Count == 0) return;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
        }

        void Put(Vector2 at, Vector4 uv, Vector2 size)
        {
            verts.Add(new Vector3(at.x, 0f, at.y));
            uvs.Add(uv);
            sizes.Add(size);
            lo = Vector2.Min(lo, at);
            hi = Vector2.Max(hi, at);
        }

        // Draws the wakes into the picture the sea reads, over the part of
        // the sea the camera sees, and tells the sea where it lies.
        public void Draw(Camera cam, float seaLevel)
        {
            if (mesh == null || mesh.vertexCount == 0 || !Frame(cam, seaLevel, out var rect))
            {
                Rect = Vector4.zero;
                Shader.SetGlobalVector(RectId, Vector4.zero);
                Shader.SetGlobalTexture(TexId, Texture2D.blackTexture);
                return;
            }
            if (target == null)
            {
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RG16) ? RenderTextureFormat.RG16 : RenderTextureFormat.ARGB32;
                target = new RenderTexture(Size, Size, 0, format, RenderTextureReadWrite.Linear)
                {
                    name = "wakes", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false,
                };
                target.Create();
            }
            if (material == null) material = new Material(Looks.Find("OkuWake", "Hidden/Internal-Colored")) { hideFlags = HideFlags.DontSave };
            if (cmd == null) cmd = new CommandBuffer { name = "Oku wakes" };
            Rect = rect;
            Shader.SetGlobalVector(RectId, rect);
            cmd.Clear();
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            Shader.SetGlobalTexture(TexId, target);
        }

        // The wakes' extent, cut to the sea the camera sees and to MaxSpan
        // round the middle of the view.
        bool Frame(Camera cam, float seaLevel, out Vector4 rect)
        {
            rect = Vector4.zero;
            Vector2 a = lo - Vector2.one, b = hi + Vector2.one;
            if (cam != null)
            {
                var view = new Vector2(float.MaxValue, float.MaxValue);
                var viewHi = new Vector2(float.MinValue, float.MinValue);
                cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), cam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);
                var eye = cam.transform.position;
                for (int i = 0; i < 4; i++)
                {
                    var ray = cam.transform.TransformVector(corners[i]);
                    // Past the horizon the view is cut 400 units out.
                    float reach = ray.y < -1e-4f ? Mathf.Min((seaLevel - eye.y) / ray.y, 1f) : 1f;
                    var hit = eye + ray * reach;
                    var flat = (new Vector2(hit.x, hit.z) - new Vector2(eye.x, eye.z));
                    if (flat.magnitude > 400f) flat = flat.normalized * 400f;
                    var p = new Vector2(eye.x, eye.z) + flat;
                    view = Vector2.Min(view, p);
                    viewHi = Vector2.Max(viewHi, p);
                }
                view = Vector2.Min(view, new Vector2(eye.x, eye.z));
                viewHi = Vector2.Max(viewHi, new Vector2(eye.x, eye.z));
                a = Vector2.Max(a, view);
                b = Vector2.Min(b, viewHi);
                if (b.x <= a.x || b.y <= a.y) return false;
                // Too wide a view keeps MaxSpan round where the camera looks.
                var fwd = cam.transform.forward;
                var look = fwd.y < -1e-3f ? eye + fwd * ((seaLevel - eye.y) / fwd.y) : eye;
                var c = new Vector2(Mathf.Clamp(look.x, a.x, b.x), Mathf.Clamp(look.z, a.y, b.y));
                if (b.x - a.x > MaxSpan) { a.x = Mathf.Max(a.x, c.x - MaxSpan / 2); b.x = Mathf.Min(b.x, a.x + MaxSpan); }
                if (b.y - a.y > MaxSpan) { a.y = Mathf.Max(a.y, c.y - MaxSpan / 2); b.y = Mathf.Min(b.y, a.y + MaxSpan); }
            }
            if (b.x - a.x > MaxSpan) b.x = a.x + MaxSpan;
            if (b.y - a.y > MaxSpan) b.y = a.y + MaxSpan;
            rect = new Vector4(a.x, a.y, 1f / Mathf.Max(b.x - a.x, 1e-3f), 1f / Mathf.Max(b.y - a.y, 1e-3f));
            return true;
        }

        public void Dispose()
        {
            if (mesh != null) Looks.Release(mesh);
            if (material != null) Looks.Release(material);
            if (target != null) { target.Release(); Looks.Release(target); }
            cmd?.Release();
            mesh = null;
            material = null;
            target = null;
            cmd = null;
            trails.Clear();
            Rect = Vector4.zero;
            Shader.SetGlobalVector(RectId, Vector4.zero);
            Shader.SetGlobalTexture(TexId, Texture2D.blackTexture);
        }
    }
}
