// StudioWindow.cs - Studio Mode's panel: what the model replaces, checks
// and fixes, Use in game and Play here, the look, material tweaks, what the
// stage shows, and screenshots.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class StudioWindow : EditorWindow
    {
        Vector2 scroll, listScroll;
        string search = "";
        TargetKind listKind = TargetKind.Feature;
        List<StudioTarget> list;
        bool showLook = true, showMaterial, showStage = true, showTyped;
        string typedName = "", typedPiece = "";
        Vector2Int typedFootprint = Vector2Int.one;
        float typedHeight;

        [MenuItem("OpenKingdoms/Studio/Studio Panel", priority = 32)]
        public static void Open() => GetWindow<StudioWindow>("Studio").minSize = new Vector2(320, 400);

        void OnEnable()
        {
            StudioSession.Changed += Repaint;
            StudioBackend.Changed += Forget;
        }

        void OnDisable()
        {
            StudioSession.Changed -= Repaint;
            StudioBackend.Changed -= Forget;
        }

        void Forget() { list = null; Repaint(); }

        void OnGUI()
        {
            StudioBackend.Toolbar();
            if (!StudioMode.IsOn)
            {
                EditorGUILayout.HelpBox("Studio Mode puts a model you made beside the original, in the game's own light, checks it and puts it in the game.", MessageType.Info);
                if (GUILayout.Button("Open Studio Mode", GUILayout.Height(30))) StudioMode.Open(true);
                return;
            }
            if (!StudioSession.Active)
            {
                bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
                EditorGUILayout.HelpBox(playing ? "The studio waits while the game plays. Stop Play to come back to it." : StudioSession.Status, MessageType.Info);
                if (!playing && GUILayout.Button("Start the studio again")) StudioMode.Open(false);
                if (!playing && GUILayout.Button("Leave Studio Mode")) StudioMode.Close(true);
                return;
            }
            if (!string.IsNullOrEmpty(StudioSession.Status)) EditorGUILayout.HelpBox(StudioSession.Status, MessageType.None);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            ModelSection();
            TargetSection();
            CheckSection();
            showLook = EditorGUILayout.Foldout(showLook, "Look", true, EditorStyles.foldoutHeader);
            if (showLook) LookSection();
            showMaterial = EditorGUILayout.Foldout(showMaterial, "Material", true, EditorStyles.foldoutHeader);
            if (showMaterial) MaterialSection();
            showStage = EditorGUILayout.Foldout(showStage, "Show", true, EditorStyles.foldoutHeader);
            if (showStage) StageSection();
            GUILayout.Space(8);
            if (GUILayout.Button("Leave Studio Mode")) StudioMode.Close(true);
            EditorGUILayout.EndScrollView();
        }

        static void Header(string text)
        {
            GUILayout.Space(6);
            GUILayout.Label(text, EditorStyles.boldLabel);
        }

        void ModelSection()
        {
            Header("Model");
            var m = StudioSession.Model;
            if (m == null)
            {
                GUILayout.Label("None yet. Drop one on the Studio Drop window or the Studio View.", EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Load the sample")) StudioSession.LoadModel(StudioSession.SamplePath);
                return;
            }
            GUILayout.Label(m.Name, EditorStyles.largeLabel);
            GUILayout.Label(m.SourcePath, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reload")) StudioSession.LoadModel(m.SourcePath);
                if (GUILayout.Button("Show the file")) EditorUtility.RevealInFinder(m.SourcePath);
            }
        }

        // ---- Replaces ----

        void TargetSection()
        {
            Header("Replaces");
            var t = StudioSession.Target;
            GUILayout.Label(t.Kind == TargetKind.None ? "Pick what this model replaces." :
                $"{KindName(t.Kind)}: {t.Label}", EditorStyles.wordWrappedLabel);
            var kinds = new[] { TargetKind.Feature, TargetKind.Unit, TargetKind.UnitCard };
            int k = GUILayout.Toolbar(Array.IndexOf(kinds, listKind), new[] { "Feature", "Unit", "Unit card" });
            if (kinds[k] != listKind) { listKind = kinds[k]; list = null; }
            if (listKind == TargetKind.UnitCard)
                GUILayout.Label("The units that stand on a painted card, which are the lodestones, the divine lodestones, the Zhon fire and glyph, and Thesh's stand. Your model takes the card's place and the unit keeps the rest.", EditorStyles.wordWrappedMiniLabel);
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            var b = StudioBackend.Get();
            list ??= listKind == TargetKind.Feature ? StudioTargets.Features(b, StudioTargets.Catalog()) : StudioTargets.Units(b, listKind == TargetKind.UnitCard);
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Height(130));
            int shown = 0;
            foreach (var item in list)
            {
                string label = item.Label;
                if (search.Length > 0 && label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && item.ObjectName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 300) { GUILayout.Label("Type to narrow the list.", EditorStyles.miniLabel); break; }
                bool on = t.Kind == item.Kind && t.Name == item.Name;
                if (GUILayout.Button(label, on ? EditorStyles.boldLabel : EditorStyles.label)) StudioSession.SetTarget(item);
            }
            if (list.Count == 0)
                GUILayout.Label(listKind == TargetKind.Feature ? "No features. Type one in below." : "No units. Type one in below.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
            if (b != null && b.Name == "Mock")
                GUILayout.Label("The stand-in world only has made-up things. To aim at something from the real game, type its name in below.", EditorStyles.wordWrappedMiniLabel);
            TypedSection();
            if (t.Kind == TargetKind.None) return;
            string size = t.Kind == TargetKind.Feature ? $"Footprint {t.Footprint.x} by {t.Footprint.y} cells" +
                (t.DrawnHeight > 0 ? $", drawn {t.DrawnHeight:0.#} cells tall" : t.Height > 0 ? $", about {t.Height:0.#} cells tall" : "") : $"Model {t.ObjectName}";
            GUILayout.Label(size, EditorStyles.miniLabel);
            if (StudioSession.OriginalMissing != null) EditorGUILayout.HelpBox(StudioSession.OriginalMissing, MessageType.Info);
            if (t.Kind == TargetKind.UnitCard)
            {
                var pieces = StudioSession.OriginalPieces;
                if (pieces.Length == 0)
                {
                    string typed = EditorGUILayout.DelayedTextField("Card piece", t.ReplacesPiece);
                    if (typed != t.ReplacesPiece) StudioSession.SetCardPiece(typed);
                }
                else
                {
                    int at = Array.FindIndex(pieces, p => string.Equals(p, t.ReplacesPiece, StringComparison.OrdinalIgnoreCase));
                    int pick = EditorGUILayout.Popup("Card piece", at, pieces);
                    if (pick != at) StudioSession.SetCardPiece(pick >= 0 ? pieces[pick] : "");
                }
                GUILayout.Label("The piece that carries the painted card. The studio picks it for you, usually the one named after the unit.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        // A feature or unit by name, with its size, for things the list
        // does not have.
        void TypedSection()
        {
            if (!showTyped && StudioSession.SuggestedName.Length > 0 && typedName.Length == 0) typedName = StudioSession.SuggestedName;
            showTyped = EditorGUILayout.Foldout(showTyped, "Type it in", true);
            if (!showTyped) return;
            typedName = EditorGUILayout.TextField(listKind == TargetKind.Feature ? "Feature name" : "Model name", typedName);
            if (listKind == TargetKind.Feature)
            {
                typedFootprint = EditorGUILayout.Vector2IntField("Footprint (cells)", typedFootprint);
                typedHeight = EditorGUILayout.FloatField("Height (cells)", typedHeight);
                GUILayout.Label("The name as the game has it, such as AraTree01. The footprint is the cells it covers on the map, and the height how tall it stands in the game.", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                if (listKind == TargetKind.UnitCard) typedPiece = EditorGUILayout.TextField("Card piece", typedPiece);
                GUILayout.Label("The unit's model name, such as ARAKING.", EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUI.DisabledScope(typedName.Trim().Length == 0))
                if (GUILayout.Button("Use this"))
                    StudioSession.SetTarget(StudioTargets.Typed(listKind, typedName, typedFootprint, typedHeight, typedPiece));
        }

        static string KindName(TargetKind k) => k == TargetKind.Feature ? "Feature" : k == TargetKind.Unit ? "Unit" : "Unit card";

        // ---- Check, fix, use ----

        void CheckSection()
        {
            Header("Check");
            var m = StudioSession.Model;
            if (m == null) { GUILayout.Label("Drop a model to check it.", EditorStyles.miniLabel); return; }
            foreach (var issue in StudioSession.Issues)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Icon(issue.Level), GUILayout.Width(20), GUILayout.Height(18));
                    GUILayout.Label(issue.Text, EditorStyles.wordWrappedLabel);
                }
                if (issue.Fix != ModelCheck.FixKind.None)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(24);
                        if (GUILayout.Button(issue.FixLabel)) StudioSession.ApplyFix(issue.Fix);
                    }
            }
            var fix = StudioSession.Fix;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Turn left")) StudioSession.SetFix(fix.Turned(-1));
                if (GUILayout.Button("Turn right")) StudioSession.SetFix(fix.Turned(1));
                if (GUILayout.Button("Half turn")) StudioSession.SetFix(fix.Turned(2));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                float s = EditorGUILayout.DelayedFloatField("Scale", fix.Scale);
                if (s > 0 && !Mathf.Approximately(s, fix.Scale)) StudioSession.SetFix(fix.Scaled(s / fix.Scale));
                if (GUILayout.Button("Centre", GUILayout.Width(60))) StudioSession.ApplyFix(ModelCheck.FixKind.Recentre);
                if (GUILayout.Button("Undo fixes", GUILayout.Width(80))) StudioSession.SetFix(StudioFix.None);
            }

            GUILayout.Space(6);
            string path = OverrideWriter.PathFor(StudioSession.Target);
            bool blocked = ModelCheck.Blocks(StudioSession.Issues);
            using (new EditorGUI.DisabledScope(blocked || path == null))
            {
                if (GUILayout.Button("Use in game", GUILayout.Height(30))) StudioSession.UseInGame(true);
            }
            if (path != null)
                GUILayout.Label("Goes to " + path + (StudioSession.Target.Kind == TargetKind.UnitCard ? " with " + Path.GetFileName(OverrideWriter.SidecarFor(path)) + " beside it. Share both." : ""), EditorStyles.wordWrappedMiniLabel);
            if (!StudioSession.Tweaks.IsDefault) StudioSession.KeepTweaks = EditorGUILayout.ToggleLeft("Keep the material changes", StudioSession.KeepTweaks);
            bool inGame = path != null && File.Exists(Path.Combine(StudioModel.ProjectDir, path));
            using (new EditorGUI.DisabledScope(!inGame))
            {
                if (GUILayout.Button("Play here", GUILayout.Height(26))) StudioMode.PlayHere();
            }
            GUILayout.Label(inGame ? "Play here shows what is in the game. After you export again, press Use in game again first."
                : "Press Use in game first, then Play here starts a battle with it.", EditorStyles.wordWrappedMiniLabel);
            if (StudioSession.Target.Kind == TargetKind.Unit)
                GUILayout.Label("Play here starts a battle as the unit's kingdom. Build it from your builder to see it.", EditorStyles.wordWrappedMiniLabel);
        }

        static GUIContent Icon(ModelCheck.Level l)
        {
            switch (l)
            {
                case ModelCheck.Level.Problem: return EditorGUIUtility.IconContent("console.erroricon.sml");
                case ModelCheck.Level.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml");
                case ModelCheck.Level.Note: return EditorGUIUtility.IconContent("console.infoicon.sml");
                default: return EditorGUIUtility.IconContent("TestPassed");
            }
        }

        // ---- Look ----

        static readonly WeatherChoice[] Weathers = { WeatherChoice.ByMap, WeatherChoice.Off, WeatherChoice.Rain, WeatherChoice.Snow, WeatherChoice.Fog };
        static readonly string[] WeatherNames = { "As the map has it", "Clear", "Rain", "Snow", "Fog" };
        static readonly string[] TimeNames = { "The game's own light", "Morning", "Noon", "Evening", "Night" };

        void LookSection()
        {
            var b = StudioBackend.Get();
            if (b == null) return;
            bool standIn = b.Name == "Mock";
            var ids = new List<string> { "", "?" };
            var names = new List<string> { "Neutral ground", standIn ? "Automatic (neutral on the stand-in world)" : "Automatic (the first map)" };
            foreach (var m in b.Maps) { ids.Add(m.Id); names.Add(m.Name + (standIn ? " (stand-in)" : "")); }
            int at = Mathf.Max(0, ids.IndexOf(StudioSession.MapChoice));
            int pick = EditorGUILayout.Popup("Ground", at, names.ToArray());
            if (pick != at) { StudioSession.MapChoice = ids[pick]; StudioSession.Relook(true); }

            var climates = new List<string> { "" };
            climates.AddRange(StudioSession.Climates);
            int c = Mathf.Max(0, climates.IndexOf(StudioSession.ClimateChoice));
            int cp = EditorGUILayout.Popup("Climate", c, climates.Select(x => x.Length == 0 ? "As the map has it" : Cap(x)).ToArray());
            if (cp != c) { StudioSession.ClimateChoice = climates[cp]; StudioSession.Relook(false); }
            if (!StudioSession.Stage.RealGround)
            {
                bool sea = EditorGUILayout.Toggle("Sea", StudioSession.Sea);
                if (sea != StudioSession.Sea) { StudioSession.Sea = sea; StudioSession.Relook(false); }
            }
            int wi = Mathf.Max(0, Array.IndexOf(Weathers, StudioSession.Weather));
            int wp = EditorGUILayout.Popup("Weather", wi, WeatherNames);
            if (wp != wi) { StudioSession.Weather = Weathers[wp]; StudioSession.Stage.SetWeather(Weathers[wp]); StudioSession.Notify(); }
            int ti = Mathf.Clamp((int)StudioSession.Time, 0, TimeNames.Length - 1);
            int tp = EditorGUILayout.Popup("Time of day", ti, TimeNames);
            if (tp != ti) { StudioSession.Time = (TimeOfDay)tp; StudioSession.Stage.SetTime((TimeOfDay)tp); StudioSession.Notify(); }
            bool sh = EditorGUILayout.Toggle("Shadows", StudioSession.Shadows);
            if (sh != StudioSession.Shadows) { StudioSession.Shadows = sh; StudioSession.Stage.SetShadows(sh); StudioSession.Notify(); }
            int team = EditorGUILayout.IntSlider("Team colour", StudioSession.TeamColour, 0, 7);
            if (team != StudioSession.TeamColour) StudioSession.SetTeam(team);
            GUILayout.Label("Materials with \"team\" in their name take the team colour here. The game does not tint drop-in models yet.", EditorStyles.wordWrappedMiniLabel);
        }

        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ---- Material ----

        void MaterialSection()
        {
            var t = StudioSession.Tweaks.Copy();
            EditorGUI.BeginChangeCheck();
            t.Tint = EditorGUILayout.ColorField("Tint", t.Tint);
            t.Brightness = EditorGUILayout.Slider("Brightness", t.Brightness, 0f, 3f);
            bool own = t.Roughness < 0;
            own = EditorGUILayout.Toggle("Keep own roughness", own);
            if (own) t.Roughness = -1f;
            else t.Roughness = EditorGUILayout.Slider("Roughness", t.Roughness < 0 ? 0.8f : t.Roughness, 0f, 1f);
            t.Emission = EditorGUILayout.Slider("Self light", t.Emission, 0f, 4f);
            if (EditorGUI.EndChangeCheck()) StudioSession.SetTweaks(t);
            GUILayout.Label("Self light at 0 keeps the model's own glow from Blender.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Reset the material")) StudioSession.SetTweaks(new MaterialTweaks());
        }

        // ---- What the stage shows ----

        void StageSection()
        {
            var s = StudioSession.Stage;
            EditorGUI.BeginChangeCheck();
            s.ShowMonarch = EditorGUILayout.ToggleLeft("Monarch for scale", s.ShowMonarch);
            s.ShowGrid = EditorGUILayout.ToggleLeft("Footprint grid", s.ShowGrid);
            s.ShowAnchor = EditorGUILayout.ToggleLeft("Anchor and front arrow", s.ShowAnchor);
            s.ShowOriginal = EditorGUILayout.ToggleLeft("Original beside it", s.ShowOriginal);
            s.ShowGhost = EditorGUILayout.ToggleLeft("Ghost of the original (classic view)", s.ShowGhost);
            if (EditorGUI.EndChangeCheck()) StudioSession.Notify();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Screenshot, classic")) StudioSession.Screenshot(true);
                if (GUILayout.Button("Screenshot, free")) StudioSession.Screenshot(false);
            }
            if (GUILayout.Button("Open the Captures folder"))
            {
                Directory.CreateDirectory(StudioSession.CapturesDir);
                EditorUtility.RevealInFinder(StudioSession.CapturesDir);
            }
            if (GUILayout.Button("Frame it in the Scene view") && SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(s.Spot + Vector3.up * s.ModelBounds.center.y, Quaternion.Euler(30f, 200f, 0), Mathf.Max(3f, s.ModelBounds.extents.magnitude * 2.5f));
            }
        }
    }
}
