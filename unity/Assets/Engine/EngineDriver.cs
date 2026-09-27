// EngineDriver.cs - the real game in Unity. Boots OpenKingdoms as the
// okengine library on the player's own game files, starts a skirmish,
// runs the engine at its 60 Hz, and draws the map, the features and
// every unit posed by its unit script. Selection and move orders go in
// through the engine's command queue.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineDriver : MonoBehaviour
    {
        public string map = "two castles";
        public string kingdom = "aramon";
        public int aiPlayers = 1;
        public bool revealMap = true;

        public static EngineDriver Current { get; private set; }
        public bool Running { get; private set; }
        public string Error { get; private set; }
        public EngineTerrain Terrain { get; private set; }
        public EngineFeatures Features { get; private set; }
        public EngineModels Models { get; private set; }
        public int UnitCount { get; private set; }
        public OkxUnit[] Units => units;
        public int PiecesDrawn { get; private set; }

        readonly OkxUnit[] units = new OkxUnit[4096];
        readonly float[] pose = new float[128 * 12];
        readonly byte[] hidden = new byte[128];
        readonly HashSet<int> selected = new HashSet<int>();
        float accumulator;
        RtsCamera rig;
        Vector2 dragStart;
        bool dragging;

        IEnumerator Start()
        {
            Current = this;
            // A frame first, so a test or a menu can take this over before
            // the engine spends seconds loading a map.
            yield return null;
            Boot();
        }

        void Boot()
        {
            try
            {
                OkEngine.PreloadDependencies(EngineSettings.PluginDir);
                if (OkEngine.okx_api_version() != OkEngine.ApiVersion)
                    throw new InvalidOperationException($"okengine API {OkEngine.okx_api_version()}, this binding expects {OkEngine.ApiVersion}");
                if (OkEngine.okx_init(EngineSettings.GameDir, EngineSettings.DataDir) != 0)
                    throw new InvalidOperationException("okx_init: " + OkEngine.LastError);
                OkEngine.okx_set_override_dir(EngineSettings.OverrideDir.Replace('\\', '/'));
                var cfg = new OkxSkirmish
                {
                    map = map, kingdom = kingdom, aiPlayers = aiPlayers,
                    lineOfSight = 0, mapRevealed = revealMap ? 1 : 0, seed = 0
                };
                float t0 = Time.realtimeSinceStartup;
                if (OkEngine.okx_start_skirmish(ref cfg) != 0)
                    throw new InvalidOperationException("okx_start_skirmish: " + OkEngine.LastError);
                Models = new EngineModels();
                Terrain = new EngineTerrain();
                if (!Terrain.Build(transform)) throw new InvalidOperationException("no terrain");
                foreach (var mr in Terrain.Root.GetComponentsInChildren<MeshFilter>())
                    mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mr.sharedMesh;
                Features = new EngineFeatures();
                Features.Build(transform);
                SetUpScene();
                Running = true;
                Debug.Log($"OpenKingdoms: {map} loaded in {Time.realtimeSinceStartup - t0:0.0}s, " +
                          $"{Terrain.ChunkTextures} ground pictures, {Features.Models.Count} feature models, " +
                          $"{Features.SpriteCount} features still on sprites");
            }
            catch (Exception e)
            {
                Error = e.Message;
                Debug.LogError("OpenKingdoms engine did not start: " + e.Message);
            }
        }

        void SetUpScene()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 600f;
            rig = cam.GetComponent<RtsCamera>();
            if (rig == null) rig = cam.gameObject.AddComponent<RtsCamera>();
            var info = Terrain.Info;
            rig.boundsMin = new Vector2(0, -info.mapH * EngineSettings.PxToUnits);
            rig.boundsMax = new Vector2(info.mapW * EngineSettings.PxToUnits, 0);
            rig.groundHeight = Terrain.HeightAt;
            rig.height = 36f;
            rig.minHeight = 6f;
            rig.maxHeight = 160f;

            // Start over the local player's army.
            int n = OkEngine.okx_units(units, units.Length);
            int me = OkEngine.okx_local_player();
            Vector3 sum = Vector3.zero;
            int mine = 0;
            for (int i = 0; i < Mathf.Min(n, units.Length); i++)
            {
                if (units[i].player != me) continue;
                sum += EngineSettings.ToUnity(units[i].x, units[i].y, units[i].z);
                mine++;
            }
            rig.focus = mine > 0 ? sum / mine
                : new Vector3(info.mapW * EngineSettings.PxToUnits / 2, 0, -info.mapH * EngineSettings.PxToUnits / 2);

            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.15f;
                sun.color = new Color(1f, 0.96f, 0.88f);
                sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.2f);
            QualitySettings.shadowDistance = 180f;
            DynamicGI.UpdateEnvironment();
        }

        void Update()
        {
            if (!Running) return;
            int rate = 60;
            accumulator += Time.deltaTime;
            int steps = Mathf.FloorToInt(accumulator * rate);
            if (steps > 0)
            {
                steps = Mathf.Min(steps, 8);
                OkEngine.okx_tick(steps);
                accumulator -= steps / (float)rate;
                if (accumulator > 0.25f) accumulator = 0;
            }
            UnitCount = Mathf.Min(OkEngine.okx_units(units, units.Length), units.Length);
            HandleInput();
            Draw();
        }

        void Draw()
        {
            int pieces = 0;
            for (int i = 0; i < UnitCount; i++)
            {
                var u = units[i];
                var model = Models.Get(u.model);
                if (model == null) continue;
                int nodes = OkEngine.okx_unit_pose(u.handle, pose, hidden, 128);
                for (int k = 0; k < nodes && k < model.NodeCount; k++)
                {
                    if (hidden[k] != 0) continue;
                    pieces += DrawPiece(model, k, EngineSettings.PoseToUnity(pose, k * 12) * model.Unscale);
                }
            }
            foreach (var f in Features.Models)
            {
                var model = Models.Get(f.Model);
                if (model == null) continue;
                for (int k = 0; k < f.Pieces.Length && k < model.NodeCount; k++)
                    pieces += DrawPiece(model, k, f.Pieces[k] * model.Unscale);
            }
            PiecesDrawn = pieces;
        }

        static int DrawPiece(EngineModel model, int k, Matrix4x4 m)
        {
            var mesh = model.Pieces[k];
            if (mesh == null) return 0;
            var mats = model.Materials[k];
            for (int s = 0; s < mats.Length; s++)
            {
                var rp = new RenderParams(mats[s])
                {
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true
                };
                Graphics.RenderMesh(rp, mesh, s, m);
            }
            return 1;
        }

        Vector3 UnitPosition(in OkxUnit u) => EngineSettings.ToUnity(u.x, u.y, u.z);

        void HandleInput()
        {
            var cam = Camera.main;
            if (cam == null) return;
            int me = OkEngine.okx_local_player();
            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) { dragStart = mouse; dragging = true; }
            if (Input.GetMouseButtonUp(0) && dragging)
            {
                dragging = false;
                if (!Input.GetKey(KeyCode.LeftShift)) selected.Clear();
                var box = Rect.MinMaxRect(Mathf.Min(dragStart.x, mouse.x), Mathf.Min(dragStart.y, mouse.y),
                                          Mathf.Max(dragStart.x, mouse.x), Mathf.Max(dragStart.y, mouse.y));
                bool click = box.width < 6 && box.height < 6;
                float best = 30f;
                int pick = -1;
                for (int i = 0; i < UnitCount; i++)
                {
                    if (units[i].player != me || units[i].state != OkEngine.UnitActive) continue;
                    Vector3 s = cam.WorldToScreenPoint(UnitPosition(units[i]) + Vector3.up);
                    if (s.z <= 0) continue;
                    if (click)
                    {
                        float d = Vector2.Distance(mouse, s);
                        if (d < best) { best = d; pick = units[i].handle; }
                    }
                    else if (box.Contains(s)) selected.Add(units[i].handle);
                }
                if (pick >= 0) selected.Add(pick);
            }
            if (Input.GetMouseButtonDown(1) && selected.Count > 0 &&
                Physics.Raycast(cam.ScreenPointToRay(mouse), out RaycastHit hit, 2000f))
            {
                Vector2 e = EngineSettings.ToEngine(hit.point);
                foreach (int h in selected)
                    OkEngine.okx_command((int)OkxCmd.Move, h, (int)e.x, (int)e.y, -1, -1, 0);
            }
            if (Input.GetKeyDown(KeyCode.S))
                foreach (int h in selected) OkEngine.okx_command((int)OkxCmd.Stop, h, 0, 0, -1, -1, 0);
        }

        void OnGUI()
        {
            if (!Running)
            {
                GUI.Label(new Rect(10, 10, 900, 40), Error == null ? "Loading OpenKingdoms..." : "Engine error: " + Error);
                return;
            }
            GUI.Label(new Rect(10, 10, 900, 20),
                $"{map}   tick {OkEngine.okx_tick_count()}   units {UnitCount}   pieces {PiecesDrawn}   selected {selected.Count}");
            GUI.Label(new Rect(10, 28, 900, 20),
                $"{Features.Models.Count} feature models, {Features.SpriteCount} features still on sprites ({Features.SpriteKinds} kinds)");
            GUI.Label(new Rect(10, Screen.height - 26, 1100, 20),
                "left drag: select   right click: move   S: stop   arrows or edge: pan   Q/E: turn   PgUp/PgDn: tilt   wheel: zoom");
            var cam = Camera.main;
            if (cam == null || Event.current.type != EventType.Repaint) return;
            for (int i = 0; i < UnitCount; i++)
            {
                if (!selected.Contains(units[i].handle)) continue;
                Vector3 s = cam.WorldToScreenPoint(UnitPosition(units[i]) + Vector3.up * 2.5f);
                if (s.z <= 0) continue;
                float frac = units[i].maxHealth > 0 ? Mathf.Clamp01(units[i].health / (float)units[i].maxHealth) : 1f;
                var r = new Rect(s.x - 16, Screen.height - s.y, 32, 4);
                GUI.color = Color.black;
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.Lerp(Color.red, Color.green, frac);
                GUI.DrawTexture(new Rect(r.x, r.y, 32 * frac, 4), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            Features?.Dispose();
            Terrain?.Dispose();
            Models?.Dispose();
            if (Running) OkEngine.okx_end_game();
            Running = false;
        }
    }
}
