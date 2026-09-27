// MapBrowser.cs - every map the backend offers, with its overview picture
// and details, and a Play button that starts a skirmish on it.
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace OpenKingdomsUnity.Studio
{
    public sealed class MapBrowser : EditorWindow
    {
        string search = "";
        string selected;
        Vector2 scroll;
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();

        [MenuItem("OpenKingdoms/Studio/Map Browser", priority = 21)]
        public static void Open() => GetWindow<MapBrowser>("Map Browser").minSize = new Vector2(640, 380);

        void OnEnable() => StudioBackend.Changed += Forget;
        void OnDisable() { StudioBackend.Changed -= Forget; Forget(); }

        void Forget()
        {
            foreach (var t in previews.Values) if (t != null) DestroyImmediate(t);
            previews.Clear();
            Repaint();
        }

        void OnGUI()
        {
            StudioBackend.Toolbar();
            var b = StudioBackend.Get();
            if (b == null) { EditorGUILayout.HelpBox("The studio pauses while the game plays.", MessageType.None); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
                {
                    search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    foreach (var m in b.Maps)
                    {
                        if (search.Length > 0 && m.Name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (GUILayout.Button(m.Name, m.Id == selected ? EditorStyles.boldLabel : EditorStyles.label)) selected = m.Id;
                    }
                    EditorGUILayout.EndScrollView();
                    GUILayout.Label($"{b.Maps.Count} maps", EditorStyles.miniLabel);
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    MapInfo map = null;
                    foreach (var m in b.Maps) if (m.Id == selected) map = m;
                    if (map == null) { GUILayout.Label("Pick a map on the left."); return; }
                    GUILayout.Label(map.Name, EditorStyles.largeLabel);
                    if (!string.IsNullOrEmpty(map.Description)) GUILayout.Label(map.Description, EditorStyles.wordWrappedLabel);
                    GUILayout.Label($"{map.Size.x:0} by {map.Size.y:0} cells, up to {map.MaxPlayers} players, {map.Climate}", EditorStyles.miniLabel);
                    var rect = GUILayoutUtility.GetRect(100, 10000, 100, 10000);
                    var tex = Preview(b, map.Id);
                    if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                    else EditorGUI.HelpBox(rect, "No overview picture for this map.", MessageType.Info);
                    if (GUILayout.Button("Play a skirmish on " + map.Name, GUILayout.Height(30))) Play(map.Id);
                }
            }
        }

        Texture2D Preview(IGameBackend b, string id)
        {
            if (previews.TryGetValue(id, out var t)) return t;
            t = UiKit.ToTexture(b.MapPreview(id, 512), true);
            previews[id] = t;
            return t;
        }

        static void Play(string mapId)
        {
            PlayerPrefs.SetString(GameRoot.AutoStartKey, mapId);
            PlayerPrefs.Save();
            RemasterMenu.Play();
        }
    }
}
