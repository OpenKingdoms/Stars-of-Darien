// WaterTests.cs - the sea's parts that need no picture: its textures tile
// seamlessly, its baked shore lies where the ground meets the sea, and
// ships and hovering units are drawn in and on the surface.
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
            Assert.AreEqual(FloatKind.Ship, Afloat.KindOf(ship));
            Assert.AreEqual(FloatKind.Hover, Afloat.KindOf(hover));
            Assert.AreEqual(FloatKind.None, Afloat.KindOf(walker));
            Assert.AreEqual(FloatKind.None, Afloat.KindOf(boatyard), "buildings stand where the engine puts them");
            const float sea = 2.5f;
            Assert.AreEqual(sea - Afloat.Draft, Afloat.Height(FloatKind.Ship, 0f, sea), 1e-4f, "a ship's keel sits under the surface");
            Assert.AreEqual(sea, Afloat.Height(FloatKind.Hover, 0f, sea), 1e-4f, "a hovering unit stands on it");
            Assert.AreEqual(0f, Afloat.Height(FloatKind.None, 0f, sea), 1e-4f, "a wading unit keeps to the floor");
            Assert.AreEqual(3f, Afloat.Height(FloatKind.Ship, 3f, sea), 1e-4f, "a ship beached on land stays on it");
            Assert.AreEqual(1f, Afloat.Height(FloatKind.Ship, 1f, -1f), 1e-4f, "with no sea nothing floats");
        }

        [Test]
        public void TheSwellOnTheCpuIsCalmOverTheShore()
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
    }
}
