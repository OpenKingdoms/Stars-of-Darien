// EngineNoticePlayTests.cs - Play in a scene without the game, when the
// engine cannot run, says why and what to do, and starts nothing else.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class EngineNoticePlayTests
    {
        static void Clear()
        {
            foreach (var d in Object.FindObjectsByType<EngineDriver>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
            foreach (var n in Object.FindObjectsByType<EngineNotice>(FindObjectsSortMode.None)) Object.DestroyImmediate(n.gameObject);
        }

        [UnityTest]
        public IEnumerator AnEngineThatCannotRunShowsWhyInstead()
        {
            // The bootstrap already ran at Play, with whatever this machine has.
            Clear();
            string saved = EngineSettings.Blocked;
            const string why = "Unity still has the old engine loaded. Restart Unity to use the new one.";
            EngineSettings.Blocked = why;
            try
            {
                Assert.AreEqual(why, EngineSettings.Problem);
                GameBootstrap.BootScene();
                yield return null;
                Assert.IsNull(Object.FindAnyObjectByType<EngineDriver>(), "no engine starts");
                var notice = Object.FindAnyObjectByType<EngineNotice>();
                Assert.IsNotNull(notice, "the notice is up");
                bool heading = false, said = false;
                foreach (var t in notice.GetComponentsInChildren<Text>())
                {
                    heading |= t.text == EngineNotice.Heading;
                    said |= t.text == why && t.isActiveAndEnabled;
                }
                Assert.IsTrue(heading, "it says the engine is not running");
                Assert.IsTrue(said, "and why, with what to do");
                GameBootstrap.BootScene();
                Assert.AreEqual(1, Object.FindObjectsByType<EngineNotice>(FindObjectsSortMode.None).Length, "a second boot keeps the one notice");
            }
            finally
            {
                EngineSettings.Blocked = saved;
                Clear();
            }
        }
    }
}
