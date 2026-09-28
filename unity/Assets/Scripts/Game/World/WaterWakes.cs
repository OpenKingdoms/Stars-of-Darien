// WaterWakes.cs - foam behind and around ships afloat: a ribbon along each
// ship's recent path that opens into the Kelvin wedge and fades over a few
// seconds, and a collar at the hull with a bow wave that grows with speed.
// One dynamic mesh for every ship, drawn with OkuWake over the sea.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class WaterWakes
    {
        public const int MaxSamples = 64;
        public const float Life = 5f, Spacing = 0.3f;
        static readonly float Spread = Mathf.Tan(19.47f * Mathf.Deg2Rad);

        struct Sample { public Vector2 At; public float Time, Speed; }

        sealed class Trail
        {
            public readonly List<Sample> Samples = new List<Sample>();
            public Vector2 Pos, Prev, LastSample;
            public float PrevTime, Speed, Beam = 0.5f, Length = 1.5f, Heading;
            public bool Seen;
        }

        readonly Dictionary<int, Trail> trails = new Dictionary<int, Trail>();
        readonly List<int> gone = new List<int>();
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector4> uvs = new List<Vector4>();
        readonly List<Vector2> sizes = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        readonly List<(Vector2 p, float age, float speed)> points = new List<(Vector2, float, float)>();
        Mesh mesh;
        Material material;
        float sea;

        public int Count => trails.Count;
        public Mesh Mesh => mesh;

        // A ship's trail, newest first, starting at the ship.
        public List<Vector2> PathOf(int handle)
        {
            var list = new List<Vector2>();
            if (!trails.TryGetValue(handle, out var t)) return list;
            list.Add(t.Pos);
            for (int i = t.Samples.Count - 1; i >= 0; i--) list.Add(t.Samples[i].At);
            return list;
        }

        public float SpeedOf(int handle) => trails.TryGetValue(handle, out var t) ? t.Speed : 0f;

        public void Update(EntityRenderer entities, IGameBackend backend, float seaLevel, float now)
        {
            sea = seaLevel;
            foreach (var t in trails.Values) t.Seen = false;
            var defs = backend.UnitDefs;
            for (int i = 0; i < entities.UnitCount; i++)
            {
                var u = entities.Units[i];
                if (u.Def < 0 || u.Def >= defs.Count || Afloat.KindOf(defs[u.Def]) != FloatKind.Ship) continue;
                if (!entities.IsDrawn(u.Handle) || (u.Flags & UnitFlags.Dying) != 0) continue;
                if (backend.GroundHeight(u.Position.x, u.Position.z) > seaLevel - 0.2f) continue;
                var at = new Vector2(u.Position.x, u.Position.z);
                if (!trails.TryGetValue(u.Handle, out var tr))
                {
                    trails[u.Handle] = tr = new Trail { Pos = at, Prev = at, LastSample = at, PrevTime = now };
                }
                // Half the hull's beam and length.
                if (entities.Hulls.TryGetValue(u.Handle, out var hull))
                {
                    tr.Beam = Mathf.Clamp(hull.x, 0.25f, 2f);
                    tr.Length = Mathf.Clamp(hull.y, 0.6f, 6f);
                }
                tr.Seen = true;
                tr.Heading = u.Heading;
                tr.Pos = at;
                float dt = now - tr.PrevTime;
                if (dt > 1e-3f)
                {
                    float inst = (at - tr.Prev).magnitude / dt;
                    tr.Speed = Mathf.Lerp(tr.Speed, inst, 1f - Mathf.Exp(-dt / 0.3f));
                    tr.Prev = at;
                    tr.PrevTime = now;
                }
                if ((at - tr.LastSample).magnitude >= Spacing)
                {
                    tr.Samples.Add(new Sample { At = at, Time = now, Speed = tr.Speed });
                    if (tr.Samples.Count > MaxSamples) tr.Samples.RemoveAt(0);
                    tr.LastSample = at;
                }
                while (tr.Samples.Count > 0 && now - tr.Samples[0].Time > Life) tr.Samples.RemoveAt(0);
            }
            // A ship out of sight leaves no trail to give it away.
            gone.Clear();
            foreach (var kv in trails) if (!kv.Value.Seen) gone.Add(kv.Key);
            foreach (int h in gone) trails.Remove(h);
            Build(now);
        }

        void Build(float now)
        {
            verts.Clear(); uvs.Clear(); sizes.Clear(); tris.Clear();
            foreach (var tr in trails.Values)
            {
                // The ribbon: from the ship back along its path. uv x runs
                // across (-1 to 1), y along in units from the stern, z
                // strength, w age. uv1 is half the width and the hull's half beam.
                points.Clear();
                points.Add((tr.Pos, 0f, tr.Speed));
                for (int i = tr.Samples.Count - 1; i >= 0; i--)
                {
                    var s = tr.Samples[i];
                    if ((s.At - points[points.Count - 1].p).sqrMagnitude > 0.0025f) points.Add((s.At, now - s.Time, s.Speed));
                }
                if (points.Count >= 2)
                {
                    int first = verts.Count;
                    float along = 0;
                    for (int j = 0; j < points.Count; j++)
                    {
                        var (p, age, speed) = points[j];
                        if (j > 0) along += (p - points[j - 1].p).magnitude;
                        var dir = j + 1 < points.Count ? p - points[j + 1].p : points[j - 1].p - p;
                        dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector2.up;
                        var side = new Vector2(dir.y, -dir.x);
                        // Measured from the stern, where the wake starts.
                        float aft = along - tr.Length;
                        float half = tr.Beam + Mathf.Max(aft, 0f) * Spread;
                        float fade = Mathf.Clamp01(1f - age / Life);
                        float strength = Mathf.Clamp01(speed / 1.6f) * Mathf.Pow(fade, 1.2f);
                        verts.Add(new Vector3(p.x - side.x * half, sea, p.y - side.y * half));
                        verts.Add(new Vector3(p.x + side.x * half, sea, p.y + side.y * half));
                        uvs.Add(new Vector4(-1, aft, strength, age));
                        uvs.Add(new Vector4(1, aft, strength, age));
                        sizes.Add(new Vector2(half, tr.Beam));
                        sizes.Add(new Vector2(half, tr.Beam));
                    }
                    for (int k = 0; k + 1 < points.Count; k++)
                    {
                        int a = first + 2 * k;
                        tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                        tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                    }
                }
                // The collar round the hull: uv -1 to 1 over a box a little
                // bigger than the hull, z the ship's speed, w -1 to mark it.
                // A hull's waterline is a pointed ellipse, so the box's
                // inscribed ellipse at 0.8 just clears it.
                int c0 = verts.Count;
                float yaw = tr.Heading * Mathf.Deg2Rad;
                var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
                // Under way the bow is where the ship is going.
                if (points.Count >= 2 && (points[0].p - points[1].p).sqrMagnitude > 0.01f) fwd = (points[0].p - points[1].p).normalized;
                var right = new Vector2(fwd.y, -fwd.x);
                float hl = tr.Length * 1.15f + 0.1f, hw = tr.Beam * 1.3f + 0.1f;
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k == 1 || k == 2) ? 1 : -1, sz = k >= 2 ? 1 : -1;
                    var p = tr.Pos + right * (sx * hw) + fwd * (sz * hl);
                    verts.Add(new Vector3(p.x, sea, p.y));
                    uvs.Add(new Vector4(sx, sz, Mathf.Clamp01(tr.Speed / 2.5f), -1));
                    sizes.Add(new Vector2(hw, tr.Beam));
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

        public void Draw()
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            if (material == null) material = new Material(Looks.Find("OkuWake", "Sprites/Default")) { hideFlags = HideFlags.DontSave };
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, null, 0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }

        public void Dispose()
        {
            if (mesh != null) Looks.Release(mesh);
            if (material != null) Looks.Release(material);
            mesh = null;
            material = null;
            trails.Clear();
        }
    }
}
