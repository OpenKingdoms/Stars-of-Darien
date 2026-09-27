// MenuScreens.cs - every screen of the game flow on one canvas: the main
// menu, skirmish setup, options, loading, the in-game bar, the pause menu
// and the victory and defeat screens. Show() picks the screens for a flow
// state, and Tick() refreshes what changes while one is up.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class MenuScreens
    {
        public const int ReferenceWidth = 1920, ReferenceHeight = 1080;

        readonly GameRoot root;
        readonly Canvas canvas;
        readonly Dictionary<string, GameObject> screens = new Dictionary<string, GameObject>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        // Skirmish setup parts that change.
        RawImage preview, loadingBackdrop;
        Text mapTitle, mapInfo, loadingTitle, loadingStage, loadingTip, hudMana, hudClock, hudSelection, resultTitle, resultInfo, setupError;
        Image loadingFill, manaFill;
        readonly List<Button> mapButtons = new List<Button>();
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
        float resultShownAt;

        public string Visible { get; private set; } = "";

        static readonly string[] ColourNames = { "Blue", "Red", "White", "Black", "Green", "Yellow", "Purple", "Orange" };
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
            BuildMainMenu();
            BuildSkirmish();
            BuildOptions();
            BuildLoading();
            BuildHud();
            BuildPause();
            BuildResult();
        }

        public GameObject Screen(string name) => screens.TryGetValue(name, out var s) ? s : null;

        public void Show(FlowState state)
        {
            string[] on;
            switch (state)
            {
                case FlowState.MainMenu: on = new[] { "Menu" }; break;
                case FlowState.Skirmish: on = new[] { "Skirmish" }; RefreshSkirmish(); break;
                case FlowState.Options: on = root.Flow.OptionsReturn == FlowState.Paused ? new[] { "Hud", "Options" } : new[] { "Options" }; break;
                case FlowState.Loading: on = new[] { "Loading" }; RefreshLoading(); break;
                case FlowState.Playing: on = new[] { "Hud" }; break;
                case FlowState.Paused: on = new[] { "Hud", "Pause" }; break;
                case FlowState.Victory:
                case FlowState.Defeat: on = new[] { "Hud", "Result" }; RefreshResult(state == FlowState.Victory); break;
                default: on = new string[0]; break;
            }
            foreach (var kv in screens) kv.Value.SetActive(Array.IndexOf(on, kv.Key) >= 0);
            Visible = string.Join(",", on);
        }

        public void Tick()
        {
            if (!canvas) return;
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
                    var e = b.ReadEconomy(b.LocalPlayer);
                    hudMana.text = $"Mana  {Mathf.FloorToInt(e.Mana)} / {Mathf.FloorToInt(e.Storage)}   +{e.Income:0.#}/s";
                    UiKit.SetBar(manaFill, e.Storage > 0 ? e.Mana / e.Storage : 0);
                    int secs = (int)(b.Tick / (uint)Mathf.Max(1, b.TicksPerSecond));
                    hudClock.text = $"{secs / 60:00}:{secs % 60:00}";
                    int sel = root.World != null ? root.World.Entities.Selected.Count : 0;
                    hudSelection.text = sel > 0 ? (sel == 1 ? "1 unit selected" : sel + " units selected") : "";
                    break;
            }
        }

        // ---- Screens ----

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

        void BuildMainMenu()
        {
            var s = NewScreen("Menu", true);
            Heading(s, "OpenKingdoms", 120, 0.66f, 0.86f);
            var sub = UiKit.Label(s, "A remaster of Total Annihilation: Kingdoms", 34, UiKit.Pale);
            sub.rectTransform.Place(0, 0.6f, 1, 0.67f);
            var rule = UiKit.Picture(s, "Rule", UiKit.BarFill, new Color(1, 1, 1, 0.8f), true);
            rule.rectTransform.Place(0.5f, 0.59f, 0.5f, 0.59f, -260, -2, -260, -2);

            var col = UiKit.Rect(s, "Buttons").Place(0.5f, 0.2f, 0.5f, 0.55f, -230, 0, -230, 0);
            UiKit.Column(col, 22);
            UiKit.MakeButton(col, "Skirmish", () => root.Flow.Fire(FlowEvent.OpenSkirmish), 38).GetComponent<RectTransform>().Size(460, 84);
            UiKit.MakeButton(col, "Options", () => root.Flow.Fire(FlowEvent.OpenOptions), 38).GetComponent<RectTransform>().Size(460, 84);
            UiKit.MakeButton(col, "Quit", () => root.Flow.Fire(FlowEvent.Exit), 38).GetComponent<RectTransform>().Size(460, 84);

            var foot = UiKit.Label(s, $"Engine: {root.Backend.Name}.   Free and open, played with your own game files.", 24, UiKit.Dim);
            foot.rectTransform.Place(0, 0, 1, 0, 0, 20, 0, -60);
        }

        void BuildSkirmish()
        {
            var s = NewScreen("Skirmish", true);
            Heading(s, "Skirmish", 72, 0.88f, 0.98f);

            // Maps.
            var list = UiKit.Panel(s, "Maps", false).Place(0, 0, 0, 1, 60, 130, -440, 150);
            var listTitle = UiKit.Label(list, "Maps", 34, UiKit.Gold, TextAnchor.MiddleCenter, true);
            listTitle.rectTransform.Place(0, 1, 1, 1, 0, -70, 0, 10);
            var items = UiKit.Rect(list, "Items").Place(0, 0, 1, 1, 24, 24, 24, 80);
            UiKit.Column(items, 10);
            foreach (var m in root.Backend.Maps)
            {
                var id = m.Id;
                var b = UiKit.MakeButton(items, m.Name, () => { root.Setup.MapId = id; RefreshSkirmish(); }, 28);
                b.GetComponent<RectTransform>().Size(0, 62);
                b.name = "Map " + id;
                mapButtons.Add(b);
            }

            // Preview and details.
            var pv = UiKit.Panel(s, "Preview", false).Place(0, 0, 0, 1, 470, 130, -1010, 150);
            var frame = UiKit.Picture(pv, "Mat", UiKit.White, new Color(0.05f, 0.04f, 0.03f));
            frame.rectTransform.Place(0.5f, 1, 0.5f, 1, -240, -510, -240, 30);
            preview = UiKit.Rect(frame.transform, "Image").Fill(4).gameObject.AddComponent<RawImage>();
            preview.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            mapTitle = UiKit.Label(pv, "", 40, UiKit.Gold, TextAnchor.UpperCenter, true);
            mapTitle.rectTransform.Place(0, 1, 1, 1, 20, -620, 20, 560);
            mapInfo = UiKit.Label(pv, "", 27, UiKit.Pale, TextAnchor.UpperCenter);
            mapInfo.rectTransform.Place(0, 0, 1, 1, 30, 20, 30, 630);

            // Seats and options.
            var seats = UiKit.Panel(s, "Seats", false).Place(0, 0, 1, 1, 1040, 130, 60, 150);
            var seatTitle = UiKit.Label(seats, "Kingdoms", 34, UiKit.Gold, TextAnchor.MiddleCenter, true);
            seatTitle.rectTransform.Place(0, 1, 1, 1, 0, -70, 0, 10);
            var rows = UiKit.Rect(seats, "Rows").Place(0, 0, 1, 1, 24, 24, 24, 80);
            UiKit.Column(rows, 12);
            var sideIds = new List<string> { "" };
            var sideNames = new List<string> { "Random" };
            foreach (var side in root.Backend.Sides) { sideIds.Add(side.Id); sideNames.Add(side.Name); }
            for (int i = 0; i < root.Setup.Seats.Count; i++) SeatRow(rows, i, sideIds, sideNames);

            var gap = UiKit.Rect(rows, "Gap").Size(0, 16);
            var optTitle = UiKit.Label(rows, "Options", 30, UiKit.Gold, TextAnchor.MiddleLeft, true);
            optTitle.rectTransform.Size(0, 46);
            var set = root.Setup;
            OptionRow(rows, "Line of sight", new[] { "On", "Off" }, set.LineOfSight ? 0 : 1, i => set.LineOfSight = i == 0);
            OptionRow(rows, "Map", new[] { "Unexplored", "Revealed" }, set.MapRevealed ? 1 : 0, i => set.MapRevealed = i == 1);
            var mana = new[] { 500, 1000, 2500, 5000 };
            OptionRow(rows, "Starting mana", Array.ConvertAll(mana, m => m.ToString()), Math.Max(0, Array.IndexOf(mana, set.StartMana)), i => set.StartMana = mana[i]);
            OptionRow(rows, "Weather", new[] { "By map", "Clear", "Rain", "Snow", "Fog" }, (int)root.Options.Weather, i => { root.Options.Weather = (WeatherChoice)i; root.Options.Save(); });

            setupError = UiKit.Label(s, "", 26, new Color(1f, 0.5f, 0.4f));
            setupError.rectTransform.Place(0, 0, 1, 0, 600, 40, 600, -110);
            var back = UiKit.MakeButton(s, "Back", () => root.Flow.Fire(FlowEvent.Back), 32);
            back.GetComponent<RectTransform>().Place(0, 0, 0, 0, 60, 40, -360, -110);
            var start = UiKit.MakeButton(s, "Start", StartGame, 36);
            start.name = "Start";
            start.GetComponent<RectTransform>().Place(1, 0, 1, 0, -360, 40, 60, -110);
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
            if (humans == 0 || players < 2) { setupError.text = "A game needs you and at least one computer player."; return; }
            if (map != null && players > map.MaxPlayers) { setupError.text = $"{map.Name} holds {map.MaxPlayers} players."; return; }
            setupError.text = "";
            root.Setup.Seed = (uint)Environment.TickCount;
            root.Flow.Fire(FlowEvent.Start);
        }

        void SeatRow(Transform parent, int index, List<string> sideIds, List<string> sideNames)
        {
            var seat = root.Setup.Seats[index];
            var row = UiKit.Rect(parent, "Seat " + index).Size(0, 58);
            UiKit.Row(row, 10);
            var name = UiKit.Label(row, index == 0 ? "You" : "Seat " + (index + 1), 28, UiKit.Pale, TextAnchor.MiddleLeft, true);
            name.rectTransform.Size(92, 0);
            if (index == 0)
            {
                var fixedKind = UiKit.Label(row, "Human", 22, UiKit.Dim);
                fixedKind.rectTransform.Size(150, 0);
            }
            else
                UiKit.Cycle(row, new[] { "Closed", "Computer" }, seat.Kind == SeatKind.Computer ? 1 : 0,
                    i => seat.Kind = i == 1 ? SeatKind.Computer : SeatKind.Closed, 21).GetComponent<RectTransform>().Size(150, 0);
            UiKit.Cycle(row, sideNames.ToArray(), Math.Max(0, sideIds.IndexOf(seat.Side)), i => seat.Side = sideIds[i], 21)
                .GetComponent<RectTransform>().Size(140, 0);
            var colour = UiKit.Cycle(row, ColourNames, seat.Colour % ColourNames.Length, null, 21);
            colour.GetComponent<RectTransform>().Size(120, 0);
            var swatch = colour.GetComponent<Image>();
            swatch.color = Tint(seat.Colour);
            colour.Init(ColourNames, seat.Colour % ColourNames.Length, i => { seat.Colour = i; swatch.color = Tint(i); }, colour.GetComponentInChildren<Text>());
            UiKit.Cycle(row, new[] { "Team 1", "Team 2", "Team 3", "Team 4" }, seat.Team % 4, i => seat.Team = i, 21)
                .GetComponent<RectTransform>().Size(110, 0);
            if (index > 0)
                UiKit.Cycle(row, new[] { "Easy", "Normal", "Hard", "Brutal" }, (int)seat.Difficulty, i => seat.Difficulty = (AiDifficulty)i, 21)
                    .GetComponent<RectTransform>().Size(116, 0);
        }

        static Color Tint(int colour)
        {
            var c = (Color)MockBackend.Palette[Mathf.Abs(colour) % MockBackend.Palette.Length];
            return Color.Lerp(c, Color.white, 0.35f);
        }

        static void OptionRow(Transform parent, string label, string[] choices, int index, Action<int> changed)
        {
            var row = UiKit.Rect(parent, label).Size(0, 54);
            UiKit.Row(row, 16);
            var l = UiKit.Label(row, label, 27, UiKit.Pale, TextAnchor.MiddleLeft);
            l.rectTransform.Size(260, 0);
            UiKit.Cycle(row, choices, index, changed).GetComponent<RectTransform>().Size(260, 0);
        }

        void RefreshSkirmish()
        {
            var map = root.CurrentMap();
            if (map == null) return;
            root.Setup.MapId = map.Id;
            foreach (var b in mapButtons)
            {
                bool on = b.name == "Map " + map.Id;
                b.GetComponent<Image>().color = on ? new Color(1.2f, 1.05f, 0.75f) : Color.white;
                b.GetComponentInChildren<Text>().color = on ? UiKit.GoldBright : UiKit.Gold;
            }
            var tex = Preview(map.Id);
            preview.texture = tex;
            if (tex != null) preview.GetComponent<AspectRatioFitter>().aspectRatio = (float)tex.width / tex.height;
            mapTitle.text = map.Name;
            string climate = string.IsNullOrEmpty(map.Climate) ? "" : "   " + char.ToUpper(map.Climate[0]) + map.Climate.Substring(1);
            mapInfo.text = $"{map.Description}\n\n{map.Size.x:0} by {map.Size.y:0}   Up to {map.MaxPlayers} players{climate}";
            if (setupError != null) setupError.text = root.LastError ?? "";
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
            var p = UiKit.Panel(s, "Panel", false).Place(0.5f, 0.5f, 0.5f, 0.5f, -420, -440, -420, -440);
            Heading(p, "Options", 60, 0.86f, 0.98f);
            var rows = UiKit.Rect(p, "Rows").Place(0, 0, 1, 1, 60, 150, 60, 150);
            UiKit.Column(rows, 14);
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
            OptionRow(rows, "Post effects", new[] { "On", "Off" }, o.PostEffects ? 0 : 1, i => o.PostEffects = i == 0);
            OptionRow(rows, "Game speed", new[] { "Normal", "Fast" }, o.GameSpeed - 1, i => o.GameSpeed = i + 1);
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
            back.GetComponent<RectTransform>().Place(0.5f, 0, 0.5f, 0, -180, 50, -180, -130);
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

        void BuildHud()
        {
            var s = NewScreen("Hud", false);
            var bar = UiKit.Picture(s, "Top", UiKit.Stone, new Color(0.85f, 0.82f, 0.78f, 0.96f));
            bar.rectTransform.Place(0, 1, 1, 1, 0, -58, 0, 0);
            var trim = UiKit.Picture(bar.transform, "Trim", UiKit.BarFill, Color.white, true);
            trim.rectTransform.Place(0, 0, 1, 0, 0, -3, 0, -3);
            var manaBar = UiKit.Bar(bar.transform, "Mana", out manaFill);
            manaBar.rectTransform.Place(0, 0.5f, 0, 0.5f, 24, -9, -284, -9);
            hudMana = UiKit.Label(bar.transform, "", 26, UiKit.Pale, TextAnchor.MiddleLeft);
            hudMana.rectTransform.Place(0, 0, 0, 1, 300, 0, -760, 0);
            hudClock = UiKit.Label(bar.transform, "", 28, UiKit.Gold, TextAnchor.MiddleCenter, true);
            hudClock.rectTransform.Place(0.5f, 0, 0.5f, 1, -100, 0, -100, 0);
            var menu = UiKit.MakeButton(bar.transform, "Menu", () => root.Flow.Fire(FlowEvent.Pause), 24);
            menu.GetComponent<RectTransform>().Place(1, 0, 1, 1, -170, 6, 14, 6);
            hudSelection = UiKit.Label(s, "", 26, UiKit.Pale, TextAnchor.LowerLeft);
            hudSelection.rectTransform.Place(0, 0, 0.5f, 0, 24, 18, 0, -60);
        }

        void BuildPause()
        {
            var s = NewScreen("Pause", false);
            var dim = UiKit.Picture(s, "Dim", UiKit.White, new Color(0, 0, 0, 0.5f));
            dim.rectTransform.Fill();
            var p = UiKit.Panel(s, "Panel", false).Place(0.5f, 0.5f, 0.5f, 0.5f, -300, -280, -300, -280);
            Heading(p, "Paused", 60, 0.76f, 0.96f);
            var col = UiKit.Rect(p, "Buttons").Place(0, 0, 1, 1, 70, 60, 70, 150);
            UiKit.Column(col, 18);
            UiKit.MakeButton(col, "Resume", () => root.Flow.Fire(FlowEvent.Resume), 32).GetComponent<RectTransform>().Size(0, 72);
            UiKit.MakeButton(col, "Options", () => root.Flow.Fire(FlowEvent.OpenOptions), 32).GetComponent<RectTransform>().Size(0, 72);
            UiKit.MakeButton(col, "Quit to menu", () => root.Flow.Fire(FlowEvent.ToMenu), 32).GetComponent<RectTransform>().Size(0, 72);
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
            foreach (var o in owned) World.Looks.Release(o);
            owned.Clear();
            if (canvas) World.Looks.Release(canvas.gameObject);
        }
    }
}
