// SageCaptures.cs - stills of the Great Sage's remastered model on the real
// engine (SageScenes.cs). Runs only when OKU_SAGE_DIR names a folder, with
// the game files. OKU_SAGE_SCENES picks scenes, comma separated, "all" by
// default.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class SageCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.TearDown();
            director = null;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator CaptureSage()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_SAGE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_SAGE_DIR to capture the Great Sage");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            director = new TrailerDirector(dir, "ffmpeg");
            yield return director.Run(System.Environment.GetEnvironmentVariable("OKU_SAGE_SCENES"), director.SageScenes);
        }
    }
}
