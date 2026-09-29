// PaintCompareCaptures.cs - a capture tool, run by name: every model in a
// folder drawn on the real engine beside its copy in another folder (a
// baked copy with the pixels in it, say), from the classic view and a
// turned one, and the frames compared. OKU_PAINT_PAIRS lists the folders
// as old|new;old|new, and OKU_PAINT_OUT takes the frames and report.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    [Explicit("a capture tool: set OKU_PAINT_PAIRS and OKU_PAINT_OUT")]
    public class PaintCompareCaptures
    {
        const int Size = 384;

        [Test]
        public void CaptureAndCompare()
        {
            string pairs = Environment.GetEnvironmentVariable("OKU_PAINT_PAIRS");
            string outDir = Environment.GetEnvironmentVariable("OKU_PAINT_OUT");
            if (string.IsNullOrEmpty(pairs) || string.IsNullOrEmpty(outDir)) Assert.Ignore("OKU_PAINT_PAIRS and OKU_PAINT_OUT are not set");
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            Directory.CreateDirectory(outDir);
            bool async = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            var report = new StringBuilder();
            var backend = new EngineBackend();
            GlbLoader.SetPainter(backend);
            var made = new List<UnityEngine.Object>();
            try
            {
                var camGo = new GameObject("paint compare camera");
                made.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.3f, 0.35f, 0.3f, 1f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 500f;
                var sunGo = new GameObject("paint compare sun");
                made.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.2f;
                sunGo.transform.rotation = Quaternion.Euler(48f, 150f, 0f);
                RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);
                Shader.SetGlobalFloat("_OkuFogOn", 0f);
                var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
                made.Add(rt);
                cam.targetTexture = rt;
                var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                made.Add(read);

                foreach (var pair in pairs.Split(';').Where(p => p.Contains("|")))
                {
                    string oldDir = pair.Split('|')[0], newDir = pair.Split('|')[1];
                    foreach (var newPath in Directory.GetFiles(newDir, "*.glb").OrderBy(p => p))
                    {
                        string name = Path.GetFileNameWithoutExtension(newPath);
                        string oldPath = Path.Combine(oldDir, name + ".glb");
                        if (!File.Exists(oldPath)) continue;
                        var a = GlbLoader.Load(oldPath, out var e1);
                        var b = GlbLoader.Load(newPath, out var e2);
                        if (a == null || b == null) { report.AppendLine($"{name} unreadable {e1} {e2}"); continue; }
                        try
                        {
                            var bounds = Bounds(a);
                            bounds.Encapsulate(Bounds(b));
                            var line = new StringBuilder(name);
                            foreach (var (view, pitch, yaw) in new[] { ("classic", 63.4f, 0f), ("turned", 30f, 140f) })
                            {
                                var rot = Quaternion.Euler(pitch, yaw, 0f);
                                cam.transform.rotation = rot;
                                cam.transform.position = bounds.center - rot * Vector3.forward * 200f;
                                cam.orthographicSize = bounds.extents.magnitude * 1.05f;
                                var pa = Draw(cam, a, read);
                                var pb = Draw(cam, b, read);
                                int differ = 0, worst = 0;
                                for (int i = 0; i < pa.Length; i++)
                                {
                                    int d = Math.Max(Math.Abs(pa[i].r - pb[i].r), Math.Max(Math.Abs(pa[i].g - pb[i].g), Math.Abs(pa[i].b - pb[i].b)));
                                    if (d > 2) differ++;
                                    worst = Math.Max(worst, d);
                                }
                                line.Append($" {view} differ {differ} worst {worst}");
                                if (differ > 0 || view == "classic")
                                {
                                    Save(pa, Path.Combine(outDir, $"{name}_{view}_old.png"));
                                    Save(pb, Path.Combine(outDir, $"{name}_{view}_new.png"));
                                }
                            }
                            report.AppendLine(line.ToString());
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(a);
                            UnityEngine.Object.DestroyImmediate(b);
                        }
                    }
                }
            }
            finally
            {
                foreach (var o in made) UnityEngine.Object.DestroyImmediate(o);
                UnityEditor.ShaderUtil.allowAsyncCompilation = async;
                GlbLoader.SetPainter(null);
                backend.Dispose();
                File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
            }
        }

        static Bounds Bounds(GameObject root)
        {
            root.SetActive(true);
            var rs = root.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            root.SetActive(false);
            return b;
        }

        static Color32[] Draw(Camera cam, GameObject root, Texture2D read)
        {
            root.SetActive(true);
            // The first frame of a new shader or texture can still be settling.
            cam.Render();
            cam.Render();
            root.SetActive(false);
            var was = RenderTexture.active;
            RenderTexture.active = cam.targetTexture;
            read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            read.Apply();
            RenderTexture.active = was;
            return read.GetPixels32();
        }

        static void Save(Color32[] px, string path)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }
    }
}
