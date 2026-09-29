// LodestoneGlowPlayTests.cs - a lodestone breathing on screen, with a
// stand-in card on the mock's lodge: its crystal is brighter at the swell
// than at the ebb while its stone stays as it is, and a ring spreads from
// the crystal after the swell and is gone before the next.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class LodestoneGlowPlayTests
    {
        GameRoot root;
        readonly List<Object> made = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            CardOverride.Forget();
            foreach (var o in made) if (o != null) Object.Destroy(o);
            made.Clear();
            if (root != null) Object.Destroy(root.gameObject);
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        RenderTexture rt;

        Texture2D Shot(Camera cam)
        {
            if (rt == null) { rt = new RenderTexture(640, 480, 24); made.Add(rt); }
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            made.Add(tex);
            return tex;
        }

        // The mean brightness of a small box round a world point, placed as
        // the shot framed it.
        float Around(Texture2D shot, Camera cam, Vector3 at, int half = 4)
        {
            cam.targetTexture = rt;
            var s = cam.WorldToScreenPoint(at);
            cam.targetTexture = null;
            int cx = Mathf.RoundToInt(s.x), cy = Mathf.RoundToInt(s.y);
            float sum = 0f;
            int n = 0;
            for (int y = cy - half; y <= cy + half; y++)
                for (int x = cx - half; x <= cx + half; x++)
                    if (x >= 0 && y >= 0 && x < shot.width && y < shot.height) { sum += Luma(shot.GetPixel(x, y)); n++; }
            return n > 0 ? sum / n : 0f;
        }

        [UnityTest]
        public IEnumerator TheCrystalBreathesTheStoneStaysAndARingSpreads()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);

            var units = new UnitState[512];
            int count = mock.ReadUnits(units);
            var lodge = units.Take(count).First(u => u.Player == mock.LocalPlayer && mock.UnitDefs[u.Def].IsBuilding);
            root.World.Entities.Hidden = u => u.Handle != lodge.Handle;

            // A grey stone off to one side and a green glowing crystal above.
            var template = new GameObject("stand-in lodestone");
            made.Add(template);
            var stoneMat = Looks.Model(null);
            stoneMat.color = new Color(0.5f, 0.5f, 0.5f);
            var crystalMat = Looks.Model(null);
            crystalMat.color = new Color(0.1f, 0.8f, 0.3f);
            crystalMat.SetFloat("_Emission", 1.2f);
            made.Add(stoneMat);
            made.Add(crystalMat);
            var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.transform.SetParent(template.transform, false);
            stone.transform.localPosition = new Vector3(0f, 7f, 0f);
            stone.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            stone.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.transform.SetParent(template.transform, false);
            crystal.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            crystal.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            crystal.GetComponent<MeshRenderer>().sharedMaterial = crystalMat;
            template.SetActive(false);
            var model = OverrideModel.From(template, null, "stand-in");
            var card = new CardOverride { Model = model, Glow = LodestoneGlow.Of(model) };
            Assert.IsNotNull(card.Glow);
            CardOverride.Use(mock.UnitDefs[lodge.Def].ObjectName, card);

            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            gc.focus = lodge.Position;
            gc.pitch = 12f;
            gc.yaw = 0f;
            gc.Zoom(24f);
            for (int i = 0; i < 30; i++) yield return null;
            float lift = root.World.Entities.SiteLift(lodge.Position);
            var crystalAt = lodge.Position + Vector3.up * (4.5f + lift);
            var stoneAt = lodge.Position + Vector3.up * (7f + lift);

            float phase = LodestonePulse.Phase(lodge.Position);
            var shots = new Dictionary<float, Texture2D>();
            var halos = new Dictionary<float, int>();
            foreach (float at in new[] { 0f, 0.5f, 0.25f, 0.7f })
            {
                root.World.Entities.PulseSeconds = (at - phase + 1f) * LodestonePulse.Period;
                yield return null;
                halos[at] = root.World.Entities.HalosDrawn;
                shots[at] = Shot(cam);
            }
            float ebb = Around(shots[0f], cam, crystalAt), swell = Around(shots[0.5f], cam, crystalAt);
            float stoneEbb = Around(shots[0f], cam, stoneAt), stoneSwell = Around(shots[0.5f], cam, stoneAt);
            // Where the ring lies a third of the way through its life.
            float radius = LodestonePulse.RingRadius(0.2f / LodestonePulse.RingLife, card.Glow.Radius, LodestonePulse.RingReach);
            var ringAt = crystalAt + cam.transform.right * radius;
            float ringOn = Around(shots[0.7f], cam, ringAt, 2), ringOff = Around(shots[0.25f], cam, ringAt, 2);
            Debug.Log($"Lodestone glow: crystal {ebb:F1} at the ebb, {swell:F1} at the swell; stone {stoneEbb:F1} and {stoneSwell:F1}; ring {ringOff:F1} without, {ringOn:F1} with; halos {halos[0f]} {halos[0.5f]} {halos[0.25f]} {halos[0.7f]}");

            Assert.Greater(stoneEbb, 5f, "the stone is in the shot");
            Assert.Greater(swell, ebb * 1.1f, "the crystal is brighter at its swell");
            Assert.AreEqual(stoneEbb, stoneSwell, stoneEbb * 0.03f + 1f, "the stone around it stays as it is");
            Assert.AreEqual(0, halos[0.25f], "no ring between breaths");
            Assert.AreEqual(1, halos[0.7f], "a ring after the swell");
            Assert.Greater(ringOn, ringOff + 3f, "the ring lights where it spreads");
        }
    }
}
