// FirePlayTests.cs - fire and magic on scenery, on the mock as the engine
// will report it: a lit row of trees spreads downwind and leaves char,
// frost rimes a tree and a rock and melts, dark magic withers a tree and a
// bush but hardly a rock, rain steams a fire and water puts one out,
// lightning splits the tree it strikes, each kind of magic marks what it
// reaches, and a big forest fire in a big magic battle keeps inside the
// plan's budget.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FirePlayTests
    {
        GameRoot root;
        MockBackend mock;
        EntityRenderer Ents => root.World.Entities;
        FxFire Fire => root.World.Fire;
        FxMagic Magic => root.World.Magic;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        // Frost Pass has no sea, so the middle of the map is dry land. The
        // game stands still, and each test moves it on itself.
        IEnumerator Begin(string map = "mock_frost", int computers = 1)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
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
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            mock.SceneryBreaks = true;
            mock.SeeAll(true);
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            for (int n = mock.ReadFeatures(fs); n > 0; n--) mock.RemoveFeature(n - 1);
            for (int i = 0; i < 3; i++) yield return null;
        }

        FeatureState FeatureAt(int index)
        {
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            Assert.Less(index, mock.ReadFeatures(fs));
            return fs[index];
        }

        int Def(string name) => mock.FeatureDefs.First(d => d.Name == name).Id;

        // A feature so many cells from the middle of the map.
        int Place(string kind, int dx = 0, int dz = 0)
        {
            var c = mock.StageCentre;
            float cell = mock.Terrain.CellSize;
            return mock.PlaceFeature(Def(kind), Mathf.RoundToInt(c.x / cell) + dx, Mathf.RoundToInt(-c.z / cell) + dz);
        }

        // A feature at a world point, on its cell.
        int PlaceAt(string kind, Vector3 at)
        {
            float cell = mock.Terrain.CellSize;
            return mock.PlaceFeature(Def(kind), Mathf.RoundToInt(at.x / cell), Mathf.RoundToInt(-at.z / cell));
        }

        // Flat dry ground near a point, as the mock's maps keep lakes in the middle.
        static Vector3 Dry(IGameBackend b, Vector3 near, float across)
        {
            float sea = b.Terrain.SeaLevel;
            for (float r = 0f; r < 120f; r += 2f)
                for (int a = 0; a < 16; a++)
                {
                    var p = near + new Vector3(Mathf.Cos(a * Mathf.PI / 8f) * r, 0f, Mathf.Sin(a * Mathf.PI / 8f) * r);
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int k = 0; k < 9; k++)
                    {
                        var q = p + new Vector3((k % 3 - 1) * across * 0.5f, 0f, (k / 3 - 1) * across * 0.5f);
                        float g = b.GroundHeight(q.x, q.z);
                        lo = Mathf.Min(lo, g);
                        hi = Mathf.Max(hi, g);
                    }
                    if (lo > sea + 0.5f && hi - lo < across * 0.25f) { p.y = b.GroundHeight(p.x, p.z); return p; }
                }
            Assert.Fail("no flat dry ground");
            return near;
        }

        SceneryLook LookOf(int index)
        {
            var f = FeatureAt(index);
            return Ents.LookAt(index, f.Def, f.Position);
        }

        // The game so many ticks a frame, as it plays.
        IEnumerator Ticks(int frames, int perFrame = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                mock.Advance(perFrame);
                yield return null;
            }
        }

        // A long while at once, then a few frames for the looks to catch up.
        IEnumerator Skip(float seconds)
        {
            mock.Advance(Mathf.RoundToInt(seconds * mock.TicksPerSecond));
            for (int i = 0; i < 3; i++) yield return null;
        }

        void Look(Vector3 at, float distance)
        {
            var cam = root.World.Camera;
            cam.focus = at;
            cam.Zoom(distance);
        }

        int CharAt(Vector3 at)
        {
            var scars = root.World.Scars;
            return scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z).r;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator ALitRowOfTreesSpreadsDownwindAndLeavesChar()
        {
            yield return Begin();
            mock.SpreadChance = 1f;
            mock.SetWind(90f, MockBackend.WindMax);
            // Four cells apart, past a neighbour's sparks but inside the wind's reach.
            var row = new int[5];
            for (int k = 0; k < row.Length; k++) row[k] = Place("mock_tree", (k - 2) * 4, 0);
            var xs = row.Select(i => FeatureAt(i).Position).ToArray();
            var middle = xs[2];
            Look(middle + new Vector3(4f, 0f, 0f), 26f);
            yield return Ticks(3);
            mock.FireFx("TARARCH 1", middle + new Vector3(0f, 1.5f, -6f), middle);
            var caught = new float[row.Length];
            for (int k = 0; k < caught.Length; k++) caught[k] = -1f;
            var places = new List<Vector3>();
            float downwind = float.NegativeInfinity;
            int lightsSeen = 0;
            for (int f = 0; f < 700; f++)
            {
                mock.Advance(2);
                yield return null;
                float now = mock.Tick / (float)mock.TicksPerSecond;
                places.Clear();
                Fire.BurningPlaces(places);
                foreach (var p in places)
                    for (int k = 0; k < xs.Length; k++)
                        if (caught[k] < 0f && Mathf.Abs(p.x - xs[k].x) < 0.1f) caught[k] = now;
                lightsSeen = Mathf.Max(lightsSeen, Fire.LightsAsked);
                if (places.Count > 0 && Fire.Burning > 0)
                {
                    var smoke = root.World.Effects.SmokeCentre;
                    if (smoke != Vector3.zero) downwind = Mathf.Max(downwind, smoke.x - places.Average(p => p.x));
                }
            }
            Debug.Log($"Fire: caught at {string.Join(", ", caught.Select(c => c.ToString("0.0")))} s; lit {Fire.Lit}, spread {Fire.Spread}, sparks {Fire.SparkBursts}, " +
                      $"flames {Fire.FlamesMade}, smoke {Fire.SmokeMade}, embers {Fire.EmbersMade}, char marks {Fire.CharMarks}, smoke {downwind:0.0} units downwind at most");
            Assert.Greater(caught[2], 0f, "the arrow set the middle tree alight");
            Assert.Greater(caught[3], caught[2], "the tree downwind caught after it");
            Assert.Greater(caught[4], caught[3], "and the next after that");
            Assert.Less(caught[1], 0f, "the tree upwind never caught");
            Assert.Less(caught[0], 0f);
            Assert.GreaterOrEqual(Fire.Spread, 2, "it spread from tree to tree");
            Assert.Greater(Fire.FlamesMade, 200, "flames burned on the trees");
            Assert.Greater(Fire.EmbersMade, 10);
            Assert.Greater(lightsSeen, 0, "a fire lights the ground round it");
            Assert.Greater(downwind, 0.5f, "its smoke drifted downwind");
            for (int k = 2; k < 5; k++)
            {
                Assert.Greater(CharAt(xs[k]), 60, $"char on the ground under tree {k}");
                var look = LookOf(row[k]);
                Assert.IsNotNull(look);
                Assert.Greater(look.Char, 0.8f, $"tree {k} is charred");
                Assert.AreEqual(Def("mock_tree_burnt"), FeatureAt(row[k]).Def, $"tree {k} burnt out");
            }
            Assert.Less(CharAt(xs[0]), 10, "no char where nothing burned");
            Assert.IsTrue(LookOf(row[0]) == null || LookOf(row[0]).Char == 0f, "the tree upwind is unmarked");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FrostRimesATreeAndARockAndMelts()
        {
            yield return Begin();
            // The spell would kill the tree by the original's rules; here it only rimes it.
            mock.SceneryBreaks = false;
            int tree = Place("mock_tree"), rock = Place("mock_rock", 2, 0);
            var at = FeatureAt(tree).Position;
            Look(at, 14f);
            yield return Ticks(3);
            mock.FireFx("MOCK FROST SPELL", at + new Vector3(0f, 2f, -6f), at + new Vector3(1f, 0f, 0f));
            int guard = 0;
            while (Magic.Marked[(int)BlastKind.Frost] == 0 && guard++ < 150) yield return Ticks(1);
            yield return Ticks(15);
            var t = LookOf(tree);
            var r = LookOf(rock);
            Assert.Greater(t.Frost, 0.8f, "the tree is rimed");
            Assert.Greater(r.Frost, 0.3f, "and the rock");
            Assert.Greater(Ents.MarkedDrawn, 0, "drawn rimed");
            yield return Skip(30f);
            Assert.Less(t.Frost, 0.9f, "melting after half a minute");
            Assert.Greater(t.Frost, 0.05f);
            yield return Skip(35f);
            Assert.AreEqual(0f, t.Frost, "melted after a minute");
            Assert.AreEqual(0f, r.Frost);
            Assert.AreEqual(0, Ents.MarkedDrawn, "nothing left marked");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DarkMagicWithersATreeAndABushButHardlyARock()
        {
            yield return Begin();
            int tree = Place("mock_tree"), bush = Place("mock_bush", 1, 1), rock = Place("mock_rock", -2, 0);
            var at = FeatureAt(tree).Position + new Vector3(0.5f, 0f, -0.5f);
            Look(at, 14f);
            yield return Ticks(3);
            mock.FireFx("TARMIND 1", at + new Vector3(0f, 2f, -6f), at);
            int guard = 0;
            while (Magic.Marked[(int)BlastKind.Dark] == 0 && guard++ < 150) yield return Ticks(1);
            yield return Ticks(100);
            var t = LookOf(tree);
            var b = LookOf(bush);
            var r = LookOf(rock);
            Assert.Greater(t.Wither, 0.6f, "the tree withers");
            Assert.Greater(b.Wither, 0.6f, "and the bush");
            Assert.Less(r.Wither, 0.35f, "the rock only darkens");
            Assert.Greater(t.Bare, 0f, "the tree lost some leaves");
            var scars = root.World.Scars;
            Assert.Greater(scars.TexelAt(ScarMap.Read(scars.Marks), at.x, at.z).b, 30, "the grass under it is blighted");
            yield return Skip(60f);
            Assert.Greater(t.Wither, 0.6f, "it stays withered");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RainSteamsAFireAndWaterPutsOneOut()
        {
            yield return Begin();
            root.World.Atmosphere.SetWeather(WeatherChoice.Rain);
            mock.SpreadChance = 0f;
            int tree = Place("mock_tree");
            var at = FeatureAt(tree).Position;
            Look(at, 14f);
            yield return Ticks(3);
            mock.FireFx("TARARCH 1", at + new Vector3(0f, 1.5f, -6f), at);
            int guard = 0;
            while (Fire.Burning == 0 && guard++ < 150) yield return Ticks(1);
            Assert.AreEqual(1, Fire.Burning, "the tree caught");
            yield return Ticks(60);
            Assert.LessOrEqual(Fire.LastFlameLife, 0.75f * FxFire.RainFlames + 1e-3f, "rain shortens the flames");
            Assert.Greater(Fire.SteamMade, Fire.SmokeMade, "and its smoke is steam");
            Assert.GreaterOrEqual(Fire.LastSmoke.r, 170, "white");
            // A water spell at it puts it out.
            Magic.Apply(BlastKind.Water, at, 1f, Vector3.zero);
            root.World.Effects.Play(BlastKind.Water, at, 1f, Vector3.zero);
            yield return Ticks(5);
            Assert.AreEqual(1, Fire.Doused);
            long flames = Fire.FlamesMade, steam = Fire.SteamMade;
            yield return Ticks(30);
            Assert.AreEqual(flames, Fire.FlamesMade, "no flames once it is doused");
            Assert.Greater(Fire.SteamMade, steam, "it steams as it smoulders");
            Assert.Greater(LookOf(tree).Wet, 0.5f, "and is wet");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator LightningSplitsTheTreeItStrikes()
        {
            yield return Begin();
            int tree = Place("mock_tree");
            var at = FeatureAt(tree).Position;
            Look(at, 12f);
            yield return Ticks(3);
            mock.FireFx("ZONHUNT 1", at + new Vector3(0f, 3f, -8f), at);
            int guard = 0;
            while (Magic.Marked[(int)BlastKind.Lightning] == 0 && guard++ < 150) yield return Ticks(1);
            yield return Ticks(10);
            var look = LookOf(tree);
            Assert.Greater(look.Split, 0.05f, "its trunk splits");
            Assert.GreaterOrEqual(look.Char, 0.6f, "charred");
            Assert.Greater(look.Glow, 0.3f, "and glowing");
            Assert.GreaterOrEqual(Fire.Smouldering, 1, "smoke curls from the split");
            // The bolt's damage kills it, and its dead trunk keeps the split.
            yield return Ticks(80);
            Assert.AreEqual(Def("mock_tree_dead"), FeatureAt(tree).Def);
            Assert.Greater(LookOf(tree).Split, 0.05f, "the dead trunk stands split");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EachKindOfMagicMarksTheSceneryItReaches()
        {
            yield return Begin();
            mock.SceneryBreaks = false;
            int tree = Place("mock_tree"), rubble = Place("mock_rubble", 3, 0);
            var at = FeatureAt(tree).Position;
            Look(at, 14f);
            yield return Ticks(3);
            var t = LookOf(tree);
            // Dark magic first, so holy light has something to heal.
            Assert.Greater(Magic.Apply(BlastKind.Dark, at, 1f, Vector3.zero), 0);
            yield return Ticks(90);
            float withered = t.Wither;
            Assert.Greater(Magic.Apply(BlastKind.Holy, at, 1f, Vector3.zero), 0);
            yield return Ticks(5);
            Assert.Greater(t.Sheen, 0.3f, "a golden sheen");
            Assert.Less(t.Wither, withered * 0.6f, "holy light heals the withering");
            yield return Skip(10f);
            Assert.Less(t.Sheen, 0.05f, "the sheen is brief");

            Assert.Greater(Magic.Apply(BlastKind.Earth, at, 2f, Vector3.zero), 0);
            yield return Ticks(6);
            Assert.Greater(t.Bend.magnitude, 0.01f, "the quake shakes the tree");
            yield return Skip(8f);
            Assert.Less(t.Bend.magnitude, 0.001f, "and it settles");

            float bare = t.Bare;
            Assert.Greater(Magic.Apply(BlastKind.Wind, at + new Vector3(1.5f, 0f, 0f), 1f, Vector3.zero), 0);
            yield return Ticks(10);
            Assert.Greater(t.Bend.magnitude, 0.05f, "the wind bends it");
            Assert.Greater(t.Bare, bare, "and strips its leaves");

            Assert.Greater(Magic.Apply(BlastKind.Water, at, 1f, Vector3.zero), 0);
            yield return Ticks(3);
            Assert.Greater(t.Wet, 0.5f, "water wets it");

            float charred = LookOf(rubble).Char;
            Magic.Apply(BlastKind.Fire, FeatureAt(rubble).Position, 1f, Vector3.zero);
            yield return Ticks(3);
            Assert.Greater(LookOf(rubble).Char, charred, "fire scorches what will not burn");
            Assert.Greater(Ents.MarkedDrawn, 0);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator SnowSettlesOverOldScarsAndAFreshOneShowsThrough()
        {
            yield return Begin();
            root.World.Atmosphere.SetWeather(WeatherChoice.Snow);
            var at = mock.StageCentre + new Vector3(-6f, 0f, 0f);
            at.y = mock.GroundHeight(at.x, at.z);
            var later = at + new Vector3(12f, 0f, 0f);
            later.y = mock.GroundHeight(later.x, later.z);
            Look(at, 20f);
            yield return Ticks(3);
            mock.FireFx("ARACAN 1", at + new Vector3(0f, 1f, -10f), at);
            yield return Ticks(60);
            var snow = root.World.Snow;
            Assert.Less(snow.CoverAt(at.x, at.z), 0.1f, "a fresh crater shows dark");
            yield return Skip(130f);
            mock.FireFx("ARACAN 1", later + new Vector3(0f, 1f, -10f), later);
            yield return Ticks(60);
            Assert.Greater(snow.CoverAt(at.x, at.z), 0.95f, "two minutes on, snow covers the old one");
            Assert.Less(snow.CoverAt(later.x, later.z), 0.1f, "while the new one shows through");
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            yield return Ticks(2);
            Assert.AreEqual(0f, snow.CoverAt(at.x, at.z), "no snow when it stops snowing");
        }

        static double Median(List<double> xs)
        {
            if (xs.Count == 0) return 0;
            xs.Sort();
            return xs[xs.Count / 2];
        }

        static double Percentile(List<double> xs, float p)
        {
            if (xs.Count == 0) return 0;
            xs.Sort();
            return xs[Mathf.Clamp(Mathf.RoundToInt((xs.Count - 1) * p), 0, xs.Count - 1)];
        }

        // A forest of over three hundred trees burning on the mock's widest
        // map while every kind of magic lands in it a few times a second,
        // drawn at 1440p: fire and magic's own main thread time, and the
        // time to draw the frame and wait for the card, looks on against off.
        [UnityTest, Timeout(900000)]
        public IEnumerator TheBudgetHoldsInABigForestFireAndMagicBattle()
        {
            var before = FxQuality.Current.Level;
            FxQuality.Use(EffectsQuality.High);
            yield return Begin("mock_marches", 3);
            var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.DefaultHDR);
            var cam = Camera.main;
            try
            {
                var fx = root.World.Effects;
                mock.SpreadChance = 0.6f;
                mock.SetWind(60f, MockBackend.WindMax * 0.7f);
                var centre = Dry(mock, mock.StageCentre, 56f);
                var trees = new List<int>();
                for (int gx = 0; gx < 24; gx++)
                    for (int gz = 0; gz < 14; gz++)
                        trees.Add(PlaceAt("mock_tree", centre + new Vector3((gx - 12) * 2 + (gz & 1), 0f, (gz - 7) * 2)));
                for (int k = 0; k < 12; k++) PlaceAt(k % 2 == 0 ? "mock_rock" : "mock_bush", centre + new Vector3((k - 6) * 4 + 1, 0f, -17f));
                var mid = FeatureAt(trees[trees.Count / 2]).Position;
                Look(mid, 60f);
                cam.targetTexture = rt;
                cam.enabled = false;
                yield return Ticks(3);
                for (int k = 0; k < 4; k++)
                {
                    var to = FeatureAt(trees[k * 14 + 3]).Position;
                    mock.FireFx("MOCK FIREBALL SPELL", to + new Vector3(-6f, 3f, 0f), to);
                }
                // Let the fire take hold, then measure.
                yield return Ticks(240);

                var names = new List<string>();
                UnityEngine.Profiling.Sampler.GetNames(names);
                var watched = new List<(string name, UnityEngine.Profiling.Recorder rec)>();
                foreach (var n in names)
                    if (n == "DrawOpaqueObjects" || n == "DrawTransparentObjects")
                    {
                        var r = UnityEngine.Profiling.Recorder.Get(n);
                        r.enabled = true;
                        watched.Add((n, r));
                    }
                var gpuSums = new double[2, 8];
                // Frost, dark, lightning, holy, fire, water, the Earthen Wave and a tornado.
                string[] spells = { "MOCK FROST SPELL", "TARMIND 1", "ZONHUNT 1", "ARAPRIES 3", "TARDRAG 2", "VERMAGE 2", "ARAKING 3", "TARWITCH 1" };
                int most = 0, mostLights = 0, mostBurning = 0, frames = 0;
                double particleMs = 0;

                IEnumerator Phase(bool looks, List<double> cpu, List<double> gpu, List<double> all)
                {
                    fx.SceneryLooks = looks;
                    var rng = new System.Random(5);
                    for (int f = 0; f < 300; f++)
                    {
                        if (f % 15 == 0)
                        {
                            for (int i = 0; i < spells.Length; i++)
                            {
                                var to = mid + new Vector3((float)rng.NextDouble() * 44f - 22f, 0f, (float)rng.NextDouble() * 26f - 13f);
                                to.y = mock.GroundHeight(to.x, to.z);
                                mock.FireFx(spells[i], to + new Vector3(0f, 2f, -8f), to);
                            }
                        }
                        mock.Advance(1);
                        yield return null;
                        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        cam.Render();
                        AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                        double drawn = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        frames++;
                        if (f < 30) continue;
                        cpu.Add(looks ? Fire.Ms + Magic.Ms : 0);
                        all.Add(fx.BlastMs);
                        gpu.Add(drawn);
                        for (int w = 0; w < watched.Count; w++) gpuSums[looks ? 1 : 0, w] += watched[w].rec.gpuElapsedNanoseconds / 1e6;
                        if (looks)
                        {
                            most = Mathf.Max(most, fx.ParticleCount);
                            mostLights = Mathf.Max(mostLights, fx.LightsLit);
                            mostBurning = Mathf.Max(mostBurning, Fire.Burning);
                            particleMs += fx.Particles.StepMs + fx.Particles.SendMs;
                        }
                    }
                }

                var offCpu = new List<double>();
                var offGpu = new List<double>();
                var offAll = new List<double>();
                var onCpu = new List<double>();
                var onGpu = new List<double>();
                var onAll = new List<double>();
                yield return Phase(false, offCpu, offGpu, offAll);
                yield return Phase(true, onCpu, onGpu, onAll);
                double cpuMedian = Median(onCpu), cpuWorst = Percentile(onCpu, 0.95f);
                double allOn = Median(onAll), allOff = Median(offAll);
                double gpuOn = Median(onGpu), gpuOff = Median(offGpu);
                int marked = Magic.Marked.Sum();
                double passes = 0;
                bool reported = false;
                var card = new System.Text.StringBuilder();
                for (int w = 0; w < watched.Count; w++)
                {
                    double without = gpuSums[0, w] / Mathf.Max(1, offCpu.Count), with = gpuSums[1, w] / Mathf.Max(1, onCpu.Count);
                    if (with + without <= 0) continue;
                    card.Append($" {watched[w].name}: {without:0.00} ms without, {with:0.00} ms with;");
                    passes += with - without;
                    reported = true;
                }
                Debug.Log($"Fire budget: {trees.Count} trees, at most {mostBurning} burning in {Fire.Patches} patches, {Fire.Lit} lit, {marked} pieces of scenery marked by magic, " +
                          $"at most {most} particles ({FxQuality.Current.Particles} the cap) and {mostLights} lights; fire and magic on the main thread median {cpuMedian:0.00} ms, " +
                          $"95th {cpuWorst:0.00} ms; every effect part median {allOn:0.00} ms with them and {allOff:0.00} ms without; particles step and send {particleMs / Mathf.Max(1, onCpu.Count):0.00} ms; " +
                          $"a 2560x1440 frame drawn and waited for: median {gpuOn:0.00} ms with them, {gpuOff:0.00} ms without, {gpuOn - gpuOff:0.00} ms for them;" +
                          (reported ? card + $" the looks add {passes:0.00} ms to the opaque and transparent passes" : " the card's passes not reported"));
                Assert.Greater(mostBurning, 60, "a big forest fire");
                Assert.Greater(marked, 100, "a big magic battle");
                foreach (var kind in new[] { BlastKind.Frost, BlastKind.Dark, BlastKind.Lightning, BlastKind.Holy, BlastKind.Earth, BlastKind.Water, BlastKind.Wind })
                    Assert.Greater(Magic.Marked[(int)kind], 0, kind + " marked the scenery it reached");
                Assert.LessOrEqual(most, FxQuality.Current.Particles);
                Assert.LessOrEqual(mostLights, FxQuality.Current.Lights);
                Assert.LessOrEqual(cpuMedian, 1.0, "fire and magic's main thread time at High");
                Assert.LessOrEqual(allOn - allOff, 2.0, "what they add to the effects' main thread time");
                Assert.LessOrEqual(reported ? passes : gpuOn - gpuOff, 2.5, "their graphics time at High");
            }
            finally
            {
                cam.enabled = true;
                cam.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                FxQuality.Use(before);
            }
        }
    }
}
