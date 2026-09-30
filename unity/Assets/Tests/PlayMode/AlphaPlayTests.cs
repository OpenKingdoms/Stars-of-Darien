// AlphaPlayTests.cs - the alpha's menu (its version in the corner, the
// closed doors marked for later), the game folder screen a player sees when
// the game did not find Total Annihilation: Kingdoms, and the Options row
// that opens it again.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Tests
{
    public class AlphaPlayTests
    {
        GameRoot root;
        GameFolderScreen screen;
        string temp;

        [SetUp]
        public void Before()
        {
            temp = Path.Combine(Path.GetTempPath(), "oku-alpha-play-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);
        }

        [TearDown]
        public void After()
        {
            BuildStamp.Reset();
            GameRoot.GameFolder = null;
            GameRoot.ChangeGameFolder = null;
            MenuScreens.SizeOverride = null;
            FadeIn.Off = false;
            if (root != null) Object.Destroy(root.gameObject);
            if (screen != null) Object.Destroy(screen.gameObject);
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        IEnumerator Boot()
        {
            MenuScreens.SizeOverride = new Vector2Int(1920, 1080);
            FadeIn.Off = true;
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, ArtDir = null });
            yield return null;
        }

        static Text Named(GameObject under, string name) =>
            under.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == name);

        [UnityTest]
        public IEnumerator TheAlphaMenuShowsItsVersionAndClosesMultiplayer()
        {
            BuildStamp.Version = "Alpha 1 (test)";
            BuildStamp.SkirmishOnly = true;
            yield return Boot();
            var menu = root.Screens.Screen("Menu");
            var stamp = Named(menu, "Build version");
            Assert.IsNotNull(stamp, "the version is on the menu");
            Assert.AreEqual("Alpha 1 (test)", stamp.text);
            Assert.IsTrue(stamp.gameObject.activeInHierarchy);
            var corners = new Vector3[4];
            stamp.rectTransform.GetWorldCorners(corners);
            var box = new Vector3[4];
            ((RectTransform)root.Screens.Canvas.transform).GetWorldCorners(box);
            Assert.Greater(corners[2].x, box[0].x + (box[2].x - box[0].x) * 0.8f, "in the right corner");
            Assert.Less(corners[0].y, box[0].y + (box[2].y - box[0].y) * 0.1f, "at the bottom");
            Assert.AreEqual(2, menu.GetComponentsInChildren<Text>(true).Count(t => t.name == "Coming later"));

            var page = root.Screens.Lobby.Menu;
            page.Doors[2].Press();
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State, "multiplayer stays closed");
            Assert.AreEqual(LobbyScreens.MenuPage.MultiplayerLater, page.Page.Help.text);
            Assert.IsTrue(menu.GetComponentsInChildren<Image>(true).Where(i => i.name == "Coming later plaque").All(i => !i.raycastTarget), "clicks reach the doors");
            page.Doors[0].Press();
            Assert.AreEqual(FlowState.Skirmish, root.Flow.State, "skirmish opens");
        }

        [UnityTest]
        public IEnumerator TheEditorMenuHasNoStampAndMultiplayerOpens()
        {
            BuildStamp.Reset();
            yield return Boot();
            var menu = root.Screens.Screen("Menu");
            Assert.IsNull(Named(menu, "Build version"));
            Assert.AreEqual(0, menu.GetComponentsInChildren<Text>(true).Count(t => t.name == "Coming later"));
            root.Screens.Lobby.Menu.Doors[2].Press();
            Assert.AreEqual(FlowState.Multiplayer, root.Flow.State);
        }

        string Folder(string rel, params string[] files)
        {
            string dir = Path.Combine(temp, rel);
            Directory.CreateDirectory(dir);
            foreach (var f in files) File.WriteAllBytes(Path.Combine(dir, f), new byte[] { 1 });
            return GameFolder.Clean(dir);
        }

        [UnityTest]
        public IEnumerator TheFolderScreenExplainsAndTakesAGoodFolder()
        {
            string empty = Folder("Empty");
            string game = Folder("GOG Games/Total Annihilation Kingdoms", "data.hpi", "terrain.hpi");
            string chosen = null, kept = null;
            bool cancelled = false;
            screen = GameFolderScreen.Show(empty, d => chosen = d, () => cancelled = true);
            screen.Keep = d => kept = d;
            yield return null;
            StringAssert.Contains("no game archives", screen.Status.text);
            Assert.IsNotNull(screen.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Cancel"));

            screen.Field.text = Folder("Half", "data.hpi");
            screen.Use();
            StringAssert.Contains("terrain.hpi", screen.Status.text);
            Assert.IsNull(kept);

            // The folder above the game is enough: the game is found in it.
            screen.Field.text = Path.Combine(temp, "GOG Games");
            screen.Use();
            Assert.AreEqual(game, kept);
            Assert.AreEqual(game, chosen);
            Assert.IsFalse(cancelled);
            yield return null;
            Assert.IsTrue(screen == null, "the screen closes");
        }

        [UnityTest]
        public IEnumerator TheFirstRunScreenOffersQuitAndBrowse()
        {
            string game = Folder("Games/TAK", "DATA.HPI", "TERRAIN.HPI");
            Folder("Games/Other");
            screen = GameFolderScreen.Show("", d => { }, null);
            screen.Keep = d => { };
            yield return null;
            StringAssert.Contains("did not find", screen.Status.text);
            Assert.IsNotNull(screen.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Quit"));
            Assert.IsNull(screen.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Cancel"));

            screen.Open("");
            Assert.Greater(screen.Rows.Count, 0, "the drives are listed");
            screen.Open(GameFolder.Clean(Path.Combine(temp, "Games")));
            yield return null;
            CollectionAssert.AreEqual(new[] { GameFolder.Clean(Path.Combine(temp, "Games/Other")), game }, screen.Rows);
            var marked = screen.List.GetComponentsInChildren<Text>().Where(t => t.text.Contains("the game is here")).ToList();
            Assert.AreEqual(1, marked.Count);
            StringAssert.StartsWith("TAK", marked[0].text);
            screen.Open(game);
            Assert.AreEqual(game, GameFolder.Clean(screen.Field.text));
            Assert.AreEqual(GameFolderScreen.Found, screen.Status.text);
        }

        [UnityTest]
        public IEnumerator OptionsShowsTheGameFolderAndChangesIt()
        {
            int asked = 0;
            GameRoot.GameFolder = () => "C:/Games/TAK";
            GameRoot.ChangeGameFolder = () => asked++;
            yield return Boot();
            root.Flow.Fire(FlowEvent.OpenOptions);
            yield return null;
            var options = root.Screens.Screen("Options");
            Assert.AreEqual("C:/Games/TAK", Named(options, "Game folder path").text);
            var change = options.GetComponentsInChildren<Button>(true).First(b => b.name == "Change game folder");
            change.onClick.Invoke();
            Assert.AreEqual(1, asked);
        }

        [UnityTest]
        public IEnumerator TheEditorsOptionsHaveNoGameFolderRow()
        {
            yield return Boot();
            var options = root.Screens.Screen("Options");
            Assert.IsFalse(options.GetComponentsInChildren<Button>(true).Any(b => b.name == "Change game folder"));
        }
    }
}
