// ThirshaCaptures.cs - stills of Thirsha's remastered model on the real
// engine (ThirshaScenes.cs). Runs only when OKU_THIRSHA_DIR names a folder,
// with the game files. OKU_THIRSHA_SCENES picks scenes, comma separated,
// "all" by default.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class ThirshaCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.TearDown();
            director = null;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator CaptureThirsha()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_THIRSHA_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_THIRSHA_DIR to capture Thirsha");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            director = new TrailerDirector(dir, "ffmpeg");
            yield return director.Run(System.Environment.GetEnvironmentVariable("OKU_THIRSHA_SCENES"), director.ThirshaScenes);
        }
    }
}
