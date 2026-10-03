// MenuScreens.cs - every screen of the game flow on one canvas. Pause, the
// question before leaving, options, loading and victory and defeat are here
// on the dialog kit; the lobby is in LobbyScreens, the editor's in EditorScreens.
using System;
using System.Collections.Generic;
using System.Text;
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
        // The banner is seen over the field this long before the result's
        // page fades in, the original's 90 frames.
        public const float ResultHold = 3f, ResultFade = 0.4f;

        // The screens on the dialog kit, built again when the canvas changes
        // size, and the order modal screens draw in over the battle HUD.
        static readonly string[] Dialogs = { "Options", "Loading", "Pause", "Leave", "Result", "Results", "Load" };
        static readonly string[] Modal = { "Pause", "Leave", "Options", "Load", "Result", "Results" };

        readonly GameRoot root;
        readonly Canvas canvas;
        readonly Dictionary<string, GameObject> screens = new Dictionary<string, GameObject>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        DialogLayout layout;
        Vector2 builtSize;
        RawImage loadingBackdrop;
        Text saveNote, loadEmpty;
        BattleHud hud;
        EditorScreens editor;
        LobbyScreens lobby;
        RectTransform saveItems;
        Text loadingTitle, loadingSeats, loadingStage, loadingTip, loadingPercent;
        Image loadingFill;
        Dialog options;
        ResultScreen results;
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
        float resultShownAt;
        uint resultTick;
        bool resultWon, leaving, lookingAtField, resultFresh;
        string tip = "";
        FlowState shown = (FlowState)(-1);
        int shownFrame = -1;

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
            canvas.gameObject.AddComponent<Resharpen>();
            Measure(out var size, out float scale);
            layout = new DialogLayout(size, scale);
            builtSize = size;
            BuildDialogs();
            BuildHud();
            editor = new EditorScreens(root, this);
            lobby = new LobbyScreens(root, this);
            lobby.Fit(size, scale);
            Order();
        }

        public Canvas Canvas => canvas;
        public ResultScreen Results => results;
        public BattleHud Hud => hud;
        public LobbyScreens Lobby => lobby;

        // Modal screens draw over the battle HUD, and their dimmed
        // backdrops take every click meant for what lies beneath.
        void Order()
        {
            foreach (var name in Modal)
                if (screens.TryGetValue(name, out var modal) && modal != null) modal.transform.SetAsLastSibling();
        }

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
            bool fresh = state != shown;
            // Back from the pause menu to a lost battle being watched.
            bool resumed = fresh && shown == FlowState.Paused;
            if (fresh)
            {
                // Escape leaves Options without its Back, so the leaving saves.
                if (shown == FlowState.Options) root.Options?.Save();
                shownFrame = Time.frameCount;
                leaving = false;
            }
            shown = state;
            string[] on;
            switch (state)
            {
                case FlowState.MainMenu: on = new[] { "Menu" }; break;
                case FlowState.Skirmish: on = new[] { "Skirmish" }; break;
                case FlowState.Multiplayer: on = new[] { "Multiplayer" }; break;
                case FlowState.Room: on = new[] { "Room" }; break;
                case FlowState.MapChoice: on = new[] { "Room", "MapChoice" }; break;
                case FlowState.Options:
                    string under = Beneath(root.Flow.OptionsReturn);
                    on = under != null ? new[] { under, "Options" } : new[] { "Options" };
                    PointRows();
                    if (fresh) OpenOptions();
                    break;
                case FlowState.Loading:
                    on = new[] { "Loading" };
                    if (fresh) RefreshLoading(); else FillLoading();
                    break;
                case FlowState.Playing: on = new[] { "Hud" }; break;
                case FlowState.Paused:
                    on = leaving ? new[] { "Hud", "Pause", "Leave" } : new[] { "Hud", "Pause" };
                    if (fresh) saveNote.text = "";
                    break;
                case FlowState.LoadList: on = new[] { "Load" }; RefreshLoadList(); break;
                case FlowState.EditorSetup: on = new[] { "EditorSetup" }; editor.RefreshSetup(); break;
                case FlowState.Editing: on = new[] { "Editor" }; editor.RefreshTools(); break;
                case FlowState.Victory:
                case FlowState.Defeat:
                    if (fresh && !resumed)
                    {
                        resultWon = state == FlowState.Victory;
                        resultShownAt = Time.unscaledTime;
                        resultTick = root.Backend.Tick;
                        lookingAtField = false;
                        resultFresh = true;
                    }
                    RefreshResult();
                    on = lookingAtField ? new[] { "Hud", "Results" } : new[] { "Hud", "Result" };
                    break;
                default: on = new string[0]; break;
            }
            foreach (var kv in screens) kv.Value.SetActive(Array.IndexOf(on, kv.Key) >= 0);
            Visible = string.Join(",", on);
            lobby?.Show(state);
        }

        // Keys the dialogs answer, for the polling in Tick and for tests.
        static readonly KeyCode[] DialogKeys =
        {
            KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape, KeyCode.F1,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Tab,
        };

        // Pause: Escape, F1 and Enter resume, Enter leaves from the question.
        // Options: Escape and Enter go back, Up and Down move the focus, Left
        // and Right step its picker. True when the key was used.
        public bool Key(KeyCode k)
        {
            bool enter = k == KeyCode.Return || k == KeyCode.KeypadEnter;
            switch (root.Flow.State)
            {
                case FlowState.Paused:
                    if (leaving && enter) { LeaveBattle(); return true; }
                    if (enter || k == KeyCode.Escape || k == KeyCode.F1) { root.Flow.Fire(FlowEvent.Resume); return true; }
                    return false;
                case FlowState.Victory:
                case FlowState.Defeat:
                    // The original's accelerators on its statistics screen.
                    if (lookingAtField || results == null) return false;
                    return results.Key(k, () => LookAtField(true), () => root.Flow.Fire(FlowEvent.ToMenu));
                case FlowState.Options:
                    if (enter || k == KeyCode.Escape) { CloseOptions(); return true; }
                    if (k == KeyCode.DownArrow || k == KeyCode.UpArrow)
                    {
                        bool down = k == KeyCode.DownArrow;
                        Focus(focus < 0 ? (down ? 0 : rows.Count - 1) : focus + (down ? 1 : -1));
                        return true;
                    }
                    if ((k == KeyCode.LeftArrow || k == KeyCode.RightArrow) && focus >= 0 && rows[focus].Picker != null)
                    {
                        rows[focus].Picker.Step(k == KeyCode.RightArrow ? 1 : -1);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        public void Tick()
        {
            if (!canvas) return;
            Measure(out var size, out float scale);
            bool rebuilt = FitDialogs(size, scale);
            if (lobby.Fit(size, scale) || rebuilt)
            {
                Order();
                Show(root.Flow.State);
            }
            lobby.Tick();
            results?.Tick();
            if (root.Flow.State == FlowState.Editing) editor.Tick();
            // Not in the frame a screen opened, so the key that opened it
            // does not close it again.
            if (Time.frameCount != shownFrame)
                foreach (var k in DialogKeys)
                    if (Input.GetKeyDown(k) && Key(k)) break;
            var b = root.Backend;
            switch (root.Flow.State)
            {
                case FlowState.Loading:
                    float f = Mathf.Clamp01(root.Loading.Fraction);
                    UiKit.SetBar(loadingFill, f);
                    loadingPercent.text = Mathf.RoundToInt(f * 100f) + "%";
                    loadingStage.text = Stage(root.Loading.Stage);
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

        // The screen Options opens over: the battle, the menu or the lobby.
        static string Beneath(FlowState from)
        {
            switch (from)
            {
                case FlowState.Paused: return "Hud";
                case FlowState.MainMenu: return "Menu";
                case FlowState.Skirmish: return "Skirmish";
                case FlowState.Room: return "Room";
                default: return null;
            }
        }

        // The alpha of a black that leaves this much of what is beneath, as
        // the eye sees it: in linear colour 0.5 alpha only takes off a quarter.
        public static float Dim(float seen) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f - Mathf.Pow(seen, 2.2f) : 1f - seen;

        // A dimmed screen that takes every click meant for the HUD beneath.
        RectTransform DimScreen(string name, float dim)
        {
            var s = NewScreen(name, false);
            var shade = UiKit.Picture(s, "Dim", UiKit.White, new Color(0, 0, 0, dim));
            shade.rectTransform.Fill();
            shade.raycastTarget = true;
            return s;
        }

        void BuildDialogs()
        {
            BuildOptions();
            BuildLoading();
            BuildPause();
            BuildLeave();
            BuildResult();
            BuildResults();
            BuildLoadList();
            foreach (var n in Dialogs) if (screens.TryGetValue(n, out var go)) go.SetActive(false);
        }

        // The dialogs are laid out for the canvas's size, so a new size
        // builds them again. Their paint follows the sharpness by itself.
        bool FitDialogs(Vector2 size, float scale)
        {
            if ((size - builtSize).sqrMagnitude < 1f) return false;
            builtSize = size;
            layout = new DialogLayout(size, scale);
            foreach (var n in Dialogs) DropScreen(n);
            BuildDialogs();
            return true;
        }

        public void StartGame()
        {
            string why = SkirmishCheck.WhyNot(root.Setup.Seats, root.CurrentMap());
            lobby.Skirmish?.ShowError(why);
            if (why != null) return;
            root.Setup.Seed = (uint)Environment.TickCount;
            root.Flow.Fire(FlowEvent.Start);
        }

        Texture2D Preview(string mapId)
        {
            if (previews.TryGetValue(mapId, out var t)) return t;
            t = UiKit.ToTexture(root.Backend.MapPreview(mapId, 512), true);
            if (t != null) { t.filterMode = FilterMode.Trilinear; owned.Add(t); }
            previews[mapId] = t;
            return t;
        }

        // ---- Options ----

        const string OptionsResting = "Changes apply at once. Back keeps them.";
        const string LookBehind = ". Look at the battle behind this panel";

        // One row of the options: its label and help, its picker and how it
        // reads and sets the options, and its focus ring.
        sealed class OptionRow
        {
            public string Label, Help;
            public bool Live;
            public string[] Choices;
            public Func<GameOptions, int> Read;
            public Action<int> Apply;
            public CyclePicker Picker;
            public RectTransform Rect;
            public HelpSpot Spot;
            public GameObject Ring;
        }

        readonly List<OptionRow> rows = new List<OptionRow>();
        int focus = -1;
        ScrollRect optionScroll;

        void BuildOptions()
        {
            rows.Clear();
            focus = -1;
            var s = DimScreen("Options", Dim(0.5f));
            var box = layout.Options();
            options = UiKit.MakeDialog(s, box, "Options", OptionsResting);
            var p = options.transform;
            var close = UiKit.MakeCross(p, "Close", box.Local(box.Close), CloseOptions);
            UiKit.Explain(close.gameObject, options, "Back to where you were, keeping the changes");
            UiKit.MakePlate(p, "Defaults", box.Local(box.Plate("Defaults")), Defaults, options, "Every setting here as it first was, but Display");
            UiKit.MakePlate(p, "Back", box.Local(box.Plate("Back")), CloseOptions, options, "Back to where you were, keeping the changes");

            var list = UiKit.ScrollList(p, "Rows", 0);
            var view = (RectTransform)list.parent.parent;
            var at = box.Local(box.List);
            view.anchorMin = view.anchorMax = view.pivot = new Vector2(0, 1);
            view.anchoredPosition = new Vector2(at.x, -at.y);
            view.sizeDelta = at.size;
            optionScroll = view.GetComponent<ScrollRect>();
            float w = at.width;
            var o = root.Options;

            Section(list, "Game");
            Row(list, w, "Weather", new[] { "By map", "Clear", "Rain", "Snow", "Fog" }, x => (int)x.Weather, i =>
            {
                o.Weather = (WeatherChoice)i;
                if (root.World != null) root.World.Atmosphere.SetWeather(GameOptions.Resolve(o.Weather, root.CurrentMap()?.Climate ?? ""));
            }, "Weather: the map's own, or always clear, rain, snow or fog");
            Row(list, w, "Game speed", new[] { "Normal", "Fast" }, x => x.GameSpeed - 1, i => o.GameSpeed = i + 1,
                "Game speed: the original's pace, or the battle at twice it");
            Row(list, w, "Controls", new[] { "Classic", "Modern" }, x => x.ClassicControls ? 0 : 1, i =>
            {
                o.ClassicControls = i == 0;
                if (root.Orders != null) root.Orders.Classic = o.ClassicControls;
            }, "Controls: Classic orders with the left button as the original, Modern with the right");
            Note(list, "Camera: WASD, the arrows or the screen edge pan. The wheel zooms. Middle drag up and down tilts and raises, left and right turns, and with Shift pans. Q and E turn, Page Up and Page Down tilt, Home returns to the classic view.");

            Section(list, "Interface");
            // The battle HUD: 100 percent is the original at 640x480, 80 at 800x600.
            var stops = HudLayout.ScaleStops;
            Row(list, w, "Interface size", Array.ConvertAll(stops, x => x + "%"), x => Math.Max(0, Array.IndexOf(stops, HudLayout.NearestStop(x.UiScale))),
                i => o.UiScale = stops[i], "Interface size: the battle panels, 100 percent is the original at 640x480", true);
            Row(list, w, "Key letters", new[] { "Shown", "Hidden" }, x => x.HotkeyLetters ? 0 : 1, i => o.HotkeyLetters = i == 0,
                "Key letters: each order's key on its button, or hidden", true);
            Row(list, w, "Pointer size", new[] { "Fit screen", "1x", "2x", "3x", "4x" }, x => Mathf.Clamp(x.CursorScale, 0, 4), i =>
            {
                o.CursorScale = i;
                if (root.Pointer != null) root.Pointer.ScaleSetting = i;
            }, "Pointer size: fitted to the screen, or one to four times the original's");

            Section(list, "Sound");
            var volumes = new[] { 0f, 0.25f, 0.5f, 0.8f, 1f };
            Row(list, w, "Sound", new[] { "Off", "Quiet", "Half", "Loud", "Full" }, x => Nearest(volumes, x.Volume), i => { o.Volume = volumes[i]; root.ApplyAudio(); },
                "Sound: how loud the game's sounds are");
            Row(list, w, "Music", new[] { "On", "Off" }, x => x.Music ? 0 : 1, i => { o.Music = i == 0; root.ApplyAudio(); },
                "Music: the game's music, on or off");

            Section(list, "Display");
            Row(list, w, "Display", new[] { "Full screen", "Window" }, x => x.Fullscreen ? 0 : 1, i =>
            {
                o.Fullscreen = i == 0;
                if (!Application.isEditor) GameOptions.ApplyDisplay(o.Fullscreen);
            }, "Display: the whole screen, or a window");
            Row(list, w, "Shadows", new[] { "Soft", "Off" }, x => x.Shadows ? 0 : 1, i =>
            {
                o.Shadows = i == 0;
                if (root.World?.Atmosphere.Sun != null) root.World.Atmosphere.Sun.shadows = o.Shadows ? LightShadows.Soft : LightShadows.None;
            }, "Shadows: soft shadows from the sun, or none to run faster");
            Row(list, w, "Post effects", new[] { "On", "Off" }, x => x.PostEffects ? 0 : 1, i =>
            {
                o.PostEffects = i == 0;
                if (root.World != null) root.World.Atmosphere.SetPostEffects(o.PostEffects, root.World.Camera != null ? root.World.Camera.GetComponent<Camera>() : null);
            }, "Post effects: the remaster's light, colour and smoothed edges, or none to run faster");
            if (GameRoot.ChangeGameFolder != null) GameFolderRow(list, w, o);
        }

        static int Nearest(float[] values, float v)
        {
            int best = 0;
            for (int k = 0; k < values.Length; k++) if (Mathf.Abs(values[k] - v) < Mathf.Abs(values[best] - v)) best = k;
            return best;
        }

        // A section's heading, a rubric in uncial minium.
        static void Section(Transform list, string name)
        {
            var r = UiKit.Rect(list, "Section " + name).Size(0, DialogLayout.RubricH);
            var t = UiKit.Words(r, "Rubric " + name, new Rect(0, 0, 10, 10), name, DialogLayout.Rubric, HudArt.Minium, UiKit.UncialFont, TextAnchor.LowerLeft);
            t.rectTransform.Place(0, 0, 1, 1, 12, 4, 12, 0);
        }

        // A paragraph in the list, as tall as its words.
        static void Note(Transform list, string text)
        {
            var box = UiKit.Rect(list, "Camera help");
            var row = UiKit.Row(box, 0, 12);
            row.childForceExpandWidth = true;
            UiKit.Words(box, "Text", new Rect(0, 0, 10, 10), text, DialogLayout.Small, new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.6f), UiKit.BodyFont, TextAnchor.UpperLeft);
        }

        OptionRow Line(Transform list, float w, string label, string help)
        {
            var row = UiKit.Rect(list, "Row " + label).Size(0, DialogLayout.RowH);
            var hit = row.gameObject.AddComponent<Image>();
            hit.sprite = UiKit.White;
            hit.color = new Color(1f, 1f, 1f, 0f);
            var rule = UiKit.Picture(row, "Ruling", UiKit.White, new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.06f));
            rule.raycastTarget = false;
            rule.rectTransform.Place(0, 0, 1, 0, 0, 0, 0, -1);
            UiKit.Words(row, "Label", UiKit.Lead(DialogLayout.RowLabel(w), DialogLayout.Body), label, DialogLayout.Body, HudArt.Ink, UiKit.BodyFont, TextAnchor.MiddleLeft);
            var ring = UiKit.PaintedImage(row, "Focus", new Rect(0, 0, w, DialogLayout.RowH), "ring", HudArt.RingSize, HudArt.RingBorder, HudArt.Ring);
            ring.fillCenter = false;
            ring.gameObject.SetActive(false);
            var r = new OptionRow { Label = label, Help = help, Rect = row, Ring = ring.gameObject, Spot = UiKit.Explain(row.gameObject, options, help) };
            rows.Add(r);
            return r;
        }

        void Row(Transform list, float w, string label, string[] choices, Func<GameOptions, int> read, Action<int> apply, string help, bool live = false)
        {
            var r = Line(list, w, label, help);
            r.Live = live;
            r.Choices = choices;
            r.Read = read;
            r.Apply = apply;
            int index = Mathf.Clamp(read(root.Options), 0, choices.Length - 1);
            r.Picker = UiKit.MakePicker(r.Rect, "Picker " + label, DialogLayout.RowPicker(w), choices, index, apply);
            r.Ring.transform.SetAsLastSibling();
        }

        // Where the player's Total Annihilation: Kingdoms is, and a plate to
        // pick another. The game starts again on the new folder.
        void GameFolderRow(Transform list, float w, GameOptions o)
        {
            var r = Line(list, w, "Game folder", "Game folder: where your Total Annihilation: Kingdoms is. Change picks another and starts again");
            var change = UiKit.MakePlate(r.Rect, "Change", DialogLayout.RowPicker(w), () => { o.Save(); GameRoot.ChangeGameFolder?.Invoke(); }, null, null, null, DialogLayout.PickerValue);
            change.name = "Change game folder";
            r.Ring.transform.SetAsLastSibling();
            var where = UiKit.Rect(list, "Game folder line").Size(0, 30);
            var t = UiKit.Words(where, "Game folder path", new Rect(0, 0, 10, 10), GameRoot.GameFolder?.Invoke() ?? "", DialogLayout.Small,
                new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.6f), UiKit.BodyFont, TextAnchor.MiddleLeft);
            t.rectTransform.Place(0, 0, 1, 1, 12, 0, 12, 0);
        }

        // The rows that change the HUD say so when it is behind the panel.
        void PointRows()
        {
            bool behind = root.Flow.OptionsReturn == FlowState.Paused;
            foreach (var r in rows) r.Spot.Line = r.Help + (behind && r.Live ? LookBehind : "");
        }

        // Opened afresh: no focus, and the list at its top.
        void OpenOptions()
        {
            foreach (var r in rows) r.Ring.SetActive(false);
            focus = -1;
            options.Rest(OptionsResting);
            if (optionScroll != null) optionScroll.verticalNormalizedPosition = 1f;
        }

        void CloseOptions()
        {
            root.Options.Save();
            root.Flow.Fire(FlowEvent.Back);
        }

        // Every setting on the sheet as a new GameOptions has it, but the
        // display, which a window would leave in a surprising place.
        void Defaults()
        {
            var fresh = new GameOptions();
            foreach (var r in rows)
            {
                if (r.Picker == null || r.Label == "Display") continue;
                int i = Mathf.Clamp(r.Read(fresh), 0, r.Choices.Length - 1);
                r.Picker.SetChoices(r.Choices, i);
                r.Apply(i);
            }
        }

        void Focus(int i)
        {
            if (rows.Count == 0) return;
            focus = Mathf.Clamp(i, 0, rows.Count - 1);
            for (int k = 0; k < rows.Count; k++) rows[k].Ring.SetActive(k == focus);
            options.ShowHelp(rows[focus].Spot.Line);
            // Scrolls just far enough to show the row whole.
            Canvas.ForceUpdateCanvases();
            var content = optionScroll.content;
            var row = rows[focus].Rect;
            float top = -row.anchoredPosition.y - row.rect.height * (1f - row.pivot.y), bottom = top + row.rect.height;
            float view = optionScroll.viewport.rect.height, y = content.anchoredPosition.y;
            if (top < y) y = top;
            else if (bottom > y + view) y = bottom - view;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(y, 0f, Mathf.Max(0f, content.rect.height - view)));
        }

        // ---- Loading ----

        // The engine's stage with one ellipsis, however many dots it brought.
        public static string Stage(string stage)
        {
            string s = (stage ?? "").Trim().TrimEnd('.', '…', ' ');
            return s.Length == 0 ? "" : s + "...";
        }

        public static string SideName(string id, IReadOnlyList<SideInfo> sides)
        {
            if (string.IsNullOrEmpty(id)) return "Random";
            if (sides != null) foreach (var s in sides) if (s.Id == id) return s.Name;
            return id;
        }

        // Who is fighting: two seats on one line, more a line for each team,
        // and past three teams one paragraph.
        public static string SeatsLine(SkirmishSetup setup, IReadOnlyList<SideInfo> sides)
        {
            if (setup == null) return "";
            var teams = new List<int>();
            var members = new List<List<string>>();
            for (int i = 0; i < setup.Seats.Count; i++)
            {
                var seat = setup.Seats[i];
                if (seat.Kind == SeatKind.Closed) continue;
                string who = seat.Kind == SeatKind.Computer ? $"Computer ({seat.Difficulty})" : i == 0 ? "You" : "Player " + (i + 1);
                // A kingdom alone is a side of its own.
                int key = seat.Team >= 0 ? seat.Team : -1 - i;
                int t = teams.IndexOf(key);
                if (t < 0) { teams.Add(key); members.Add(new List<string>()); t = teams.Count - 1; }
                members[t].Add(who + ", " + SideName(seat.Side, sides));
            }
            if (members.Count == 2 && members[0].Count == 1 && members[1].Count == 1) return members[0][0] + "  against  " + members[1][0];
            var sb = new StringBuilder();
            for (int t = 0; t < members.Count; t++)
            {
                if (t > 0) sb.Append(members.Count > 3 ? "  against  " : "\nagainst  ");
                sb.Append(string.Join("  and  ", members[t]));
            }
            return sb.ToString();
        }

        static Text Shadowed(Text t)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.65f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
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
            var g = layout.Loading();
            foreach (var band in g.Bands)
                UiKit.PaintedPicture(s, "Band", band, "twist", x => HudArt.TwistTile(DialogLayout.Interlace, 20f, x, false, HudArt.Azurite), new Vector2(20f, 0f));
            foreach (var k in g.Knots) UiKit.PaintedPicture(s, "Knot", k, "knot", x => HudArt.SolomonKnot(DialogLayout.Knot, x));
            var silver = HudArt.Silver;
            Shadowed(UiKit.Words(s, "Name", UiKit.Lead(g.Name, DialogLayout.DialogTitle), GameRoot.Title, DialogLayout.DialogTitle, new Color(silver.r, silver.g, silver.b, 0.6f), UiKit.TitleFont));
            loadingTitle = Shadowed(UiKit.Words(s, "Title", UiKit.Lead(g.Title, DialogLayout.LoadingTitle), "", DialogLayout.LoadingTitle, HudArt.GoldHi, UiKit.TitleFont));
            loadingSeats = Shadowed(UiKit.Words(s, "Seats", g.Seats, "", DialogLayout.Sentence, silver, UiKit.BodyFont, TextAnchor.UpperCenter));
            UiKit.VellumBar(s, "Progress", g.Bar, out loadingFill);
            loadingPercent = Shadowed(UiKit.Words(s, "Percent", g.Percent, "0%", DialogLayout.Numbers, HudArt.GoldHi, UiKit.TitleFont, TextAnchor.MiddleLeft));
            loadingStage = Shadowed(UiKit.Words(s, "Stage", g.Stage, "", DialogLayout.Sentence, silver, UiKit.BodyFont));
            loadingTip = Shadowed(UiKit.Words(s, "Tip", g.Tip, "", DialogLayout.Sentence, new Color(silver.r, silver.g, silver.b, 0.78f), UiKit.BodyFont));
        }

        void RefreshLoading()
        {
            tip = "Tip: " + Tips[UnityEngine.Random.Range(0, Tips.Length)];
            FillLoading();
            UiKit.SetBar(loadingFill, 0);
            loadingPercent.text = "0%";
            loadingStage.text = "";
        }

        void FillLoading()
        {
            var map = root.CurrentMap();
            loadingTitle.text = MapCatalog.DisplayName(map) ?? "";
            loadingSeats.text = SeatsLine(root.Setup, root.Backend.Sides);
            loadingTip.text = tip;
            var tex = map != null ? Preview(map.Id) : null;
            loadingBackdrop.texture = tex;
            loadingBackdrop.enabled = tex != null;
            if (tex != null)
            {
                // Cover the screen, keeping the picture's shape.
                float sa = builtSize.x / Mathf.Max(1f, builtSize.y), ta = (float)tex.width / tex.height;
                loadingBackdrop.uvRect = ta > sa ? new Rect((1 - sa / ta) / 2, 0, sa / ta, 1) : new Rect(0, (1 - ta / sa) / 2, 1, ta / sa);
            }
        }

        // The battle HUD, on its own canvas under this one, so the pause,
        // options and result screens draw over it.
        void BuildHud()
        {
            hud = new BattleHud(root);
            hud.Root.transform.SetParent(root.transform, false);
            screens["Hud"] = hud.Root;
        }

        // ---- Pause, and the question before leaving ----

        // Save game is always in a development build, and in a skirmish-only
        // one while the stamp's switch says so.
        static bool OffersSave => !BuildStamp.SkirmishOnly || BuildStamp.SaveInAlpha;

        void BuildPause()
        {
            var s = DimScreen("Pause", Dim(0.5f));
            bool save = OffersSave;
            var box = layout.Pause(save);
            var d = UiKit.MakeDialog(s, box, "Paused", "Escape or F1 resumes");
            var p = d.transform;
            UiKit.MakePlate(p, "Resume", box.Local(box.Plate("Resume")), () => root.Flow.Fire(FlowEvent.Resume), d, "Back to the battle");
            if (save) UiKit.MakePlate(p, "Save game", box.Local(box.Plate("Save game")), SaveGame, d, "Keeps this battle to play again");
            UiKit.MakePlate(p, "Options", box.Local(box.Plate("Options")), () => root.Flow.Fire(FlowEvent.OpenOptions), d, "Interface, sound and display");
            UiKit.MakePlate(p, "Quit to menu", box.Local(box.Plate("Quit to menu")), () => AskLeave(true), d, "Leaves the battle. It is not saved.", "menubutton.wav");
            saveNote = UiKit.Words(p, "Save note", UiKit.Lead(box.Local(box.Note), DialogLayout.Note), "", DialogLayout.Note, HudArt.Verdigris, UiKit.BodyFont);
        }

        void SaveGame()
        {
            string path = null;
            // A lost battle being watched is not kept.
            bool ok = !root.Flow.Decided && root.SaveNow(out path);
            saveNote.text = ok ? "Saved as " + System.IO.Path.GetFileNameWithoutExtension(path) : "This game could not be saved.";
            saveNote.color = ok ? HudArt.Verdigris : HudArt.Minium;
        }

        // A screen inside Paused: Quit to menu asks it, Stay puts it away.
        void AskLeave(bool on)
        {
            leaving = on;
            if (root.Flow.State == FlowState.Paused) Show(FlowState.Paused);
        }

        void LeaveBattle()
        {
            leaving = false;
            root.Flow.Fire(FlowEvent.ToMenu);
        }

        void BuildLeave()
        {
            var s = DimScreen("Leave", Dim(0.7f));
            var box = layout.Leave();
            var d = UiKit.MakeDialog(s, box, "Leave the battle?", "Enter leaves. Escape returns to the battle.");
            var p = d.transform;
            UiKit.Words(p, "Question", box.Local(box.Note), "The battle ends here and is not saved.", DialogLayout.Body, HudArt.Ink, UiKit.BodyFont);
            UiKit.MakePlate(p, "Leave", box.Local(box.Plate("Leave")), LeaveBattle, d, "Ends the battle and goes to the main menu", "cancel.wav");
            UiKit.MakePlate(p, "Stay", box.Local(box.Plate("Stay")), () => AskLeave(false), d, "Back to the pause menu", "menubutton.wav");
        }

        // ---- Victory and defeat ----

        // The page itself is built when a battle ends, once the menus' art
        // and pages are there, and again for a new size.
        void BuildResult()
        {
            NewScreen("Result", false);
            results?.Dispose();
            results = null;
        }

        // The plate that brings the result back, where the sidebar's Menu
        // button and clock are.
        void BuildResults()
        {
            var s = NewScreen("Results", false);
            var px = builtSize * layout.Scale;
            int percent = HudLayout.NearestStop(root.Options != null ? root.Options.UiScale : HudLayout.DefaultScale);
            var hl = new HudLayout(Mathf.RoundToInt(px.x), Mathf.RoundToInt(px.y), percent);
            var head = hl.InBlock(HudLayout.Header);
            float k = hl.S / layout.Scale;
            var r = new Rect((head.x + 8f) * k, (head.y + 3f) * k, (head.width - 16f) * k, (head.height - 6f) * k);
            int size = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.55f), DialogLayout.Heading, DialogLayout.PlateLabel);
            UiKit.MakePlate(s, "Results", r, () => LookAtField(false), null, null, "menubutton.wav", size);
        }

        public void LookAtTheField() => LookAtField(true);

        void LookAtField(bool on)
        {
            if (on)
            {
                // The whole field, enemies and all. After a defeat the
                // computers still at war fight on.
                root.Backend.SeeAll(true);
                root.Backend.PlayOn();
                // The HUD may have changed size since the plate was made.
                DropScreen("Results");
                BuildResults();
                Order();
            }
            lookingAtField = on;
            if (root.Flow.State == FlowState.Victory || root.Flow.State == FlowState.Defeat) Show(root.Flow.State);
        }

        void RefreshResult()
        {
            if (results == null)
                results = new ResultScreen(root, lobby, (RectTransform)screens["Result"].transform, builtSize, layout.Scale);
            // The hold runs from the battle's end, however often the page opens.
            float hold = Mathf.Max(0f, resultShownAt + ResultHold - Time.unscaledTime);
            results.Show(resultWon, (int)resultTick, MapCatalog.DisplayName(root.CurrentMap()), hold, resultFresh);
            resultFresh = false;
        }

        // You, as the skirmish page names you, whatever the engine calls
        // the local seat, and everyone else by name.
        public static string ResultName(PlayerInfo pl) => pl.IsLocal ? "You" : pl.Name ?? "";

        // ---- Saved games ----

        // The saved games on the folder screen's page: vellum, the game's name
        // on its strip, and a dialog of that size with the saves as ruled rows.
        void BuildLoadList()
        {
            var s = NewScreen("Load", false);
            UiKit.VellumPage(s, "Page");
            UiKit.TitleStrip(s, GameRoot.Title);
            var box = layout.Folder();
            var d = UiKit.MakeDialog(s, box, "Load a game", "Pick a saved battle to play it again. Escape goes back.");
            var p = d.transform;
            var well = UiKit.PaintedImage(p, "Saves", box.Local(new Rect(box.List.x, box.Note.y, box.List.width, box.List.yMax - box.Note.y)),
                "trough", HudArt.TroughSize, HudArt.TroughBorder, HudArt.Trough);
            well.raycastTarget = true;
            saveItems = UiKit.ScrollList(well.transform, "Items", DialogLayout.ListRowGap);
            ((RectTransform)saveItems.parent.parent).Fill(12);
            loadEmpty = UiKit.Words(well.transform, "Empty", new Rect(0, 0, 10, 10), "", DialogLayout.Body,
                new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.6f), UiKit.BodyFont);
            loadEmpty.rectTransform.Fill(24);
            var leave = box.Plate("Leave");
            UiKit.MakePlate(p, "Back", box.Local(new Rect(box.Frame.center.x - DialogLayout.PlateW / 2f, leave.y, DialogLayout.PlateW, leave.height)),
                () => root.Flow.Fire(FlowEvent.Back), d, "Back to the skirmish");
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
                UiKit.ListRow(saveItems, "Save " + System.IO.Path.GetFileName(entry.Path), label, DialogLayout.ListRowH, DialogLayout.Row, HudArt.Ink, () =>
                {
                    UiKit.Play("menubutton.wav");
                    root.LoadSave(entry);
                });
            }
        }

        public void Dispose()
        {
            results?.Dispose();
            hud?.Dispose();
            lobby?.Dispose();
            foreach (var o in owned) World.Looks.Release(o);
            owned.Clear();
            if (canvas) World.Looks.Release(canvas.gameObject);
        }
    }
}
