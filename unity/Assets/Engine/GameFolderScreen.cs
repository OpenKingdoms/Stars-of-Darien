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

        static readonly Color Bad = new Color(0.95f, 0.55f, 0.42f);

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
            var stone = UiKit.Picture(canvas, "Stone", UiKit.Stone, new Color(0.8f, 0.78f, 0.74f));
            stone.rectTransform.Fill();
            stone.raycastTarget = true;
            var glow = UiKit.Picture(canvas, "Glow", UiKit.Glow, new Color(1f, 0.72f, 0.35f, 0.22f));
            glow.rectTransform.Place(0.1f, -0.2f, 0.9f, 1.1f);
            glow.raycastTarget = false;
            UiKit.Label(canvas, GameRoot.Title, 64, UiKit.Gold, TextAnchor.MiddleCenter, true).rectTransform.Place(0, 0.89f, 1, 0.98f);

            var p = UiKit.Panel(canvas, "Panel", false).Place(0.5f, 0.5f, 0.5f, 0.5f, -640, -470, -640, -390);
            UiKit.Label(p, Heading, 52, UiKit.Gold, TextAnchor.MiddleCenter, true).rectTransform.Place(0, 1, 1, 1, 40, -110, 40, 24);
            var intro = UiKit.Label(p, Intro, 27, UiKit.Pale, TextAnchor.UpperLeft);
            intro.rectTransform.Place(0, 1, 1, 1, 60, -230, 60, 120);

            Field = UiKit.Input(p, "The game's folder, such as C:\\GOG Games\\Total Annihilation Kingdoms", 26);
            Field.name = "Folder";
            Field.GetComponent<RectTransform>().Place(0, 1, 1, 1, 60, -310, 280, 240);
            Field.text = Shown(start);
            Field.onEndEdit.AddListener(_ => Check(false));
            var browse = UiKit.MakeButton(p, "Browse", ToggleBrowser, 28);
            browse.name = "Browse";
            browse.GetComponent<RectTransform>().Place(1, 1, 1, 1, -250, -310, 60, 240);

            Status = UiKit.Label(p, "", 24, UiKit.Pale, TextAnchor.UpperLeft);
            Status.name = "Status";
            Status.rectTransform.Place(0, 1, 1, 1, 60, -400, 60, 322);

            // The lower half: where the game usually is, or the Browse list.
            Tips = UiKit.Label(p, Where, 24, UiKit.Dim, TextAnchor.UpperLeft);
            Tips.name = "Where";
            Tips.rectTransform.Place(0, 0, 1, 1, 60, 140, 60, 420);
            var box = UiKit.Picture(p, "Browser", UiKit.White, new Color(0.06f, 0.045f, 0.03f, 0.8f));
            Browser = box.gameObject;
            box.rectTransform.Place(0, 0, 1, 1, 60, 130, 60, 410);
            var up = UiKit.MakeButton(box.transform, "Up", Up, 24);
            up.name = "Up";
            up.GetComponent<RectTransform>().Place(0, 1, 0, 1, 12, -62, -152, 10);
            ListFolder = UiKit.Label(box.transform, "", 22, UiKit.Gold, TextAnchor.MiddleLeft);
            ListFolder.rectTransform.Place(0, 1, 1, 1, 180, -62, 16, 10);
            List = UiKit.ScrollList(box.transform, "Folders", 4);
            ((RectTransform)List.parent.parent).Place(0, 0, 1, 1, 12, 12, 12, 74);
            Browser.SetActive(false);

            UseButton = UiKit.MakeButton(p, "Use this folder", Use, 30, "ok.wav");
            UseButton.name = "Use this folder";
            UseButton.GetComponent<RectTransform>().Place(0.5f, 0, 0.5f, 0, -460, 36, 40, -110);
            string leave = cancelled != null ? "Cancel" : "Quit";
            var back = UiKit.MakeButton(p, leave, Leave, 30);
            back.name = leave;
            back.GetComponent<RectTransform>().Place(0.5f, 0, 0.5f, 0, 40, 36, -460, -110);

            if (!string.IsNullOrWhiteSpace(start)) Check(false);
            else Say("The game did not find Total Annihilation: Kingdoms on this computer by itself.", UiKit.Pale);
        }

        // Paths as Windows writes them.
        public static string Shown(string dir) => (dir ?? "").Replace('/', '\\');

        void Say(string text, bool good) => Say(text, good ? UiKit.GoldBright : Bad);

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
                else Say("Open the folder the game is in. A folder that holds it says so in the list.", UiKit.Pale);
            }
            for (int i = List.childCount - 1; i >= 0; i--) Destroy(List.GetChild(i).gameObject);
            Rows.Clear();
            for (int i = 0; i < folders.Count && i < MaxRows; i++)
            {
                string path = GameFolder.Clean(folders[i]);
                bool game = Browsing.Length > 0 && GameFolder.Holds(path);
                string label = Browsing.Length == 0 ? Shown(path) : Path.GetFileName(path.TrimEnd('/'));
                var b = UiKit.MakeButton(List, game ? label + "    (the game is here)" : label, () => Open(path), 22, "");
                b.name = "Folder " + label;
                var text = b.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                if (game) text.color = UiKit.GoldBright;
                b.GetComponent<RectTransform>().Size(0, 44);
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
