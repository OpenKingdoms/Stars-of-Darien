// FieldSaveTests.cs - the battlefield's look kept beside a saved game: the
// scar textures and the CPU copy of the dips come back byte for byte, frost,
// wet and holy light fade on from where they were, the snow over old scars
// lies as it lay, a newer, damaged or misplaced sidecar is refused, a save
// with none loads clean, and a long battle's field stays small.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FieldSaveTests
    {
        const float Px = ScarStamps.PixelsPerUnit;
        EffectsQuality before;
        ScarMap map;
        ScarSnow snow;
        string dir;

        [SetUp]
        public void High()
        {
            before = FxQuality.Current.Level;
            FxQuality.Use(EffectsQuality.High);
        }

        [TearDown]
        public void CleanUp()
        {
            snow?.Dispose();
            snow = null;
            map?.Dispose();
            map = null;
            FxQuality.Use(before);
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true);
            dir = null;
        }

        static MockBackend Game(string id)
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0 };
            var s = new SkirmishSetup { MapId = id, Seed = 5, MapRevealed = true, LineOfSight = false };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            Assert.AreEqual(GameStatus.Running, b.Status, id + " loaded");
            return b;
        }

        static WeaponInfo Gun(string name, string explosion, int areaPx, WeaponFlags flags = WeaponFlags.None, string type = "ballistic") =>
            new WeaponInfo { Name = name, Type = type, DamageKind = "explosion", ExplosionClass = explosion, AreaOfEffect = areaPx / Px, Flags = flags };

        static readonly WeaponInfo Cannon = Gun("ARACAN 1", "large explosion", 90);
        static readonly WeaponInfo Catapult = Gun("ARAPULT 1", "large dust puff", 100);
        static readonly WeaponInfo Fireball = Gun("TARHEL 1", "flamestrike", 100, WeaponFlags.FireStarter, "line of sight");

        // Dry ground at a point, or the nearest dry ground along a line to the map's middle.
        static Vector3 Dry(MockBackend b, float x, float z)
        {
            var t = b.Terrain;
            var mid = new Vector3(t.Size.x * 0.5f, 0f, -t.Size.y * 0.5f);
            for (float k = 0f; k <= 1f; k += 0.05f)
            {
                float px = Mathf.Lerp(x, mid.x, k), pz = Mathf.Lerp(z, mid.z, k);
                float g = t.Sample(px, pz);
                if (g > t.SeaLevel + 0.5f) return new Vector3(px, g, pz);
            }
            return new Vector3(x, t.Sample(x, z), z);
        }

        static BlastEvent Blast(int id, WeaponInfo w, Vector3 at, Vector3 dir) =>
            new BlastEvent { Id = id, Weapon = w, Position = at, Radius = w.AreaOfEffect * 0.5f, Direction = dir, Unit = -1, Feature = -1, Def = -1 };

        // Craters of cannon and catapult and fire's char in a few patches of
        // the map, landing a few a frame as in a battle, with frost, wet,
        // holy light and blight beside them.
        static float Scar(ScarMap m, MockBackend b, int blasts, int seed, float clock, int patches = 3, float patch = 18f)
        {
            var rng = new System.Random(seed);
            var size = b.Terrain.Size;
            var centres = new List<Vector2>();
            for (int i = 0; i < patches; i++)
                centres.Add(new Vector2(size.x * (0.2f + 0.6f * (float)rng.NextDouble()), -size.y * (0.2f + 0.6f * (float)rng.NextDouble())));
            for (int i = 0; i < blasts; i++)
            {
                var c = centres[i % centres.Count];
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = patch * Mathf.Sqrt((float)rng.NextDouble());
                var at = Dry(b, c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                var dir = new Vector3(Mathf.Cos(a + 1f), 0f, Mathf.Sin(a + 1f));
                var w = i % 7 == 3 ? Catapult : i % 11 == 5 ? Fireball : Cannon;
                m.Add(Blast(1000 + i, w, at, dir));
                if (i % 8 == 7) m.Update(clock += 0.25f);
            }
            var k = centres[0];
            m.Mark(ScarKind.Frost, Dry(b, k.x + 10f, k.y), 4f);
            m.Mark(ScarKind.Water, Dry(b, k.x - 10f, k.y), 4f);
            m.Mark(ScarKind.Holy, Dry(b, k.x, k.y + 10f), 4f);
            m.Mark(ScarKind.Dark, Dry(b, k.x, k.y - 10f), 4f);
            m.Mark(ScarKind.Lightning, Dry(b, k.x + 6f, k.y + 6f), 2f);
            return clock;
        }

        // Draws every waiting stamp, a quarter second of the battle a frame.
        static float Drain(ScarMap m, float clock)
        {
            for (int i = 0; i < 4000 && m.Pending > 0; i++) m.Update(clock += 0.25f);
            return clock;
        }

        static byte[] Bytes(FieldState f, params (string tag, byte[] payload)[] extra)
        {
            var ms = new MemoryStream();
            FieldSave.Write(ms, f, extra);
            return ms.ToArray();
        }

        static FieldState Back(byte[] bytes)
        {
            var f = FieldSave.Read(new MemoryStream(bytes), out string why);
            Assert.IsNotNull(f, why);
            return f;
        }

        // The first index where two arrays differ, -1 when they are the same.
        static int Differ(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b ? -1 : 0;
            if (a.Length != b.Length) return System.Math.Min(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return i;
            return -1;
        }

        static int NonZero(byte[] a, int channel, int channels, int over = 0)
        {
            int n = 0;
            for (int i = channel; i < a.Length; i += channels) if (a[i] > over) n++;
            return n;
        }

        static void Same(ScarState a, ScarState b, string what)
        {
            Assert.AreEqual(-1, Differ(a.Marks, b.Marks), what + ": the marks texture");
            Assert.AreEqual(-1, Differ(a.Shape, b.Shape), what + ": the shape texture");
            Assert.AreEqual(-1, Differ(a.Fade, b.Fade), what + ": the fade texture");
            Assert.AreEqual(-1, Differ(a.Depth, b.Depth), what + ": the CPU copy of the dips");
            Assert.AreEqual(-1, Differ(a.Rim, b.Rim), what + ": the CPU copy of the rims");
        }

        [Test]
        public void TheScarTexturesAndTheDipsComeBackByteForByte()
        {
            var b = Game("mock_highlands");
            map = ScarMap.Begin(b, null, "grass", EffectsQuality.High);
            float clock = Drain(map, Scar(map, b, 80, 7, 30f));
            // A few still waiting, as when a save comes in a busy frame.
            map.Mark(ScarKind.Fire, Dry(b, 40f, -40f), 2f);
            map.Mark(ScarKind.Gunpowder, Dry(b, 60f, -50f), 2.5f);
            var saved = map.Capture();
            Assert.AreEqual(2, saved.Pending.Length, "two stamps waiting");
            Assert.Greater(NonZero(saved.Shape, 0, 4), 500, "craters dug");
            Assert.Greater(NonZero(saved.Marks, 0, 4, 100), 100, "char");
            Assert.Greater(NonZero(saved.Fade, 0, 4), 10, "frost");
            Assert.Greater(map.Dents.Dented, 500, "dips kept on the CPU");
            var probes = new List<(float x, float z, float offset)>();
            for (int i = 0; i < 4000; i++)
            {
                float x = (i % 80) * b.Terrain.Size.x / 80f + 0.37f, z = -(i / 80) * b.Terrain.Size.y / 50f - 0.21f;
                probes.Add((x, z, ScarMap.GroundOffset(x, z)));
            }

            var bytes = Bytes(new FieldState { Scars = saved, Clock = clock, FxClock = clock });
            Debug.Log($"{saved.TexW}x{saved.TexH} texels, {map.Dents.Dented} dented, {bytes.Length / 1024f:0.0} KB kept");
            map = ScarMap.Begin(b, null, "grass", EffectsQuality.High);
            Assert.AreEqual(0, map.Dents.Dented, "a new battle's field is clean");
            map.Restore(Back(bytes).Scars, clock);
            var again = map.Capture();
            Same(saved, again, "restored");
            Assert.AreEqual(saved.Pending.Length, again.Pending.Length, "the stamps still waiting");
            for (int i = 0; i < saved.Pending.Length; i++)
            {
                Assert.AreEqual(saved.Pending[i].Kind, again.Pending[i].Kind);
                Assert.AreEqual(saved.Pending[i].X, again.Pending[i].X);
                Assert.AreEqual(saved.Pending[i].Dent, again.Pending[i].Dent);
                Assert.AreEqual(saved.Pending[i].Seed, again.Pending[i].Seed);
            }
            Assert.AreEqual(saved.Now, again.Now);
            Assert.AreEqual(saved.SteppedAt, again.SteppedAt);
            Assert.AreEqual(saved.Marked, again.Marked);
            int dipped = 0;
            foreach (var (x, z, offset) in probes)
            {
                Assert.AreEqual(offset, ScarMap.GroundOffset(x, z), $"the ground at ({x:0.00}, {z:0.00})");
                if (offset < 0f) dipped++;
            }
            Assert.Greater(dipped, 20, "some probes fell in craters");
        }

        [Test]
        public void FrostWetAndHolyLightFadeOnFromWhereTheyWere()
        {
            var b = Game("mock_highlands");
            map = ScarMap.Begin(b, null, "grass", EffectsQuality.High);
            var frost = Dry(b, 40f, -40f);
            var wet = Dry(b, 60f, -40f);
            var holy = Dry(b, 80f, -40f);
            float t = 100f;
            map.Update(t);
            map.Mark(ScarKind.Frost, frost, 3f);
            map.Mark(ScarKind.Water, wet, 3f);
            map.Mark(ScarKind.Holy, holy, 3f);
            map.Update(t += 0.25f);
            for (int i = 0; i < 39; i++) map.Update(t += 0.25f);
            var saved = map.Capture();
            var bytes = Bytes(new FieldState { Scars = saved, Clock = t, FxClock = t });
            Color32 At(ScarState s, Vector3 p) => map.TexelAt(Pixels(s.Fade), p.x, p.z);
            // Holy light lies brightest on a ring at 0.8 of its reach, and its middle fades first.
            var ring = holy + new Vector3(0.8f * 3f, 0f, 0f);
            var then = (At(saved, frost).r, At(saved, wet).g, At(saved, ring).b);

            // Ten seconds on with no save.
            float u = t;
            for (int i = 0; i < 20; i++) map.Update(u += 0.5f);
            var on = map.Capture();
            var played = (At(on, frost).r, At(on, wet).g, At(on, ring).b);
            Debug.Log($"frost {then.Item1} to {played.Item1}, wet {then.Item2} to {played.Item2}, holy light {then.Item3} to {played.Item3}");
            Assert.Greater(then.Item1, played.Item1, "frost melts");
            Assert.Greater(played.Item1, 0, "and is not gone");
            Assert.Greater(then.Item2, played.Item2, "wet dries");
            Assert.Greater(played.Item2, 0);
            Assert.Greater(then.Item3, played.Item3, "holy light fades");
            Assert.Greater(played.Item3, 0);

            // Saved and loaded at the same second, the same ten seconds on.
            map = ScarMap.Begin(b, null, "grass", EffectsQuality.High);
            map.Restore(Back(bytes).Scars, t);
            u = t;
            for (int i = 0; i < 20; i++) map.Update(u += 0.5f);
            Assert.AreEqual(-1, Differ(on.Fade, map.Capture().Fade), "loaded, it fades as it would have");

            // Loaded on a battle clock that starts again, as the mock's loads do.
            map = ScarMap.Begin(b, null, "grass", EffectsQuality.High);
            map.Restore(Back(bytes).Scars, 0f);
            u = 0f;
            for (int i = 0; i < 20; i++) map.Update(u += 0.5f);
            Assert.AreEqual(-1, Differ(on.Fade, map.Capture().Fade), "on another clock too");
        }

        static Color32[] Pixels(byte[] rgba)
        {
            var px = new Color32[rgba.Length / 4];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            return px;
        }

        [Test]
        public void TheSnowOverOldScarsLiesAsItLay()
        {
            var b = Game("mock_frost");
            map = ScarMap.Begin(b, null, "snow", EffectsQuality.High);
            snow = new ScarSnow(b, map);
            var old = Dry(b, 30f, -30f);
            var fresh = Dry(b, 60f, -60f);
            snow.Update(true, 10f);
            map.Update(20f);
            map.Mark(ScarKind.Gunpowder, old, 2.5f);
            map.Update(20.25f);
            map.Update(150f);
            map.Mark(ScarKind.Gunpowder, fresh, 2.5f);
            map.Update(150.25f);
            snow.Update(true, 200f);
            float buried = snow.CoverAt(old.x, old.z), showing = snow.CoverAt(fresh.x, fresh.z);
            Assert.AreEqual(1f, buried, 1e-4f, "the old crater is under the snow");
            Assert.Less(showing, 0.5f, "the fresh one shows dark through it");
            var bytes = Bytes(new FieldState { Scars = map.Capture(), Snow = snow.Capture(), Clock = 200f, FxClock = 200f });

            foreach (float clock in new[] { 200f, 0f })
            {
                snow.Dispose();
                map = ScarMap.Begin(b, null, "snow", EffectsQuality.High);
                snow = new ScarSnow(b, map);
                var f = Back(bytes);
                map.Restore(f.Scars, clock);
                snow.Restore(f.Snow, clock);
                snow.Update(true, clock);
                Assert.AreEqual(buried, snow.CoverAt(old.x, old.z), 1e-4f, $"loaded at {clock} s, the old crater is still buried");
                Assert.AreEqual(showing, snow.CoverAt(fresh.x, fresh.z), 1e-4f, "and the fresh one still shows");
            }
        }

        [Test]
        public void TheSidecarReadsBackAndStepsOverWhatALaterVersionAdds()
        {
            var f = new FieldState { Map = "mock_frost", Tick = 4321, Clock = 72.05f, FxClock = 72.1f, SaveLength = 99, SaveHash = 0xFEEDF00DUL };
            f.Looks.Add(new LookState { Index = 7, Def = 2, At = new Vector3(10.5f, 3f, -20.5f), Char = 0.4f, WitherTo = 0.9f, WitherAt = 70f, Split = 0.2f, SplitAngle = 1.3f, FrostPeak = 0.8f, FrostAt = 71f });
            f.Fires.Add(new FireState { At = new Vector3(4f, 1f, -6f), Def = 3, Seed = 11, Start = 65f, Seconds = 12f, Out = true, OutAt = 70f, SmoulderFor = 25f, Look = 0 });
            foreach (var bytes in new[] { Bytes(f), Bytes(f, ("NEXT", new byte[3000]), ("MORE", new byte[] { 1, 2, 3 })) })
            {
                var g = Back(bytes);
                Assert.AreEqual(f.Map, g.Map);
                Assert.AreEqual(f.Tick, g.Tick);
                Assert.AreEqual(f.Clock, g.Clock);
                Assert.AreEqual(f.FxClock, g.FxClock);
                Assert.AreEqual(f.SaveLength, g.SaveLength);
                Assert.AreEqual(f.SaveHash, g.SaveHash);
                Assert.AreEqual(1, g.Looks.Count);
                Assert.AreEqual(f.Looks[0].Index, g.Looks[0].Index);
                Assert.AreEqual(f.Looks[0].At, g.Looks[0].At);
                Assert.AreEqual(f.Looks[0].WitherTo, g.Looks[0].WitherTo);
                Assert.AreEqual(f.Looks[0].Split, g.Looks[0].Split);
                Assert.AreEqual(f.Looks[0].FrostAt, g.Looks[0].FrostAt);
                Assert.AreEqual(1, g.Fires.Count);
                Assert.AreEqual(f.Fires[0].At, g.Fires[0].At);
                Assert.AreEqual(f.Fires[0].OutAt, g.Fires[0].OutAt);
                Assert.IsTrue(g.Fires[0].Out);
                Assert.AreEqual(0, g.Fires[0].Look);
                Assert.IsNull(g.Scars, "no scar map was kept");
            }
        }

        [Test]
        public void ANewerDamagedOrForeignSidecarIsRefused()
        {
            var good = Bytes(new FieldState { Map = "mock_frost", Tick = 1, Clock = 1f, FxClock = 1f });
            var newer = (byte[])good.Clone();
            newer[8] = (byte)(FieldSave.Version + 1);
            Assert.IsNull(FieldSave.Read(new MemoryStream(newer), out string why));
            StringAssert.Contains("newer", why);
            var cut = new byte[good.Length / 2];
            System.Array.Copy(good, cut, cut.Length);
            Assert.IsNull(FieldSave.Read(new MemoryStream(cut), out why));
            StringAssert.Contains("damaged", why);
            var noise = new byte[256];
            new System.Random(3).NextBytes(noise);
            Assert.IsNull(FieldSave.Read(new MemoryStream(noise), out why));
            StringAssert.Contains("not a kept field", why);
        }

        [Test]
        public void ASaveWithoutAFieldLoadsCleanAndAFieldKeepsToItsOwnSave()
        {
            dir = Path.Combine(Path.GetTempPath(), "oku-field-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string save = Path.Combine(dir, "2026-10-12 10-00-00 Frost Pass.oksav");
            File.WriteAllText(save, "{\"map\":\"mock_frost\",\"tick\":120}");
            Assert.IsNull(FieldSave.Load(save, out string why), "an old save has no field beside it");
            StringAssert.Contains("no field", why);

            Assert.Greater(FieldSave.Save(save, null, null, "mock_frost"), 0, "the field is written");
            string side = FieldSave.PathFor(save);
            Assert.IsTrue(File.Exists(side));
            Assert.AreEqual(".okfield", Path.GetExtension(side));
            CollectionAssert.AreEqual(new[] { save }, Directory.GetFiles(dir, "*.oksav"), "the saves listed are the save alone");
            Assert.IsNotNull(FieldSave.Load(save, out why), why);

            // The save written over by another battle of the same name.
            File.WriteAllText(save, "{\"map\":\"mock_frost\",\"tick\":121}");
            Assert.IsNull(FieldSave.Load(save, out why));
            StringAssert.Contains("another save", why);
            FieldSave.Forget(save);
            Assert.IsFalse(File.Exists(side), "forgotten before a save of the same name");
        }

        [Test]
        public void AFieldAtOddsWithItselfLeavesTheFieldCleanAndThrowsNothing()
        {
            var b = Game("mock_frost");
            map = ScarMap.Begin(b, null, "snow", EffectsQuality.High);
            snow = new ScarSnow(b, map);
            // A snow record claiming more patches than it holds, and textures smaller than they say.
            snow.Restore(new SnowState { W = 999, H = 999, Now = 1f, Since = 0f, Scarred = new float[10] }, 1f);
            map.Restore(new ScarState { TexW = 100, TexH = 100, SizeX = 96f, SizeZ = 96f, Marks = new byte[7], Shape = new byte[7], Fade = new byte[7], Depth = new byte[3], Rim = new byte[3] }, 1f);
            snow.Update(true, 1f);
            Assert.AreEqual(0, map.Dents.Dented, "the field stays clean");
            Assert.AreEqual(0f, ScarMap.GroundOffset(48f, -48f));
        }

        [Test]
        public void ALongBattlesFieldStaysSmall()
        {
            // The largest maps the scar map covers in full at High: 512 units, 2048 texels across.
            var b = Game("mock_marches");
            map = ScarMap.Begin(b, null, "grass", EffectsQuality.High);
            Assert.AreEqual(2048, map.TexW);
            long empty = Bytes(new FieldState { Scars = map.Capture() }).Length;
            Drain(map, Scar(map, b, 4000, 11, 10f, 6, 40f));
            var saved = map.Capture();
            long raw = saved.Marks.Length * 3L + saved.Depth.Length * 2L;
            long kept = Bytes(new FieldState { Scars = saved }).Length;
            Debug.Log($"A long battle's field on a 512 unit map: {map.Stamped} stamps, {map.Dents.Dented} texels dented, " +
                      $"{raw / 1048576f:0.0} MB as held, {kept / 1048576f:0.00} MB kept, {empty / 1024f:0.0} KB for a clean field");
            Assert.Greater(map.Stamped, 3500, "thousands of craters");
            Assert.Less(empty, 128 * 1024, "a clean field keeps to a few dozen KB");
            Assert.Greater(kept, 512 * 1024, "it holds the craters");
            Assert.Less(kept, 4L * 1024 * 1024, "a long battle's field keeps under 4 MB");
        }
    }
}
