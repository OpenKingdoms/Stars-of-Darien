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
        bool showLook = true, showMaterial, showStage = true;

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
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            var b = StudioBackend.Get();
            list ??= listKind == TargetKind.Feature ? StudioTargets.Features(b, StudioTargets.Catalog()) : StudioTargets.Units(b, listKind == TargetKind.UnitCard);
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Height(130));
            int shown = 0;
            foreach (var item in list)
            {
                string label = item.Label;
                if (search.Length > 0 && label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 300) { GUILayout.Label("Type to narrow the list.", EditorStyles.miniLabel); break; }
                bool on = t.Kind == item.Kind && t.Name == item.Name;
                if (GUILayout.Button(label, on ? EditorStyles.boldLabel : EditorStyles.label)) StudioSession.SetTarget(Copy(item));
            }
            if (list.Count == 0)
                GUILayout.Label(listKind == TargetKind.Feature ? "No features. With your game files, run tools/sprite-replace/extract.py to list every sprite feature." : "No units.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
            if (t.Kind == TargetKind.None) return;
            string size = t.Kind == TargetKind.Feature ? $"Footprint {t.Footprint.x} by {t.Footprint.y} cells" + (t.Height > 0 ? $", {t.Height:0.#} cells tall" : "") : $"Model {t.ObjectName}";
            GUILayout.Label(size, EditorStyles.miniLabel);
            if (t.Kind == TargetKind.UnitCard)
            {
                var pieces = StudioSession.OriginalPieces;
                if (pieces.Length == 0) EditorGUILayout.HelpBox("The original model did not load, so its pieces are unknown.", MessageType.Warning);
                else
                {
                    int at = Array.FindIndex(pieces, p => string.Equals(p, t.ReplacesPiece, StringComparison.OrdinalIgnoreCase));
                    int pick = EditorGUILayout.Popup("Stands in for", at, pieces);
                    string tex = EditorGUILayout.DelayedTextField("Its texture", t.ReplacesTexture);
                    if (pick != at || tex != t.ReplacesTexture) StudioSession.SetCardPiece(pick >= 0 ? pieces[pick] : "", tex);
                }
            }
        }

        static StudioTarget Copy(StudioTarget t) => new StudioTarget
        {
            Kind = t.Kind, Name = t.Name, ObjectName = t.ObjectName, Description = t.Description, Side = t.Side,
            Footprint = t.Footprint, Height = t.Height, Width = t.Width, SpriteFile = t.SpriteFile, SpriteSize = t.SpriteSize,
            Hotspot = t.Hotspot, Maps = new List<string>(t.Maps), UnitDef = t.UnitDef, FeatureDef = t.FeatureDef,
        };

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
            if (path != null) GUILayout.Label("Goes to " + path + (StudioSession.Target.Kind == TargetKind.UnitCard ? " with a .json beside it" : ""), EditorStyles.wordWrappedMiniLabel);
            if (!StudioSession.Tweaks.IsDefault) StudioSession.KeepTweaks = EditorGUILayout.ToggleLeft("Keep the material changes", StudioSession.KeepTweaks);
            using (new EditorGUI.DisabledScope(path == null || !File.Exists(Path.Combine(StudioModel.ProjectDir, path ?? ""))))
            {
                if (GUILayout.Button("Play here", GUILayout.Height(26))) StudioMode.PlayHere();
            }
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

        void LookSection()
        {
            var b = StudioBackend.Get();
            if (b == null) return;
            var ids = new List<string> { "", "?" };
            var names = new List<string> { "Neutral ground", b.Name == "Mock" ? "Automatic (neutral on the mock)" : "Automatic (the first map)" };
            foreach (var m in b.Maps) { ids.Add(m.Id); names.Add(m.Name + (b.Name == "Mock" ? " (mock)" : "")); }
            int at = Mathf.Max(0, ids.IndexOf(StudioSession.MapChoice));
            int pick = EditorGUILayout.Popup("Ground", at, names.ToArray());
            if (pick != at) { StudioSession.MapChoice = ids[pick]; StudioSession.Relook(true); }

            var climates = new List<string> { "" };
            climates.AddRange(StudioSession.Climates);
            int c = Mathf.Max(0, climates.IndexOf(StudioSession.ClimateChoice));
            int cp = EditorGUILayout.Popup("Climate", c, climates.Select(x => x.Length == 0 ? "By the map" : Cap(x)).ToArray());
            if (cp != c) { StudioSession.ClimateChoice = climates[cp]; StudioSession.Relook(false); }
            if (!StudioSession.Stage.RealGround)
            {
                bool sea = EditorGUILayout.Toggle("Sea", StudioSession.Sea);
                if (sea != StudioSession.Sea) { StudioSession.Sea = sea; StudioSession.Relook(false); }
            }
            var w = (WeatherChoice)EditorGUILayout.EnumPopup("Weather", StudioSession.Weather);
            if (w != StudioSession.Weather) { StudioSession.Weather = w; StudioSession.Stage.SetWeather(w); }
            var time = (TimeOfDay)EditorGUILayout.EnumPopup("Time of day", StudioSession.Time);
            if (time != StudioSession.Time) { StudioSession.Time = time; StudioSession.Stage.SetTime(time); }
            bool sh = EditorGUILayout.Toggle("Shadows", StudioSession.Shadows);
            if (sh != StudioSession.Shadows) { StudioSession.Shadows = sh; StudioSession.Stage.SetShadows(sh); }
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
