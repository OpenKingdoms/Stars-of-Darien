// MetalCaptures.cs - hand-built lodestones under the game's sky, as the
// loader read them before (flat, no glow) and as it reads them now (metal,
// roughness, clear coat, textured glow and the sky's reflections), for a
// person to judge. Runs only with OKU_CAPTURE_DIR and OKU_METAL_GLBS, a
// semicolon list of .glb files.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class MetalCaptures
    {
        [UnityTest]
        public IEnumerator CaptureTheLodestones()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            string list = System.Environment.GetEnvironmentVariable("OKU_METAL_GLBS");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(list)) Assert.Ignore("set OKU_CAPTURE_DIR and OKU_METAL_GLBS to capture");
            Directory.CreateDirectory(dir);
            var mock = new MockBackend { StageSeconds = 0f };
            var root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);
            root.World.Entities.Hidden = u => true;
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            var size = mock.Terrain.Size;
            var at = new Vector3(size.x * 0.3f, 0, -size.y * 0.35f);
            at.y = mock.GroundHeight(at.x, at.z);
            gc.focus = at + Vector3.up * 1.2f;
            gc.pitch = 32f;
            gc.yaw = 20f;
            gc.Zoom(9f);
            var rt = RenderTexture.GetTemporary(640, 560, 24);
            foreach (var path in list.Split(';'))
            {
                if (!File.Exists(path)) continue;
                var go = GlbLoader.Load(path, out var error);
                Assert.IsNull(error, error);
                go.transform.position = at;
                go.SetActive(true);
                string name = Path.GetFileNameWithoutExtension(path) + "-" + Directory.GetParent(Path.GetDirectoryName(path)).Name;
                foreach (bool now in new[] { false, true })
                {
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            foreach (var k in new[] { "_OKU_PBR", "_EMISSION", "_CLEARCOAT" })
                                if (m.HasProperty("_Metallic")) { if (now) m.EnableKeyword(k); else m.DisableKeyword(k); }
                        }
                    root.World.Atmosphere.Reflections.enabled = now;
                    for (int i = 0; i < 12; i++) yield return null;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(dir, $"{name}-{(now ? "after" : "before")}.png"), tex.EncodeToPNG());
                    Object.Destroy(tex);
                }
                Object.Destroy(go);
            }
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(root.gameObject);
        }
    }
}
