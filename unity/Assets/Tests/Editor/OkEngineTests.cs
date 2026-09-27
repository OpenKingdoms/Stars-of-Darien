// OkEngineTests.cs - the okengine binding against the real engine: maps
// listed, a skirmish loaded, and terrain, models, units, poses and
// features read through the C# mirror of ok_embed.h. Needs okengine and
// the game files, and is ignored without them.
using NUnit.Framework;
using OpenKingdomsUnity.Engine;

namespace OpenKingdomsUnity.Tests
{
    public class OkEngineTests
    {
        [OneTimeSetUp]
        public void Boot()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            OkEngine.PreloadDependencies(EngineSettings.PluginDir);
            Assert.AreEqual(OkEngine.ApiVersion, OkEngine.okx_api_version());
            Assert.AreEqual(0, OkEngine.okx_init(EngineSettings.GameDir, EngineSettings.DataDir), OkEngine.LastError);
            var cfg = new OkxSkirmish { map = "two castles", kingdom = "aramon", aiPlayers = 1, mapRevealed = 1, seed = 99 };
            Assert.AreEqual(0, OkEngine.okx_start_skirmish(ref cfg), OkEngine.LastError);
        }

        [OneTimeTearDown]
        public void End()
        {
            if (EngineSettings.EngineAvailable) OkEngine.okx_end_game();
        }

        [Test]
        public void TheMapsAreListed()
        {
            var maps = OkEngine.Maps();
            Assert.Greater(maps.Length, 5);
            CollectionAssert.Contains(maps, "two castles");
        }

        [Test]
        public void TheTerrainHasHeightsBlocksAndPictures()
        {
            Assert.AreEqual(0, OkEngine.okx_terrain_info(out var t));
            Assert.Greater(t.heightsW, 1);
            var h = new float[t.heightsW * t.heightsH];
            Assert.AreEqual(h.Length, OkEngine.okx_terrain_heights(h, h.Length));
            var blocks = new int[t.blocksW * t.blocksH * 3];
            Assert.AreEqual(t.blocksW * t.blocksH, OkEngine.okx_terrain_blocks(blocks, blocks.Length));
            int need = OkEngine.okx_terrain_chunk(blocks[0], null, 0, out int w, out int hh);
            Assert.AreEqual(w * hh * 4, need);
        }

        [Test]
        public void UnitsComeWithModelsTexturesAndPoses()
        {
            var units = new OkxUnit[512];
            int n = OkEngine.okx_units(units, units.Length);
            Assert.GreaterOrEqual(n, 2);
            var u = units[0];
            Assert.GreaterOrEqual(u.model, 0);
            Assert.AreEqual(0, OkEngine.okx_model_info(u.model, out var mi));
            Assert.Greater(mi.vertCount, 0);
            var nodes = new OkxNode[mi.nodeCount];
            Assert.AreEqual(mi.nodeCount, OkEngine.okx_model_nodes(u.model, nodes, nodes.Length));
            Assert.AreEqual(-1, nodes[0].parent);
            var batches = new OkxBatch[mi.batchCount];
            OkEngine.okx_model_batches(u.model, batches, batches.Length);
            int tex = -1;
            foreach (var b in batches) if (b.texture >= 0) tex = b.texture;
            Assert.GreaterOrEqual(tex, 0);
            Assert.Greater(OkEngine.okx_texture(tex, null, 0, out _, out _), 0);
            var pose = new float[128 * 12];
            Assert.AreEqual(mi.nodeCount, OkEngine.okx_unit_pose(u.handle, pose, new byte[128], 128));
            Assert.AreEqual(u.x, pose[3], 64f);
            Assert.AreEqual(u.z, pose[11], 64f);
        }

        [Test]
        public void TheEngineTicksAndTakesOrders()
        {
            var units = new OkxUnit[512];
            int n = OkEngine.okx_units(units, units.Length);
            int me = OkEngine.okx_local_player();
            int pick = -1;
            for (int i = 0; i < n && pick < 0; i++) if (units[i].player == me) pick = i;
            Assert.GreaterOrEqual(pick, 0);
            var u = units[pick];
            Assert.AreEqual(0, OkEngine.okx_command((int)OkxCmd.Move, u.handle, (int)u.x + 400, (int)u.z, -1, -1, 0));
            uint t0 = OkEngine.okx_tick_count();
            Assert.AreEqual(120, OkEngine.okx_tick(120));
            Assert.GreaterOrEqual(OkEngine.okx_tick_count(), t0 + 120);
            n = OkEngine.okx_units(units, units.Length);
            float x = u.x;
            for (int i = 0; i < n; i++) if (units[i].handle == u.handle) x = units[i].x;
            Assert.Greater(x, u.x + 50f);
        }

        [Test]
        public void FeaturesAreModelsOrSprites()
        {
            int n = OkEngine.okx_features(null, 0);
            Assert.Greater(n, 0);
            var f = new OkxFeature[n];
            OkEngine.okx_features(f, n);
            foreach (var x in f) Assert.IsTrue(x.model >= 0 || x.sprite >= 0);
            Assert.Greater(OkEngine.okx_feature_def_count(), 100);
        }
    }
}
