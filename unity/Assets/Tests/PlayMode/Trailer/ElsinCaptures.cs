// ElsinCaptures.cs - stills of Elsin's remastered model on the real engine
// (ElsinScenes.cs). Runs only when OKU_ELSIN_DIR names a folder, with the
// game files; OKU_ELSIN_SCENES picks scenes, comma separated, "all" by default.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class ElsinCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.TearDown();
            director = null;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator CaptureElsin()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_ELSIN_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_ELSIN_DIR to capture Elsin");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            director = new TrailerDirector(dir, "ffmpeg");
            yield return director.Run(System.Environment.GetEnvironmentVariable("OKU_ELSIN_SCENES"), director.ElsinScenes);
        }
    }
}
