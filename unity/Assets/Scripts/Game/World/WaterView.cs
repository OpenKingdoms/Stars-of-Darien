// WaterView.cs - the sea for one map: a wave mesh at sea level over the map
// and past its edge, its baked depth and shore distance, the textures and
// every value OkuWater, OkuWake and the ground under the sea read, eased
// toward the climate's look and the weather, and the ships' wakes.
using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class WaterView
    {
        public const float MeshStep = 1.5f;

        public GameObject Root { get; private set; }
        public WaterWakes Wakes { get; private set; }
        public float SeaLevel { get; private set; }
        public Vector4 SeaRect { get; private set; }

        Mesh mesh;
        Material material;
        Texture2D seaData;
        float cell;
        Vector2 size, origin;
        Func<float, float, float> ground;
        string climate = "";
        float bedLuma = 0.08f;

        // The look the water eases toward, and where it is now.
        public struct Look
        {
            public Vector4 Waves;       // swell height, roughness, whitecaps, rain
            public Vector4 Sigma;       // absorption per unit, caustics
            public Color Scatter;       // deep water's colour in full light (linear), a sun glint
            public Color Bed;           // sea bed colour (linear), a how far the bed takes it
            public Color Sky;           // the zenith (linear), a reflection strength

            public static Look Lerp(Look a, Look b, float t) => new Look
            {
                Waves = Vector4.Lerp(a.Waves, b.Waves, t), Sigma = Vector4.Lerp(a.Sigma, b.Sigma, t),
                Scatter = Color.Lerp(a.Scatter, b.Scatter, t), Bed = Color.Lerp(a.Bed, b.Bed, t), Sky = Color.Lerp(a.Sky, b.Sky, t),
            };
        }

        public Look Now { get; private set; }
        bool eased;

        // Sets up the sea over a map whose ground height at a world x, z
        // `ground` gives, with `margin` world units of sea past each edge.
        // The map's north-west corner is at `at`, the origin for a game.
        public void Build(Transform parent, Vector2 mapSize, float cellSize, float seaLevel, Func<float, float, float> groundAt, float margin, Vector2 at = default)
        {
            size = mapSize;
            origin = at;
            cell = cellSize;
            SeaLevel = seaLevel;
            ground = groundAt;
            mesh = Grid(origin, size, margin, MeshStep);
            Root = new GameObject("Sea");
            Root.transform.SetParent(parent, false);
            Root.transform.position = new Vector3(0, seaLevel, 0);
            Root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = Root.AddComponent<MeshRenderer>();
            material = Looks.Water();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Wakes = new WaterWakes();
            Bake();
            Apply(Target(WeatherChoice.Off), 0f);
        }

        // The ground changed: bake the sea's depth and shore again.
        public void Bake()
        {
            if (seaData != null) Looks.Release(seaData);
            var r = WaterTextures.SeaRect(size, cell);
            SeaRect = new Vector4(r.x + origin.x, r.y + origin.y, r.z, r.w);
            var px = WaterTextures.SeaData(SeaRect, cell, SeaLevel, ground, out int w, out int h);
            // Row 0 is north, the top of the texture.
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            seaData = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name = "sea data", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            seaData.SetPixels32(flipped);
            seaData.Apply(false);
            SetStatics();
        }

        // The ground changed in a world rectangle (x from x0 to x1, z from
        // zNorth down to zSouth): bake just the texels it can reach. Shore
        // distances stop at 8 cells (16.5 texels), so texels farther than
        // that from the edit keep theirs, and a window as much wider again
        // sees every shore the rest can reach. Heights on the rectangle's
        // edge reach a cell past it, hence the spare texels.
        public void Bake(float x0, float x1, float zNorth, float zSouth)
        {
            if (seaData == null) { Bake(); return; }
            int w = seaData.width, h = seaData.height;
            float per = WaterTextures.SeaTexelsPerCell / cell;
            const int reach = 20;
            int ix0 = Mathf.Clamp(Mathf.FloorToInt((x0 - SeaRect.x) * per) - reach, 0, w);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((x1 - SeaRect.x) * per) + reach, 0, w);
            int iy0 = Mathf.Clamp(Mathf.FloorToInt((SeaRect.y - zNorth) * per) - reach, 0, h);
            int iy1 = Mathf.Clamp(Mathf.CeilToInt((SeaRect.y - zSouth) * per) + reach, 0, h);
            if (ix1 <= ix0 || iy1 <= iy0) return;
            int wx0 = Mathf.Max(0, ix0 - reach), wx1 = Mathf.Min(w, ix1 + reach);
            int wy0 = Mathf.Max(0, iy0 - reach), wy1 = Mathf.Min(h, iy1 + reach);
            int ww = wx1 - wx0;
            var px = WaterTextures.SeaData(SeaRect, cell, SeaLevel, ground, new RectInt(wx0, wy0, ww, wy1 - wy0));
            int bw = ix1 - ix0, bh = iy1 - iy0;
            var block = new Color32[bw * bh];
            // The texture's row 0 is the south edge.
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                    block[(bh - 1 - y) * bw + x] = px[(iy0 - wy0 + y) * ww + (ix0 - wx0 + x)];
            seaData.SetPixels32(ix0, h - iy1, bw, bh, block);
            seaData.Apply(false);
        }

        // The average lightness of the painted ground under the sea, linear.
        public void SetBedLuma(float luma) { bedLuma = Mathf.Max(0.02f, luma); SetStatics(); }

        public void SetClimate(string c) { climate = c ?? ""; eased = false; }

        void SetStatics()
        {
            Shader.SetGlobalTexture("_OkuSeaData", seaData);
            Shader.SetGlobalVector("_OkuSeaRect", SeaRect);
            Shader.SetGlobalVector("_OkuSeaCell", new Vector4(cell, 0, 0, 0));
            Shader.SetGlobalFloat("_OkuSeaLevel", SeaLevel);
            Shader.SetGlobalFloat("_OkuBedLuma", bedLuma);
            Shader.SetGlobalTexture("_OkuWaterNormals", WaterTextures.Normals);
            Shader.SetGlobalTexture("_OkuWaterFoam", WaterTextures.Foam);
            Shader.SetGlobalTexture("_OkuWaterCaustics", WaterTextures.Caustics);
            Shader.SetGlobalTexture("_OkuWaterNoise", WaterTextures.Noise);
            WaterWaves.SeaLevel = SeaLevel;
        }

        // No sea: the ground shaders skip their underwater work.
        public static void ClearGlobals()
        {
            Shader.SetGlobalFloat("_OkuSeaLevel", -1000f);
            WaterWaves.SeaLevel = -1f;
        }

        // The camera draws the scene under the sea from its opaque and depth
        // textures, so it asks URP for both.
        public static void Prepare(Camera cam)
        {
            if (cam == null || Looks.Urp == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            data.requiresColorOption = CameraOverrideOption.On;
            data.requiresDepthOption = CameraOverrideOption.On;
        }

        // The climate's water, before weather. Red goes first, so light
        // sand under half a unit of water shows turquoise, and by two units
        // down the sea is its own deep colour. Colours are linear, as the
        // shaders read global colours unconverted.
        public static Look ClimateLook(string climate)
        {
            switch (climate)
            {
                case "desert":
                    return new Look { Sigma = new Vector4(2.0f, 0.45f, 0.34f, 1.1f), Scatter = new Color(0.005f, 0.05f, 0.075f, 1f), Bed = new Color(0.93f, 0.86f, 0.68f, 0.8f), Sky = new Color(0.42f, 0.58f, 0.76f, 1f) };
                case "volcanic":
                    return new Look { Sigma = new Vector4(2.6f, 0.9f, 0.7f, 0.6f), Scatter = new Color(0.008f, 0.022f, 0.032f, 1f), Bed = new Color(0.36f, 0.33f, 0.3f, 0.7f), Sky = new Color(0.4f, 0.47f, 0.58f, 1f) };
                case "swamp":
                    return new Look { Sigma = new Vector4(2.2f, 1.3f, 1.9f, 0f), Scatter = new Color(0.03f, 0.035f, 0.012f, 1f), Bed = new Color(0.34f, 0.3f, 0.2f, 0.6f), Sky = new Color(0.44f, 0.52f, 0.54f, 1f) };
                case "snow":
                    return new Look { Sigma = new Vector4(2.4f, 0.75f, 0.55f, 0.7f), Scatter = new Color(0.008f, 0.04f, 0.075f, 1f), Bed = new Color(0.6f, 0.62f, 0.62f, 0.7f), Sky = new Color(0.5f, 0.6f, 0.74f, 1f) };
                default:
                    return new Look { Sigma = new Vector4(2.2f, 0.6f, 0.44f, 1f), Scatter = new Color(0.004f, 0.036f, 0.07f, 1f), Bed = new Color(0.86f, 0.82f, 0.68f, 0.8f), Sky = new Color(0.36f, 0.53f, 0.74f, 1f) };
            }
        }

        // The climate's water in this weather.
        public Look Target(WeatherChoice w)
        {
            var look = ClimateLook(climate);
            look.Waves = new Vector4(1f, 0.06f, 0f, 0f);
            // Deep water under a grey sky, and that sky.
            var slate = new Color(0.02f, 0.03f, 0.035f, 1f);
            var grey = new Color(0.46f, 0.5f, 0.53f, 1f);
            switch (w)
            {
                case WeatherChoice.Rain:
                    look.Waves = new Vector4(1.6f, 0.18f, 0.6f, 1f);
                    look.Scatter = Keep(Color.Lerp(look.Scatter, slate, 0.45f), look.Scatter.a * 0.4f);
                    look.Sky = Keep(Color.Lerp(look.Sky, grey, 0.7f), look.Sky.a);
                    look.Sigma = new Vector4(look.Sigma.x, look.Sigma.y * 1.4f, look.Sigma.z * 1.4f, look.Sigma.w * 0.3f);
                    break;
                case WeatherChoice.Snow:
                    look.Waves = new Vector4(0.8f, 0.1f, 0f, 0f);
                    look.Scatter = Keep(Color.Lerp(look.Scatter, slate, 0.25f) * 0.85f, look.Scatter.a * 0.6f);
                    look.Sigma.w *= 0.6f;
                    break;
                case WeatherChoice.Fog:
                    // Calm and glassy, the grey sky lying on it.
                    look.Waves = new Vector4(0.35f, 0.02f, 0f, 0f);
                    look.Scatter = Keep(Color.Lerp(look.Scatter, slate, 0.4f), look.Scatter.a * 0.04f);
                    look.Sky = Keep(Color.Lerp(look.Sky, grey, 0.85f), look.Sky.a * 1.3f);
                    look.Sigma.w *= 0.5f;
                    break;
            }
            return look;
        }

        static Color Keep(Color c, float a) { c.a = a; return c; }

        // Once a frame: the clock, the wind, the look eased toward the
        // weather over a few seconds, and the wakes.
        public void Update(Atmosphere atmosphere, EntityRenderer entities, IGameBackend backend)
        {
            if (Root == null) return;
            var w = atmosphere != null ? atmosphere.Weather : WeatherChoice.Off;
            var wind = atmosphere != null ? new Vector2(atmosphere.Wind.x, atmosphere.Wind.z) : new Vector2(1.5f, 0.6f);
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.25f);
            Apply(Target(w), eased ? 1f - Mathf.Exp(-dt / 1.0f) : 1f);
            eased = true;
            WaterWaves.Time = Time.unscaledTime;
            WaterWaves.Wind = wind.sqrMagnitude > 1e-6f ? wind.normalized : Vector2.right;
            Shader.SetGlobalFloat("_OkuWaterTime", WaterWaves.Time);
            Shader.SetGlobalVector("_OkuWaterWind", new Vector4(WaterWaves.Wind.x, WaterWaves.Wind.y, wind.magnitude, 0));
            if (entities != null && backend != null) Wakes.Update(entities, backend, SeaLevel, WaterWaves.Time);
            Wakes.Draw();
        }

        // Sets the look part way from where it is toward a target.
        public void Apply(Look target, float t)
        {
            Now = t >= 1f ? target : Look.Lerp(Now, target, t);
            var l = Now;
            WaterWaves.Amplitude = l.Waves.x;
            Shader.SetGlobalVector("_OkuWaterWaves", l.Waves);
            Shader.SetGlobalVector("_OkuWaterSigma", l.Sigma);
            Shader.SetGlobalColor("_OkuWaterScatter", l.Scatter);
            Shader.SetGlobalColor("_OkuWaterBed", l.Bed);
            Shader.SetGlobalColor("_OkuWaterSky", l.Sky);
        }

        // A flat grid over the map grown by margin on every side.
        static Mesh Grid(Vector2 origin, Vector2 size, float margin, float step)
        {
            int nx = Mathf.CeilToInt((size.x + 2 * margin) / step), nz = Mathf.CeilToInt((size.y + 2 * margin) / step);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                    verts[z * (nx + 1) + x] = new Vector3(origin.x - margin + x * step, 0, origin.y + margin - z * step);
            var tris = new int[nx * nz * 6];
            int k = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int nw = z * (nx + 1) + x, ne = nw + 1, sw = nw + nx + 1, se = sw + 1;
                    tris[k++] = nw; tris[k++] = ne; tris[k++] = se;
                    tris[k++] = nw; tris[k++] = se; tris[k++] = sw;
                }
            var mesh = new Mesh { name = "sea", hideFlags = HideFlags.DontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = verts, triangles = tris };
            var n = new Vector3[verts.Length];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            mesh.normals = n;
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.up * 4);
            return mesh;
        }

        public void Dispose()
        {
            Wakes?.Dispose();
            Wakes = null;
            if (Root != null) Looks.Release(Root);
            Root = null;
            if (mesh != null) Looks.Release(mesh);
            if (material != null) Looks.Release(material);
            if (seaData != null) Looks.Release(seaData);
            mesh = null; material = null; seaData = null;
            ClearGlobals();
        }
    }
}
