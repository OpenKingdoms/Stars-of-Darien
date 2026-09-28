// ModelHolePlayTests.cs - the original models as the engine hands them
// over, posed by their scripts, drawn alone from the classic tilt at eight
// headings and two zooms, each compared on screen with a reference drawn
// from both sides with its full-size texels. A model that covers less than
// its reference shows holes, from faces culled or texels lost. The suite
// checks a set of models known for thin parts and open shells, and
// OKU_HOLES_ALL=1 checks every unit and structure in the data.
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class ModelHolePlayTests
    {
        // Screen pixels to a world unit at the classic zoom, and zoomed out.
        static readonly float[] Densities = { 40f, 16f };

        static readonly HashSet<string> Known = new HashSet<string>
        {
            "ARASSH", "ARABUILD", "ARAKEEP", "ARACASTL", "ARANGATE", "ARADRAG", "ARASPY", "ARAFLY",
            "ARABOW", "ARACAN", "ARAGREN", "ARAFAST", "ARAWAR", "ARATRANS", "ARASWORD", "ARAARCH",
        };
        const float Pitch = 62f;

        // The reference is drawn double-sided from each picture's full-size
        // texels only, as the original's rasteriser drew them; the one-sided
        // copy tells culled faces apart from texels lost to smaller mips.
        sealed class Modes
        {
            readonly Dictionary<(Material, bool), Material> made = new Dictionary<(Material, bool), Material>();
            readonly Dictionary<Texture, Texture2D> full = new Dictionary<Texture, Texture2D>();
            public readonly List<Object> Owned = new List<Object>();

            public Material Reference(Material m) => Make(m, true);
            public Material OneSided(Material m) => Make(m, false);

            Material Make(Material m, bool twoSided)
            {
                if (m == null) return null;
                if (made.TryGetValue((m, twoSided), out var r)) return r;
                r = new Material(m);
                if (twoSided) r.SetFloat("_Cull", 0f);
                if (m.mainTexture is Texture2D t && t.mipmapCount > 1)
                {
                    if (!full.TryGetValue(t, out var f))
                    {
                        f = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false) { filterMode = t.filterMode, wrapMode = t.wrapMode };
                        f.SetPixels32(t.GetPixels32(0));
                        f.Apply(false);
                        full[t] = f;
                        Owned.Add(f);
                    }
                    r.mainTexture = f;
                }
                Owned.Add(r);
                return made[(m, twoSided)] = r;
            }
        }

        static IGameBackend Start(out EngineBackend engine)
        {
            engine = new EngineBackend();
            var s = new SkirmishSetup { MapId = "two castles", Seed = 5, LineOfSight = false, MapRevealed = true };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = engine.Sides[0].Id, Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = engine.Sides[0].Id, Colour = 1, Team = 1 });
            engine.StartSkirmish(s);
            for (int i = 0; i < 4000 && !engine.PumpLoading().Done; i++) { }
            return engine;
        }

        static readonly PiecePose[] poses = new PiecePose[256];

        // The pieces as the model's script poses them after a few seconds of
        // an animation, hidden pieces left out, as the game draws them.
        static List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> Parts(IGameBackend b, int id, PresentedModel model, string anim)
        {
            var list = new List<(Mesh, int, Material, Matrix4x4)>();
            int n = Mathf.Min(b.PoseModel(id, anim, 3f, poses), model.Pieces.Length);
            for (int p = 0; p < n; p++)
            {
                if (model.Pieces[p] == null || poses[p].Hidden) continue;
                for (int s = 0; s < model.Materials[p].Length; s++) list.Add((model.Pieces[p], s, model.Materials[p][s], poses[p].Matrix * model.Unscale));
            }
            return list;
        }

        static Bounds BoundsOf(List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> parts)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var part in parts)
            {
                var mb = part.mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var c = part.m.MultiplyPoint3x4(new Vector3((k & 1) == 0 ? mb.min.x : mb.max.x, (k & 2) == 0 ? mb.min.y : mb.max.y, (k & 4) == 0 ? mb.min.z : mb.max.z));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            return b;
        }

        const int Layer = 31;
        static readonly Color32 Clear = new Color32(255, 0, 255, 255);

        // Pixels covered, drawing the parts turned to a heading, seen from
        // the south at the classic tilt with ppu screen pixels to a unit.
        static int Coverage(Camera cam, List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> parts, Bounds b, float heading, float ppu, System.Func<Material, Material> pick, string png)
        {
            float r = b.extents.magnitude + 0.2f;
            int size = Mathf.Clamp(Mathf.CeilToInt(2 * r * ppu), 16, 1024);
            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            var rot = Quaternion.Euler(Pitch, 0, 0);
            cam.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * 50f, rot);
            cam.orthographicSize = r;
            cam.targetTexture = rt;
            var turn = Matrix4x4.TRS(b.center, Quaternion.Euler(0, heading, 0), Vector3.one) * Matrix4x4.Translate(-b.center);
            foreach (var part in parts)
            {
                var mat = pick(part.mat);
                if (mat == null) continue;
                Graphics.RenderMesh(new RenderParams(mat) { camera = cam, layer = Layer, shadowCastingMode = ShadowCastingMode.Off }, part.mesh, part.sub, turn * part.m);
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            int covered = 0;
            foreach (var p in tex.GetPixels32())
                if (System.Math.Abs(p.r - Clear.r) + System.Math.Abs(p.g - Clear.g) + System.Math.Abs(p.b - Clear.b) > 24) covered++;
            if (png != null) File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.Destroy(tex);
            return covered;
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator EveryModelCoversItsReference()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            yield return null;
            var cam = new GameObject("hole camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Clear;
            cam.cullingMask = 1 << Layer;
            cam.allowMSAA = false;
            int drawnAtAll = 0;
            var dir = System.Environment.GetEnvironmentVariable("OKU_HOLES_DIR");
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var watch = new HashSet<string> { "ARASSH", "ARABUILD" };
            // OKU_HOLES_ONLY limits the check to a comma list of models.
            var onlyList = System.Environment.GetEnvironmentVariable("OKU_HOLES_ONLY");
            var only = !string.IsNullOrEmpty(onlyList) ? new HashSet<string>(onlyList.ToUpperInvariant().Split(','))
                : System.Environment.GetEnvironmentVariable("OKU_HOLES_ALL") == "1" ? null : Known;
            Start(out var b);
            using (b)
            {
                var cache = new ModelCache(b);
                var modes = new Modes();
                var report = new StringBuilder();
                var holed = new List<string>();
                var seen = new HashSet<string>();
                int checkedModels = 0;
                foreach (var def in b.UnitDefs)
                {
                    if (string.IsNullOrEmpty(def.ObjectName) || !seen.Add(def.ObjectName.ToUpperInvariant())) continue;
                    if (only != null && !only.Contains(def.ObjectName.ToUpperInvariant())) continue;
                    int id = b.LoadModel(def.ObjectName, 0);
                    var model = cache.Get(id);
                    if (model == null || model.Override != null) continue;
                    float worst = 1f, worstCull = 1f, worstMip = 1f;
                    string worstAt = "";
                    bool any = false;
                    foreach (var anim in def.IsBuilding ? new[] { "" } : new[] { "", "walk" })
                    {
                    var parts = Parts(b, id, model, anim);
                    if (parts.Count == 0) continue;
                    any = true;
                    var bounds = BoundsOf(parts);
                    foreach (float ppu in Densities)
                        for (int k = 0; k < 8; k++)
                        {
                            float heading = k * 45f;
                            bool save = !string.IsNullOrEmpty(dir) && watch.Contains(def.ObjectName.ToUpperInvariant()) && k % 2 == 0;
                            string stem = save ? Path.Combine(dir, $"{def.ObjectName}-{(anim == "" ? "still" : anim)}-{heading:000}-{ppu:0}") : null;
                            int drawn = Coverage(cam, parts, bounds, heading, ppu, m => m, stem != null ? stem + "-drawn.png" : null);
                            int full = Coverage(cam, parts, bounds, heading, ppu, modes.Reference, stem != null ? stem + "-reference.png" : null);
                            int oneSided = stem != null ? Coverage(cam, parts, bounds, heading, ppu, modes.OneSided, stem + "-onesided.png") : full;
                            if (full <= 0) continue;
                            drawnAtAll++;
                            // A pixel or two of a part seen edge on may come and
                            // go with filtering; a hole is more than that.
                            float f = full - drawn <= Mathf.Max(12, full / 100) ? 1f : (float)drawn / full;
                            worstCull = Mathf.Min(worstCull, (float)oneSided / full);
                            worstMip = Mathf.Min(worstMip, oneSided > 0 ? (float)drawn / oneSided : 1f);
                            if (f < worst) { worst = f; worstAt = $"{(anim == "" ? "still" : anim)}, heading {heading}, {ppu} px a unit"; }
                        }
                    }
                    if (!any) continue;
                    checkedModels++;
                    report.AppendLine($"{def.ObjectName}: covers {worst:P1} of its reference at worst, {worstAt}; culling keeps {worstCull:P1}, smaller mips keep {worstMip:P1}");
                    if (worst < 1f) holed.Add($"{def.ObjectName} {worst:P1} ({worstAt})");
                }
                foreach (var o in modes.Owned) Object.DestroyImmediate(o);
                string text = $"{checkedModels} models, {holed.Count} with holes\n" + report;
                Debug.Log("Model holes: " + text);
                if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "coverage.txt"), text);
                Object.Destroy(cam.gameObject);
                Assert.Greater(checkedModels, only == Known ? Known.Count - 4 : only != null ? 0 : 50, "the models were checked");
                Assert.Greater(drawnAtAll, checkedModels, "the models drew");
                Assert.IsEmpty(holed, "models that show holes: " + string.Join(", ", holed));
                b.EndGame();
            }
        }
    }
}
