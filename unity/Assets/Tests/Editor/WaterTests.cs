// WaterTests.cs - the sea's parts that need no picture: its textures tile
// seamlessly, its baked shore lies where the ground meets the sea and
// follows an edit, a pool is told from the open sea, a map with no water
// under its sea level draws none, the weather eases in and out, and ships
// and hovering units are drawn in and on the surface at their hull's
// waterline.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class WaterTests
    {
        // Across the wrap a texture changes no more than between any two
        // neighbouring columns or rows inside it.
        static void AssertTiles(byte[] px, int n, int channel, string what)
        {
            double inside = 0, across = 0;
            int count = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    inside += System.Math.Abs(px[(y * n + x) * 4 + channel] - px[(y * n + x + 1) * 4 + channel]);
                    count++;
                }
            inside /= count;
            for (int y = 0; y < n; y++) across += System.Math.Abs(px[(y * n + n - 1) * 4 + channel] - px[(y * n) * 4 + channel]);
            across /= n;
            double down = 0;
            for (int x = 0; x < n; x++) down += System.Math.Abs(px[((n - 1) * n + x) * 4 + channel] - px[x * 4 + channel]);
            down /= n;
            Assert.Less(across, inside * 2 + 1, what + " tiles left to right");
            Assert.Less(down, inside * 2 + 1, what + " tiles top to bottom");
        }

        [Test]
        public void TheSeaTexturesTile()
        {
            const int n = 128;
            var waves = WaterTextures.WaveSlopes(n, 48, 11);
            AssertTiles(waves, n, 0, "the waves' x slope");
            AssertTiles(waves, n, 1, "the waves' z slope");
            AssertTiles(waves, n, 2, "the waves' crests");
            AssertTiles(waves, n, 3, "the waves' height");
            AssertTiles(WaterTextures.FoamPixels(n, 23), n, 0, "the foam");
            AssertTiles(WaterTextures.CausticPixels(n, 37), n, 0, "the caustics");
            AssertTiles(WaterTextures.NoisePixels(n, 5), n, 0, "the noise");
        }

        // The slopes are the height's own: the height rises where the slope says.
        [Test]
        public void TheWaveSlopesMatchTheirHeights()
        {
            const int n = 64;
            var px = WaterTextures.WaveSlopes(n, 24, 3);
            int agree = 0, total = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float slope = px[(y * n + x) * 4] / 255f - 0.5f;
                    float rise = px[(y * n + (x + 1) % n) * 4 + 3] - px[(y * n + (x + n - 1) % n) * 4 + 3];
                    if (Mathf.Abs(slope) < 0.08f || Mathf.Abs(rise) < 2) continue;
                    total++;
                    if (Mathf.Sign(slope) == Mathf.Sign(rise)) agree++;
                }
            Assert.Greater(total, 100);
            Assert.Greater(agree / (float)total, 0.95f, "the x slope has the height's sign of change");
        }

        // Ground rising from west to east, crossing the sea at x = 10.3.
        [Test]
        public void TheShoreIsWhereTheGroundMeetsTheSea()
        {
            const float sea = 2f, cross = 10.3f;
            float Ground(float x, float z) => sea + (x - cross) * 0.25f;
            var rect = new Vector4(0, 0, 32, 8);
            var px = WaterTextures.SeaData(rect, 1f, sea, Ground, out int w, out int h);
            Assert.AreEqual(64, w);
            int row = h / 2;
            for (int x = 0; x + 1 < w; x++)
            {
                float a = px[row * w + x].g / 255f - 0.5f, b = px[row * w + x + 1].g / 255f - 0.5f;
                if (a < 0 || b >= 0) continue;
                // The zero crossing between the two texel centres.
                float xa = (x + 0.5f) / 2f, xb = (x + 1.5f) / 2f;
                float at = xa + (xb - xa) * a / (a - b);
                Assert.AreEqual(cross, at, 0.3f, "the baked shore lies where the ground meets the sea");
                float depth = px[row * w + 4].r / 255f * 8f;
                Assert.AreEqual(sea - Ground(2.25f, 0), depth, 0.05f, "and the depth is the sea's over the ground");
                return;
            }
            Assert.Fail("no shore in the baked sea");
        }

        // An island raised in the map editor bakes the same sea, done just
        // around the edit, as a whole bake does.
        [Test]
        public void AnEditBakesTheSeaAsAWholeBakeWould()
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = GameRoot.DefaultSetup(b);
            s.MapId = "mock_bay";
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            var parent = new GameObject("test world");
            var view = new TerrainView();
            try
            {
                view.Build(b, parent.transform);
                Assert.IsNotNull(view.Sea, "the bay has a sea");
                var t = b.Terrain;
                for (int z = 60; z <= 66; z++)
                    for (int x = 64; x <= 70; x++) t.Heights[z * t.HeightsW + x] = 3.5f;
                int per = TerrainBuilder.SamplesPerBlock(t);
                view.Rebuild(new RectInt(64 / per, 60 / per, 7 / per + 2, 7 / per + 2));
                var edited = ((Texture2D)Shader.GetGlobalTexture("_OkuSeaData")).GetPixels32();
                view.Sea.Bake();
                var whole = ((Texture2D)Shader.GetGlobalTexture("_OkuSeaData")).GetPixels32();
                Assert.AreEqual(whole.Length, edited.Length);
                int differ = 0;
                for (int i = 0; i < whole.Length; i++) if (!whole[i].Equals(edited[i])) differ++;
                Assert.AreEqual(0, differ, "texels that differ from a whole bake");
            }
            finally
            {
                view.Dispose();
                Object.DestroyImmediate(parent);
                b.Dispose();
            }
        }

        [Test]
        public void ShipsFloatAndHoveringUnitsStandOnTheSea()
        {
            var ship = new UnitDef { Name = "verman", Category = "VER BOAT ATTACK BALLISTIC" };
            var hover = new UnitDef { Name = "vermage", Category = "VER Monarch" };
            var walker = new UnitDef { Name = "arakni", Category = "ARA MELEE ATTACK" };
            var boatyard = new UnitDef { Name = "verasy", Category = "VER BOAT BUILDING", IsBuilding = true };
            var flyer = new UnitDef { Name = "zonlord", Category = "ZON FLYING", CanFly = true };
            var boatish = new UnitDef { Name = "aragoat", Category = "ARA BOATSWAIN,SHIPBOAT" };
            Assert.AreEqual(FloatKind.Ship, Afloat.KindOf(ship));
            Assert.AreEqual(FloatKind.Hover, Afloat.KindOf(hover));
            Assert.AreEqual(FloatKind.None, Afloat.KindOf(walker));
            Assert.AreEqual(FloatKind.None, Afloat.KindOf(boatyard), "buildings stand where the engine puts them");
            Assert.AreEqual(FloatKind.None, Afloat.KindOf(flyer), "a flyer keeps its own height, hovering name or not");
            Assert.AreEqual(FloatKind.None, Afloat.KindOf(boatish), "BOAT only as a word of its own");
            Assert.AreEqual(FloatKind.Ship, ship.Float, "the def keeps its answer");
            Assert.IsTrue(Afloat.HasWord("VER,BOAT\tATTACK", "boat"));
            Assert.IsTrue(Afloat.HasWord("BOAT", "BOAT"));
            Assert.IsFalse(Afloat.HasWord("", "BOAT"));
            const float sea = 2.5f;
            Assert.AreEqual(sea - Afloat.Draft, Afloat.Height(FloatKind.Ship, 0f, sea), 1e-4f, "a ship's keel sits under the surface");
            Assert.AreEqual(sea, Afloat.Height(FloatKind.Hover, 0f, sea), 1e-4f, "a hovering unit stands on it");
            Assert.AreEqual(0f, Afloat.Height(FloatKind.None, 0f, sea), 1e-4f, "a wading unit keeps to the floor");
            Assert.AreEqual(3f, Afloat.Height(FloatKind.Ship, 3f, sea), 1e-4f, "a ship beached on land stays on it");
            Assert.AreEqual(1f, Afloat.Height(FloatKind.Ship, 1f, -1f), 1e-4f, "with no sea nothing floats");
            Assert.AreEqual(sea - Afloat.Draft, Afloat.Lift(ship, 0f, sea), 1e-4f, "both backends lift a ship off the floor");
            Assert.AreEqual(0f, Afloat.Lift(flyer, 0f, sea), 1e-4f);
        }

        // A hull box two units high, with a mast, cannons on its sides at a
        // height and an oar reaching under the keel.
        static PresentedModel Galleon(float cannonAt)
        {
            Mesh Box(Vector3 min, Vector3 max)
            {
                var v = new Vector3[8];
                for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) != 0 ? max.x : min.x, (i & 2) != 0 ? max.y : min.y, (i & 4) != 0 ? max.z : min.z);
                var m = new Mesh { vertices = v, triangles = new[] { 0, 1, 2, 1, 3, 2, 4, 6, 5, 5, 6, 7 } };
                m.RecalculateBounds();
                return m;
            }
            var pieces = new[]
            {
                new PieceInfo { Name = "ground", Parent = -1, Offset = Vector3.zero },
                new PieceInfo { Name = "base", Parent = 0, Offset = Vector3.zero },
                new PieceInfo { Name = "cannon", Parent = 1, Offset = Vector3.zero },
                new PieceInfo { Name = "mast", Parent = 1, Offset = Vector3.zero },
                new PieceInfo { Name = "oar", Parent = 1, Offset = Vector3.zero },
            };
            var meshes = new[]
            {
                null,
                Box(new Vector3(-1, 0.05f, -4), new Vector3(1, 2.05f, 3)),
                Box(new Vector3(0.7f, cannonAt, -1), new Vector3(1.4f, cannonAt + 0.4f, -0.5f)),
                Box(new Vector3(-0.1f, 1.5f, 0), new Vector3(0.1f, 9f, 0.2f)),
                Box(new Vector3(1.2f, -0.3f, 1), new Vector3(1.8f, 0.5f, 1.2f)),
            };
            var b = new Bounds(meshes[1].bounds.center, Vector3.zero);
            foreach (var m in meshes) if (m != null) { b.Encapsulate(m.bounds.min); b.Encapsulate(m.bounds.max); }
            return new PresentedModel { Data = new ModelData { Pieces = pieces, Scale = 1f }, Pieces = meshes, RestBounds = b };
        }

        // A third of the hull's side goes under, from its keel, unless its
        // gunports would, and the waterline's length and beam are the hull's.
        [Test]
        public void AShipSitsAtItsHullsWaterline()
        {
            var high = ShipHull.Of(Galleon(1.2f));
            Assert.AreEqual(0.05f, high.Keel, 1e-3f, "the keel is the hull's bottom, not the oar's");
            Assert.AreEqual(0.6f, high.Draft, 0.02f, "a third of a two-unit side under the water");
            Assert.AreEqual(1f, high.HalfBeam, 0.01f, "the hull's beam, not the oar's reach");
            Assert.AreEqual(3.5f, high.HalfLength, 0.01f);
            Assert.AreEqual(-0.5f, high.Middle, 0.01f);
            Assert.AreEqual(0.6f - (Afloat.Draft - 0.05f), high.Sink, 0.02f, "drawn that much deeper than the backends lift it");
            var low = ShipHull.Of(Galleon(0.45f));
            Assert.AreEqual(0.45f - 0.05f - 0.08f, low.Draft, 0.02f, "cannons at their ports stay out of the water");
        }

        [Test]
        public void TheSwellOnTheCpuIsCalmOverTheShore()
        {
            float amplitude = WaterWaves.Amplitude, time = WaterWaves.Time;
            try
            {
                WaterWaves.Amplitude = 1f;
                WaterWaves.Time = 3.7f;
                float open = 0, shore = 0;
                for (int i = 0; i < 64; i++)
                {
                    open = Mathf.Max(open, Mathf.Abs(WaterWaves.Height(i * 0.7f, -i * 0.3f, WaterWaves.Damp(3f))));
                    shore = Mathf.Max(shore, Mathf.Abs(WaterWaves.Height(i * 0.7f, -i * 0.3f, WaterWaves.Damp(0.05f))));
                }
                Assert.Greater(open, 0.02f, "the open sea has a swell");
                Assert.Less(open, 0.2f, "a gentle one");
                Assert.Less(shore, open * 0.1f, "which dies away where the water is shallow");
            }
            finally
            {
                WaterWaves.Amplitude = amplitude;
                WaterWaves.Time = time;
            }
        }

        // A pool a couple of cells across is told from the open sea by the
        // widest water round it, so its shore gets no foam.
        [Test]
        public void APoolIsToldFromTheOpenSea()
        {
            const float sea = 2f;
            // The sea west of x = 20, land east of it, and a pool of radius 1.5 at (40, -10).
            float Ground(float x, float z) => x < 20 ? 0f : new Vector2(x - 40, z + 10).magnitude < 1.5f ? 1f : 4f;
            var rect = new Vector4(0, 0, 64, 20);
            var px = WaterTextures.SeaData(rect, 1f, sea, Ground, out int w, out int h);
            Color32 At(float x, float z) => px[Mathf.FloorToInt(-z * 2) * w + Mathf.FloorToInt(x * 2)];
            float Wide(float x, float z) => At(x, z).b / 255f * WaterTextures.WideCap;
            float Shore(float x, float z) => (At(x, z).g / 255f - 0.5f) * 2 * WaterTextures.ShoreCap;
            Assert.Less(Wide(40, -10), 2f, "the pool is narrow all round");
            Assert.Greater(Wide(19.2f, -10), 5f, "the sea's shore has wide water off it");
            Assert.Greater(Shore(4, -10), 12f, "shore distances reach far out to sea");
            Assert.Less(Shore(30, -10), -8f, "and far inland");
        }

        // The sea's data covers the edge ring, so the ring's coasts and
        // shallows carry on past the map.
        [Test]
        public void TheSeaDataCoversTheRing()
        {
            var r = WaterTextures.SeaRect(new Vector2(100, 80), 2f);
            Assert.AreEqual(-EdgeRing.Width * 2f, r.x, 1e-3f);
            Assert.AreEqual(EdgeRing.Width * 2f, r.y, 1e-3f);
            Assert.AreEqual(100 + 4 * EdgeRing.Width, r.z, 1e-3f);
        }

        // In the map editor the engine replaces its terrain after a paint.
        // An edit after that must bake the sea from the new ground.
        [Test]
        public void AnEditAfterAPaintBakesTheNewGround()
        {
            var b = new MockBackend { StageSeconds = 0, ReplacesTerrainOnPaint = true };
            var s = GameRoot.DefaultSetup(b);
            s.MapId = "mock_bay";
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            var parent = new GameObject("test world");
            var view = new TerrainView();
            try
            {
                view.Build(b, parent.transform);
                var before = b.Terrain;
                var ids = b.ChunkLibrary();
                Assert.IsTrue(b.PaintBlocks(0, 0, 1, 1, new[] { ids[0] }, null, null));
                Assert.AreNotSame(before, b.Terrain, "the paint replaced the terrain, as the engine's does");
                var t = b.Terrain;
                // An island in the middle of the bay.
                for (int z = 60; z <= 66; z++)
                    for (int x = 64; x <= 70; x++) t.Heights[z * t.HeightsW + x] = 3.5f;
                int per = TerrainBuilder.SamplesPerBlock(t);
                view.Rebuild(new RectInt(64 / per, 60 / per, 7 / per + 2, 7 / per + 2));
                var tex = (Texture2D)Shader.GetGlobalTexture("_OkuSeaData");
                var rect = view.Sea.SeaRect;
                int ix = Mathf.FloorToInt((67.5f - rect.x) / rect.z * tex.width);
                int iy = Mathf.FloorToInt((1 + (-63.5f - rect.y) / rect.w) * tex.height);
                var c = tex.GetPixel(ix, iy);
                Assert.Less(c.g, 0.5f, "the island's middle is baked as land");
                Assert.AreEqual(0f, c.r, 0.01f, "with no water over it");
            }
            finally
            {
                view.Dispose();
                Object.DestroyImmediate(parent);
                b.Dispose();
            }
        }

        // A sea level under all of a map's ground, as on Inner Circle,
        // draws no sea and leaves the ground's underwater work off.
        [Test]
        public void ASeaLevelWithNoWaterUnderItDrawsNoSea()
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = GameRoot.DefaultSetup(b);
            s.MapId = "mock_frost";
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            var t = b.Terrain;
            for (int i = 0; i < t.Heights.Length; i++) t.Heights[i] += 5f;
            t.SeaLevel = 1f;
            var parent = new GameObject("test world");
            var view = new TerrainView();
            try
            {
                view.Build(b, parent.transform);
                Assert.IsNull(view.Sea, "no sea where no ground lies under it");
                Assert.AreEqual(-1000f, Shader.GetGlobalFloat("_OkuSeaLevel"));
            }
            finally
            {
                view.Dispose();
                Object.DestroyImmediate(parent);
                b.Dispose();
            }
        }

        // The weather's water eases in over a second or so and back out to
        // exactly the clear look, and rain dulls the shallows.
        [Test]
        public void TheWeathersWaterEasesInAndOut()
        {
            var v = new WaterView();
            v.SetClimate("grass");
            var clear = v.Target(WeatherChoice.Off);
            var rain = v.Target(WeatherChoice.Rain);
            v.Ease(WeatherChoice.Off, 0.02f);
            Assert.AreEqual(clear.Waves, v.Now.Waves, "the first frame takes the look at once");
            v.Ease(WeatherChoice.Rain, 0.25f);
            float part = (v.Now.Waves.y - clear.Waves.y) / (rain.Waves.y - clear.Waves.y);
            Assert.That(part, Is.InRange(0.1f, 0.4f), "a quarter second in, the rain is part way in");
            for (int i = 0; i < 80; i++) v.Ease(WeatherChoice.Rain, 0.25f);
            Assert.AreEqual(rain.Waves.y, v.Now.Waves.y, 1e-4f, "and all the way in after a while");
            for (int i = 0; i < 80; i++) v.Ease(WeatherChoice.Off, 0.25f);
            Assert.AreEqual(clear.Waves.y, v.Now.Waves.y, 1e-4f, "rain back to clear is the clear look again");
            Assert.AreEqual(clear.Scatter.g, v.Now.Scatter.g, 1e-4f);
            Assert.AreEqual(clear.Bed.b, v.Now.Bed.b, 1e-4f);
            float Sat(Color c) => (Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b)) / Mathf.Max(c.r, c.g, c.b);
            Assert.Less(Sat(rain.Bed), Sat(clear.Bed) * 0.7f, "a rainy sea bed is greyer");
            Assert.Greater(rain.Sigma.y, clear.Sigma.y, "seen through murkier water");
            Assert.Greater(Sat(clear.Deep), Sat(clear.Scatter), "far from land the sea is a deeper, richer colour");
        }
    }
}
