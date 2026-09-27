// StudioSessionTests.cs - the studio running on the stand-in world: its
// per-frame loop, keeping work over script reloads and Play, following a
// change of backend, the classic view, the original as the game draws it,
// and never taking over another scene.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using OpenKingdomsUnity.Studio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using FixKind = OpenKingdomsUnity.Studio.ModelCheck.FixKind;

namespace OpenKingdomsUnity.Tests
{
    public class StudioSessionTests : StudioFixture
    {
        string mapChoice;
        StudioView view, free;
        bool turning;

        [SetUp]
        public void OpenOnNeutralGround()
        {
            mapChoice = StudioSession.MapChoice;
            view = StudioSession.View;
            free = StudioSession.Free;
            turning = StudioSession.Turning;
            StudioSession.Reset();
            StudioSession.MapChoice = "";
        }

        [TearDown]
        public void Close()
        {
            StudioMode.Close(false);
            StudioSession.Reset();
            StudioSession.MapChoice = mapChoice;
            StudioSession.View = view;
            StudioSession.Free = free;
            StudioSession.Turning = turning;
        }

        static void Resume() =>
            typeof(StudioMode).GetMethod("Resume", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

        static StudioTarget Mock(string name) =>
            StudioTargets.Features(StudioBackend.Get(), new List<StudioTarget>()).First(t => t.Name == name);

        [Test]
        public void TheStudioLoopTurnsPicksUpDropsAndReloads()
        {
            string dropped = Path.Combine(StudioModel.ProjectDir, StudioModel.DropFolder, "ZZStudioLoopTest.glb");
            try
            {
                Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
                StudioModel.EnsureDropFolder();
                double now = 1000;
                StudioSession.TickAt(now);
                StudioSession.TickAt(now += 1.5);
                StudioSession.View.Classic = false;
                StudioSession.Turning = true;
                float yaw = StudioSession.Stage.TurntableYaw;
                StudioSession.TickAt(now += 0.2);
                Assert.Greater(StudioSession.Stage.TurntableYaw, yaw, "the turntable turns in the free view");

                File.Copy(Sample, dropped, true);
                File.SetLastWriteTimeUtc(dropped, DateTime.UtcNow.AddSeconds(-5));
                StudioSession.TickAt(now += 1.5);
                Assert.IsNotNull(StudioSession.Model, StudioSession.Status);
                StringAssert.EndsWith("ZZStudioLoopTest.glb", StudioSession.Model.SourcePath, "a model saved into Drop loads by itself");

                StudioSession.SetFix(StudioFix.None.Scaled(1.5f).Turned(1));
                var f = GlbFile.Read(File.ReadAllBytes(dropped), out _);
                ((Dictionary<string, object>)MiniJson.Arr(f.Json, "nodes")[0])["name"] = "reexported";
                File.WriteAllBytes(dropped, f.Write());
                File.SetLastWriteTimeUtc(dropped, DateTime.UtcNow.AddSeconds(-2));
                StudioSession.TickAt(now += 1.0);
                StringAssert.StartsWith("Reloaded", StudioSession.Status, "an export over the same file reloads it");
                CollectionAssert.Contains(StudioSession.Model.Facts.NodeNames, "reexported");
                Assert.AreEqual(1.5f, StudioSession.Fix.Scale, 1e-4f, "with the fixes kept");
                Assert.AreEqual(1, StudioSession.Fix.QuarterTurns);

                // A model from outside is copied in for Unity's importer, and
                // that copy is not taken for a new model in Drop.
                string obj = StudioModeTests.BoxObj(temp, "zz_studio_loop_box", false);
                Assert.IsTrue(StudioSession.LoadModel(obj), StudioSession.Status);
                StudioSession.TickAt(now += 1.5);
                StudioSession.TickAt(now += 1.5);
                Assert.AreEqual(Path.GetFullPath(obj), StudioSession.Model.SourcePath, "still the artist's own file");
            }
            finally
            {
                AssetDatabase.DeleteAsset(StudioModel.ToAsset(dropped));
                if (File.Exists(dropped)) File.Delete(dropped);
                StudioModeTests.RemoveImported("zz_studio_loop_box");
            }
        }

        [Test]
        public void AFailedReloadWaitsForTheNextChange()
        {
            string path = Path.Combine(temp, "well.glb");
            File.Copy(Sample, path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            Assert.IsTrue(StudioSession.LoadModel(path), StudioSession.Status);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-5));
            Assert.IsTrue(StudioSession.Model.ChangedOnDisk());
            Assert.IsFalse(StudioSession.LoadModel(StudioSession.Model.SourcePath));
            Assert.IsFalse(StudioSession.Model.ChangedOnDisk(), "the broken file is not read again until it changes");
            File.Copy(Sample, path, true);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-1));
            Assert.IsTrue(StudioSession.Model.ChangedOnDisk(), "a good export after it is");
        }

        [Test]
        public void AGltfReloadsWhenItsBinOrPictureChanges()
        {
            var f = GlbFile.Read(File.ReadAllBytes(Sample), out _);
            File.WriteAllBytes(Path.Combine(temp, "well.bin"), f.Bin);
            ((Dictionary<string, object>)MiniJson.Arr(f.Json, "buffers")[0])["uri"] = "well.bin";
            string gltf = Path.Combine(temp, "well.gltf");
            File.WriteAllText(gltf, JsonText.Write(f.Json, true));
            foreach (var p in Directory.GetFiles(temp)) File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddSeconds(-10));
            var m = StudioModel.Load(gltf, out var error);
            Assert.IsNull(error, error);
            try
            {
                Assert.IsFalse(m.ChangedOnDisk());
                File.SetLastWriteTimeUtc(Path.Combine(temp, "well.bin"), DateTime.UtcNow.AddSeconds(-2));
                Assert.IsTrue(m.ChangedOnDisk(), "the .bin is watched with the .gltf");
            }
            finally { m.Dispose(); }
        }

        [Test]
        public void TheTargetFixesAndTweaksSurviveAScriptReloadAndPlay()
        {
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            Assert.IsTrue(StudioSession.LoadModel(Sample), StudioSession.Status);
            StudioSession.SetTarget(Mock("mock_rock"));
            StudioSession.SetFix(StudioFix.None.Turned(1).Scaled(1.5f));
            StudioSession.SetTweaks(new MaterialTweaks { Tint = Color.red });
            void Kept(string when)
            {
                Assert.IsTrue(StudioSession.Active, when);
                Assert.IsNotNull(StudioSession.Model, when);
                Assert.AreEqual("mock_rock", StudioSession.Target.Name, when);
                Assert.AreEqual(1, StudioSession.Fix.QuarterTurns, when);
                Assert.AreEqual(1.5f, StudioSession.Fix.Scale, 1e-4f, when);
                Assert.AreEqual(Color.red, StudioSession.Tweaks.Tint, when);
            }

            // A script reload: the old domain goes, a new one starts empty and resumes.
            StudioSession.ForgetAsAfterReload();
            Resume();
            Kept("after a script reload");

            // Play here then Stop: the stage stops before Play, and edit mode resumes it.
            StudioSession.Stop();
            StudioSession.ForgetAsAfterReload();
            Resume();
            Kept("after Play and Stop");

            // A target typed in by hand comes back too.
            StudioSession.SetTarget(StudioTargets.Typed(TargetKind.Feature, "AraTree01", new Vector2Int(1, 2), 14f));
            StudioSession.ForgetAsAfterReload();
            Resume();
            Assert.AreEqual("AraTree01", StudioSession.Target.Name);
            Assert.AreEqual(new Vector2Int(1, 2), StudioSession.Target.Footprint);
            Assert.AreEqual(14f, StudioSession.Target.DrawnHeight, 1e-4f);
        }

        [UnityTest]
        public IEnumerator UseTheseSettingsNowMovesTheOpenStudioToTheNewBackend()
        {
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            var before = StudioSession.Stage.Backend;
            Assert.AreSame(StudioBackend.Get(), before);
            SettingsWindow.UseNow();
            for (int i = 0; i < 30 && (!StudioSession.Active || StudioSession.Stage.Backend != StudioBackend.Get()); i++) yield return null;
            Assert.IsTrue(StudioSession.Active, StudioSession.Status);
            Assert.AreSame(StudioBackend.Get(), StudioSession.Stage.Backend, "the stage and the lists use the same backend");
            Assert.AreNotSame(before, StudioSession.Stage.Backend);
            Assert.IsTrue(StudioBackend.PreferMock, "the stand-in toggle is left as it was");
            Assert.AreEqual("Mock", StudioSession.Stage.Backend.Name);
        }

        [Test]
        public void TheClassicViewShowsTheModelUnturned()
        {
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            Assert.IsTrue(StudioSession.LoadModel(Sample), StudioSession.Status);
            var stage = StudioSession.Stage;
            stage.Tick(2f, true);
            stage.Tick(0.1f, false);
            stage.Aim(StudioView.ClassicDefault);
            Assert.Less(Quaternion.Angle(stage.ModelObject.transform.rotation, Quaternion.identity), 0.5f, "the turntable's turn is left out of the classic view");
            stage.Aim(StudioView.FreeDefault);
            Assert.Greater(Quaternion.Angle(stage.ModelObject.transform.rotation, Quaternion.identity), 1f, "and kept in the free view");

            // The original's picture lies in the camera's plane, as the game draws it.
            StudioSession.SetTarget(Mock("mock_rock"));
            stage.Aim(StudioView.ClassicDefault);
            var face = GameObject.Find(StudioStage.RootName + "/Ghost/Face").transform;
            Assert.Less(Quaternion.Angle(face.rotation, stage.Camera.transform.rotation), 0.01f);
            var want = EntityRenderer.CardMatrix(face.parent.position, 1f, 0f, 1f, 0f, stage.Camera.transform);
            Assert.AreEqual(want.lossyScale.x, face.lossyScale.x, 1e-4f, "nudged toward the camera and shrunk like the game's");
            Assert.Less(face.lossyScale.x, 1f);
        }

        [Test]
        public void TheStageShowsAModelsOwnGlowAsTheGameDoes()
        {
            var f = GlbFile.Read(File.ReadAllBytes(Sample), out _);
            foreach (var m in MiniJson.Arr(f.Json, "materials")) ((Dictionary<string, object>)m)["emissiveFactor"] = new List<object> { 0.5, 0.5, 0.5 };
            string path = Path.Combine(temp, "glowwell.glb");
            File.WriteAllBytes(path, f.Write());
            var game = GlbLoader.Load(File.ReadAllBytes(path), "glow", out _);
            float inGame = game.GetComponentsInChildren<Renderer>(true)[0].sharedMaterials[0].GetFloat("_Emission");
            UnityEngine.Object.DestroyImmediate(game);
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            Assert.IsTrue(StudioSession.LoadModel(path), StudioSession.Status);
            float Stage() => StudioSession.Stage.ModelObject.GetComponentsInChildren<Renderer>(true)[0].sharedMaterials[0].GetFloat("_Emission");
            Assert.AreEqual(inGame, Stage(), 1e-4f);
            StudioSession.SetTweaks(new MaterialTweaks { Emission = 2f });
            Assert.AreEqual(2f, Stage(), 1e-4f, "a self light tweak sets the glow");
            StudioSession.SetTweaks(new MaterialTweaks());
            Assert.AreEqual(inGame, Stage(), 1e-4f, "and resetting it brings the model's own back");
        }

        [Test]
        public void OnTheStandInWorldTheMonarchIsFourCellsTall()
        {
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            var monarch = GameObject.Find(StudioStage.RootName + "/Monarch");
            Assert.IsNotNull(monarch);
            Assert.IsTrue(StudioSession.Stage.MonarchIsStandIn, "not the stand-in world's small monarch");
            float top = monarch.GetComponentsInChildren<Renderer>().Max(r => r.bounds.max.y) - monarch.transform.position.y;
            Assert.AreEqual(StudioTargets.MonarchHeight, top, 0.1f);
            Assert.IsTrue(StudioSession.LoadModel(Sample), StudioSession.Status);
            StudioSession.SetTarget(StudioTargets.Units(StudioBackend.Get(), false).First(t => t.Name == "aramon_knight"));
            Assert.IsFalse(StudioSession.Issues.Any(i => i.Fix == FixKind.MatchSize || i.Fix == FixKind.TurnQuarter), string.Join("\n", StudioSession.Issues));
            StudioSession.SetTarget(StudioTargets.Typed(TargetKind.Feature, "AraTree01", Vector2Int.one, 1f));
            Assert.IsTrue(StudioSession.Issues.Any(i => i.Fix == FixKind.MatchSize), "a name typed in is judged by the height typed");
        }

        [Test]
        public void AFeatureNotOnTheMapIsStillShownAsTheGameDrawsIt()
        {
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            var b = StudioBackend.Get();
            var bush = Mock("mock_bush");
            var feats = new FeatureState[4096];
            int n = b.ReadFeatures(feats);
            for (int i = n - 1; i >= 0; i--) if (feats[i].Def == bush.FeatureDef) b.RemoveFeature(feats[i].Index);
            Assert.IsTrue(StudioSession.LoadModel(Sample), StudioSession.Status);
            StudioSession.SetTarget(bush);
            Assert.IsNotNull(GameObject.Find(StudioStage.RootName + "/Original"), "placed for a moment to read its picture");
            Assert.IsNotNull(GameObject.Find(StudioStage.RootName + "/Ghost"));
            Assert.IsNull(StudioSession.OriginalMissing);
            Assert.AreEqual(1f, StudioSession.Target.DrawnHeight, 1e-4f, "the height the game draws it");
            n = b.ReadFeatures(feats);
            Assert.IsFalse(feats.Take(n).Any(f => f.Def == bush.FeatureDef), "and taken away again");
        }

        [Test]
        public void OpeningAnotherSceneLeavesStudioModeAndKeepsItsWork()
        {
            const string probe = "Assets/ZZStudioSceneTest.unity";
            try
            {
                Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, probe);
                new GameObject("unsaved work");
                EditorSceneManager.MarkSceneDirty(s);
                Resume();
                Assert.AreEqual(probe, EditorSceneManager.GetActiveScene().path, "the studio does not take the scene back");
                Assert.IsNotNull(GameObject.Find("unsaved work"), "and the unsaved work is still there");
                Assert.IsFalse(StudioMode.IsOn, "opening another scene left Studio Mode");
                Assert.IsFalse(StudioSession.Active);
            }
            finally { AssetDatabase.DeleteAsset(probe); }
        }
    }
}
