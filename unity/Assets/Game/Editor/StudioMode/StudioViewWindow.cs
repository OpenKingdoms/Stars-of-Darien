// StudioViewWindow.cs - the stage in the exact classic camera or a free view
// orbiting the turning model. Drop a model on it to load it.
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class StudioViewWindow : EditorWindow
    {
        RenderTexture rt;

        [MenuItem("OpenKingdoms/Studio/Studio View", priority = 30)]
        public static void Open() => GetWindow<StudioViewWindow>("Studio View").minSize = new Vector2(320, 240);

        void OnEnable()
        {
            StudioSession.Changed += Repaint;
            StudioSession.Repaint += Repaint;
            wantsMouseMove = false;
        }

        void OnDisable()
        {
            StudioSession.Changed -= Repaint;
            StudioSession.Repaint -= Repaint;
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
        }

        void OnGUI()
        {
            if (!StudioSession.Active)
            {
                EditorGUILayout.HelpBox(StudioMode.IsOn ? StudioSession.Status : "Studio Mode is off.", MessageType.Info);
                if (!StudioMode.IsOn && GUILayout.Button("Open Studio Mode", GUILayout.Height(26))) StudioMode.Open(true);
                return;
            }
            Toolbar();
            var area = GUILayoutUtility.GetRect(100, 10000, 100, 10000);
            StudioDropWindow.Drop(area);
            Controls(area);
            if (Event.current.type != EventType.Repaint) return;
            float scale = EditorGUIUtility.pixelsPerPoint;
            int w = Mathf.Max(16, (int)(area.width * scale)), h = Mathf.Max(16, (int)(area.height * scale));
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) { rt.Release(); DestroyImmediate(rt); }
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 2 };
            }
            var labels = StudioSession.Stage.Render(rt, Current);
            GUI.DrawTexture(area, rt, ScaleMode.StretchToFill, false);
            foreach (var (label, at) in labels)
            {
                var p = new Vector2(area.x + at.x / scale, area.y + at.y / scale);
                var size = Styles.Label.CalcSize(new GUIContent(label));
                GUI.Label(new Rect(p.x - size.x / 2, p.y - size.y, size.x, size.y), label, Styles.Label);
            }
            if (StudioSession.Model == null)
                GUI.Label(new Rect(area.x, area.y + 8, area.width, 24), "Drop a model here, or on the Studio Drop window.", Styles.Hint);
        }

        static StudioView Current => StudioSession.View.Classic ? StudioSession.View : StudioSession.Free;

        void Toolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool classic = StudioSession.View.Classic;
                if (GUILayout.Toggle(classic, "Classic view", EditorStyles.toolbarButton) && !classic) StudioSession.View.Classic = true;
                if (GUILayout.Toggle(!classic, "Free view", EditorStyles.toolbarButton) && classic) StudioSession.View.Classic = false;
                GUILayout.Space(8);
                if (!StudioSession.View.Classic)
                    StudioSession.Turning = GUILayout.Toggle(StudioSession.Turning, "Turn", EditorStyles.toolbarButton);
                GUILayout.Label("Zoom", EditorStyles.miniLabel);
                if (StudioSession.View.Classic)
                    StudioSession.View.Distance = GUILayout.HorizontalSlider(StudioSession.View.Distance, 8f, 110f, GUILayout.Width(120));
                else
                    StudioSession.Free.Distance = GUILayout.HorizontalSlider(StudioSession.Free.Distance, 1.5f, 80f, GUILayout.Width(120));
                if (GUILayout.Button("Reset", EditorStyles.toolbarButton))
                {
                    StudioSession.View = StudioView.ClassicDefault;
                    StudioSession.View.Classic = classic;
                    StudioSession.Free = StudioView.FreeDefault;
                    StudioSession.Stage.TurntableYaw = 0;
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Screenshot", EditorStyles.toolbarButton)) StudioSession.Screenshot(StudioSession.View.Classic);
            }
        }

        // Dragging turns the free view round the model and the wheel zooms
        // either view.
        void Controls(Rect area)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type == EventType.ScrollWheel)
            {
                float k = 1f + e.delta.y * 0.05f;
                if (StudioSession.View.Classic) StudioSession.View.Distance = Mathf.Clamp(StudioSession.View.Distance * k, 8f, 110f);
                else StudioSession.Free.Distance = Mathf.Clamp(StudioSession.Free.Distance * k, 1.5f, 80f);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && !StudioSession.View.Classic)
            {
                StudioSession.Free.Yaw += e.delta.x * 0.5f;
                StudioSession.Free.Pitch = Mathf.Clamp(StudioSession.Free.Pitch + e.delta.y * 0.4f, -5f, 88f);
                e.Use();
                Repaint();
            }
        }

        static class Styles
        {
            public static readonly GUIStyle Label = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.LowerCenter };
            public static readonly GUIStyle Hint = new GUIStyle(EditorStyles.whiteLargeLabel) { alignment = TextAnchor.UpperCenter };
        }
    }
}
