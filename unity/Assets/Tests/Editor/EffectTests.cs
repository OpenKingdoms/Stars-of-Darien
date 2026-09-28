// EffectTests.cs - the original's rules for effects, the engine's records
// played at its pace, and every kind of shot and effect drawn.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class EffectTests
    {
        // ── The original's rules ──────────────────────────────────────

        [Test]
        public void TheRetailArtBlendsAndTimesAsTheOriginal()
        {
            Assert.IsTrue(FxLook.KnownArt(74, 59, 31, out var explodeb));
            Assert.IsTrue(explodeb.Additive);
            Assert.AreEqual(1, explodeb.Duration, "explodeb shows 30 frames a second");
            Assert.IsTrue(FxLook.KnownArt(52, 47, 11, out var nimbus));
            Assert.IsTrue(nimbus.Additive);
            Assert.AreEqual(3, nimbus.Duration, "a nimbus shows 10 frames a second");
            Assert.IsTrue(FxLook.KnownArt(39, 39, 10, out var fireball));
            Assert.IsTrue(fireball.Additive);
            Assert.AreEqual(2, fireball.Duration);
            Assert.IsTrue(FxLook.KnownArt(9, 9, 4, out var cannon));
            Assert.IsFalse(cannon.Additive, "cannon balls alpha blend");
            Assert.IsTrue(FxLook.KnownArt(102, 109, 12, out var tsunami));
            Assert.IsFalse(tsunami.Additive, "TsunamiExplode alpha blends");
            Assert.IsTrue(FxLook.KnownArt(36, 34, 12, out var flame));
            Assert.IsTrue(flame.Additive);
            Assert.IsFalse(FxLook.KnownArt(3, 3, 3, out _), "a mod's art is not guessed at");
        }

        [Test]
        public void FrameTimesFollowTheBackendsClock()
        {
            Assert.AreEqual(4, FxLook.FrameTicks(2, 60), "15 frames a second at 60 ticks");
            Assert.AreEqual(2, FxLook.FrameTicks(2, 30));
            Assert.AreEqual(2, FxLook.FrameTicks(1, 60));
            Assert.AreEqual(6, FxLook.FrameTicks(3, 60));
        }

        [Test]
        public void BeamsTakeTheWeaponsColours()
        {
            FxLook.BeamColours(BeamKind.Lightning, out var i, out var m, out var o);
            Assert.AreEqual(new Color32(255, 255, 255, 255), i);
            Assert.AreEqual(new Color32(200, 230, 255, 255), m);
            Assert.AreEqual(new Color32(180, 200, 255, 255), o);
            FxLook.BeamColours(BeamKind.CreonParalyzer, out i, out m, out o);
            Assert.AreEqual(new Color32(0, 255, 128, 255), i);
            Assert.AreEqual(new Color32(48, 200, 128, 255), o);
            FxLook.BeamColours(BeamKind.CreonLightbeam, out i, out _, out _);
            Assert.AreEqual(new Color32(255, 255, 150, 255), i);
        }

        [Test]
        public void TheJagHoldsItsEndsAndChangesEachStep()
        {
            bool moved = false;
            for (int s = 0; s <= FxLook.BeamSegments; s++)
            {
                float a = FxLook.Jag(7, 3, s);
                Assert.AreEqual(a, FxLook.Jag(7, 3, s), "steady within a step");
                Assert.LessOrEqual(Mathf.Abs(a), 1f);
                if (s == 0 || s == FxLook.BeamSegments) Assert.AreEqual(0f, a);
                moved |= Mathf.Abs(a - FxLook.Jag(7, 4, s)) > 0.05f;
            }
            Assert.IsTrue(moved, "the next step jags afresh");
            Assert.AreEqual(1, FxLook.JagStep(2, 60) - FxLook.JagStep(0, 60), "a new jag every 30 Hz frame");
        }

        // ── Orientation ───────────────────────────────────────────────

        [Test]
        public void ABeamRunsFromItsSourceToItsEndAndJagsAcrossIt()
        {
            var from = new Vector3(10, 2, -10);
            var to = new Vector3(18, 1, -30);
            var eye = new Vector3(14, 40, 0);
            var pts = new Vector3[FxLook.BeamSegments + 1];
            EffectRenderer.BeamPath(from, to, BeamKind.Lightning, 11, 5, eye, pts);
            Assert.Less((pts[0] - from).magnitude, 1e-4f);
            Assert.Less((pts[pts.Length - 1] - to).magnitude, 1e-4f);
            var dir = (to - from).normalized;
            bool jagged = false;
            for (int s = 0; s < pts.Length; s++)
            {
                var onLine = from + dir * Vector3.Dot(pts[s] - from, dir);
                var aside = pts[s] - onLine;
                Assert.LessOrEqual(aside.magnitude, FxLook.BeamJag + 1e-4f, "within the jag of the ray");
                Assert.AreEqual(0f, Vector3.Dot(aside, dir), 1e-3f, "thrown across the ray, not along it");
                float along = Vector3.Dot(pts[s] - from, dir) / (to - from).magnitude;
                Assert.AreEqual(s / (float)(pts.Length - 1), along, 1e-3f, "in order from source to end");
                jagged |= aside.magnitude > 0.05f;
            }
            Assert.IsTrue(jagged);
            EffectRenderer.BeamPath(from, to, BeamKind.CreonLightbeam, 11, 5, eye, pts);
            foreach (var p in pts)
                Assert.Less(Vector3.Cross(p - from, dir).magnitude, 1e-3f, "a lightbeam is a straight shaft");
        }

        [Test]
        public void AStraightShotAndAnArcFaceTheWayTheyFly()
        {
            var b = MockGame();
            foreach (var weapon in new[] { "VERARCH 1", "ARABOW 1", "ZONTER 1" })
            {
                var from = b.StageCentre + new Vector3(0, 1, -8);
                b.FireFx(weapon, from, from + new Vector3(3, 0, 16));
                b.Advance(4);
                var shots = new ProjectileState[64];
                int n = b.ReadProjectiles(shots);
                var poses = new PiecePose[16];
                bool checkedOne = false;
                for (int i = 0; i < n; i++)
                {
                    if (shots[i].Kind != ShotKind.Model) continue;
                    Assert.Greater(b.ReadProjectilePose(shots[i].Id, poses), 0);
                    var forward = ((Vector3)poses[0].Matrix.GetColumn(2)).normalized;
                    Assert.Greater(Vector3.Dot(forward, shots[i].Velocity.normalized), 0.99f, weapon + " points along its flight");
                    checkedOne = true;
                }
                Assert.IsTrue(checkedOne, weapon + " flies as a model");
                b.Advance(200);
            }
            b.Dispose();
        }

        // ── The engine's records ──────────────────────────────────────

        const int Tps = 60;

        // A record as okx_effects writes one for frame f of a strip of n
        // cells of cw by ch pixels, anchored at the middle.
        static OkxEffect Record(int kind, int id, int sprite, int f, int n, int cw, int ch, float x = 320, float y = 40, float z = 320)
        {
            float sw = cw * n;
            return new OkxEffect
            {
                kind = kind, id = id, sprite = sprite, frame = f, x = x, y = y, z = z,
                offX = cw / 2f, w = cw, top = y + ch / 2f, bottom = y - ch / 2f,
                u0 = f * cw / sw, u1 = (f * cw + cw) / sw, v1 = 1f,
            };
        }

        static Vector2Int Size(int strip) =>
            strip == 1 ? new Vector2Int(95 * 30, 72) : strip == 2 ? new Vector2Int(39 * 10, 39) : strip == 3 ? new Vector2Int(36 * 12, 34) :
            strip == 4 ? new Vector2Int(74 * 31, 59) : new Vector2Int(20 * 5, 20);

        [Test]
        public void ABlastPlaysAtTheOriginalsPaceToItsLastFrame()
        {
            // lightning1: 30 frames of 95 by 72, added, 2 legacy frames each.
            var fx = new EngineFx(Size);
            var effects = new EffectState[16];
            int lastFrame = -1, shownAfterCut = 0;
            for (uint t = 0; t < 140; t++)
            {
                // The engine steps it every 2 ticks and lets it go after 60.
                int engineFrame = (int)t / EngineFx.EngineTicksPerFrame;
                var rec = new[] { Record(OkEngine.EffectImpact, 5, 1, engineFrame, 30, 95, 72) };
                fx.Update(t, Tps, rec, t < 60 ? 1 : 0, new OkxProjectile[0], 0);
                int n = fx.Effects(effects);
                if (t < 120)
                {
                    Assert.AreEqual(1, n, "still playing at tick " + t);
                    Assert.AreEqual(Mathf.Min((int)t / 4, 29), effects[0].Frame, "tick " + t);
                    Assert.AreEqual(t % 4 / 4f, effects[0].Phase, 1e-4f);
                    Assert.IsTrue(effects[0].Additive);
                    Assert.IsFalse(effects[0].Loops);
                    lastFrame = effects[0].Frame;
                    if (t >= 60) shownAfterCut++;
                }
                else Assert.AreEqual(0, n, "played out at tick " + t);
            }
            Assert.AreEqual(29, lastFrame, "it reached its last frame");
            Assert.Greater(shownAfterCut, 50, "it outlived the engine's cut");
            var frames = fx.Frames(1);
            Assert.AreEqual(30, frames.Length);
            Assert.AreEqual(4, frames[10].Ticks);
            Assert.IsTrue(frames[10].Additive);
            Assert.Greater(frames[10].Width, 0f);
        }

        [Test]
        public void ABlastTheEngineHoldsLongerEndsAtTheOriginalsPace()
        {
            // explodeb shows 30 frames a second, 2 ticks each, though the engine gives it 4.
            var fx = new EngineFx(Size);
            var effects = new EffectState[16];
            int lastShown = -1;
            for (uint t = 0; t < 200; t++)
            {
                var rec = new[] { Record(OkEngine.EffectImpact, 1, 4, (int)t / 4, 31, 74, 59) };
                fx.Update(t, Tps, rec, t < 124 ? 1 : 0, new OkxProjectile[0], 0);
                if (fx.Effects(effects) > 0) { lastShown = (int)t; Assert.LessOrEqual(effects[0].Frame, (int)t / 2); }
            }
            Assert.AreEqual(61, lastShown, "31 frames of 2 ticks, and gone after");
        }

        [Test]
        public void AShotsPictureLoopsAtTheOriginalsPace()
        {
            // FireballA: 10 frames of 39, added.
            var fx = new EngineFx(Size);
            var effects = new EffectState[16];
            var shots = new ProjectileState[16];
            for (uint t = 0; t < 90; t++)
            {
                var rec = new[] { Record(OkEngine.EffectProjectile, 3, 2, (int)(t / 2) % 10, 10, 39, 39, 320 + t * 4f) };
                var shot = new[] { new OkxProjectile { id = 3, kind = OkEngine.ProjSprite, model = -1, x = 320 + t * 4f, y = 40, z = 320, vx = 4 } };
                fx.Update(t, Tps, rec, 1, shot, 1);
                Assert.AreEqual(1, fx.Effects(effects));
                Assert.AreEqual(-4, effects[0].Id, "a picture's id is -1 - its shot's");
                Assert.IsTrue(effects[0].IsProjectile);
                if (t >= 20) Assert.AreEqual((int)(t / 4) % 10, effects[0].Frame, "tick " + t);
                Assert.AreEqual(1, fx.Projectiles(shots));
                Assert.AreEqual(ShotKind.Picture, shots[0].Kind);
                Assert.IsFalse(shots[0].Shadow, "added fire throws no cannon ball shadow");
            }
        }

        [Test]
        public void ArtTheTableDoesNotKnowKeepsTheEnginesPaceAndAlpha()
        {
            var fx = new EngineFx(Size);
            var effects = new EffectState[16];
            for (uint t = 0; t < 8; t++)
            {
                fx.Update(t, Tps, new[] { Record(OkEngine.EffectImpact, 1, 9, (int)t / 2, 5, 20, 20) }, 1, new OkxProjectile[0], 0);
                Assert.AreEqual(1, fx.Effects(effects));
                Assert.IsFalse(effects[0].Additive);
            }
        }

        [Test]
        public void ABreathIsToldFromLightningByItsFlames()
        {
            foreach (bool breath in new[] { true, false })
            {
                var fx = new EngineFx(Size);
                var shots = new ProjectileState[8];
                ProjectileState last = default;
                for (uint t = 0; t < 12; t++)
                {
                    var beam = new OkxProjectile { id = 1, kind = OkEngine.ProjBeam, model = -1, x = 400, y = 16, z = 600, fromX = 400, fromY = 16, fromZ = 300 };
                    // A flame leaves the muzzle, 12 pixels up, every tick.
                    var flame = new[] { Record(OkEngine.EffectImpact, (int)t, 3, 0, 12, 36, 34, 400, 28, 300) };
                    fx.Update(t, Tps, flame, breath ? 1 : 0, new[] { beam }, 1);
                    int n = fx.Projectiles(shots);
                    if (!breath && t < EngineFx.BreathWait) { Assert.AreEqual(0, n, "a beam waits a tick for its first flames"); continue; }
                    Assert.AreEqual(1, n);
                    last = shots[0];
                }
                Assert.AreEqual(breath ? BeamKind.Fire : BeamKind.Lightning, last.Beam);
                Assert.AreEqual(ShotKind.Beam, last.Kind);
                var src = EngineSettings.ToUnity(400, 28, 300);
                Assert.Less((last.Source - src).magnitude, 1e-3f, "the source is the body, 12 pixels over the ground");
                Assert.AreEqual(EngineSettings.ToUnity(400, 16, 600).y + 0.5f, last.Position.y, 1e-3f, "the end is raised to the body");
            }
        }

        [Test]
        public void ASlotTakenAgainIsANewBlastAndTheOldOnePlaysOn()
        {
            var fx = new EngineFx(Size);
            var effects = new EffectState[16];
            for (uint t = 0; t < 20; t++) fx.Update(t, Tps, new[] { Record(OkEngine.EffectImpact, 2, 1, (int)t / 2, 30, 95, 72) }, 1, new OkxProjectile[0], 0);
            fx.Effects(effects);
            int first = effects[0].Id;
            fx.Update(20, Tps, new[] { Record(OkEngine.EffectImpact, 2, 1, 0, 30, 95, 72, 900, 40, 900) }, 1, new OkxProjectile[0], 0);
            Assert.AreEqual(2, fx.Effects(effects), "the old blast and the new one");
            var ids = new HashSet<int> { effects[0].Id, effects[1].Id };
            Assert.IsTrue(ids.Contains(first), "the old one keeps its id");
            Assert.AreEqual(2, ids.Count);
        }

        [Test]
        public void EveryKindTheEngineReportsComesThrough()
        {
            var fx = new EngineFx(Size);
            var records = new[]
            {
                Record(OkEngine.EffectImpact, 0, 1, 0, 30, 95, 72),
                Record(OkEngine.EffectProjectile, 1, 2, 0, 10, 39, 39),
            };
            var shots = new[]
            {
                new OkxProjectile { id = 1, kind = OkEngine.ProjSprite, model = -1, x = 320, y = 40, z = 320 },
                new OkxProjectile { id = 2, kind = OkEngine.ProjDot, model = -1, x = 330, y = 40, z = 320 },
                new OkxProjectile { id = 3, kind = OkEngine.ProjModel, model = 4, x = 340, y = 40, z = 320 },
                new OkxProjectile { id = 4, kind = OkEngine.ProjBeam, model = -1, x = 350, y = 0, z = 500, fromX = 350, fromZ = 300 },
            };
            for (uint t = 0; t < 4; t++) fx.Update(t, Tps, records, records.Length, shots, shots.Length);
            var effects = new EffectState[8];
            Assert.AreEqual(2, fx.Effects(effects));
            var kinds = new HashSet<int>();
            var out1 = new ProjectileState[8];
            int n = fx.Projectiles(out1);
            for (int i = 0; i < n; i++) kinds.Add(out1[i].Kind);
            CollectionAssert.AreEquivalent(new[] { ShotKind.Picture, ShotKind.Dot, ShotKind.Model, ShotKind.Beam }, kinds);
        }

        // ── Every family draws ────────────────────────────────────────

        static MockBackend MockGame()
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0 };
            var s = new SkirmishSetup { MapId = "mock_highlands", Seed = 5 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b;
        }

        [Test]
        public void EveryWeaponFamilyTurnsIntoADrawing()
        {
            var b = MockGame();
            var models = new ModelCache(b);
            var fx = new EffectRenderer(b, models);
            var cam = new GameObject("fx test camera").AddComponent<Camera>();
            try
            {
                var at = b.StageCentre;
                cam.transform.position = at + new Vector3(0, 30, -20);
                cam.transform.LookAt(at);
                foreach (var w in MockBackend.FxWeapons)
                {
                    b.FireFx(w.Name, at + new Vector3(0, 1, -5), at + new Vector3(0, 1, 5));
                    int drawn = 0, peakCount = 0;
                    bool beam = false, model = false, light = false;
                    for (int t = 0; t < 150; t++)
                    {
                        b.Advance(1);
                        fx.Render(cam);
                        drawn += fx.Drawn;
                        peakCount = Mathf.Max(peakCount, fx.Count);
                        beam |= fx.Beams > 0;
                        model |= fx.ModelShots > 0;
                        light |= fx.LightsLit > 0;
                    }
                    Assert.Greater(drawn, 0, w.Name + " drew nothing");
                    if (w.Draw == MockBackend.FxDraw.Model) Assert.IsTrue(model, w.Name + " flies as its model");
                    if (w.Draw == MockBackend.FxDraw.Beam) Assert.IsTrue(beam, w.Name + " draws a beam");
                    if (w.Draw == MockBackend.FxDraw.Picture || w.Draw == MockBackend.FxDraw.Flame || w.Draw == MockBackend.FxDraw.Ring ||
                        w.Draw == MockBackend.FxDraw.Rain || w.Draw == MockBackend.FxDraw.Wander || w.Impact != null)
                        Assert.Greater(peakCount, 0, w.Name + " shows its pictures");
                    if (w.Light != FxLight.None && w.Light != FxLight.Auto) Assert.IsTrue(light, w.Name + " lights the ground");
                    b.Advance(300);
                    fx.Render(cam);
                }
            }
            finally
            {
                fx.Dispose();
                models.Dispose();
                Object.DestroyImmediate(cam.gameObject);
                b.Dispose();
            }
        }

        [Test]
        public void AFireBlastScorchesTheGroundAndAShotsTrailFollowsItsPath()
        {
            var b = MockGame();
            var fx = new EffectRenderer(b, new ModelCache(b));
            var cam = new GameObject("fx test camera").AddComponent<Camera>();
            try
            {
                var at = b.StageCentre;
                cam.transform.position = at + new Vector3(0, 30, -20);
                cam.transform.LookAt(at);
                b.FireFx("TARDRAG 2", at + new Vector3(0, 1, -6), at + new Vector3(0, 1, 6));
                int trails = 0;
                for (int t = 0; t < 120; t++) { b.Advance(1); fx.Render(cam); trails = Mathf.Max(trails, fx.TrailCount); }
                Assert.Greater(trails, 0, "the fireball left a trail");
                Assert.Greater(fx.Marks, 0, "its blast left a scorch");
            }
            finally
            {
                fx.Dispose();
                Object.DestroyImmediate(cam.gameObject);
                b.Dispose();
            }
        }

        [Test]
        public void TheArtIsPaddedSoFramesNeverBleed()
        {
            // Two 4 by 2 frames, the first red to its right edge, the second blue.
            var img = new RgbaImage(8, 2);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 8; x++)
                {
                    int i = (y * 8 + x) * 4;
                    img.Pixels[i] = (byte)(x < 4 ? 255 : 0); img.Pixels[i + 2] = (byte)(x < 4 ? 0 : 255); img.Pixels[i + 3] = 255;
                }
            var px = FxArt.Atlas(img, 2, out int w, out int h);
            var art = FxArt.Build(img, 2, false);
            try
            {
                Assert.AreEqual(2, art.Frames);
                Assert.AreEqual(2 * (4 + 2 * FxArt.Pad), w);
                var r = art.Rect(0, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
                // Just past frame 0's right edge is clear, never frame 1's blue.
                int x = Mathf.FloorToInt(r.z * w + 0.5f), y = Mathf.FloorToInt(r.y * h + 0.5f);
                int i = (y * w + x) * 4;
                Assert.AreEqual(0, px[i + 3], "clear between frames");
                Assert.Greater(px[i], 128, "and bled with the edge's own red");
                Assert.Less(px[i + 2], 50);
                var r1 = art.Rect(1, new Vector2(0.5f, 0f), new Vector2(1f, 1f));
                Assert.Greater(r1.x, r.z, "frame 1 sits past frame 0 and its border");
                Assert.AreEqual((4 + 3 * FxArt.Pad) / (float)w, r1.x, 1e-5f);
            }
            finally { art.Dispose(); }
        }
    }
}
