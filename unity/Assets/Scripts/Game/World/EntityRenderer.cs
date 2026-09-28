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

        // Winged flyers' animators by StableId, and how far each flyer is
        // drawn above its engine position this frame, by handle.
        struct FlightEntry { public Flyer F; public float Seen; }
        readonly Dictionary<uint, FlightEntry> flyers = new Dictionary<uint, FlightEntry>();
        readonly Dictionary<int, float> lifts = new Dictionary<int, float>();
        readonly List<uint> stale = new List<uint>();
        FlightType[] flightTypes;
        FlightRig[] flightRigs;
        bool[] flightKnown, rigKnown;
        int flightVersion = -1;
        float nextSweep;
        // The camera's view, so a flyer out of it is stepped but not posed.
        readonly Plane[] frustum = new Plane[6];
        bool haveFrustum;
        // The simulation's clock in seconds, set before each Render so wings
        // stop when the game pauses and follow its speed. NaN falls back to
        // the tick count.
        public double SimSeconds = double.NaN;
        double lastSim = double.NaN;
        float simDt, simNow;
        // A unit whose drawn piece matrices are kept each frame, for tests.
        public int Watch = -1;
        public readonly Matrix4x4[] Watched = new Matrix4x4[MaxPieces];
        public int WatchedCount { get; private set; }
        public Vector3 WatchedRing { get; private set; }
        // Flyers posed by the animator this frame.
        public int FlyersPosed { get; private set; }

        public float VisualLift(int handle) => lifts.TryGetValue(handle, out var v) ? v : 0f;

        public bool TryFlight(uint stableId, out Flyer f)
        {
            bool ok = flyers.TryGetValue(stableId, out var e);
            f = e.F;
            return ok;
        }

        readonly Mesh quad, ring, shaft, barQuad;
        readonly Material ringMat, barBack, barGood, barMid, barLow, shaftMat;
        // Each feature as drawn, rebuilt only when what it is changes.
        readonly List<FeatureEntry> featureEntries = new List<FeatureEntry>();

        // A building being placed: its model at rest and its footprint,
        // green where it can stand and red where it cannot.
        public struct GhostState
        {
            public int Def;
            public Vector3 At;
            public bool Ok;
            public int Facing;          // quarter turns clockwise
            public float ShakeFrom;     // when a turn was refused
        }
        public GhostState? Ghost;
        // Sites already placed with Shift, waiting for their builder.
        public List<GhostState> QueuedGhosts;
        float ghostAngle;
        int ghostDef = -1;

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

        // Ground never seen, where no feature is drawn.
        public System.Func<Vector3, bool> Unseen;
        readonly bool[] featureHidden = new bool[MaxFeatures];

        public bool FeatureHidden(int index) => index >= 0 && index < featureHidden.Length && featureHidden[index];
        Mesh flat;
        Material ghostGood, ghostBad, barMana;

        // Lines from selected units to where their orders take them.
        Material lineMove, lineAttack, lineBuild, linePatrol;
        readonly Dictionary<int, Vector3> positions = new Dictionary<int, Vector3>();

        void AddOrderLines()
        {
            if (Selected.Count == 0 || Selected.Count > 60) return;
            positions.Clear();
            for (int i = 0; i < UnitCount; i++) positions[Units[i].Handle] = Units[i].Position + Vector3.up * VisualLift(Units[i].Handle);
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

        // The build preview as the original draws it: the building itself
        // at the snapped site, its colours mixed half way to green where it
        // can stand or red where it cannot, see-through, casting nothing.
        readonly Dictionary<(Material, bool), Material> ghostMats = new Dictionary<(Material, bool), Material>();

        Material GhostMaterial(Material from, bool ok)
        {
            if (ghostMats.TryGetValue((from, ok), out var m)) return m;
            m = Keep(new Material(Looks.Find("OkuGhost", "Sprites/Default")) { hideFlags = HideFlags.DontSave, enableInstancing = true });
            m.mainTexture = from.mainTexture;
            m.SetColor("_Tint", ok ? new Color(60 / 255f, 220 / 255f, 90 / 255f) : new Color(200 / 255f, 60 / 255f, 60 / 255f));
            ghostMats[(from, ok)] = m;
            return m;
        }

        void AddGhost()
        {
            if (QueuedGhosts != null)
                foreach (var q in QueuedGhosts) AddGhostAt(q, q.Facing * 90f, false);
            if (Ghost == null) { ghostDef = -1; return; }
            var g = Ghost.Value;
            // The turn eases to the new facing over a fraction of a second.
            float target = g.Facing * 90f;
            if (g.Def != ghostDef) { ghostDef = g.Def; ghostAngle = target; }
            ghostAngle = Mathf.MoveTowardsAngle(ghostAngle, target, Mathf.Max(90f, Mathf.Abs(Mathf.DeltaAngle(ghostAngle, target)) * 12f) * Time.unscaledDeltaTime);
            // A refused turn shakes the ghost briefly.
            float since = Time.unscaledTime - g.ShakeFrom;
            if (since < 0.35f)
            {
                var cam = Camera.main;
                var side = cam != null ? cam.transform.right : Vector3.right;
                g.At += side * Mathf.Sin(since * 60f) * 0.25f * (1f - since / 0.35f);
            }
            AddGhostAt(g, ghostAngle, true);
        }

        void AddGhostAt(GhostState g, float angle, bool live)
        {
            if (g.Def < 0 || g.Def >= backend.UnitDefs.Count) return;
            var def = backend.UnitDefs[g.Def];
            var turn = Quaternion.Euler(0, angle, 0);
            var fp = new Vector3(Mathf.Max(1, def.Footprint.x), 1, Mathf.Max(1, def.Footprint.y));
            overlay.Add(flat, 0, g.Ok ? ghostGood : ghostBad, Matrix4x4.TRS(g.At + Vector3.up * 0.06f, turn, fp));
            foreach (var part in GhostParts(g, angle))
                overlay.Add(part.mesh, part.sub, GhostMaterial(part.mat, g.Ok), part.m);
        }

        // The pieces of a building as it will stand, at the building's own
        // scale and height, turned by its facing: the model's rest pose from
        // its piece offsets, the script's alternate pieces (*_off, *_dead)
        // left out as the engine hides them on a new building.
        public List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> GhostParts(GhostState g, float angle)
        {
            var list = new List<(Mesh, int, Material, Matrix4x4)>();
            var def = backend.UnitDefs[g.Def];
            int id = backend.LoadModel(def.ObjectName, backend.PlayerById(backend.LocalPlayer)?.Colour ?? 0);
            var model = models.Get(id);
            if (model == null) return list;
            var d = model.Data;
            var at = Matrix4x4.TRS(g.At + Vector3.up * SiteLift(g.At), Quaternion.Euler(0, angle, 0), Vector3.one);
            var card = CardOverride.For(def.ObjectName);
            if (card != null)
                foreach (var part in card.Model.Parts) list.Add((part.Mesh, part.Submesh, part.Material, at * part.NodeToRoot));
            for (int p = 0; p < model.Pieces.Length; p++)
            {
                string name = d.Pieces[p].Name ?? "";
                if (name.EndsWith("_off") || name.EndsWith("_dead") || model.Pieces[p] == null) continue;
                if (card != null && card.Hides(name)) continue;
                var r = Matrix4x4.Translate(d.Pieces[p].Offset * d.Scale);
                for (int q = d.Pieces[p].Parent; q >= 0; q = d.Pieces[q].Parent) r = Matrix4x4.Translate(d.Pieces[q].Offset * d.Scale) * r;
                for (int s = 0; s < model.Materials[p].Length; s++) list.Add((model.Pieces[p], s, model.Materials[p][s], at * r));
            }
            return list;
        }

        // Where the ghost's pieces reach, in world space.
        public Bounds GhostBounds(GhostState g) => BoundsOf(GhostParts(g, g.Facing * 90f));

        // Where a unit's drawn pieces reach, in world space.
        public Bounds UnitBounds(int handle)
        {
            var list = new List<(Mesh, int, Material, Matrix4x4)>();
            for (int i = 0; i < UnitCount; i++)
            {
                if (Units[i].Handle != handle) continue;
                var model = models.Get(Units[i].Model);
                if (model == null) break;
                int n = Mathf.Min(backend.ReadUnitPose(handle, poses), model.Pieces.Length);
                var def = backend.UnitDefs[Units[i].Def];
                float lift = def.IsBuilding ? SiteLift(Units[i].Position) : 0f;
                var card = CardOverride.For(def.ObjectName);
                for (int p = 0; p < n; p++)
                    if (model.Pieces[p] != null && !poses[p].Hidden && (card == null || !card.Hides(model.Data.Pieces[p].Name)))
                        list.Add((model.Pieces[p], 0, null, Matrix4x4.Translate(Vector3.up * lift) * poses[p].Matrix * model.Unscale));
                if (card != null)
                {
                    var u = Units[i];
                    var at = Matrix4x4.TRS(u.Position + Vector3.up * lift, Quaternion.Euler(u.Pitch, u.Heading - 180f, u.Roll), Vector3.one);
                    foreach (var part in card.Model.Parts) list.Add((part.Mesh, part.Submesh, part.Material, at * part.NodeToRoot));
                }
            }
            return BoundsOf(list);
        }

        static Bounds BoundsOf(List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> parts)
        {
            bool first = true;
            var b = new Bounds();
            foreach (var part in parts)
            {
                var mb = part.mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    var w = part.m.MultiplyPoint3x4(c);
                    if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                }
            }
            return b;
        }

        // How far a building on a lodestone site stands up: the top of the
        // site's drop-in plinth ("standTop" in its glTF extras), else 0.
        readonly List<(Vector3 at, float top)> sites = new List<(Vector3, float)>();

        public float SiteLift(Vector3 at)
        {
            foreach (var s in sites)
                if ((new Vector2(s.at.x - at.x, s.at.z - at.z)).sqrMagnitude < 1.5f * 1.5f) return s.top;
            return 0f;
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
            barMana = Keep(Looks.Overlay(new Color(0.35f, 0.6f, 1f, 1f)));
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

            double now = double.IsNaN(SimSeconds) ? backend.Tick / (double)Mathf.Max(1, backend.TicksPerSecond) : SimSeconds;
            simDt = double.IsNaN(lastSim) ? 0f : (float)(now - lastSim);
            lastSim = now;
            simNow = (float)now;
            lifts.Clear();
            FlyersPosed = 0;
            haveFrustum = cam != null;
            if (haveFrustum) GeometryUtility.CalculateFrustumPlanes(cam, frustum);

            UnitCount = backend.ReadUnits(Units);
            DrawnSize.Clear();
            rising.Clear();
            blocksUsed = 0;
            buildSeen.Clear();
            for (int i = 0; i < UnitCount; i++) AddUnit(ref Units[i], cam);
            SweepFlyers();
            risingBlock = -1;
            if (buildShown.Count > buildSeen.Count)
                foreach (var h in new List<int>(buildShown.Keys)) if (!buildSeen.Contains(h)) buildShown.Remove(h);

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
            DrawRising();
            billboards.Draw();
            overlay.Draw();
        }

        // Each unit drawn this frame and how big it is: height and radius in
        // world units. Picking uses it, so a unit is picked where it shows.
        public readonly Dictionary<int, Vector2> DrawnSize = new Dictionary<int, Vector2>();

        public bool IsDrawn(int handle) => DrawnSize.ContainsKey(handle);

        // ---- Frames under construction ----
        // The original draws nothing of a frame under half built. Here a
        // frame rises from the ground with its build, from nothing: solid
        // up to a cut that follows the build smoothly, a glowing edge at
        // the cut and a faint ghost of the rest in team colour.
        public static float GhostShows = 1f;
        public const float BuildEase = 0.25f;     // seconds to catch up with a read
        readonly Dictionary<int, float> buildShown = new Dictionary<int, float>();
        readonly HashSet<int> buildSeen = new HashSet<int>();
        readonly List<(Mesh mesh, int sub, Material mat, Matrix4x4 m, int block)> rising = new List<(Mesh, int, Material, Matrix4x4, int)>();
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        readonly Dictionary<Material, Material> buildMats = new Dictionary<Material, Material>();
        int blocksUsed;
        int risingBlock = -1;
        static readonly int BuildCutId = Shader.PropertyToID("_BuildCut");
        static readonly int BuildBandId = Shader.PropertyToID("_BuildBand");
        static readonly int BuildTintId = Shader.PropertyToID("_BuildTint");
        static readonly int BuildGlowId = Shader.PropertyToID("_BuildGlow");

        // How built a frame shows now, 0 to 1, or 1 for one not being built.
        public float BuildShown(int handle) => buildShown.TryGetValue(handle, out var f) ? f : 1f;

        // Where the next parts go: the instanced batch, or, for a frame
        // being built, its own draws with its cut.
        void Put(Mesh mesh, int sub, Material mat, in Matrix4x4 m)
        {
            if (risingBlock < 0) { solid.Add(mesh, sub, mat, m); return; }
            var mm = m;
            if (mm.determinant < 0) { mesh = InstancedDraws.Mirrored(mesh); mm = m * Matrix4x4.Scale(new Vector3(-1, 1, 1)); }
            rising.Add((mesh, sub, BuildMaterial(mat), mm, risingBlock));
        }

        Material BuildMaterial(Material m)
        {
            if (m == null) return null;
            if (buildMats.TryGetValue(m, out var b)) return b;
            b = new Material(m) { name = m.name + " (rising)", hideFlags = HideFlags.DontSave };
            b.EnableKeyword("_OKU_BUILD");
            b.enableInstancing = false;
            owned.Add(b);
            return buildMats[m] = b;
        }

        // Starts a frame's draws with a cut at world height cut.
        void BeginRising(float cut, float band, Color tint, float glow)
        {
            if (blocksUsed == blocks.Count) blocks.Add(new MaterialPropertyBlock());
            var block = blocks[blocksUsed];
            block.Clear();
            block.SetFloat(BuildCutId, cut);
            block.SetFloat(BuildBandId, band);
            block.SetColor(BuildTintId, tint);
            block.SetFloat(BuildGlowId, glow);
            risingBlock = blocksUsed++;
        }

        void DrawRising()
        {
            Shader.SetGlobalFloat("_OkuBuildGhost", GhostShows);
            foreach (var r in rising)
            {
                if (r.mat == null) continue;
                var rp = new RenderParams(r.mat)
                {
                    matProps = blocks[r.block], shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true,
                    worldBounds = new Bounds(r.m.GetColumn(3), Vector3.one * 50f),
                };
                Graphics.RenderMesh(rp, r.mesh, r.sub, r.m);
            }
        }

        // The build shown for a frame, easing toward the latest read.
        float EaseBuild(int handle, float read)
        {
            buildSeen.Add(handle);
            if (!buildShown.TryGetValue(handle, out var shown)) shown = read;
            shown += (read - shown) * (1f - Mathf.Exp(-Time.unscaledDeltaTime / BuildEase));
            buildShown[handle] = shown;
            return shown;
        }

        void AddUnit(ref UnitState u, Camera cam)
        {
            if (Hidden != null && Hidden(u)) return;
            risingBlock = -1;
            var def = u.Def >= 0 && u.Def < backend.UnitDefs.Count ? backend.UnitDefs[u.Def] : null;
            var model = models.Get(u.Model, OverrideKind.Unit, def != null ? new[] { def.Name, def.ObjectName } : null);
            if (model != null && (u.Flags & UnitFlags.Building) != 0 && (u.Flags & UnitFlags.Dying) == 0)
            {
                float shown = EaseBuild(u.Handle, Mathf.Clamp01(u.BuildProgress));
                var rb = model.RestBounds;
                float lift = def != null && def.IsBuilding ? SiteLift(u.Position) : 0f;
                float bottom = u.Position.y + lift + Mathf.Min(0f, rb.min.y), top = u.Position.y + lift + rb.max.y;
                var owner = backend.PlayerById(u.Player);
                Color tint = owner != null ? (Color)owner.Tint : new Color(0.8f, 0.8f, 0.8f);
                // The glow grows in over the first few percent, so a site
                // just begun is only its ghost.
                BeginRising(Mathf.Lerp(bottom, top, shown), 0.1f + 0.05f * (top - bottom), tint, Mathf.SmoothStep(0f, 1f, shown / 0.06f));
            }
            float height = 1.5f, radius = 0.6f, air = 0f;
            if (model != null && model.Override != null)
            {
                int n = Pose(u, def, model, out air);
                AddOverride(model, n);
            }
            else if (model != null)
            {
                int n = Pose(u, def, model, out air);
                // A unit that is mostly a painted card draws its 3D model in
                // place of the card, facing with the unit, on any plinth.
                var card = def != null ? CardOverride.For(def.ObjectName) : null;
                if (card != null)
                {
                    var at = Matrix4x4.TRS(u.Position + Vector3.up * (def.IsBuilding ? SiteLift(u.Position) : 0f),
                        Quaternion.Euler(u.Pitch, u.Heading - 180f, u.Roll), Vector3.one);
                    foreach (var part in card.Model.Parts) Put(part.Mesh, part.Submesh, part.Material, at * part.NodeToRoot);
                }
                for (int p = 0; p < n; p++)
                {
                    var mesh = model.Pieces[p];
                    if (mesh == null || poses[p].Hidden) continue;
                    if (card != null && card.Hides(model.Data.Pieces[p].Name)) continue;
                    var mats = model.Materials[p];
                    for (int s = 0; s < mats.Length; s++) Put(mesh, s, mats[s], posed[p]);
                }
                var b = model.RestBounds;
                height = Mathf.Max(Mathf.Abs(b.max.y), Mathf.Abs(b.min.y)) + 0.4f;
                radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * 0.9f, 0.4f, 6f);
            }
            DrawnSize[u.Handle] = new Vector2(height, radius);
            risingBlock = -1;

            if ((u.Flags & UnitFlags.Dying) != 0) return;
            bool selected = Selected.Contains(u.Handle);
            var ground = u.Position + Vector3.up * (0.05f + air);
            if (u.Handle == Watch) WatchedRing = ground;
            if (selected)
                overlay.Add(ring, 0, ringMat, Matrix4x4.TRS(ground, Quaternion.identity, new Vector3(radius, 1, radius)));
            if (selected || u.Health < u.MaxHealth)
            {
                float f = u.MaxHealth > 0 ? Mathf.Clamp01((float)u.Health / u.MaxHealth) : 1f;
                var face = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
                var top = u.Position + Vector3.up * (height + air);
                const float w = 1.2f, h = 0.14f;
                var left = top - cam.transform.right * (w * 0.5f);
                overlay.Add(barQuad, 0, barBack, Matrix4x4.TRS(left, face, new Vector3(w, h, 1)));
                var fill = f > 0.6f ? barGood : f > 0.3f ? barMid : barLow;
                overlay.Add(barQuad, 0, fill, Matrix4x4.TRS(left - cam.transform.forward * 0.01f, face, new Vector3(w * f, h, 1)));
                if (u.MaxMana > 0)
                {
                    // A caster's own mana, in blue under its health.
                    float mf = Mathf.Clamp01((float)u.Mana / u.MaxMana);
                    var below = left - cam.transform.up * (h * 1.3f);
                    overlay.Add(barQuad, 0, barBack, Matrix4x4.TRS(below, face, new Vector3(w, h * 0.8f, 1)));
                    overlay.Add(barQuad, 0, barMana, Matrix4x4.TRS(below - cam.transform.forward * 0.01f, face, new Vector3(w * mf, h * 0.8f, 1)));
                }
            }
        }

        // The unit's pieces in posed[], piece space to world: the script's
        // pose, any plinth, a flyer's wings and the animation nudges.
        // Returns the piece count and how far a flyer is drawn lifted.
        int Pose(in UnitState u, UnitDef def, PresentedModel model, out float air)
        {
            int n = Mathf.Min(Mathf.Min(backend.ReadUnitPose(u.Handle, poses), model.Pieces.Length), MaxPieces);
            // A building on a site with a plinth stands on the plinth.
            var lift = def != null && def.IsBuilding ? Matrix4x4.Translate(Vector3.up * SiteLift(u.Position)) : Matrix4x4.identity;
            for (int p = 0; p < n; p++) posed[p] = lift * poses[p].Matrix * model.Unscale;
            air = Fly(u, def, model, n);
            // Nudges from the animation editor, for every animation and
            // for the script function driving the unit now.
            var nudges = def != null ? AnimOverride.Load(def.ObjectName) : null;
            nudges?.Apply(model.Data.Pieces, posed, n, backend.UnitAnimation(u.Handle));
            if (u.Handle == Watch) { System.Array.Copy(posed, Watched, n); WatchedCount = n; }
            return n;
        }

        // A drop-in model: parts named like a piece follow that piece, the
        // rest ride the root. Overrides are in cells, so the model's own
        // unit scale comes off first.
        void AddOverride(PresentedModel model, int n)
        {
            var d = model.Data;
            int root = 0;
            for (int i = 0; i < d.Pieces.Length; i++) if (d.Pieces[i].Parent < 0) { root = i; break; }
            if (n <= root) return;
            var basis = posed[root] * model.RestInverse[root];
            foreach (var part in model.Override.Parts)
            {
                Matrix4x4 m;
                if (part.Piece >= 0 && part.Piece < n)
                {
                    if (poses[part.Piece].Hidden) continue;
                    m = posed[part.Piece] * model.RestInverse[part.Piece] * part.NodeToRoot;
                }
                else m = basis * part.NodeToRoot;
                Put(part.Mesh, part.Submesh, part.Material, m);
            }
        }

        // Steps a winged flyer's animator and, in view, poses it in posed[].
        // Returns how far the flyer is drawn above its engine position.
        float Fly(in UnitState u, UnitDef def, PresentedModel model, int n)
        {
            var type = FlightTypeOf(u.Def, def);
            if (type == null) return 0f;
            var rig = RigOf(u.Def, u.Model, type, model.Data);
            var input = FlightInput.Of(u, def);
            // Only an attacking unit is asked which function poses it.
            input.Attacking = (u.Flags & UnitFlags.Attacking) != 0 &&
                              backend.UnitAnimation(u.Handle).StartsWith("attack", System.StringComparison.OrdinalIgnoreCase);
            if (!flyers.TryGetValue(u.StableId, out var e)) e.F = FlightAnimator.Start(u.StableId, type, input, rig);
            FlightAnimator.Step(ref e.F, input, type, simDt, rig);
            e.Seen = Time.unscaledTime;
            flyers[u.StableId] = e;
            float offset = FlightAnimator.VisualOffset(e.F, input);
            var b = model.RestBounds;
            if (InView(u.Position + Vector3.up * (offset + b.center.y), b.extents.magnitude + 2f))
            {
                FlightPose.Apply(e.F, type, rig, model.Data, posed, n, offset, simNow);
                FlyersPosed++;
            }
            else FlightPose.Lift(posed, n, offset);
            if (offset != 0f) lifts[u.Handle] = offset;
            return offset;
        }

        bool InView(Vector3 at, float reach)
        {
            if (!haveFrustum) return true;
            for (int i = 0; i < frustum.Length; i++) if (frustum[i].GetDistanceToPoint(at) < -reach) return false;
            return true;
        }

        // The flight table's entry for a def, looked up once per def.
        FlightType FlightTypeOf(int index, UnitDef def)
        {
            if (def == null || index < 0) return null;
            if (flightTypes == null || flightVersion != FlightTable.Version || flightTypes.Length != backend.UnitDefs.Count)
            {
                flightTypes = new FlightType[backend.UnitDefs.Count];
                flightRigs = new FlightRig[flightTypes.Length];
                flightKnown = new bool[flightTypes.Length];
                rigKnown = new bool[flightTypes.Length];
                flightVersion = FlightTable.Version;
            }
            if (index >= flightTypes.Length) return null;
            if (!flightKnown[index])
            {
                flightTypes[index] = FlightTable.Load().Find(def.Name, def.ObjectName);
                flightKnown[index] = true;
            }
            return flightTypes[index];
        }

        // A def's flight clips from its own script, baked the first time
        // they are needed. Null where the table's poses stand in.
        FlightRig RigOf(int index, int model, FlightType type, ModelData data)
        {
            if (rigKnown[index]) return flightRigs[index];
            rigKnown[index] = true;
            var def = backend.UnitDefs[index];
            var flap = Sampler(def, model, type.Clip("flap", "fly"));
            string why = "its script has no " + type.Clip("flap", "fly");
            if (flap != null)
                flightRigs[index] = FlightRig.Bake(type, data, backend.TicksPerSecond, flap, Sampler(def, model, type.Clip("glide", "soar")), out why);
            if (flightRigs[index] == null) Debug.Log($"Flight: {def.Name} takes the table's poses, since {why}");
            return flightRigs[index];
        }

        FlightRig.Sampler Sampler(UnitDef def, int model, string function)
        {
            string name = System.Array.Find(def.Animations, a => string.Equals(a, function, System.StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            return (seconds, into) => backend.PoseModel(model, name, seconds, into);
        }

        // Bakes a flyer's clips while the loading screen is up.
        public void WarmFlight(int def, int model)
        {
            if (def < 0 || def >= backend.UnitDefs.Count) return;
            var type = FlightTypeOf(def, backend.UnitDefs[def]);
            var data = type != null ? models.Get(model)?.Data : null;
            if (data != null) RigOf(def, model, type, data);
        }

        // Forgets flyers not drawn for two seconds, checked once a second.
        void SweepFlyers()
        {
            float t = Time.unscaledTime;
            if (flyers.Count == 0 || t < nextSweep) return;
            nextSweep = t + 1f;
            stale.Clear();
            foreach (var kv in flyers) if (t - kv.Value.Seen > 2f) stale.Add(kv.Key);
            foreach (var id in stale) flyers.Remove(id);
        }

        // One feature as drawn, and what it was built from: its kind, model,
        // picture, place and heading. A corpse that sinks changes place, so
        // it is rebuilt each frame, and a feature that stays is built once.
        sealed class FeatureEntry
        {
            public int Def = int.MinValue, Model, Sprite;
            public Vector3 Position;
            public float Heading;
            public readonly List<(Mesh mesh, int sub, Material mat, Matrix4x4 m, bool flat)> Draws = new List<(Mesh, int, Material, Matrix4x4, bool)>();
            public bool Card;
            public float W, Bottom, Top, OffX;
            public Mesh Drape;
            public float SiteTop;

            public bool Same(in FeatureState f) =>
                f.Def == Def && f.Model == Model && f.Sprite == Sprite && f.Position == Position && f.Heading == Heading;
        }

        // Features rebuilt in the last frame.
        public int FeaturesRebuilt { get; private set; }

        // Whether a feature is drawn standing at p, to within in x and z.
        public bool FeatureDrawnAt(Vector3 p, float within) => !float.IsNaN(FeatureDrawnHeight(p, within));

        // The height a feature standing at p is drawn at, or NaN.
        public float FeatureDrawnHeight(Vector3 p, float within)
        {
            for (int i = 0; i < featureEntries.Count; i++)
            {
                var e = featureEntries[i];
                if (featureHidden[i] || (e.Draws.Count == 0 && !e.Card)) continue;
                if (Mathf.Abs(e.Position.x - p.x) > within || Mathf.Abs(e.Position.z - p.z) > within) continue;
                return e.Draws.Count > 0 && e.Draws[0].m != Matrix4x4.identity ? e.Draws[0].m.GetColumn(3).y : e.Position.y;
            }
            return float.NaN;
        }

        bool sitesStale;

        void AddFeatures(Camera cam)
        {
            FeaturesRebuilt = 0;
            int n = backend.ReadFeatures(features);
            bool seaOn = backend.Terrain != null && backend.Terrain.SeaLevel > 0;
            while (featureEntries.Count > n)
            {
                Forget(featureEntries[featureEntries.Count - 1]);
                featureEntries.RemoveAt(featureEntries.Count - 1);
                sitesStale = true;
            }
            while (featureEntries.Count < n) featureEntries.Add(new FeatureEntry());
            for (int i = 0; i < n; i++)
            {
                var e = featureEntries[i];
                if (e.Same(features[i])) continue;
                Build(e, features[i], seaOn);
                FeaturesRebuilt++;
                sitesStale = true;
            }
            if (sitesStale)
            {
                sites.Clear();
                foreach (var e in featureEntries) if (e.SiteTop > 0) sites.Add((e.Position, e.SiteTop));
                sitesStale = false;
            }
            for (int i = 0; i < n; i++)
            {
                featureHidden[i] = Unseen != null && Unseen(features[i].Position);
                if (featureHidden[i]) continue;
                var e = featureEntries[i];
                foreach (var d in e.Draws) (d.flat ? billboards : solid).Add(d.mesh, d.sub, d.mat, d.m);
                if (!e.Card) continue;
                var mat = SpriteMaterial(e.Sprite);
                if (mat != null) billboards.Add(quad, 0, mat, CardMatrix(e.Position, e.W, e.Bottom, e.Top, e.OffX, cam.transform));
            }
        }

        // Lets go of what a feature's entry holds.
        void Forget(FeatureEntry e)
        {
            e.Draws.Clear();
            e.Card = false;
            e.SiteTop = 0;
            if (e.Drape != null) { owned.Remove(e.Drape); Looks.Release(e.Drape); e.Drape = null; }
        }

        void Build(FeatureEntry e, in FeatureState f, bool seaOn)
        {
            Forget(e);
            e.Def = f.Def; e.Model = f.Model; e.Sprite = f.Sprite; e.Position = f.Position; e.Heading = f.Heading;
            var fdef = f.Def >= 0 && f.Def < backend.FeatureDefs.Count ? backend.FeatureDefs[f.Def] : null;
            var fnames = fdef != null ? new[] { fdef.Name, fdef.SequenceName, fdef.ObjectName } : new string[0];
            // Shoreline wave sprites give way to the sea's own foam.
            if (f.Model < 0 && fdef != null && seaOn && IsWave(fdef)) return;
            if (f.Model < 0 && f.Sprite >= 0)
            {
                // A sprite feature with a drop-in model draws the model.
                var over = OverrideLoader.Find(OverrideKind.Feature, null, fnames);
                if (over != null)
                {
                    if (over.StandTop > 0) e.SiteTop = over.StandTop;
                    var at = Matrix4x4.TRS(f.Position, Quaternion.Euler(0, f.Heading, 0), Vector3.one);
                    foreach (var part in over.Parts) e.Draws.Add((part.Mesh, part.Submesh, part.Material, at * part.NodeToRoot, part.Flat));
                    return;
                }
            }
            if (f.Model >= 0)
            {
                var model = models.Get(f.Model, OverrideKind.Feature, fnames);
                if (model == null) return;
                int pn = backend.ReadFeaturePose(f.Index, poses);
                if (model.Override != null)
                {
                    var basis = pn > 0 ? poses[0].Matrix * model.Unscale * model.RestInverse[0] : Matrix4x4.identity;
                    foreach (var part in model.Override.Parts) e.Draws.Add((part.Mesh, part.Submesh, part.Material, basis * part.NodeToRoot, part.Flat));
                    return;
                }
                for (int p = 0; p < pn && p < model.Pieces.Length; p++)
                {
                    if (model.Pieces[p] == null || poses[p].Hidden) continue;
                    var mats = model.Materials[p];
                    for (int s = 0; s < mats.Length; s++) e.Draws.Add((model.Pieces[p], s, mats[s], poses[p].Matrix * model.Unscale, false));
                }
            }
            else if (f.Sprite >= 0 && f.Flat)
            {
                // A flat sprite, such as a lodestone site, lies on the
                // ground and follows it, north up.
                var mat = SpriteMaterial(f.Sprite);
                if (mat == null) return;
                e.Drape = Drape(f);
                e.Draws.Add((e.Drape, 0, mat, Matrix4x4.identity, true));
            }
            else if (f.Sprite >= 0)
            {
                e.Card = true;
                e.W = f.SpriteWidth; e.Bottom = f.SpriteBottom; e.Top = f.SpriteTop; e.OffX = f.SpriteOffsetX;
            }
        }

        // How far a feature card is drawn toward the camera, shrunk to look
        // the same, so rising ground behind it does not cut it.
        public const float CardNudge = 0.5f;

        // A sprite feature's card, for the centred unit quad: in the camera's
        // plane and standing on its point, so it keeps its height at any tilt.
        public static Matrix4x4 CardMatrix(Vector3 pos, float w, float bottom, float top, float offX, Transform cam)
        {
            EffectRenderer.Nudged(pos, cam, CardNudge, out var pivot, out float k);
            float h = top - bottom;
            var centre = pivot + (cam.up * (bottom + h * 0.5f) + cam.right * (w * 0.5f - offX)) * k;
            return Matrix4x4.TRS(centre, cam.rotation, new Vector3(w * k, h * k, 1));
        }

        static bool IsWave(FeatureDef d) =>
            (d.Name ?? "").IndexOf("wave", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            (d.SequenceName ?? "").IndexOf("wave", System.StringComparison.OrdinalIgnoreCase) >= 0;

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
            flyers.Clear();
            flightTypes = null;
            flightRigs = null;
            FlightPose.Forget();
        }
    }
}
