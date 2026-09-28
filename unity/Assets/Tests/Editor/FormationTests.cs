// FormationTests.cs - the formation drag's pure parts: facing, frontage and
// ranks, roles, wings, the four shapes and "as they stand", matching units
// to slots, snapping off impassable ground, and the gesture itself.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FormationTests
    {
        static FormationKind Kind(FormationRole role, int fp = 2, FormationMover mover = FormationMover.Walker) =>
            new FormationKind { Role = role, Footprint = fp, Mover = mover, Layer = FormationRoles.LayerOf(mover) };

        // Members in handle order, standing in a loose grid south of the line.
        static FormationLayout Layout(params (FormationRole role, int count, int fp)[] groups)
        {
            var l = new FormationLayout();
            int n = groups.Sum(g => g.count);
            l.Reserve(n);
            int k = 0;
            foreach (var g in groups)
                for (int i = 0; i < g.count; i++, k++)
                    l.Members[k] = new FormationMember { Handle = 100 + k, Position = new Vector2(20 + (k % 10) * 3, -60 - (k / 10) * 3), Kind = Kind(g.role, g.fp) };
            l.Count = n;
            return l;
        }

        static FormationRequest Drag(Vector2 a, Vector2 b, FormationShape shape = FormationShape.Line, bool faceAbout = false) =>
            new FormationRequest { A = a, B = b, Shape = shape, FaceAbout = faceAbout, CameraRight = Vector2.right };

        static FormationLayout Planned(FormationLayout l, FormationRequest r)
        {
            FormationPlanner.Plan(l, r);
            new FormationAssign().Assign(l);
            new FormationSnap().Snap(l, null);
            return l;
        }

        static IEnumerable<FormationSlot> SlotsOf(FormationLayout l, FormationRole role) =>
            l.Slots.Take(l.Count).Where(s => s.Role == role);

        static void Near(Vector2 expected, Vector2 got, string message = "")
        {
            Assert.Less((expected - got).magnitude, 1e-3f, $"{message} expected {expected} got {got}");
        }

        static int[] RankSizes(IEnumerable<FormationSlot> slots) =>
            slots.GroupBy(s => Mathf.Round(s.Local.y * 100)).OrderByDescending(g => g.Key).Select(g => g.Count()).ToArray();

        // ---- 1. Facing ----

        [Test]
        public void TheDragDirectionSetsTheFacingAndFTurnsItAbout()
        {
            var a = new Vector2(20, -40);
            (Vector2 to, float heading)[] cases = { (new Vector2(40, -40), 0f), (new Vector2(0, -40), 180f), (new Vector2(20, -60), 90f) };
            foreach (var c in cases)
            {
                var l = Planned(Layout((FormationRole.Melee, 4, 2)), Drag(a, c.to));
                Assert.AreEqual(c.heading, l.Heading, 0.01f, $"drag to {c.to}");
                var turned = Planned(Layout((FormationRole.Melee, 4, 2)), Drag(a, c.to, faceAbout: true));
                Assert.AreEqual((c.heading + 180f) % 360f, turned.Heading, 0.01f, $"faced about, drag to {c.to}");
            }
        }

        [Test]
        public void FacingAwayFromTheGroupIgnoresTheDragDirection()
        {
            // The group stands south of an east-west line: it faces north
            // whichever way the line was drawn.
            foreach (var (a, b) in new[] { (new Vector2(10, -40), new Vector2(40, -40)), (new Vector2(40, -40), new Vector2(10, -40)) })
            {
                var r = Drag(a, b);
                r.Facing = FormationFacing.AwayFromGroup;
                Assert.AreEqual(0f, Planned(Layout((FormationRole.Melee, 6, 2)), r).Heading, 0.01f);
            }
        }

        // ---- 2 and 3. Frontage and ranks ----

        [Test]
        public void TwelveMeleeAndEightArchersOnAnEighteenCellLine()
        {
            var l = Planned(Layout((FormationRole.Melee, 12, 2), (FormationRole.Ranged, 8, 2)), Drag(new Vector2(10, -30), new Vector2(28, -30)));
            CollectionAssert.AreEqual(new[] { 6, 6 }, RankSizes(SlotsOf(l, FormationRole.Melee)));
            CollectionAssert.AreEqual(new[] { 6, 2 }, RankSizes(SlotsOf(l, FormationRole.Ranged)));
            float lastMelee = SlotsOf(l, FormationRole.Melee).Min(s => s.Local.y);
            Assert.IsTrue(SlotsOf(l, FormationRole.Ranged).All(s => s.Local.y < lastMelee), "archers stand behind the melee");
            var back = SlotsOf(l, FormationRole.Ranged).Where(s => s.Local.y < SlotsOf(l, FormationRole.Ranged).Max(t => t.Local.y) - 0.01f).ToArray();
            Assert.AreEqual(0f, back.Sum(s => s.Local.x), 0.01f, "the last two are centred");
            // Nothing stands in front of the line, and the front rank is on it.
            for (int i = 0; i < l.Count; i++)
            {
                Assert.LessOrEqual(l.Slots[i].World.y, -30f + 0.001f);
                if (Mathf.Abs(l.Slots[i].Local.y) < 0.01f) Assert.AreEqual(-30f, l.Slots[i].World.y, 0.001f);
            }
            Assert.AreEqual(6, l.Wide);
            Assert.AreEqual(4, l.Deep);
        }

        [Test]
        public void AShortDragMakesAFileAndALongOneStopsAtItsWidest()
        {
            var a = new Vector2(10, -30);
            var file = Planned(Layout((FormationRole.Melee, 5, 2)), Drag(a, a + new Vector2(1.5f, 0)));
            Assert.IsTrue(file.Slots.Take(5).All(s => Mathf.Abs(s.Local.x) < 0.001f), "one unit wide");
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1 }, RankSizes(file.Slots.Take(5)));

            var wide = Planned(Layout((FormationRole.Melee, 12, 2)), Drag(a, a + new Vector2(60, 0)));
            Assert.AreEqual(36f, wide.Frontage, 0.001f, "12 at a pitch of 3");
            Assert.AreEqual(a.x + 1.5f, wide.Slots.Take(12).Min(s => s.World.x), 0.001f, "the formation grows out from the press");
            CollectionAssert.AreEqual(new[] { 12 }, RankSizes(wide.Slots.Take(12)));
        }

        // ---- 4. Wings ----

        [Test]
        public void CavalryRideOnTheWingsOrFormTheFront()
        {
            var l = Planned(Layout((FormationRole.Melee, 10, 2), (FormationRole.Cavalry, 4, 3)), Drag(new Vector2(10, -30), new Vector2(40, -30)));
            float h = SlotsOf(l, FormationRole.Melee).Max(s => s.Local.x);
            Assert.AreEqual(2, SlotsOf(l, FormationRole.Cavalry).Count(s => s.Local.x < -h), "two on the left wing");
            Assert.AreEqual(2, SlotsOf(l, FormationRole.Cavalry).Count(s => s.Local.x > h), "two on the right wing");

            var alone = Planned(Layout((FormationRole.Cavalry, 4, 3), (FormationRole.Command, 1, 2)), Drag(new Vector2(10, -30), new Vector2(26, -30)));
            Assert.IsTrue(SlotsOf(alone, FormationRole.Cavalry).All(s => Mathf.Abs(s.Local.y) < 0.01f), "four riders alone are the front");
            Assert.IsTrue(SlotsOf(alone, FormationRole.Command).All(s => s.Local.y < -1f));
        }

        [Test]
        public void ALeftRiderTakesTheLeftWing()
        {
            var l = Layout((FormationRole.Melee, 6, 2), (FormationRole.Cavalry, 2, 3));
            l.Members[6].Position = new Vector2(0, -40);
            l.Members[7].Position = new Vector2(60, -40);
            Planned(l, Drag(new Vector2(20, -30), new Vector2(38, -30)));
            var left = SlotsOf(l, FormationRole.Cavalry).OrderBy(s => s.Local.x).First();
            Assert.AreEqual(6, left.Member, "the rider already on the left");
        }

        // ---- 5 and 6. Matching ----

        [Test]
        public void AFormationMovedWithoutTurningKeepsItsPlaces()
        {
            foreach (var shape in new[] { FormationShape.Line, FormationShape.Block, FormationShape.Wedge, FormationShape.Loose })
            {
                var l = Planned(Layout((FormationRole.Melee, 12, 2), (FormationRole.Ranged, 6, 2), (FormationRole.Cavalry, 4, 3)), Drag(new Vector2(10, -60), new Vector2(28, -60), shape));
                var before = new int[l.Count];
                for (int i = 0; i < l.Count; i++) { before[i] = l.Slots[i].Member; l.Members[l.Slots[i].Member].Position = l.Slots[i].World; }
                Planned(l, Drag(new Vector2(10, -40), new Vector2(28, -40), shape));
                for (int i = 0; i < l.Count; i++) Assert.AreEqual(before[i], l.Slots[i].Member, $"{shape}: slot {i} keeps its unit");
            }
        }

        static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Side(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            float d1 = Side(c, d, a), d2 = Side(c, d, b), d3 = Side(a, b, c), d4 = Side(a, b, d);
            const float e = 1e-3f;
            return (d1 > e && d2 < -e || d1 < -e && d2 > e) && (d3 > e && d4 < -e || d3 < -e && d4 > e);
        }

        static int Crossings(FormationLayout l, Func<FormationSlot, FormationSlot, bool> together)
        {
            int c = 0;
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    var s = l.Slots[i];
                    var t = l.Slots[j];
                    if (s.Role != t.Role || !together(s, t)) continue;
                    if (Cross(l.Members[s.Member].Position, s.Wanted, l.Members[t.Member].Position, t.Wanted)) c++;
                }
            return c;
        }

        [Test]
        public void RandomGroupsGetShortUncrossedPaths()
        {
            var rng = new System.Random(7);
            for (int round = 0; round < 40; round++)
            {
                int melee = rng.Next(1, 40), ranged = rng.Next(0, 24);
                var l = Layout((FormationRole.Melee, melee, 2), (FormationRole.Ranged, ranged, 2));
                for (int i = 0; i < l.Count; i++) l.Members[i].Position = new Vector2((float)rng.NextDouble() * 60f, -20f - (float)rng.NextDouble() * 60f);
                var a = new Vector2((float)rng.NextDouble() * 60f, -(float)rng.NextDouble() * 60f);
                var b = a + new Vector2((float)rng.NextDouble() * 40f - 20f, (float)rng.NextDouble() * 40f - 20f);
                Planned(l, Drag(a, b, (FormationShape)(round % 4)));
                Assert.AreEqual(0, Crossings(l, (s, t) => true), $"round {round}: no two paths in a role group cross");
                CollectionAssert.AreEquivalent(Enumerable.Range(0, l.Count), l.Slots.Take(l.Count).Select(s => s.Member), "every unit gets one slot");
            }
        }

        [Test]
        public void TheMatchingIsTheShortestForSmallGroups()
        {
            var rng = new System.Random(3);
            for (int round = 0; round < 20; round++)
            {
                var l = Layout((FormationRole.Melee, 6, 2));
                for (int i = 0; i < 6; i++) l.Members[i].Position = new Vector2((float)rng.NextDouble() * 30f, -(float)rng.NextDouble() * 30f);
                Planned(l, Drag(new Vector2(5, -10), new Vector2(5 + (float)rng.NextDouble() * 20f, -12)));
                float got = 0;
                for (int i = 0; i < 6; i++) got += (l.Members[l.Slots[i].Member].Position - l.Slots[i].Wanted).magnitude;
                float best = float.MaxValue;
                foreach (var perm in Permutations(Enumerable.Range(0, 6).ToArray()))
                {
                    float sum = 0;
                    for (int i = 0; i < 6; i++) sum += (l.Members[perm[i]].Position - l.Slots[i].Wanted).magnitude;
                    best = Mathf.Min(best, sum);
                }
                Assert.AreEqual(best, got, 1e-3f, $"round {round}");
            }
        }

        static IEnumerable<int[]> Permutations(int[] a, int k = 0)
        {
            if (k == a.Length) { yield return (int[])a.Clone(); yield break; }
            for (int i = k; i < a.Length; i++)
            {
                (a[k], a[i]) = (a[i], a[k]);
                foreach (var p in Permutations(a, k + 1)) yield return p;
                (a[k], a[i]) = (a[i], a[k]);
            }
        }

        [Test]
        public void LargeGroupsAreSortedByProjection()
        {
            // A hundred melee in a grid, sent to a line far ahead.
            var l = Layout((FormationRole.Melee, 100, 2));
            for (int i = 0; i < 100; i++) l.Members[i].Position = new Vector2(10 + (i % 10) * 3, -80 - (i / 10) * 3);
            Planned(l, Drag(new Vector2(20, -20), new Vector2(50, -20)));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 100), l.Slots.Take(100).Select(s => s.Member));
            CollectionAssert.AreEqual(Enumerable.Repeat(10, 10).ToArray(), RankSizes(l.Slots.Take(100)), "ten ranks of ten");
            Assert.AreEqual(0, Crossings(l, (s, t) => Mathf.Abs(s.Local.y - t.Local.y) < 0.01f), "no paths cross within a rank");
            // The front rank goes to the units in front.
            var frontUnits = l.Slots.Take(100).Where(s => Mathf.Abs(s.Local.y) < 0.01f).Select(s => l.Members[s.Member].Position.y).ToArray();
            Assert.IsTrue(frontUnits.All(y => y >= -80 - 0.01f), "the front rank came from the front");
        }

        [Test]
        public void TheSameInputGivesTheSameFormation()
        {
            var rng = new System.Random(11);
            var l1 = Layout((FormationRole.Melee, 30, 2), (FormationRole.Ranged, 80, 2), (FormationRole.Cavalry, 9, 3), (FormationRole.Command, 2, 2));
            for (int i = 0; i < l1.Count; i++) l1.Members[i].Position = new Vector2((float)rng.NextDouble() * 60f, -(float)rng.NextDouble() * 60f);
            var l2 = new FormationLayout();
            l2.Reserve(l1.Count);
            Array.Copy(l1.Members, l2.Members, l1.Count);
            l2.Count = l1.Count;
            foreach (var shape in new[] { FormationShape.Line, FormationShape.Block, FormationShape.Wedge, FormationShape.Loose })
            {
                var r = Drag(new Vector2(12, -20), new Vector2(40, -31), shape);
                Planned(l1, r);
                Planned(l2, r);
                for (int i = 0; i < l1.Count; i++)
                {
                    Assert.AreEqual(l1.Slots[i].Member, l2.Slots[i].Member);
                    Assert.AreEqual(l1.Slots[i].World, l2.Slots[i].World);
                }
            }
        }

        // ---- 7. Snapping ----

        sealed class Ground : IFormationGround
        {
            public int Width { get; set; } = 60;
            public int Height { get; set; } = 60;
            public Func<int, int, bool> Land = (x, y) => true;
            public bool CanStand(FormationMover mover, int x, int y) =>
                mover == FormationMover.Flyer || (mover == FormationMover.Boat ? !Land(x, y) : Land(x, y));
        }

        [Test]
        public void AWalkerOnWaterStepsBackToLand()
        {
            // Water north of row 17 and land south of it, the line on the water.
            var g = new Ground { Land = (x, y) => y >= 17 };
            var l = Layout((FormationRole.Melee, 8, 2), (FormationRole.Ranged, 8, 2));
            FormationPlanner.Plan(l, Drag(new Vector2(10, -16), new Vector2(34, -16)));
            new FormationAssign().Assign(l);
            new FormationSnap().Snap(l, g);
            var cells = new HashSet<(int, int)>();
            for (int i = 0; i < l.Count; i++)
            {
                var s = l.Slots[i];
                Assert.AreNotEqual(SlotState.Nowhere, s.State);
                int x0 = Mathf.FloorToInt(s.World.x - 1 + 0.5f), y0 = Mathf.FloorToInt(-s.World.y - 1 + 0.5f);
                for (int y = y0; y < y0 + 2; y++)
                    for (int x = x0; x < x0 + 2; x++)
                    {
                        Assert.IsTrue(g.Land(x, y), $"slot {i} stands on land");
                        Assert.IsTrue(cells.Add((x, y)), $"slot {i} shares cell {x},{y}");
                    }
                if (s.State == SlotState.Snapped) Assert.Less(s.World.y, s.Wanted.y, $"slot {i} moved back, not forward");
            }
            Assert.IsTrue(l.Slots.Take(l.Count).Any(s => s.State == SlotState.Snapped));
            Assert.IsTrue(l.Slots.Take(l.Count).Any(s => s.State == SlotState.Good), "slots on land stay");
        }

        [Test]
        public void ASlotWithNoLandNearIsSentAsDrawn()
        {
            var g = new Ground { Land = (x, y) => false };
            var l = Layout((FormationRole.Melee, 4, 2));
            FormationPlanner.Plan(l, Drag(new Vector2(20, -20), new Vector2(32, -20)));
            new FormationAssign().Assign(l);
            new FormationSnap().Snap(l, g);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(SlotState.Nowhere, l.Slots[i].State);
                Assert.AreEqual(l.Slots[i].Wanted, l.Slots[i].World);
            }
        }

        [Test]
        public void SlotsStayInsideTheMap()
        {
            var g = new Ground();
            var l = Layout((FormationRole.Melee, 6, 2));
            FormationPlanner.Plan(l, Drag(new Vector2(-10, 5), new Vector2(8, 5)));
            new FormationAssign().Assign(l);
            new FormationSnap().Snap(l, g);
            for (int i = 0; i < 6; i++)
            {
                var w = l.Slots[i].World;
                Assert.IsTrue(w.x >= 1f && w.x <= 59f && w.y <= -1f && w.y >= -59f, $"slot {i} at {w}");
            }
        }

        [Test]
        public void TheSearchFindsTheOneFreeSpotOnAFarCorner()
        {
            // Only a 2 by 2 patch on the corner of the sixth ring out can take it.
            var g = new Ground { Land = (x, y) => x >= 27 && x <= 28 && y >= 25 && y <= 26 };
            var l = Layout((FormationRole.Melee, 1, 2));
            FormationPlanner.Plan(l, Drag(new Vector2(20, -20), new Vector2(21, -20)));
            new FormationAssign().Assign(l);
            new FormationSnap().Snap(l, g);
            Assert.AreEqual(SlotState.Snapped, l.Slots[0].State);
            Near(new Vector2(28, -26), l.Slots[0].World, "centred on the cells it takes");
        }

        [Test]
        public void AFormationSpillingOffTheMapSnapsWithinItsBudget()
        {
            // A deep Loose of 256 drawn on a small map runs far past its edge.
            var g = new Ground { Width = 48, Height = 48 };
            var l = Layout((FormationRole.Melee, 128, 2), (FormationRole.Ranged, 128, 2));
            FormationPlanner.Plan(l, Drag(new Vector2(4, -10), new Vector2(34, -10), FormationShape.Loose));
            new FormationAssign().Assign(l);
            var snap = new FormationSnap();
            snap.Snap(l, g);
            // Its searches stop well short of the hundred thousand spots they would try.
            Assert.LessOrEqual(snap.Tried, 20000);
            for (int i = 0; i < l.Count; i++)
            {
                var w = l.Slots[i].World;
                Assert.IsTrue(w.x >= 1f && w.x <= 47f && w.y <= -1f && w.y >= -47f, $"slot {i} at {w} is inside the map");
            }
        }

        // ---- 8 and 9. Block, Wedge and Loose ----

        [Test]
        public void ABlockGuardsItsMonarchInTheMiddle()
        {
            var l = Planned(Layout((FormationRole.Melee, 24, 2), (FormationRole.Command, 1, 2)), Drag(new Vector2(10, -30), new Vector2(25, -30), FormationShape.Block));
            CollectionAssert.AreEqual(new[] { 5, 5, 5, 5, 5 }, RankSizes(l.Slots.Take(25)));
            var king = SlotsOf(l, FormationRole.Command).Single();
            Near(new Vector2(0, -6), king.Local);
            Assert.AreEqual(24, l.Members[king.Member].Handle - 100, "the monarch takes it");
        }

        [Test]
        public void AWedgeOfTenHasRanksOfOneToFourWithRidersAtThePoint()
        {
            var l = Planned(Layout((FormationRole.Cavalry, 2, 3), (FormationRole.Melee, 8, 2)), Drag(new Vector2(10, -30), new Vector2(22, -30), FormationShape.Wedge));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, RankSizes(l.Slots.Take(10)));
            var point = l.Slots.Take(10).Single(s => Mathf.Abs(s.Local.y) < 0.01f);
            Assert.AreEqual(FormationRole.Cavalry, point.Role);
            Assert.AreEqual(0f, point.Local.x, 0.001f);
        }

        [Test]
        public void LooseSpreadsFiveArchersOverAFortyCellLine()
        {
            var l = Planned(Layout((FormationRole.Ranged, 5, 2)), Drag(new Vector2(10, -30), new Vector2(50, -30), FormationShape.Loose));
            var xs = l.Slots.Take(5).Select(s => s.World.x).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { 10f, 20f, 30f, 40f, 50f }, xs);
        }

        // ---- As they stand ----

        [Test]
        public void AsTheyStandKeepsTheLayoutTurnedToTheNewFacing()
        {
            // Three units in a row facing north, sent to a line drawn right to
            // left: the row turns to face south and keeps its order mirrored.
            var l = Layout((FormationRole.Melee, 2, 2), (FormationRole.Ranged, 1, 2));
            l.Members[0].Position = new Vector2(10, -50);
            l.Members[1].Position = new Vector2(14, -50);
            l.Members[2].Position = new Vector2(12, -54);
            for (int i = 0; i < 3; i++) l.Members[i].Heading = 0f;
            var r = Drag(new Vector2(40, -20), new Vector2(30, -20));
            r.AsTheyStand = true;
            Planned(l, r);
            Assert.AreEqual(180f, l.Heading, 0.01f);
            for (int i = 0; i < 3; i++) Assert.AreEqual(i, l.Slots[i].Member, "no reassignment");
            Near(new Vector2(37, -20), l.Slots[0].World, "the west unit turns to the east end");
            Near(new Vector2(33, -20), l.Slots[1].World);
            Near(new Vector2(35, -16), l.Slots[2].World, "the one behind stays behind");
        }

        [Test]
        public void AsTheyStandCountsRoughRanks()
        {
            // Two ragged rows of five, facing north, sent north.
            var l = Layout((FormationRole.Melee, 10, 2));
            for (int i = 0; i < 10; i++)
            {
                l.Members[i].Position = new Vector2(10 + (i % 5) * 3, -50 - (i / 5) * 3 + (i % 3 - 1) * 0.3f);
                l.Members[i].Heading = 0f;
            }
            var r = Drag(new Vector2(10, -20), new Vector2(22, -20));
            r.AsTheyStand = true;
            Planned(l, r);
            Assert.AreEqual(5, l.Wide);
            Assert.AreEqual(2, l.Deep);
        }

        [Test]
        public void AGroupStillInItsLastFormationKeepsThatFacing()
        {
            var m = new FormationMember[4];
            for (int i = 0; i < 4; i++)
                m[i] = new FormationMember { Handle = i, Position = new Vector2(i * 3, 0), Heading = 180f, HasSlot = true, Slot = new Vector2(i * 3, 0.5f), SlotHeading = 90f, SlotFormation = 7 };
            Near(FormationPlanner.DirOf(90f), FormationPlanner.StandingFacing(m, 4, new Vector2(0, 20)), "the last formation's facing");
            for (int i = 0; i < 3; i++) m[i].Position += new Vector2(0, -10);
            Assert.AreEqual(0f, (FormationPlanner.StandingFacing(m, 4, new Vector2(0, 20)) - FormationPlanner.DirOf(180f)).magnitude, 1e-4f, "scattered from it, the headings decide");
            m[0].Heading = 0; m[1].Heading = 90; m[2].Heading = 270;
            Assert.AreEqual(0f, (FormationPlanner.StandingFacing(m, 4, new Vector2(4.5f, 20)) - Vector2.up).magnitude, 0.05f, "headings that disagree give way to the line");
        }

        // ---- 10. Roles ----

        [Test]
        public void RolesForTheStockUnitsAndTheRest()
        {
            void Is(string name, string cat, FormationRole role, FormationLayer layer)
            {
                var k = FormationRoles.Classify(name, cat, 0);
                Assert.AreEqual(role, k.Role, name);
                Assert.AreEqual(layer, k.Layer, name);
            }
            Is("ARAKNIGH", "", FormationRole.Cavalry, FormationLayer.Ground);
            Is("ARACAN", "", FormationRole.Siege, FormationLayer.Ground);
            Is("TARFIRE", "", FormationRole.Siege, FormationLayer.Ground);
            Is("VERMAGE", "", FormationRole.Command, FormationLayer.Ground);
            Is("ZONHUNT", "", FormationRole.Command, FormationLayer.Air);
            Is("TARSHIP", "", FormationRole.Ranged, FormationLayer.Air);
            Is("ZONKRAK", "", FormationRole.Ranged, FormationLayer.Water);
            Is("araknigh", "", FormationRole.Cavalry, FormationLayer.Ground);
            Is("XYZ", "XYZ MELEE ATTACK", FormationRole.Melee, FormationLayer.Ground);
            Is("MOD1", "ARA Monarch", FormationRole.Command, FormationLayer.Ground);
            Is("MOD2", "ARA BALLISTIC ATTACK", FormationRole.Ranged, FormationLayer.Ground);
            Is("MOD3", "ARA MAGIC ATTACK", FormationRole.Caster, FormationLayer.Ground);
            Is("MOD4", "VER BOAT BALLISTIC", FormationRole.Ranged, FormationLayer.Water);
            Is("MOD5", "ZON FLY MELEE", FormationRole.Melee, FormationLayer.Air);
            Is("MOD6", "", FormationRole.Command, FormationLayer.Ground);
            Assert.AreEqual(FormationRole.Cavalry, FormationRoles.Classify("MOD7", "ARA MELEE", 3, speed: 2.8f).Role);
            Assert.AreEqual(FormationRole.Siege, FormationRoles.Classify("MOD8", "ARA BALLISTIC", 4).Role);
            Assert.AreEqual(FormationMover.Hover, FormationRoles.Classify("VERMAGE", "", 0).Mover);
            Assert.AreEqual(2, FormationRoles.Classify("MOD9", "", 0).Footprint, "a footprint of 0 counts as 2");
        }

        [Test]
        public void EachLayerFormsOnItsOwn()
        {
            // Two layouts on the same line: the flyers' slots do not push the walkers'.
            var ground = Layout((FormationRole.Melee, 6, 2));
            var air = Layout((FormationRole.Caster, 3, 4));
            var r = Drag(new Vector2(10, -30), new Vector2(28, -30));
            Planned(ground, r);
            Planned(air, r);
            Assert.AreEqual(ground.Heading, air.Heading);
            Assert.AreEqual(6, ground.Wide);
            Assert.AreEqual(3, air.Wide);
        }

        // ---- 11. The gesture ----

        static PointerFrame At(Vector2 screen, bool onGround = true) =>
            new PointerFrame { Screen = screen, OnGround = onGround, Ground = new Vector3(screen.x / 10f, 0, -screen.y / 10f), Focused = true, Dpi = 96 };

        static PointerFrame Down(int b, Vector2 at) { var f = At(at); if (b == 0) { f.LeftDown = f.LeftHeld = true; } else { f.RightDown = f.RightHeld = true; } return f; }
        static PointerFrame Held(int b, Vector2 at) { var f = At(at); if (b == 0) f.LeftHeld = true; else f.RightHeld = true; return f; }
        static PointerFrame Up(int b, Vector2 at) { var f = At(at); if (b == 0) f.LeftUp = true; else f.RightUp = true; return f; }

        [Test]
        public void AShortPressIsAClickInEachScheme()
        {
            foreach (bool classic in new[] { false, true })
            {
                var g = new FormationGesture { Classic = classic };
                var p = new Vector2(400, 300);
                Assert.AreEqual(GestureEvent.Pending, g.Feed(Down(1, p), true, false));
                Assert.AreEqual(GestureEvent.Pending, g.Feed(Held(1, p + new Vector2(5, 5)), true, false));
                Assert.AreEqual(GestureEvent.Click, g.Feed(Up(1, p + new Vector2(5, 5)), true, false));
                Assert.AreEqual(p, g.Press.Screen, "the click happens where the press was");
                Assert.AreEqual(GestureState.Idle, g.State);
            }
            var c = new FormationGesture { Classic = true };
            var ctrl = Down(0, new Vector2(100, 100));
            ctrl.Ctrl = true;
            Assert.AreEqual(GestureEvent.Pending, c.Feed(ctrl, true, false));
            Assert.AreEqual(GestureEvent.Click, c.Feed(Up(0, new Vector2(108, 100)), true, false), "8 pixels with Ctrl is still a click");
        }

        [Test]
        public void PastTheThresholdTheDragGoesLiveAndTheReleaseSends()
        {
            var g = new FormationGesture { Classic = false };
            var p = new Vector2(400, 300);
            g.Feed(Down(1, p), true, false);
            Assert.AreEqual(GestureEvent.Pending, g.Feed(Held(1, p + new Vector2(11, 0)), true, false));
            Assert.AreEqual(GestureEvent.Started, g.Feed(Held(1, p + new Vector2(12, 0)), true, false));
            Assert.IsTrue(g.Live);
            Assert.AreEqual(GestureEvent.Dragging, g.Feed(Held(1, p + new Vector2(80, 0)), true, false));
            var up = Up(1, p + new Vector2(90, 0));
            up.Shift = true;
            up.Alt = true;
            Assert.AreEqual(GestureEvent.Committed, g.Feed(up, true, false));
            Assert.IsTrue(g.Shift, "Shift at the release queues");
            Assert.IsTrue(g.Alt, "Alt at the release sends as they stand");
        }

        [Test]
        public void EscapeTheOtherButtonLostFocusOrAMissedReleaseAborts()
        {
            var p = new Vector2(400, 300);
            Func<PointerFrame, PointerFrame>[] breaks =
            {
                f => { f.EscapeDown = true; return f; },
                f => { f.LeftDown = f.LeftHeld = true; return f; },
                f => { f.Focused = false; return f; },
                f => { f.RightHeld = false; return f; },
            };
            foreach (var brk in breaks)
            {
                var g = new FormationGesture { Classic = false };
                g.Feed(Down(1, p), true, false);
                g.Feed(Held(1, p + new Vector2(40, 0)), true, false);
                Assert.IsTrue(g.Live);
                Assert.AreEqual(GestureEvent.Aborted, g.Feed(brk(Held(1, p + new Vector2(60, 0))), true, false));
                Assert.AreEqual(GestureState.Idle, g.State);
                Assert.AreEqual(GestureEvent.None, g.Feed(Up(1, p + new Vector2(60, 0)), true, false), "the release after an abort does nothing");
            }
        }

        [Test]
        public void NoGestureOverTheUiWithNothingSelectedOrWithACommandArmed()
        {
            var p = new Vector2(400, 300);
            var g = new FormationGesture { Classic = false };
            var ui = Down(1, p);
            ui.OverUi = true;
            Assert.AreEqual(GestureEvent.None, g.Feed(ui, true, false));
            Assert.AreEqual(GestureEvent.None, g.Feed(Down(1, p), false, false), "nothing selected");
            Assert.AreEqual(GestureEvent.None, g.Feed(Down(1, p), true, true), "a command armed");
            var sky = Down(1, p);
            sky.OnGround = false;
            Assert.AreEqual(GestureEvent.None, g.Feed(sky, true, false), "off the ground");
            Assert.AreEqual(GestureEvent.None, g.Feed(Down(0, p), true, false), "the modern left button selects");
            var c = new FormationGesture { Classic = true };
            Assert.AreEqual(GestureEvent.None, c.Feed(Down(0, p), true, false), "a plain classic left drag is the box");
        }

        [Test]
        public void TheClassicRightDragHonoursItsOptionAndLargerThreshold()
        {
            var p = new Vector2(400, 300);
            var off = new FormationGesture { Classic = true, ClassicRightDrag = false };
            Assert.AreEqual(GestureEvent.None, off.Feed(Down(1, p), true, false));
            var on = new FormationGesture { Classic = true };
            on.Feed(Down(1, p), true, false);
            Assert.AreEqual(GestureEvent.Pending, on.Feed(Held(1, p + new Vector2(15, 0)), true, false), "15 pixels is still a deselect");
            Assert.AreEqual(GestureEvent.Started, on.Feed(Held(1, p + new Vector2(16, 0)), true, false));
            var hi = new FormationGesture { Classic = true };
            var d = Down(1, p);
            d.Dpi = 192;
            hi.Feed(d, true, false);
            var h = Held(1, p + new Vector2(20, 0));
            h.Dpi = 192;
            Assert.AreEqual(GestureEvent.Pending, hi.Feed(h, true, false), "the threshold scales with the screen");
        }

        [Test]
        public void TabFAndGChangeTheDragWhileItIsLive()
        {
            var p = new Vector2(400, 300);
            var g = new FormationGesture { Classic = false };
            g.Feed(Down(1, p), true, false);
            g.Feed(Held(1, p + new Vector2(40, 0)), true, false);
            var k = Held(1, p + new Vector2(41, 0));
            k.TabDown = k.FDown = k.GDown = true;
            g.Feed(k, true, false);
            Assert.AreEqual(FormationShape.Block, g.Shape);
            Assert.IsTrue(g.FaceAbout);
            Assert.IsTrue(g.PaceFlip);
            k.Alt = true;
            k.FDown = k.GDown = false;
            g.Feed(k, true, false);
            Assert.AreEqual(FormationShape.Block, g.Shape, "Tab waits while Alt is held");
            g.Feed(Up(1, p + new Vector2(41, 0)), true, false);
            g.Feed(Down(1, p), true, false);
            Assert.AreEqual(FormationShape.Block, g.Shape, "the shape sticks for the next drag");
            Assert.IsFalse(g.FaceAbout, "facing about is for one drag");
            Assert.IsFalse(g.PaceFlip, "so is the pace");
        }
    }
}
