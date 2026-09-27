// FlightPlayTests.cs - the mock's flyer in a running game: its wings are
// posed by the flight animator rather than the script, it both flaps and
// glides on a long flight, and a paused game draws it the same way frame
// after frame.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FlightPlayTests
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            Time.timeScale = 1f;
            if (root != null) Object.Destroy(root.gameObject);
        }

        static IEnumerator Until(System.Func<bool> done, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        static int PieceIndex(ModelData d, string name)
        {
            for (int p = 0; p < d.Pieces.Length; p++) if (d.Pieces[p].Name == name) return p;
            return -1;
        }

        [UnityTest]
        public IEnumerator AMockFlyerFlapsAndGlides()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f, "the game to load");

            var ents = root.World.Entities;
            int handle = -1, model = -1;
            uint id = 0;
            for (int i = 0; i < ents.UnitCount; i++)
                if (ents.Units[i].Player == mock.LocalPlayer && mock.UnitDefs[ents.Units[i].Def].CanFly)
                {
                    handle = ents.Units[i].Handle;
                    id = ents.Units[i].StableId;
                    model = ents.Units[i].Model;
                }
            Assert.GreaterOrEqual(handle, 0, "the player has a flyer");
            var data = mock.GetModel(model);
            int body = PieceIndex(data, "body"), wing = PieceIndex(data, "wingl1");
            ents.Watch = handle;

            // Back and forth across the map, twice as fast as normal.
            var ends = new[] { new Vector3(130, 0, -24), new Vector3(30, 0, -100) };
            int leg = 0;
            mock.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
            Time.timeScale = 2f;
            bool flapped = false, glided = false, differs = false;
            var poses = new PiecePose[32];
            float end = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                for (int i = 0; i < ents.UnitCount; i++)
                {
                    if (ents.Units[i].Handle != handle) continue;
                    var at = ents.Units[i].Position;
                    if (new Vector2(at.x - ends[leg].x, at.z - ends[leg].z).magnitude < 3f)
                    {
                        leg = 1 - leg;
                        mock.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
                    }
                }
                if (!ents.TryFlight(id, out var f)) continue;
                flapped |= f.Mode == FlightMode.Flap && f.Weight >= 1f;
                glided |= f.Mode == FlightMode.Glide;
                if (f.Weight >= 1f && ents.WatchedCount > wing && mock.ReadUnitPose(handle, poses) > wing)
                {
                    var script = poses[body].Matrix.inverse * poses[wing].Matrix;
                    var drawn = ents.Watched[body].inverse * ents.Watched[wing];
                    differs |= Quaternion.Angle(script.rotation, drawn.rotation) > 2f;
                }
            }
            Time.timeScale = 1f;
            Assert.IsTrue(flapped, "it flapped");
            Assert.IsTrue(glided, "it glided");
            Assert.IsTrue(differs, "the drawn wing is the animator's, not the script's");

            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            yield return null;
            var still = (Matrix4x4[])ents.Watched.Clone();
            for (int i = 0; i < 5; i++) yield return null;
            for (int p = 0; p < ents.WatchedCount; p++)
                for (int k = 0; k < 16; k++)
                    Assert.AreEqual(still[p][k], ents.Watched[p][k], 1e-6f, $"piece {p} moved while paused");
        }
    }
}
