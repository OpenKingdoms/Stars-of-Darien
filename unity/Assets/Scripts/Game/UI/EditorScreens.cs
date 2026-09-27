// EditorScreens.cs - the map editor's two screens: picking a map to edit,
// and the tools while editing. The tool panel on the left chooses raise,
// lower, flatten, smooth, paint, features or erase, the brush size and
// strength, and shows the picture library or the feature list a page at
// a time. The bar on top names the map, saves it as a new map, and goes
// back to the menu.
using System.Collections.Generic;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class EditorScreens
    {
        const int PageSize = 24;

        readonly GameRoot root;
        readonly MenuScreens screens;
        RectTransform setupItems, palette;
        RawImage setupPreview;
        Text setupTitle, note, pageLabel, strokeLabel;
        InputField nameField, searchField;
        string chosenMap;
        int page;
        uint[] library;
        readonly Dictionary<EditTool, Button> toolButtons = new Dictionary<EditTool, Button>();
        readonly List<Object> owned = new List<Object>();

        public EditorScreens(GameRoot root, MenuScreens screens)
        {
            this.root = root;
            this.screens = screens;
            BuildSetup();
            BuildTools();
        }

        // ---- Picking a map ----

        void BuildSetup()
        {
            var s = screens.NewScreenFor("EditorSetup", true);
            var head = UiKit.Label(s, "Map editor", 72, UiKit.Gold, TextAnchor.MiddleCenter, true);
            head.rectTransform.Place(0, 0.88f, 1, 0.98f);
            var list = UiKit.Panel(s, "Maps", false).Place(0, 0, 0, 1, 60, 130, -520, 150);
            setupItems = UiKit.ScrollList(list, "Items", 10);
            ((RectTransform)setupItems.parent.parent).Place(0, 0, 1, 1, 24, 24, 24, 24);
            var pv = UiKit.Panel(s, "Preview", false).Place(0, 0, 1, 1, 560, 130, 60, 150);
            setupPreview = UiKit.Rect(pv, "Image").Place(0, 0, 1, 1, 30, 120, 30, 30).gameObject.AddComponent<RawImage>();
            setupPreview.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            setupTitle = UiKit.Label(pv, "Pick a map to change, or to start a new one from.", 34, UiKit.Gold, TextAnchor.MiddleCenter, true);
            setupTitle.rectTransform.Place(0, 0, 1, 0, 20, 30, 20, -100);
            var back = UiKit.MakeButton(s, "Back", () => root.Flow.Fire(FlowEvent.Back), 32);
            back.GetComponent<RectTransform>().Place(0, 0, 0, 0, 60, 40, -360, -110);
            var open = UiKit.MakeButton(s, "Open", Open, 36);
            open.name = "Open";
            open.GetComponent<RectTransform>().Place(1, 0, 1, 0, -360, 40, 60, -110);
        }

        public void RefreshSetup()
        {
            for (int i = setupItems.childCount - 1; i >= 0; i--) Object.Destroy(setupItems.GetChild(i).gameObject);
            foreach (var m in root.Backend.Maps)
            {
                var id = m.Id;
                UiKit.MakeButton(setupItems, m.Name, () => Choose(id), 28).GetComponent<RectTransform>().Size(0, 62);
            }
            if (chosenMap == null && root.Backend.Maps.Count > 0) Choose(root.Backend.Maps[0].Id);
        }

        void Choose(string id)
        {
            chosenMap = id;
            var tex = screens.PreviewFor(id);
            setupPreview.texture = tex;
            if (tex != null) setupPreview.GetComponent<AspectRatioFitter>().aspectRatio = (float)tex.width / tex.height;
            foreach (var m in root.Backend.Maps) if (m.Id == id) setupTitle.text = m.Name;
        }

        // Opens the chosen map in the editor through the usual loading screen.
        public void Open()
        {
            if (chosenMap == null) return;
            root.Setup = GameRoot.DefaultSetup(root.Backend);
            root.Setup.MapId = chosenMap;
            root.Flow.Fire(FlowEvent.Start);
        }

        // ---- The tools ----

        void BuildTools()
        {
            var s = screens.NewScreenFor("Editor", false);
            var top = UiKit.Picture(s, "Top", UiKit.Stone, new Color(0.85f, 0.82f, 0.78f, 0.96f));
            top.rectTransform.Place(0, 1, 1, 1, 0, -64, 0, 0);
            var row = UiKit.Rect(top.transform, "Row").Fill(8);
            UiKit.Row(row, 12);
            var label = UiKit.Label(row, "Map name", 26, UiKit.Gold, TextAnchor.MiddleLeft, true);
            label.rectTransform.Size(160, 0);
            nameField = UiKit.Input(row, "A name for the new map", 26);
            nameField.GetComponent<RectTransform>().Size(440, 0);
            UiKit.MakeButton(row, "Save as new map", Save, 24).GetComponent<RectTransform>().Size(260, 0);
            note = UiKit.Label(row, "", 24, UiKit.Pale, TextAnchor.MiddleLeft);
            note.rectTransform.Size(560, 0);
            UiKit.MakeButton(row, "Menu", () => root.Flow.Fire(FlowEvent.ToMenu), 24).GetComponent<RectTransform>().Size(150, 0);

            var panel = UiKit.Panel(s, "Tools", false).Place(0, 0, 0, 1, 0, 0, -380, 64);
            var col = UiKit.Rect(panel, "Column").Place(0, 0, 1, 1, 18, 18, 18, 18);
            UiKit.Column(col, 8);
            var tools = UiKit.Rect(col, "ToolGrid").Size(0, 210);
            var grid = tools.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(166, 62);
            grid.spacing = new Vector2(8, 8);
            foreach (EditTool t in System.Enum.GetValues(typeof(EditTool)))
            {
                var tool = t;
                toolButtons[t] = UiKit.MakeButton(tools, t == EditTool.Feature ? "Features" : t.ToString(), () => { SetTool(tool); }, 24);
            }
            OptionRow(col, "Brush", new[] { "1", "2", "3", "4", "6", "8", "12" }, 2, i => { if (root.Editor != null) root.Editor.Radius = new[] { 1, 2, 3, 4, 6, 8, 12 }[i]; });
            OptionRow(col, "Strength", new[] { "Gentle", "Firm", "Strong" }, 1, i => { if (root.Editor != null) root.Editor.Strength = new[] { 1, 3, 8 }[i]; });
            searchField = UiKit.Input(col, "Search features", 22);
            searchField.GetComponent<RectTransform>().Size(0, 48);
            searchField.onValueChanged.AddListener(_ => { page = 0; RefreshPalette(); });
            var pager = UiKit.Rect(col, "Pager").Size(0, 50);
            UiKit.Row(pager, 8);
            UiKit.MakeButton(pager, "<", () => { page = Mathf.Max(0, page - 1); RefreshPalette(); }, 26).GetComponent<RectTransform>().Size(70, 0);
            pageLabel = UiKit.Label(pager, "", 22, UiKit.Pale);
            pageLabel.rectTransform.Size(180, 0);
            UiKit.MakeButton(pager, ">", () => { page++; RefreshPalette(); }, 26).GetComponent<RectTransform>().Size(70, 0);
            palette = UiKit.ScrollList(col, "Palette", 6);
            ((RectTransform)palette.parent.parent).Size(0, 420);
            strokeLabel = UiKit.Label(s, "", 22, UiKit.Pale, TextAnchor.LowerRight);
            strokeLabel.rectTransform.Place(1, 0, 1, 0, -600, 16, 20, -50);
        }

        static void OptionRow(Transform parent, string label, string[] choices, int index, System.Action<int> changed)
        {
            var row = UiKit.Rect(parent, label).Size(0, 52);
            UiKit.Row(row, 12);
            var l = UiKit.Label(row, label, 24, UiKit.Pale, TextAnchor.MiddleLeft);
            l.rectTransform.Size(120, 0);
            UiKit.Cycle(row, choices, index, changed, 22).GetComponent<RectTransform>().Size(200, 0);
        }

        public void RefreshTools()
        {
            var map = root.CurrentMap();
            nameField.text = map != null ? map.Name + " edited" : "new map";
            note.text = "";
            library = null;
            page = 0;
            SetTool(root.Editor != null ? root.Editor.Tool : EditTool.Raise);
        }

        void SetTool(EditTool tool)
        {
            if (root.Editor != null) root.Editor.Tool = tool;
            foreach (var kv in toolButtons)
                kv.Value.GetComponent<Image>().color = kv.Key == tool ? new Color(1.2f, 1.05f, 0.75f) : Color.white;
            page = 0;
            RefreshPalette();
        }

        void RefreshPalette()
        {
            for (int i = palette.childCount - 1; i >= 0; i--) Object.Destroy(palette.GetChild(i).gameObject);
            foreach (var o in owned) World.Looks.Release(o);
            owned.Clear();
            var tool = root.Editor != null ? root.Editor.Tool : EditTool.Raise;
            searchField.gameObject.SetActive(tool == EditTool.Feature);
            if (tool == EditTool.Paint) PaintPalette();
            else if (tool == EditTool.Feature) FeaturePalette();
            else pageLabel.text = "";
        }

        void PaintPalette()
        {
            if (library == null) library = root.Backend.ChunkLibrary();
            int pages = Mathf.Max(1, (library.Length + PageSize - 1) / PageSize);
            page = Mathf.Min(page, pages - 1);
            pageLabel.text = $"{page + 1} of {pages}";
            HorizontalLayoutGroup row = null;
            for (int i = page * PageSize; i < Mathf.Min(library.Length, (page + 1) * PageSize); i++)
            {
                if ((i - page * PageSize) % 3 == 0)
                {
                    var r = UiKit.Rect(palette, "Row").Size(0, 104);
                    row = UiKit.Row(r, 6);
                }
                uint id = library[i];
                var btn = UiKit.MakeButton(row.transform, "", () => { if (root.Editor != null) root.Editor.PaintChunk = id; HighlightPaint(id); }, 18);
                btn.name = "Chunk " + id;
                btn.GetComponent<RectTransform>().Size(104, 104);
                var tex = UiKit.ToTexture(root.Backend.ChunkPicture(id), true);
                if (tex != null) owned.Add(tex);
                var img = UiKit.Rect(btn.transform, "Picture").Fill(6).gameObject.AddComponent<RawImage>();
                img.texture = tex;
                img.raycastTarget = false;
            }
            HighlightPaint(root.Editor != null ? root.Editor.PaintChunk : 0);
        }

        void HighlightPaint(uint id)
        {
            foreach (var img in palette.GetComponentsInChildren<Button>())
                img.GetComponent<Image>().color = img.name == "Chunk " + id ? new Color(1.4f, 1.2f, 0.7f) : Color.white;
        }

        void FeaturePalette()
        {
            var defs = root.Backend.FeatureDefs;
            string q = searchField.text ?? "";
            var shown = new List<FeatureDef>();
            foreach (var d in defs)
                if (q.Length == 0 || d.Name.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) shown.Add(d);
            int pages = Mathf.Max(1, (shown.Count + PageSize - 1) / PageSize);
            page = Mathf.Min(page, pages - 1);
            pageLabel.text = $"{page + 1} of {pages}";
            for (int i = page * PageSize; i < Mathf.Min(shown.Count, (page + 1) * PageSize); i++)
            {
                var d = shown[i];
                int id = d.Id;
                var btn = UiKit.MakeButton(palette, d.Name, () => { if (root.Editor != null) root.Editor.FeatureDef = id; HighlightFeature(id); }, 22);
                btn.name = "Feature " + id;
                btn.GetComponent<RectTransform>().Size(0, 50);
            }
            HighlightFeature(root.Editor != null ? root.Editor.FeatureDef : -1);
        }

        void HighlightFeature(int id)
        {
            foreach (var b in palette.GetComponentsInChildren<Button>())
                b.GetComponent<Image>().color = b.name == "Feature " + id ? new Color(1.4f, 1.2f, 0.7f) : Color.white;
        }

        void Save()
        {
            string name = (nameField.text ?? "").Trim();
            if (name.Length == 0) { note.text = "Give the map a name first."; return; }
            if (root.Backend.SaveMap(name))
            {
                note.text = "Saved as " + name + ". It is in the skirmish list now.";
                root.Editor?.MarkSaved();
            }
            else note.text = "The map could not be saved.";
        }

        public void Tick()
        {
            if (strokeLabel == null || root.Editor == null) return;
            strokeLabel.text = root.Editor.Dirty ? $"{root.Editor.Strokes} changes, not saved yet" : "";
        }
    }
}
