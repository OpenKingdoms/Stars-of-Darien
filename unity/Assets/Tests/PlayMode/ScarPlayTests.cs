// ScarPlayTests.cs - the scar map in a game on the mock: a cannon's dip and
// rim, a unit in a crater drawn lower, frost without a crater, the cap under
// five thousand blasts, and scars fading on Low but lasting on High.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class ScarPlayTests
    {
        const float Px = ScarStamps.PixelsPerUnit;
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            FxQuality.Use(EffectsQuality.High);
        }

        // Frost Pass has no sea, so the staged rows stand on dry land.
        IEnumerator Boot(MockBackend mock, EffectsQuality level, string map = "mock_frost")
        {
            root = GameRoot.Boot(mock);
            yield return null;
            root.Options.EffectsQuality = level;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.LineOfSight = false;
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Options.GameSpeed = 0;
            Assert.IsNotNull(root.World.Scars, "the world keeps a scar map");
            Assert.AreEqual(level, root.World.Scars.Level);
        }

        static List<BlastEvent> Blasts(IGameBackend b, string weapon)
        {
            var buf = new BlastEvent[1024];
            var found = new List<BlastEvent>();
            int n = b.ReadBlasts(0, buf);
            for (int i = 0; i < n; i++)
                if (buf[i].Weapon != null && buf[i].Weapon == MockBackend.FxWeaponNamed(weapon)?.Info) found.Add(buf[i]);
            return found;
        }

        // Runs the mock a few ticks a frame until done, then a few frames
        // more so the scar map draws what came and its regions dip.
        static IEnumerator Until(MockBackend mock, System.Func<bool> done, int maxTicks, int perFrame = 2)
        {
            for (int t = 0; t < maxTicks && !done(); t += perFrame)
            {
                mock.Advance(perFrame);
                yield return null;
            }
            for (int i = 0; i < 8; i++) yield return null;
        }

        static IEnumerator Run(MockBackend mock, int ticks, int perFrame)
        {
            for (int t = 0; t < ticks; t += perFrame)
            {
                mock.Advance(perFrame);
                yield return null;
            }
        }

        static int Strongest(Color32 c) => Mathf.Max(c.r, c.g);

        [UnityTest]
        public IEnumerator ACannonBlastDentsTheGroundAboutSixPixelsWithARim()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock, EffectsQuality.High);
            var scars = root.World.Scars;
            Assert.IsTrue(mock.StageFx("ground", 1000f));
            yield return Until(mock, () => Blasts(mock, "ARACAN 1").Count > 0, 900);
            var cannon = Blasts(mock, "ARACAN 1");
            Assert.Greater(cannon.Count, 0, "the cannon fired at the ground");
            // The region's finer mesh is built on a worker.
            for (int f = 0; f < 300 && root.World.Terrain.RefinedRegions == 0; f++) yield return null;
            var at = cannon[0].Position;
            ScarStamps.Make(cannon[0], mock.Terrain.Sample(at.x, at.z), out var stamp);
            float centre = ScarMap.GroundOffset(at.x, at.z) * Px;
            float rim = float.MinValue;
            for (int a = 0; a < 32; a++)
                for (float k = 0.8f; k <= 1.8f; k += 0.1f)
                {
                    float ang = a * Mathf.PI / 16f, r = stamp.Dent * k;
                    rim = Mathf.Max(rim, ScarMap.GroundOffset(at.x + Mathf.Cos(ang) * r, at.z + Mathf.Sin(ang) * r) * Px);
                }
            var shape = ScarMap.Read(scars.Shape);
            var texel = scars.TexelAt(shape, at.x, at.z);
            Debug.Log($"Cannon on the mock: {centre:0.00} px at the centre, rim {rim:0.00} px, shape texel {texel.r}/{texel.g}, " +
                      $"{root.World.Terrain.RefinedRegions} regions dented, {scars.TexW}x{scars.TexH} texels");
            Assert.AreEqual(-stamp.Depth, centre, 0.75f, "its depth at its centre");
            Assert.LessOrEqual(stamp.Depth, 6f + 0.01f, "no deeper than the owner's 6 pixels");
            Assert.Greater(rim, 1f, "a rim of thrown earth");
            Assert.AreEqual(stamp.Depth / ScarStamps.MaxDepthPx * 255f, texel.r, 6f, "the texture the ground dips by holds the same dip");
            Assert.Greater(root.World.Terrain.RefinedRegions, 0, "the crater's region is rebuilt to dip");
            var marks = scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z);
            Assert.Greater(marks.r, 100, "scorched");
            Assert.Greater(marks.g, 100, "and dug");
        }

        [UnityTest]
        public IEnumerator AUnitStandingInACraterIsDrawnLowerByTheDip()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock, EffectsQuality.High);
            Assert.IsTrue(mock.StageFx("ranged", 1000f));
            yield return Until(mock, () => Blasts(mock, "ARACAN 1").Exists(b => b.Unit >= 0), 900);
            var hit = Blasts(mock, "ARACAN 1").Find(b => b.Unit >= 0);
            Assert.GreaterOrEqual(hit.Unit, 0, "the cannon struck the knight standing in the row");
            var ents = root.World.Entities;
            ents.Watch = hit.Unit;
            yield return null;
            UnitState u = default;
            bool found = false;
            for (int i = 0; i < ents.UnitCount; i++)
                if (ents.Units[i].Handle == hit.Unit) { u = ents.Units[i]; found = true; }
            Assert.IsTrue(found, "the knight lives");
            float sink = ScarMap.SinkY(u.Position);
            var poses = new PiecePose[EntityRenderer.MaxPieces];
            Assert.Greater(mock.ReadUnitPose(hit.Unit, poses), 0);
            Assert.Greater(ents.WatchedCount, 0);
            float engine = poses[0].Matrix.m13, drawn = ents.Watched[0].m13;
            Debug.Log($"Knight in a crater: sinks {sink * Px:0.00} px, root piece at {engine:0.000} by the engine and {drawn:0.000} drawn");
            Assert.Less(sink * Px, -3f, "the crater under it is several pixels deep");
            Assert.AreEqual(engine + sink, drawn, 0.01f, "drawn lower by the dip");
        }

        [UnityTest]
        public IEnumerator AFrostSpellLeavesFrostAndCracksButNoCrater()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock, EffectsQuality.High);
            var scars = root.World.Scars;
            Assert.IsTrue(mock.StageFx("ground", 1000f));
            // A shower is one hidden shot to the engine, so one blast where it lands.
            yield return Until(mock, () => Blasts(mock, "ARAPRIES 2").Count > 0, 900);
            var hail = Blasts(mock, "ARAPRIES 2");
            Assert.AreEqual(1, hail.Count, "the hail shower fell");
            // Its far side, away from the neighbouring fire storm's crater.
            var at = hail[0].Position + Vector3.right * hail[0].Radius * 0.5f;
            var fade = scars.TexelAt(ScarMap.Read(scars.Fade), at.x, at.z);
            var shape = scars.TexelAt(ScarMap.Read(scars.Shape), at.x, at.z);
            Debug.Log($"Hail on the mock: frost {fade.r} ({fade.r / 255f * ScarMap.FadeRange:0} s), dip {shape.r}, cracks {shape.b}, offset {ScarMap.GroundOffset(at.x, at.z) * Px:0.00} px");
            Assert.Greater(fade.r, 80, "rime lying for a good while yet");
            Assert.Greater(shape.b, 20, "cracks");
            Assert.AreEqual(0, shape.r, "no dip");
            Assert.AreEqual(0f, ScarMap.GroundOffset(at.x, at.z), "nothing sinks there");
        }

        [UnityTest]
        public IEnumerator TheCapHoldsUnderFiveThousandBlastsAtOnce()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock, EffectsQuality.High);
            var scars = root.World.Scars;
            long bytes = scars.Bytes;
            var cannon = new WeaponInfo { Name = "stress cannon", Type = "ballistic", DamageKind = "explosion", ExplosionClass = "large explosion", AreaOfEffect = 90f / Px };
            var size = mock.Terrain.Size;
            var rng = new System.Random(5);
            for (int i = 0; i < 5000; i++)
            {
                float x = (float)rng.NextDouble() * size.x, z = -(float)rng.NextDouble() * size.y;
                scars.Add(new BlastEvent
                {
                    Id = 100000 + i, Weapon = cannon, Position = new Vector3(x, mock.GroundHeight(x, z), z), Radius = cannon.AreaOfEffect * 0.5f,
                    Unit = -1, Feature = -1,
                });
            }
            int cap = ScarMap.QueueCap(EffectsQuality.High), most = ScarMap.StampsPerFrame(EffectsQuality.High);
            Assert.LessOrEqual(scars.Pending, cap, "the queue holds its cap");
            Assert.Greater(scars.Merged + scars.Dropped, 0, "past the cap stamps merge or give way");
            double worst = 0, total = 0;
            int frames = 0;
            while (scars.Pending > 0 && frames < 300)
            {
                yield return null;
                frames++;
                Assert.LessOrEqual(scars.StampedLastFrame, most, "a frame draws its budget at most");
                worst = System.Math.Max(worst, scars.LastMs);
                total += scars.LastMs;
            }
            Debug.Log($"Stress: 5000 blasts, {scars.PeakPending} waiting at most, {scars.Merged} merged, {scars.Dropped} gave way, " +
                      $"drained in {frames} frames at up to {scars.PeakStamped} a frame, {worst:0.00} ms at worst and {total / Mathf.Max(1, frames):0.00} ms a frame, " +
                      $"{bytes / 1048576f:0.0} MB held, {root.World.Terrain.RefinedRegions} regions dented");
            Assert.AreEqual(0, scars.Pending, "it drains");
            Assert.AreEqual(bytes, scars.Bytes, "the scar map's memory does not grow");
            Assert.Less(worst, 12.0, "no frame stalls on stamps");
        }

        [UnityTest]
        public IEnumerator OnLowTheScarsFade()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock, EffectsQuality.Low);
            var scars = root.World.Scars;
            Assert.IsTrue(mock.StageFx("ground", 1000f));
            yield return Until(mock, () => Blasts(mock, "ARACAN 1").Count > 0, 900);
            var at = Blasts(mock, "ARACAN 1")[0].Position;
            int before = Strongest(scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z));
            Assert.AreEqual(0f, ScarMap.GroundOffset(at.x, at.z), "Low draws no dips");
            yield return Run(mock, 60 * MockBackend.Tps, 30);
            int minute = Strongest(scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z));
            yield return Run(mock, 80 * MockBackend.Tps, 30);
            int later = Strongest(scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z));
            Debug.Log($"Low: the crater's marks {before}, after a minute {minute}, after two and a third {later}");
            Assert.Greater(before, 150, "a fresh crater shows");
            Assert.Less(minute, before - 100, "a minute fades it most of the way");
            Assert.AreEqual(0, later, "two minutes and it is gone");
        }

        [UnityTest]
        public IEnumerator OnHighTheScarsLastTheBattle()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock, EffectsQuality.High);
            var scars = root.World.Scars;
            Assert.IsTrue(mock.StageFx("ground", 1000f));
            yield return Until(mock, () => Blasts(mock, "ARACAN 1").Count > 0, 900);
            var at = Blasts(mock, "ARACAN 1")[0].Position;
            int before = Strongest(scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z));
            float dip = ScarMap.GroundOffset(at.x, at.z);
            yield return Run(mock, 140 * MockBackend.Tps, 30);
            Assert.Greater(before, 150);
            Assert.AreEqual(before, Strongest(scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z)), "the marks stay");
            Assert.AreEqual(dip, ScarMap.GroundOffset(at.x, at.z), "and so does the dip");
        }
    }
}
