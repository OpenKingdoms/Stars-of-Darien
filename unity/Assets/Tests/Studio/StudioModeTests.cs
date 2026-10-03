// StudioModeTests.cs - Studio Mode on the mock: every format comes in as the
// game's glb, checks and fixes, baking into the file, writing where the game
// looks, and the studio scene opening with the sample on the stage.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using OpenKingdomsUnity.Studio;
using UnityEditor;
using UnityEngine;
using Level = OpenKingdomsUnity.Studio.ModelCheck.Level;
using FixKind = OpenKingdomsUnity.Studio.ModelCheck.FixKind;

namespace OpenKingdomsUnity.Tests
{
    // A temp folder, the stand-in world, and the machine's studio settings
    // put back afterwards.
    public abstract class StudioFixture
    {
        protected string temp;
        bool savedMock;
        static readonly string[] Prefs = { "oku.studio.map", "oku.studio.climate", "oku.studio.weather", "oku.studio.time", "oku.studio.sea", "oku.studio.shadows", "oku.studio.team", "oku.studio.lastModel", "oku.sprites.dir" };
        readonly Dictionary<string, string> savedPrefs = new Dictionary<string, string>();

        [SetUp]
        public void Before()
        {
            temp = Path.Combine(Path.GetTempPath(), "oku-studio-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);
            savedMock = StudioBackend.PreferMock;
            StudioBackend.PreferMock = true;
            savedPrefs.Clear();
            foreach (var k in Prefs) if (EditorPrefs.HasKey(k)) savedPrefs[k] = Pref(k);
        }

        [TearDown]
        public void After()
        {
            foreach (var k in Prefs)
            {
                if (!savedPrefs.TryGetValue(k, out var v)) EditorPrefs.DeleteKey(k);
                else if (k == "oku.studio.sea" || k == "oku.studio.shadows") EditorPrefs.SetBool(k, v == "True");
                else if (k == "oku.studio.weather" || k == "oku.studio.time" || k == "oku.studio.team") EditorPrefs.SetInt(k, int.Parse(v));
                else EditorPrefs.SetString(k, v);
            }
            StudioBackend.PreferMock = savedMock;
            StudioBackend.Release();
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        static string Pref(string k) =>
            k == "oku.studio.sea" || k == "oku.studio.shadows" ? EditorPrefs.GetBool(k).ToString()
            : k == "oku.studio.weather" || k == "oku.studio.time" || k == "oku.studio.team" ? EditorPrefs.GetInt(k).ToString()
            : EditorPrefs.GetString(k);

        protected static string Sample => StudioSession.SamplePath;
    }

    public class StudioModeTests : StudioFixture
    {
        // ---- Coming in ----

        [Test]
        public void TheSampleLoadsWithItsFacts()
        {
            var m = StudioModel.Load(Sample, out var error);
            Assert.IsNull(error);
            try
            {
                Assert.IsTrue(m.Facts.HasGeometry);
                Assert.AreEqual(156, m.Facts.Triangles);
                Assert.AreEqual(1, m.Facts.Textures.Count);
                Assert.AreEqual(64, m.Facts.Textures[0].Width);
                Assert.IsTrue(m.Facts.Textures[0].Readable);
                Assert.AreEqual(0f, m.Facts.Bounds.min.y, 1e-3f, "it stands on the ground");
                Assert.AreEqual(2f, m.Facts.Bounds.max.y, 1e-3f, "the flag's tip");
                Assert.Less(m.Facts.Bounds.center.z, 0f, "the bucket hangs on the south side, Unity -z");
                CollectionAssert.Contains(m.Facts.NodeNames, "bucket");
            }
            finally { m.Dispose(); }
        }

        [Test]
        public void AGltfIsPackedWithItsBufferAndPictures()
        {
            string gltf = SplitSample(temp, true);
            var m = StudioModel.Load(gltf, out var error);
            Assert.IsNull(error, error);
            try
            {
                Assert.AreEqual(156, m.Facts.Triangles);
                Assert.AreEqual(1, m.Facts.Textures.Count);
                Assert.IsTrue(m.Facts.Textures[0].Readable, "the picture beside it came in");
                Assert.AreEqual(0, m.Facts.MissingTextures.Count);
            }
            finally { m.Dispose(); }

            File.Delete(Path.Combine(temp, "stone.png"));
            m = StudioModel.Load(gltf, out error);
            Assert.IsNull(error);
            try
            {
                CollectionAssert.Contains(m.Facts.MissingTextures, "stone.png");
                Assert.IsTrue(ModelCheck.Run(m.Facts, StudioFix.None, Feature()).Any(i => i.Level == Level.Warning && i.Text.Contains("stone.png")));
            }
            finally { m.Dispose(); }
        }

        // A one-cell box as an .obj, with a material file naming two pictures.
        internal static string BoxObj(string dir, string name, bool withPicture)
        {
            string obj = Path.Combine(dir, name + ".obj");
            File.WriteAllText(obj, "mtllib " + name + ".mtl\no box\nv -0.5 0 -0.5\nv 0.5 0 -0.5\nv 0.5 1 -0.5\nv -0.5 1 -0.5\nv -0.5 0 0.5\nv 0.5 0 0.5\nv 0.5 1 0.5\nv -0.5 1 0.5\n" +
                "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nusemtl stone\nf 1/1 2/2 3/3 4/4\nf 5/1 8/2 7/3 6/4\nf 1/1 5/2 6/3 2/4\nf 2/1 6/2 7/3 3/4\nf 3/1 7/2 8/3 4/4\nf 5/1 1/2 4/3 8/4\n");
            File.WriteAllText(Path.Combine(dir, name + ".mtl"), "newmtl stone\nKd 0.8 0.8 0.8\nmap_Kd stone.png\nmap_bump leaves.png\n");
            if (withPicture)
            {
                var t = new Texture2D(4, 4);
                File.WriteAllBytes(Path.Combine(dir, "stone.png"), t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
            }
            return obj;
        }

        internal static void RemoveImported(string name)
        {
            string root = Path.Combine(StudioModel.ProjectDir, StudioModel.ImportedFolder);
            if (!Directory.Exists(root)) return;
            foreach (var d in Directory.GetDirectories(root, name + "-*"))
                if (!AssetDatabase.DeleteAsset(StudioModel.ToAsset(d))) Directory.Delete(d, true);
        }

        [Test]
        public void AnObjComesInThroughUnitysImporterWithItsPictures()
        {
            string obj = BoxObj(temp, "studio_test_box", true);
            StudioModel m = null;
            try
            {
                m = StudioModel.Load(obj, out var error);
                Assert.IsNull(error, error);
                Assert.AreEqual(12, m.Facts.Triangles);
                Assert.AreEqual(1f, m.Facts.Bounds.size.x, 0.02f);
                Assert.AreEqual(1f, m.Facts.Bounds.size.y, 0.02f);
                var copies = Directory.GetDirectories(Path.Combine(StudioModel.ProjectDir, StudioModel.ImportedFolder), "studio_test_box-*");
                Assert.AreEqual(1, copies.Length, "copied into a folder of its own for Unity to import");
                Assert.IsTrue(File.Exists(Path.Combine(copies[0], "studio_test_box.obj")));
                Assert.IsTrue(File.Exists(Path.Combine(copies[0], "stone.png")), "with the picture its material names");
                CollectionAssert.Contains(m.Facts.MissingTextures, "leaves.png", "a picture that is not there is caught");
                CollectionAssert.DoesNotContain(m.Facts.MissingTextures, "stone.png");
                Assert.IsTrue(ModelCheck.Run(m.Facts, StudioFix.None, Feature()).Any(i => i.Level == Level.Warning && i.Text.Contains("leaves.png")));
            }
            finally
            {
                m?.Dispose();
                RemoveImported("studio_test_box");
            }
        }

        [Test]
        public void TheGlbWriterRoundTrips()
        {
            var root = new GameObject("thing");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                root.transform.position = new Vector3(5, 0, 5);
                root.transform.rotation = Quaternion.Euler(0, 90, 0);
                cube.name = "torso";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localPosition = new Vector3(1, 0.5f, -2);
                cube.transform.localRotation = Quaternion.Euler(0, 30, 0);
                var glb = GlbWriter.Write(root);
                var back = GlbLoader.Load(glb, "thing", out var error);
                Assert.IsNull(error);
                try
                {
                    var t = back.transform.Find("thing/torso");
                    Assert.IsNotNull(t, "names and the tree survive");
                    Assert.AreEqual(Vector3.zero, t.parent.localPosition, "the root's place is not kept, its origin is the anchor");
                    Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 90, 0), t.parent.localRotation), 0.01f, "its turn is");
                    Assert.Less(Vector3.Distance(new Vector3(1, 0.5f, -2), t.localPosition), 1e-4f, "z flips out and back");
                    Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 30, 0), t.localRotation), 0.01f);
                    var mesh = t.GetComponent<MeshFilter>().sharedMesh;
                    Assert.AreEqual(24, mesh.vertexCount);
                    var tri = mesh.triangles;
                    var v = mesh.vertices;
                    var n = mesh.normals;
                    var face = Vector3.Cross(v[tri[1]] - v[tri[0]], v[tri[2]] - v[tri[0]]);
                    Assert.Greater(Vector3.Dot(face, n[tri[0]]), 0f, "triangles still face out");
                }
                finally { UnityEngine.Object.DestroyImmediate(back); }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // A quad with a picture that is clear on its left half, in a material
        // cut out or not.
        static GameObject Leaf(bool cutOut, List<UnityEngine.Object> made)
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "leaf" };
            var px = new Color32[64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(40, 160, 40, (byte)(i % 8 < 4 ? 0 : 255));
            tex.SetPixels32(px);
            tex.Apply();
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = cutOut ? "leaf_cut" : "leaf_solid", mainTexture = tex };
            if (cutOut) { mat.SetFloat("_Mode", 1); mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", 0.4f); }
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            made.Add(tex);
            made.Add(mat);
            made.Add(go);
            return go;
        }

        [Test]
        public void ASeeThroughPictureOnAnOpaqueMaterialIsCaught()
        {
            var made = new List<UnityEngine.Object>();
            try
            {
                foreach (bool cut in new[] { false, true })
                {
                    var glb = GlbWriter.Write(Leaf(cut, made));
                    var file = GlbFile.Read(glb, out _);
                    var mat = MiniJson.Arr(file.Json, "materials")[0];
                    Assert.AreEqual(cut ? "MASK" : null, MiniJson.Text(mat, "alphaMode"), "the writer keeps the cut-out mode");
                    var go = GlbLoader.Load(glb, "leaf", out var error);
                    Assert.IsNull(error);
                    made.Add(go);
                    var facts = StudioModel.FactsOf(file, go);
                    var issues = ModelCheck.Run(facts, StudioFix.None, Feature(1, 1, 1f));
                    bool warned = issues.Any(i => i.Level == Level.Warning && i.Text.Contains("see-through"));
                    Assert.AreEqual(!cut, warned, string.Join(" | ", issues));
                    if (cut) Assert.AreEqual(0.4f, go.GetComponentInChildren<Renderer>().sharedMaterial.GetFloat("_Cutoff"), 1e-4f, "the game cuts it out");
                }
            }
            finally { foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        [Test]
        public void ModelsMadeFromThePlayersFilesAreKnown()
        {
            Assert.IsTrue(StudioModel.FromPlayersFiles("Assets/Overrides/Generated/AraTree01.glb"));
            Assert.IsTrue(StudioModel.FromPlayersFiles(Path.Combine(StudioModel.ProjectDir, "Assets/Overrides/Generated/Units/ARALODE.glb")));
            Assert.IsFalse(StudioModel.FromPlayersFiles("Assets/Overrides/Drop/tree.glb"));
            Assert.IsFalse(StudioModel.FromPlayersFiles("Assets/Overrides/GeneratedByHand/tree.glb"));
            Assert.IsFalse(StudioModel.FromPlayersFiles(Sample));
            StudioTargets.CatalogDir = temp;
            Assert.IsTrue(StudioModel.FromPlayersFiles(Path.Combine(temp, "models", "AraTree01.glb")), "anything under the sprite catalog");
        }

        [Test]
        public void AModelStampedAsMadeFromThePlayersFilesNeverGoesIn()
        {
            var f = GlbFile.Read(File.ReadAllBytes(Sample), out _);
            var asset = (Dictionary<string, object>)f.Json["asset"];
            asset["extras"] = new Dictionary<string, object> { [GlbCheck.PlayersFilesKey] = true };
            string path = Path.Combine(temp, "carved.glb");
            var bytes = f.Write();
            File.WriteAllBytes(path, bytes);
            var m = StudioModel.Load(path, out var error);
            Assert.IsNull(error, error);
            try { Assert.IsTrue(m.Facts.FromPlayersFiles, "the stamp is read"); }
            finally { m.Dispose(); }
            var written = OverrideWriter.Write(temp, Feature(), bytes, StudioFix.None.Scaled(2f), null, out error);
            Assert.IsEmpty(written, "refused wherever it came from");
            Assert.AreEqual(OverrideWriter.PlayersFilesRefused, error);

            var g = GlbFile.Read(File.ReadAllBytes(Sample), out _);
            ((Dictionary<string, object>)MiniJson.Arr(g.Json, "nodes")[0])["extras"] = new Dictionary<string, object> { [GlbCheck.PlayersFilesKey] = 1.0 };
            Assert.IsTrue(GlbCheck.FromPlayersFiles(g), "Blender may write the stamp on a node as 1");
            Assert.IsFalse(GlbCheck.FromPlayersFiles(GlbFile.Read(File.ReadAllBytes(Sample), out _)));

            StudioTargets.CatalogDir = temp;
            Assert.IsEmpty(OverrideWriter.Write(temp, Feature(), File.ReadAllBytes(Sample), StudioFix.None, null, out error, Path.Combine(temp, "plain.glb")), "a file from the sprite catalog's folder");
            Assert.AreEqual(OverrideWriter.PlayersFilesRefused, error);
        }

        [Test]
        public void AGlbTheGameCannotReadIsRefusedInPlainWords()
        {
            string Try(Action<GlbFile> spoil)
            {
                var f = GlbFile.Read(File.ReadAllBytes(Sample), out _);
                spoil(f);
                string path = Path.Combine(temp, Guid.NewGuid().ToString("N") + ".glb");
                File.WriteAllBytes(path, f.Write());
                StudioModel m = null;
                string error = null;
                Assert.DoesNotThrow(() => m = StudioModel.Load(path, out error));
                Assert.IsNull(m);
                return error;
            }
            object Indices(GlbFile f)
            {
                var prim = MiniJson.Arr(MiniJson.Arr(f.Json, "meshes")[0], "primitives")[0];
                return MiniJson.Arr(f.Json, "accessors")[MiniJson.Int(prim, "indices")];
            }
            StringAssert.Contains("Compression", Try(f =>
            {
                ((Dictionary<string, object>)Indices(f)).Remove("bufferView");
                f.Json["extensionsRequired"] = new List<object> { "KHR_draco_mesh_compression" };
            }));
            StringAssert.Contains("Compression", Try(f => ((Dictionary<string, object>)Indices(f)).Remove("bufferView")), "geometry with nothing behind it");
            StringAssert.Contains(".bin", Try(f => GlbFile.List(f.Json, "buffers").Add(new Dictionary<string, object> { ["uri"] = "extra.bin", ["byteLength"] = 4.0 })));
            StringAssert.Contains("damaged", Try(f => ((Dictionary<string, object>)MiniJson.Arr(f.Json, "nodes")[0])["mesh"] = 99.0));
            StringAssert.Contains("PNG or JPEG", Try(f => f.Json["extensionsRequired"] = new List<object> { "KHR_texture_basisu" }));
            string junk = Path.Combine(temp, "junk.glb");
            File.WriteAllBytes(junk, new byte[] { 1, 2, 3 });
            Assert.IsNull(StudioModel.Load(junk, out var why));
            StringAssert.Contains("isn't a glTF Binary", why);
            Assert.IsFalse(Resources.FindObjectsOfTypeAll<GameObject>().Any(g => g != null && g.name.StartsWith("studio-load-")), "nothing half-built is left behind");
        }

        [Test]
        public void TheWindowLayoutCanBeKeptAndPutBack()
        {
            Assert.IsNotNull(StudioMode.SaveLayoutMethod(), "Unity's layout saving is where Studio Mode looks for it");
            Assert.IsNotNull(StudioMode.LoadLayoutMethod());
        }

        // ---- Checks and fixes ----

        static StudioTarget Feature(int w = 1, int d = 1, float height = 0) =>
            new StudioTarget { Kind = TargetKind.Feature, Name = "TestTree", Footprint = new Vector2Int(w, d), Height = height };

        static ModelFacts Facts(Bounds b, int tris = 200)
        {
            var f = new ModelFacts { Bounds = b, HasGeometry = true, Triangles = tris };
            f.Textures.Add(new TextureFact { Name = "bark", Width = 512, Height = 512 });
            return f;
        }

        static bool Has(List<ModelCheck.Issue> issues, Level l, FixKind k) => issues.Any(i => i.Level == l && i.Fix == k);

        [Test]
        public void ANeatModelPasses()
        {
            var issues = ModelCheck.Run(Facts(new Bounds(new Vector3(0, 7, 0), new Vector3(3, 14, 3))), StudioFix.None, Feature(1, 1, 14f));
            Assert.IsFalse(issues.Any(i => i.Level >= Level.Warning), string.Join("\n", issues));
            Assert.IsFalse(ModelCheck.Blocks(issues));
        }

        [Test]
        public void NothingToReplaceBlocksUseInGame()
        {
            var issues = ModelCheck.Run(Facts(new Bounds(Vector3.up, Vector3.one * 2)), StudioFix.None, new StudioTarget());
            Assert.IsTrue(ModelCheck.Blocks(issues));
            var card = new StudioTarget { Kind = TargetKind.UnitCard, Name = "lode", ObjectName = "ARALODE" };
            Assert.IsTrue(ModelCheck.Blocks(ModelCheck.Run(Facts(new Bounds(Vector3.up, Vector3.one * 2)), StudioFix.None, card)), "a card needs its piece");
            Assert.IsTrue(ModelCheck.Blocks(ModelCheck.Run(new ModelFacts(), StudioFix.None, Feature())), "an empty model");
        }

        [Test]
        public void AnOffCentreFloatingModelIsCentredOnTheAnchor()
        {
            var f = Facts(new Bounds(new Vector3(3, 2.5f, -1), new Vector3(2, 3, 2)));
            var t = Feature(2, 2, 3f);
            var issues = ModelCheck.Run(f, StudioFix.None, t);
            Assert.IsTrue(Has(issues, Level.Warning, FixKind.Recentre));
            Assert.IsTrue(Has(issues, Level.Warning, FixKind.StandOnGround), string.Join("\n", issues));
            var fix = ModelCheck.Apply(FixKind.Recentre, f, StudioFix.None, t);
            var b = fix.Apply(f.Bounds);
            Assert.AreEqual(1f, b.min.y, 1e-4f, "centring keeps the ground line");
            Assert.AreEqual(0f, b.center.x, 1e-4f);
            Assert.AreEqual(0f, b.center.z, 1e-4f);
            Assert.IsFalse(Has(ModelCheck.Run(f, fix, t), Level.Warning, FixKind.Recentre));
            fix = ModelCheck.Apply(FixKind.StandOnGround, f, fix, t);
            Assert.AreEqual(0f, fix.Apply(f.Bounds).min.y, 1e-4f);
            Assert.AreEqual(0f, fix.Apply(f.Bounds).center.x, 1e-4f);
            Assert.IsFalse(ModelCheck.Run(f, fix, t).Any(i => i.Level >= Level.Warning), string.Join("\n", ModelCheck.Run(f, fix, t)));
        }

        [Test]
        public void AFoundationBelowTheGroundStandsWhereItWasExported()
        {
            // A standing stone 4 cells tall with 0.6 of it below the ground.
            var f = Facts(new Bounds(new Vector3(0.2f, 1.4f, 0), new Vector3(1.5f, 4, 1.5f)));
            var t = Feature(1, 1, 4f);
            var issues = ModelCheck.Run(f, StudioFix.None, t);
            Assert.IsFalse(issues.Any(i => i.Level >= Level.Warning), string.Join("\n", issues));
            Assert.IsTrue(issues.Any(i => i.Level == Level.Good && i.Text.Contains("foundation")), string.Join("\n", issues));

            var b = ModelCheck.Apply(FixKind.Recentre, f, StudioFix.None, t).Apply(f.Bounds);
            Assert.AreEqual(-0.6f, b.min.y, 1e-4f, "centring keeps the foundation");
            Assert.AreEqual(0f, b.center.x, 1e-4f);

            // Resizes scale about the anchor on the ground, foundation and all.
            var grown = ModelCheck.Apply(FixKind.MatchSize, f, StudioFix.None, Feature(1, 1, 8f));
            Assert.AreEqual(8f, grown.Apply(f.Bounds).size.y, 1e-3f);
            Assert.AreEqual(-1.2f, grown.Apply(f.Bounds).min.y, 1e-3f);
            var cm = Facts(new Bounds(new Vector3(0, 140, 0), new Vector3(150, 400, 150)));
            var shrunk = ModelCheck.Apply(FixKind.Shrink100, cm, StudioFix.None, t);
            Assert.AreEqual(-0.6f, shrunk.Apply(cm.Bounds).min.y, 1e-3f);
            Assert.IsFalse(ModelCheck.Run(cm, shrunk, t).Any(i => i.Level >= Level.Warning), string.Join("\n", ModelCheck.Run(cm, shrunk, t)));
        }

        [Test]
        public void AModelExportedTooLowStillWarns()
        {
            // Half of it under the ground: the origin was put at its middle.
            var f = Facts(new Bounds(Vector3.zero, new Vector3(1.5f, 4, 1.5f)));
            var t = Feature(1, 1, 4f);
            var issues = ModelCheck.Run(f, StudioFix.None, t);
            Assert.IsTrue(Has(issues, Level.Warning, FixKind.StandOnGround), string.Join("\n", issues));
            Assert.IsFalse(Has(issues, Level.Warning, FixKind.Recentre), "it is centred");
            var fix = ModelCheck.Apply(FixKind.StandOnGround, f, StudioFix.None, t);
            Assert.AreEqual(0f, fix.Apply(f.Bounds).min.y, 1e-4f);
            var deep = Facts(new Bounds(new Vector3(0, 2f - 1.2f, 0), new Vector3(1.5f, 4, 1.5f)));
            Assert.IsTrue(Has(ModelCheck.Run(deep, StudioFix.None, t), Level.Warning, FixKind.StandOnGround), "deeper than a quarter of its height");
        }

        [Test]
        public void AModelTheWrongSizeIsScaledToTheOriginal()
        {
            // Exported in centimetres: a 14 cell tree 1400 tall.
            var f = Facts(new Bounds(new Vector3(0, 700, 0), new Vector3(300, 1400, 300)));
            var t = Feature(1, 1, 14f);
            var issues = ModelCheck.Run(f, StudioFix.None, t);
            Assert.IsTrue(Has(issues, Level.Warning, FixKind.Shrink100));
            var fix = ModelCheck.Apply(FixKind.Shrink100, f, StudioFix.None, t);
            Assert.AreEqual(14f, fix.Apply(f.Bounds).size.y, 1e-3f);
            Assert.IsFalse(ModelCheck.Run(f, fix, t).Any(i => i.Level >= Level.Warning), "centred and sized in one click");

            var small = Facts(new Bounds(new Vector3(0, 1, 0), new Vector3(1, 2, 1)));
            Assert.IsTrue(Has(ModelCheck.Run(small, StudioFix.None, t), Level.Warning, FixKind.MatchSize));
            var grown = ModelCheck.Apply(FixKind.MatchSize, small, StudioFix.None, t);
            Assert.AreEqual(14f, grown.Apply(small.Bounds).size.y, 1e-3f);
            Assert.AreEqual(0f, grown.Apply(small.Bounds).min.y, 1e-3f);
        }

        [Test]
        public void AUnitIsSizedAgainstItsOriginalModel()
        {
            var t = new StudioTarget { Kind = TargetKind.Unit, Name = "knight", ObjectName = "knight" };
            var original = new Bounds(new Vector3(0, 1, 0), new Vector3(1, 2, 0.8f));
            var f = Facts(new Bounds(new Vector3(0, 3, 0), new Vector3(3, 6, 2.4f)));
            Assert.IsTrue(Has(ModelCheck.Run(f, StudioFix.None, t, original), Level.Warning, FixKind.MatchSize));
            var fix = ModelCheck.Apply(FixKind.MatchSize, f, StudioFix.None, t, original);
            Assert.AreEqual(2f, fix.Apply(f.Bounds).size.y, 1e-3f);
        }

        [Test]
        public void AModelLyingTheOtherWayIsTurnedAQuarter()
        {
            // A wall three cells east to west, modelled north to south.
            var f = Facts(new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1, 1, 3)));
            var t = Feature(3, 1, 1f);
            Assert.IsTrue(Has(ModelCheck.Run(f, StudioFix.None, t), Level.Warning, FixKind.TurnQuarter));
            var fix = ModelCheck.Apply(FixKind.TurnQuarter, f, StudioFix.None, t);
            Assert.AreEqual(3f, fix.Apply(f.Bounds).size.x, 1e-3f);
            Assert.IsFalse(Has(ModelCheck.Run(f, fix, t), Level.Warning, FixKind.TurnQuarter));
        }

        [Test]
        public void TrianglesAndPicturesAreAdviceUpToTheGuardsAndRefusedPastThem()
        {
            var f = Facts(new Bounds(new Vector3(0, 7, 0), new Vector3(3, 14, 3)), 9000);
            f.Textures.Add(new TextureFact { Name = "huge", Width = 4096, Height = 4096 });
            f.Textures.Add(new TextureFact { Name = "odd.webp", Readable = false });
            f.MissingTextures.Add("leaves.png");
            var issues = ModelCheck.Run(f, StudioFix.None, Feature(1, 1, 14f));
            Assert.IsTrue(issues.Any(i => i.Level == Level.Note && i.Text.Contains("9,000")), string.Join("\n", issues));
            Assert.IsTrue(issues.Any(i => i.Level == Level.Note && i.Text.Contains("huge") && i.Text.Contains("4096")));
            Assert.IsTrue(issues.Any(i => i.Level == Level.Warning && i.Text.Contains("odd.webp")));
            Assert.IsTrue(issues.Any(i => i.Level == Level.Warning && i.Text.Contains("leaves.png")));
            Assert.IsFalse(ModelCheck.Blocks(issues), "advice does not block");

            var heavy = Facts(new Bounds(new Vector3(0, 7, 0), new Vector3(3, 14, 3)), 100001);
            heavy.Textures.Add(new TextureFact { Name = "photo", Width = 6000, Height = 4000 });
            issues = ModelCheck.Run(heavy, StudioFix.None, Feature(1, 1, 14f));
            Assert.IsTrue(issues.Any(i => i.Level == Level.Problem && i.Text.Contains("100,001")), string.Join("\n", issues));
            Assert.IsTrue(issues.Any(i => i.Level == Level.Problem && i.Text.Contains("photo")));
            Assert.IsTrue(ModelCheck.Blocks(issues), "past the guards the pull request check would fail it");
        }

        [Test]
        public void PartsNamedLikePiecesAreNoticed()
        {
            var f = Facts(new Bounds(new Vector3(0, 1, 0), new Vector3(1, 2, 1)));
            f.NodeNames.AddRange(new[] { "Torso", "head", "sword" });
            var t = new StudioTarget { Kind = TargetKind.Unit, Name = "knight", ObjectName = "knight" };
            var issues = ModelCheck.Run(f, StudioFix.None, t, new Bounds(new Vector3(0, 1, 0), new Vector3(1, 2, 1)), new[] { "base", "torso", "head", "larm" });
            Assert.IsTrue(issues.Any(i => i.Level == Level.Good && i.Text.Contains("2 parts")), string.Join("\n", issues));
        }

        [Test]
        public void FixesComposeLikeTheirMatrix()
        {
            var fix = StudioFix.None.Scaled(2f).Turned(1).Moved(new Vector3(1, 0, -3)).Turned(2).Scaled(0.5f);
            var m = Matrix4x4.Scale(Vector3.one * 0.5f) * Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)) * Matrix4x4.Translate(new Vector3(1, 0, -3))
                    * Matrix4x4.Rotate(Quaternion.Euler(0, 90, 0)) * Matrix4x4.Scale(Vector3.one * 2f);
            var p = new Vector3(0.3f, 1.1f, -0.7f);
            Assert.Less(Vector3.Distance(m.MultiplyPoint3x4(p), fix.Matrix.MultiplyPoint3x4(p)), 1e-4f);
            Assert.AreEqual(3, fix.QuarterTurns);
            Assert.IsTrue(StudioFix.None.IsIdentity);
            Assert.IsTrue(StudioFix.None.Turned(4).IsIdentity);
        }

        [Test]
        public void AFeatureIsSizedAgainstItsPictureAsTheGameDrawsIt()
        {
            // The definition says 18.75 cells, the game draws the tree 9.4 tall.
            var t = Feature(1, 1, 18.75f);
            t.DrawnHeight = 9.4f;
            t.DrawnFrom = "map";
            var f = Facts(new Bounds(new Vector3(0, 9.375f, 0), new Vector3(3, 18.75f, 3)));
            var issues = ModelCheck.Run(f, StudioFix.None, t);
            Assert.IsTrue(Has(issues, Level.Warning, FixKind.MatchSize), string.Join("\n", issues));
            var fix = ModelCheck.Apply(FixKind.MatchSize, f, StudioFix.None, t);
            Assert.AreEqual(9.4f, fix.Apply(f.Bounds).size.y, 1e-3f, "matched to the picture, not the definition");
            Assert.IsTrue(ModelCheck.Run(f, fix, t).Any(i => i.Level == Level.Good && i.Text.Contains("as the game draws it")));

            var rough = Feature(1, 1, 18.75f);
            var small = Facts(new Bounds(new Vector3(0, 1, 0), new Vector3(1, 2, 1)));
            Assert.IsTrue(ModelCheck.Run(small, StudioFix.None, rough).Any(i => i.Text.Contains("rough guide")), "only the definition, and it says so");
        }

        [Test]
        public void TheStandInWorldsThingsAreNotJudgedForSize()
        {
            var toy = Feature(2, 2, 1.5f);
            toy.ToScale = false;
            var f = Facts(new Bounds(new Vector3(0, 7, 0), new Vector3(1, 14, 3)));
            var issues = ModelCheck.Run(f, StudioFix.None, toy);
            Assert.IsFalse(issues.Any(i => i.Fix == FixKind.MatchSize || i.Fix == FixKind.FitFootprint || i.Fix == FixKind.TurnQuarter), string.Join("\n", issues));
            Assert.IsTrue(issues.Any(i => i.Text.Contains("not to scale")));
            Assert.AreEqual(1f, ModelCheck.Apply(FixKind.MatchSize, f, StudioFix.None, toy).Scale, "nothing to match against");
        }

        [Test]
        public void TheCardTabListsOnlyTheCardUnitsWithTheirPiece()
        {
            var mock = new MockBackend { StageSeconds = 0 };
            var cards = StudioTargets.Units(mock, true);
            Assert.AreEqual(StudioTargets.Cards.Length, cards.Count, "the card units, known by name without the game");
            var lode = cards.First(c => c.ObjectName == "ARALODE");
            Assert.AreEqual(TargetKind.UnitCard, lode.Kind);
            Assert.AreEqual("aralode", lode.ReplacesPiece, "the card piece is picked for you");
            Assert.AreEqual("Assets/Overrides/Units/ARALODE.glb", OverrideWriter.PathFor(lode));
            Assert.AreEqual(mock.UnitDefs.Count, StudioTargets.Units(mock, false).Count, "the Unit tab still lists every unit");
            Assert.AreEqual("VerLode", StudioTargets.CardPiece(new[] { "base", "VerLode", "VerLode_off" }, "verlode"));
            Assert.AreEqual("glow", StudioTargets.CardPiece(new[] { "base", "glow", "glow_off" }, "missing"), "else the piece with an _off twin");
        }

        [Test]
        public void AThingTypedInByHandGoesWhereTheGameLooks()
        {
            var tree = StudioTargets.Typed(TargetKind.Feature, " AraTree01 ", new Vector2Int(1, 2), 14f);
            Assert.AreEqual("Assets/Overrides/Features/AraTree01.glb", OverrideWriter.PathFor(tree));
            Assert.AreEqual(new Vector2Int(1, 2), tree.Footprint);
            Assert.IsTrue(tree.ToScale);
            var king = StudioTargets.Typed(TargetKind.Unit, "araking", Vector2Int.one, 0);
            Assert.AreEqual("Assets/Overrides/Units/ARAKING.glb", OverrideWriter.PathFor(king));
            Assert.AreEqual("zonfire", StudioTargets.Typed(TargetKind.UnitCard, "ZonFire", Vector2Int.one, 0).ReplacesPiece, "a known card gets its piece");
            Assert.IsNull(StudioTargets.Typed(TargetKind.Feature, "  ", Vector2Int.one, 1));
        }

        [Test]
        public void APrefabForTheSameThingIsReported()
        {
            var t = Feature();
            string dir = Path.Combine(temp, "Assets", "Overrides", "Features");
            Directory.CreateDirectory(dir);
            Assert.IsNull(OverrideWriter.Outranks(temp, t));
            File.WriteAllText(Path.Combine(dir, "TestTree.gltf"), "{}");
            Assert.IsNull(OverrideWriter.Outranks(temp, t), "a .gltf gives way to the .glb");
            File.WriteAllText(Path.Combine(dir, "TestTree.prefab"), "");
            Assert.AreEqual("Assets/Overrides/Features/TestTree.prefab", OverrideWriter.Outranks(temp, t));
        }

        [Test]
        public void PlayHerePutsCopiesAFootprintApartOnFlatGround()
        {
            // A cliff east of x = 20.
            var spots = StudioMode.PlaceFor((x, z) => x > 20f ? 5f : 0f, Vector3.zero, 3, 3, 3);
            Assert.AreEqual(3, spots.Count);
            foreach (var a in spots)
            {
                Assert.Less(a.x + 1.5f, 20f, "none on the cliff");
                Assert.Greater(a.x, 0f, "east of the start");
                Assert.Less(a.z, 0f, "south of it");
                foreach (var b in spots)
                    if (a != b) Assert.GreaterOrEqual(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z)), 5f, "a footprint and two cells apart");
            }
        }

        // ---- Baked into the file ----

        [Test]
        public void TheFixIsBakedIntoTheGlbTheGameReads()
        {
            var bytes = File.ReadAllBytes(Sample);
            var fix = StudioFix.None.Scaled(2f).Turned(1).Moved(new Vector3(0.5f, 0.25f, -1f));
            var baked = OverrideWriter.Prepare(bytes, fix, null, out var error);
            Assert.IsNull(error);
            var before = StudioModel.Load(Sample, out _);
            var go = GlbLoader.Load(baked, "baked", out error);
            try
            {
                var file = GlbFile.Read(baked, out _);
                var after = StudioModel.FactsOf(file, go);
                var want = fix.Apply(before.Facts.Bounds);
                Assert.Less(Vector3.Distance(want.min, after.Bounds.min), 1e-3f, $"{want} against {after.Bounds}");
                Assert.Less(Vector3.Distance(want.max, after.Bounds.max), 1e-3f);
                Assert.AreEqual(before.Facts.Triangles, after.Triangles, "the geometry itself is untouched");
                Assert.AreEqual(bytes.Length, OverrideWriter.Prepare(bytes, StudioFix.None, null, out _).Length, "no fix, no change");
            }
            finally
            {
                before.Dispose();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MaterialTweaksAreBakedIntoTheMaterials()
        {
            var tweaks = new MaterialTweaks { Tint = new Color(1f, 0.5f, 0.5f), Brightness = 2f, Roughness = 0.2f, Emission = 1.5f };
            var baked = OverrideWriter.Prepare(File.ReadAllBytes(Sample), StudioFix.None, tweaks, out var error);
            Assert.IsNull(error);
            var f = GlbFile.Read(baked, out _);
            var wood = (Dictionary<string, object>)MiniJson.Arr(f.Json, "materials").First(m => MiniJson.Text(m, "name") == "wood");
            var factor = MiniJson.Arr(MiniJson.Obj(wood, "pbrMetallicRoughness"), "baseColorFactor");
            Assert.AreEqual(0.42 * 2, (double)factor[0], 1e-4);
            Assert.AreEqual(0.27 * 0.5 * 2, (double)factor[1], 1e-4);
            Assert.AreEqual(0.2, MiniJson.Num(MiniJson.Obj(wood, "pbrMetallicRoughness"), "roughnessFactor"), 1e-4);
            var go = GlbLoader.Load(baked, "tweaked", out _);
            try
            {
                var mat = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).First(m => m.name == "wood");
                Assert.AreEqual(1.5f, mat.GetFloat("_Emission"), 1e-4f, "the game's loader reads the self light back");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- Where it goes ----

        [Test]
        public void ModelsGoWhereTheGameLooks()
        {
            Assert.AreEqual("Assets/Overrides/Features/AraTree01.glb", OverrideWriter.PathFor(new StudioTarget { Kind = TargetKind.Feature, Name = "AraTree01" }));
            Assert.AreEqual("Assets/Overrides/Units/ARAKING.glb", OverrideWriter.PathFor(new StudioTarget { Kind = TargetKind.Unit, Name = "AraKing", ObjectName = "araking" }));
            Assert.AreEqual("Assets/Overrides/Units/ARALODE.glb", OverrideWriter.PathFor(new StudioTarget { Kind = TargetKind.UnitCard, Name = "lode", ObjectName = "AraLode" }));
            Assert.IsNull(OverrideWriter.PathFor(new StudioTarget()));
            Assert.AreEqual("Assets/Overrides/Units/ARALODE.json", OverrideWriter.SidecarFor("Assets/Overrides/Units/ARALODE.glb"));
        }

        [Test]
        public void WritingPutsTheFileAndACardsSidecarInPlace()
        {
            var bytes = File.ReadAllBytes(Sample);
            var card = new StudioTarget { Kind = TargetKind.UnitCard, Name = "lode", ObjectName = "AraLode", ReplacesPiece = "stone", ReplacesTexture = "aralode01" };
            var written = OverrideWriter.Write(temp, card, bytes, StudioFix.None, null, out var error);
            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { "Assets/Overrides/Units/ARALODE.glb", "Assets/Overrides/Units/ARALODE.json" }, written);
            var side = MiniJson.Parse(File.ReadAllText(Path.Combine(temp, written[1])));
            Assert.AreEqual("stone", MiniJson.Text(side, "replacesPiece"));
            Assert.AreEqual("aralode01", MiniJson.Text(side, "replacesTexture"));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(temp, written[0])), "no fix, the file as it was");

            var whole = new StudioTarget { Kind = TargetKind.Unit, Name = "lode", ObjectName = "AraLode" };
            OverrideWriter.Write(temp, whole, bytes, StudioFix.None, null, out error);
            Assert.IsNull(error);
            Assert.IsFalse(File.Exists(Path.Combine(temp, written[1])), "a whole unit model is not a card");

            var feature = OverrideWriter.Write(temp, Feature(), bytes, StudioFix.None.Scaled(3f), null, out error);
            Assert.AreEqual("Assets/Overrides/Features/TestTree.glb", feature.Single());
            Assert.AreNotEqual(bytes.Length, new FileInfo(Path.Combine(temp, feature[0])).Length, "the fix is in");
            Assert.IsEmpty(OverrideWriter.Write(temp, new StudioTarget(), bytes, StudioFix.None, null, out error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TheGameReadsAHandMadeCardAndItsSidecar()
        {
            string rel = "Assets/Overrides/Units/ZZSTUDIOTESTCARD";
            string full = Path.Combine(StudioModel.ProjectDir, rel);
            File.Copy(Sample, full + ".glb", true);
            File.WriteAllText(full + ".json", "{\"replacesPiece\": \"plinth\", \"replacesTexture\": \"stone01\"}");
            try
            {
                CardOverride.Forget();
                var c = CardOverride.For("zzstudiotestcard");
                Assert.IsNotNull(c);
                Assert.AreEqual("plinth", c.ReplacesPiece);
                Assert.AreEqual("stone01", c.ReplacesTexture);
                Assert.IsTrue(c.Hides("plinth_off"));
                Assert.IsNull(OverrideIndex.Scan(StudioModel.ProjectDir).Find(OverrideKind.Unit, "ZZSTUDIOTESTCARD"), "not a whole unit model");
            }
            finally
            {
                File.Delete(full + ".glb");
                File.Delete(full + ".json");
                foreach (var meta in new[] { full + ".glb.meta", full + ".json.meta" }) if (File.Exists(meta)) File.Delete(meta);
                CardOverride.Forget();
            }
        }

        [Test]
        public void TheCatalogKeepsTheMapsAFeatureIsOn()
        {
            string json = "[{\"name\": \"AraTree01\", \"description\": \"Tree\", \"footprint\": [1, 2], \"height\": 226, " +
                "\"sprite\": {\"w\": 45, \"h\": 115, \"hotspot\": [21, 112]}, \"maps\": 2, \"mapNames\": [\"Boneyards\", \"Dark Forest\"]}]";
            var t = StudioTargets.FromCatalog(json, temp).Single();
            Assert.AreEqual(new Vector2Int(1, 2), t.Footprint);
            Assert.AreEqual(226 / 16f, t.Height, 1e-4f);
            Assert.AreEqual(new Vector2(21, 112), t.Hotspot);
            CollectionAssert.AreEqual(new[] { "Boneyards", "Dark Forest" }, t.Maps);
            var mock = new MockBackend { StageSeconds = 0 };
            Assert.AreEqual("mock_frost", StudioTargets.MapNamed(mock, new[] { "nowhere", "Frost pass" })?.Id, "names match without case or spaces");
            Assert.IsNull(StudioTargets.MapNamed(mock, new[] { "nowhere" }));
            Assert.AreEqual("aramon_monarch", StudioTargets.Monarch(mock)?.Name);
        }

        // ---- The studio itself ----

        [Test]
        public void TheStudioOpensOnTheMockWithTheSampleOnTheStage()
        {
            StudioSession.Reset();
            string mapChoice = StudioSession.MapChoice;
            string written = null;
            try
            {
                StudioSession.MapChoice = "";
                Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
                Assert.IsTrue(StudioMode.IsOn);
                Assert.IsTrue(StudioSession.Active);
                Assert.AreEqual(StudioMode.ScenePath, UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path);
                Assert.IsNotNull(StudioSession.Stage.Camera);
                Assert.IsFalse(StudioSession.Stage.RealGround, "the neutral ground");

                Assert.IsTrue(StudioSession.LoadModel(Sample), StudioSession.Status);
                var model = StudioSession.Stage.ModelObject;
                Assert.IsNotNull(model);
                Assert.Greater(model.GetComponentsInChildren<Renderer>().Length, 0);
                Assert.AreEqual(HideFlags.DontSave, model.hideFlags, "nothing of the stage is saved in the scene");

                var rock = StudioTargets.Features(StudioBackend.Get(), new List<StudioTarget>()).First(t => t.Name == "mock_rock");
                StudioSession.SetTarget(rock);
                Assert.IsTrue(StudioSession.Issues.Count > 0);
                Assert.IsFalse(ModelCheck.Blocks(StudioSession.Issues), string.Join("\n", StudioSession.Issues));
                Assert.IsNotNull(GameObject.Find(StudioStage.RootName + "/Original"), "the mock rock's sprite stands beside it");
                var ghost = GameObject.Find(StudioStage.RootName + "/Ghost").GetComponentInChildren<Renderer>(true);
                Assert.AreEqual("OpenKingdoms/Studio/Ghost", ghost.sharedMaterial.shader.name, "the ghost draws over the model");

                StudioSession.ApplyFix(FixKind.Recentre);
                StudioSession.SetFix(StudioSession.Fix.Turned(1));
                StudioSession.SetTweaks(new MaterialTweaks { Tint = Color.red, Emission = 1f });
                StudioSession.Stage.SetWeather(WeatherChoice.Snow);
                StudioSession.Stage.SetTime(TimeOfDay.Evening);
                StudioSession.SetTeam(1);
                StudioSession.Relook(false);
                StudioSession.Stage.Tick(0.2f, true);

                string classic = StudioSession.Screenshot(true, temp);
                string free = StudioSession.Screenshot(false, temp);
                Assert.Greater(new FileInfo(classic).Length, 10000, "a real picture");
                Assert.Greater(new FileInfo(free).Length, 10000);

                written = OverrideWriter.PathFor(StudioSession.Target);
                var paths = StudioSession.UseInGame(false);
                CollectionAssert.AreEqual(new[] { written }, paths);
                Assert.AreEqual(written, OverrideIndex.Scan(StudioModel.ProjectDir).Find(OverrideKind.Feature, "MOCK_ROCK"), "the game finds it");
            }
            finally
            {
                if (written != null) AssetDatabase.DeleteAsset(written);
                StudioMode.Close(false);
                StudioSession.Reset();
                StudioSession.MapChoice = mapChoice;
            }
            Assert.IsFalse(StudioMode.IsOn);
            Assert.IsNull(GameObject.Find(StudioStage.RootName), "leaving takes the stage away");
        }

        [Test]
        public void TheStudioShowsAMockMapsRealGround()
        {
            StudioSession.Reset();
            string mapChoice = StudioSession.MapChoice;
            try
            {
                StudioSession.MapChoice = "mock_isles";
                Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
                Assert.IsTrue(StudioSession.Stage.RealGround);
                Assert.AreEqual("mock_isles", StudioSession.LoadedMap);
                var s = StudioSession.Stage.Spot;
                Assert.Greater(s.y, StudioBackend.Get().Terrain.SeaLevel, "the model stands on dry land");
                var knight = StudioTargets.Units(StudioBackend.Get(), false).First(t => t.Name == "aramon_knight");
                StudioSession.LoadModel(Sample);
                StudioSession.SetTarget(knight);
                Assert.Greater(StudioSession.OriginalPieces.Length, 0, "the original model's pieces, for naming parts");
                Assert.IsNotNull(GameObject.Find(StudioStage.RootName + "/Original"));
            }
            finally
            {
                StudioMode.Close(false);
                StudioSession.Reset();
                StudioSession.MapChoice = mapChoice;
            }
        }

        // Pictures of the stage for docs/STUDIO_MODE.md, on the mock with the
        // sample. Runs only when OKU_STUDIO_SHOTS names a folder.
        [Test]
        public void CapturesForTheGuide()
        {
            string dir = Environment.GetEnvironmentVariable("OKU_STUDIO_SHOTS");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_STUDIO_SHOTS to capture the guide's pictures");
            Directory.CreateDirectory(dir);
            StudioSession.Reset();
            string mapChoice = StudioSession.MapChoice;
            var weather = StudioSession.Weather;
            var time = StudioSession.Time;
            void Shot(string name, StudioView v)
            {
                var t = new Texture2D(2, 2);
                t.LoadImage(StudioSession.Stage.Screenshot(v, 1280, 720));
                File.WriteAllBytes(Path.Combine(dir, name), t.EncodeToJPG(88));
                UnityEngine.Object.DestroyImmediate(t);
            }
            try
            {
                StudioSession.MapChoice = "";
                StudioSession.Weather = WeatherChoice.Off;
                StudioSession.Time = TimeOfDay.Game;
                Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
                StudioSession.LoadModel(Sample);
                StudioSession.SetTarget(StudioTargets.Features(StudioBackend.Get(), new List<StudioTarget>()).First(t => t.Name == "mock_rock"));
                var classic = StudioView.ClassicDefault;
                classic.Distance = 15f;
                Shot("classic.jpg", classic);
                var free = StudioView.FreeDefault;
                free.Distance = 10f;
                StudioSession.Stage.TurntableYaw = 20f;
                StudioSession.Stage.Tick(0f, false);
                Shot("free.jpg", free);

                StudioSession.Stage.SetWeather(WeatherChoice.Snow);
                for (int i = 0; i < 60; i++) { StudioSession.Stage.Aim(free); StudioSession.Stage.Tick(0.1f, false); }
                Shot("snow.jpg", free);
                StudioSession.Stage.SetWeather(WeatherChoice.Off);
                StudioSession.Stage.SetTime(TimeOfDay.Evening);
                Shot("evening.jpg", free);
                StudioSession.Stage.SetWeather(WeatherChoice.Off);
                StudioSession.Stage.SetTime(TimeOfDay.Night);
                StudioSession.SetTweaks(new MaterialTweaks { Tint = new Color(1f, 0.85f, 0.6f), Emission = 0.8f });
                StudioSession.SetTeam(1);
                Shot("night-glow.jpg", free);

                StudioSession.SetTweaks(new MaterialTweaks());
                StudioSession.MapChoice = "mock_isles";
                StudioSession.Time = TimeOfDay.Morning;
                Assert.IsTrue(StudioSession.Start(false), StudioSession.Status);
                StudioSession.Stage.TurntableYaw = 20f;
                StudioSession.Stage.Tick(0f, false);
                var wide = StudioView.FreeDefault;
                wide.Distance = 16f;
                wide.Pitch = 30f;
                Shot("map-ground.jpg", wide);
            }
            finally
            {
                StudioMode.Close(false);
                StudioSession.Reset();
                StudioSession.MapChoice = mapChoice;
                StudioSession.Weather = weather;
                StudioSession.Time = time;
            }
        }

        // The studio on the real engine with the player's own files. Runs only
        // when OKU_STUDIO_ENGINE names a folder for its pictures, never committed.
        [Test]
        public void TheStudioOpensOnTheRealEngine()
        {
            string dir = Environment.GetEnvironmentVariable("OKU_STUDIO_ENGINE");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_STUDIO_ENGINE to try the studio on the real engine");
            StudioBackend.PreferMock = false;
            StudioBackend.Release();
            var b = StudioBackend.Get();
            if (b == null || b.Name == "Mock") Assert.Ignore("the engine or the game files are missing: " + StudioBackend.Problem);
            Directory.CreateDirectory(dir);
            StudioSession.Reset();
            string mapChoice = StudioSession.MapChoice;
            try
            {
                StudioSession.MapChoice = "?";
                Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
                Assert.IsTrue(StudioSession.Stage.RealGround, "a real map's ground");
                Assert.IsNotNull(GameObject.Find(StudioStage.RootName + "/Monarch"), "a real monarch");
                Assert.IsTrue(StudioSession.LoadModel(Sample), StudioSession.Status);
                var feature = StudioTargets.Features(StudioBackend.Get(), StudioTargets.Catalog()).First(t => t.FeatureDef >= 0);
                StudioSession.SetTarget(feature);
                Debug.Log($"Engine studio: feature {feature.Name}, issues:\n" + string.Join("\n", StudioSession.Issues));
                File.WriteAllBytes(Path.Combine(dir, "engine-feature-classic.png"), StudioSession.Stage.Screenshot(StudioView.ClassicDefault, 1280, 720));
                var unit = StudioTargets.Units(StudioBackend.Get(), false).First(t => !string.IsNullOrEmpty(t.ObjectName));
                StudioSession.SetTarget(unit);
                Assert.Greater(StudioSession.OriginalPieces.Length, 0, "the original 3DO's pieces");
                Debug.Log($"Engine studio: unit {unit.Name} pieces {string.Join(",", StudioSession.OriginalPieces)}");
                var free = StudioView.FreeDefault;
                free.Distance = 12f;
                File.WriteAllBytes(Path.Combine(dir, "engine-unit-free.png"), StudioSession.Stage.Screenshot(free, 1280, 720));
                Assert.IsNotNull(GameObject.Find(StudioStage.RootName + "/Monarch"), "the game's monarch for scale");

                // A sprite feature the loaded map lacks is still shown as the game draws it.
                var feats = new FeatureState[16384];
                int n = Mathf.Min(b.ReadFeatures(feats), feats.Length);
                var onMap = new HashSet<int>(feats.Take(n).Select(f => f.Def));
                var away = StudioTargets.Features(b, StudioTargets.Catalog()).FirstOrDefault(t => t.FeatureDef >= 0 && !onMap.Contains(t.FeatureDef) && string.IsNullOrEmpty(t.ObjectName));
                if (away != null)
                {
                    StudioSession.SetTarget(away);
                    Assert.IsNull(StudioSession.OriginalMissing, away.Name);
                    Assert.Greater(StudioSession.Target.DrawnHeight, 0f);
                    Debug.Log($"Engine studio: {away.Name} not on the map, drawn {StudioSession.Target.DrawnHeight:0.##} cells, defined {away.Height:0.##}");
                    File.WriteAllBytes(Path.Combine(dir, "engine-placed-classic.png"), StudioSession.Stage.Screenshot(StudioView.ClassicDefault, 1280, 720));
                    n = Mathf.Min(b.ReadFeatures(feats), feats.Length);
                    Assert.IsFalse(feats.Take(n).Any(f => f.Def == away.FeatureDef), "taken away again");
                }

                // A lodestone's card piece is picked from its own model.
                var lode = StudioTargets.Units(b, true).FirstOrDefault(t => t.UnitDef >= 0 && t.ObjectName == "ARALODE");
                if (lode != null)
                {
                    StudioSession.SetTarget(lode);
                    Debug.Log($"Engine studio: ARALODE pieces {string.Join(",", StudioSession.OriginalPieces)}, card piece {StudioSession.Target.ReplacesPiece}");
                    Assert.IsTrue(StudioSession.OriginalPieces.Contains(StudioSession.Target.ReplacesPiece), StudioSession.Target.ReplacesPiece);
                }
            }
            finally
            {
                StudioMode.Close(false);
                StudioSession.Reset();
                StudioSession.MapChoice = mapChoice;
            }
        }

        // The sample split into a .gltf, a .bin and its picture.
        static string SplitSample(string dir, bool withPicture)
        {
            var f = GlbFile.Read(File.ReadAllBytes(Sample), out _);
            var images = MiniJson.Arr(f.Json, "images");
            var views = MiniJson.Arr(f.Json, "bufferViews");
            var img = (Dictionary<string, object>)images[0];
            int view = MiniJson.Int(img, "bufferView");
            int off = MiniJson.Int(views[view], "byteOffset"), len = MiniJson.Int(views[view], "byteLength");
            if (withPicture) File.WriteAllBytes(Path.Combine(dir, "stone.png"), f.Bin.Skip(off).Take(len).ToArray());
            img.Remove("bufferView");
            img["uri"] = "stone.png";
            File.WriteAllBytes(Path.Combine(dir, "well.bin"), f.Bin);
            ((Dictionary<string, object>)MiniJson.Arr(f.Json, "buffers")[0])["uri"] = "well.bin";
            string path = Path.Combine(dir, "well.gltf");
            File.WriteAllText(path, JsonText.Write(f.Json, true));
            return path;
        }
    }
}
