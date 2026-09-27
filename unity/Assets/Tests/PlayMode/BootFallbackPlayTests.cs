// BootFallbackPlayTests.cs - an engine that fails to start leaves the game
// on the mock engine with its menus up and the reason on the main menu.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class BootFallbackPlayTests
    {
        [UnityTest]
        public IEnumerator AFailedEngineFallsBackAndSaysWhy()
        {
            var saved = GameRoot.BackendFactory;
            GameRoot.BackendFactory = () => throw new System.InvalidOperationException("okengine API 16, this binding expects 17");
            GameRoot root = null;
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("okengine API 16"));
                root = GameRoot.Boot();
                yield return null;
                yield return null;
                Assert.IsInstanceOf<MockBackend>(root.Backend);
                StringAssert.Contains("okengine API 16", root.BackendProblem);
                Assert.AreEqual("Menu", root.Screens.Visible, "the main menu is up");
                bool shown = false;
                foreach (var t in root.Screens.Screen("Menu").GetComponentsInChildren<Text>())
                    shown |= t.text.Contains("okengine API 16") && t.text.Contains("Mock engine");
                Assert.IsTrue(shown, "the menu says the mock runs, and why");
            }
            finally
            {
                GameRoot.BackendFactory = saved;
                if (root != null) Object.Destroy(root.gameObject);
            }
        }
    }
}
