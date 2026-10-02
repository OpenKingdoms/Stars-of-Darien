// WorldView.cs - everything drawn for one loaded game: terrain and sea,
// sky and weather, the camera, and the units, features and projectiles.
// Built when loading finishes and torn down when the game ends.
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
        public GameCamera Camera { get; private set; }
        readonly IGameBackend backend;

        public WorldView(IGameBackend backend) => this.backend = backend;

        // How long each part of the last Build took, for the log.
        public string BuildTimes { get; private set; } = "";

        // warmNow false leaves the unit models to WarmSome, a slice a
        // frame, so the loading screen keeps drawing while they build.
        public void Build(MapInfo map, GameOptions options, bool warmNow = true)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder();
            void Took(string part)
            {
                times.Append(times.Length > 0 ? ", " : "").Append(part).Append(' ').Append((clock.ElapsedMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" s");
                clock.Restart();
            }
            Root = new GameObject("World");
            Terrain.Build(backend, Root.transform);
            Took("terrain");
            var size = backend.Terrain.Size;
            Atmosphere.Build(Root.transform, map != null ? map.Climate : "", options.Weather, options.Shadows, Mathf.Max(size.x, size.y));
            Terrain.SetSeaClimate(map != null ? map.Climate : "");
            Models = new ModelCache(backend);
            Entities = new EntityRenderer(backend, Models);
            Took("sky");
            QueueWarm();
            if (warmNow)
            {
                WarmSome(double.MaxValue);
                Took("unit models");
            }
            Effects = new EffectRenderer(backend, Models);
            Fog = new FogView(backend);
            Fog.Update(true);
            Entities.Hidden = u => !Fog.InSight(u.Position) && !Friendly(u.Player);
            // Impacts show in sight, and shots also when a friend fired them.
            Effects.Hidden = (at, player) => !Fog.InSight(at) && (player < 0 || !Friendly(player));
            Effects.Warm(backend.WarmEffectStrips());
            Took("effects and fog");
            Entities.Unseen = p => Fog.State(p) == 0;

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
            Took("camera");
            BuildTimes = times.ToString();
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
        // not stall a frame.
        readonly System.Collections.Generic.Queue<System.Action> warming = new System.Collections.Generic.Queue<System.Action>();

        public int WarmLeft => warming.Count;

        void QueueWarm()
        {
            warming.Clear();
            var units = new UnitState[EntityRenderer.MaxUnits];
            int n = backend.ReadUnits(units);
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < n; i++)
            {
                int unitDef = units[i].Def, unitModel = units[i].Model;
                warming.Enqueue(() =>
                {
                    Models.Get(unitModel);
                    Entities.WarmFlight(unitDef, unitModel);
                });
                var def = backend.UnitDefs[unitDef];
                int colour = backend.PlayerById(units[i].Player)?.Colour ?? 0;
                foreach (int o in def.BuildOptions)
                {
                    if (o < 0 || o >= backend.UnitDefs.Count || !seen.Add((o, colour))) continue;
                    int option = o;
                    warming.Enqueue(() =>
                    {
                        int model = backend.LoadModel(backend.UnitDefs[option].ObjectName, colour);
                        Models.Get(model);
                        Entities.WarmFlight(option, model);
                    });
                }
            }
        }

        // Builds models off the list for about budgetMs, at least one.
        // True once none are left.
        public bool WarmSome(double budgetMs)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (warming.Count > 0)
            {
                warming.Dequeue()();
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            return warming.Count == 0;
        }

        public void Render()
        {
            if (Root == null || Camera == null) return;
            var cam = Camera.GetComponent<UnityEngine.Camera>();
            // Shadows reach a little past what the camera frames, so the
            // cascades spend their texels where the eye is.
            using (new Unity.Profiling.ProfilerMarker("Oku.Shadows").Auto()) Looks.ShadowDistance(Mathf.Clamp(Camera.distance * 2.4f + 25f, 50f, 260f));
            using (new Unity.Profiling.ProfilerMarker("Oku.Fog").Auto()) Fog.Update();
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
            Entities?.Dispose();
            Effects?.Dispose();
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
