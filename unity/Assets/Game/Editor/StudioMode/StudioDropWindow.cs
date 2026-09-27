// StudioDropWindow.cs - where a model comes in: drag a .glb, .gltf, .fbx
// or .obj onto it from Explorer or the Project window, pick a file, or save
// one into Assets/Overrides/Drop, which it lists. The sample is one click.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class StudioDropWindow : EditorWindow
    {
        Vector2 scroll;

        [MenuItem("OpenKingdoms/Studio/Studio Drop", priority = 31)]
        public static void Open() => GetWindow<StudioDropWindow>("Studio Drop").minSize = new Vector2(260, 180);

        void OnEnable() => StudioSession.Changed += Repaint;
        void OnDisable() => StudioSession.Changed -= Repaint;

        void OnGUI()
        {
            if (!StudioMode.IsOn)
            {
                EditorGUILayout.HelpBox("Studio Mode is off.", MessageType.Info);
                if (GUILayout.Button("Open Studio Mode", GUILayout.Height(26))) StudioMode.Open(true);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("The studio waits while the game plays.", MessageType.Info);
                return;
            }
            var area = GUILayoutUtility.GetRect(100, 10000, 70, 90);
            GUI.Box(area, "Drop a .glb, .gltf, .fbx or .obj here", Styles.Drop);
            Drop(area);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Pick a file")) Pick();
                if (GUILayout.Button("Load the sample")) StudioSession.LoadModel(StudioSession.SamplePath);
                if (GUILayout.Button("Open the Drop folder")) RevealDrop();
            }
            GUILayout.Label("Models saved into Assets/Overrides/Drop show up at once, and a model reloads whenever its file changes.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);
            var files = StudioSession.DropFiles();
            if (files.Count == 0) return;
            GUILayout.Label("In the Drop folder", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            string current = StudioSession.Model?.SourcePath;
            foreach (var f in files)
            {
                bool on = current != null && string.Equals(Path.GetFullPath(current), f.FullName, System.StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button($"{f.Name}   {f.LastWriteTime:HH:mm}", on ? EditorStyles.boldLabel : EditorStyles.label)) StudioSession.LoadModel(f.FullName);
            }
            EditorGUILayout.EndScrollView();
        }

        // Accepts a model dragged onto rect, from the Project window or from
        // outside Unity. Shared by the Studio View.
        public static void Drop(Rect area)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            var path = DragAndDrop.paths?.FirstOrDefault(StudioModel.CanLoad);
            if (path == null) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                StudioSession.LoadModel(path);
            }
            e.Use();
        }

        static void Pick()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("Load a model", EditorPrefs.GetString("oku.studio.pickDir", ""),
                new[] { "Models", "glb,gltf,fbx,obj", "All files", "*" });
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString("oku.studio.pickDir", Path.GetDirectoryName(path));
            StudioSession.LoadModel(path);
        }

        static void RevealDrop()
        {
            string dir = Path.Combine(StudioModel.ProjectDir, StudioModel.DropFolder);
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        static class Styles
        {
            public static readonly GUIStyle Drop = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
        }
    }
}
