// KirennaCaptures.cs - stills of Kirenna's remastered model on the real
// engine (KirennaScenes.cs). Runs only when OKU_KIRENNA_DIR names a folder,
// with the game files. OKU_KIRENNA_SCENES picks scenes, comma separated,
// "all" by default.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class KirennaCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.TearDown();
            director = null;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator CaptureKirenna()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_KIRENNA_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_KIRENNA_DIR to capture Kirenna");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            director = new TrailerDirector(dir, "ffmpeg");
            yield return director.Run(System.Environment.GetEnvironmentVariable("OKU_KIRENNA_SCENES"), director.KirennaScenes);
        }
    }
}
