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

        public void Build(MapInfo map, GameOptions options)
        {
            Root = new GameObject("World");
            Terrain.Build(backend, Root.transform);
            var size = backend.Terrain.Size;
            Atmosphere.Build(Root.transform, map != null ? map.Climate : "", options.Weather, options.Shadows, Mathf.Max(size.x, size.y));
            Terrain.SetSeaClimate(map != null ? map.Climate : "");
            Models = new ModelCache(backend);
            Entities = new EntityRenderer(backend, Models);
            Warm();
            Effects = new EffectRenderer(backend);
            Fog = new FogView(backend);
            Fog.Update(true);
            Entities.Hidden = u => !Fog.InSight(u.Position) && !Friendly(u.Player);
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

        // Builds the models of every unit on the field and of everything the
        // players' units can build, and the flyers' clips, now while the
        // loading screen is up, so a unit seen for the first time does not
        // stall a frame.
        void Warm()
        {
            var units = new UnitState[EntityRenderer.MaxUnits];
            int n = backend.ReadUnits(units);
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < n; i++)
            {
                Models.Get(units[i].Model);
                Entities.WarmFlight(units[i].Def, units[i].Model);
                var def = backend.UnitDefs[units[i].Def];
                int colour = backend.PlayerById(units[i].Player)?.Colour ?? 0;
                foreach (int o in def.BuildOptions)
                {
                    if (o < 0 || o >= backend.UnitDefs.Count || !seen.Add((o, colour))) continue;
                    int model = backend.LoadModel(backend.UnitDefs[o].ObjectName, colour);
                    Models.Get(model);
                    Entities.WarmFlight(o, model);
                }
            }
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
            using (new Unity.Profiling.ProfilerMarker("Oku.Effects").Auto()) Effects.Render(cam);
            // An edit in the map editor can make a sea where there was none.
            WaterView.Prepare(cam, Terrain.Sea != null);
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
