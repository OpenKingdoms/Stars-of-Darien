// MockDestructionTests.cs - the mock reports destruction as the contract
// says: a blast with its weapon for every shot that lands, pieces thrown by
// the dying, and with SceneryBreaks on scenery that dies into its stages,
// fire that spreads downwind, and a wind that turns.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class MockDestructionTests
    {
        const int Tps = MockBackend.Tps;

        // Frost Pass has no sea, so the middle of the map is dry land.
        static MockBackend Loaded(float damageScale = 1f, string map = "mock_frost")
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = damageScale };
            var s = new SkirmishSetup { MapId = map, Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b;
        }

        // Everything after an id, read a few at a time as a renderer would.
        static List<BlastEvent> Blasts(MockBackend b, int since = 0)
        {
            var all = new List<BlastEvent>();
            var buf = new BlastEvent[16];
            int n;
            while ((n = b.ReadBlasts(since, buf)) > 0)
            {
                all.AddRange(buf.Take(n));
                since = buf[n - 1].Id;
            }
            return all;
        }

        static List<FeatureEvent> FeatureEvents(MockBackend b, int since = 0)
        {
            var buf = new FeatureEvent[4096];
            return buf.Take(b.ReadFeatureEvents(since, buf)).ToList();
        }

        static int LastBlast(MockBackend b) => Blasts(b).Select(e => e.Id).DefaultIfEmpty(0).Max();

        // Takes away the map's own scenery, so only a test's stands, and
        // returns the last feature event.
        static int Bare(MockBackend b)
        {
            var fs = new FeatureState[4096];
            for (int n = b.ReadFeatures(fs); n > 0; n--) b.RemoveFeature(n - 1);
            Assert.AreEqual(0, b.ReadFeatures(fs));
            return FeatureEvents(b).Select(e => e.Id).DefaultIfEmpty(0).Max();
        }

        static int Def(MockBackend b, string name) => b.FeatureDefs.First(d => d.Name == name).Id;

        static FeatureState FeatureAt(MockBackend b, int index)
        {
            var fs = new FeatureState[4096];
            int n = b.ReadFeatures(fs);
            Assert.Less(index, n);
            return fs[index];
        }

        // A feature set down in the middle of the map, cells (dx, dz) from it.
        static int Place(MockBackend b, string kind, int dx = 0, int dz = 0)
        {
            var c = b.StageCentre;
            return b.PlaceFeature(Def(b, kind), Mathf.RoundToInt(c.x) + dx, Mathf.RoundToInt(-c.z) + dz);
        }

        // A shot at a feature's place from a little way south.
        static void Shoot(MockBackend b, string weapon, int feature)
        {
            var at = FeatureAt(b, feature).Position;
            b.FireFx(weapon, at + new Vector3(0f, 1f, -8f), at);
        }

        [Test]
        public void EveryShotThatLandsIsABlastWithItsWeapon()
        {
            var b = Loaded();
            b.SeeAll(true);
            Assert.IsTrue(b.StageFx("ground"));
            b.Advance(Tps * 5);
            var all = Blasts(b);
            for (int i = 1; i < all.Count; i++) Assert.AreEqual(all[i - 1].Id + 1, all[i].Id, "ids rise by one");

            var cannon = all.First(e => e.Weapon != null && e.Weapon.Name == "ARACAN 1");
            Assert.AreEqual(BlastCause.Weapon, cannon.Cause);
            Assert.AreEqual("ballistic", cannon.Weapon.Type);
            Assert.AreEqual("explosion", cannon.Weapon.DamageKind);
            Assert.AreEqual("large explosion", cannon.Weapon.ExplosionClass);
            Assert.AreEqual(cannon.Weapon.AreaOfEffect * 0.5f, cannon.Radius, 1e-4f, "half the area");
            Assert.Greater(cannon.Radius, 2f);
            Assert.AreEqual(cannon.Weapon.Damage, cannon.Damage);
            Assert.AreEqual(1f, cannon.Direction.magnitude, 1e-3f);
            Assert.Less(cannon.Direction.y, 0f, "the ball comes down");
            Assert.AreEqual(b.GroundHeight(cannon.Position.x, cannon.Position.z), cannon.Position.y, 0.01f, "it burst on the ground");
            Assert.AreEqual(-1, cannon.Unit);
            Assert.AreEqual(BlastFlags.None, cannon.Flags);
            Assert.AreEqual(b.LocalPlayer, cannon.Player);
            Assert.GreaterOrEqual(cannon.Shooter, 0, "the staged gunner");

            var hail = all.Where(e => e.Weapon?.Name == "ARAPRIES 2").ToList();
            Assert.Greater(hail.Count, 3, "a drop of hail at a time");
            Assert.IsTrue(hail.All(e => e.Direction == Vector3.down));
            Assert.IsTrue(all.Any(e => e.Weapon?.Name == "TARMAGE 3" && (e.Flags & BlastFlags.FireStarter) != 0), "the fire storm's meteors start fires");
        }

        [Test]
        public void ReadingFromTheLastIdGivesWhatCameAfter()
        {
            var b = Loaded();
            b.StageFx("ground");
            b.Advance(Tps * 5);
            var all = Blasts(b);
            Assert.Greater(all.Count, 3);
            var two = new BlastEvent[2];
            Assert.AreEqual(2, b.ReadBlasts(0, two), "a full buffer");
            Assert.AreEqual(all[0].Id, two[0].Id, "oldest first");
            Assert.AreEqual(2, b.ReadBlasts(two[1].Id, two));
            Assert.AreEqual(all[2].Id, two[0].Id);
            int last = all[all.Count - 1].Id;
            Assert.AreEqual(0, b.ReadBlasts(last, two), "nothing new until the game runs");
            Assert.AreEqual(0, b.ReadBlasts(0, null));
            b.Advance(Tps * 4);
            var more = Blasts(b, last);
            Assert.IsNotEmpty(more);
            Assert.AreEqual(last + 1, more[0].Id);
        }

        [Test]
        public void AnArrowThatStrikesAUnitIsADirectHit()
        {
            var b = Loaded();
            b.SeeAll(true);
            var us = new UnitState[256];
            int n = b.ReadUnits(us);
            var archer = us.Take(n).First(u => b.RoleOf(u.Def) == MockBackend.Role.Archer && u.Player == b.LocalPlayer);
            var foe = us.Take(n).First(u => u.Player != b.LocalPlayer && !b.UnitDefs[u.Def].IsBuilding);
            var c = b.StageCentre;
            Assert.IsTrue(b.Place(archer.Handle, new Vector2(c.x, c.z - 6f), 0f));
            Assert.IsTrue(b.Place(foe.Handle, new Vector2(c.x, c.z + 6f), 180f));
            int since = LastBlast(b);
            var from = new Vector3(c.x, b.GroundHeight(c.x, c.z - 6f) + 1f, c.z - 6f);
            var to = new Vector3(c.x, b.GroundHeight(c.x, c.z + 6f) + 0.8f, c.z + 6f);
            b.FireFx("ARABOW 1", from, to, archer.Handle, foe.Handle);
            b.Advance(Tps * 2);
            var hit = Blasts(b, since).First(e => e.Weapon?.Name == "ARABOW 1");
            Assert.AreEqual(foe.Handle, hit.Unit);
            Assert.IsTrue((hit.Flags & BlastFlags.DirectHit) != 0, "an arrow has no area, so scenery is spared");
            Assert.AreEqual(0f, hit.Radius);
            Assert.AreEqual(archer.Handle, hit.Shooter);
            Assert.AreEqual(archer.Def, hit.Def);
            Assert.Greater(hit.Direction.z, 0.5f, "flying north");
        }

        [Test]
        public void EachMockFighterNamesItsWeapons()
        {
            var b = new MockBackend();
            int Role(MockBackend.Role r) => Enumerable.Range(0, b.UnitDefs.Count).First(d => b.RoleOf(d) == r && !b.UnitDefs[d].IsBuilding);
            int mage = Role(MockBackend.Role.Mage), archer = Role(MockBackend.Role.Archer), knight = Role(MockBackend.Role.Knight);
            int lodge = Enumerable.Range(0, b.UnitDefs.Count).First(d => b.UnitDefs[d].IsBuilding);
            Assert.AreEqual("MOCK ARROW", b.Weapon(archer, 0).Name);
            Assert.AreEqual("MOCK FIREBALL", b.Weapon(mage, 0).Name);
            Assert.AreEqual("MOCK FIREBALL SPELL", b.Weapon(mage, 1).Name);
            Assert.AreEqual("MOCK FROST SPELL", b.Weapon(mage, 2).Name);
            Assert.IsNull(b.Weapon(mage, 3));
            Assert.AreEqual(mage, b.Weapon(mage, 1).Def);
            Assert.AreEqual(1, b.Weapon(mage, 1).Slot);
            Assert.AreEqual(WeaponFlags.FireStarter | WeaponFlags.Spell, b.Weapon(mage, 1).Flags);
            Assert.AreEqual("melee", b.Weapon(knight, 0).Type);
            Assert.AreEqual("MOCK DEATH BLAST", b.Weapon(lodge, WeaponSlot.Death).Name);
            Assert.IsNull(b.Weapon(archer, WeaponSlot.Death));
            Assert.IsNull(b.Weapon(-1, 0));
            Assert.IsNull(b.Weapon(b.UnitDefs.Count, 0));
            foreach (var w in MockBackend.FxWeapons)
            {
                Assert.IsNotEmpty(w.Info.Type, w.Name + " has a type");
                Assert.AreEqual(w.Name, w.Info.Name);
                Assert.AreEqual(w.Light, w.Info.Light);
            }
        }

        [Test]
        public void TheDyingThrowPiecesAndBuildingsAndMonarchsBurst()
        {
            var b = Loaded();
            b.SeeAll(true);
            int foe = b.Players.First(p => !p.IsLocal).Index;
            b.Rout(foe);
            var pieces = new PieceEvent[512];
            int n = b.ReadPieceEvents(0, pieces);
            Assert.Greater(n, 5);
            var poses = new PiecePose[64];
            bool shattered = false;
            foreach (var p in pieces.Take(n))
            {
                Assert.AreEqual(foe, p.Player);
                Assert.IsFalse(p.OutOfSight);
                Assert.AreNotEqual(PieceExplode.None, p.How & (PieceExplode.Fall | PieceExplode.Shatter));
                var model = b.GetModel(p.Model);
                Assert.Less(p.Piece, model.Pieces.Length);
                b.ReadUnitPose(p.Unit, poses);
                Assert.AreEqual(poses[p.Piece].Matrix, p.Pose, "the piece leaves from where it was");
                shattered |= model.Pieces[p.Piece].Name == "roof" && p.How == PieceExplode.Shatter;
            }
            Assert.IsTrue(shattered, "the lodge's roof shatters");
            var deaths = Blasts(b).Where(e => e.Cause == BlastCause.Death).ToList();
            Assert.AreEqual(2, deaths.Count, "the lodge and the monarch");
            foreach (var d in deaths)
            {
                Assert.AreEqual(WeaponSlot.Death, d.Slot);
                Assert.AreEqual("MOCK DEATH BLAST", d.Weapon.Name);
                Assert.AreEqual(Vector3.zero, d.Direction);
                Assert.Greater(d.Radius, 0f);
                Assert.AreEqual(foe, d.Player);
                Assert.GreaterOrEqual(d.Shooter, 0, "the dying unit");
            }
        }

        [Test]
        public void SceneryStandsUnlessItBreaks()
        {
            var b = Loaded();
            Assert.IsFalse(b.SceneryBreaks, "off, as the engine is today");
            Assert.AreEqual(0, FeatureEvents(b).Count, "the map's own scenery is no news");
            int events = Bare(b);
            int tree = Place(b, "mock_tree");
            int since = LastBlast(b);
            Shoot(b, "ARACAN 1", tree);
            b.Advance(Tps * 3);
            var shot = Blasts(b, since).First(e => e.Weapon?.Name == "ARACAN 1");
            Assert.AreEqual(tree, shot.Feature, "it landed on the tree");
            Assert.IsTrue(FeatureEvents(b, events).All(e => e.Kind == FeatureEventKind.Placed));
            Assert.AreEqual(Def(b, "mock_tree"), FeatureAt(b, tree).Def);
        }

        [Test]
        public void ATreeHitByACannonDiesAndStandsAsItsDeadStage()
        {
            var b = Loaded();
            b.SceneryBreaks = true;
            Bare(b);
            int tree = Place(b, "mock_tree");
            var at = FeatureAt(b, tree).Position;
            int since = FeatureEvents(b).Last().Id;
            Shoot(b, "ARACAN 1", tree);
            b.Advance(Tps * 2);
            var shot = Blasts(b).Last(e => e.Weapon?.Name == "ARACAN 1");
            var events = FeatureEvents(b, since);
            var hit = events.First(e => e.Kind == FeatureEventKind.Hit && e.Feature == tree);
            Assert.AreEqual(shot.Id, hit.Blast);
            Assert.AreEqual(shot.Position, hit.From);
            Assert.AreEqual(2000, hit.Damage);
            Assert.AreEqual(0, hit.Health);
            var dying = events.First(e => e.Kind == FeatureEventKind.Dying && e.Feature == tree);
            Assert.AreEqual(45, dying.Ticks, "its death animation");
            Assert.AreEqual(shot.Id, dying.Blast);
            b.Advance(dying.Ticks + 5);
            var dead = FeatureEvents(b, since).First(e => e.Kind == FeatureEventKind.Dead && e.Feature == tree);
            Assert.AreEqual(Def(b, "mock_tree"), dead.Def);
            Assert.AreEqual(Def(b, "mock_tree_dead"), dead.NewDef);
            Assert.AreEqual(dying.Tick + (uint)dying.Ticks - 1, dead.Tick);
            var now = FeatureAt(b, tree);
            Assert.AreEqual(dead.NewDef, now.Def, "the dead tree stands in its place");
            Assert.AreEqual(at, now.Position);
            Assert.GreaterOrEqual(now.Model, 0);
            Assert.AreEqual(b.FeatureDefs[dead.Def].DeadDef, dead.NewDef);
        }

        [Test]
        public void AWallCrumblesToRubbleThatShrugsOffHits()
        {
            var b = Loaded();
            b.SceneryBreaks = true;
            Bare(b);
            int wall = Place(b, "mock_wall");
            int since = FeatureEvents(b).Last().Id;
            for (int i = 0; i < 5; i++)
            {
                Shoot(b, "ARACAN 1", wall);
                b.Advance(Tps * 2);
            }
            var deaths = FeatureEvents(b, since).Where(e => e.Kind == FeatureEventKind.Dead).ToList();
            Assert.AreEqual(2, deaths.Count);
            Assert.AreEqual(Def(b, "mock_wall_a"), deaths[0].NewDef, "half its height");
            Assert.AreEqual(Def(b, "mock_rubble"), deaths[1].NewDef);
            Assert.AreEqual(Def(b, "mock_rubble"), FeatureAt(b, wall).Def);
            since = FeatureEvents(b).Last().Id;
            Shoot(b, "ARACAN 1", wall);
            b.Advance(Tps * 2);
            var hit = FeatureEvents(b, since).Single(e => e.Feature == wall);
            Assert.AreEqual(FeatureEventKind.Hit, hit.Kind);
            Assert.AreEqual(0, hit.Damage, "rubble ignores hits, though the blast reached it");
        }

        [Test]
        public void FireSpreadsDownwindAndLeavesBurntTrees()
        {
            var b = Loaded();
            b.SceneryBreaks = true;
            b.SpreadChance = 1f;
            b.SetWind(90f, MockBackend.WindMax);
            Assert.IsTrue(b.ReadWind(out var wind));
            Assert.AreEqual(1f, wind.Toward.x, 1e-4f, "blowing east");
            Assert.AreEqual(1f, wind.Strength);
            Bare(b);
            int lit = Place(b, "mock_tree");
            int downwind = Place(b, "mock_tree", 4);
            int upwind = Place(b, "mock_tree", -4);
            int beside = Place(b, "mock_tree", 0, 2);
            int far = Place(b, "mock_tree", 11);
            int since = FeatureEvents(b).Last().Id;
            Shoot(b, "TARARCH 1", lit);
            b.Advance(Tps * 7);
            var burning = FeatureEvents(b, since).Where(e => e.Kind == FeatureEventKind.Burning).ToList();
            var first = burning.First();
            Assert.AreEqual(lit, first.Feature, "the flaming arrow lit the tree it hit");
            Assert.AreNotEqual(0, first.Blast);
            Assert.AreEqual(240, first.Ticks);
            Assert.IsTrue(burning.Any(e => e.Feature == downwind), "sparks carry four cells downwind");
            Assert.IsTrue(burning.Any(e => e.Feature == beside), "and catch what is within three cells");
            Assert.IsFalse(burning.Any(e => e.Feature == upwind), "but not four cells upwind");
            Assert.IsFalse(burning.Any(e => e.Feature == far));
            Assert.AreEqual(0, burning.First(e => e.Feature == downwind).Blast, "caught from a neighbour");
            b.Advance(Tps * 3);
            var burnt = FeatureEvents(b, since).First(e => e.Kind == FeatureEventKind.Burnt && e.Feature == lit);
            Assert.AreEqual(Def(b, "mock_tree_burnt"), burnt.NewDef);
            Assert.AreEqual(burnt.NewDef, FeatureAt(b, lit).Def);
            Assert.AreEqual(Def(b, "mock_tree"), FeatureAt(b, upwind).Def);
        }

        [Test]
        public void TheWindTurnsAtMostFortyFiveDegreesAtATime()
        {
            var b = Loaded(0f);
            Assert.IsFalse(b.ReadWind(out _), "no wind until scenery breaks, as the engine has none yet");
            b.SceneryBreaks = true;
            Assert.IsTrue(b.ReadWind(out var wind));
            float last = wind.Heading;
            int turns = 0;
            for (int s = 0; s < 120; s++)
            {
                Assert.AreEqual(Tps, b.Advance(Tps));
                Assert.IsTrue(b.ReadWind(out wind));
                Assert.That(wind.Speed, Is.InRange(MockBackend.WindMin, MockBackend.WindMax));
                Assert.AreEqual(MockBackend.WindMax, wind.MaxSpeed);
                Assert.That(wind.Strength, Is.InRange(0f, 1f));
                Assert.AreEqual(1f, wind.Toward.magnitude, 1e-4f);
                float turn = Mathf.Abs(Mathf.DeltaAngle(last, wind.Heading));
                Assert.LessOrEqual(turn, 45.01f);
                if (turn > 0f) turns++;
                last = wind.Heading;
            }
            Assert.GreaterOrEqual(turns, 4, "redrawn every 10 to 20 seconds");
        }

        [Test]
        public void PlacedAndRemovedFeaturesAreReported()
        {
            var b = Loaded();
            int rock = Place(b, "mock_rock", 3, 3);
            var placed = FeatureEvents(b).Single();
            Assert.AreEqual(FeatureEventKind.Placed, placed.Kind);
            Assert.AreEqual(rock, placed.Feature);
            Assert.AreEqual(Def(b, "mock_rock"), placed.Def);
            Assert.AreEqual(FeatureAt(b, rock).Position, placed.Position);
            Assert.AreEqual(placed.Position, placed.From, "no blast behind it");
            Assert.IsTrue(b.RemoveFeature(rock));
            var removed = FeatureEvents(b, placed.Id).Single();
            Assert.AreEqual(FeatureEventKind.Removed, removed.Kind);
            Assert.AreEqual(rock, removed.Feature);
        }

        [Test]
        public void TheRingsKeepTheNewestAndAGapShowsWhatWasDropped()
        {
            var b = Loaded();
            for (int k = 0; k < MockBackend.RingSize * 2; k++) b.RemoveFeature(Place(b, "mock_rock", 3, 3));
            var events = FeatureEvents(b);
            Assert.That(events.Count, Is.InRange(MockBackend.RingSize, MockBackend.RingSize + MockBackend.RingSize / 4));
            Assert.Greater(events[0].Id, 1, "the oldest are gone");
            Assert.AreEqual(MockBackend.RingSize * 4, events[events.Count - 1].Id);
        }

        [Test]
        public void ABlastThePlayerCannotSeeSaysSo()
        {
            var b = Loaded();
            b.Advance(10);
            var c = b.StageCentre;
            int since = LastBlast(b);
            b.FireFx("ARACAN 1", c + new Vector3(0f, 1f, -8f), c);
            b.Advance(Tps * 2);
            var unseen = Blasts(b, since).First(e => e.Weapon?.Name == "ARACAN 1");
            Assert.IsTrue((unseen.Flags & BlastFlags.OutOfSight) != 0, "the middle of the map is in the fog");
            b.SeeAll(true);
            since = unseen.Id;
            b.FireFx("ARACAN 1", c + new Vector3(0f, 1f, -8f), c);
            b.Advance(Tps * 2);
            var seen = Blasts(b, since).First(e => e.Weapon?.Name == "ARACAN 1");
            Assert.AreEqual(BlastFlags.None, seen.Flags & BlastFlags.OutOfSight);
        }

        [Test]
        public void AShotIntoTheSeaBurstsOnWater()
        {
            var b = Loaded(1f, "mock_isles");
            var t = b.Terrain;
            Vector3? sea = null;
            for (int z = 0; z < t.HeightsH && sea == null; z += 2)
                for (int x = 0; x < t.HeightsW && sea == null; x += 2)
                    if (t.HeightAt(x, z) < t.SeaLevel - 0.5f) sea = new Vector3(x * t.CellSize, t.HeightAt(x, z), -z * t.CellSize);
            Assert.IsNotNull(sea, "the isles have a strait");
            int since = LastBlast(b);
            b.FireFx("ARACAN 1", sea.Value + new Vector3(0f, 4f, -8f), sea.Value);
            b.Advance(Tps * 2);
            var splash = Blasts(b, since).First(e => e.Weapon?.Name == "ARACAN 1");
            Assert.IsTrue((splash.Flags & BlastFlags.Water) != 0);
            Assert.AreEqual("medium water explosion", splash.Weapon.WaterExplosionClass);
        }

        [Test]
        public void TheStagedScenesBreakAndBurn()
        {
            var b = Loaded();
            b.SeeAll(true);
            Assert.IsTrue(b.StageBreak("break", 2f));
            Assert.IsTrue(b.SceneryBreaks);
            b.Advance(Tps * 20);
            var dead = FeatureEvents(b).Where(e => e.Kind == FeatureEventKind.Dead).Select(e => b.FeatureDefs[e.Def].Name).ToList();
            foreach (var kind in new[] { "mock_tree", "mock_wall", "mock_hut", "mock_stone_body" })
                Assert.Contains(kind, dead, kind + " broke");
            var b2 = Loaded();
            Assert.IsTrue(b2.StageBreak("fire"));
            b2.Advance(Tps * 15);
            int burning = FeatureEvents(b2).Count(e => e.Kind == FeatureEventKind.Burning);
            Assert.Greater(burning, 3, "the grove catches from one arrow");
            Assert.IsFalse(b2.StageBreak("nothing"));
        }
    }
}
