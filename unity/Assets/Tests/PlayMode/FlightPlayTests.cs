// FlightPlayTests.cs - the mock's flyer in a running game: its wings are
// posed by the flight animator rather than the script, it both flaps and
// glides on a long flight, its selection ring rides with it, a flyer out
// of view is stepped but not posed, a paused game draws it the same way
// frame after frame, and an attack in the air plays the script's own pose.
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

        IEnumerator Boot(MockBackend mock)
        {
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f, "the game to load");
        }

        static UnitState Find(EntityRenderer ents, int handle)
        {
            for (int i = 0; i < ents.UnitCount; i++) if (ents.Units[i].Handle == handle) return ents.Units[i];
            return default;
        }

        // The local player's flyer.
        static int Flyer(MockBackend mock, EntityRenderer ents, out uint id, out int model)
        {
            id = 0;
            model = -1;
            for (int i = 0; i < ents.UnitCount; i++)
                if (ents.Units[i].Player == mock.LocalPlayer && mock.UnitDefs[ents.Units[i].Def].CanFly)
                {
                    id = ents.Units[i].StableId;
                    model = ents.Units[i].Model;
                    return ents.Units[i].Handle;
                }
            return -1;
        }

        void Follow(Vector3 at) => root.World.Camera.focus = new Vector3(at.x, root.World.Camera.focus.y, at.z);

        [UnityTest]
        public IEnumerator AMockFlyerFlapsAndGlides()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock);
            var ents = root.World.Entities;
            int handle = Flyer(mock, ents, out uint id, out int model);
            Assert.GreaterOrEqual(handle, 0, "the player has a flyer");
            var data = mock.GetModel(model);
            int body = PieceIndex(data, "body"), wing = PieceIndex(data, "wingl1");
            ents.Watch = handle;
            mock.Select(new[] { handle }, false);
            ents.Selected.Add(handle);

            // Back and forth across the map, twice as fast as normal, in view.
            var ends = new[] { new Vector3(130, 0, -24), new Vector3(30, 0, -100) };
            int leg = 0;
            mock.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
            Time.timeScale = 2f;
            bool flapped = false, glided = false, differs = false, ringRode = false;
            var poses = new PiecePose[32];
            float end = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                var u = Find(ents, handle);
                Follow(u.Position);
                if (new Vector2(u.Position.x - ends[leg].x, u.Position.z - ends[leg].z).magnitude < 3f)
                {
                    leg = 1 - leg;
                    mock.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
                }
                if (!ents.TryFlight(id, out var f)) continue;
                flapped |= f.Mode == FlightMode.Flap && f.Weight >= 1f;
                glided |= f.Mode == FlightMode.Glide;
                float lift = ents.VisualLift(handle);
                if (Mathf.Abs(lift) > 0.05f && ents.Selected.Contains(handle))
                {
                    Assert.AreEqual(u.Position.y + 0.05f + lift, ents.WatchedRing.y, 1e-3f, "the ring rides with the drawn flyer");
                    ringRode = true;
                }
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
            Assert.IsTrue(ringRode, "the selected flyer's ring was seen off its engine height");

            // Out of view the animator runs on but nothing is posed.
            var cam = root.World.Camera;
            cam.enabled = false;
            cam.transform.rotation = Quaternion.LookRotation(Vector3.up);
            yield return null;
            ents.TryFlight(id, out var before);
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                Assert.AreEqual(0, ents.FlyersPosed, "no flyer is posed out of view");
            }
            ents.TryFlight(id, out var after);
            Assert.AreNotEqual(before.Phase, after.Phase, "its beat runs on");
            cam.enabled = true;
            yield return null;
            yield return null;
            Assert.Greater(ents.FlyersPosed, 0, "back in view it is posed");

            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            yield return null;
            var still = (Matrix4x4[])ents.Watched.Clone();
            for (int i = 0; i < 5; i++) yield return null;
            for (int p = 0; p < ents.WatchedCount; p++)
                for (int k = 0; k < 16; k++)
                    Assert.AreEqual(still[p][k], ents.Watched[p][k], 1e-6f, $"piece {p} moved while paused");
        }

        // A flyer fighting from the air shows the script's attack, so what
        // it hits comes out of its drawn pose.
        [UnityTest]
        public IEnumerator AnAttackInTheAirPlaysTheScript()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Boot(mock);
            var ents = root.World.Entities;
            int handle = Flyer(mock, ents, out uint id, out int model);
            var data = mock.GetModel(model);
            int body = PieceIndex(data, "body"), wing = PieceIndex(data, "wingl1");
            var home = Find(ents, handle).Position;
            int target = -1;
            float best = float.MaxValue;
            for (int i = 0; i < ents.UnitCount; i++)
            {
                var u = ents.Units[i];
                if (u.Handle == handle || u.Player != mock.LocalPlayer || mock.UnitDefs[u.Def].IsBuilding) continue;
                float d = (u.Position - home).sqrMagnitude;
                if (d < best) { best = d; target = u.Handle; }
            }
            Assert.GreaterOrEqual(target, 0);
            ents.Watch = handle;
            Assert.IsTrue(mock.Command(new GameCommand { Kind = CommandKind.Attack, Unit = handle, TargetUnit = target, BuildDef = -1 }));
            var poses = new PiecePose[32];
            bool shown = false;
            float end = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < end && !shown)
            {
                yield return null;
                var u = Find(ents, handle);
                Follow(u.Position);
                if (!ents.TryFlight(id, out var f) || f.Mode != FlightMode.Attack || f.Weight > 0f || u.Altitude <= 1f) continue;
                if (ents.WatchedCount <= wing || mock.ReadUnitPose(handle, poses) <= wing) continue;
                var script = poses[body].Matrix.inverse * poses[wing].Matrix;
                var drawn = ents.Watched[body].inverse * ents.Watched[wing];
                Assert.Less(Quaternion.Angle(script.rotation, drawn.rotation), 0.1f, "the drawn wing is the attack's");
                shown = true;
            }
            Assert.IsTrue(shown, "the flyer attacked from the air");
        }
    }
}
