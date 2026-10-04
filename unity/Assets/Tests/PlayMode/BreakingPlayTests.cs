// BreakingPlayTests.cs - scenery breaks on the mock as the engine will
// report it: a tree loses its crown and its dead trunk topples away from
// the blast, a wall comes down a stage at a time, taking a feature away
// rebuilds no other on a big field, a dying unit's pieces leave it, and a
// battle full of deaths keeps breaking inside its budget.
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
    public class BreakingPlayTests
    {
        GameRoot root;
        MockBackend mock;
        EntityRenderer Ents => root.World.Entities;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        // Frost Pass has no sea, so the middle of the map is dry land. The
        // game stands still, and each test moves it a tick a frame.
        IEnumerator Begin(string map = "mock_frost", int computers = 1)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            // More computers on their own, so one routed leaves a war going on.
            for (int k = 2; k <= computers && k < root.Setup.Seats.Count; k++)
            {
                root.Setup.Seats[k].Kind = SeatKind.Computer;
                root.Setup.Seats[k].Side = mock.Sides[k % mock.Sides.Count].Id;
            }
            root.Setup.Seed = 3;
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Options.EffectsQuality = EffectsQuality.High;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            root.Options.GameSpeed = 0;
            mock.SceneryBreaks = true;
            mock.SeeAll(true);
            for (int i = 0; i < 3; i++) yield return null;
        }

        void Bare()
        {
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            for (int n = mock.ReadFeatures(fs); n > 0; n--) mock.RemoveFeature(n - 1);
        }

        int Def(string name) => mock.FeatureDefs.First(d => d.Name == name).Id;

        FeatureState FeatureAt(int index)
        {
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            Assert.Less(index, mock.ReadFeatures(fs));
            return fs[index];
        }

        int Place(string kind, int dx = 0, int dz = 0)
        {
            var c = mock.StageCentre;
            return mock.PlaceFeature(Def(kind), Mathf.RoundToInt(c.x) + dx, Mathf.RoundToInt(-c.z) + dz);
        }

        // A cannonball at a feature from the south, so it travels north.
        void Shoot(int feature, string weapon = "ARACAN 1")
        {
            var at = FeatureAt(feature).Position;
            mock.FireFx(weapon, at + new Vector3(0f, 1f, -8f), at);
        }

        // The game a tick a frame, as it plays.
        IEnumerator Ticks(int n)
        {
            for (int i = 0; i < n; i++)
            {
                mock.Advance(1);
                yield return null;
            }
        }

        // Where every chunk in play is now, leaving out the grit.
        List<Vector3> Chunks(params Debris.State[] states)
        {
            var d = Ents.Debris;
            var list = new List<Vector3>();
            for (int i = 0; i < d.Capacity; i++)
            {
                var s = d.StateOf(i);
                if (s != Debris.State.Free && !d.IsFine(i) && (states.Length == 0 || states.Contains(s))) list.Add(d.PositionOf(i));
            }
            return list;
        }

        void Look(Vector3 at, float distance = 18f)
        {
            var cam = root.World.Camera;
            cam.focus = at;
            cam.Zoom(distance);
        }

        [UnityTest]
        public IEnumerator ATreeLosesItsCrownAndItsTrunkTopplesAwayFromTheBlast()
        {
            yield return Begin();
            Bare();
            int tree = Place("mock_tree");
            var at = FeatureAt(tree).Position;
            Look(at);
            yield return Ticks(2);
            Shoot(tree);
            int guard = 0;
            while (Ents.Breaking == 0 && guard++ < 120) yield return Ticks(1);
            Assert.AreEqual(1, Ents.Breaking, "the tree is breaking");
            Assert.AreEqual(BreakKind.Tree, Ents.Falls.LastKind);
            Assert.Greater(Ents.Falls.LastHeld, 0, "its trunk stands");
            Assert.Greater(Ents.Falls.LastFlying, 0, "its crown flies");
            Assert.Greater(Ents.Falls.LastLowestFlying, Ents.Falls.LastHighestHeld, "the crown is what flies");
            yield return Ticks(20);
            var crown = Chunks(Debris.State.Flying, Debris.State.Resting);
            Assert.Greater(crown.Count, 0);
            Assert.Greater(crown.Average(p => p.z), at.z + 0.3f, "the crown went north with the shot");
            yield return Ticks(40);
            Assert.AreEqual(Def("mock_tree_dead"), FeatureAt(tree).Def, "the dead tree stands");
            Assert.AreEqual(0, Ents.Breaking);
            Assert.IsTrue(Ents.FeatureDrawnAt(at, 0.3f));

            // Let the crown sink away, then fell the dead tree.
            yield return Ticks(30 * 16);
            Assert.AreEqual(0, Chunks().Count, "the crown has gone");
            Shoot(tree);
            guard = 0;
            while (Ents.Breaking == 0 && guard++ < 120) yield return Ticks(1);
            Assert.AreEqual(1, Ents.Breaking);
            Assert.AreEqual(0, Ents.Falls.LastHeld, "nothing of a dead tree stands");
            yield return Ticks(10);
            var leaning = Chunks(Debris.State.Riding);
            Assert.Greater(leaning.Count, 0, "the trunk falls as one");
            yield return Ticks(80);
            var trunk = Chunks();
            Assert.Greater(trunk.Count, 0);
            Assert.AreEqual(trunk.Count, Chunks(Debris.State.Flying, Debris.State.Resting).Count, "it broke where it landed");
            Assert.Greater(trunk.Average(p => p.z), at.z + 0.8f, "it fell away from the blast");
            Assert.Less(Mathf.Abs(trunk.Average(p => p.x) - at.x), 0.8f, "straight away from it");
            Assert.AreEqual(Def("mock_stump"), FeatureAt(tree).Def, "the stump is left");
            Assert.IsTrue(Ents.FeatureDrawnAt(at, 0.3f));
        }

        [UnityTest]
        public IEnumerator AWallComesDownAStageAtATime()
        {
            yield return Begin();
            Bare();
            int wall = Place("mock_wall");
            var at = FeatureAt(wall).Position;
            Look(at);
            yield return Ticks(2);
            Shoot(wall);
            int guard = 0;
            while (Ents.Falls.LastKind != BreakKind.Wall && guard++ < 200)
            {
                if (guard % 60 == 0) Shoot(wall);
                yield return Ticks(1);
            }
            Assert.AreEqual(BreakKind.Wall, Ents.Falls.LastKind, "the wall broke");
            Assert.Greater(Ents.Falls.LastFlying, 0, "its top crumbles");
            Assert.Greater(Ents.Falls.LastHeld, 0, "its foot stands");
            Assert.Greater(Ents.Falls.LastLowestFlying, Ents.Falls.LastHighestHeld, "from the top");
            Assert.LessOrEqual(Ents.Falls.LastHighestHeld, 1.05f, "the half the next stage keeps");
            yield return Ticks(40);
            Assert.AreEqual(Def("mock_wall_a"), FeatureAt(wall).Def, "half a wall");
            Assert.AreEqual(0, Ents.Breaking, "the half wall took over");
            int flying = Ents.Falls.LastFlying;

            for (int shot = 0; shot < 4 && FeatureAt(wall).Def != Def("mock_rubble"); shot++)
            {
                Shoot(wall);
                yield return Ticks(60);
            }
            Assert.AreEqual(Def("mock_rubble"), FeatureAt(wall).Def, "rubble");
            Assert.AreEqual(0, Ents.Falls.LastHeld, "nothing of the half wall stands");
            Assert.Greater(Ents.Falls.LastFlying, 0);
            Assert.IsTrue(Ents.FeatureDrawnAt(at, 0.3f), "the rubble is drawn");
            Debug.Log($"The wall: {flying} chunks crumbled from its top, then {Ents.Falls.LastFlying} came down with the rest");
        }

        [UnityTest]
        public IEnumerator AHutCavesInAndAStoneBodyShatters()
        {
            yield return Begin();
            Bare();
            int hut = Place("mock_hut", -3);
            int body = Place("mock_stone_body", 3);
            var hutAt = FeatureAt(hut).Position;
            Look(hutAt, 24f);
            yield return Ticks(2);
            Shoot(hut);
            int guard = 0;
            while (Ents.Falls.LastKind != BreakKind.Hut && guard++ < 120) yield return Ticks(1);
            Assert.AreEqual(BreakKind.Hut, Ents.Falls.LastKind);
            Assert.Greater(Ents.Falls.LastFlying, 0);
            yield return Ticks(60);
            Assert.AreEqual(Def("mock_hut_wreck"), FeatureAt(hut).Def, "the wreck");
            var bodyAt = FeatureAt(body).Position;
            Shoot(body);
            guard = 0;
            while (Ents.Falls.LastKind != BreakKind.Body && guard++ < 120) yield return Ticks(1);
            Assert.AreEqual(BreakKind.Body, Ents.Falls.LastKind);
            Assert.AreEqual(0, Ents.Falls.LastHeld, "nothing of it stands");
            yield return Ticks(30);
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            int left = mock.ReadFeatures(fs);
            Assert.IsFalse(fs.Take(left).Any(f => f.Def == Def("mock_stone_body")), "the body is gone");
            Assert.Greater(Chunks().Count, 0, "in pieces");
        }

        [UnityTest]
        public IEnumerator TakingAFeatureAwayRebuildsNoOtherOnABigField()
        {
            yield return Begin();
            Bare();
            string[] kinds = { "mock_tree", "mock_wall", "mock_hut", "mock_tree_dead", "mock_stone_body" };
            var size = mock.Terrain.Size;
            int placed = 0;
            for (int z = 4; z < size.y - 4 && placed < 3000; z++)
                for (int x = 4; x < size.x - 4 && placed < 3000; x += 2)
                    if (mock.PlaceFeature(Def(kinds[placed % kinds.Length]), x, z) >= 0) placed++;
            Assert.Greater(placed, 1500, "a big field");
            yield return null;
            yield return null;
            Assert.AreEqual(0, Ents.FeaturesRebuilt, "nothing changes, nothing is built");

            Ents.RebuildFeatures();
            yield return null;
            double all = root.RenderMs;
            Assert.AreEqual(placed, Ents.FeaturesRebuilt);
            yield return null;
            double still = root.RenderMs;

            Assert.IsTrue(mock.RemoveFeature(0));
            yield return null;
            Assert.AreEqual(0, Ents.FeaturesRebuilt, "the first taken away, the rest stay");
            double first = root.RenderMs;
            Assert.IsTrue(mock.RemoveFeature(placed / 2));
            Assert.IsTrue(mock.RemoveFeature(placed / 2 + 7));
            yield return null;
            Assert.AreEqual(0, Ents.FeaturesRebuilt, "two from the middle");
            Assert.GreaterOrEqual(mock.PlaceFeature(Def("mock_tree"), 2, 2), 0);
            yield return null;
            Assert.AreEqual(1, Ents.FeaturesRebuilt, "one put down is one built");
            Debug.Log($"{placed} features: a frame building them all {all:0.0} ms, a still frame {still:0.0} ms, the frame the first is taken away {first:0.0} ms");
        }

        [UnityTest]
        public IEnumerator ADyingUnitsPiecesLeaveIt()
        {
            yield return Begin("mock_highlands", 2);
            // No scenery, so every chunk in play is a piece.
            Bare();
            int foe = mock.Players.First(p => !p.IsLocal).Index;
            var units = new UnitState[EntityRenderer.MaxUnits];
            int n = mock.ReadUnits(units);
            var theirs = units.Take(n).Where(u => u.Player == foe).ToList();
            Assert.Greater(theirs.Count, 0);
            Look(theirs[0].Position, 30f);
            yield return null;
            int since = 0, k;
            var buf = new PieceEvent[512];
            while ((k = mock.ReadPieceEvents(since, buf)) > 0) since = buf[k - 1].Id;
            mock.Rout(foe);
            int thrown = mock.ReadPieceEvents(since, buf);
            Assert.Greater(thrown, 3, "the dying throw pieces");
            yield return null;
            Assert.Greater(Ents.PiecesThrown, 0);
            for (int i = 0; i < thrown; i++) Assert.IsTrue(Ents.PieceThrown(buf[i].Unit, buf[i].Piece), "the unit no longer draws its piece");
            var start = Chunks();
            Assert.Greater(start.Count, 0, "the pieces fly");
            yield return Ticks(45);
            var later = Chunks();
            Assert.AreEqual(start.Count, later.Count);
            Assert.Greater(Enumerable.Range(0, start.Count).Average(i => (later[i] - start[i]).magnitude), 0.5f, "they flew from where they were");
            Assert.Less(later.Average(p => p.y), start.Average(p => p.y), "and came down");
            yield return Ticks(45);
            Assert.AreEqual(0, Ents.PiecesThrown, "the dead are gone, so nothing is hidden on them");
            Assert.Greater(Chunks(Debris.State.Resting).Count, 0, "the pieces lie on the ground");
        }

        double drawWith, drawWithout;
        int gpuChunks;

        // A frame drawn at 1440p and waited for, with the chunks and dust and without.
        IEnumerator GpuCost()
        {
            var cam = Camera.main;
            var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.DefaultHDR);
            var px = new Texture2D(1, 1, TextureFormat.RGBAFloat, false);
            var old = cam.targetTexture;
            double Draw()
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = null;
                cam.targetTexture = old;
                return clock.Elapsed.TotalMilliseconds;
            }
            gpuChunks = Ents.Debris.Drawn;
            var with = new List<double>();
            var without = new List<double>();
            for (int round = 0; round < 6; round++)
            {
                Ents.DrawBreaking = round % 2 == 0;
                yield return null;
                for (int k = 0; k < 2; k++) Draw();
                for (int k = 0; k < 8; k++) (Ents.DrawBreaking ? with : without).Add(Draw());
            }
            Ents.DrawBreaking = true;
            with.Sort();
            without.Sort();
            drawWith = with[with.Count / 2];
            drawWithout = without[without.Count / 2];
            Object.Destroy(rt);
            Object.Destroy(px);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator ABattleFullOfDeathsBreaksInsideItsBudget()
        {
            // Four kingdoms, so two can be routed and the war goes on.
            yield return Begin("mock_highlands", 3);
            Bare();
            string[] kinds = { "mock_tree", "mock_wall", "mock_hut", "mock_tree_dead", "mock_stone_body" };
            var c = mock.StageCentre;
            var targets = new List<int>();
            float sea = mock.Terrain.SeaLevel, cell = mock.Terrain.CellSize;
            for (int z = -30; z <= 30 && targets.Count < 170; z += 3)
                for (int x = -40; x <= 40 && targets.Count < 170; x += 4)
                {
                    int cx = Mathf.RoundToInt(c.x / cell) + x, cz = Mathf.RoundToInt(-c.z / cell) + z;
                    if (mock.GroundHeight(cx * cell, -cz * cell) < sea + 0.5f) continue;
                    targets.Add(mock.PlaceFeature(Def(kinds[targets.Count % kinds.Length]), cx, cz));
                }
            Assert.Greater(targets.Count, 100, "a field of scenery on dry land");
            Look(c, 90f);
            yield return Ticks(3);
            var ms = new List<double>();
            int peakMoving = 0, peakActive = 0, peakDrawn = 0, broke = 0;
            var rng = new System.Random(5);
            for (int frame = 0; frame < 600; frame++)
            {
                if (frame % 12 == 0)
                {
                    var fs = new FeatureState[EntityRenderer.MaxFeatures];
                    int nf = mock.ReadFeatures(fs);
                    for (int k = 0; k < 14 && nf > 0; k++)
                    {
                        var at = fs[rng.Next(nf)].Position;
                        mock.FireFx("ARACAN 1", at + new Vector3(0f, 1f, -8f), at);
                    }
                }
                var foes = mock.Players.Where(p => !p.IsLocal).ToList();
                if (frame == 200) mock.Rout(foes[0].Index);
                if (frame == 300 && foes.Count > 2) mock.Rout(foes[1].Index);
                mock.Advance(1);
                yield return null;
                if (frame == 320) yield return GpuCost();
                if (frame > 5) ms.Add(Ents.BreakMs);
                peakMoving = Mathf.Max(peakMoving, Ents.Debris.Moving);
                peakActive = Mathf.Max(peakActive, Ents.Debris.Active);
                peakDrawn = Mathf.Max(peakDrawn, Ents.Debris.Drawn);
                if (Ents.Breaking > 0) broke++;
            }
            ms.Sort();
            double mean = ms.Average(), p95 = ms[(int)(ms.Count * 0.95)], worst = ms[ms.Count - 1];
            Debug.Log($"Drawing the breaking at 1440p with {gpuChunks} chunks drawn: {drawWith:0.00} ms a frame with it and {drawWithout:0.00} ms without, {drawWith - drawWithout:0.00} ms for the chunks and dust");
            Assert.AreEqual(GameStatus.Running, mock.Status, "the war went on to the end");
            Debug.Log($"Breaking in a battle of deaths, {targets.Count} pieces of scenery under fire and two armies routed, effects {Ents.Debris.Budget.Level}: {mean:0.000} ms a frame on average, " +
                      $"{p95:0.000} ms at the 95th percentile, {worst:0.00} ms at worst; at most {peakMoving} chunks moving, {peakActive} in play and {peakDrawn} drawn; " +
                      $"{Ents.Fractures.Ready} kinds split; {broke} frames with scenery breaking");
            Assert.Greater(broke, 30, "scenery broke");
            Assert.Less(mean, 2.0, "inside the plan's 2 ms of main thread");
            Assert.LessOrEqual(peakMoving, Ents.Debris.Budget.Flying, "inside the setting's flying chunks");
        }
    }
}
