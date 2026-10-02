// FreeForAllPlayTests.cs - eight kingdoms, each alone, on the biggest
// eight-player map through the real engine: no two are allies as the
// backend reads them. Skipped without the game files. With
// OKU_FFA_CAPTURE_DIR set it also saves the skirmish page as it stood.
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FreeForAllPlayTests
    {
        [TearDown]
        public void CleanUp()
        {
            MenuScreens.SizeOverride = null;
            FadeIn.Off = false;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator EightKingdomsAloneAreNobodysAllies()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            string dir = System.Environment.GetEnvironmentVariable("OKU_FFA_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                MenuScreens.SizeOverride = new Vector2Int(1920, 1080);
                FadeIn.Off = true;
            }
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            var root = GameRoot.Boot();
            try
            {
                yield return null;
                var b = root.Backend;
                var map = b.Maps.Where(m => MapCatalog.PlayersOf(m) >= 8).OrderByDescending(m => m.Size.x * m.Size.y).FirstOrDefault();
                Assert.IsNotNull(map, "an eight-player map");
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = map.Id;
                root.Screens.Show(FlowState.Skirmish);
                var seats = root.Setup.Seats;
                Assert.AreEqual(LobbyScreens.SeatRows, seats.Count, "the page shows eight rows");
                for (int i = 0; i < seats.Count; i++)
                {
                    if (i > 0) seats[i].Kind = SeatKind.Computer;
                    seats[i].Team = SeatTeam.Alone;
                }
                root.Screens.Lobby.Skirmish.Refresh();
                yield return null;
                Assert.IsNull(SkirmishCheck.WhyNot(seats, map), "eight kingdoms alone can play");
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    yield return LobbyCaptures.Shoot(root, 1920, 1080, Path.Combine(dir, "skirmish-eight-alone.png"));
                }

                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 300f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
                for (int i = 0; i < 30; i++) yield return null;

                var ps = b.Players;
                Assert.AreEqual(8, ps.Count, "eight kingdoms");
                for (int a = 0; a < ps.Count; a++)
                    for (int c = 0; c < ps.Count; c++)
                        if (a != c) Assert.IsFalse(b.Allied(ps[a].Index, ps[c].Index), $"players {ps[a].Index} and {ps[c].Index} are allies");
                foreach (var p in ps) Assert.AreEqual(SeatTeam.Alone, p.Team, $"player {p.Index} reads back alone");
                Assert.AreEqual(GameStatus.Running, b.Status, "the battle goes on");
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
