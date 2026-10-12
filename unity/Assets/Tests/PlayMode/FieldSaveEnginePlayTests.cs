// FieldSaveEnginePlayTests.cs - on the real engine with the player's files,
// ignored without them. Cannon dig and char Edmont's Field and a Mind
// Mage's dark magic withers a wood, the pause menu's Save game keeps it, and
// the skirmish page's Load brings back the same texels and dips, the
// unit drawn lowered in its crater and the tree still withered, with no
// frame of a clean field in between. OKU_SAVED_SCARS_DIR saves a picture of
// the field before the save and after the load, and OKU_FIELD_LONG measures
// the field a long battle keeps.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class FieldSaveEnginePlayTests
    {
        const string FieldMap = "edmont's field", LongMap = "ulasem arena";
        GameRoot root;
        string saves;
        IGameBackend B => root.Backend;

        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);

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

        // ---- A battle on the engine ----

        IEnumerator Battle(string map, System.Func<MapInfo, SeatSetup[]> seats)
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            root = GameRoot.Boot();
            yield return null;
            var info = B.Maps.FirstOrDefault(m => m.Id.ToLowerInvariant() == map || (m.Name ?? "").ToLowerInvariant() == map);
            if (info == null) Assert.Ignore($"the game files have no {map}");
            root.Options.EffectsQuality = EffectsQuality.High;
            root.Options.Weather = WeatherChoice.Off;
            root.Options.GameSpeed = 1;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            var s = root.Setup;
            s.MapId = info.Id;
            s.MapRevealed = true;
            s.LineOfSight = false;
            s.StartMana = 60000;
            s.UnitLimit = 1000;
            s.MonarchExpendable = true;
            s.RandomStarts = false;
            s.Seats.Clear();
            s.Seats.AddRange(seats(info));
            yield return null;
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 300f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            // The test moves the battle on itself.
            root.Options.GameSpeed = 0;
        }

        static SeatSetup Seat(SeatKind kind, string side, int colour, int team, int start, AiDifficulty d = AiDifficulty.Easy) =>
            new SeatSetup { Kind = kind, Side = side, Colour = colour, Team = team, Start = start, Difficulty = d };

        // The start farthest from another.
        static int Farthest(MapInfo m, int from)
        {
            int best = from;
            for (int i = 0; i < m.Starts.Length; i++)
                if ((m.Starts[i] - m.Starts[from]).sqrMagnitude > (m.Starts[best] - m.Starts[from]).sqrMagnitude) best = i;
            return best;
        }

        IEnumerator Run(int ticks, int perFrame = 30)
        {
            for (int done = 0; done < ticks && GameFlow.InGame(root.Flow.State);)
            {
                int ran = B.Advance(Mathf.Min(perFrame, ticks - done));
                if (ran <= 0) yield break;
                done += ran;
                yield return null;
            }
        }

        int Def(string name)
        {
            foreach (var d in B.UnitDefs)
                if (string.Equals(d.Name, name, System.StringComparison.OrdinalIgnoreCase)) return d.Id;
            return -1;
        }

        List<int> Place(int player, string unit, int count)
        {
            var list = new List<int>();
            int def = Def(unit);
            Assert.GreaterOrEqual(def, 0, "the game has " + unit);
            for (int i = 0; i < count; i++)
            {
                int h = OkEngine.okx_place_unit(def, player);
                if (h < 0) break;
                list.Add(h);
            }
            return list;
        }

        readonly UnitState[] unitBuf = new UnitState[8192];

        List<UnitState> Units(System.Func<UnitState, bool> keep)
        {
            int n = B.ReadUnits(unitBuf);
            var list = new List<UnitState>();
            for (int i = 0; i < n; i++)
                if ((unitBuf[i].Flags & UnitFlags.Dying) == 0 && keep(unitBuf[i])) list.Add(unitBuf[i]);
            return list;
        }

        Vector3 Ground(float x, float z) => new Vector3(x, B.GroundHeight(x, z), z);

        Vector3 StartOf(int seat)
        {
            var m = root.CurrentMap();
            return Ground(m.Starts[seat].x, -m.Starts[seat].y);
        }

        // The flattest dry ground within reach of a point.
        Vector3 Flat(Vector3 near, float reach, float across)
        {
            var t = B.Terrain;
            var best = near;
            float bestSlope = float.MaxValue;
            for (float r = 0f; r <= reach; r += 2f)
                for (int a = 0; a < 16; a++)
                {
                    var p = near + new Vector3(Mathf.Cos(a * Mathf.PI / 8f) * r, 0f, Mathf.Sin(a * Mathf.PI / 8f) * r);
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int k = 0; k < 9; k++)
                    {
                        float g = B.GroundHeight(p.x + (k % 3 - 1) * across * 0.5f, p.z + (k / 3 - 1) * across * 0.5f);
                        lo = Mathf.Min(lo, g);
                        hi = Mathf.Max(hi, g);
                    }
                    if (t != null && t.SeaLevel > 0 && lo < t.SeaLevel + 0.5f) continue;
                    if (hi - lo < bestSlope) { bestSlope = hi - lo; best = p; }
                }
            return Ground(best.x, best.z);
        }

        // A spell cast as the HUD casts it, chosen and then aimed at a point.
        bool Cast(int caster, int slot, Vector3 at)
        {
            B.Select(new[] { caster }, false);
            var a = B.SelectionActions().FirstOrDefault(x => x.Kind == ActionKind.Spell && x.Arg == slot);
            bool ok = a != null && B.DoAction(a.Id, at, -1, default, false);
            if (!ok)
            {
                B.Command(new GameCommand { Kind = CommandKind.SetWeapon, Unit = caster, TargetUnit = -1, BuildDef = -1, Arg = slot });
                ok = B.Command(GameCommand.To(CommandKind.AttackGround, caster, at));
            }
            B.Select(new int[0], false);
            return ok;
        }

        // What a spell costs its caster, as the HUD asks.
        int SpellCost(int caster, int slot)
        {
            B.Select(new[] { caster }, false);
            var a = B.SelectionActions().FirstOrDefault(x => x.Kind == ActionKind.Spell && x.Arg == slot);
            B.Select(new int[0], false);
            return a != null ? a.ManaCost : 0;
        }

        static bool IsTree(FeatureDef d) => d != null && d.Breakable && Fracture.KindOf(d) == BreakKind.Tree;

        readonly FeatureState[] featBuf = new FeatureState[EntityRenderer.MaxFeatures];

        // The feature standing at a place, as the engine lists it now.
        bool FeatureAt(Vector3 at, out FeatureState f)
        {
            int n = B.ReadFeatures(featBuf);
            for (int i = 0; i < n; i++)
                if (featBuf[i].Position.x == at.x && featBuf[i].Position.z == at.z) { f = featBuf[i]; return true; }
            f = default;
            return false;
        }

        // ---- Pictures ----

        void Pose(Vector3 focus, float distance, float pitch, float yaw)
        {
            var gc = root.World.Camera;
            gc.maxDistance = Mathf.Max(gc.maxDistance, distance);
            gc.focus = new Vector3(focus.x, gc.SmoothGround(focus), focus.z);
            gc.pitch = pitch;
            gc.yaw = yaw;
            gc.Zoom(distance);
        }

        // The field through the battle's camera, offscreen, into dir.
        void Grab(string dir, string name)
        {
            const int W = 1920, H = 1080;
            var cam = root.World.Camera.GetComponent<Camera>();
            var hdr = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.aspect = W / (float)H;
            cam.targetTexture = hdr;
            cam.Render();
            Graphics.Blit(hdr, rt);
            RenderTexture.ReleaseTemporary(hdr);
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            cam.ResetAspect();
            RenderTexture.ReleaseTemporary(rt);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        static int Differ(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b ? -1 : 0;
            if (a.Length != b.Length) return System.Math.Min(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return i;
            return -1;
        }

        static int Over(byte[] a, int channel, int over)
        {
            int n = 0;
            for (int i = channel; a != null && i < a.Length; i += 4) if (a[i] > over) n++;
            return n;
        }

        // ---- The test ----

        [UnityTest, Timeout(1800000)]
        public IEnumerator CratersCharAndAWitheredTreeComeBackFromTheLoadWithNoCleanFrame()
        {
            yield return Battle(FieldMap, m => new[]
            {
                Seat(SeatKind.Human, "ARAMON", 0, 0, 0),
                Seat(SeatKind.Computer, "VERUNA", 1, 1, Farthest(m, 0)),
            });
            int me = B.LocalPlayer;
            var start = StartOf(0);
            var middle = Ground(B.Terrain.Size.x * 0.5f, -B.Terrain.Size.y * 0.5f);
            var cannons = Place(me, "ARACAN", 6);
            var mage = Place(me, "TARMIND", 1);
            Assert.AreEqual(6, cannons.Count, "the cannon found room");
            Assert.AreEqual(1, mage.Count, "the Mind Mage found room");
            yield return Run(30);

            // Cannon on open ground toward the middle of the map.
            var field = Flat(start + (middle - start).normalized * 24f, 14f, 8f);
            var rng = new System.Random(5);
            foreach (int h in cannons)
                B.Command(GameCommand.To(CommandKind.AttackGround, h, field + new Vector3((float)rng.NextDouble() * 16f - 8f, 0f, (float)rng.NextDouble() * 16f - 8f)));
            var scars = root.World.Scars;
            for (int t = 0; t < 60 * 90 && scars.Stamped < 80; t += 30) yield return Run(30);
            foreach (int h in cannons) B.Command(GameCommand.To(CommandKind.Stop, h, Vector3.zero));
            Assert.GreaterOrEqual(scars.Stamped, 80, "the cannon have been firing");
            yield return Run(60 * 3);

            // The Mind Mage's Area Mind Control, dark magic that under the
            // remastered rules reaches scenery, on the wood nearest the
            // craters, once its own mana covers the spell.
            int mind = Def("TARMIND"), slot = 1;
            Assert.AreEqual(BlastKind.Dark, FxKinds.Of(B.Weapon(mind, slot), "TARMIND"), "the Mind Mage's area spell is dark magic");
            var size = B.Terrain.Size;
            int n = B.ReadFeatures(featBuf);
            var tree = featBuf.Take(n).Where(f => f.Def >= 0 && f.Def < B.FeatureDefs.Count && IsTree(B.FeatureDefs[f.Def]) && (f.Position - field).magnitude > 18f &&
                                                  f.Position.x > 24f && f.Position.x < size.x - 24f && -f.Position.z > 24f && -f.Position.z < size.y - 24f)
                .OrderBy(f => (f.Position - field).sqrMagnitude).First();
            int cost = SpellCost(mage[0], slot);
            Assert.Greater(cost, 0, "the spell has a cost");
            for (int t = 0; t < 60 * 120 && Units(u => u.Handle == mage[0]).Select(u => u.Mana).DefaultIfEmpty(0).First() < cost; t += 60) yield return Run(60);
            var magic = root.World.Magic;
            int darkBefore = magic.Marked[(int)BlastKind.Dark];
            Assert.IsTrue(Cast(mage[0], slot, tree.Position), "the spell was cast");
            for (int t = 0; t < 60 * 90 && magic.Marked[(int)BlastKind.Dark] == darkBefore; t += 30) yield return Run(30);
            Assert.Greater(magic.Marked[(int)BlastKind.Dark], darkBefore, "dark magic reached the scenery");
            yield return Run(60 * 4);

            // The most withered tree, its withering grown in full.
            var marked = new List<(int index, int def, Vector3 at, SceneryLook look)>();
            root.World.Entities.MarkedLooks(marked);
            var withered = marked.Where(x => IsTree(B.FeatureDefs[x.def]) && x.look.Height >= 1.5f && x.look.WitherTo > 0.3f && x.look.Wither == x.look.WitherTo)
                .OrderByDescending(x => x.look.WitherTo).ThenBy(x => (x.at - tree.Position).sqrMagnitude).FirstOrDefault();
            Assert.IsNotNull(withered.look, $"a standing tree is withered, of {marked.Count} marked pieces of scenery");
            var treeAt = withered.at;

            // Two swordsmen walk into the deepest crater.
            var swords = Place(me, "ARASWORD", 2);
            Assert.AreEqual(2, swords.Count, "the swordsmen found room");
            var deep = field;
            float deepest = 0f;
            for (float x = -12f; x <= 12f; x += 0.25f)
                for (float z = -12f; z <= 12f; z += 0.25f)
                {
                    float o = ScarMap.GroundOffset(field.x + x, field.z + z);
                    if (o < deepest) { deepest = o; deep = Ground(field.x + x, field.z + z); }
                }
            Assert.Less(deepest * ScarStamps.PixelsPerUnit, -2f, "a crater pixels deep");
            foreach (int h in swords) B.Command(GameCommand.To(CommandKind.Move, h, deep));
            for (int t = 0; t < 60 * 60; t += 30)
            {
                yield return Run(30);
                if (Units(u => swords.Contains(u.Handle)).Any(u => (u.Position - deep).magnitude < 1.2f && u.Speed < 0.01f)) break;
            }

            // The last blasts' smoke and dust clear, as they are not kept. The
            // particles step a tenth of a second a frame at most, so a few ticks a frame.
            yield return Run(60 * 30, 6);
            var walked = Units(u => swords.Contains(u.Handle));
            Debug.Log($"The deepest crater at {deep} is {deepest * ScarStamps.PixelsPerUnit:0.0} px down, and the swordsmen stand " +
                      string.Join(", ", walked.Select(u => $"{(u.Position - deep).magnitude:0.0} from it sunk {ScarMap.SinkY(u.Position) * ScarStamps.PixelsPerUnit:0.0} px")));
            var mine = Units(u => u.Player == me && !B.UnitDefs[u.Def].CanFly).OrderBy(u => ScarMap.SinkY(u.Position)).First();
            float sink = ScarMap.SinkY(mine.Position);
            Assert.Less(sink, -0.08f, "a unit stands in a crater");

            // Paused, the stamps drawn and the dented regions in place.
            root.Flow.Fire(FlowEvent.Pause);
            var w = root.World;
            yield return Until(() => w.Scars.Pending == 0 && w.Terrain.RefinedRegions == w.Terrain.RefinedList().Count, 60f);
            yield return null;
            var before = w.Scars.Capture();
            int dented = w.Scars.Dents.Dented, regions = w.Terrain.RefinedRegions;
            Assert.IsTrue(FeatureAt(treeAt, out var treeNow), "the withered tree stands");
            var look = w.Entities.LookAt(treeNow.Index, treeNow.Def, treeAt);
            float wither = look.Wither, witherTo = look.WitherTo;
            Assert.Greater(Over(before.Marks, 0, 100), 50, "the ground is charred");
            Assert.Greater(dented, 500, "and dug");
            Assert.Greater(regions, 0, "its regions dip");
            w.Entities.Watch = mine.Handle;
            yield return null;
            var poses = new PiecePose[EntityRenderer.MaxPieces];
            Assert.Greater(B.ReadUnitPose(mine.Handle, poses), 0);
            Assert.AreEqual(poses[0].Matrix.m13 + sink, w.Entities.Watched[0].m13, 0.01f, "the unit is drawn lowered in its crater");
            Debug.Log($"Before the save: {scars.Stamped} stamps, {dented} texels dented, {regions} regions, {Over(before.Marks, 0, 100)} texels charred, " +
                      $"{B.UnitDefs[mine.Def].Name} {sink * ScarStamps.PixelsPerUnit:0.0} px down, tree {B.FeatureDefs[treeNow.Def].Name} withered {wither:0.00}, " +
                      $"{w.Magic.Marked[(int)BlastKind.Dark]} pieces of scenery darkened");
            string shots = Env("OKU_SAVED_SCARS_DIR");
            // The craters and the tree side by side, the sun behind the camera where it can be.
            var focus = Vector3.Lerp(field, treeAt, 0.5f);
            var across = treeAt - field;
            float distance = Mathf.Clamp(new Vector2(across.x, across.z).magnitude * 1.5f + 22f, 30f, 120f);
            float yaw = Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw + 180f, 150f)) < Mathf.Abs(Mathf.DeltaAngle(yaw, 150f))) yaw += 180f;
            if (!string.IsNullOrEmpty(shots))
            {
                Pose(focus, distance, 50f, yaw);
                for (int i = 0; i < 4; i++) yield return null;
                Grab(shots, "before-save");
            }

            // Save game, Quit to menu, and the skirmish page's Load.
            var old = new HashSet<string>(root.ListSaves().Select(e => e.Path));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            ButtonOn("Pause", "Save game").onClick.Invoke();
            double saveMs = clock.Elapsed.TotalMilliseconds;
            var made = root.ListSaves().Where(e => !old.Contains(e.Path)).ToList();
            Assert.AreEqual(1, made.Count, "one new save, the field beside it is not another");
            string saved = made[0].Path;
            Assert.IsTrue(File.Exists(FieldSave.PathFor(saved)), "the field is kept beside the save");
            long fieldBytes = new FileInfo(FieldSave.PathFor(saved)).Length, saveBytes = new FileInfo(saved).Length;
            Assert.AreEqual(fieldBytes, root.LastFieldBytes);
            ButtonOn("Pause", "Quit to menu").onClick.Invoke();
            yield return null;
            ButtonOn("Leave", "Leave").onClick.Invoke();
            yield return null;
            Assert.IsNull(root.World, "the battle is gone");
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            root.Screens.Lobby.Skirmish.Page.Root.GetComponentsInChildren<ArtButton>(true).First(b => b.name == "Load").Press();
            yield return null;
            Assert.AreEqual(FlowState.LoadList, root.Flow.State);
            var entry = root.Screens.Screen("Load").GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Save " + Path.GetFileName(saved));
            Assert.IsNotNull(entry, "the save is in the list");
            clock.Restart();
            entry.onClick.Invoke();

            // Every frame the field is drawn, from its first, is the field as saved.
            var problems = new List<string>();
            ScarState first = null;
            int drawn = 0;
            float deadline = Time.realtimeSinceStartup + 300f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var v = root.World;
                if (v == null || v.FramesDrawn == 0) continue;
                drawn++;
                if (first == null) first = v.Scars.Capture();
                if (v.Scars.Dents.Dented != dented) problems.Add($"frame {drawn}: {v.Scars.Dents.Dented} texels dented, not {dented}");
                if (v.Terrain.RefinedRegions != regions) problems.Add($"frame {drawn}: {v.Terrain.RefinedRegions} regions dip, not {regions}");
                float sunk = ScarMap.SinkY(mine.Position);
                if (sunk != sink) problems.Add($"frame {drawn}: the crater under the unit sinks it {sunk}, not {sink}");
                var l = v.Entities.LookAt(treeNow.Index, treeNow.Def, treeAt);
                if (l == null || Mathf.Abs(l.Wither - wither) > 1e-4f) problems.Add($"frame {drawn}: the tree is withered {l?.Wither}, not {wither}");
                if (root.Flow.State == FlowState.Playing && drawn >= 5) break;
            }
            double loadMs = clock.Elapsed.TotalMilliseconds;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            CollectionAssert.IsEmpty(problems.Take(10).ToList(), "no frame shows the field otherwise");
            Assert.IsNull(root.World.FieldNote, "the field was restored");
            Assert.AreEqual(-1, Differ(before.Marks, first.Marks), "the char and soil as saved, texel for texel");
            Assert.AreEqual(-1, Differ(before.Shape, first.Shape), "the dips, rims and cracks as saved");
            Assert.AreEqual(-1, Differ(before.Fade, first.Fade), "what was fading, as it was");
            Assert.AreEqual(-1, Differ(before.Depth, first.Depth), "the CPU copy of the dips");
            Assert.AreEqual(-1, Differ(before.Rim, first.Rim), "the CPU copy of the rims");

            // The same unit, drawn lowered in the same crater, and the tree withered as it was.
            var ents = root.World.Entities;
            var back = Units(u => u.StableId == mine.StableId);
            Assert.AreEqual(1, back.Count, "the unit is back");
            Assert.Less((back[0].Position - mine.Position).magnitude, 0.01f, "where it stood");
            ents.Watch = back[0].Handle;
            yield return null;
            Assert.Greater(B.ReadUnitPose(back[0].Handle, poses), 0);
            Assert.AreEqual(sink, ScarMap.SinkY(back[0].Position), "the crater under it as deep");
            Assert.AreEqual(poses[0].Matrix.m13 + sink, ents.Watched[0].m13, 0.01f, "drawn lowered in it");
            var after = ents.LookAt(treeNow.Index, treeNow.Def, treeAt);
            Assert.AreEqual(witherTo, after.WitherTo, "the tree's withering");
            Assert.AreEqual(wither, after.Wither, 1e-4f, "drawn as withered");
            Assert.Greater(ents.MarkedDrawn, 0, "marked scenery is drawn with its marks");
            Debug.Log($"Edmont's Field kept in {fieldBytes / 1024f:0.0} KB beside a {saveBytes / 1024f:0.0} KB save, saved in {saveMs:0} ms, " +
                      $"loaded to its first played frame in {loadMs / 1000.0:0.0} s, {root.World.LooksRestored} looks restored, every one of {drawn} frames as saved");
            if (!string.IsNullOrEmpty(shots))
            {
                Pose(focus, distance, 50f, yaw);
                for (int i = 0; i < 4; i++) yield return null;
                Grab(shots, "after-load");
            }

            // And it plays on.
            uint tick = B.Tick;
            yield return Run(60);
            Assert.Greater(B.Tick, tick, "the loaded battle runs");
        }

        static readonly string[] Sides = { "ARAMON", "TAROS", "VERUNA", "ZHON", "CREON", "ARAMON", "TAROS", "VERUNA" };

        // Each kingdom's army for the long battle, as the perf scenes field them.
        static (string, int)[] Army(string side)
        {
            switch (side)
            {
                case "ARAMON": return new[] { ("ARACAN", 8), ("ARAPULT", 3), ("ARATRE", 2), ("ARAGREN", 4), ("ARASWORD", 8), ("ARAPRIES", 2), ("ARADRAG", 1) };
                case "TAROS": return new[] { ("TARMAGE", 4), ("TARFIRE", 2), ("TARARCH", 8), ("TARBLACK", 6), ("TARWITCH", 2), ("TARDRAG", 1) };
                case "VERUNA": return new[] { ("VERMUSK", 8), ("VERMORT", 3), ("VERBAL", 2), ("VERSWORD", 8), ("VERLIHR", 2), ("VERDRAG", 1) };
                case "ZHON": return new[] { ("ZONGIANT", 3), ("ZONTROLL", 6), ("ZONORC", 8), ("ZONSHAM", 3), ("ZONDRAG", 1) };
                default: return new[] { ("CRETORT", 3), ("CRESHOC", 8), ("CREAUTO", 6), ("CREFIRE", 2), ("CREDRAG", 1) };
            }
        }

        // A long battle on the largest map: eight kingdoms in two alliances,
        // an army each sent at the middle and sent again every three minutes,
        // for a quarter of an hour, then saved, to measure what its field keeps.
        [UnityTest, Timeout(3600000)]
        public IEnumerator ALongBattlesFieldIsMeasured()
        {
            if (string.IsNullOrEmpty(Env("OKU_FIELD_LONG"))) Assert.Ignore("set OKU_FIELD_LONG to measure a long battle's field");
            int minutes = int.TryParse(Env("OKU_FIELD_LONG"), out int m) && m > 1 ? m : 15;
            int seats = 0;
            yield return Battle(LongMap, info =>
            {
                seats = Mathf.Min(8, info.Starts.Length);
                var list = new SeatSetup[seats];
                for (int i = 0; i < seats; i++)
                    list[i] = Seat(i == 0 ? SeatKind.Human : SeatKind.Computer, Sides[i], i, i % 2, i);
                return list;
            });
            var w = root.World;
            var middle = Ground(B.Terrain.Size.x * 0.5f, -B.Terrain.Size.y * 0.5f);
            // A guard at home, so the local kingdom stands to the end.
            Place(B.Players[0].Index, "ARASWORD", 8);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int minute = 0;
            for (; minute < minutes && GameFlow.InGame(root.Flow.State); minute++)
            {
                if (minute % 3 == 0)
                {
                    var army = new List<int>();
                    for (int i = 0; i < seats; i++)
                        foreach (var (u, c) in Army(Sides[i])) army.AddRange(Place(B.Players[i].Index, u, c));
                    foreach (int h in army) B.Command(GameCommand.To(CommandKind.Patrol, h, middle));
                }
                yield return Run(60 * B.TicksPerSecond, 60);
                Debug.Log($"Long battle, minute {minute + 1}: {w.Scars.Stamped} stamps, {w.Scars.Dents.Dented} texels dented, {Units(u => true).Count} units");
            }
            if (!GameFlow.InGame(root.Flow.State)) Assert.Inconclusive($"the battle ended after {minute} minutes: {root.Flow.State}");
            root.Flow.Fire(FlowEvent.Pause);
            yield return Until(() => w.Scars.Pending == 0 && w.Terrain.RefinedRegions == w.Terrain.RefinedList().Count, 120f);
            var marked = new List<(int, int, Vector3, SceneryLook)>();
            w.Entities.MarkedLooks(marked);
            var save = System.Diagnostics.Stopwatch.StartNew();
            Assert.IsTrue(root.SaveNow(out string path));
            double saveMs = save.Elapsed.TotalMilliseconds;
            long fieldBytes = new FileInfo(FieldSave.PathFor(path)).Length, saveBytes = new FileInfo(path).Length;
            var f = FieldSave.Load(path, out string why);
            Assert.IsNotNull(f, why);
            long raw = f.Scars.Marks.Length * 3L + f.Scars.Depth.Length * 2L;
            string line = $"A long battle on {root.CurrentMap().Name}: {minutes} minutes of {seats} kingdoms in {clock.Elapsed.TotalSeconds:0} s, {w.Scars.Stamped} stamps, " +
                          $"{w.Scars.Dents.Dented} texels dented of {w.Scars.TexW}x{w.Scars.TexH}, {w.Terrain.RefinedRegions} regions, {marked.Count} pieces of scenery marked, " +
                          $"{f.Fires.Count} fires; field kept in {fieldBytes / 1048576.0:0.00} MB ({raw / 1048576.0:0.0} MB as held) beside a {saveBytes / 1048576.0:0.00} MB save, saved in {saveMs:0} ms";
            Debug.Log(line);
            string dir = Env("OKU_SAVED_SCARS_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "long-battle.txt"), line + System.Environment.NewLine);
            }
            Assert.Less(fieldBytes, 8L * 1024 * 1024, "a long battle's field keeps under 8 MB");
        }
    }
}
