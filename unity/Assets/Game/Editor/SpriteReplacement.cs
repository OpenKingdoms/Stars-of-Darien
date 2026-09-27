// SpriteReplacement.cs - the work list of sprite-only features and their
// 3D replacements. Reads the local catalog that tools/sprite-replace/
// extract.py writes from the player's own files (never committed), shows
// each sprite beside its drop-in model when there is one, counts progress,
// and opens Blender on a template for the one selected.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenKingdomsUnity.Studio
{
    public sealed class SpriteReplacement : EditorWindow
    {
        const string TemplateKey = "oku.sprites.template";
        static string CatalogDir { get => StudioTargets.CatalogDir; set => StudioTargets.CatalogDir = value; }
        static string Blender { get => SettingsWindow.Blender; set => SettingsWindow.Blender = value; }
        static string Template { get => EditorPrefs.GetString(TemplateKey, "tools/sprite-replace/template.py"); set => EditorPrefs.SetString(TemplateKey, value); }

        public sealed class Entry
        {
            public string Name, World, Description, Category, Seq;
            public int Maps;
            public Vector2Int Footprint;
            public int Height;
            public string Status;       // from the catalog: "sprite" or "3d"
            public string Override;     // the drop-in model's path, or null
            public bool Generated;
        }

        enum Filter { All, SpriteOnly, HandMade, Generated }

        List<Entry> entries = new List<Entry>();
        string loadError;
        string search = "";
        Filter show;
        int selected = -1;
        Vector2 scroll;
        StudioPreview preview;
        Texture2D spriteTex;
        string spriteFor, previewFor;

        [MenuItem("OpenKingdoms/Studio/Sprite Replacement", priority = 22)]
        public static void Open() => GetWindow<SpriteReplacement>("Sprite Replacement").minSize = new Vector2(820, 460);

        void OnEnable()
        {
            preview = new StudioPreview();
            Reload();
        }

        void OnDisable()
        {
            preview?.Dispose();
            if (spriteTex != null) DestroyImmediate(spriteTex);
        }

        void OnFocus() => Refresh();

        public static List<Entry> ReadCatalog(string json, out string error)
        {
            error = null;
            var list = new List<Entry>();
            object root;
            try { root = MiniJson.Parse(json); }
            catch (Exception e) { error = "catalog.json: " + e.Message; return list; }
            if (!(root is List<object> items)) { error = "catalog.json is not a list"; return list; }
            foreach (var o in items)
            {
                var fp = MiniJson.Arr(o, "footprint");
                list.Add(new Entry
                {
                    Name = MiniJson.Text(o, "name", "?"), World = MiniJson.Text(o, "world", ""),
                    Description = MiniJson.Text(o, "description", ""), Category = MiniJson.Text(o, "category", ""),
                    Seq = MiniJson.Text(o, "seq", ""), Maps = MiniJson.Int(o, "maps", 0),
                    Footprint = fp != null && fp.Count == 2 ? new Vector2Int((int)(double)fp[0], (int)(double)fp[1]) : Vector2Int.one,
                    Height = MiniJson.Int(o, "height", 0), Status = MiniJson.Text(o, "status", "sprite"),
                });
            }
            return list;
        }

        void Reload()
        {
            string path = Path.Combine(CatalogDir, "catalog.json");
            loadError = null;
            entries = File.Exists(path) ? ReadCatalog(File.ReadAllText(path), out loadError) : new List<Entry>();
            if (!File.Exists(path)) loadError = $"No catalog at {path}. Run tools/sprite-replace/extract.py on your game files first.";
            entries = entries.OrderByDescending(e => e.Maps).ThenBy(e => e.Name).ToList();
            Refresh();
        }

        // Which entries have a drop-in model now.
        void Refresh()
        {
            OverrideLoader.Reset(OverrideIndex.Scan(OverrideLoader.ProjectDir));
            var index = OverrideLoader.Index;
            foreach (var e in entries)
            {
                e.Override = index.Find(OverrideKind.Feature, e.Name, e.Seq);
                e.Generated = index.IsGenerated(e.Override);
            }
            previewFor = null;
            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Catalog", EditorStyles.miniLabel, GUILayout.Width(46));
                CatalogDir = EditorGUILayout.DelayedTextField(CatalogDir, EditorStyles.toolbarTextField, GUILayout.Width(200));
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60))) Reload();
                GUILayout.FlexibleSpace();
                show = (Filter)EditorGUILayout.EnumPopup(show, EditorStyles.toolbarDropDown, GUILayout.Width(100));
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.Width(180));
            }
            if (loadError != null) { EditorGUILayout.HelpBox(loadError, MessageType.Warning); if (entries.Count == 0) return; }

            int modelled = entries.Count(e => e.Override != null), hand = entries.Count(e => e.Override != null && !e.Generated);
            var bar = GUILayoutUtility.GetRect(100, 20, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(bar, entries.Count > 0 ? (float)modelled / entries.Count : 0,
                $"{modelled} of {entries.Count} have a 3D model ({hand} hand-made, {modelled - hand} generated)");

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        if (!Visible(e)) continue;
                        string mark = e.Override == null ? "sprite" : e.Generated ? "generated" : "3D";
                        var style = i == selected ? EditorStyles.boldLabel : EditorStyles.label;
                        if (GUILayout.Button($"{e.Name}   [{mark}]   {e.Maps} maps", style)) selected = i;
                    }
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    if (selected < 0 || selected >= entries.Count) { GUILayout.Label("Pick a feature on the left."); return; }
                    DrawEntry(entries[selected]);
                }
            }
        }

        bool Visible(Entry e)
        {
            if (search.Length > 0 && e.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                e.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            switch (show)
            {
                case Filter.SpriteOnly: return e.Override == null;
                case Filter.HandMade: return e.Override != null && !e.Generated;
                case Filter.Generated: return e.Generated;
                default: return true;
            }
        }

        void DrawEntry(Entry e)
        {
            GUILayout.Label($"{e.Name}   {e.Description}", EditorStyles.largeLabel);
            GUILayout.Label($"{e.World}, {e.Category}, footprint {e.Footprint.x} by {e.Footprint.y}, {e.Height / 16f:0.#} cells tall, on {e.Maps} maps", EditorStyles.miniLabel);
            GUILayout.Label(e.Override != null ? "Model: " + e.Override : "No 3D model yet.", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var left = GUILayoutUtility.GetRect(100, 10000, 160, 10000);
                var right = GUILayoutUtility.GetRect(100, 10000, 160, 10000);
                var sprite = Sprite(e.Name);
                EditorGUI.DrawRect(left, new Color(0.17f, 0.17f, 0.19f));
                if (sprite != null) GUI.DrawTexture(left, sprite, ScaleMode.ScaleToFit);
                else EditorGUI.HelpBox(left, "No sprite picture in the catalog.", MessageType.Info);
                if (e.Override != null)
                {
                    if (previewFor != e.Override) { preview.SetTemplate(OverrideLoader.Template(e.Override)); previewFor = e.Override; }
                    if (preview.Draw(right)) Repaint();
                }
                else EditorGUI.HelpBox(right, "The game draws the sprite on a card.", MessageType.None);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Blender template", GUILayout.Height(26))) OpenBlender(e);
                if (e.Override != null && GUILayout.Button("Show model file", GUILayout.Height(26), GUILayout.Width(130)))
                    EditorUtility.RevealInFinder(Path.Combine(OverrideLoader.ProjectDir, e.Override));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Blender = EditorGUILayout.TextField("Blender", Blender);
                Template = EditorGUILayout.TextField("Template script", Template);
            }
        }

        Texture2D Sprite(string name)
        {
            if (spriteFor == name) return spriteTex;
            if (spriteTex != null) DestroyImmediate(spriteTex);
            spriteTex = null;
            spriteFor = name;
            string path = Path.Combine(CatalogDir, "sprites", name + ".png");
            if (!File.Exists(path)) return null;
            spriteTex = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point };
            spriteTex.LoadImage(File.ReadAllBytes(path));
            return spriteTex;
        }

        // Blender opens the template with the feature's name, sprite and
        // the hand-made model path it should save to.
        void OpenBlender(Entry e)
        {
            string repo = Path.GetDirectoryName(OverrideLoader.ProjectDir);
            string template = Path.IsPathRooted(Template) ? Template : Path.Combine(repo, Template);
            if (!File.Exists(Blender)) { EditorUtility.DisplayDialog("Blender", "Blender is not at " + Blender, "OK"); return; }
            if (!File.Exists(template))
            {
                EditorUtility.DisplayDialog("Blender template", "The template script is not there yet: " + template, "OK");
                return;
            }
            string sprite = Path.Combine(CatalogDir, "sprites", e.Name + ".png");
            string output = Path.Combine(OverrideLoader.ProjectDir, OverrideIndex.Folder(OverrideKind.Feature), e.Name + ".glb");
            var args = $"-P \"{template}\" -- \"{e.Name}\" \"{sprite}\" \"{output}\" {e.Footprint.x} {e.Footprint.y} {e.Height}";
            try { Process.Start(new ProcessStartInfo(Blender, args) { UseShellExecute = false }); }
            catch (Exception ex) { Debug.LogError("Blender did not start: " + ex.Message); }
        }
    }
}
