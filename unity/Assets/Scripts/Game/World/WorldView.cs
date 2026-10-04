// WorldView.cs - everything drawn for one loaded game: terrain and sea,
// sky and weather, the camera, and the units, features and projectiles.
// Built a step at a time behind the loading screen, torn down when the game ends.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class WorldView
    {
        public GameObject Root { get; private set; }
        public TerrainView Terrain { get; } = new TerrainView();
        public Atmosphere Atmosphere { get; } = new Atmosphere();
        public ModelCache Models { get; private set; }
        public EntityRenderer Entities { get; private set; }
        public EffectRenderer Effects { get; private set; }
        public FogView Fog { get; private set; }
        // The battle's craters and marks on the ground.
        public ScarMap Scars { get; private set; }
        string climate = "";
        public GameCamera Camera { get; private set; }
        readonly IGameBackend backend;

        public WorldView(IGameBackend backend) => this.backend = backend;

        // How long each part of the last build took, and its slowest step, for the log.
        public string BuildTimes
        {
            get
            {
                var text = new System.Text.StringBuilder();
                foreach (var part in partOrder)
                    text.Append(text.Length > 0 ? ", " : "").Append(part).Append(' ').Append(Seconds(partMs[part])).Append(" s");
                if (slowestPart != null) text.Append($"; the slowest step was {slowestMs:0} ms, in the {slowestPart}");
                return text.ToString();
            }
        }

        static string Seconds(double ms) => (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        // The build in steps, each named by its part: the land, the edge
        // ring, the sea, the sky, the effects, the camera, the units' models
        // and the scenery. BuildSome runs a slice of them a frame, so the
        // loading screen keeps drawing. A step is true once done, and false
        // to be called again next frame.
        readonly System.Collections.Generic.Queue<(string part, System.Func<bool> step)> building = new System.Collections.Generic.Queue<(string, System.Func<bool>)>();
        readonly System.Collections.Generic.List<string> partOrder = new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.Dictionary<string, double> partMs = new System.Collections.Generic.Dictionary<string, double>();
        double slowestMs;
        string slowestPart;

        public const string ModelsPart = "unit models", SceneryPart = "scenery", BreakingPart = "scenery breaking";
        // About how long a call of the scenery's step builds features.
        public const double ScenerySliceMs = 20;
        public int StepsLeft => building.Count;
        // The part the next step builds, or null once the world is built.
        public string Part => building.Count > 0 ? building.Peek().part : null;
        public bool BuildingLand => Part == TerrainView.LandPart || Part == TerrainView.EdgePart || Part == TerrainView.SeaPart;
        public int ModelCount { get; private set; }

        // now false leaves the steps to BuildSome.
        public void Build(MapInfo map, GameOptions options, bool now = true)
        {
            FxQuality.Use(options.EffectsQuality);
            building.Clear();
            partOrder.Clear();
            partMs.Clear();
            slowestMs = 0;
            slowestPart = null;
            Root = new GameObject("World");
            foreach (var step in Terrain.Steps(backend, Root.transform)) building.Enqueue(step);
            building.Enqueue(("sky", () => { Sky(map, options); return true; }));
            building.Enqueue(("unit drawing", () => { Models = new ModelCache(backend); Entities = new EntityRenderer(backend, Models); return true; }));
            building.Enqueue(("effects and fog", () => { EffectsAndFog(); return true; }));
            building.Enqueue(("camera", () => { Frame(options); return true; }));
            QueueWarm();
            // The battle's first frame would build every feature at once.
            building.Enqueue((SceneryPart, () => Entities.WarmFeatures(ScenerySliceMs)));
            // Each kind of scenery that can break is split into its chunks.
            building.Enqueue((BreakingPart, () => Entities.WarmBreaking(ScenerySliceMs)));
            if (now) while (!BuildSome(double.MaxValue)) System.Threading.Thread.Yield();
        }

        // Runs steps for about budgetMs, at least one, and stops early at a
        // step that is not done. True once none are left.
        public bool BuildSome(double budgetMs)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double last = 0;
            while (building.Count > 0)
            {
                var (part, step) = building.Peek();
                bool done = step();
                double now = clock.Elapsed.TotalMilliseconds, took = now - last;
                last = now;
                if (!partMs.ContainsKey(part)) { partOrder.Add(part); partMs[part] = 0; }
                partMs[part] += took;
                if (took > slowestMs) { slowestMs = took; slowestPart = part; }
                if (!done) break;
                building.Dequeue();
                if (now >= budgetMs) break;
            }
            return building.Count == 0;
        }

        void Sky(MapInfo map, GameOptions options)
        {
            climate = map != null ? map.Climate ?? "" : "";
            var size = backend.Terrain.Size;
            Atmosphere.Build(Root.transform, map != null ? map.Climate : "", options.Weather, options.Shadows, Mathf.Max(size.x, size.y));
            Terrain.SetSeaClimate(map != null ? map.Climate : "");
        }

        void EffectsAndFog()
        {
            Effects = new EffectRenderer(backend, Models);
            Fog = new FogView(backend);
            Fog.Update(true);
            Entities.Hidden = u => !Fog.InSight(u.Position) && !Friendly(u.Player);
            // Impacts show in sight, and shots also when a friend fired them.
            Effects.Hidden = (at, player) => !Fog.InSight(at) && (player < 0 || !Friendly(player));
            Effects.Warm(backend.WarmEffectStrips());
            Scars = ScarMap.Begin(backend, Terrain, climate, FxQuality.Current.Level);
            Entities.Unseen = p => Fog.State(p) == 0;
        }

        void Frame(GameOptions options)
        {
            var size = backend.Terrain.Size;
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<UnityEngine.Camera>();
                cam.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 1500f;
            cam.fieldOfView = 40f;
            cam.depthTextureMode |= DepthTextureMode.Depth;
            WaterView.Prepare(cam, Terrain.Sea != null);
            Camera = cam.GetComponent<GameCamera>();
            if (Camera == null) Camera = cam.gameObject.AddComponent<GameCamera>();
            Camera.enabled = true;
            Camera.ground = backend.GroundHeight;
            Camera.Frame(new Vector2(0, -size.y), new Vector2(size.x, 0), StartFocus());
            Atmosphere.SetPostEffects(options.PostEffects, cam);
            Fog.Camera = cam;
            Fog.Update(true);
        }

        // Over the local player's first unit, or the map centre.
        Vector3 StartFocus()
        {
            var units = new UnitState[EntityRenderer.MaxUnits];
            int n = backend.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Player == backend.LocalPlayer) return units[i].Position;
            var s = backend.Terrain.Size;
            return new Vector3(s.x / 2, 0, -s.y / 2);
        }

        bool Friendly(int player) => backend.Allied(player, backend.LocalPlayer);

        // The models of every unit on the field and of everything the
        // players' units can build, and the flyers' clips, are built while
        // the loading screen is up, so a unit seen for the first time does
        // not stall a frame. A model's materials come a step each first.
        void QueueWarm()
        {
            int before = building.Count;
            var units = new UnitState[EntityRenderer.MaxUnits];
            int n = backend.ReadUnits(units);
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < n; i++)
            {
                int unitDef = units[i].Def, unitModel = units[i].Model;
                building.Enqueue((ModelsPart, () =>
                {
                    if (Models.WarmMaterial(unitModel)) return false;
                    Models.Get(unitModel);
                    Entities.WarmFlight(unitDef, unitModel);
                    return true;
                }));
                var def = backend.UnitDefs[unitDef];
                int colour = backend.PlayerById(units[i].Player)?.Colour ?? 0;
                foreach (int o in def.BuildOptions)
                {
                    if (o < 0 || o >= backend.UnitDefs.Count || !seen.Add((o, colour))) continue;
                    int option = o;
                    int? model = null;
                    building.Enqueue((ModelsPart, () =>
                    {
                        model ??= backend.LoadModel(backend.UnitDefs[option].ObjectName, colour);
                        if (Models.WarmMaterial(model.Value)) return false;
                        Models.Get(model.Value);
                        Entities.WarmFlight(option, model.Value);
                        return true;
                    }));
                }
            }
            ModelCount = building.Count - before;
        }

        public void Render()
        {
            if (Root == null || Camera == null) return;
            var cam = Camera.GetComponent<UnityEngine.Camera>();
            // Shadows reach a little past what the camera frames, so the
            // cascades spend their texels where the eye is.
            using (new Unity.Profiling.ProfilerMarker("Oku.Shadows").Auto()) Looks.ShadowDistance(Mathf.Clamp(Camera.distance * 2.4f + 25f, 50f, 260f));
            using (new Unity.Profiling.ProfilerMarker("Oku.Fog").Auto()) Fog.Update();
            // Before the units, so they sit in this frame's craters.
            using (new Unity.Profiling.ProfilerMarker("Oku.Scars").Auto()) Scars?.Update(backend.Tick / (float)Mathf.Max(1, backend.TicksPerSecond));
            using (new Unity.Profiling.ProfilerMarker("Oku.Entities").Auto()) Entities.Render(cam);
            // An edit in the map editor can make a sea where there was none.
            WaterView.Prepare(cam, Terrain.Sea != null);
            // After the sea's camera needs, since soft effects add their own.
            using (new Unity.Profiling.ProfilerMarker("Oku.Effects").Auto()) Effects.Render(cam);
            using (new Unity.Profiling.ProfilerMarker("Oku.Water").Auto()) Terrain.Sea?.Update(Atmosphere, Entities, backend, cam);
            Atmosphere.Follow(Camera.focus, Camera.transform.position.y - Camera.focus.y, Camera.distance);
        }

        public void Dispose()
        {
            building.Clear();
            Entities?.Dispose();
            Effects?.Dispose();
            Scars?.Dispose();
            Scars = null;
            Fog?.Dispose();
            Models?.Dispose();
            Terrain.Dispose();
            Atmosphere.Dispose();
            if (Camera != null)
            {
                // The camera outlives the game: no copies for a sea that is gone.
                WaterView.Prepare(Camera.GetComponent<UnityEngine.Camera>(), false);
                Camera.enabled = false;
            }
            if (Root != null) Looks.Release(Root);
            Root = null;
        }
    }
}
