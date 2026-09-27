// UnitBrowser.cs - every unit def the backend knows, with a turntable
// preview of its model in a team colour and, when the backend can pose
// it, its script animations playing.
using System.Collections.Generic;
using System.Linq;
using OpenKingdomsUnity.Game;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class UnitBrowser : EditorWindow
    {
        string search = "";
        int sideFilter;
        int selected = -1;
        int colour;
        int animation;
        bool playing = true;
        float time;
        double lastTime;
        Vector2 listScroll, infoScroll;
        StudioPreview preview;
        readonly PiecePose[] pose = new PiecePose[128];
        int model = -1;
        string modelProblem;

        [MenuItem("OpenKingdoms/Studio/Unit Browser", priority = 20)]
        public static void Open() => GetWindow<UnitBrowser>("Unit Browser").minSize = new Vector2(760, 420);

        void OnEnable()
        {
            preview = new StudioPreview();
            StudioBackend.Changed += Reset;
        }

        void OnDisable()
        {
            StudioBackend.Changed -= Reset;
            preview?.Dispose();
            preview = null;
        }

        void Reset()
        {
            selected = -1;
            model = -1;
            Repaint();
        }

        void OnGUI()
        {
            StudioBackend.Toolbar();
            var b = StudioBackend.Get();
            if (b == null) { EditorGUILayout.HelpBox("The studio pauses while the game plays.", MessageType.None); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(260)))
                {
                    search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                    var sides = new List<string> { "All kingdoms" };
                    sides.AddRange(b.Sides.Select(s => s.Name));
                    sideFilter = EditorGUILayout.Popup(sideFilter, sides.ToArray());
                    listScroll = EditorGUILayout.BeginScrollView(listScroll);
                    foreach (var d in b.UnitDefs)
                    {
                        if (sideFilter > 0 && d.Side != b.Sides[sideFilter - 1].Id) continue;
                        string label = string.IsNullOrEmpty(d.Description) ? d.Name : $"{d.Name}  ({d.Description})";
                        if (search.Length > 0 && label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var style = d.Id == selected ? EditorStyles.boldLabel : EditorStyles.label;
                        if (GUILayout.Button(label, style)) Select(b, d.Id);
                    }
                    EditorGUILayout.EndScrollView();
                    GUILayout.Label($"{b.UnitDefs.Count} units", EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    if (selected < 0 || selected >= b.UnitDefs.Count) { GUILayout.Label("Pick a unit on the left."); return; }
                    var def = b.UnitDefs[selected];
                    DrawControls(b, def);
                    var rect = GUILayoutUtility.GetRect(200, 10000, 200, 10000);
                    var pm = StudioBackend.Models?.Get(model, OverrideKind.Unit, new[] { def.Name, def.ObjectName });
                    if (pm == null)
                    {
                        EditorGUI.HelpBox(rect, modelProblem ?? "No model.", MessageType.Info);
                    }
                    else
                    {
                        int posed = 0;
                        if (def.Animations.Length > 0 && animation < def.Animations.Length)
                            posed = b.PoseModel(model, def.Animations[animation], time, pose);
                        if (pm.Override != null) preview.SetTemplate(OpenKingdomsUnity.Game.World.OverrideLoader.Template(pm.Override.Path));
                        else preview.SetModel(pm, posed > 0 ? pose : null, posed);
                        if (preview.Draw(rect) || playing) Repaint();
                    }
                    DrawInfo(def, pm);
                }
            }
            Tick();
        }

        void Select(IGameBackend b, int id)
        {
            selected = id;
            animation = 0;
            time = 0;
            var def = b.UnitDefs[id];
            model = b.LoadModel(def.ObjectName, colour);
            modelProblem = model < 0 ? $"The model {def.ObjectName} did not load." : null;
            preview.Turn = true;
        }

        void DrawControls(IGameBackend b, UnitDef def)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int c = EditorGUILayout.IntSlider("Team colour", colour, 0, 9);
                if (c != colour) { colour = c; model = b.LoadModel(def.ObjectName, colour); }
                preview.Turn = GUILayout.Toggle(preview.Turn, "Turn", EditorStyles.miniButton, GUILayout.Width(50));
            }
            if (def.Animations.Length == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                animation = EditorGUILayout.Popup("Animation", Mathf.Min(animation, def.Animations.Length - 1), def.Animations);
                playing = GUILayout.Toggle(playing, playing ? "Pause" : "Play", EditorStyles.miniButton, GUILayout.Width(50));
            }
            time = EditorGUILayout.Slider("Time", time % 10f, 0f, 10f);
        }

        void DrawInfo(UnitDef def, OpenKingdomsUnity.Game.World.PresentedModel pm)
        {
            infoScroll = EditorGUILayout.BeginScrollView(infoScroll, GUILayout.Height(110));
            EditorGUILayout.LabelField(def.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Kingdom", def.Side);
            EditorGUILayout.LabelField("Model", def.ObjectName + (pm?.Override != null ? "   (drop-in: " + pm.Override.Path + ")" : ""));
            EditorGUILayout.LabelField("Health, mana", $"{def.MaxHealth}, {def.ManaCost}");
            if (pm != null)
                EditorGUILayout.LabelField("Pieces", string.Join(", ", pm.Data.Pieces.Select(p => p.Name)), EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing && lastTime > 0) time += (float)(now - lastTime);
            lastTime = now;
        }
    }
}
