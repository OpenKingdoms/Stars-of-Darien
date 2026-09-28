// FogLookPlayTests.cs - the fog of war looks as the original's: black
// where never seen, on land and on the sea, a little over half as bright
// where seen before, and no feature drawn on ground never seen.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FogLookPlayTests
    {
        GameRoot root;
        MockBackend mock;

        [TearDown]
        public void CleanUp()
        {
            FogView.Disabled = false;
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin(bool lineOfSight)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.MapRevealed = false;
            root.Setup.LineOfSight = lineOfSight;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);
        }

        UnitState[] Units()
        {
            var u = new UnitState[512];
            int n = mock.ReadUnits(u);
            return u.Take(n).ToArray();
        }

        // A knight walks out toward the middle of the map and back, so the
        // ground it passed is seen before and no longer in sight.
        void Scout()
        {
            var knight = Units().First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Knight);
            var size = mock.Terrain.Size;
            var home = knight.Position;
            var toward = new Vector3(size.x / 2, 0, -size.y / 2) - home;
            toward.y = 0;
            var far = home + toward.normalized * 34f;
            Walk(knight.Handle, far);
            Walk(knight.Handle, home);
        }

        void Walk(int handle, Vector3 to)
        {
            Assert.IsTrue(mock.Command(GameCommand.To(CommandKind.Move, handle, to)));
            for (int t = 0; t < 4000; t += 30)
            {
                mock.Advance(30);
                var u = Units().First(x => x.Handle == handle);
                if (new Vector2(u.Position.x - to.x, u.Position.z - to.z).magnitude < 1.5f) return;
            }
        }

        // The fog state at p when all ground within reach shares it, else -1,
        // so a sample is clear of the feathered edges.
        int Settled(FogView fog, Vector3 p, float reach)
        {
            int s = fog.State(p);
            for (float dx = -reach; dx <= reach; dx += 1f)
                for (float dz = -reach; dz <= reach; dz += 1f)
                    if (fog.State(p + new Vector3(dx, 0, dz)) != s) return -1;
            return s;
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        Texture2D Shot(Camera cam, RenderTexture rt)
        {
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            return tex;
        }

        [UnityTest]
        public IEnumerator NeverSeenIsBlackAndSeenBeforeIsHalfLit()
        {
            yield return Begin(true);
            Scout();
            var fog = root.World.Fog;
            fog.Update(true);

            var size = mock.Terrain.Size;
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            var units = Units();
            var knight = units.First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Knight);
            gc.focus = Vector3.Lerp(knight.Position, new Vector3(size.x / 2, 0, -size.y / 2), 0.55f);
            gc.pitch = 80f;
            gc.yaw = 0f;
            gc.Zoom(110f);
            for (int i = 0; i < 30; i++) yield return null;

            var rt = RenderTexture.GetTemporary(960, 540, 24);
            cam.targetTexture = rt;
            // Ground samples by their fog, clear of units and the fog's edges.
            var never = new List<Vector2>();
            var sea = new List<Vector2>();
            var before = new List<Vector2>();
            var ring = new List<Vector2>();
            for (float x = 2; x < size.x - 2; x += 2f)
                for (float z = -2; z > -size.y + 2; z -= 2f)
                {
                    var p = new Vector3(x, mock.GroundHeight(x, z), z);
                    if (units.Any(u => (u.Position - p).sqrMagnitude < 16f)) continue;
                    int s = Settled(fog, p, 4f);
                    if (s < 0 || s == 2) continue;
                    bool wet = mock.Terrain.SeaLevel > 0 && p.y < mock.Terrain.SeaLevel - 0.2f;
                    if (wet) p.y = mock.Terrain.SeaLevel;
                    var sp = cam.WorldToScreenPoint(p);
                    if (sp.z <= 0 || sp.x < 8 || sp.y < 8 || sp.x > rt.width - 8 || sp.y > rt.height - 8) continue;
                    var at = new Vector2(sp.x, sp.y);
                    if (s == 1) before.Add(at);
                    else if (wet) sea.Add(at);
                    else never.Add(at);
                }
            // The land past the west edge takes the fog of the edge beside it.
            for (float z = -2; z > -size.y + 2; z -= 2f)
            {
                if (Settled(fog, new Vector3(0.5f, 0, z), 4f) != 0) continue;
                var sp = cam.WorldToScreenPoint(new Vector3(-6f, mock.GroundHeight(0.5f, z), z));
                if (sp.z <= 0 || sp.x < 8 || sp.y < 8 || sp.x > rt.width - 8 || sp.y > rt.height - 8) continue;
                ring.Add(new Vector2(sp.x, sp.y));
            }
            cam.targetTexture = null;

            var fogged = Shot(cam, rt);
            FogView.Disabled = true;
            fog.Update(true);
            var lit = Shot(cam, rt);
            FogView.Disabled = false;
            fog.Update(true);
            RenderTexture.ReleaseTemporary(rt);
            string dir = Application.temporaryCachePath;
            File.WriteAllBytes(Path.Combine(dir, "fog-look-fogged.png"), fogged.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(dir, "fog-look-lit.png"), lit.EncodeToPNG());
            Debug.Log($"Fog look: {never.Count} land, {sea.Count} sea and {ring.Count} past the edge never seen, {before.Count} seen before, pictures in {dir}");

            Assert.Greater(never.Count, 10, "the view takes in land never seen");
            Assert.Greater(sea.Count, 10, "and sea never seen");
            Assert.Greater(before.Count, 10, "and ground seen before");
            Assert.Greater(ring.Count, 5, "and land past the edge");
            Color32 Px(Texture2D t, Vector2 p) => t.GetPixel((int)p.x, (int)p.y);
            float worst = never.Concat(sea).Concat(ring).Max(p => Luma(Px(fogged, p)));
            Assert.LessOrEqual(worst, 0.03f * 255f, "ground, sea and the land past an edge never seen are black");
            float litLand = never.Average(p => Luma(Px(lit, p)));
            Assert.Greater(litLand, 20f, "and they are not black when lit, so the check means something");

            float sumFog = 0, sumLit = 0;
            foreach (var p in before)
            {
                float l = Luma(Px(lit, p));
                if (l < 40f) continue;
                sumLit += l;
                sumFog += Luma(Px(fogged, p));
            }
            Assert.Greater(sumLit, 0f);
            float ratio = sumFog / sumLit;
            Debug.Log($"Fog look: seen before at {ratio:P1} of lit");
            Assert.That(ratio, Is.InRange(0.50f, 0.56f), "ground seen before shows at a little over half its lit brightness");
        }

        [UnityTest]
        public IEnumerator NoFeatureIsDrawnOnGroundNeverSeen()
        {
            yield return Begin(true);
            // One tree by the monarch, in sight, and one in the far corner.
            var monarch = Units().First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            var size = mock.Terrain.Size;
            int near = mock.PlaceFeature(0, Mathf.RoundToInt(monarch.Position.x + 2), Mathf.RoundToInt(-monarch.Position.z));
            int far = mock.PlaceFeature(0, Mathf.RoundToInt(monarch.Position.x < size.x / 2 ? size.x - 6 : 6), Mathf.RoundToInt(-monarch.Position.z < size.y / 2 ? size.y - 6 : 6));
            Assert.GreaterOrEqual(near, 0);
            Assert.GreaterOrEqual(far, 0);
            yield return new WaitForSecondsRealtime(FogView.Interval + 0.1f);
            yield return null;
            var fs = new FeatureState[4096];
            int n = mock.ReadFeatures(fs);
            Assert.AreNotEqual(0, root.World.Fog.State(fs[near].Position), "the tree by the monarch is in sight");
            Assert.AreEqual(0, root.World.Fog.State(fs[far].Position), "the one in the far corner is not");
            Assert.Greater(n, 0);
            int hidden = 0, shown = 0;
            for (int i = 0; i < n; i++)
            {
                bool never = root.World.Fog.State(fs[i].Position) == 0;
                Assert.AreEqual(never, root.World.Entities.FeatureHidden(i), $"feature {i} at {fs[i].Position}");
                if (never) hidden++; else shown++;
            }
            Assert.Greater(hidden, 0, "some features stand on ground never seen");
            Assert.Greater(shown, 0, "and some in sight");
        }
    }
}
