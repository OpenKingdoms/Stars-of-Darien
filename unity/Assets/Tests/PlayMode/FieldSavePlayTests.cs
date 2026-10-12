// FieldSavePlayTests.cs - on the mock, through the screens a player uses: a
// battle saved from the pause menu and loaded from the skirmish page's Load
// is scarred, dented and withered from the first frame it is drawn, and a
// save with no field kept beside it still loads, to a clean field.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class FieldSavePlayTests
    {
        GameRoot root;
        MockBackend mock;
        string saves;

        [SetUp]
        public void OwnSaves() => saves = TempSaves.Use();

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            TempSaves.Drop(saves);
            FxQuality.Use(EffectsQuality.High);
        }

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        Button ButtonOn(string screen, string words)
        {
            var go = root.Screens.Screen(screen);
            Assert.IsTrue(go.activeInHierarchy, screen + " is up");
            var b = go.GetComponentsInChildren<Button>().FirstOrDefault(x => x.GetComponentsInChildren<Text>().Any(t => t.text == words));
            Assert.IsNotNull(b, $"{screen} has {words}");
            return b;
        }

        // Frost Pass has no sea, so the staged rows stand on dry land.
        IEnumerator Begin()
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Options.EffectsQuality = EffectsQuality.High;
            root.Options.Weather = WeatherChoice.Off;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_frost";
            root.Setup.Seed = 3;
            root.Setup.LineOfSight = false;
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 60f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            root.Options.GameSpeed = 0;
        }

        // Cannon, hail and fire on the ground, and dark magic on one of the
        // map's own trees, until the craters are dug and the tree withered.
        IEnumerator Fight(System.Action<FeatureState> withered)
        {
            var w = root.World;
            Assert.IsTrue(mock.StageFx("ground", 1000f));
            for (int t = 0; t < 900 && w.Scars.Dents.Dented == 0; t += 2)
            {
                mock.Advance(2);
                yield return null;
            }
            Assert.Greater(w.Scars.Dents.Dented, 0, "a crater was dug");
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            int n = mock.ReadFeatures(fs);
            var tree = fs.Take(n).Where(f => f.Def == 0).OrderBy(f => (f.Position - mock.StageCentre).sqrMagnitude).First();
            Assert.Greater(w.Magic.Apply(BlastKind.Dark, tree.Position, 1f, Vector3.zero), 0, "dark magic reached the tree");
            for (int i = 0; i < 60; i++)
            {
                mock.Advance(3);
                yield return null;
            }
            withered(tree);
            // Every stamp drawn and every dented region in place.
            yield return Until(() => w.Scars.Pending == 0 && w.Terrain.RefinedRegions == w.Terrain.RefinedList().Count, 30f);
        }

        // Quits to the menu and loads the save from the skirmish page's Load.
        IEnumerator LoadFromTheSkirmishPage(string saved)
        {
            ButtonOn("Pause", "Quit to menu").onClick.Invoke();
            yield return null;
            ButtonOn("Leave", "Leave").onClick.Invoke();
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
            Assert.IsNull(root.World, "the battle is gone");
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            var load = root.Screens.Lobby.Skirmish.Page.Root.GetComponentsInChildren<ArtButton>(true).First(b => b.name == "Load");
            load.Press();
            yield return null;
            Assert.AreEqual(FlowState.LoadList, root.Flow.State);
            var entry = root.Screens.Screen("Load").GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Save " + Path.GetFileName(saved));
            Assert.IsNotNull(entry, "the save is in the list");
            entry.onClick.Invoke();
        }

        string SaveFromPause()
        {
            var old = new HashSet<string>(root.ListSaves().Select(e => e.Path));
            ButtonOn("Pause", "Save game").onClick.Invoke();
            var mine = root.ListSaves().Where(e => !old.Contains(e.Path)).ToList();
            Assert.AreEqual(1, mine.Count, "one new save, and the field beside it is not one");
            return mine[0].Path;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator ABattleSavedFromPauseComesBackScarredFromTheFirstFrame()
        {
            yield return Begin();
            FeatureState tree = default;
            yield return Fight(t => tree = t);
            root.Flow.Fire(FlowEvent.Pause);
            for (int i = 0; i < 3; i++) yield return null;
            var w = root.World;
            var before = w.Scars.Capture();
            int dented = w.Scars.Dents.Dented, regions = w.Terrain.RefinedRegions;
            var look = w.Entities.LookAt(tree.Index, tree.Def, tree.Position);
            float wither = look.Wither;
            Assert.Greater(wither, 0.3f, "the tree is withered");
            Assert.Greater(regions, 0, "the ground dips under the craters");
            // The deepest point of the craters, to check the ground there.
            var deep = Vector3.zero;
            float deepest = 0f;
            for (float x = 0f; x < mock.Terrain.Size.x; x += 0.5f)
                for (float z = 0f; z > -mock.Terrain.Size.y; z -= 0.5f)
                {
                    float o = ScarMap.GroundOffset(x, z);
                    if (o < deepest) { deepest = o; deep = new Vector3(x, 0f, z); }
                }
            Assert.Less(deepest * ScarStamps.PixelsPerUnit, -2f, "a crater pixels deep");

            string saved = SaveFromPause();
            Assert.IsTrue(File.Exists(FieldSave.PathFor(saved)), "the field is kept beside the save");
            Assert.Greater(root.LastFieldBytes, 0);
            yield return LoadFromTheSkirmishPage(saved);

            // Every frame the field is drawn, from the first, is the field as saved.
            var problems = new List<string>();
            ScarState first = null;
            int drawn = 0;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var v = root.World;
                if (v == null || v.FramesDrawn == 0) continue;
                drawn++;
                if (first == null) first = v.Scars.Capture();
                if (v.Scars.Dents.Dented != dented) problems.Add($"frame {drawn}: {v.Scars.Dents.Dented} texels dented, not {dented}");
                if (v.Terrain.RefinedRegions != regions) problems.Add($"frame {drawn}: {v.Terrain.RefinedRegions} regions dip, not {regions}");
                if (ScarMap.GroundOffset(deep.x, deep.z) != deepest) problems.Add($"frame {drawn}: the crater's floor is at {ScarMap.GroundOffset(deep.x, deep.z)}, not {deepest}");
                var l = v.Entities.LookAt(tree.Index, tree.Def, tree.Position);
                if (l == null || Mathf.Abs(l.Wither - wither) > 1e-4f) problems.Add($"frame {drawn}: the tree is withered {l?.Wither}, not {wither}");
                if (root.Flow.State == FlowState.Playing && drawn >= 5) break;
            }
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            CollectionAssert.IsEmpty(problems.Take(10).ToList(), "no frame shows the field otherwise");
            Assert.IsNull(root.World.FieldNote, "the field was restored");
            Assert.IsNotNull(first);
            Assert.AreEqual(-1, Differ(before.Marks, first.Marks), "the marks texture as saved");
            Assert.AreEqual(-1, Differ(before.Shape, first.Shape), "the shape texture as saved");
            Assert.AreEqual(-1, Differ(before.Fade, first.Fade), "the fade texture as saved");
            Assert.AreEqual(-1, Differ(before.Depth, first.Depth), "the dips kept on the CPU");
            Assert.AreEqual(-1, Differ(before.Rim, first.Rim), "the rims kept on the CPU");
            Debug.Log($"Mock field: {dented} texels dented, {regions} regions, the tree withered {wither:0.00}, kept in {root.LastFieldBytes} bytes");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator ASaveWithNoFieldBesideItLoadsToACleanField()
        {
            yield return Begin();
            yield return Fight(_ => { });
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            string saved = SaveFromPause();
            // As a save from before fields were kept.
            File.Delete(FieldSave.PathFor(saved));
            yield return LoadFromTheSkirmishPage(saved);
            yield return Until(() => root.Flow.State == FlowState.Playing, 60f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            StringAssert.Contains("no field", root.World.FieldNote);
            Assert.AreEqual(0, root.World.Scars.Dents.Dented, "a clean field");
            Assert.AreEqual(0, root.World.Terrain.RefinedRegions);
            mock.Advance(30);
            yield return null;
            Assert.AreEqual(GameStatus.Running, mock.Status, "and it plays on");
        }

        static int Differ(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b ? -1 : 0;
            if (a.Length != b.Length) return System.Math.Min(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return i;
            return -1;
        }
    }
}
