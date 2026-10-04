// DestructionCaptures.cs - the destruction scenes on the real engine with
// real maps, filmed offscreen like the trailer. Runs only when
// OKU_DESTRUCTION_DIR names a folder:
//   OKU_DESTRUCTION_SCENES  scenes to run, comma separated, or "all" (default)
//   OKU_DESTRUCTION_SIZE    1080 (default) or 1440, the frame's height
//   OKU_TRAILER_FFMPEG      the ffmpeg to encode with (default: ffmpeg on PATH)
// Each shot becomes shots/<name>.mp4 with stills in review/, and each scene
// writes what it checked to checks.log. tools/trailer/destruction.sh runs
// this and makes the contact sheets.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class DestructionCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.EndWatch();
            director?.TearDown();
            director = null;
            TrailerDirector.W = 1920;
            TrailerDirector.H = 1080;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator FilmTheDestruction()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_DESTRUCTION_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_DESTRUCTION_DIR to film the destruction scenes");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            if (System.Environment.GetEnvironmentVariable("OKU_DESTRUCTION_SIZE") == "1440")
            {
                TrailerDirector.W = 2560;
                TrailerDirector.H = 1440;
            }
            string ffmpeg = System.Environment.GetEnvironmentVariable("OKU_TRAILER_FFMPEG");
            director = new TrailerDirector(dir, string.IsNullOrEmpty(ffmpeg) ? "ffmpeg" : ffmpeg) { StillsPerShot = 8 };
            yield return director.Run(System.Environment.GetEnvironmentVariable("OKU_DESTRUCTION_SCENES"), TrailerDirector.DestructionScenes);
            director.EndWatch();
            Assert.AreEqual(0, director.Failures, "checks failed, see checks.log in " + dir);
        }
    }
}
