// GameFlowTests.cs - the screen flow goes where it should and refuses
// what makes no sense in the state it is in.
using NUnit.Framework;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Tests
{
    public class GameFlowTests
    {
        [Test]
        public void AGameGoesFromTheMenuToVictoryAndBack()
        {
            var f = new GameFlow();
            int changes = 0;
            f.Changed += (a, b) => changes++;
            Assert.AreEqual(FlowState.MainMenu, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.OpenSkirmish));
            Assert.IsTrue(f.Fire(FlowEvent.Start));
            Assert.AreEqual(FlowState.Loading, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.Loaded));
            Assert.AreEqual(FlowState.Playing, f.State);
            Assert.IsTrue(f.InGameNow);
            Assert.IsTrue(f.Fire(FlowEvent.Won));
            Assert.AreEqual(FlowState.Victory, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.ToMenu));
            Assert.AreEqual(FlowState.MainMenu, f.State);
            Assert.AreEqual(5, changes);
        }

        [Test]
        public void PauseResumesAndQuitsToTheMenu()
        {
            var f = new GameFlow();
            f.Fire(FlowEvent.OpenSkirmish); f.Fire(FlowEvent.Start); f.Fire(FlowEvent.Loaded);
            Assert.IsTrue(f.Fire(FlowEvent.Pause));
            Assert.IsTrue(f.Fire(FlowEvent.Resume));
            Assert.AreEqual(FlowState.Playing, f.State);
            f.Fire(FlowEvent.Pause);
            Assert.IsTrue(f.Fire(FlowEvent.ToMenu));
            Assert.AreEqual(FlowState.MainMenu, f.State);
        }

        [Test]
        public void OptionsReturnsWhereItWasOpened()
        {
            var f = new GameFlow();
            f.Fire(FlowEvent.OpenOptions);
            Assert.IsFalse(f.InGameNow);
            f.Fire(FlowEvent.Back);
            Assert.AreEqual(FlowState.MainMenu, f.State);

            f.Fire(FlowEvent.OpenSkirmish); f.Fire(FlowEvent.Start); f.Fire(FlowEvent.Loaded); f.Fire(FlowEvent.Pause);
            Assert.IsTrue(f.Fire(FlowEvent.OpenOptions));
            Assert.IsTrue(f.InGameNow, "the game stays loaded under options opened from pause");
            f.Fire(FlowEvent.Back);
            Assert.AreEqual(FlowState.Paused, f.State);
        }

        [Test]
        public void AFailedLoadGoesBackToSetup()
        {
            var f = new GameFlow();
            f.Fire(FlowEvent.OpenSkirmish); f.Fire(FlowEvent.Start);
            Assert.IsTrue(f.Fire(FlowEvent.LoadFailed));
            Assert.AreEqual(FlowState.Skirmish, f.State);
        }

        [Test]
        public void ASavedGameLoadsFromTheSkirmishAndAFailedLoadReturnsThere()
        {
            var f = new GameFlow();
            Assert.IsFalse(f.Fire(FlowEvent.OpenLoad), "the menu has no saved games");
            f.Fire(FlowEvent.OpenSkirmish);
            Assert.IsTrue(f.Fire(FlowEvent.OpenLoad));
            Assert.AreEqual(FlowState.LoadList, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.Start));
            Assert.AreEqual(FlowState.Loading, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.LoadFailed));
            Assert.AreEqual(FlowState.LoadList, f.State, "back to the saves, not to skirmish setup");
            f.Fire(FlowEvent.Start);
            Assert.IsTrue(f.Fire(FlowEvent.Loaded));
            Assert.AreEqual(FlowState.Playing, f.State);
            f.Fire(FlowEvent.Pause);
            f.Fire(FlowEvent.ToMenu);
            f.Fire(FlowEvent.OpenSkirmish);
            f.Fire(FlowEvent.OpenLoad);
            Assert.IsTrue(f.Fire(FlowEvent.Back));
            Assert.AreEqual(FlowState.Skirmish, f.State, "back to the skirmish it was opened from");
        }

        [Test]
        public void ABuildWithoutTheMapEditorNeverOpensIt()
        {
            try
            {
                BuildStamp.MapEditor = false;
                var f = new GameFlow();
                Assert.IsFalse(f.Fire(FlowEvent.OpenEditor));
                Assert.AreEqual(FlowState.MainMenu, f.State);
            }
            finally { BuildStamp.Reset(); }
        }

        [Test]
        public void TheMapEditorLoadsIntoEditingAndLeavesToTheMenu()
        {
            var f = new GameFlow();
            Assert.IsTrue(f.Fire(FlowEvent.OpenEditor));
            Assert.IsTrue(f.Fire(FlowEvent.Start));
            Assert.IsTrue(f.Fire(FlowEvent.Loaded));
            Assert.AreEqual(FlowState.Editing, f.State, "an editor load opens the editor, not a battle");
            Assert.IsTrue(f.InGameNow);
            Assert.IsFalse(f.Fire(FlowEvent.Won), "nobody wins in the editor");
            Assert.IsTrue(f.Fire(FlowEvent.ToMenu));
            f.Fire(FlowEvent.OpenSkirmish); f.Fire(FlowEvent.Start); f.Fire(FlowEvent.Loaded);
            Assert.AreEqual(FlowState.Playing, f.State, "a skirmish load still plays");
        }

        [Test]
        public void NonsenseEventsAreRefused()
        {
            var f = new GameFlow();
            Assert.IsFalse(f.Fire(FlowEvent.Loaded));
            Assert.IsFalse(f.Fire(FlowEvent.Won));
            Assert.IsFalse(f.Fire(FlowEvent.Resume));
            Assert.IsFalse(f.CanFire(FlowEvent.Start));
            Assert.AreEqual(FlowState.MainMenu, f.State);
            f.Fire(FlowEvent.OpenSkirmish); f.Fire(FlowEvent.Start);
            Assert.IsFalse(f.Fire(FlowEvent.Pause), "no pausing a load");
            Assert.IsFalse(f.Fire(FlowEvent.Back), "no backing out of a load half way");
        }

        [Test]
        public void ARoomOpensTheMapChoiceAndStartsABattle()
        {
            var f = new GameFlow();
            Assert.IsTrue(f.Fire(FlowEvent.OpenMultiplayer));
            Assert.AreEqual(FlowState.Multiplayer, f.State);
            Assert.IsFalse(f.Fire(FlowEvent.Start), "no battle without a room");
            Assert.IsTrue(f.Fire(FlowEvent.EnterRoom));
            Assert.IsTrue(f.Fire(FlowEvent.ChooseMap));
            Assert.AreEqual(FlowState.MapChoice, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.Back));
            Assert.AreEqual(FlowState.Room, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.Start));
            Assert.IsTrue(f.Fire(FlowEvent.LoadFailed));
            Assert.AreEqual(FlowState.Room, f.State, "a failed load returns to the room");
            Assert.IsTrue(f.Fire(FlowEvent.Back));
            Assert.IsTrue(f.Fire(FlowEvent.Back));
            Assert.AreEqual(FlowState.MainMenu, f.State);
        }

        [Test]
        public void TheSkirmishOpensOptionsAndSavedGamesAndComesBack()
        {
            var f = new GameFlow();
            f.Fire(FlowEvent.OpenSkirmish);
            Assert.IsTrue(f.Fire(FlowEvent.OpenOptions));
            Assert.IsTrue(f.Fire(FlowEvent.Back));
            Assert.AreEqual(FlowState.Skirmish, f.State);
            Assert.IsTrue(f.Fire(FlowEvent.OpenLoad));
            Assert.IsTrue(f.Fire(FlowEvent.Back));
            Assert.AreEqual(FlowState.Skirmish, f.State);
            f.Fire(FlowEvent.Back);
            f.Fire(FlowEvent.OpenLoad);
            f.Fire(FlowEvent.Back);
            Assert.AreEqual(FlowState.MainMenu, f.State);
        }
    }
}
