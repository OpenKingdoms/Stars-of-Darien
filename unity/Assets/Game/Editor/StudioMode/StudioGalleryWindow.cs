// StudioGalleryWindow.cs - the gallery's view and toolbar. A click flies to
// a model, the arrow keys or N and P step through them, Home goes back to
// the overview, and F9 or Shift+F9 capture the view.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class StudioGalleryWindow : EditorWindow
    {
        RenderTexture rt;
        string search = "";
        Vector2 downAt;
        bool dragged;
        Vector2Int viewSize = new Vector2Int(1280, 720);

        [MenuItem("OpenKingdoms/Studio/Gallery", priority = 33)]
        public static void Open()
        {
            if (!StudioMode.IsOn && !StudioMode.Open(true)) return;
            var w = GetWindow<StudioGalleryWindow>("Gallery", true, typeof(StudioViewWindow));
            w.minSize = new Vector2(420, 300);
            w.Focus();
        }

        void OnEnable()
        {
            StudioSession.Changed += Repaint;
            wantsMouseMove = false;
        }

        void OnDisable()
        {
            StudioSession.Changed -= Repaint;
            if (StudioGallery.Current != null) StudioGallery.Current.Changed -= Repaint;
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
        }

        // Closing the window lets the gallery and its models go.
        void OnDestroy() => StudioGallery.Close();

        StudioGallery Gallery()
        {
            if (!StudioSession.Active) return null;
            var g = StudioGallery.Show(StudioGallery.LastFolder);
            g.Changed -= Repaint;
            g.Changed += Repaint;
            return g;
        }

        void OnGUI()
        {
            if (!StudioMode.IsOn)
            {
                EditorGUILayout.HelpBox("The gallery shows every model in a folder on the studio's stage. It opens in Studio Mode.", MessageType.Info);
                if (GUILayout.Button("Open Studio Mode and the gallery", GUILayout.Height(26))) Open();
                return;
            }
            var g = Gallery();
            if (g == null)
            {
                EditorGUILayout.HelpBox(EditorApplication.isPlayingOrWillChangePlaymode ? "The gallery waits while the game plays." : StudioSession.Status, MessageType.Info);
                return;
            }
            Toolbar(g);
            var area = GUILayoutUtility.GetRect(100, 10000, 100, 10000);
            float scale = EditorGUIUtility.pixelsPerPoint;
            viewSize = new Vector2Int(Mathf.Max(16, (int)(area.width * scale)), Mathf.Max(16, (int)(area.height * scale)));
            if (StudioCapture.HandleKeys(this, g.Render, viewSize)) return;
            Keys(g);
            Mouse(g, area, scale);
            if (Event.current.type == EventType.Repaint)
            {
                if (rt == null || rt.width != viewSize.x || rt.height != viewSize.y)
                {
                    if (rt != null) { rt.Release(); DestroyImmediate(rt); }
                    rt = new RenderTexture(viewSize.x, viewSize.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 2 };
                }
                g.Render(rt);
                GUI.DrawTexture(area, rt, ScaleMode.StretchToFill, false);
            }
            Overlay(g, area);
        }

        void Toolbar(StudioGallery g)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (EditorGUILayout.DropdownButton(new GUIContent("Folder: " + g.FolderLabel), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.MaxWidth(320)))
                    FolderMenu(g);
                GUILayout.Space(6);
                string typed = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(80), GUILayout.MaxWidth(220));
                if (typed != search) { search = typed; g.SetFilter(search); }
                GUILayout.FlexibleSpace();
                bool compare = GUILayout.Toggle(g.Compare, new GUIContent("Compare originals", "Stand the original from the game beside each model, when the game is installed."), EditorStyles.toolbarButton);
                if (compare != g.Compare) g.SetCompare(compare);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool classic = g.View.Classic;
                if (GUILayout.Toggle(classic, "Classic view", EditorStyles.toolbarButton) && !classic) g.SetClassic(true);
                if (GUILayout.Toggle(!classic, "Free view", EditorStyles.toolbarButton) && classic) g.SetClassic(false);
                if (!classic) g.Turning = GUILayout.Toggle(g.Turning, "Turn", EditorStyles.toolbarButton);
                GUILayout.Space(8);
                if (GUILayout.Button("Overview", EditorStyles.toolbarButton)) g.Overview();
                if (GUILayout.Button("< Previous", EditorStyles.toolbarButton)) g.Step(-1);
                if (GUILayout.Button("Next >", EditorStyles.toolbarButton)) g.Step(1);
                int at = g.Selected != null ? IndexOf(g, g.Selected) + 1 : 0;
                GUILayout.Label(g.Selected != null ? $"{g.Selected.Name}   {at} of {g.Shown.Count}" : $"{g.Shown.Count} shown", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Screenshot", "Saves the view into the capture folder. F9 does the same, Shift+F9 records a clip."), EditorStyles.toolbarButton))
                    StudioCapture.Shot(this, g.Render, viewSize);
            }
        }

        static int IndexOf(StudioGallery g, StudioGallery.Entry e)
        {
            for (int i = 0; i < g.Shown.Count; i++) if (g.Shown[i] == e) return i;
            return -1;
        }

        void FolderMenu(StudioGallery g)
        {
            var menu = new GenericMenu();
            foreach (var (label, path) in StudioGallery.QuickFolders)
            {
                string p = path;
                bool on = string.Equals(Path.GetFullPath(StudioModel.Absolute(p)), g.Folder, System.StringComparison.OrdinalIgnoreCase);
                menu.AddItem(new GUIContent(label), on, () => UseFolder(p));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Pick a folder..."), false, () =>
            {
                string picked = EditorUtility.OpenFolderPanel("A folder of .glb models", g.Folder, "");
                if (!string.IsNullOrEmpty(picked)) UseFolder(picked);
            });
            menu.AddItem(new GUIContent("Show the folder"), false, () => { if (Directory.Exists(g.Folder)) EditorUtility.RevealInFinder(g.Folder); });
            menu.ShowAsContext();
        }

        void UseFolder(string folder)
        {
            StudioGallery.LastFolder = folder;
            search = "";
            Gallery();
            Repaint();
        }

        void Keys(StudioGallery g)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return;
            switch (e.keyCode)
            {
                case KeyCode.RightArrow: case KeyCode.N: g.Step(1); break;
                case KeyCode.LeftArrow: case KeyCode.P: g.Step(-1); break;
                case KeyCode.Home: case KeyCode.Escape: g.Overview(); break;
                default: return;
            }
            e.Use();
        }

        // The wheel zooms, a drag turns the free view or pans the classic one,
        // and a click flies to the model under the pointer.
        void Mouse(StudioGallery g, Rect area, float scale)
        {
            var e = Event.current;
            if (e.type == EventType.MouseUp && !dragged && e.button == 0 && area.Contains(e.mousePosition))
            {
                GUIUtility.keyboardControl = 0;
                var pixel = (e.mousePosition - area.position) * scale;
                var hit = g.Pick(g.RayAt(pixel, viewSize));
                if (hit != null) g.Select(hit);
                e.Use();
                return;
            }
            if (!area.Contains(e.mousePosition)) return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    downAt = e.mousePosition;
                    dragged = false;
                    break;
                case EventType.ScrollWheel:
                    g.Zoom(e.delta.y);
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!dragged && (e.mousePosition - downAt).sqrMagnitude < 16f) break;
                    dragged = true;
                    g.Drag(e.delta);
                    e.Use();
                    break;
            }
        }

        void Overlay(StudioGallery g, Rect area)
        {
            var p = g.Progress;
            if (p.HasValue)
            {
                var bar = new Rect(area.x + 8, area.y + 8, Mathf.Min(420, area.width - 16), 20);
                float f = p.Value.Total > 0 ? p.Value.Done / (float)p.Value.Total : 0f;
                EditorGUI.ProgressBar(bar, f, $"{p.Value.What}: {p.Value.Done} of {p.Value.Total}");
            }
            if (!string.IsNullOrEmpty(g.Status))
                GUI.Label(new Rect(area.x + 8, area.yMax - 22, area.width - 16, 20), g.Status, Styles.Status);
            if (g.Entries.Count > 0 && g.Shown.Count == 0)
                GUI.Label(new Rect(area.x, area.y + 40, area.width, 24), "Nothing matches \"" + search + "\".", Styles.Hint);
        }

        static class Styles
        {
            public static readonly GUIStyle Status = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.LowerLeft };
            public static readonly GUIStyle Hint = new GUIStyle(EditorStyles.whiteLargeLabel) { alignment = TextAnchor.UpperCenter };
        }
    }
}
