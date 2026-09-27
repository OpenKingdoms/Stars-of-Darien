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
        public GameCamera Camera { get; private set; }
        readonly IGameBackend backend;

        public WorldView(IGameBackend backend) => this.backend = backend;

        public void Build(MapInfo map, GameOptions options)
        {
            Root = new GameObject("World");
            Terrain.Build(backend, Root.transform);
            var size = backend.Terrain.Size;
            Atmosphere.Build(Root.transform, map != null ? map.Climate : "", options.Weather, options.Shadows, Mathf.Max(size.x, size.y));
            Models = new ModelCache(backend);
            Entities = new EntityRenderer(backend, Models);
            Effects = new EffectRenderer(backend);

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
            Camera = cam.GetComponent<GameCamera>();
            if (Camera == null) Camera = cam.gameObject.AddComponent<GameCamera>();
            Camera.enabled = true;
            Camera.ground = backend.GroundHeight;
            Camera.Frame(new Vector2(0, -size.y), new Vector2(size.x, 0), StartFocus());
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

        public void Render()
        {
            if (Root == null || Camera == null) return;
            var cam = Camera.GetComponent<UnityEngine.Camera>();
            // Shadows reach a little past what the camera frames, so the
            // cascades spend their texels where the eye is.
            QualitySettings.shadowDistance = Mathf.Clamp(Camera.distance * 2.4f + 25f, 50f, 260f);
            Entities.Render(cam);
            Effects.Render(cam);
            Atmosphere.Follow(Camera.focus, Camera.transform.position.y - Camera.focus.y, Camera.distance);
        }

        public void Dispose()
        {
            Entities?.Dispose();
            Effects?.Dispose();
            Models?.Dispose();
            Terrain.Dispose();
            Atmosphere.Dispose();
            if (Camera != null) Camera.enabled = false;
            if (Root != null) Looks.Release(Root);
            Root = null;
        }
    }
}
