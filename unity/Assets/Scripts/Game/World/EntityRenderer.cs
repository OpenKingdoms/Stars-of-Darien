// EntityRenderer.cs - draws units, features and projectiles each frame
// from the backend's snapshots, all through GPU instancing: every model
// piece at the pose the backend computed, sprite features as upright
// quads turned to the camera, arrows as thin shafts, and selection rings
// and health bars on top.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class EntityRenderer
    {
        public const int MaxUnits = 4096, MaxFeatures = 8192, MaxProjectiles = 2048, MaxPieces = 128;

        readonly IGameBackend backend;
        readonly ModelCache models;
        readonly InstancedDraws solid = new InstancedDraws();
        readonly InstancedDraws overlay = new InstancedDraws { CastShadows = false };
        // Sprite billboards take shadows but cast none, since a flat card
        // turned to the camera would throw a wrong one.
        readonly InstancedDraws billboards = new InstancedDraws { CastShadows = false };
        readonly Dictionary<int, Material> spriteMats = new Dictionary<int, Material>();
        readonly List<Object> owned = new List<Object>();

        public readonly UnitState[] Units = new UnitState[MaxUnits];
        public int UnitCount { get; private set; }
        readonly FeatureState[] features = new FeatureState[MaxFeatures];
        readonly ProjectileState[] shots = new ProjectileState[MaxProjectiles];
        readonly PiecePose[] poses = new PiecePose[MaxPieces];
        readonly Matrix4x4[] posed = new Matrix4x4[MaxPieces];
        public readonly HashSet<int> Selected = new HashSet<int>();
        public int Drawn => solid.Count + billboards.Count;

        readonly Mesh quad, ring, shaft, barQuad;
        readonly Material ringMat, barBack, barGood, barMid, barLow, shaftMat;
        // Features never move in the mock and rarely in a game, so their
        // matrices are cached until the count changes.
        int featureCount = -1;
        readonly List<(Mesh mesh, int sub, Material mat, Matrix4x4 m, bool flat)> featureDraws = new List<(Mesh, int, Material, Matrix4x4, bool)>();
        readonly List<(int sprite, Vector3 pos, float w, float bottom, float top, float offX)> spriteFeatures = new List<(int, Vector3, float, float, float, float)>();

        // A building being placed: its model at rest and its footprint,
        // green where it can stand and red where it cannot.
        public struct GhostState
        {
            public int Def;
            public Vector3 At;
            public bool Ok;
        }
        public GhostState? Ghost;

        // The map editor's brush on the ground: a ring, or a square for paint.
        public struct BrushState
        {
            public Vector3 At;
            public float Radius;
            public EditTool Tool;
        }
        public BrushState? Brush;
        Material brushMat;

        void AddBrush()
        {
            if (Brush == null) return;
            var b = Brush.Value;
            var at = b.At + Vector3.up * 0.1f;
            if (b.Tool == EditTool.Paint)
                overlay.Add(flat, 0, brushMat, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(b.Radius * 2, 1, b.Radius * 2)));
            else
                overlay.Add(ring, 0, brushMat, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(b.Radius, 1, b.Radius)));
        }
        // Units not to draw, such as enemies out of sight.
        public System.Func<UnitState, bool> Hidden;
        Mesh flat;
        Material ghostGood, ghostBad;

        // Lines from selected units to where their orders take them.
        Material lineMove, lineAttack, lineBuild, linePatrol;
        readonly Dictionary<int, Vector3> positions = new Dictionary<int, Vector3>();

        void AddOrderLines()
        {
            if (Selected.Count == 0 || Selected.Count > 60) return;
            positions.Clear();
            for (int i = 0; i < UnitCount; i++) positions[Units[i].Handle] = Units[i].Position;
            foreach (int h in Selected)
            {
                if (!positions.TryGetValue(h, out var from)) continue;
                var o = backend.ReadOrder(h);
                Material mat;
                switch (o.Kind)
                {
                    case OrderKind.Move: mat = lineMove; break;
                    case OrderKind.Attack: case OrderKind.AttackGround: mat = lineAttack; break;
                    case OrderKind.Build: case OrderKind.Repair: mat = lineBuild; break;
                    case OrderKind.Patrol: case OrderKind.Guard: mat = linePatrol; break;
                    default: continue;
                }
                Vector3 to = o.Target;
                if (o.TargetUnit >= 0 && positions.TryGetValue(o.TargetUnit, out var tp)) to = tp;
                else if (o.Building >= 0 && positions.TryGetValue(o.Building, out var bp)) to = bp;
                var d = to - from;
                d.y = 0;
                if (d.magnitude < 0.5f) continue;
                var mid = (from + to) * 0.5f + Vector3.up * 0.08f;
                overlay.Add(flat, 0, mat, Matrix4x4.TRS(mid, Quaternion.LookRotation(d.normalized), new Vector3(0.08f, 1, d.magnitude)));
                overlay.Add(ring, 0, mat, Matrix4x4.TRS(to + Vector3.up * 0.08f, Quaternion.identity, new Vector3(0.35f, 1, 0.35f)));
            }
        }

        void AddGhost()
        {
            if (Ghost == null) return;
            var g = Ghost.Value;
            if (g.Def < 0 || g.Def >= backend.UnitDefs.Count) return;
            var def = backend.UnitDefs[g.Def];
            var fp = new Vector3(Mathf.Max(1, def.Footprint.x), 1, Mathf.Max(1, def.Footprint.y));
            overlay.Add(flat, 0, g.Ok ? ghostGood : ghostBad, Matrix4x4.TRS(g.At + Vector3.up * 0.06f, Quaternion.identity, fp));
            int id = backend.LoadModel(def.ObjectName, backend.Players.Count > 0 ? backend.Players[backend.LocalPlayer].Colour : 0);
            var model = models.Get(id);
            if (model == null) return;
            var d = model.Data;
            var rest = new Matrix4x4[d.Pieces.Length];
            for (int p = 0; p < d.Pieces.Length; p++)
            {
                var m = Matrix4x4.Translate(d.Pieces[p].Offset * d.Scale);
                int parent = d.Pieces[p].Parent;
                rest[p] = parent >= 0 && parent < p ? rest[parent] * m : m;
                if (model.Pieces[p] == null) continue;
                var at = Matrix4x4.Translate(g.At) * rest[p];
                for (int s = 0; s < model.Materials[p].Length; s++) billboards.Add(model.Pieces[p], s, model.Materials[p][s], at);
            }
        }

        public EntityRenderer(IGameBackend backend, ModelCache models)
        {
            this.backend = backend;
            this.models = models;
            quad = Keep(Quad(0.5f));
            // Cards light like the ground under them, not like a wall facing the camera.
            quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            barQuad = Keep(Quad(0f));
            ring = Keep(Ring(0.85f, 1f, 40));
            shaft = Keep(Box(new Vector3(0.04f, 0.04f, 0.9f)));
            ringMat = Keep(Looks.Overlay(new Color(0.45f, 1f, 0.45f, 0.9f)));
            barBack = Keep(Looks.Overlay(new Color(0, 0, 0, 0.75f)));
            barGood = Keep(Looks.Overlay(new Color(0.3f, 0.95f, 0.3f, 1f)));
            barMid = Keep(Looks.Overlay(new Color(1f, 0.85f, 0.2f, 1f)));
            barLow = Keep(Looks.Overlay(new Color(1f, 0.25f, 0.2f, 1f)));
            flat = Keep(FlatQuad());
            ghostGood = Keep(Looks.Overlay(new Color(0.3f, 1f, 0.35f, 0.35f)));
            ghostBad = Keep(Looks.Overlay(new Color(1f, 0.25f, 0.2f, 0.4f)));
            brushMat = Keep(Looks.Overlay(new Color(1f, 0.9f, 0.5f, 0.45f)));
            lineMove = Keep(Looks.Overlay(new Color(0.4f, 1f, 0.4f, 0.55f)));
            lineAttack = Keep(Looks.Overlay(new Color(1f, 0.3f, 0.25f, 0.6f)));
            lineBuild = Keep(Looks.Overlay(new Color(1f, 0.85f, 0.3f, 0.6f)));
            linePatrol = Keep(Looks.Overlay(new Color(0.4f, 0.7f, 1f, 0.55f)));
            shaftMat = Keep(Looks.Model(null));
            shaftMat.color = new Color(0.35f, 0.25f, 0.15f);
        }

        T Keep<T>(T o) where T : Object { owned.Add(o); return o; }

        public void Render(Camera cam)
        {
            solid.Clear();
            overlay.Clear();
            billboards.Clear();

            UnitCount = backend.ReadUnits(Units);
            for (int i = 0; i < UnitCount; i++) AddUnit(ref Units[i], cam);

            AddFeatures(cam);

            int shotCount = backend.ReadProjectiles(shots);
            for (int i = 0; i < shotCount; i++)
            {
                var s = shots[i];
                // Picture shots come through the effects instead.
                if (s.Kind == 2) continue;
                var dir = s.Velocity.sqrMagnitude > 1e-4f ? s.Velocity.normalized : Vector3.forward;
                solid.Add(shaft, 0, shaftMat, Matrix4x4.TRS(s.Position, Quaternion.LookRotation(dir), Vector3.one));
            }

            AddGhost();
            AddOrderLines();
            AddBrush();
            solid.Draw();
            billboards.Draw();
            overlay.Draw();
        }

        void AddUnit(ref UnitState u, Camera cam)
        {
            if (Hidden != null && Hidden(u)) return;
            var def = u.Def >= 0 && u.Def < backend.UnitDefs.Count ? backend.UnitDefs[u.Def] : null;
            var model = models.Get(u.Model, OverrideKind.Unit, def != null ? new[] { def.Name, def.ObjectName } : null);
            float height = 1.5f, radius = 0.6f;
            if (model != null && model.Override != null)
            {
                int n = backend.ReadUnitPose(u.Handle, poses);
                AddOverride(model, n);
            }
            else if (model != null)
            {
                int n = Mathf.Min(backend.ReadUnitPose(u.Handle, poses), model.Pieces.Length);
                for (int p = 0; p < n; p++) posed[p] = poses[p].Matrix * model.Unscale;
                // Nudges from the animation editor, for every animation and
                // for the script function driving the unit now.
                var nudges = def != null ? AnimOverride.Load(def.ObjectName) : null;
                nudges?.Apply(model.Data.Pieces, posed, n, backend.UnitAnimation(u.Handle));
                for (int p = 0; p < n; p++)
                {
                    var mesh = model.Pieces[p];
                    if (mesh == null || poses[p].Hidden) continue;
                    var mats = model.Materials[p];
                    for (int s = 0; s < mats.Length; s++) solid.Add(mesh, s, mats[s], posed[p]);
                }
                var b = model.RestBounds;
                height = Mathf.Max(Mathf.Abs(b.max.y), Mathf.Abs(b.min.y)) + 0.4f;
                radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * 0.9f, 0.4f, 6f);
            }

            if ((u.Flags & UnitFlags.Dying) != 0) return;
            bool selected = Selected.Contains(u.Handle);
            var ground = u.Position + Vector3.up * 0.05f;
            if (selected)
                overlay.Add(ring, 0, ringMat, Matrix4x4.TRS(ground, Quaternion.identity, new Vector3(radius, 1, radius)));
            if (selected || u.Health < u.MaxHealth)
            {
                float f = u.MaxHealth > 0 ? Mathf.Clamp01((float)u.Health / u.MaxHealth) : 1f;
                var face = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
                var top = u.Position + Vector3.up * height;
                const float w = 1.2f, h = 0.14f;
                var left = top - cam.transform.right * (w * 0.5f);
                overlay.Add(barQuad, 0, barBack, Matrix4x4.TRS(left, face, new Vector3(w, h, 1)));
                var fill = f > 0.6f ? barGood : f > 0.3f ? barMid : barLow;
                overlay.Add(barQuad, 0, fill, Matrix4x4.TRS(left - cam.transform.forward * 0.01f, face, new Vector3(w * f, h, 1)));
            }
        }

        // A drop-in model: parts named like a piece follow that piece, the
        // rest ride the root. Overrides are in cells, so the model's own
        // unit scale comes off first.
        void AddOverride(PresentedModel model, int posed)
        {
            var d = model.Data;
            int root = 0;
            for (int i = 0; i < d.Pieces.Length; i++) if (d.Pieces[i].Parent < 0) { root = i; break; }
            if (posed <= root) return;
            var basis = poses[root].Matrix * model.Unscale * model.RestInverse[root];
            foreach (var part in model.Override.Parts)
            {
                Matrix4x4 m;
                if (part.Piece >= 0 && part.Piece < posed)
                {
                    if (poses[part.Piece].Hidden) continue;
                    m = poses[part.Piece].Matrix * model.Unscale * model.RestInverse[part.Piece] * part.NodeToRoot;
                }
                else m = basis * part.NodeToRoot;
                solid.Add(part.Mesh, part.Submesh, part.Material, m);
            }
        }

        void AddFeatures(Camera cam)
        {
            int n = backend.ReadFeatures(features);
            if (n != featureCount)
            {
                bool seaOn = backend.Terrain != null && backend.Terrain.SeaLevel > 0;
                featureCount = n;
                featureDraws.Clear();
                foreach (var d in drapes) { owned.Remove(d); Looks.Release(d); }
                drapes.Clear();
                spriteFeatures.Clear();
                for (int i = 0; i < n; i++)
                {
                    var f = features[i];
                    var fdef = f.Def >= 0 && f.Def < backend.FeatureDefs.Count ? backend.FeatureDefs[f.Def] : null;
                    var fnames = fdef != null ? new[] { fdef.Name, fdef.SequenceName, fdef.ObjectName } : new string[0];
                    // Shoreline wave sprites give way to the sea's own foam.
                    if (f.Model < 0 && fdef != null && seaOn && IsWave(fdef)) continue;
                    if (f.Model < 0 && f.Sprite >= 0)
                    {
                        // A sprite feature with a drop-in model draws the model.
                        var over = OverrideLoader.Find(OverrideKind.Feature, null, fnames);
                        if (over != null)
                        {
                            var at = Matrix4x4.TRS(f.Position, Quaternion.Euler(0, f.Heading, 0), Vector3.one);
                            foreach (var part in over.Parts) featureDraws.Add((part.Mesh, part.Submesh, part.Material, at * part.NodeToRoot, part.Flat));
                            continue;
                        }
                    }
                    if (f.Model >= 0)
                    {
                        var model = models.Get(f.Model, OverrideKind.Feature, fnames);
                        if (model == null) continue;
                        int pn = backend.ReadFeaturePose(f.Index, poses);
                        if (model.Override != null)
                        {
                            var basis = pn > 0 ? poses[0].Matrix * model.Unscale * model.RestInverse[0] : Matrix4x4.identity;
                            foreach (var part in model.Override.Parts) featureDraws.Add((part.Mesh, part.Submesh, part.Material, basis * part.NodeToRoot, part.Flat));
                            continue;
                        }
                        for (int p = 0; p < pn && p < model.Pieces.Length; p++)
                        {
                            if (model.Pieces[p] == null || poses[p].Hidden) continue;
                            var mats = model.Materials[p];
                            for (int s = 0; s < mats.Length; s++) featureDraws.Add((model.Pieces[p], s, mats[s], poses[p].Matrix * model.Unscale, false));
                        }
                    }
                    else if (f.Sprite >= 0 && f.Flat)
                    {
                        // A flat sprite, such as a lodestone site, lies on the
                        // ground and follows it, north up.
                        var mat = SpriteMaterial(f.Sprite);
                        if (mat != null) featureDraws.Add((Drape(f), 0, mat, Matrix4x4.identity, true));
                    }
                    else if (f.Sprite >= 0)
                        spriteFeatures.Add((f.Sprite, f.Position, f.SpriteWidth, f.SpriteBottom, f.SpriteTop, f.SpriteOffsetX));
                }
            }
            foreach (var d in featureDraws) (d.flat ? billboards : solid).Add(d.mesh, d.sub, d.mat, d.m);

            // Upright quads turned about y to face the camera.
            var fwd = cam.transform.forward;
            fwd.y = 0;
            var face = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd) : Quaternion.identity;
            foreach (var s in spriteFeatures)
            {
                var mat = SpriteMaterial(s.sprite);
                if (mat == null) continue;
                float h = s.top - s.bottom;
                var centre = s.pos + Vector3.up * (s.bottom + h * 0.5f) + face * Vector3.right * (s.w * 0.5f - s.offX);
                billboards.Add(quad, 0, mat, Matrix4x4.TRS(centre, face, new Vector3(s.w, h, 1)));
            }
        }

        static bool IsWave(FeatureDef d) =>
            (d.Name ?? "").IndexOf("wave", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            (d.SequenceName ?? "").IndexOf("wave", System.StringComparison.OrdinalIgnoreCase) >= 0;

        readonly List<Mesh> drapes = new List<Mesh>();

        // A grid over the sprite's rectangle, each vertex a hair above the ground.
        Mesh Drape(FeatureState f)
        {
            const int N = 8;
            float x0 = f.Position.x - f.SpriteOffsetX, w = f.SpriteWidth;
            float half = (f.SpriteTop - f.SpriteBottom) * 0.5f;
            float zNorth = f.Position.z + half, depth = half * 2;
            var v = new Vector3[(N + 1) * (N + 1)];
            var uv = new Vector2[v.Length];
            var n = new Vector3[v.Length];
            var c = new Color32[v.Length];
            for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    float x = x0 + w * i / N, z = zNorth - depth * j / N;
                    int k = j * (N + 1) + i;
                    v[k] = new Vector3(x, backend.GroundHeight(x, z) + 0.04f, z);
                    uv[k] = new Vector2((float)i / N, 1f - (float)j / N);
                    n[k] = Vector3.up;
                    c[k] = new Color32(255, 255, 255, 255);
                }
            var t = new List<int>();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = j * (N + 1) + i, b = a + 1, d = a + N + 1, e = d + 1;
                    t.Add(a); t.Add(b); t.Add(e);
                    t.Add(a); t.Add(e); t.Add(d);
                }
            var mesh = new Mesh { name = "flat sprite", hideFlags = HideFlags.DontSave, vertices = v, uv = uv, normals = n, colors32 = c };
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            drapes.Add(mesh);
            owned.Add(mesh);
            return mesh;
        }

        Material SpriteMaterial(int sprite)
        {
            if (spriteMats.TryGetValue(sprite, out var m)) return m;
            var tex = UI.UiKit.ToTexture(backend.Sprite(sprite), true);
            if (tex != null) owned.Add(tex);
            m = tex != null ? Keep(Looks.Model(tex)) : null;
            spriteMats[sprite] = m;
            return m;
        }

        // ---- Meshes ----

        // A unit quad in the xy plane, facing -z, pivot at x = 0.5 - pivot.
        static Mesh Quad(float pivot)
        {
            float x0 = -pivot, x1 = 1f - pivot;
            var m = new Mesh { name = "quad" };
            m.vertices = new[] { new Vector3(x0, -0.5f, 0), new Vector3(x1, -0.5f, 0), new Vector3(x1, 0.5f, 0), new Vector3(x0, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        // A unit square lying on the ground, centred, facing up.
        static Mesh FlatQuad()
        {
            var m = new Mesh { name = "flat" };
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return m;
        }

        static Mesh Ring(float inner, float outer, int segments)
        {
            var v = new List<Vector3>();
            var c = new List<Color32>();
            var t = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v.Add(d * inner); v.Add(d * outer);
                c.Add(new Color32(255, 255, 255, 255)); c.Add(new Color32(255, 255, 255, 255));
                if (i < segments)
                {
                    int b = i * 2;
                    t.Add(b); t.Add(b + 1); t.Add(b + 3);
                    t.Add(b); t.Add(b + 3); t.Add(b + 2);
                }
            }
            var m = new Mesh { name = "ring" };
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            return m;
        }

        static Mesh Box(Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = go.GetComponent<MeshFilter>().sharedMesh;
            var m = Object.Instantiate(src);
            Looks.Release(go);
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], size);
            m.vertices = v;
            var cols = new Color32[v.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
            m.colors32 = cols;
            m.RecalculateBounds();
            return m;
        }

        public void Dispose()
        {
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            spriteMats.Clear();
        }
    }
}
