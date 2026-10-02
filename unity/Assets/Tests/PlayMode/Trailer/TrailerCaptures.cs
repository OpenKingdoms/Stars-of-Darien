// TrailerCaptures.cs - the trailer's entry point. Runs only when
// OKU_TRAILER_DIR names a folder, on the real engine with the game files:
//   OKU_TRAILER_SCENES  scenes to film, comma separated, or "all" (default)
//   OKU_TRAILER_FFMPEG  the ffmpeg to encode with (default: ffmpeg on PATH)
// Each shot becomes shots/<name>.mp4 and shots/<name>.sounds.tsv, with a
// few frames of it in review/. tools/trailer/render.sh runs this and
// tools/trailer/assemble.py cuts the trailer from the shots.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class TrailerCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.TearDown();
            director = null;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator FilmTheTrailer()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_TRAILER_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_TRAILER_DIR to film the trailer");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            string ffmpeg = System.Environment.GetEnvironmentVariable("OKU_TRAILER_FFMPEG");
            director = new TrailerDirector(dir, string.IsNullOrEmpty(ffmpeg) ? "ffmpeg" : ffmpeg);
            yield return director.Run(System.Environment.GetEnvironmentVariable("OKU_TRAILER_SCENES"));
        }
    }
}
