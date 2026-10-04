// CreonStagesEnginePlayTests.cs - on the real engine, skipped without the
// game files: every stage scenery breaks or burns into has a model of its
// own, and each Creon stage set down on a map draws it and, when it can
// break again, splits into chunks.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class CreonStagesEnginePlayTests
    {
        static bool Named(MapInfo m, string name) =>
            string.Equals(m.Name, name, System.StringComparison.OrdinalIgnoreCase) || string.Equals(m.Id, name, System.StringComparison.OrdinalIgnoreCase);

        [UnityTest, Timeout(600000)]
        public IEnumerator EveryCreonStageDrawsItsOwnModelAndSplits()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            var root = GameRoot.Boot();
            try
            {
                yield return null;
                var b = root.Backend;
                // Open grass, the biggest map otherwise.
                var map = b.Maps.FirstOrDefault(m => Named(m, "edmont's field")) ?? b.Maps.OrderByDescending(m => m.Size.x * m.Size.y).First();
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                var setup = GameRoot.DefaultSetup(b);
                setup.MapId = map.Id;
                root.Setup = setup;
                root.Flow.Fire(FlowEvent.Start);
                float deadline = Time.realtimeSinceStartup + 300f;
                while (root.Flow.State == FlowState.Loading && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
                root.Options.GameSpeed = 0;
                yield return null;
                var ents = root.World.Entities;

                Assert.That(ents.StagesWithoutModels(), Is.Empty, "stages drawn from an earlier stage's chunks");

                var defs = b.FeatureDefs;
                var stage = new bool[defs.Count];
                foreach (var d in defs)
                {
                    if (d.DeadDef >= 0 && d.DeadDef < stage.Length) stage[d.DeadDef] = true;
                    if (d.BurntDef >= 0 && d.BurntDef < stage.Length) stage[d.BurntDef] = true;
                }
                var creon = defs.Where(d => stage[d.Id] && d.Name.StartsWith("cre", System.StringComparison.OrdinalIgnoreCase) &&
                                            !string.IsNullOrEmpty(d.SequenceName) && Fracture.KindOf(d) != BreakKind.Body).ToList();
                foreach (var d in creon)
                    Assert.IsNotNull(OverrideLoader.Find(OverrideKind.Feature, null, d.Name, d.SequenceName, d.ObjectName), d.Name + " has a model");

                // Each set down on cleared ground, row by row.
                var fs = new FeatureState[EntityRenderer.MaxFeatures];
                for (int n = b.ReadFeatures(fs); n > 0; n--) b.RemoveFeature(n - 1);
                yield return null;
                int w = b.Terrain.HeightsW - 1, h = b.Terrain.HeightsH - 1, margin = 8;
                int x = margin, z = margin, rowDepth = 0;
                var placed = new List<string>();
                foreach (var d in creon)
                {
                    int fx = Mathf.Max(1, d.Footprint.x), fz = Mathf.Max(1, d.Footprint.y);
                    int index = -1;
                    for (int tries = 0; index < 0 && tries < 400 && z + fz < h - margin; tries++)
                    {
                        if (x + fx > w - margin) { x = margin; z += rowDepth + 2; rowDepth = 0; }
                        index = b.PlaceFeature(d.Id, x, z);
                        x += index >= 0 ? fx + 2 : 3;
                        if (index >= 0) rowDepth = Mathf.Max(rowDepth, fz);
                    }
                    Assert.GreaterOrEqual(index, 0, d.Name + " found room");
                    placed.Add(d.Name);
                }
                // The renderer reads the placements and asks for their splits on the frames that follow.
                for (int f = 0; f < 5; f++) yield return null;
                for (int f = 0; f < 3000 && ents.Fractures.Waiting > 0; f++) yield return null;
                Assert.AreEqual(0, ents.Fractures.Waiting, "every split finished");

                var cards = new List<string>();
                ents.FeatureCoverage(out int all, out int cardCount, out int empty, cards);
                Assert.AreEqual(placed.Count, all);
                Assert.That(cards, Is.Empty, "stages standing as flat cards");
                Assert.AreEqual(0, empty, "stages that draw nothing");

                int split = 0;
                var last = new List<FeatureDef>();
                foreach (var d in creon)
                {
                    var kind = Fracture.KindOf(d);
                    if (!d.Breakable || kind == BreakKind.None || kind == BreakKind.Rock) { last.Add(d); continue; }
                    var set = ents.Fractures.Get(d.Id);
                    Assert.IsNotNull(set, d.Name + " split when set down");
                    Assert.IsFalse(set.Whole, d.Name + " split, not whole");
                    Assert.Greater(set.Count, 1, d.Name + " broke into chunks");
                    split++;
                }
                Assert.Greater(split, 0);
                // A last stage never breaks, so nothing splits it, but its model would.
                foreach (var d in last) ents.Fractures.Ask(d.Id, d.Name, Fracture.KindOf(d), Fracture.InteriorOf(d, Fracture.KindOf(d)));
                for (int f = 0; f < 3000 && ents.Fractures.Waiting > 0; f++) yield return null;
                Assert.AreEqual(0, ents.Fractures.Waiting, "every last stage split");
                foreach (var d in last)
                {
                    var set = ents.Fractures.Get(d.Id);
                    Assert.IsTrue(set != null && !set.Whole && set.Count > 1, d.Name + " splits into chunks");
                }
                Debug.Log($"{placed.Count} Creon stages set down on {map.Name}, {split} split as they came and {last.Count} last stages split on asking: {string.Join(" ", placed)}");
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
