// OkSimTests.cs - the plugin, driven from C#, must agree with the C tests.
// Run: Window > General > Test Runner > EditMode > Run All, or from a
// shell with scripts/unity-test.ps1.
using NUnit.Framework;

namespace OpenKingdomsUnity.Tests
{
    public class OkSimTests
    {
        const int Soldier = OkNative.KindSoldier, Archer = OkNative.KindArcher;

        // The wall core/tests/test_ok_sim.c builds, down x = 30 with a gap.
        static void BuildWall(OkSim sim)
        {
            for (int y = 0; y < 64; y++)
                if (y < 30 || y > 33) sim.SetBlocked(30, y, true);
        }

        // play_script from the C tests, move for move.
        static OkSim PlayScript(uint seed, int ticks)
        {
            var sim = new OkSim(seed, 64, 64);
            BuildWall(sim);
            for (int i = 0; i < 10; i++) sim.Spawn(0, i % 2, 4 + i, 10 + i % 7);
            for (int i = 0; i < 10; i++) sim.Spawn(1, i % 2, 50 + i % 3, 50 + i);
            sim.Step();
            for (int i = 0; i < 10; i++) sim.Move(0, i, 50 - i, 10 + i % 5);
            for (int i = 0; i < 10; i++) sim.Move(1, 10 + i, 40 + i, 60);
            for (int t = 0; t < ticks; t++) sim.Step();
            return sim;
        }

        // play_battle from the C tests.
        static OkSim PlayBattle(out int ticks)
        {
            var sim = new OkSim(11, 64, 64);
            BuildWall(sim);
            for (int i = 0; i < 12; i++) sim.Spawn(0, i % 3 == 2 ? Archer : Soldier, 8 + i % 4, 26 + i / 4 * 2);
            for (int i = 0; i < 12; i++) sim.Spawn(1, i % 3 == 2 ? Archer : Soldier, 50 + i % 4, 28 + i / 4 * 2);
            sim.Step();
            for (int i = 0; i < 12; i++) sim.Move(0, i, 45 + i % 3, 28 + i / 3);
            var events = new OkEvent[256];
            int t;
            for (t = 0; t < 9000 && sim.Winner == -1; t++)
            {
                sim.Step();
                sim.DrainEvents(events);
            }
            ticks = t;
            return sim;
        }

        [Test]
        public void ThePluginLoadsAndSpeaksTheSameAbi()
        {
            Assert.AreEqual((uint)OkNative.AbiVersion, OkNative.ok_sim_abi_version());
        }

        [Test]
        public void TheScriptHashMatchesTheCTests()
        {
            using (var sim = PlayScript(7, 300))
                Assert.AreEqual(0xa7d1bdd26c061facUL, sim.Hash);
        }

        [Test]
        public void TheBattleEndsAndItsHashMatchesTheCTests()
        {
            using (var sim = PlayBattle(out int ticks))
            {
                Assert.AreEqual(1, sim.Winner);
                Assert.AreEqual(651, ticks);
                Assert.AreEqual(0x1431334bfd09ab0dUL, sim.Hash);
            }
        }

        [Test]
        public void AUnitWalksToItsGoalAndSaysSo()
        {
            using (var sim = new OkSim(1, 32, 32))
            {
                sim.Spawn(0, Soldier, 2, 2);
                sim.Step();
                sim.Move(0, 0, 10, 2);
                for (int t = 0; t < 200; t++) sim.Step();
                var views = new OkUnitView[4];
                Assert.AreEqual(1, sim.Snapshot(views));
                Assert.AreEqual(10 * OkNative.FixedOne, views[0].x);
                Assert.AreEqual(OkNative.UnitIdle, views[0].state);
                Assert.AreEqual(100, views[0].maxHp);
                var events = new OkEvent[8];
                Assert.AreEqual(2, sim.DrainEvents(events));
                Assert.AreEqual(OkNative.EventArrived, events[1].kind);
            }
        }

        [Test]
        public void TwoSimsFedByOneRelayAgreeEveryTurn()
        {
            using (var relay = new OkRelay())
            using (var pa = new OkPeer())
            using (var pb = new OkPeer())
            using (var a = new OkSim(3, 32, 32))
            using (var b = new OkSim(3, 32, 32))
            {
                var frame = new byte[OkNative.TurnFrameMax];
                for (int turn = 0; turn < 200; turn++)
                {
                    if (turn < 4)
                    {
                        relay.Command(0, OkCmd.Spawn(0, Soldier, 4 + turn, 4));
                        relay.Command(1, OkCmd.Spawn(1, Archer, 20 + turn, 20));
                    }
                    if (turn == 6)
                        for (int u = 0; u < 8; u += 2) relay.Command(0, OkCmd.Move(0, u, 18, 18));
                    int len = relay.CloseTurn(frame);
                    Assert.IsTrue(pa.Receive(frame, len));
                    Assert.IsTrue(pb.Receive(frame, len));
                    for (int k = 0; k < OkNative.TurnTicks; k++)
                    {
                        Assert.IsTrue(pa.Step(a));
                        Assert.IsTrue(pb.Step(b));
                    }
                    Assert.IsFalse(relay.ReportHash(0, (uint)turn, a.Hash));
                    Assert.IsFalse(relay.ReportHash(1, (uint)turn, b.Hash));
                }
                Assert.AreEqual(a.Hash, b.Hash);
                Assert.AreEqual(0u, relay.Desyncs);
                Assert.AreEqual(200u, pb.Turns);
            }
        }

        [Test]
        public void TheAiRaisesItsFirstWaveThroughLocalLockstep()
        {
            using (var sim = new OkSim(5, 64, 64))
            using (var lockstep = new OkLocalLockstep(sim))
            using (var ai = new OkAi(1, 56, 56, 2))
            {
                for (int t = 0; t < 400; t++)
                    lockstep.Tick(() => ai.Think(sim, cmd => lockstep.Send(ai.Player, cmd)));
                Assert.AreEqual(1, ai.WavesLeft);
                var views = new OkUnitView[64];
                int n = sim.Snapshot(views);
                Assert.AreEqual(4, n);
                for (int i = 0; i < n; i++) Assert.AreEqual(1, views[i].player);
            }
        }
    }
}
