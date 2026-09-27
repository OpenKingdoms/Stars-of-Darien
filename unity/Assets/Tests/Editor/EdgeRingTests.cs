// EdgeRingTests.cs - the land past the map's edge meets the edge with no
// step, eases down away from it, and mirrors the map's picture.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class EdgeRingTests
    {
        static MapTerrain Loaded(string map)
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = GameRoot.DefaultSetup(b);
            s.MapId = map;
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b.Terrain;
        }

        [Test]
        public void TheRingMeetsEveryEdgeWithoutAStep()
        {
            var t = Loaded("mock_highlands");
            var size = t.Size;
            float shelf = EdgeRing.Shelf(t);
            for (float x = 0; x <= size.x; x += 3)
            {
                Assert.AreEqual(t.Sample(x, 0), EdgeRing.Height(t, new Vector2(x, 0.001f), shelf), 0.05f, "north edge at " + x);
                Assert.AreEqual(t.Sample(x, -size.y), EdgeRing.Height(t, new Vector2(x, -size.y - 0.001f), shelf), 0.05f, "south edge at " + x);
            }
            for (float z = 0; z <= size.y; z += 3)
            {
                Assert.AreEqual(t.Sample(0, -z), EdgeRing.Height(t, new Vector2(-0.001f, -z), shelf), 0.05f, "west edge at " + z);
                Assert.AreEqual(t.Sample(size.x, -z), EdgeRing.Height(t, new Vector2(size.x + 0.001f, -z), shelf), 0.05f, "east edge at " + z);
            }
        }

        [Test]
        public void TheRingIsSmoothAndReachesTheShelf()
        {
            var t = Loaded("mock_frost");
            float shelf = EdgeRing.Shelf(t);
            var size = t.Size;
            // Walking out from the middle of the west edge, no step is steep.
            float prev = EdgeRing.Height(t, new Vector2(0, -size.y / 2), shelf);
            for (float d = 0.5f; d <= EdgeRing.Width; d += 0.5f)
            {
                float h = EdgeRing.Height(t, new Vector2(-d, -size.y / 2), shelf);
                Assert.Less(Mathf.Abs(h - prev), 1.5f, "a gentle slope at " + d);
                prev = h;
            }
            Assert.AreEqual(shelf, EdgeRing.Height(t, new Vector2(-EdgeRing.Width, -size.y / 2), shelf), 0.01f, "the far side of the ring is the shelf");
        }

        [Test]
        public void TheRingMirrorsTheMap()
        {
            var t = Loaded("mock_isles");
            var m = EdgeRing.Mirror(t, new Vector2(-5, -10));
            Assert.AreEqual(new Vector2(5, -10), m);
            var size = t.Size;
            m = EdgeRing.Mirror(t, new Vector2(size.x + 3, 2));
            Assert.AreEqual(new Vector2(size.x - 3, -2), m);
        }

        [Test]
        public void TheRingMeshLeavesTheMapItselfOpen()
        {
            var t = Loaded("mock_isles");
            var mesh = EdgeRing.Build(t);
            try
            {
                var v = mesh.vertices;
                var tris = mesh.triangles;
                var size = t.Size;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var c = (v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f;
                    bool inside = c.x > 0.01f && c.x < size.x - 0.01f && c.z < -0.01f && c.z > -size.y + 0.01f;
                    Assert.IsFalse(inside, "no ring triangle lies on the map");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
