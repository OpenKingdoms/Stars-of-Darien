// EngineBackendTests.cs - IGameBackend on the real engine, the way the
// presentation's screens use it: the catalogue and a map preview before a
// game, then a skirmish loaded by pumping, and the snapshots, poses,
// players, economy and orders of a running battle. Needs okengine and the
// game files, and is ignored without them.
using System;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class EngineBackendTests
    {
        EngineBackend backend;

        [OneTimeSetUp]
        public void Boot()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            backend = new EngineBackend();
        }

        [OneTimeTearDown]
        public void End() => backend?.Dispose();

        static SkirmishSetup TwoCastles(string mine = "ARAMON", string theirs = "TAROS")
        {
            var s = new SkirmishSetup { MapId = "two castles", Seed = 7, LineOfSight = false, MapRevealed = true };
            // As GameRoot's default lineup has it: teams from 0, closed seats after.
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = mine, Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = theirs, Colour = 1, Team = 1 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 4, Team = 2 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 5, Team = 3 });
            return s;
        }

        // Stars of Darien plays the remastered battlefield rules everywhere.
        [Test, Order(0)]
        public void TheEnginePlaysTheRemasteredRules()
        {
            Assert.IsTrue(OkEngine.RemasteredRules);
            int on;
            try { on = OkEngine.okx_remastered(); }
            catch (EntryPointNotFoundException) { Assert.Ignore("this engine predates the remastered rules"); return; }
            Assert.AreEqual(1, on);
        }

        [Test, Order(1)]
        public void TheCatalogueAndAPreviewComeBeforeAnyGame()
        {
            Assert.Greater(backend.Maps.Count, 5);
            MapInfo two = null;
            foreach (var m in backend.Maps) if (m.Id == "two castles") two = m;
            Assert.IsNotNull(two);
            Assert.GreaterOrEqual(two.MaxPlayers, 2);
            var preview = backend.MapPreview("two castles", 256);
            Assert.IsNotNull(preview);
            Assert.AreEqual(preview.Width * preview.Height * 4, preview.Pixels.Length);
            Assert.AreEqual(5, backend.Sides.Count);
            // Sizes and starts in cells, the starts inside the map.
            Assert.AreEqual(0f, two.Size.x % 32f);
            Assert.GreaterOrEqual(two.Size.x, 64f);
            Assert.GreaterOrEqual(two.Starts.Length, 2);
            foreach (var st in two.Starts)
                Assert.IsTrue(st.x >= 0 && st.x < two.Size.x && st.y >= 0 && st.y < two.Size.y, "a start at " + st);
            // The menus' art comes from the engine.
            var art = backend.InterfaceArt("mainscreen", "ExitButton", 1);
            Assert.IsNotNull(art);
            Assert.AreEqual(3, art.Frames);
            Assert.AreEqual(art.Image.Width * art.Image.Height * 4, art.Image.Pixels.Length);
        }

        // A seat that claimed a start stands there, the rest take what is left.
        [Test, Order(21)]
        public void AClaimedStartIsWhereTheMonarchStands()
        {
            MapInfo two = null;
            foreach (var m in backend.Maps) if (m.Id == "two castles") two = m;
            var setup = TwoCastles();
            setup.Seats[0].Start = 1;
            backend.StartSkirmish(setup);
            LoadProgress p = default;
            for (int i = 0; i < 20000 && !p.Done && !p.Failed; i++) p = backend.PumpLoading();
            Assert.IsTrue(p.Done, p.Error);
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, king = -1;
            for (int i = 0; i < n && king < 0; i++)
            {
                var d = backend.UnitDefs[units[i].Def];
                if (units[i].Player == me && !d.IsBuilding && d.BuildOptions.Length > 0) king = i;
            }
            Assert.GreaterOrEqual(king, 0);
            var at = units[king].Position;
            var want = two.Starts[1];
            Assert.Less(Mathf.Abs(at.x - want.x), 4f, "east of " + want + ": " + at);
            Assert.Less(Mathf.Abs(-at.z - want.y), 4f, "south of " + want + ": " + at);
        }

        // A game warms what its armies can show, each side's own nimbus among
        // them, and not every strip the engine holds.
        [Test, Order(22)]
        public void AGameWarmsTheStripsItsArmiesCanShow()
        {
            backend.StartSkirmish(TwoCastles());
            LoadProgress p = default;
            for (int i = 0; i < 20000 && !p.Done && !p.Failed; i++) p = backend.PumpLoading();
            Assert.IsTrue(p.Done, p.Error);
            var warm = backend.WarmEffectStrips();
            int all = 0, nimbuses = 0;
            for (int i = 0, misses = 0; i < 1024 && misses < 32; i++)
            {
                if (OkEngine.okx_effect_strip(i, null, 0, out _, out _) > 0) { all++; misses = 0; }
                else misses++;
            }
            foreach (int s in warm)
            {
                Assert.Greater(OkEngine.okx_effect_strip(s, null, 0, out int w, out int h), 0, $"strip {s}");
                // A nimbus is 11 frames of 52 by 47.
                if (w == 52 * 11 && h == 47) nimbuses++;
            }
            Assert.Greater(warm.Count, 0);
            Assert.Less(warm.Count, all, "not every strip the engine holds");
            Assert.AreEqual(2, nimbuses, "the Aramon and Taros nimbuses");
        }

        // The HUD's build row shows every option of every builder in the
        // player's game at once, in one row at the default size. The
        // catalogue is whole once a game has loaded.
        [Test, Order(3)]
        public void EveryBuildersOptionsShowWithoutPaging()
        {
            int most = 0;
            string widest = "";
            foreach (var d in backend.UnitDefs)
            {
                int n = 0;
                foreach (int o in d.BuildOptions) if (o >= 0 && o < backend.UnitDefs.Count) n++;
                if (n > most) { most = n; widest = d.Name; }
            }
            Assert.Greater(most, 0);
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
            {
                foreach (int p in OpenKingdomsUnity.Game.UI.HudLayout.ScaleStops)
                    Assert.IsFalse(new OpenKingdomsUnity.Game.UI.HudLayout(size.x, size.y, p).Builds(most).Paged, $"{widest}'s {most} at {size} and {p}%");
                var g = new OpenKingdomsUnity.Game.UI.HudLayout(size.x, size.y, OpenKingdomsUnity.Game.UI.HudLayout.DefaultScale).Builds(most);
                Assert.AreEqual(1, g.Rows, $"{widest}'s {most} options in one row at {size}");
            }
        }

        [Test, Order(2)]
        public void ASkirmishLoadsByPumping()
        {
            backend.StartSkirmish(TwoCastles());
            Assert.AreEqual(GameStatus.Loading, backend.Status);
            LoadProgress p = default;
            int pumps = 0;
            for (; pumps < 5000 && !p.Done && !p.Failed; pumps++) p = backend.PumpLoading();
            Assert.Greater(pumps, 2, "the load comes in slices");
            Assert.IsTrue(p.Done, p.Error);
            // Two sides on teams 0 and 1 are enemies, so nobody has won yet.
            Assert.AreEqual(GameStatus.Running, backend.Status);
            backend.Advance(30);
            Assert.AreEqual(GameStatus.Running, backend.Status);
            Assert.AreNotEqual(backend.Players[0].Team, backend.Players[1].Team);
            var t = backend.Terrain;
            Assert.IsNotNull(t);
            Assert.AreEqual(1f, t.CellSize);
            Assert.AreEqual(t.HeightsW * t.HeightsH, t.Heights.Length);
            Assert.IsNotNull(backend.TerrainChunk(t.Blocks[0]));
            Assert.Greater(backend.UnitDefs.Count, 20);
            Assert.GreaterOrEqual(backend.Players.Count, 2);
            Assert.IsTrue(backend.Players[0].IsLocal);
            // The lineup is the lobby's own.
            Assert.AreEqual("ARAMON", backend.Players[0].Side);
            Assert.AreEqual("TAROS", backend.Players[1].Side);
            Assert.AreEqual(1, backend.Players[1].Colour);
            Assert.IsTrue(backend.Players[1].IsComputer);
        }

        [Test, Order(5)]
        public void TheGamesOwnClickSelectsAndOrders()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            int me = backend.LocalPlayer, mine = -1;
            for (int i = 0; i < n && mine < 0; i++)
                if (units[i].Player == me && (units[i].Flags & UnitFlags.Active) != 0) mine = i;
            Assert.GreaterOrEqual(mine, 0);
            var u = units[mine];
            backend.Cancel();
            backend.Click(u.Position, u.Handle, false);
            var sel = new int[8];
            Assert.AreEqual(1, backend.ReadSelection(sel));
            Assert.AreEqual(u.Handle, sel[0]);
            backend.Click(u.Position + new Vector3(18f, 0f, 0f), -1, false);
            backend.Advance(3);
            Assert.AreEqual(OrderKind.Move, backend.ReadOrder(u.Handle).Kind);
            Assert.IsTrue(backend.OrderSelection(CommandKind.Stop));
            backend.Advance(3);
            Assert.AreEqual(OrderKind.None, backend.ReadOrder(u.Handle).Kind);
            backend.Cancel();
            Assert.AreEqual(0, backend.ReadSelection(sel));
        }

        [Test, Order(7)]
        public void ABuildingPlacedTurnedStandsTurned()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, king = -1;
            for (int i = 0; i < n && king < 0; i++)
                if (units[i].Player == me && backend.UnitDefs[units[i].Def].BuildOptions.Length > 0) king = i;
            Assert.GreaterOrEqual(king, 0);
            var u = units[king];
            int hall = -1, lode = -1;
            foreach (int opt in backend.UnitDefs[u.Def].BuildOptions)
            {
                var d = backend.UnitDefs[opt];
                if (!d.IsBuilding) continue;
                if (!backend.CanRotate(opt)) { if (lode < 0) lode = opt; }
                else if (hall < 0 && d.Footprint.x != d.Footprint.y) hall = opt;
            }
            Assert.GreaterOrEqual(hall, 0, "a building with two side lengths");
            Assert.GreaterOrEqual(lode, 0, "a lodestone, which never turns");

            Vector3 site = default;
            bool found = false;
            for (int r = 6; r <= 40 && !found; r += 2)
                for (int k = 0; k < 8 && !found; k++)
                    found = backend.CanBuildAt(hall, u.Position + Quaternion.Euler(0, k * 45f, 0) * new Vector3(r, 0, 0), 1, out site);
            Assert.IsTrue(found);
            var c = new GameCommand { Kind = CommandKind.Build, Unit = u.Handle, Target = site, TargetUnit = -1, BuildDef = hall, Facing = 1 };
            Assert.IsTrue(backend.Command(c));
            backend.Advance(5);
            Assert.IsFalse(backend.CanBuildAt(hall, site, 1, out _), "the turned frame holds its site");
            // Built to half way, where the tests after this one have always
            // found the battle they share.
            int facing = -1;
            for (int t = 0; t < 60 * 90 && facing < 0; t += 60)
            {
                backend.Advance(60);
                n = backend.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Def == hall && units[i].Player == me && units[i].BuildProgress >= 0.5f) facing = units[i].Facing;
            }
            Assert.AreEqual(1, facing);
        }

        // A building stands facing south, toward the classic camera, and
        // reads 180 as the contract turns.
        [Test, Order(7)]
        public void ALodestoneStandsFacingSouth()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, king = -1;
            for (int i = 0; i < n && king < 0; i++)
                if (units[i].Player == me && backend.UnitDefs[units[i].Def].BuildOptions.Length > 0 && !backend.UnitDefs[units[i].Def].IsBuilding) king = i;
            Assert.GreaterOrEqual(king, 0);
            var u = units[king];
            int lode = Array.Find(backend.UnitDefs[u.Def].BuildOptions, o => backend.UnitDefs[o].IsBuilding && backend.UnitDefs[o].Name.ToUpperInvariant().Contains("LODE"));
            var features = new FeatureState[8192];
            int nf = backend.ReadFeatures(features);
            Vector3 site = default;
            float best = float.MaxValue;
            for (int i = 0; i < nf; i++)
            {
                if (backend.FeatureDefs[features[i].Def].Name.IndexOf("Mana", StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (features[i].Position - u.Position).sqrMagnitude;
                if (d < best && backend.CanBuildAt(lode, features[i].Position, 0, out var snapped)) { best = d; site = snapped; }
            }
            Assert.Less(best, float.MaxValue, "a lodestone site");
            Assert.IsTrue(backend.Command(new GameCommand { Kind = CommandKind.Build, Unit = u.Handle, Target = site, TargetUnit = -1, BuildDef = lode }));
            float heading = float.NaN;
            for (int t = 0; t < 60 * 90 && float.IsNaN(heading); t += 60)
            {
                backend.Advance(60);
                n = backend.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Def == lode && units[i].Player == me && units[i].BuildProgress >= 0.5f) heading = units[i].Heading;
            }
            Assert.AreEqual(0f, Mathf.DeltaAngle(heading, 180f), 0.5f, "a lodestone faces south");
        }

        // Ctrl+Z takes every finished unit of the player's of a type the
        // selection holds, Ctrl+A every one, and Ctrl, Shift and a number add
        // a group (the original's SelectAllUnitsSelectedType, SelectAllUnits
        // and RetrieveSquadAdd).
        // Last, since it adds two soldiers to the battle the others share.
        [Test, Order(23)]
        public void TheSelectKeysTakeEveryUnitOfATypeAndAddAGroup()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            int me = backend.LocalPlayer;
            var defs = backend.UnitDefs;
            var monarch = units.Take(n).First(x => x.Player == me && !defs[x.Def].IsBuilding);
            int def = Enumerable.Range(0, defs.Count).First(d => defs[d].Side == defs[monarch.Def].Side &&
                !defs[d].IsBuilding && !defs[d].CanFly && (defs[d].Category ?? "").Contains("MELEE"));
            int a = OkEngine.okx_place_unit(def, me), b = OkEngine.okx_place_unit(def, me);
            Assert.IsTrue(a >= 0 && b >= 0, "two " + defs[def].Name + " placed");
            n = backend.ReadUnits(units);
            var mine = units.Take(n).Where(x => x.Player == me && (x.Flags & UnitFlags.Active) != 0 && x.BuildProgress >= 1f).ToList();
            int same = mine.Count(x => x.Def == def);
            Assert.GreaterOrEqual(same, 2);
            backend.Cancel();
            backend.Select(new[] { a }, false);
            Assert.AreEqual(same, backend.SelectBy(SelectKind.SameType));
            var sel = new int[64];
            int got = backend.ReadSelection(sel);
            Assert.IsTrue(sel.Take(got).Contains(b), "the other one");
            Assert.IsFalse(sel.Take(got).Contains(monarch.Handle), "not the monarch");
            Assert.AreEqual(1, backend.SelectBy(SelectKind.Category, "monarch"));
            Assert.AreEqual(mine.Count, backend.SelectBy(SelectKind.All));
            backend.Select(new[] { a }, false);
            backend.AssignGroup(6);
            backend.Select(new[] { b }, false);
            Assert.AreEqual(2, backend.AddGroup(6));
            backend.Cancel();
        }

        [Test, Order(9)]
        public void TheWholeBattleIsCountedAndNoMatchIsOutOfStep()
        {
            var units = new UnitState[1024];
            int drawn = backend.ReadUnits(units);
            int all = OkEngine.okx_unit_count(0);
            Assert.GreaterOrEqual(all, drawn, "the count sees through the fog");
            Assert.Greater(OkEngine.okx_unit_count(backend.LocalPlayer), 0);
            Assert.AreEqual(0, OkEngine.okx_net_match(out var m));
            Assert.AreEqual(0, m.live, "a skirmish is no match");
            Assert.AreEqual(0, m.desynced);
        }

        [Test, Order(8)]
        public void TheSidebarsButtonsListCastAndToggle()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, caster = -1;
            UnitAction spell = null;
            for (int i = 0; i < n && caster < 0; i++)
            {
                if (units[i].Player != me) continue;
                backend.Select(new[] { units[i].Handle }, false);
                foreach (var a in backend.SelectionActions())
                    if (a.Kind == ActionKind.Spell && a.ManaCost > 0) { spell = a; caster = i; break; }
            }
            Assert.IsNotNull(spell, "a caster lists its spell");
            Assert.Greater(units[caster].MaxMana, 0, "a caster has a mana bar");
            var actions = backend.SelectionActions();
            foreach (var a in actions) AssertAPicture(a);
            var passive = Array.Find(actions, a => a.Id == "Passive");
            Assert.IsNotNull(passive);
            Assert.AreEqual(ActionKind.Stance, passive.Kind);
            Assert.IsTrue(backend.DoAction("Passive", Vector3.zero, -1, default, false));
            backend.Advance(3);
            Assert.IsTrue(Array.Find(backend.SelectionActions(), a => a.Id == "Passive").Toggled);
            Assert.IsTrue(backend.DoAction("Offensive", Vector3.zero, -1, default, false));
            backend.Advance(3);
            Assert.IsTrue(Array.Find(backend.SelectionActions(), a => a.Id == "Offensive").Toggled);
            Assert.AreEqual("A", Array.Find(actions, a => a.Id == "ATTACK").Hotkey);
            backend.Cancel();
            backend.Cancel();
        }

        [Test, Order(6)]
        public void ThePointerIsTheGamesPickWithItsOwnArt()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            int me = backend.LocalPlayer, mine = -1, theirs = -1;
            for (int i = 0; i < n; i++)
            {
                if ((units[i].Flags & UnitFlags.Active) == 0) continue;
                if (units[i].Player == me && mine < 0) mine = i;
                if (units[i].Player != me && theirs < 0) theirs = i;
            }
            Assert.IsTrue(mine >= 0 && theirs >= 0);
            var u = units[mine];
            var e = units[theirs];
            backend.Cancel();
            backend.Cancel();
            Assert.AreEqual(GameCursor.Select, backend.CursorAt(u.Position, u.Handle, out _));
            backend.Select(new[] { u.Handle }, false);
            Assert.AreEqual(GameCursor.Attack, backend.CursorAt(e.Position, e.Handle, out _));
            backend.Arm(CommandKind.Patrol);
            Assert.AreEqual(GameCursor.Patrol, backend.CursorAt(u.Position + new Vector3(20f, 0f, 0f), -1, out _));
            backend.Cancel();
            backend.Cancel();

            foreach (GameCursor c in System.Enum.GetValues(typeof(GameCursor)))
            {
                var art = backend.CursorArt(c);
                Assert.IsNotNull(art, c.ToString());
                foreach (var f in art)
                {
                    Assert.Greater(f.Image.Width, 0);
                    Assert.IsTrue(f.Hotspot.x >= 0 && f.Hotspot.x < f.Image.Width && f.Hotspot.y >= 0 && f.Hotspot.y < f.Image.Height, c.ToString());
                    Assert.Greater(f.Millis, 0);
                }
            }
            Assert.Greater(backend.CursorArt(GameCursor.Revive).Length, 1, "the revive pointer moves");
        }

        [Test, Order(4)]
        public void ASavedGameComesBack()
        {
            backend.Advance(60);
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            uint tick = backend.Tick;
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "okunity-test.sav");
            Assert.IsTrue(backend.SaveGame(path));
            Assert.IsTrue(backend.SaveInfo(path, out string map, out uint savedTick, out _));
            Assert.AreEqual("two castles", map);
            Assert.AreEqual(tick, savedTick);
            backend.Advance(120);
            Assert.IsTrue(backend.LoadGame(path));
            LoadProgress p = default;
            for (int i = 0; i < 5000 && !p.Done && !p.Failed; i++) p = backend.PumpLoading();
            Assert.IsTrue(p.Done, p.Error);
            Assert.AreEqual(tick, backend.Tick);
            var again = new UnitState[512];
            Assert.AreEqual(n, backend.ReadUnits(again));
            for (int i = 0; i < n; i++) Assert.AreEqual(units[i].Position, again[i].Position);
            System.IO.File.Delete(path);
        }

        // The destruction reads came after API 23 without a bump. An engine
        // built before them gives nothing and the battle goes on, and one
        // with them gives events in id order and weapons by slot.
        [Test, Order(3)]
        public void TheDestructionReadsWorkWithOrWithoutTheEnginesExports()
        {
            Assert.AreEqual(GameStatus.Running, backend.Status);
            var blasts = new BlastEvent[64];
            int n = backend.ReadBlasts(0, blasts);
            Assert.That(n, Is.InRange(0, blasts.Length));
            for (int i = 1; i < n; i++) Assert.Greater(blasts[i].Id, blasts[i - 1].Id);
            Assert.That(backend.ReadFeatureEvents(0, new FeatureEvent[64]), Is.InRange(0, 64));
            Assert.That(backend.ReadPieceEvents(0, new PieceEvent[64]), Is.InRange(0, 64));
            if (backend.ReadWind(out var wind)) Assert.That(wind.Heading, Is.InRange(0f, 360f));
            foreach (var d in backend.UnitDefs.Take(40))
                for (int slot = 0; slot < 3; slot++)
                {
                    var w = backend.Weapon(d.Id, slot);
                    if (w != null) Assert.AreEqual(slot, w.Slot);
                }
            foreach (var f in backend.FeatureDefs)
            {
                Assert.That(f.DeadDef, Is.InRange(-1, backend.FeatureDefs.Count - 1), f.Name);
                Assert.That(f.BurntDef, Is.InRange(-1, backend.FeatureDefs.Count - 1), f.Name);
            }
        }

        [Test, Order(3)]
        public void TheBattleReadsAndTakesOrders()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            Assert.GreaterOrEqual(n, 2);
            int me = backend.LocalPlayer;
            int pick = -1;
            for (int i = 0; i < n && pick < 0; i++) if (units[i].Player == me) pick = i;
            Assert.GreaterOrEqual(pick, 0);
            var u = units[pick];
            Assert.AreEqual(u.Position.y, backend.GroundHeight(u.Position.x, u.Position.z), 1f);
            Assert.LessOrEqual(u.Position.z, 0f);

            var model = backend.GetModel(u.Model);
            Assert.IsNotNull(model);
            Assert.AreEqual(model.Positions.Length, model.VertexPiece.Length);
            Assert.Greater(model.Scale, 0f);
            var poses = new PiecePose[128];
            Assert.AreEqual(model.Pieces.Length, backend.ReadUnitPose(u.Handle, poses));
            Vector3 root = poses[0].Matrix.GetColumn(3);
            Assert.AreEqual(u.Position.x, root.x, 4f);
            Assert.AreEqual(u.Position.z, root.z, 4f);

            var def = backend.UnitDefs[u.Def];
            Assert.Greater(def.BuildOptions.Length, 0, "the monarch has a build menu");
            Assert.Greater(backend.UnitDefs[def.BuildOptions[0]].ManaCost, 0);
            Assert.IsNotEmpty(backend.UnitDefs[def.BuildOptions[0]].Title);
            var pic = backend.UnitPicture(def.BuildOptions[0]);
            Assert.IsNotNull(pic, "the build button has the game's picture");
            Assert.AreEqual(pic.Width * pic.Height * 4, pic.Pixels.Length);
            var eco = backend.ReadEconomy(me);
            Assert.Greater(eco.Storage, 0f);

            var target = u.Position + new Vector3(30f, 0f, 0f);
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, u.Handle, target)));
            backend.Advance(2);
            var order = backend.ReadOrder(u.Handle);
            Assert.AreEqual(OrderKind.Move, order.Kind);
            Assert.AreEqual(target.x, order.Target.x, 2f);

            // A placement ghost: somewhere near the monarch can take its first building.
            int building = -1;
            foreach (int opt in def.BuildOptions) if (backend.UnitDefs[opt].IsBuilding) { building = opt; break; }
            Assert.GreaterOrEqual(building, 0);
            bool placed = false;
            for (int r = 6; r <= 40 && !placed; r += 2)
                for (int k = 0; k < 8 && !placed; k++)
                {
                    var at = u.Position + Quaternion.Euler(0, k * 45f, 0) * new Vector3(r, 0, 0);
                    if (backend.CanBuildAt(building, at, 0, out var snapped))
                    {
                        placed = true;
                        Assert.AreEqual(at.x, snapped.x, 2f);
                        Assert.AreEqual(backend.GroundHeight(snapped.x, snapped.z), snapped.y, 0.01f);
                    }
                }
            Assert.IsTrue(placed, "some ground near the monarch takes a building");
            Assert.GreaterOrEqual(backend.QueuedCount(u.Handle, -1), 0);

            int need = backend.ReadFog(null, out int fw, out int fh);
            Assert.AreEqual(backend.Terrain.HeightsW * backend.Terrain.HeightsH, need);
            var fog = new byte[need];
            backend.ReadFog(fog, out fw, out fh);
            int inSight = 0;
            foreach (var f in fog) if (f == 2) inSight++;
            Assert.Greater(inSight, 0);
            Assert.AreEqual(120, backend.Advance(120));
            StringAssert.StartsWith("walk", backend.UnitAnimation(u.Handle), "a marching unit is walking");
            n = backend.ReadUnits(units);
            float x = u.Position.x;
            for (int i = 0; i < n; i++) if (units[i].Handle == u.Handle) x = units[i].Position.x;
            Assert.Greater(x, u.Position.x + 3f);
            Assert.GreaterOrEqual(backend.ReadProjectiles(new ProjectileState[64]), 0);

            // The Unit Browser: the monarch's walk, played outside the battle.
            CollectionAssert.Contains(def.Animations, "walk");
            uint before = backend.Tick;
            var a = new PiecePose[128];
            var b = new PiecePose[128];
            int pieces = backend.PoseModel(u.Model, "walk", 1.0f, a);
            Assert.Greater(pieces, 1);
            Assert.AreEqual(pieces, backend.PoseModel(u.Model, "walk", 1.12f, b));
            int moved = 0;
            for (int i = 0; i < pieces; i++) if (a[i].Matrix != b[i].Matrix) moved++;
            Assert.GreaterOrEqual(moved, 2, "the legs swing");
            Assert.AreEqual(before, backend.Tick, "the battle stood still");
            var effects = new EffectState[256];
            int ne = backend.ReadEffects(effects);
            for (int i = 0; i < Mathf.Min(ne, effects.Length); i++)
            {
                Assert.Greater(effects[i].Top, effects[i].Bottom);
                Assert.Greater(effects[i].UvMax.x, effects[i].UvMin.x);
                Assert.IsNotNull(backend.EffectStrip(effects[i].Strip));
            }
            // The map editor: a height edit shows in Terrain and the ground at once.
            int cellsNeed = backend.ReadCells(null, out int cw, out int ch);
            Assert.AreEqual(cw * ch, cellsNeed);
            var hill = new byte[] { 220, 220, 220, 220 };
            int hx = cw / 3, hz = ch / 3;
            Assert.IsTrue(backend.EditCells(hx, hz, 2, 2, hill));
            Assert.AreEqual(220f / 16f, backend.Terrain.HeightAt(hx, hz), 0.001f);
            Assert.AreEqual(220f / 16f, backend.GroundHeight(hx + 0.5f, -(hz + 0.5f)), 0.5f);
            var features = new FeatureState[1024];
            Assert.Greater(backend.ReadFeatures(features), 0);
        }

        // The inner part of a button picture: the original's blank tile is
        // flat stone (about 9), every real picture has a figure on it (13 up).
        static float Contrast(RgbaImage img)
        {
            int mx = img.Width / 5, my = img.Height / 5, n = 0;
            double sum = 0, sq = 0;
            for (int y = my; y < img.Height - my; y++)
                for (int x = mx; x < img.Width - mx; x++)
                {
                    int i = (y * img.Width + x) * 4;
                    if (img.Pixels[i + 3] == 0) continue;
                    double l = 0.3 * img.Pixels[i] + 0.59 * img.Pixels[i + 1] + 0.11 * img.Pixels[i + 2];
                    sum += l;
                    sq += l * l;
                    n++;
                }
            if (n == 0) return 0f;
            double mean = sum / n;
            return (float)Math.Sqrt(Math.Max(0, sq / n - mean * mean));
        }

        void AssertAPicture(UnitAction a, string who = "")
        {
            Assert.AreNotEqual("Not in the engine yet", a.Why, $"{who} {a.Id} is listed though the engine cannot do it");
            var pic = backend.ActionPicture(a.Picture);
            Assert.IsNotNull(pic, $"{who} {a.Id} has the original's picture");
            Assert.Greater(Contrast(pic), 11.5f, $"{who} {a.Id} (picture {a.Picture}) is a real picture, not the blank tile");
        }

        // Every kingdom's monarch: each button it lists shows the original's
        // picture, and nothing the engine cannot carry out is listed. Last,
        // as it starts a game for each kingdom.
        [Test, Order(20)]
        public void EveryMonarchsButtonsShowTheirPictures()
        {
            foreach (var side in backend.Sides)
            {
                backend.StartSkirmish(TwoCastles(side.Id, side.Id == "TAROS" ? "ARAMON" : "TAROS"));
                LoadProgress p = default;
                for (int i = 0; i < 20000 && !p.Done && !p.Failed; i++) p = backend.PumpLoading();
                Assert.IsTrue(p.Done, side.Id + ": " + p.Error);
                backend.Advance(3);
                var units = new UnitState[512];
                int n = backend.ReadUnits(units), me = backend.LocalPlayer, king = -1;
                for (int i = 0; i < n && king < 0; i++)
                {
                    var d = backend.UnitDefs[units[i].Def];
                    if (units[i].Player == me && !d.IsBuilding && d.BuildOptions.Length > 0) king = i;
                }
                Assert.GreaterOrEqual(king, 0, side.Id + " has a monarch");
                backend.Select(new[] { units[king].Handle }, false);
                var actions = backend.SelectionActions();
                Assert.Greater(actions.Length, 3, side.Id + "'s monarch has orders");
                string who = side.Id + " " + backend.UnitDefs[units[king].Def].Name;
                foreach (var a in actions) AssertAPicture(a, who);
                backend.Cancel();
            }
        }

        // A formation move reaches the engine whole: the point in the
        // contract's space, and the heading held on arrival, turned as the
        // unit's own walking turns it.
        [Test, Order(10)]
        public void AFormationMoveWalksToItsPointAndHoldsItsHeading()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, pick = -1, enemy = -1;
            for (int i = 0; i < n; i++)
            {
                var d = backend.UnitDefs[units[i].Def];
                if (d.IsBuilding || (units[i].Flags & UnitFlags.Active) == 0) continue;
                if (units[i].Player == me) { if (pick < 0) pick = i; }
                else if (enemy < 0) enemy = units[i].Handle;
            }
            Assert.GreaterOrEqual(pick, 0);
            var u = units[pick];
            // East is 90 in the contract, and a walk east faces east.
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, u.Handle, u.Position + new Vector3(8f, 0f, 0f))));
            float walking = float.NaN;
            for (int t = 0; t < 60 && float.IsNaN(walking); t += 5)
            {
                backend.Advance(5);
                n = backend.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Handle == u.Handle && units[i].Position.x > u.Position.x + 0.5f) walking = units[i].Heading;
            }
            Assert.AreEqual(0f, Mathf.DeltaAngle(walking, 90f), 20f, "walking east it faces 90");
            backend.Command(GameCommand.To(CommandKind.Stop, u.Handle, Vector3.zero));
            backend.Advance(3);
            n = backend.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == u.Handle) u = units[i];
            var to = new Vector2(u.Position.x - 4f, u.Position.z + 2f);
            Assert.IsTrue(backend.MoveFormation(new[] { u.Handle }, new[] { to }, 90f, false, false));
            Vector3 at = u.Position;
            float heading = u.Heading;
            for (int t = 0; t < 60 * 60; t += 30)
            {
                backend.Advance(30);
                n = backend.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Handle == u.Handle) { at = units[i].Position; heading = units[i].Heading; }
                if (Vector2.Distance(new Vector2(at.x, at.z), to) < 0.25f && Mathf.Abs(Mathf.DeltaAngle(heading, 90f)) < 3f) break;
            }
            Assert.AreEqual(to.x, at.x, 0.5f);
            Assert.AreEqual(to.y, at.z, 0.5f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(heading, 90f), 3f, "it faces the heading given");
            Assert.AreEqual(0f, Mathf.DeltaAngle(heading, walking), 20f, "the way its walk east faced");
            if (enemy >= 0)
                Assert.IsFalse(backend.MoveFormation(new[] { enemy }, new[] { to }, null, false, false), "nobody took it");
        }

        // A spell chosen on the sidebar and cast at an enemy is used once:
        // the next plain click on the ground is a move, as in the original.
        [Test, Order(10)]
        public void ASpellCastOnceLeavesTheNextClickAMove()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, caster = -1, enemy = -1;
            UnitAction spell = null;
            for (int i = 0; i < n; i++)
            {
                if ((units[i].Flags & UnitFlags.Active) == 0) continue;
                if (units[i].Player != me) { if (enemy < 0 && !backend.UnitDefs[units[i].Def].IsBuilding) enemy = i; continue; }
                if (caster >= 0 || backend.UnitDefs[units[i].Def].IsBuilding) continue;
                backend.Select(new[] { units[i].Handle }, false);
                foreach (var a in backend.SelectionActions())
                    if (a.Kind == ActionKind.Spell && a.ManaCost > 0 && a.Enabled) { spell = a; caster = i; break; }
            }
            Assert.IsNotNull(spell, "a caster with a spell it can cast");
            Assert.GreaterOrEqual(enemy, 0);
            var u = units[caster];
            backend.Select(new[] { u.Handle }, false);
            // The sidebar's press chooses it, the click on the enemy casts it.
            Assert.IsTrue(backend.DoAction(spell.Id, Vector3.zero, -1, default, false));
            backend.Advance(2);
            Assert.IsTrue(backend.DoAction(spell.Id, units[enemy].Position, units[enemy].Handle, default, false));
            backend.Advance(3);
            Assert.AreEqual(OrderKind.Attack, backend.ReadOrder(u.Handle).Kind, "it goes for the enemy");
            Assert.AreEqual(OkEngine.ArmNone, OkEngine.okx_armed(out _), "nothing stays armed");
            var ground = u.Position + new Vector3(-3f, 0f, 3f);
            ground.y = backend.GroundHeight(ground.x, ground.z);
            backend.Click(ground, -1, false);
            backend.Advance(3);
            Assert.AreEqual(OrderKind.Move, backend.ReadOrder(u.Handle).Kind, "a plain click afterwards moves");
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, u.Handle, ground + new Vector3(1f, 0f, 0f))));
            backend.Advance(3);
            Assert.AreEqual(OrderKind.Move, backend.ReadOrder(u.Handle).Kind, "so does a move order");
            backend.Command(GameCommand.To(CommandKind.Stop, u.Handle, Vector3.zero));
            backend.Cancel();
            backend.Cancel();
        }

        // An attack dragged over an area arms the engine only for that
        // drag: the next plain click on the ground is a move.
        [Test, Order(10)]
        public void AnAttackDragLeavesTheNextClickAMove()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, pick = -1;
            for (int i = 0; i < n && pick < 0; i++)
                if (units[i].Player == me && (units[i].Flags & UnitFlags.Active) != 0 && !backend.UnitDefs[units[i].Def].IsBuilding) pick = i;
            Assert.GreaterOrEqual(pick, 0);
            var u = units[pick];
            backend.Select(new[] { u.Handle }, false);
            var c = u.Position + new Vector3(6f, 0f, 6f);
            backend.DoAction("ATTACK", c, -1, Rect.MinMaxRect(c.x - 2f, c.z - 2f, c.x + 2f, c.z + 2f), false);
            backend.Advance(3);
            Assert.AreEqual(OkEngine.ArmNone, OkEngine.okx_armed(out _), "nothing stays armed");
            backend.Select(new[] { u.Handle }, false);
            var ground = u.Position + new Vector3(-3f, 0f, 3f);
            ground.y = backend.GroundHeight(ground.x, ground.z);
            backend.Click(ground, -1, false);
            backend.Advance(3);
            Assert.AreEqual(OrderKind.Move, backend.ReadOrder(u.Handle).Kind, "a plain click afterwards moves");
            backend.Command(GameCommand.To(CommandKind.Stop, u.Handle, Vector3.zero));
            backend.Cancel();
            backend.Cancel();
        }

        // Ctrl on an order, by command and by the game's own click: the new
        // order goes in place of the one in hand and the queue stays.
        [Test, Order(10)]
        public void CtrlReplacesTheOrderInHandAndKeepsTheQueue()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units), me = backend.LocalPlayer, pick = -1;
            for (int i = 0; i < n && pick < 0; i++)
                if (units[i].Player == me && (units[i].Flags & UnitFlags.Active) != 0 && !backend.UnitDefs[units[i].Def].IsBuilding) pick = i;
            Assert.GreaterOrEqual(pick, 0);
            var u = units[pick];
            Vector3 At(float x, float z)
            {
                var p = u.Position + new Vector3(x, 0f, z);
                p.y = backend.GroundHeight(p.x, p.z);
                return p;
            }
            var legs = new OrderLeg[16];
            backend.Cancel();
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, u.Handle, At(8f, 0f))));
            var b = GameCommand.To(CommandKind.Move, u.Handle, At(8f, 8f));
            b.Queue = true;
            Assert.IsTrue(backend.Command(b));
            var c = GameCommand.To(CommandKind.Move, u.Handle, At(0f, 8f));
            c.Queue = true;
            Assert.IsTrue(backend.Command(c));
            backend.Advance(2);
            Assert.AreEqual(3, backend.ReadOrderQueue(u.Handle, legs), "two moves queued behind the first");

            var d = GameCommand.To(CommandKind.Move, u.Handle, At(-8f, 0f));
            d.Keep = true;
            Assert.IsTrue(backend.Command(d));
            backend.Advance(2);
            Assert.AreEqual(3, backend.ReadOrderQueue(u.Handle, legs), "Keep leaves the queue");
            Assert.AreEqual(d.Target.x, legs[0].Target.x, 1.5f, "the new move is the one in hand");
            Assert.AreEqual(b.Target.z, legs[1].Target.z, 1.5f, "and the queued ones follow");
            Assert.AreEqual(c.Target.x, legs[2].Target.x, 1.5f);

            backend.Select(new[] { u.Handle }, false);
            var e = At(-8f, -8f);
            backend.Click(e, -1, false, true);
            backend.Advance(2);
            Assert.AreEqual(3, backend.ReadOrderQueue(u.Handle, legs), "a Ctrl-click leaves the queue");
            Assert.AreEqual(e.z, legs[0].Target.z, 1.5f, "and its move is the one in hand");

            backend.Click(e, -1, false);
            backend.Advance(2);
            Assert.AreEqual(1, backend.ReadOrderQueue(u.Handle, legs), "a plain click replaces them all");
            backend.Command(GameCommand.To(CommandKind.Stop, u.Handle, Vector3.zero));
            backend.Cancel();
        }

        // A catapult's rock thrown at the ground comes back from the engine as
        // a blast with its weapon, where it fell and coming down.
        [Test, Order(10)]
        public void ACatapultsRockComesBackAsABlastWithItsWeapon()
        {
            Assert.AreEqual(GameStatus.Running, backend.Status);
            int def = backend.UnitDefs.First(d => string.Equals(d.Name, "ARAPULT", StringComparison.OrdinalIgnoreCase)).Id;
            var w = backend.Weapon(def, 0);
            Assert.IsNotNull(w, "the engine describes the weapon");
            Assert.AreEqual("ballistic", w.Type);
            Assert.Greater(w.AreaOfEffect, 0f);
            Assert.IsNotEmpty(w.ExplosionClass);
            int h = OkEngine.okx_place_unit(def, backend.LocalPlayer);
            Assert.GreaterOrEqual(h, 0);
            var us = new UnitState[1024];
            var u = us.Take(backend.ReadUnits(us)).First(x => x.Handle == h);
            var buf = new BlastEvent[256];
            int since = 0;
            for (int k; (k = backend.ReadBlasts(since, buf)) > 0;) since = buf[k - 1].Id;
            // Ten cells toward the middle of the map.
            var centre = new Vector3(backend.Terrain.Size.x * 0.5f, 0f, -backend.Terrain.Size.y * 0.5f);
            var way = centre - u.Position;
            way.y = 0f;
            var aim = u.Position + way.normalized * 10f;
            Assert.IsTrue(backend.Command(new GameCommand { Kind = CommandKind.AttackGround, Unit = h, Target = aim, TargetUnit = -1, BuildDef = -1 }));
            BlastEvent? rock = null;
            for (int t = 0; t < 900 && rock == null; t += 2)
            {
                backend.Advance(2);
                int k = backend.ReadBlasts(since, buf);
                for (int i = 0; i < k; i++)
                {
                    if (buf[i].Def == def && buf[i].Shooter == h) rock = buf[i];
                    since = buf[i].Id;
                }
            }
            Assert.IsNotNull(rock, "the rock burst");
            var b = rock.Value;
            Debug.Log($"rock: {b.Weapon?.Name} at {b.Position}, way {b.Direction}, radius {b.Radius}, flags {b.Flags}");
            Assert.AreEqual(BlastCause.Weapon, b.Cause);
            Assert.AreEqual(0, b.Slot);
            Assert.AreSame(w, b.Weapon);
            Assert.AreEqual(w.AreaOfEffect * 0.5f, b.Radius, 1e-3f);
            Assert.AreEqual(w.Damage, b.Damage);
            Assert.AreEqual(backend.LocalPlayer, b.Player);
            Assert.Less(Vector2.Distance(new Vector2(b.Position.x, b.Position.z), new Vector2(aim.x, aim.z)), 3f);
            Assert.AreEqual(b.Position.y, backend.GroundHeight(b.Position.x, b.Position.z), 1.5f);
            Assert.Greater(Vector3.Dot(b.Direction, way.normalized), 0.05f, "thrown toward the aim");
            Assert.Less(b.Direction.y, 0f, "coming down");
            Assert.AreEqual(-1, b.Unit);
        }

        // The engine's wind, a fire starter, the stages scenery breaks into,
        // and an archer that our own rock kills throwing its pieces.
        [Test, Order(10)]
        public void TheEngineTellsItsWindStagesAndThrownPieces()
        {
            Assert.AreEqual(GameStatus.Running, backend.Status);
            Assert.IsTrue(backend.ReadWind(out var wind), "the simulation's wind");
            Debug.Log($"wind: heading {wind.Heading:0}, {wind.Speed:0} of {wind.MaxSpeed:0}");
            Assert.That(wind.Heading, Is.InRange(0f, 360f));
            Assert.Greater(wind.MaxSpeed, 0f);
            Assert.That(wind.Strength, Is.InRange(0f, 1f));
            int Unit(string name) => backend.UnitDefs.First(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).Id;
            Assert.AreEqual(WeaponFlags.FireStarter, backend.Weapon(Unit("TARNECRO"), 0).Flags & WeaponFlags.FireStarter);
            Assert.AreEqual(WeaponFlags.None, backend.Weapon(Unit("ARAPULT"), 0).Flags & WeaponFlags.FireStarter);
            int dies = backend.FeatureDefs.Count(f => f.Breakable && f.DeadDef >= 0);
            int burns = backend.FeatureDefs.Count(f => f.Flammable && f.BurntDef >= 0);
            int stands = backend.FeatureDefs.Count(f => f.Indestructible);
            Debug.Log($"feature defs: {dies} die into a stage, {burns} burn into one, {stands} stand");
            Assert.Greater(dies, 50);
            Assert.Greater(burns, 50);
            Assert.Greater(stands, 50);

            int pult = OkEngine.okx_place_unit(Unit("ARAPULT"), backend.LocalPlayer);
            Assert.GreaterOrEqual(pult, 0);
            var us = new UnitState[1024];
            var buf = new PieceEvent[256];
            int since = 0, arch = -1;
            PieceEvent? thrown = null;
            Vector3 fell = default;
            // How an archer falls depends on how it was struck, so another
            // stands in for one that fell without throwing a piece.
            for (int life = 0; life < 5 && thrown == null; life++)
            {
                arch = OkEngine.okx_place_unit(Unit("ARAARCH"), backend.LocalPlayer);
                Assert.GreaterOrEqual(arch, 0);
                var p = us.Take(backend.ReadUnits(us)).First(x => x.Handle == pult);
                // Out of the catapult's way and its least reach, standing still, then our own rock on it.
                Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, arch, p.Position + Vector3.right * 20f)));
                backend.Advance(60 * 8);
                for (int t = 0; t < 60 * 20 && backend.ReadOrder(arch).Kind != OrderKind.None; t += 30) backend.Advance(30);
                for (int k; (k = backend.ReadPieceEvents(since, buf)) > 0;) since = buf[k - 1].Id;
                for (int shot = 0; shot < 6 && thrown == null; shot++)
                {
                    var a = us.Take(backend.ReadUnits(us)).FirstOrDefault(x => x.Handle == arch);
                    if (a.Handle != arch || (a.Flags & UnitFlags.Active) == 0) break;
                    fell = a.Position;
                    Assert.IsTrue(backend.Command(new GameCommand { Kind = CommandKind.AttackGround, Unit = pult, Target = a.Position, TargetUnit = -1, BuildDef = -1 }));
                    for (int t = 0; t < 600 && thrown == null; t += 2)
                    {
                        backend.Advance(2);
                        int k = backend.ReadPieceEvents(since, buf);
                        for (int i = 0; i < k; i++) { if (buf[i].Unit == arch && thrown == null) thrown = buf[i]; since = buf[i].Id; }
                    }
                }
                // Let a fallen archer's corpse settle before the next one is set down.
                if (thrown == null) backend.Advance(60 * 3);
            }
            Assert.IsNotNull(thrown, "an archer threw a piece");
            var e = thrown.Value;
            Vector3 at = e.Pose.GetColumn(3);
            Debug.Log($"piece: node {e.Piece} {e.How} at {at}, the archer at {fell}");
            Assert.AreEqual(Unit("ARAARCH"), e.Def);
            Assert.AreEqual(backend.LocalPlayer, e.Player);
            Assert.AreNotEqual(PieceExplode.None, e.How);
            Assert.Less(e.Piece, backend.GetModel(e.Model).Pieces.Length);
            Assert.Less(Vector2.Distance(new Vector2(at.x, at.z), new Vector2(fell.x, fell.z)), 4f, "where the archer fell");
        }

        // A catapult's rock on the nearest scenery one rock destroys: the hit
        // and the stage that takes its cell come back naming the rock's blast.
        [Test, Order(10)]
        public void ARockOnSceneryComesBackHitThenDead()
        {
            Assert.AreEqual(GameStatus.Running, backend.Status);
            int def = backend.UnitDefs.First(d => string.Equals(d.Name, "ARAPULT", StringComparison.OrdinalIgnoreCase)).Id;
            var w = backend.Weapon(def, 0);
            int h = OkEngine.okx_place_unit(def, backend.LocalPlayer);
            Assert.GreaterOrEqual(h, 0);
            var us = new UnitState[1024];
            var p = us.Take(backend.ReadUnits(us)).First(x => x.Handle == h);
            var fs = new FeatureState[16384];
            int nf = Mathf.Min(backend.ReadFeatures(fs), fs.Length);
            FeatureState target = default;
            float best = float.MaxValue;
            for (int i = 0; i < nf; i++)
            {
                var d = backend.FeatureDefs[fs[i].Def];
                float dist = Vector3.Distance(fs[i].Position, p.Position);
                if (d.Breakable && d.DeadDef >= 0 && d.HitPoints <= w.Damage && dist > 10f && dist < best) { best = dist; target = fs[i]; }
            }
            Assert.Less(best, float.MaxValue, "breakable scenery near the start");
            var buf = new FeatureEvent[256];
            int since = 0;
            for (int k; (k = backend.ReadFeatureEvents(since, buf)) > 0;) since = buf[k - 1].Id;
            Assert.IsTrue(backend.Command(new GameCommand { Kind = CommandKind.AttackGround, Unit = h, Target = target.Position, TargetUnit = -1, BuildDef = -1 }));
            FeatureEvent? hit = null, dead = null;
            int idx = target.Index;
            var heard = new System.Text.StringBuilder();
            // The catapult may walk into reach first.
            for (int t = 0; t < 60 * 120 && dead == null; t += 2)
            {
                backend.Advance(2);
                int k = backend.ReadFeatureEvents(since, buf);
                for (int i = 0; i < k; i++)
                {
                    var e = buf[i];
                    since = e.Id;
                    if (e.Feature == idx)
                    {
                        heard.Append($"{e.Kind}@{e.Tick} ");
                        if (e.Kind == FeatureEventKind.Hit && hit == null) hit = e;
                        if (e.Kind == FeatureEventKind.Dead) dead = e;
                        continue;
                    }
                    // A feature that goes below it moves its index down.
                    bool gone = e.Kind == FeatureEventKind.Removed || e.Kind == FeatureEventKind.Swept ||
                                (e.Kind == FeatureEventKind.Dead || e.Kind == FeatureEventKind.Burnt) && e.NewDef < 0;
                    if (gone && e.Feature < idx) idx--;
                }
            }
            Debug.Log($"scenery {backend.FeatureDefs[target.Def].Name} heard: {heard}");
            Assert.IsNotNull(hit, "the rock hit it");
            Assert.IsNotNull(dead, "it died into its stage");
            Debug.Log($"scenery: {backend.FeatureDefs[target.Def].Name} {best:0.0} cells away, hit for {hit.Value.Damage}, " +
                $"dead into {backend.FeatureDefs[dead.Value.NewDef].Name} after {dead.Value.Tick - hit.Value.Tick} ticks");
            Assert.AreEqual(w.Damage, hit.Value.Damage);
            Assert.AreEqual(0, hit.Value.Health);
            Assert.Greater(hit.Value.Blast, 0);
            Assert.AreEqual(hit.Value.Blast, dead.Value.Blast, "the stage names the same blast");
            Assert.AreEqual(backend.FeatureDefs[target.Def].DeadDef, dead.Value.NewDef);
            Assert.Less(Vector2.Distance(new Vector2(hit.Value.From.x, hit.Value.From.z), new Vector2(target.Position.x, target.Position.z)), 3f);
        }

        // A ship the engine builds is drawn with its origin just under the
        // surface, and posed there too: the engine reports a floater at the
        // sea but poses it on the floor.
        [Test, Order(11)]
        public void AShipIsDrawnInTheSurfaceAndPosedWhereItIsDrawn()
        {
            var s = new SkirmishSetup { MapId = "per mare per terras", Seed = 7, LineOfSight = false, MapRevealed = true, StartMana = 20000 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "VERUNA", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 4, Team = 2 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 5, Team = 3 });
            backend.StartSkirmish(s);
            LoadProgress p = default;
            for (int pumps = 0; pumps < 5000 && !p.Done && !p.Failed; pumps++) p = backend.PumpLoading();
            Assert.IsTrue(p.Done, p.Error);
            int Find(string name) { for (int i = 0; i < backend.UnitDefs.Count; i++) if (string.Equals(backend.UnitDefs[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
            int yard = Find("verasy"), ship = Find("verscout");
            Assert.GreaterOrEqual(yard, 0);
            Assert.GreaterOrEqual(ship, 0);
            Assert.AreEqual(FloatKind.Ship, backend.UnitDefs[ship].Float);
            // The skiff's hull reaches 62 px from its centre, the harbour has none.
            Assert.AreEqual(8, backend.UnitDefs[ship].HullCells);
            Assert.AreEqual(0, backend.UnitDefs[yard].HullCells);
            var units = new UnitState[1024];
            int n = backend.ReadUnits(units), builder = -1;
            Vector3 from = Vector3.zero;
            for (int i = 0; i < n; i++)
                if (units[i].Player == backend.LocalPlayer && Array.IndexOf(backend.UnitDefs[units[i].Def].BuildOptions, yard) >= 0) { builder = units[i].Handle; from = units[i].Position; }
            Assert.GreaterOrEqual(builder, 0, "someone builds the harbour");
            Vector3 site = from;
            bool placed = false;
            for (float r = 4; r <= 70 && !placed; r += 2)
                for (int k = 0; k < 24 && !placed; k++)
                {
                    float a = k * Mathf.PI * 2 / 24;
                    placed = backend.CanBuildAt(yard, from + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r, 0, out site);
                }
            Assert.IsTrue(placed, "a harbour site on the water");
            Assert.IsTrue(backend.Command(new GameCommand { Kind = CommandKind.Build, Unit = builder, Target = site, TargetUnit = -1, BuildDef = yard }));
            int harbour = -1, boat = -1;
            for (int step = 0; step < 1200 && boat < 0; step++)
            {
                backend.Advance(20);
                n = backend.ReadUnits(units);
                for (int i = 0; i < n; i++)
                {
                    if (units[i].Player != backend.LocalPlayer || (units[i].Flags & UnitFlags.Building) != 0) continue;
                    if (units[i].Def == yard && harbour < 0)
                    {
                        harbour = units[i].Handle;
                        backend.Command(new GameCommand { Kind = CommandKind.FactoryEnqueue, Unit = harbour, TargetUnit = -1, BuildDef = ship });
                    }
                    if (units[i].Def == ship) boat = i;
                }
            }
            Assert.GreaterOrEqual(boat, 0, "the harbour built a ship");
            var u = units[boat];
            float sea = backend.Terrain.SeaLevel, ground = backend.GroundHeight(u.Position.x, u.Position.z);
            Assert.Less(ground, sea - 1f, "the ship is over deep water");
            Assert.AreEqual(sea - Afloat.Draft, u.Position.y, 0.01f, "its origin is drawn just under the surface");
            var pose = new PiecePose[128];
            Assert.Greater(backend.ReadUnitPose(u.Handle, pose), 0);
            Assert.AreEqual(u.Position.y, pose[0].Matrix.m13, 0.3f, "and its pose stands there, not on the floor");
        }

        // A frame of a building only the monarch raises: with him selected
        // the hammer shows over it and the game's click sets him to work
        // there, or after his walk with Shift. A Mage Builder is held to its
        // own build list, so it gets no hammer and the click does not put it
        // to work (legacy:233556-233574).
        [Test, Order(12)]
        public void TheHammerOverAFrameIsForABuilderThatCouldBuildIt()
        {
            backend.StartSkirmish(TwoCastles());
            LoadProgress p = default;
            for (int pumps = 0; pumps < 5000 && !p.Done && !p.Failed; pumps++) p = backend.PumpLoading();
            Assert.IsTrue(p.Done, p.Error);
            int Find(string name) { for (int i = 0; i < backend.UnitDefs.Count; i++) if (string.Equals(backend.UnitDefs[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
            int kingDef = Find("araking"), mageDef = Find("arabuild");
            Assert.IsTrue(kingDef >= 0 && mageDef >= 0);
            int me = backend.LocalPlayer;
            var units = new UnitState[1024];
            int n = backend.ReadUnits(units);
            var king = units.Take(n).First(u => u.Player == me && u.Def == kingDef);
            int mage = OkEngine.okx_place_unit(mageDef, me);
            Assert.GreaterOrEqual(mage, 0, "a Mage Builder set down");
            backend.Advance(2);

            var mageList = backend.UnitDefs[mageDef].BuildOptions;
            int only = -1;
            Vector3 site = default;
            foreach (int opt in backend.UnitDefs[kingDef].BuildOptions)
            {
                if (!backend.UnitDefs[opt].IsBuilding || Array.IndexOf(mageList, opt) >= 0) continue;
                bool found = false;
                for (int r = 8; r <= 60 && !found; r += 2)
                    for (int k = 0; k < 16 && !found; k++)
                        found = backend.CanBuildAt(opt, king.Position + Quaternion.Euler(0, k * 22.5f, 0) * new Vector3(r, 0, 0), 0, out site);
                if (found) { only = opt; break; }
            }
            Assert.GreaterOrEqual(only, 0, "a building only the monarch raises, and ground for it");
            Assert.IsTrue(backend.Command(new GameCommand { Kind = CommandKind.Build, Unit = king.Handle, Target = site, TargetUnit = -1, BuildDef = only }));
            int frame = -1;
            for (int t = 0; t < 60 * 60 && frame < 0; t += 10)
            {
                backend.Advance(10);
                frame = backend.ReadOrder(king.Handle).Building;
            }
            Assert.GreaterOrEqual(frame, 0, "the monarch began the frame");
            backend.Advance(60);
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Stop, king.Handle, Vector3.zero)));
            backend.Advance(3);
            Assert.AreNotEqual(frame, backend.ReadOrder(king.Handle).Building, "stopped, the monarch leaves the frame standing");
            n = backend.ReadUnits(units);
            var at = units.Take(n).First(u => u.Handle == frame).Position;

            backend.Cancel();
            backend.Cancel();
            Assert.AreNotEqual(GameCursor.Repair, backend.CursorAt(at, frame, out _), "no hammer with nothing selected");
            backend.Select(new[] { mage }, false);
            Assert.IsFalse(backend.CanHelpBuild(mage, frame), "the Mage Builder could not build it");
            Assert.AreNotEqual(GameCursor.Repair, backend.CursorAt(at, frame, out _), "so it gets no hammer");
            backend.Click(at, frame, false);
            backend.Advance(2);
            Assert.AreNotEqual(frame, backend.ReadOrder(mage).Building, "and the click does not set it to work there");

            backend.Select(new[] { king.Handle }, false);
            Assert.IsTrue(backend.CanHelpBuild(king.Handle, frame));
            Assert.AreEqual(GameCursor.Repair, backend.CursorAt(at, frame, out _), "the hammer, for the monarch");
            backend.Click(at, frame, false);
            backend.Advance(2);
            var order = backend.ReadOrder(king.Handle);
            Assert.AreEqual(OrderKind.Build, order.Kind);
            Assert.AreEqual(frame, order.Building, "the click set him back to work on it");

            // Shift puts the help behind the order in hand, a walk away from it.
            n = backend.ReadUnits(units);
            var kp = units.Take(n).First(u => u.Handle == king.Handle).Position;
            var away = kp + (kp - at).normalized * 12f;
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, king.Handle, away)), "a walk away");
            backend.Advance(2);
            backend.Click(at, frame, true);
            backend.Advance(2);
            var legs = new OrderLeg[8];
            Assert.AreEqual(2, backend.ReadOrderQueue(king.Handle, legs), "Shift queues the help behind the walk");
            Assert.AreEqual(OrderKind.Move, legs[0].Kind);
            Assert.AreEqual(frame, legs[1].TargetUnit, "and then the frame");
            backend.Cancel();
            backend.Cancel();
        }
    }

    // How Ctrl reaches the engine, which needs no engine to check.
    public class EngineOrderBitsTests
    {
        [Test]
        public void CtrlIsTheKeepBitOnAnOrderAndTheSecondBitOnAClick()
        {
            var c = GameCommand.To(CommandKind.Move, 1, Vector3.zero);
            Assert.AreEqual(0, EngineBackend.CommandArg(c));
            c.Keep = true;
            Assert.AreEqual(0x4000, EngineBackend.CommandArg(c));
            c.Queue = true;
            Assert.AreEqual(0xC000, EngineBackend.CommandArg(c));
            var build = new GameCommand { Kind = CommandKind.Build, Facing = 3, Keep = true };
            Assert.AreEqual(0x4003, EngineBackend.CommandArg(build));
            Assert.AreEqual(0, EngineBackend.ClickFlags(false, false));
            Assert.AreEqual(1, EngineBackend.ClickFlags(true, false));
            Assert.AreEqual(2, EngineBackend.ClickFlags(false, true));
            Assert.AreEqual(3, EngineBackend.ClickFlags(true, true));
        }

        // A summons without end is the 0x2000 bit, on a build only.
        [Test]
        public void ASummonsIsTheEndlessBitOnABuild()
        {
            var build = new GameCommand { Kind = CommandKind.Build, Facing = 1, Endless = true };
            Assert.AreEqual(0x2001, EngineBackend.CommandArg(build));
            build.Queue = true;
            Assert.AreEqual(0xA001, EngineBackend.CommandArg(build));
            var move = GameCommand.To(CommandKind.Move, 1, Vector3.zero);
            move.Endless = true;
            Assert.AreEqual(0, EngineBackend.CommandArg(move), "nothing but a build carries it");
        }
    }
}
