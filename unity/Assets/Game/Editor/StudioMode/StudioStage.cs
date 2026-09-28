// StudioStage.cs - the studio scene's unsaved contents: ground, sky, sea and
// weather, the turntable, monarch, footprint grid, anchor, the original and
// its ghost, and the camera for the classic and free views.
using System;
using System.Collections.Generic;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Studio
{
    public enum TimeOfDay { Game, Morning, Noon, Evening, Night }

    public struct StudioView
    {
        public bool Classic;
        public float Distance, Yaw, Pitch;
        public const float ClassicDistance = 34f, ClassicFov = 40f;
        public static StudioView ClassicDefault => new StudioView { Classic = true, Distance = ClassicDistance, Pitch = GameCamera.ClassicPitch };
        public static StudioView FreeDefault => new StudioView { Classic = false, Distance = 11f, Yaw = 200f, Pitch = 25f };
    }

    public sealed class StudioStage : IDisposable
    {
        public const string RootName = "Studio Stage";

        public GameObject Root { get; private set; }
        public Camera Camera { get; private set; }
        public Vector3 Spot { get; private set; }
        public Atmosphere Atmosphere { get; private set; }
        public bool RealGround => terrain != null;
        public float TurntableYaw;
        public GameObject ModelObject => model;
        public IGameBackend Backend => backend;
        public bool MonarchIsStandIn { get; private set; }

        public bool ShowMonarch = true, ShowGrid = true, ShowAnchor = true, ShowOriginal = true, ShowGhost = true;

        IGameBackend backend;
        TerrainView terrain;
        MapTerrain ground;
        GameObject neutral, turntable, fixNode, model, original, ghost, monarch, grid, anchor, inPlace;
        readonly List<Object> owned = new List<Object>();
        readonly List<Object> modelOwned = new List<Object>();
        readonly List<Transform> cards = new List<Transform>();
        readonly Dictionary<Material, (Color color, float gloss, float glow)> baseLook = new Dictionary<Material, (Color, float, float)>();
        Color climateSun;
        float weatherLight = 1f;
        string climate = "grass";
        bool sea = true;
        public string ClimateName => climate;
        Bounds modelBounds = new Bounds(Vector3.up * 0.5f, Vector3.one);
        float originalWidth = 1f, monarchWidth = 1f, monarchHeight = StudioTargets.MonarchHeight;
        Vector2Int footprint = Vector2Int.one;
        TimeOfDay time;
        bool shadows = true;

        // ---- Building ----

        // The ground from the backend's loaded map when real is set,
        // otherwise a neutral island in the climate's colour.
        public void Build(IGameBackend b, bool real, string climateName, WeatherChoice weather, TimeOfDay tod, bool withSea, bool withShadows)
        {
            Dispose();
            backend = b;
            climate = string.IsNullOrEmpty(climateName) ? "grass" : climateName;
            sea = withSea;
            shadows = withShadows;
            time = tod;
            Shader.SetGlobalFloat("_OkuFogOn", 0f);
            Root = new GameObject(RootName);
            if (real && b != null && b.Terrain != null)
            {
                ground = b.Terrain;
                terrain = new TerrainView();
                terrain.Build(b, Root.transform);
                terrain.SetSeaClimate(climate);
                Spot = PickSpot(ground, StartOf(b));
            }
            else
            {
                BuildNeutral();
                Spot = Vector3.zero;
            }
            BuildSky(weather);

            var camGo = new GameObject("Studio Camera");
            camGo.transform.SetParent(Root.transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 1500f;
            Camera.fieldOfView = StudioView.ClassicFov;
            Camera.depthTextureMode |= DepthTextureMode.Depth;
            WaterView.Prepare(Camera, HasSea);
            Atmosphere.SetPostEffects(true, Camera);

            turntable = new GameObject("Turntable");
            turntable.transform.SetParent(Root.transform, false);
            turntable.transform.position = Spot;
            fixNode = new GameObject("Fix");
            fixNode.transform.SetParent(turntable.transform, false);
            RebuildGrid();
            RebuildAnchor();
            Hide(Root);
        }

        public float GroundAt(float x, float z)
        {
            if (ground != null) return ground.Sample(x, z);
            if (!sea) return 0f;
            float r = new Vector2(x, z).magnitude;
            return r < 36f ? 0f : -2.6f * Mathf.SmoothStep(0f, 1f, (r - 36f) / 12f);
        }

        Vector3 OnGround(Vector3 p) => new Vector3(p.x, GroundAt(p.x, p.z), p.z);

        static Vector3 StartOf(IGameBackend b)
        {
            var units = new UnitState[512];
            int n = b.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Player == b.LocalPlayer) return units[i].Position;
            var s = b.Terrain.Size;
            return new Vector3(s.x / 2, 0, -s.y / 2);
        }

        // The flattest dry place near the start with room for the monarch,
        // the model and the original in a row, and open ground around them
        // so no cliff stands in the free view.
        public static Vector3 PickSpot(MapTerrain t, Vector3 near)
        {
            var size = t.Size;
            Vector3 best = new Vector3(near.x, t.Sample(near.x, near.z), near.z);
            float bestScore = float.MaxValue;
            for (float dz = -48; dz <= 48; dz += 2)
                for (float dx = -48; dx <= 48; dx += 2)
                {
                    float x = near.x + dx, z = near.z + dz;
                    if (x < 18 || z > -14 || x > size.x - 18 || z < -size.y + 14) continue;
                    float mid = t.Sample(x, z), lo = float.MaxValue, hi = float.MinValue, around = 0f;
                    bool wet = false;
                    for (float sz = -12; sz <= 12; sz += 2)
                        for (float sx = -16; sx <= 16; sx += 2)
                        {
                            float h = t.Sample(x + sx, z + sz);
                            if (Mathf.Abs(sz) <= 4 && Mathf.Abs(sx) <= 10)
                            {
                                lo = Mathf.Min(lo, h);
                                hi = Mathf.Max(hi, h);
                                wet |= t.SeaLevel > 0 && h < t.SeaLevel + 0.3f;
                            }
                            else around = Mathf.Max(around, Mathf.Abs(h - mid));
                        }
                    float score = (hi - lo) * 2f + around * 0.5f + (wet ? 1000f : 0f) + new Vector2(dx, dz).magnitude * 0.02f;
                    if (score < bestScore) { bestScore = score; best = new Vector3(x, mid, z); }
                }
            return best;
        }

        void BuildNeutral()
        {
            neutral = new GameObject("Neutral Ground");
            neutral.transform.SetParent(Root.transform, false);
            const float half = 48f, step = 1f;
            int n = Mathf.RoundToInt(2 * half / step);
            var v = new Vector3[(n + 1) * (n + 1)];
            var uv = new Vector2[v.Length];
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float x = -half + i * step, z = -half + j * step;
                    v[j * (n + 1) + i] = new Vector3(x, GroundAt(x, z), z);
                    uv[j * (n + 1) + i] = new Vector2(x / 4f, z / 4f);
                }
            var tris = new int[n * n * 6];
            int k = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    tris[k++] = a; tris[k++] = c; tris[k++] = d;
                    tris[k++] = a; tris[k++] = d; tris[k++] = b;
                }
            var mesh = Own(new Mesh { name = "neutral ground", indexFormat = IndexFormat.UInt32, vertices = v, uv = uv, triangles = tris });
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var tex = Own(GroundTexture(ClimateColour(climate)));
            var mat = Own(Looks.Terrain(tex, sea ? -0.35f : -100f));
            var land = new GameObject("Land");
            land.transform.SetParent(neutral.transform, false);
            land.AddComponent<MeshFilter>().sharedMesh = mesh;
            land.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (!sea)
            {
                // A wide plain to the horizon.
                var plain = new GameObject("Plain");
                plain.transform.SetParent(neutral.transform, false);
                var pm = Own(new Mesh { name = "plain" });
                const float far = 600f;
                pm.vertices = new[] { new Vector3(-far, -0.02f, -far), new Vector3(-far, -0.02f, far), new Vector3(far, -0.02f, far), new Vector3(far, -0.02f, -far) };
                pm.uv = new[] { new Vector2(-far, -far) / 4f, new Vector2(-far, far) / 4f, new Vector2(far, far) / 4f, new Vector2(far, -far) / 4f };
                pm.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                pm.RecalculateNormals();
                pm.RecalculateBounds();
                plain.AddComponent<MeshFilter>().sharedMesh = pm;
                plain.AddComponent<MeshRenderer>().sharedMaterial = mat;
                return;
            }
            BuildSea();
        }

        WaterView studioSea;

        bool HasSea => studioSea != null || terrain?.Sea != null;

        void BuildSea()
        {
            const float level = -0.35f, reach = 400f, rect = 64f;
            // The island does not change with the climate, so neither does its sea.
            if (studioSea != null)
            {
                studioSea.Root.transform.SetParent(neutral.transform, false);
                studioSea.SetClimate(climate);
                return;
            }
            studioSea = new WaterView();
            studioSea.Build(neutral.transform, new Vector2(2 * rect, 2 * rect), 1f, level, GroundAt, reach - rect, new Vector2(-rect, rect));
            studioSea.SetClimate(climate);
        }

        public static Color ClimateColour(string c)
        {
            switch (c)
            {
                case "snow": return new Color(0.83f, 0.85f, 0.88f);
                case "desert": return new Color(0.7f, 0.58f, 0.4f);
                case "swamp": return new Color(0.3f, 0.34f, 0.22f);
                case "volcanic": return new Color(0.3f, 0.26f, 0.24f);
                default: return new Color(0.36f, 0.45f, 0.24f);
            }
        }

        // Soft value noise in the ground's colour, so the eye has something
        // to read distance by without it looking like any real place.
        internal static Texture2D GroundTexture(Color c)
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "neutral ground", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color32[n * n];
            var rng = new System.Random(7);
            var coarse = new float[9 * 9];
            for (int i = 0; i < coarse.Length; i++) coarse[i] = (float)rng.NextDouble();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x / 16f, fy = y / 16f;
                    int ix = (int)fx, iy = (int)fy;
                    float tx = fx - ix, ty = fy - iy;
                    float a = Mathf.Lerp(coarse[iy % 8 * 9 + ix % 8], coarse[iy % 8 * 9 + (ix + 1) % 8], tx);
                    float b = Mathf.Lerp(coarse[(iy + 1) % 8 * 9 + ix % 8], coarse[(iy + 1) % 8 * 9 + (ix + 1) % 8], tx);
                    float v = 0.88f + 0.16f * Mathf.Lerp(a, b, ty) + 0.05f * (float)(rng.NextDouble() - 0.5);
                    px[y * n + x] = new Color(c.r * v, c.g * v, c.b * v, 1f);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        void BuildSky(WeatherChoice weather)
        {
            Atmosphere?.Dispose();
            Atmosphere = new Atmosphere();
            // The atmosphere tunes the built-in pipeline's quality settings,
            // which belong to the project, so they are put back.
            var q = (QualitySettings.shadows, QualitySettings.shadowResolution, QualitySettings.shadowProjection, QualitySettings.shadowCascades, QualitySettings.shadowCascade4Split);
            Atmosphere.Build(Root.transform, climate, weather, shadows, ground != null ? Mathf.Max(ground.Size.x, ground.Size.y) : 96f);
            (QualitySettings.shadows, QualitySettings.shadowResolution, QualitySettings.shadowProjection, QualitySettings.shadowCascades, QualitySettings.shadowCascade4Split) = q;
            climateSun = Atmosphere.Sun.color;
            weatherLight = Atmosphere.Sun.intensity;
            ApplyTime();
            if (Camera != null) Atmosphere.SetPostEffects(true, Camera);
        }

        // ---- The look ----

        public void SetClimate(string c, bool withSea, WeatherChoice weather)
        {
            climate = string.IsNullOrEmpty(c) ? "grass" : c;
            if (terrain == null && (withSea != sea || neutral != null))
            {
                sea = withSea;
                // A sea that stays is kept, one that goes is let go with its globals.
                if (studioSea != null && sea) studioSea.Root.transform.SetParent(Root.transform, false);
                else { studioSea?.Dispose(); studioSea = null; }
                if (neutral != null) Object.DestroyImmediate(neutral);
                BuildNeutral();
                RebuildGrid();
                RebuildAnchor();
                Place();
            }
            terrain?.SetSeaClimate(climate);
            if (Camera != null) WaterView.Prepare(Camera, HasSea);
            BuildSky(weather);
            Hide(Root);
        }

        public void SetWeather(WeatherChoice w)
        {
            Atmosphere.SetWeather(GameOptions.Resolve(w, climate));
            weatherLight = Atmosphere.Sun.intensity;
            ApplyTime();
            Hide(Root);
        }

        public void SetTime(TimeOfDay t)
        {
            time = t;
            ApplyTime();
        }

        public void SetShadows(bool on)
        {
            shadows = on;
            if (Atmosphere?.Sun != null) Atmosphere.Sun.shadows = on ? LightShadows.Soft : LightShadows.None;
        }

        void ApplyTime()
        {
            var sun = Atmosphere?.Sun;
            if (sun == null) return;
            Quaternion rot;
            Color col;
            float mult, ambient, exposure;
            switch (time)
            {
                case TimeOfDay.Morning: rot = Quaternion.Euler(20f, 95f, 0); col = new Color(1f, 0.86f, 0.7f); mult = 0.9f; ambient = 0.7f; exposure = 1.05f; break;
                case TimeOfDay.Noon: rot = Quaternion.Euler(76f, 170f, 0); col = new Color(1f, 0.98f, 0.95f); mult = 1.1f; ambient = 0.95f; exposure = 1.3f; break;
                case TimeOfDay.Evening: rot = Quaternion.Euler(12f, 245f, 0); col = new Color(1f, 0.64f, 0.4f); mult = 0.8f; ambient = 0.5f; exposure = 0.85f; break;
                case TimeOfDay.Night: rot = Quaternion.Euler(40f, 200f, 0); col = new Color(0.55f, 0.65f, 1f); mult = 0.28f; ambient = 0.28f; exposure = 0.3f; break;
                default: rot = Quaternion.Euler(48f, 150f, 0); col = climateSun; mult = 1f; ambient = 0.85f; exposure = 1.2f; break;
            }
            sun.transform.rotation = rot;
            sun.color = col;
            sun.intensity = weatherLight * mult;
            sun.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            RenderSettings.ambientIntensity = ambient;
            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure")) RenderSettings.skybox.SetFloat("_Exposure", exposure);
            DynamicGI.UpdateEnvironment();
        }

        // ---- The model ----

        public void SetModel(StudioModel m, StudioFix fix, MaterialTweaks tweaks, Color team)
        {
            ClearModel();
            if (m?.Template == null) return;
            model = Object.Instantiate(m.Template, fixNode.transform, false);
            model.name = m.Name;
            model.SetActive(true);
            // Each material is the instance's own, so tweaks never reach the
            // loaded template.
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var copy = new Material(mats[i]) { name = mats[i].name };
                    modelOwned.Add(copy);
                    baseLook[copy] = (mats[i].HasProperty("_Color") ? mats[i].color : Color.white, mats[i].HasProperty("_Glossiness") ? mats[i].GetFloat("_Glossiness") : 0.2f,
                        mats[i].HasProperty("_Emission") ? mats[i].GetFloat("_Emission") : 0f);
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            modelBounds = m.Facts != null && m.Facts.HasGeometry ? m.Facts.Bounds : new Bounds(Vector3.up * 0.5f, Vector3.one);
            SetFix(fix);
            SetTweaks(tweaks, team);
            Hide(Root);
        }

        public void ClearModel()
        {
            if (model != null) Object.DestroyImmediate(model);
            model = null;
            foreach (var o in modelOwned) if (o != null) Object.DestroyImmediate(o);
            modelOwned.Clear();
            baseLook.Clear();
        }

        public void SetFix(StudioFix fix)
        {
            if (fixNode == null) return;
            fixNode.transform.localPosition = fix.Offset;
            fixNode.transform.localRotation = Quaternion.Euler(0, fix.QuarterTurns * 90f, 0);
            fixNode.transform.localScale = Vector3.one * fix.Scale;
            fixedBounds = fix.Apply(modelBounds);
            Place();
        }

        Bounds fixedBounds = new Bounds(Vector3.up * 0.5f, Vector3.one);
        public Bounds ModelBounds => fixedBounds;

        // Tint, brightness, roughness and self light over every material, and
        // the team colour on materials named for it.
        public void SetTweaks(MaterialTweaks t, Color team)
        {
            t ??= new MaterialTweaks();
            foreach (var kv in baseLook)
            {
                var m = kv.Key;
                if (m == null) continue;
                var c = t.Apply(kv.Value.color);
                if (m.name.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0) c = new Color(c.r * team.r, c.g * team.g, c.b * team.b, c.a);
                if (m.HasProperty("_Color")) m.color = c;
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", t.Roughness >= 0 ? (1f - t.Roughness) * 0.5f : kv.Value.gloss);
                // The model's own glow stays unless the tweak sets one, as Bake does.
                if (m.HasProperty("_Emission")) m.SetFloat("_Emission", t.Emission > 0 ? t.Emission : kv.Value.glow);
            }
        }

        // ---- What stands around it ----

        // The original beside the model and its ghost over it: a sprite on a
        // card like the game draws, or the original model at rest.
        public void SetOriginal(StudioTarget t, PresentedModel pm, Texture2D sprite, Rect spriteRect)
        {
            if (original != null) Object.DestroyImmediate(original);
            if (ghost != null) Object.DestroyImmediate(ghost);
            if (inPlace != null) Object.DestroyImmediate(inPlace);
            original = ghost = inPlace = null;
            cards.RemoveAll(c => c == null);
            footprint = t != null && t.Kind != TargetKind.None ? new Vector2Int(Mathf.Max(1, t.Footprint.x), Mathf.Max(1, t.Footprint.y)) : Vector2Int.one;
            originalWidth = 1f;
            if (t != null && t.Kind != TargetKind.None)
            {
                if (pm != null)
                {
                    Func<string, bool> skip = null;
                    if (t.Kind == TargetKind.UnitCard && !string.IsNullOrEmpty(t.ReplacesPiece))
                    {
                        var card = new CardOverride { ReplacesPiece = t.ReplacesPiece };
                        skip = card.Hides;
                        inPlace = Rest(pm, "Original around the model", null, skip);
                        inPlace.transform.SetParent(turntable.transform, false);
                    }
                    original = Rest(pm, "Original", null, null);
                    // A card's ghost is only the piece it replaces.
                    ghost = Rest(pm, "Ghost", GhostMaterial, t.Kind == TargetKind.UnitCard ? (Func<string, bool>)(n => skip != null && !skip(n)) : null);
                    var b = pm.RestBounds;
                    originalWidth = Mathf.Max(b.size.x, b.size.z);
                }
                else if (sprite != null)
                {
                    original = Card("Original", sprite, spriteRect, false);
                    ghost = Card("Ghost", sprite, spriteRect, true);
                    originalWidth = spriteRect.width;
                }
            }
            if (original != null) original.transform.SetParent(Root.transform, false);
            if (ghost != null) { ghost.transform.SetParent(Root.transform, false); ghost.transform.position = Spot; }
            RebuildGrid();
            Place();
            Hide(Root);
        }

        public void SetMonarch(PresentedModel pm, Color team)
        {
            if (monarch != null) Object.DestroyImmediate(monarch);
            MonarchIsStandIn = pm == null;
            if (pm != null)
            {
                monarch = Rest(pm, "Monarch", null, null);
                monarchWidth = Mathf.Max(pm.RestBounds.size.x, pm.RestBounds.size.z);
                monarchHeight = pm.RestBounds.max.y;
            }
            else
            {
                monarch = Marker(team);
                monarchWidth = 2f;
                monarchHeight = StudioTargets.MonarchHeight;
            }
            monarch.transform.SetParent(Root.transform, false);
            Place();
            Hide(Root);
        }

        // A stand-in monarch: a figure 4 cells tall on a two by two cell base,
        // the size of the game's monarchs.
        GameObject Marker(Color team) => Marker(team, owned);

        // The stand-in monarch with its materials and mesh kept in owned.
        internal static GameObject Marker(Color team, List<Object> owned)
        {
            T Own<T>(T o) where T : Object { o.hideFlags = HideFlags.DontSave; owned.Add(o); return o; }
            var go = new GameObject("Monarch");
            var mat = Own(Looks.Model(null));
            mat.color = Color.Lerp(team, Color.white, 0.25f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0, 1.65f, 0);
            body.transform.localScale = new Vector3(0.9f, 1.65f, 0.9f);
            body.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "Head";
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = new Vector3(0, 3.65f, 0);
            head.transform.localScale = Vector3.one * 0.7f;
            head.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var baseMat = Own(Looks.Overlay(new Color(team.r, team.g, team.b, 0.45f)));
            var plate = new GameObject("Base");
            plate.transform.SetParent(go.transform, false);
            plate.AddComponent<MeshFilter>().sharedMesh = Own(Quad(new Vector3(-1, 0.03f, -1), new Vector3(1, 0.03f, 1)));
            plate.AddComponent<MeshRenderer>().sharedMaterial = baseMat;
            return go;
        }

        static Shader GhostShader => Shader.Find("OpenKingdoms/Studio/Ghost") ?? Looks.Find("OkuGhost", "Sprites/Default");

        Material GhostMaterial(Material src)
        {
            var m = Own(new Material(GhostShader));
            if (src != null && src.mainTexture != null) m.mainTexture = src.mainTexture;
            m.SetColor("_Tint", new Color(0.55f, 0.85f, 1f));
            m.SetFloat("_Mix", 0.35f);
            m.SetFloat("_Alpha", 0.45f);
            return m;
        }

        // A backend model at rest, its pieces placed by their offsets, less
        // the script's alternates and whatever skip names.
        internal static GameObject Rest(PresentedModel pm, string name, Func<Material, Material> remap, Func<string, bool> skip)
        {
            var go = new GameObject(name);
            var d = pm.Data;
            var ghostMats = new Dictionary<Material, Material>();
            for (int p = 0; p < pm.Pieces.Length; p++)
            {
                var mesh = pm.Pieces[p];
                if (mesh == null) continue;
                string piece = d.Pieces[p].Name ?? "";
                if (piece.EndsWith("_off") || piece.EndsWith("_dead")) continue;
                if (skip != null && skip(piece)) continue;
                var at = Vector3.zero;
                for (int q = p; q >= 0; q = d.Pieces[q].Parent) at += d.Pieces[q].Offset * d.Scale;
                var child = new GameObject(piece);
                child.transform.SetParent(go.transform, false);
                child.transform.localPosition = at;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mats = (Material[])pm.Materials[p].Clone();
                if (remap != null)
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!ghostMats.TryGetValue(mats[i], out var g)) ghostMats[mats[i]] = g = remap(mats[i]);
                        mats[i] = g;
                    }
                var r = child.AddComponent<MeshRenderer>();
                r.sharedMaterials = mats;
                if (remap != null) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            return go;
        }

        // A picture facing the camera, as the game draws a sprite feature:
        // rect is left of the anchor, bottom, width, height in cells. Aim
        // turns and nudges its Face, and go stays on the anchor.
        GameObject Card(string name, Texture2D sprite, Rect rect, bool asGhost)
        {
            var go = new GameObject(name);
            var face = new GameObject("Face");
            face.transform.SetParent(go.transform, false);
            var quad = new GameObject("Picture");
            quad.transform.SetParent(face.transform, false);
            quad.transform.localPosition = new Vector3(rect.x, rect.y, 0);
            quad.transform.localScale = new Vector3(rect.width, rect.height, 1);
            quad.AddComponent<MeshFilter>().sharedMesh = Own(CardQuad());
            Material mat;
            if (asGhost)
            {
                mat = Own(new Material(GhostShader) { mainTexture = sprite });
                mat.SetColor("_Tint", new Color(0.55f, 0.85f, 1f));
                mat.SetFloat("_Mix", 0.1f);
                mat.SetFloat("_Alpha", 0.5f);
            }
            else
            {
                mat = Own(Looks.Model(sprite));
                mat.SetFloat("_Cull", 0f);
            }
            var r = quad.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            cards.Add(face.transform);
            return go;
        }

        static Mesh CardQuad()
        {
            var m = new Mesh { name = "card" };
            m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            // Cards light like the ground under them, as in the game.
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            m.RecalculateBounds();
            return m;
        }

        static Mesh Quad(Vector3 a, Vector3 b)
        {
            var m = new Mesh { name = "quad" };
            m.vertices = new[] { new Vector3(a.x, a.y, a.z), new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, a.y, a.z) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        // The monarch west of the model and the original east of it, clear
        // of both, on the ground.
        void Place()
        {
            if (Root == null) return;
            float half = Mathf.Max(fixedBounds.extents.x, fixedBounds.extents.z, footprint.x * 0.5f);
            if (monarch != null) monarch.transform.position = OnGround(Spot + Vector3.left * (half + monarchWidth * 0.5f + 1.5f));
            if (original != null) original.transform.position = OnGround(Spot + Vector3.right * (half + originalWidth * 0.5f + 1.5f));
            if (ghost != null) ghost.transform.position = Spot;
        }

        public IEnumerable<(string label, Vector3 at)> Labels()
        {
            float top = fixedBounds.max.y + 0.4f;
            yield return ("New model", Spot + Vector3.up * top);
            if (monarch != null && monarch.activeSelf) yield return (MonarchIsStandIn ? "Monarch (stand-in, 4 cells)" : "Monarch", monarch.transform.position + Vector3.up * (monarchHeight + 0.4f));
            if (original != null && original.activeSelf) yield return ("Original", original.transform.position + Vector3.up * Mathf.Max(1f, top));
        }

        // Cell lines round the footprint, on the ground, the footprint's
        // own edge drawn bright.
        void RebuildGrid()
        {
            if (Root == null) return;
            if (grid != null) Object.DestroyImmediate(grid);
            grid = new GameObject("Footprint Grid");
            grid.transform.SetParent(Root.transform, false);
            int margin = 3;
            float x0 = Spot.x - footprint.x * 0.5f - margin, x1 = Spot.x + footprint.x * 0.5f + margin;
            float z0 = Spot.z - footprint.y * 0.5f - margin, z1 = Spot.z + footprint.y * 0.5f + margin;
            var lines = new List<Vector3>();
            var edge = new List<Vector3>();
            void Line(List<Vector3> into, Vector3 a, Vector3 b)
            {
                const int steps = 8;
                for (int i = 0; i < steps; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)steps);
                    var q = Vector3.Lerp(a, b, (i + 1) / (float)steps);
                    into.Add(OnGround(p) + Vector3.up * 0.04f);
                    into.Add(OnGround(q) + Vector3.up * 0.04f);
                }
            }
            for (float x = x0; x <= x1 + 0.01f; x += 1f) Line(lines, new Vector3(x, 0, z0), new Vector3(x, 0, z1));
            for (float z = z0; z <= z1 + 0.01f; z += 1f) Line(lines, new Vector3(x0, 0, z), new Vector3(x1, 0, z));
            float fx0 = Spot.x - footprint.x * 0.5f, fx1 = Spot.x + footprint.x * 0.5f, fz0 = Spot.z - footprint.y * 0.5f, fz1 = Spot.z + footprint.y * 0.5f;
            for (int k = 0; k < 3; k++)
            {
                float e = 0.015f * k;
                Line(edge, new Vector3(fx0 - e, 0, fz0 - e), new Vector3(fx1 + e, 0, fz0 - e));
                Line(edge, new Vector3(fx1 + e, 0, fz0 - e), new Vector3(fx1 + e, 0, fz1 + e));
                Line(edge, new Vector3(fx1 + e, 0, fz1 + e), new Vector3(fx0 - e, 0, fz1 + e));
                Line(edge, new Vector3(fx0 - e, 0, fz1 + e), new Vector3(fx0 - e, 0, fz0 - e));
            }
            AddLines(grid, "Cells", lines, new Color(1f, 1f, 1f, 0.28f));
            AddLines(grid, "Footprint", edge, new Color(1f, 0.82f, 0.25f, 0.95f));
            Hide(grid);
        }

        void AddLines(GameObject parent, string name, List<Vector3> points, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var mesh = Own(new Mesh { name = name, indexFormat = IndexFormat.UInt32 });
            mesh.SetVertices(points);
            var idx = new int[points.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Own(Looks.Overlay(c));
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // The anchor: a pin where the model's origin stands and an arrow on
        // the ground to the south, where its front must face.
        void RebuildAnchor()
        {
            if (Root == null) return;
            if (anchor != null) Object.DestroyImmediate(anchor);
            anchor = new GameObject("Anchor");
            anchor.transform.SetParent(Root.transform, false);
            anchor.transform.position = Spot;
            var mat = Own(Looks.Overlay(new Color(1f, 0.45f, 0.15f, 0.95f)));
            var pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(pin.GetComponent<Collider>());
            pin.name = "Pin";
            pin.transform.SetParent(anchor.transform, false);
            pin.transform.localPosition = new Vector3(0, 0.35f, 0);
            pin.transform.localScale = new Vector3(0.05f, 0.35f, 0.05f);
            pin.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var arrow = new GameObject("Front");
            arrow.transform.SetParent(anchor.transform, false);
            var m = Own(new Mesh { name = "front arrow" });
            m.vertices = new[] { new Vector3(-0.12f, 0.06f, -0.2f), new Vector3(0.12f, 0.06f, -0.2f), new Vector3(0.12f, 0.06f, -0.75f), new Vector3(-0.12f, 0.06f, -0.75f),
                                 new Vector3(-0.3f, 0.06f, -0.75f), new Vector3(0.3f, 0.06f, -0.75f), new Vector3(0f, 0.06f, -1.15f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            arrow.AddComponent<MeshFilter>().sharedMesh = m;
            var r = arrow.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            Hide(anchor);
        }

        // ---- Each frame ----

        public void Tick(float dt, bool turning)
        {
            if (Root == null) return;
            if (turning) TurntableYaw = (TurntableYaw + Mathf.Min(dt, 0.25f) * 25f) % 360f;
            // The sea's clock, wind and weather.
            studioSea?.Update(Atmosphere, null, null, Camera);
            terrain?.Sea?.Update(Atmosphere, null, null, Camera);
            if (Atmosphere != null)
                foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>())
                    ps.Simulate(Mathf.Min(dt, 0.1f), true, false, false);
        }

        // Something on the stage moves by itself, such as falling snow.
        public bool Animating
        {
            get
            {
                if (Root == null) return false;
                foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>())
                    if (ps.gameObject.activeInHierarchy && ps.particleCount > 0) return true;
                return false;
            }
        }

        // Points the camera for a view and draws into rt, or into the Game
        // view when rt is null.
        public void Aim(StudioView v)
        {
            if (Camera == null) return;
            float pitch = v.Classic ? GameCamera.ClassicPitch : v.Pitch, yaw = v.Classic ? 0f : v.Yaw;
            var focus = v.Classic ? Spot : Spot + Vector3.up * Mathf.Clamp(fixedBounds.center.y, 0.3f, 6f);
            Camera.fieldOfView = StudioView.ClassicFov;
            // The classic view shows the model as it will stand in the game,
            // never as the turntable left it.
            if (turntable != null) turntable.transform.rotation = v.Classic ? Quaternion.identity : Quaternion.Euler(0, TurntableYaw, 0);
            Camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            Camera.transform.position = focus - Camera.transform.forward * v.Distance;
            if (!v.Classic)
            {
                var p = Camera.transform.position;
                p.y = Mathf.Max(p.y, GroundAt(p.x, p.z) + 0.3f);
                Camera.transform.position = p;
            }
            if (monarch != null) monarch.SetActive(ShowMonarch);
            if (original != null) original.SetActive(ShowOriginal);
            if (ghost != null) ghost.SetActive(ShowGhost && v.Classic);
            if (grid != null) grid.SetActive(ShowGrid);
            if (anchor != null) anchor.SetActive(ShowAnchor);
            if (inPlace != null) inPlace.SetActive(true);
            // Cards lie in the camera's plane on their anchor, drawn a little
            // toward the camera and shrunk to match, as EntityRenderer.CardMatrix does.
            foreach (var c in cards)
            {
                if (c == null || c.parent == null) continue;
                EffectRenderer.Nudged(c.parent.position, Camera.transform, EntityRenderer.CardNudge, out var pivot, out float k);
                c.SetPositionAndRotation(pivot, Camera.transform.rotation);
                c.localScale = Vector3.one * k;
            }
            Atmosphere?.Follow(focus, Camera.transform.position.y - focus.y, v.Distance);
        }

        // Draws into rt and returns where the labels fall on it, in its
        // pixels from the top left, for the ones in front of the camera.
        public List<(string label, Vector2 at)> Render(RenderTexture rt, StudioView v)
        {
            var labels = new List<(string, Vector2)>();
            if (Camera == null) return labels;
            Aim(v);
            var was = Camera.targetTexture;
            Camera.targetTexture = rt;
            Camera.Render();
            foreach (var (label, at) in Labels())
            {
                var s = Camera.WorldToScreenPoint(at);
                if (s.z > 0) labels.Add((label, new Vector2(s.x, rt.height - s.y)));
            }
            Camera.targetTexture = was;
            return labels;
        }

        public byte[] Screenshot(StudioView v, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var was = RenderTexture.active;
            try
            {
                Render(rt, v);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = was;
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // ---- Housekeeping ----

        T Own<T>(T o) where T : Object
        {
            o.hideFlags = HideFlags.DontSave;
            owned.Add(o);
            return o;
        }

        static void Hide(GameObject go)
        {
            if (go == null) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        }

        // Stages left by an earlier session, after a script reload.
        public static void RemoveStray()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == RootName && go.transform.parent == null && !UnityEditor.EditorUtility.IsPersistent(go))
                    Object.DestroyImmediate(go);
        }

        public void Dispose()
        {
            ClearModel();
            studioSea?.Dispose();
            studioSea = null;
            terrain?.Dispose();
            terrain = null;
            Atmosphere?.Dispose();
            Atmosphere = null;
            if (Root != null) Object.DestroyImmediate(Root);
            Root = null;
            Camera = null;
            foreach (var o in owned) if (o != null) Object.DestroyImmediate(o);
            owned.Clear();
            cards.Clear();
            ground = null;
            neutral = turntable = fixNode = original = ghost = monarch = grid = anchor = inPlace = null;
        }
    }
}
