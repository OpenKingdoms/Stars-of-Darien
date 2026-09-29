// MenuScreens.cs - every screen of the game flow on one canvas: options,
// loading, the pause menu and the victory and defeat screens here, the
// opening screen and the lobby in LobbyScreens, the map editor's in
// EditorScreens. The battle HUD has a canvas of its own beneath them.
// Show() picks the screens for a flow state, and Tick() refreshes what
// changes while one is up.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class MenuScreens
    {
        public const int ReferenceWidth = 1920, ReferenceHeight = 1080;
        // Lay out for this size instead of the screen's, for captures and
        // tests that render off screen.
        public static Vector2Int? SizeOverride;

        readonly GameRoot root;
        readonly Canvas canvas;
        readonly Dictionary<string, GameObject> screens = new Dictionary<string, GameObject>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        RawImage loadingBackdrop;
        Text saveNote, loadEmpty;
        BattleHud hud;
        EditorScreens editor;
        LobbyScreens lobby;
        RectTransform saveItems;
        Text loadingTitle, loadingStage, loadingTip, resultTitle, resultInfo;
        Image loadingFill;
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
        float resultShownAt;

        public string Visible { get; private set; } = "";

        static readonly string[] Tips =
        {
            "Right click the ground to move, or an enemy to attack.",
            "Q and E turn the camera, Page Up and Page Down tilt it, Home returns to the classic view.",
            "Drag a box to select a group. Hold Shift to add to it.",
            "Mana flows back on its own. Spend it before your stores are full.",
            "S stops the selected units where they stand.",
        };

        public MenuScreens(GameRoot root)
        {
            this.root = root;
            canvas = UiKit.MakeCanvas("Screens", 10);
            canvas.transform.SetParent(root.transform, false);
            BuildOptions();
            BuildLoading();
            BuildHud();
            BuildPause();
            BuildResult();
            BuildLoadList();
            editor = new EditorScreens(root, this);
            lobby = new LobbyScreens(root, this);
            Measure(out var size, out float scale);
            lobby.Fit(size, scale);
            // Modal screens draw over the battle HUD, and their dimmed
            // backdrops take every click meant for what lies beneath.
            foreach (var name in new[] { "Pause", "Options", "Load", "Result" })
                if (screens.TryGetValue(name, out var modal)) modal.transform.SetAsLastSibling();
        }

        public Canvas Canvas => canvas;
        public BattleHud Hud => hud;
        public LobbyScreens Lobby => lobby;

        // The canvas's size in its own units and its scale, as it will draw:
        // the canvas scaler's pick for the screen, or SizeOverride's.
        void Measure(out Vector2 size, out float scale)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (SizeOverride is Vector2Int o)
            {
                scale = Mathf.Sqrt(o.x / (float)ReferenceWidth * (o.y / (float)ReferenceHeight));
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = scale;
                canvas.scaleFactor = scale;
                size = new Vector2(o.x, o.y) / scale;
                return;
            }
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            size = ((RectTransform)canvas.transform).rect.size;
            scale = canvas.scaleFactor;
            if (size.x < 1f || size.y < 1f || scale <= 0f)
            {
                scale = Mathf.Sqrt(UnityEngine.Screen.width / (float)ReferenceWidth * (UnityEngine.Screen.height / (float)ReferenceHeight));
                size = new Vector2(UnityEngine.Screen.width, UnityEngine.Screen.height) / Mathf.Max(0.01f, scale);
            }
        }

        public void DropScreen(string name)
        {
            if (!screens.TryGetValue(name, out var go)) return;
            screens.Remove(name);
            if (go != null) { go.SetActive(false); World.Looks.Release(go); }
        }

        public GameObject Screen(string name) => screens.TryGetValue(name, out var s) ? s : null;

        public void Show(FlowState state)
        {
            string[] on;
            switch (state)
            {
                case FlowState.MainMenu: on = new[] { "Menu" }; break;
                case FlowState.Skirmish: on = new[] { "Skirmish" }; break;
                case FlowState.Multiplayer: on = new[] { "Multiplayer" }; break;
                case FlowState.Room: on = new[] { "Room" }; break;
                case FlowState.MapChoice: on = new[] { "Room", "MapChoice" }; break;
                case FlowState.Options: on = root.Flow.OptionsReturn == FlowState.Paused ? new[] { "Hud", "Options" } : new[] { "Options" }; break;
                case FlowState.Loading: on = new[] { "Loading" }; RefreshLoading(); break;
                case FlowState.Playing: on = new[] { "Hud" }; break;
                case FlowState.Paused: on = new[] { "Hud", "Pause" }; saveNote.text = ""; break;
                case FlowState.LoadList: on = new[] { "Load" }; RefreshLoadList(); break;
                case FlowState.EditorSetup: on = new[] { "EditorSetup" }; editor.RefreshSetup(); break;
                case FlowState.Editing: on = new[] { "Editor" }; editor.RefreshTools(); break;
                case FlowState.Victory:
                case FlowState.Defeat: on = new[] { "Hud", "Result" }; RefreshResult(state == FlowState.Victory); break;
                default: on = new string[0]; break;
            }
            foreach (var kv in screens) kv.Value.SetActive(Array.IndexOf(on, kv.Key) >= 0);
            Visible = string.Join(",", on);
            lobby?.Show(state);
        }

        public void Tick()
        {
            if (!canvas) return;
            Measure(out var size, out float scale);
            if (lobby.Fit(size, scale)) Show(root.Flow.State);
            lobby.Tick();
            if (root.Flow.State == FlowState.Editing) editor.Tick();
            var b = root.Backend;
            switch (root.Flow.State)
            {
                case FlowState.Loading:
                    UiKit.SetBar(loadingFill, root.Loading.Fraction);
                    loadingStage.text = string.IsNullOrEmpty(root.Loading.Stage) ? "" : root.Loading.Stage + "...";
                    break;
                case FlowState.Playing:
                case FlowState.Paused:
                case FlowState.Victory:
                case FlowState.Defeat:
                case FlowState.Options:
                    if (b.Status == GameStatus.Idle) break;
                    hud.Tick();
                    break;
            }
        }

        // ---- Screens ----

        public RectTransform NewScreenFor(string name, bool backdrop) => NewScreen(name, backdrop);
        public Texture2D PreviewFor(string mapId) => Preview(mapId);

        RectTransform NewScreen(string name, bool backdrop)
        {
            var rt = UiKit.Rect(canvas.transform, name).Fill();
            if (backdrop)
            {
                var stone = UiKit.Picture(rt, "Stone", UiKit.Stone, new Color(0.8f, 0.78f, 0.74f));
                stone.rectTransform.Fill();
                var glow = UiKit.Picture(rt, "Glow", UiKit.Glow, new Color(1f, 0.72f, 0.35f, 0.22f));
                glow.rectTransform.Place(0.1f, -0.2f, 0.9f, 1.1f);
                glow.raycastTarget = false;
            }
            screens[name] = rt.gameObject;
            return rt;
        }

        static Text Heading(Transform parent, string text, int size, float y0, float y1)
        {
            var t = UiKit.Label(parent, text, size, UiKit.Gold, TextAnchor.MiddleCenter, true);
            t.rectTransform.Place(0, y0, 1, y1);
            return t;
        }

        public void StartGame()
        {
            int humans = 0, players = 0;
            foreach (var seat in root.Setup.Seats)
            {
                if (seat.Kind == SeatKind.Human) humans++;
                if (seat.Kind != SeatKind.Closed) players++;
            }
            var map = root.CurrentMap();
            string why = null;
            if (humans == 0 || players < 2) why = "A game needs you and at least one computer player.";
            else if (map != null && players > MapCatalog.PlayersOf(map)) why = $"{map.Name} holds {MapCatalog.PlayersOf(map)} players.";
            lobby.Skirmish?.ShowError(why);
            if (why != null) return;
            root.Setup.Seed = (uint)Environment.TickCount;
            root.Flow.Fire(FlowEvent.Start);
        }

        static void OptionRow(Transform parent, string label, string[] choices, int index, Action<int> changed)
        {
            var row = UiKit.Rect(parent, label).Size(0, 54);
            UiKit.Row(row, 16);
            var l = UiKit.Label(row, label, 27, UiKit.Pale, TextAnchor.MiddleLeft);
            l.rectTransform.Size(260, 0);
            UiKit.Cycle(row, choices, index, changed).GetComponent<RectTransform>().Size(260, 0);
        }

        Texture2D Preview(string mapId)
        {
            if (previews.TryGetValue(mapId, out var t)) return t;
            t = UiKit.ToTexture(root.Backend.MapPreview(mapId, 512), true);
            if (t != null) { t.filterMode = FilterMode.Trilinear; owned.Add(t); }
            previews[mapId] = t;
            return t;
        }

        void BuildOptions()
        {
            var s = NewScreen("Options", false);
            var dim = UiKit.Picture(s, "Dim", UiKit.White, new Color(0, 0, 0, 0.55f));
            dim.rectTransform.Fill();
            dim.raycastTarget = true;
            // As tall as the screen allows, never taller: the rows scroll,
            // and Back and the close cross stay in view.
            var p = UiKit.Panel(s, "Panel", false).Place(0.5f, 0, 0.5f, 1, -440, 24, -440, 24);
            p.gameObject.AddComponent<MaxHeight>().Max = 1020;
            var title = UiKit.Label(p, "Options", 60, UiKit.Gold, TextAnchor.MiddleCenter, true);
            title.rectTransform.Place(0, 1, 1, 1, 0, -110, 0, 20);
            var close = UiKit.MakeButton(p, "X", () => { root.Options.Save(); root.Flow.Fire(FlowEvent.Back); }, 30);
            close.name = "Close";
            close.GetComponent<RectTransform>().Place(1, 1, 1, 1, -84, -84, 20, 20);
            var rows = UiKit.ScrollList(p, "Rows", 14);
            ((RectTransform)rows.parent.parent).Place(0, 0, 1, 1, 60, 130, 60, 120);
            var o = root.Options;
            OptionRow(rows, "Weather", new[] { "By map", "Clear", "Rain", "Snow", "Fog" }, (int)o.Weather, i =>
            {
                o.Weather = (WeatherChoice)i;
                if (root.World != null) root.World.Atmosphere.SetWeather(GameOptions.Resolve(o.Weather, root.CurrentMap()?.Climate ?? ""));
            });
            OptionRow(rows, "Shadows", new[] { "Soft", "Off" }, o.Shadows ? 0 : 1, i =>
            {
                o.Shadows = i == 0;
                if (root.World?.Atmosphere.Sun != null) root.World.Atmosphere.Sun.shadows = o.Shadows ? LightShadows.Soft : LightShadows.None;
            });
            OptionRow(rows, "Post effects", new[] { "On", "Off" }, o.PostEffects ? 0 : 1, i =>
            {
                o.PostEffects = i == 0;
                if (root.World != null) root.World.Atmosphere.SetPostEffects(o.PostEffects, root.World.Camera != null ? root.World.Camera.GetComponent<Camera>() : null);
            });
            OptionRow(rows, "Game speed", new[] { "Normal", "Fast" }, o.GameSpeed - 1, i => o.GameSpeed = i + 1);
            OptionRow(rows, "Controls", new[] { "Classic", "Modern" }, o.ClassicControls ? 0 : 1, i =>
            {
                o.ClassicControls = i == 0;
                if (root.Orders != null) root.Orders.Classic = o.ClassicControls;
            });
            OptionRow(rows, "Pointer size", new[] { "Fit screen", "1x", "2x", "3x", "4x" }, o.CursorScale, i =>
            {
                o.CursorScale = i;
                if (root.Pointer != null) root.Pointer.ScaleSetting = i;
            });
            // The battle HUD: 100 percent is the original at 640x480, 80 at 800x600.
            var stops = HudLayout.ScaleStops;
            OptionRow(rows, "Interface size", Array.ConvertAll(stops, p => p + "%"), Math.Max(0, Array.IndexOf(stops, HudLayout.NearestStop(o.UiScale))), i => o.UiScale = stops[i]);
            OptionRow(rows, "Key letters", new[] { "Shown", "Hidden" }, o.HotkeyLetters ? 0 : 1, i => o.HotkeyLetters = i == 0);
            var help = UiKit.Label(rows, "Camera: WASD, the arrows or the screen edge pan. The wheel zooms. Middle drag up and down tilts and raises, left and right turns, and with Shift pans. Q and E turn, Page Up and Page Down tilt, Home returns to the classic view.", 21, UiKit.Dim, TextAnchor.UpperLeft);
            help.rectTransform.Size(0, 84);
            var volumes = new[] { 0f, 0.25f, 0.5f, 0.8f, 1f };
            int vi = 0;
            for (int k = 0; k < volumes.Length; k++) if (Mathf.Abs(volumes[k] - o.Volume) < Mathf.Abs(volumes[vi] - o.Volume)) vi = k;
            OptionRow(rows, "Sound", new[] { "Off", "Quiet", "Half", "Loud", "Full" }, vi, i => { o.Volume = volumes[i]; root.ApplyAudio(); });
            OptionRow(rows, "Music", new[] { "On", "Off" }, o.Music ? 0 : 1, i => { o.Music = i == 0; root.ApplyAudio(); });
            OptionRow(rows, "Display", new[] { "Full screen", "Window" }, o.Fullscreen ? 0 : 1, i =>
            {
                o.Fullscreen = i == 0;
                if (!Application.isEditor) UnityEngine.Screen.fullScreen = o.Fullscreen;
            });
            var back = UiKit.MakeButton(p, "Back", () => { o.Save(); root.Flow.Fire(FlowEvent.Back); }, 32);
            back.name = "Back";
            back.GetComponent<RectTransform>().Place(0.5f, 0, 0.5f, 0, -180, 34, -180, -114);
        }

        void BuildLoading()
        {
            var s = NewScreen("Loading", false);
            var black = UiKit.Picture(s, "Black", UiKit.White, Color.black);
            black.rectTransform.Fill();
            loadingBackdrop = UiKit.Rect(s, "Backdrop").Fill().gameObject.AddComponent<RawImage>();
            loadingBackdrop.color = new Color(0.45f, 0.42f, 0.38f);
            var shade = UiKit.Picture(s, "Shade", UiKit.Glow, new Color(0, 0, 0, 0.85f));
            shade.rectTransform.Place(0, -0.6f, 1, 0.55f);
            loadingTitle = UiKit.Label(s, "", 80, UiKit.Gold, TextAnchor.MiddleCenter, true);
            loadingTitle.rectTransform.Place(0, 0.3f, 1, 0.42f);
            var name = UiKit.Label(s, GameRoot.Title, 40, UiKit.Dim, TextAnchor.MiddleCenter, true);
            name.rectTransform.Place(0, 0.86f, 1, 0.95f);
            var bar = UiKit.Bar(s, "Progress", out loadingFill);
            bar.rectTransform.Place(0.5f, 0.2f, 0.5f, 0.2f, -520, -16, -520, -16);
            loadingStage = UiKit.Label(s, "", 30, UiKit.Pale);
            loadingStage.rectTransform.Place(0, 0.12f, 1, 0.18f);
            loadingTip = UiKit.Label(s, "", 26, UiKit.Dim);
            loadingTip.rectTransform.Place(0, 0.04f, 1, 0.1f);
        }

        void RefreshLoading()
        {
            var map = root.CurrentMap();
            loadingTitle.text = map != null ? map.Name : "";
            var tex = map != null ? Preview(map.Id) : null;
            loadingBackdrop.texture = tex;
            loadingBackdrop.enabled = tex != null;
            if (tex != null)
            {
                // Cover the screen, keeping the picture's shape.
                float sa = (float)ReferenceWidth / ReferenceHeight, ta = (float)tex.width / tex.height;
                loadingBackdrop.uvRect = ta > sa ? new Rect((1 - sa / ta) / 2, 0, sa / ta, 1) : new Rect(0, (1 - ta / sa) / 2, 1, ta / sa);
            }
            loadingTip.text = "Tip: " + Tips[UnityEngine.Random.Range(0, Tips.Length)];
            UiKit.SetBar(loadingFill, 0);
            loadingStage.text = "";
        }

        // The battle HUD, on its own canvas under this one, so the pause,
        // options and result screens draw over it.
        void BuildHud()
        {
            hud = new BattleHud(root);
            hud.Root.transform.SetParent(root.transform, false);
            screens["Hud"] = hud.Root;
        }

        void BuildPause()
        {
            var s = NewScreen("Pause", false);
            var dim = UiKit.Picture(s, "Dim", UiKit.White, new Color(0, 0, 0, 0.5f));
            dim.rectTransform.Fill();
            var p = UiKit.Panel(s, "Panel", false).Place(0.5f, 0.5f, 0.5f, 0.5f, -300, -340, -300, -340);
            p.gameObject.AddComponent<FitInParent>().Margin = 24;
            Heading(p, "Paused", 60, 0.8f, 0.96f);
            var col = UiKit.Rect(p, "Buttons").Place(0, 0, 1, 1, 70, 90, 70, 150);
            UiKit.Column(col, 18);
            UiKit.MakeButton(col, "Resume", () => root.Flow.Fire(FlowEvent.Resume), 32).GetComponent<RectTransform>().Size(0, 72);
            UiKit.MakeButton(col, "Save game", () =>
            {
                bool ok = root.SaveNow(out var path);
                saveNote.text = ok ? "Saved as " + System.IO.Path.GetFileNameWithoutExtension(path) : "This game could not be saved.";
            }, 32).GetComponent<RectTransform>().Size(0, 72);
            UiKit.MakeButton(col, "Options", () => root.Flow.Fire(FlowEvent.OpenOptions), 32).GetComponent<RectTransform>().Size(0, 72);
            UiKit.MakeButton(col, "Quit to menu", () => root.Flow.Fire(FlowEvent.ToMenu), 32).GetComponent<RectTransform>().Size(0, 72);
            saveNote = UiKit.Label(p, "", 24, UiKit.Pale);
            saveNote.rectTransform.Place(0, 0, 1, 0, 20, 24, 20, -70);
        }

        void BuildResult()
        {
            var s = NewScreen("Result", false);
            var dim = UiKit.Picture(s, "Dim", UiKit.Glow, new Color(0, 0, 0, 0.8f));
            dim.rectTransform.Place(-0.3f, -0.3f, 1.3f, 1.3f);
            resultTitle = UiKit.Label(s, "", 150, UiKit.Gold, TextAnchor.MiddleCenter, true);
            resultTitle.rectTransform.Place(0, 0.52f, 1, 0.72f);
            resultInfo = UiKit.Label(s, "", 32, UiKit.Pale);
            resultInfo.rectTransform.Place(0, 0.4f, 1, 0.5f);
            var back = UiKit.MakeButton(s, "Return to menu", () => root.Flow.Fire(FlowEvent.ToMenu), 32);
            back.GetComponent<RectTransform>().Place(0.5f, 0.24f, 0.5f, 0.24f, -220, -40, -220, -40);
        }

        void BuildLoadList()
        {
            var s = NewScreen("Load", true);
            Heading(s, "Load a game", 72, 0.88f, 0.98f);
            var panel = UiKit.Panel(s, "Saves", false).Place(0.5f, 0, 0.5f, 1, -560, 150, -560, 150);
            saveItems = UiKit.ScrollList(panel, "Items", 10);
            ((RectTransform)saveItems.parent.parent).Place(0, 0, 1, 1, 24, 24, 24, 24);
            loadEmpty = UiKit.Label(panel, "No saved games yet. Save one from the pause menu.", 30, UiKit.Dim);
            loadEmpty.rectTransform.Fill(40);
            var back = UiKit.MakeButton(s, "Back", () => root.Flow.Fire(FlowEvent.Back), 32);
            back.GetComponent<RectTransform>().Place(0, 0, 0, 0, 60, 40, -360, -110);
        }

        void RefreshLoadList()
        {
            for (int i = saveItems.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(saveItems.GetChild(i).gameObject);
            var saves = root.ListSaves();
            loadEmpty.text = root.LastError ?? "No saved games yet. Save one from the pause menu.";
            loadEmpty.gameObject.SetActive(saves.Count == 0 || root.LastError != null);
            foreach (var save in saves)
            {
                var entry = save;
                int secs = (int)(entry.Tick / (uint)Mathf.Max(1, root.Backend.TicksPerSecond));
                string label = $"{entry.Map}    {entry.SavedAt:d MMM yyyy, HH:mm}    {secs / 60}:{secs % 60:00} in";
                var b = UiKit.MakeButton(saveItems, label, () => root.LoadSave(entry), 26);
                b.GetComponent<RectTransform>().Size(0, 64);
                b.name = "Save " + System.IO.Path.GetFileName(entry.Path);
            }
        }

        void RefreshResult(bool won)
        {
            resultShownAt = Time.unscaledTime;
            resultTitle.text = won ? "Victory" : "Defeat";
            resultTitle.color = won ? UiKit.GoldBright : new Color(0.8f, 0.3f, 0.25f);
            var b = root.Backend;
            int secs = (int)(b.Tick / (uint)Mathf.Max(1, b.TicksPerSecond));
            var map = root.CurrentMap();
            resultInfo.text = (won ? "Your enemies are vanquished" : "Your kingdom has fallen") +
                $" on {(map != null ? map.Name : "the field")} after {secs / 60} min {secs % 60} s.";
        }

        public void Dispose()
        {
            hud?.Dispose();
            lobby?.Dispose();
            foreach (var o in owned) World.Looks.Release(o);
            owned.Clear();
            if (canvas) World.Looks.Release(canvas.gameObject);
        }
    }
}
