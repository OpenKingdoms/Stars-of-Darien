// FxBlastTests.cs - each blast's parts round the original's picture on the
// mock: a cannon's flash, light, rings and debris, smoke that drifts with
// the wind, and the Battle effects setting drawing less on Low.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FxBlastTests
    {
        static MockBackend MockGame()
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0 };
            var s = new SkirmishSetup { MapId = "mock_highlands", Seed = 5, MapRevealed = true, LineOfSight = false };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b;
        }

        // The nearest ground to the map's middle well above its water.
        static Vector3 Dry(MockBackend b)
        {
            var c = b.StageCentre;
            float sea = b.Terrain.SeaLevel;
            for (float r = 0f; r < 60f; r += 2f)
                for (int a = 0; a < 16; a++)
                {
                    var p = c + new Vector3(Mathf.Cos(a * Mathf.PI / 8f) * r, 0f, Mathf.Sin(a * Mathf.PI / 8f) * r);
                    p.y = b.GroundHeight(p.x, p.z);
                    // Dry for a few units round, so a blast's parts land on land.
                    bool dry = true;
                    for (int k = 0; k < 8 && dry; k++)
                    {
                        var q = p + new Vector3(Mathf.Cos(k * Mathf.PI / 4f) * 6f, 0f, Mathf.Sin(k * Mathf.PI / 4f) * 6f);
                        dry = b.GroundHeight(q.x, q.z) > sea + 0.5f;
                    }
                    if (dry && p.y > sea + 0.5f) return p;
                }
            Assert.Fail("no dry ground near the middle");
            return c;
        }

        sealed class Scene : System.IDisposable
        {
            public MockBackend B;
            public EffectRenderer Fx;
            public Camera Cam;
            // Dry ground near the map's middle, as its lake sits there.
            public Vector3 At;
            ModelCache models;
            readonly EffectsQuality before = FxQuality.Current.Level;

            public Scene(EffectsQuality quality = EffectsQuality.High)
            {
                FxQuality.Use(quality);
                B = MockGame();
                models = new ModelCache(B);
                Fx = new EffectRenderer(B, models) { FixedClock = true };
                Cam = new GameObject("blast test camera").AddComponent<Camera>();
                At = Dry(B);
                Cam.transform.position = At + new Vector3(0, 30, -20);
                Cam.transform.LookAt(At);
                Fx.Warm(B.WarmEffectStrips());
                Assert.IsTrue(Fx.WaitForArt());
            }

            public void Run(int ticks, System.Action<int> each = null)
            {
                for (int t = 0; t < ticks; t++)
                {
                    B.Advance(1);
                    Fx.Render(Cam);
                    each?.Invoke(t);
                }
            }

            public void Dispose()
            {
                Fx.Dispose();
                models.Dispose();
                Object.DestroyImmediate(Cam.gameObject);
                B.Dispose();
                FxQuality.Use(before);
            }
        }

        [Test]
        public void ACannonBlastMakesAFlashLightRingsAndDebris()
        {
            using (var s = new Scene())
            {
                var at = s.At;
                s.B.FireFx("ARACAN 1", at + new Vector3(0, 1, -8), at);
                int flash = 0, rings = 0, debris = 0, smoke = 0;
                float brightest = 0f;
                s.Run(120, t =>
                {
                    flash = Mathf.Max(flash, s.Fx.FlashLights);
                    rings = Mathf.Max(rings, s.Fx.RingCount);
                    debris = Mathf.Max(debris, s.Fx.DebrisCount);
                    smoke = Mathf.Max(smoke, s.Fx.SmokeCount);
                    brightest = Mathf.Max(brightest, s.Fx.LightBrightest);
                });
                Assert.AreEqual(1, s.Fx.BlastsPlayed, "the cannon's one blast was read and played");
                Assert.Greater(flash, 0, "a flash with a real light");
                Assert.Greater(brightest, FxLook.MaxLightIntensity, "brighter than a lit blast's glow");
                Assert.Greater(rings, 0, "a shock ring along the ground");
                Assert.Greater(debris, 0, "dirt thrown");
                Assert.Greater(smoke, 0, "a smoke column");
            }
        }

        [Test]
        public void ABlastTheFogHidesOrOffScreenThrowsNothing()
        {
            using (var s = new Scene())
            {
                s.Fx.Hidden = (p, player) => true;
                var at = s.At;
                s.B.FireFx("ARACAN 1", at + new Vector3(0, 1, -8), at);
                int most = 0;
                s.Run(60, t => most = Mathf.Max(most, s.Fx.ParticleCount + s.Fx.DebrisCount + s.Fx.RingCount + s.Fx.FlashLights));
                Assert.AreEqual(0, most, "nothing where the player cannot see");

                s.Fx.Hidden = null;
                s.Cam.transform.position = at + new Vector3(0, 30, 200);
                s.Cam.transform.LookAt(at + new Vector3(0, 0, 260));
                s.B.FireFx("ARACAN 1", at + new Vector3(0, 1, -8), at);
                most = 0;
                s.Run(60, t => most = Mathf.Max(most, s.Fx.ParticleCount + s.Fx.DebrisCount));
                Assert.AreEqual(0, most, "nothing behind the camera");
            }
        }

        // Where the smoke has gone from the blast, along the ground.
        static Vector3 Drift(float heading)
        {
            using (var s = new Scene())
            {
                s.B.SceneryBreaks = true;
                s.B.SetWind(heading, MockBackend.WindMax);
                var at = s.At;
                s.B.FireFx("ARACAN 1", at + new Vector3(0, 1, -8), at);
                s.Run(30 * 8);
                Assert.Greater(s.Fx.SmokeCount, 0, "smoke still rising after eight seconds");
                var d = s.Fx.SmokeCentre - at;
                d.y = 0f;
                return d;
            }
        }

        [Test]
        public void SmokeDriftsWithTheWind()
        {
            var east = Drift(90f);
            var west = Drift(270f);
            var north = Drift(0f);
            Debug.Log($"Smoke after eight seconds: east wind {east}, west wind {west}, north wind {north}");
            Assert.Greater(east.x, 2f, "an east wind carries the smoke east");
            Assert.Less(Mathf.Abs(east.z), east.x * 0.5f);
            Assert.Less(west.x, -2f, "a west wind carries it west");
            Assert.Greater(north.z, 2f, "a north wind carries it north");
        }

        // The most particles, debris and lights a volley shows at a setting.
        static (int particles, int debris, int lights) Volley(EffectsQuality q)
        {
            using (var s = new Scene(q))
            {
                var at = s.At;
                string[] guns = { "ARACAN 1", "ARAPULT 1", "TARDRAG 2", "ARACAN 1", "VERMAGE 2", "ARACAN 1" };
                for (int i = 0; i < guns.Length; i++)
                    s.B.FireFx(guns[i], at + new Vector3((i - 2.5f) * 4f, 1, -8), at + new Vector3((i - 2.5f) * 4f, 0, 0));
                int p = 0, d = 0, l = 0;
                s.Run(150, t =>
                {
                    p = Mathf.Max(p, s.Fx.ParticleCount);
                    d = Mathf.Max(d, s.Fx.DebrisCount);
                    l = Mathf.Max(l, s.Fx.LightsLit);
                });
                return (p, d, l);
            }
        }

        [Test]
        public void LowDrawsLessThanHigh()
        {
            var low = Volley(EffectsQuality.Low);
            var high = Volley(EffectsQuality.High);
            Debug.Log($"A volley of six: Low {low}, High {high}");
            Assert.Greater(low.particles, 0, "Low still shows the blasts");
            Assert.Less(low.particles, high.particles, "fewer particles on Low");
            Assert.Less(low.debris, high.debris, "less debris on Low");
            Assert.LessOrEqual(low.lights, FxQuality.For(EffectsQuality.Low).Lights, "no more lights than Low allows");
            Assert.LessOrEqual(low.lights, high.lights);
        }

        [Test]
        public void AnArrowThatMissesStaysStuckInTheGround()
        {
            using (var s = new Scene())
            {
                var at = s.At;
                s.B.FireFx("MOCK ARROW", at + new Vector3(0, 1, -10), at);
                int most = 0;
                s.Run(60, t => most = Mathf.Max(most, s.Fx.StuckShots));
                Assert.Greater(most, 0, "the arrow stands where it fell");
                s.Run(30 * 12);
                Assert.AreEqual(0, s.Fx.StuckShots, "and is gone after a while");
            }
        }

        // Two fights from the same start, one drawing every part and one
        // none, leave the simulation in the same place: nothing goes back.
        [Test]
        public void ThePartsFeedNothingBack()
        {
            string Run(bool parts)
            {
                using (var s = new Scene())
                {
                    s.Fx.Blasts = parts;
                    s.B.DamageScale = 1f;
                    Assert.IsTrue(s.B.StageBreak("break", 1f));
                    s.Run(30 * 12);
                    var text = new System.Text.StringBuilder();
                    var units = new UnitState[1024];
                    int n = s.B.ReadUnits(units);
                    for (int i = 0; i < n; i++) text.Append(units[i].Handle).Append(':').Append(units[i].Health).Append('@').Append(units[i].Position.ToString("F3")).Append(';');
                    var features = new FeatureState[8192];
                    int m = s.B.ReadFeatures(features);
                    for (int i = 0; i < m; i++) text.Append(features[i].Def).Append('@').Append(features[i].Position.ToString("F3")).Append(';');
                    if (parts) Assert.Greater(s.Fx.BlastsPlayed, 3, "the cannons' blasts were drawn");
                    else Assert.AreEqual(0, s.Fx.ParticleCount, "and none without the parts");
                    return text.ToString();
                }
            }
            Assert.AreEqual(Run(false), Run(true));
        }

        // Decision 7: Forward+, so more than four blast lights can light the
        // ground, and every lit shader of ours keeps a variant for it.
        [Test]
        public void TheRendererRunsForwardPlusAndTheLitShadersFollowIt()
        {
            var data = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Game/Rendering/OkuRenderer.asset");
            Assert.IsNotNull(data, "the renderer");
            var mode = new UnityEditor.SerializedObject(data).FindProperty("m_RenderingMode");
            Assert.AreEqual(2, mode.intValue, "Forward+ is URP's rendering mode 2");
            foreach (var name in new[] { "OpenKingdoms/Presentation/Model", "OpenKingdoms/Presentation/Terrain", "OpenKingdoms/Model" })
            {
                var shader = Shader.Find(name);
                Assert.IsNotNull(shader, name);
                Assert.IsTrue(shader.keywordSpace.FindKeyword("_CLUSTER_LIGHT_LOOP").isValid, name + " has a Forward+ variant");
            }
        }

        [Test]
        public void EveryKindPlaysItsOwnParts()
        {
            using (var s = new Scene())
            {
                var at = s.At;
                var seen = new System.Collections.Generic.Dictionary<BlastKind, string>();
                foreach (BlastKind kind in System.Enum.GetValues(typeof(BlastKind)))
                {
                    if (kind == BlastKind.None) continue;
                    s.Fx.Play(kind, at, 3f, Vector3.forward);
                    int p = 0, d = 0, l = 0, r = 0;
                    s.Run(45, t =>
                    {
                        p = Mathf.Max(p, s.Fx.ParticleCount);
                        d = Mathf.Max(d, s.Fx.DebrisCount);
                        l = Mathf.Max(l, s.Fx.FlashLights);
                        r = Mathf.Max(r, s.Fx.RingCount);
                    });
                    Assert.Greater(p + d, 0, kind + " shows something");
                    seen[kind] = $"{p} particles {d} debris {l} lights {r} rings";
                    s.Fx.Clear();
                }
                Debug.Log("Blast parts: " + string.Join("; ", System.Linq.Enumerable.Select(seen, kv => kv.Key + " " + kv.Value)));
            }
        }
    }
}
