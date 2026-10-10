// ShaderWaitPlayTests.cs - in the editor a battle waits behind the loading
// screen while shaders compile, so its first frames never show placeholders.
#if UNITY_EDITOR
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class ShaderWaitPlayTests
    {
        GameRoot root;
        string saves;
        System.Func<bool> compiling;
        float cap;

        [SetUp]
        public void SetUp()
        {
            saves = TempSaves.Use();
            compiling = GameRoot.ShadersCompiling;
            cap = GameRoot.ShaderWaitCap;
        }

        [TearDown]
        public void TearDown()
        {
            GameRoot.ShadersCompiling = compiling;
            GameRoot.ShaderWaitCap = cap;
            if (root != null) Object.Destroy(root.gameObject);
            TempSaves.Drop(saves);
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

        IEnumerator StartBattle()
        {
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0.05f, DamageScale = 0f });
            yield return null;
            yield return null;
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            root.Setup.MapId = "mock_isles";
            root.Screens.StartGame();
            Assert.AreEqual(FlowState.Loading, root.Flow.State);
        }

        [UnityTest]
        public IEnumerator TheLoadingScreenStaysUpWhileShadersCompile()
        {
            bool busy = true;
            int asked = 0;
            GameRoot.ShadersCompiling = () => { asked++; return busy; };
            yield return StartBattle();
            yield return Until(() => root.World != null && root.World.StepsLeft == 0 || root.Flow.State != FlowState.Loading, 30f, "the world to build");
            for (int i = 0; i < 20; i++) yield return null;

            Assert.AreEqual(FlowState.Loading, root.Flow.State, "the battle waits for its shaders");
            Assert.IsTrue(root.Screens.Screen("Loading").activeInHierarchy, "behind the loading screen");
            Assert.AreEqual(GameRoot.ShaderStage, root.Loading.Stage);
            Assert.Greater(asked, 0, "the editor was asked");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Greater(root.World.Entities.Drawn, 0, "the world draws behind the screen, so its shaders are asked for");

            busy = false;
            yield return Until(() => root.Flow.State == FlowState.Playing, 5f, "the battle once the shaders are done");
            Assert.AreEqual("Hud", root.Screens.Visible);
        }

        [UnityTest]
        public IEnumerator ACompileThatNeverEndsHoldsTheBattleOnlySoLong()
        {
            GameRoot.ShadersCompiling = () => true;
            GameRoot.ShaderWaitCap = 0.5f;
            LogAssert.Expect(LogType.Warning, new Regex("shaders"));
            yield return StartBattle();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f, "the battle after the wait's cap");
        }
    }
}
#endif
