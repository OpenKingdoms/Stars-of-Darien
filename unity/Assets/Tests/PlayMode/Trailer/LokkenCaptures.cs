// LokkenCaptures.cs - stills of Lokken's remastered model on the real engine
// (LokkenScenes.cs). Runs only when OKU_LOKKEN_DIR names a folder, with the
// game files. OKU_LOKKEN_SCENES picks scenes, comma separated, "all" by default.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class LokkenCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.TearDown();
            director = null;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator CaptureLokken()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_LOKKEN_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_LOKKEN_DIR to capture Lokken");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            director = new TrailerDirector(dir, "ffmpeg");
            yield return director.Run(System.Environment.GetEnvironmentVariable("OKU_LOKKEN_SCENES"), director.LokkenScenes);
        }
    }
}
