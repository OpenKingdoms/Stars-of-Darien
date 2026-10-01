// GameFolderScreen.cs - what a player sees when the game did not find Total
// Annihilation: Kingdoms, and what Options opens to pick another copy: what
// the game needs, a folder field with a Browse list of drives and folders,
// and why a folder will not do. A pick that holds the game is remembered.
using System;
using System.Collections.Generic;
using System.IO;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Engine
{
    public sealed class GameFolderScreen : MonoBehaviour
    {
        public const string Heading = "Find your game";
        public const string Intro = GameRoot.Title + " plays with the files of your own copy of Total Annihilation: Kingdoms and ships none of them. " +
            "Pick the folder where the game is installed, the one with data.hpi and terrain.hpi in it.";
        public const string Where = "Where it usually is: the GOG edition in C:\\GOG Games\\Total Annihilation Kingdoms, " +
            "the CD edition wherever it was installed, often C:\\Cavedog\\Kingdoms. Press Browse to look through your drives.";
        public const string Found = "This folder holds the game. Press Use this folder.";
        const int MaxRows = 400;

        public InputField Field { get; private set; }
        public Text Status { get; private set; }
        public Text Tips { get; private set; }
        public RectTransform List { get; private set; }
        public Text ListFolder { get; private set; }
        public GameObject Browser { get; private set; }
        public Button UseButton { get; private set; }
        // The folder the Browse list shows, "" for the drives.
        public string Browsing { get; private set; } = "";
        // The folders the list shows, in order, for tests.
        public readonly List<string> Rows = new List<string>();

        // Saves the pick, for every later start. Tests keep it to themselves.
        public Action<string> Keep = EngineSettings.ChooseGameDir;
        Action<string> chosen;
        Action cancelled;

        // chosen gets the folder once it holds the game and is saved.
        // cancelled null leaves no way back but Quit, for the first run.
        public static GameFolderScreen Show(string start, Action<string> chosen, Action cancelled)
        {
            var s = new GameObject("GameFolderScreen").AddComponent<GameFolderScreen>();
            s.chosen = chosen;
            s.cancelled = cancelled;
            s.Build(start);
            return s;
        }

        void Build(string start)
        {
            if (FindAnyObjectByType<Camera>() == null)
            {
                var cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.transform.SetParent(transform, false);
            }
            var canvas = UiKit.MakeCanvas("Game folder", 100).transform;
            canvas.SetParent(transform, false);
            canvas.gameObject.AddComponent<Resharpen>();
            // The same book as the main menu: vellum, bands and knots, the
            // game's name on a purple strip, the dialog under it.
            UiKit.VellumPage(canvas, "Page");
            UiKit.TitleStrip(canvas, GameRoot.Title);
            var size = MenuScreens.SizeOverride ?? new Vector2Int(Screen.width, Screen.height);
            var box = DialogLayout.ForScreen(size.x, size.y).Folder();
            string resting = cancelled != null ? "Enter uses the folder in the field, Escape cancels." : "Enter uses the folder in the field.";
            var dialog = UiKit.MakeDialog(canvas, box, Heading, resting);
            var p = dialog.transform;
            var rt = (RectTransform)p;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -DialogLayout.StripH / 2f);

            UiKit.Words(p, "Intro", box.Local(box.Note), Intro, DialogLayout.Body, HudArt.Ink, UiKit.BodyFont, TextAnchor.UpperLeft);
            Field = UiKit.VellumField(p, "Folder", box.Local(box.Field), "The game's folder, such as C:\\GOG Games\\Total Annihilation Kingdoms", DialogLayout.Body);
            Field.text = Shown(start);
            Field.onEndEdit.AddListener(_ => Check(false));
            UiKit.MakePlate(p, "Browse", box.Local(box.Plate("Browse")), ToggleBrowser, dialog, "Looks through your drives and folders");

            Status = UiKit.Words(p, "Status", box.Local(box.Status), "", DialogLayout.Body, HudArt.Ink, UiKit.BodyFont, TextAnchor.UpperLeft);

            // The lower half: where the game usually is, or the Browse list.
            var lower = box.Local(box.List);
            Tips = UiKit.Words(p, "Where", lower, Where, DialogLayout.Body, new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.6f), UiKit.BodyFont, TextAnchor.UpperLeft);
            var well = UiKit.PaintedImage(p, "Browser", lower, "trough", HudArt.TroughSize, HudArt.TroughBorder, HudArt.Trough);
            well.raycastTarget = true;
            Browser = well.gameObject;
            UiKit.MakePlate(well.transform, "Up", DialogLayout.BrowseUp, Up, dialog, "The folder above this one");
            ListFolder = UiKit.Words(well.transform, "Shown folder", UiKit.Lead(DialogLayout.BrowseFolder(lower.width), DialogLayout.Row), "", DialogLayout.Row,
                HudArt.Ink, UiKit.BodyFont, TextAnchor.MiddleLeft);
            List = UiKit.ScrollList(well.transform, "Folders", 0);
            var rows = DialogLayout.BrowseRows(lower.size);
            var view = (RectTransform)List.parent.parent;
            view.anchorMin = view.anchorMax = view.pivot = new Vector2(0, 1);
            view.anchoredPosition = new Vector2(rows.x, -rows.y);
            view.sizeDelta = rows.size;
            Browser.SetActive(false);

            UseButton = UiKit.MakePlate(p, "Use this folder", box.Local(box.Plate("Use this folder")), Use, dialog, "Takes the folder in the field", "ok.wav");
            string leave = cancelled != null ? "Cancel" : "Quit";
            UiKit.MakePlate(p, leave, box.Local(box.Plate("Leave")), Leave, dialog, cancelled != null ? "Keeps the folder you had" : "Closes the game");

            if (!string.IsNullOrWhiteSpace(start)) Check(false);
            else Say("The game did not find Total Annihilation: Kingdoms on this computer by itself.", HudArt.Ink);
        }

        // Paths as Windows writes them.
        public static string Shown(string dir) => (dir ?? "").Replace('/', '\\');

        void Say(string text, bool good) => Say(text, good ? HudArt.Verdigris : HudArt.Minium);

        void Say(string text, Color colour)
        {
            Status.text = text;
            Status.color = colour;
        }

        // Says whether the field's folder holds the game, and takes it when
        // asked to and it does.
        public bool Check(bool take)
        {
            string typed = Field.text;
            string dir = GameFolder.Resolve(typed);
            if (dir == null)
            {
                Say(GameFolder.Problem(typed), false);
                return false;
            }
            if (!string.Equals(dir, GameFolder.Clean(typed), StringComparison.OrdinalIgnoreCase))
            {
                Field.text = Shown(dir);
                Say($"Found the game in {Shown(dir)}. Press Use this folder.", true);
            }
            else Say(Found, true);
            if (!take) return true;
            Keep?.Invoke(dir);
            Debug.Log(GameRoot.Title + ": the player chose the game folder " + dir);
            var then = chosen;
            Destroy(gameObject);
            then?.Invoke(dir);
            return true;
        }

        public void Use() => Check(true);

        public void Leave()
        {
            if (cancelled == null)
            {
                Debug.Log(GameRoot.Title + ": quit from the game folder screen");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }
            var then = cancelled;
            Destroy(gameObject);
            then();
        }

        void ToggleBrowser()
        {
            bool on = !Browser.activeSelf;
            Browser.SetActive(on);
            Tips.gameObject.SetActive(!on);
            if (!on) return;
            string from = GameFolder.Clean(Field.text);
            Open(!string.IsNullOrEmpty(from) && Directory.Exists(from) ? from : "");
        }

        void Up()
        {
            if (Browsing.Length == 0) return;
            string parent = null;
            try { parent = Path.GetDirectoryName(Browsing); }
            catch (ArgumentException) { }
            Open(string.IsNullOrEmpty(parent) ? "" : GameFolder.Clean(parent));
        }

        // Lists the drives, or the folders in dir, the game's marked.
        public void Open(string dir)
        {
            var folders = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(dir)) folders.AddRange(Directory.GetLogicalDrives());
                else
                {
                    foreach (var d in Directory.GetDirectories(dir))
                    {
                        string name = Path.GetFileName(d);
                        if (name.StartsWith("$") || name.StartsWith(".") || name == "System Volume Information") continue;
                        folders.Add(d);
                    }
                    folders.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Say($"{dir} could not be opened ({e.Message}).", false);
                return;
            }
            Browsing = dir ?? "";
            ListFolder.text = Browsing.Length == 0 ? "Your drives" : Shown(Browsing);
            if (Browsing.Length > 0)
            {
                Field.text = Shown(Browsing);
                if (GameFolder.Holds(Browsing)) Say(Found, true);
                else Say("Open the folder the game is in. A folder that holds it says so in the list.", HudArt.Ink);
            }
            for (int i = List.childCount - 1; i >= 0; i--) Destroy(List.GetChild(i).gameObject);
            Rows.Clear();
            for (int i = 0; i < folders.Count && i < MaxRows; i++)
            {
                string path = GameFolder.Clean(folders[i]);
                bool game = Browsing.Length > 0 && GameFolder.Holds(path);
                string label = Browsing.Length == 0 ? Shown(path) : Path.GetFileName(path.TrimEnd('/'));
                UiKit.ListRow(List, "Folder " + label, game ? label + "    (the game is here)" : label, DialogLayout.ListRowH, DialogLayout.Row,
                    game ? HudArt.Verdigris : HudArt.Ink, () => Open(path));
                Rows.Add(path);
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Use();
            else if (Input.GetKeyDown(KeyCode.Escape) && cancelled != null) Leave();
        }
    }
}
