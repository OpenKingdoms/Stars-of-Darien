// AnimationEditor.cs - the Studio's animation editor. Pick a unit and one
// of its script animations, play it on the model with scrub, speed and
// loop controls, and read the piece tree with each piece's move and turn
// over time. A piece's pose can be nudged, for every animation or for the
// one shown, and the nudges saved beside the model as
// Overrides/Units/<object>.anim.json, which the battle applies.
using System.Collections.Generic;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class AnimationEditor : EditorWindow
    {
        const int Samples = 64;

        string search = "";
        int selectedDef = -1, model = -1, animation, piece = -1;
        float time, speed = 1f, length = 2f;
        bool playing = true, loop = true, forAll = true;
        double lastTime;
        Vector2 listScroll, treeScroll;
        StudioPreview preview;
        AnimOverride nudges;
        bool dirty;
        string note = "";
        readonly PiecePose[] pose = new PiecePose[128];
        readonly Matrix4x4[] posed = new Matrix4x4[128];
        readonly bool[] hidden = new bool[128];
        // The selected piece's local move and turn over the timeline.
        Vector3[] moveCurve, turnCurve;
        string curveKey;

        [MenuItem("OpenKingdoms/Studio/Animation Editor", priority = 23)]
        public static void Open() => GetWindow<AnimationEditor>("Animation Editor").minSize = new Vector2(980, 560);

        void OnEnable()
        {
            preview = new StudioPreview { Turn = false, Yaw = 210f, Pitch = 20f };
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
            selectedDef = -1;
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
                UnitList(b);
                if (selectedDef < 0 || selectedDef >= b.UnitDefs.Count) { GUILayout.Label("Pick a unit on the left."); return; }
                var def = b.UnitDefs[selectedDef];
                var pm = StudioBackend.Models?.Get(model);
                if (pm == null) { EditorGUILayout.HelpBox($"The model {def.ObjectName} did not load.", MessageType.Info); return; }
                using (new EditorGUILayout.VerticalScope())
                {
                    Transport(def);
                    int n = Pose(b, def, pm, time);
                    var rect = GUILayoutUtility.GetRect(200, 10000, 220, 10000);
                    preview.SetPosed(pm, posed, hidden, n, piece);
                    preview.Draw(rect);
                    Curves(b, def, pm, rect.width);
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(300)))
                {
                    PieceTree(pm);
                    NudgePanel(def, pm);
                }
            }
            Tick();
            if (playing) Repaint();
        }

        void UnitList(IGameBackend b)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                foreach (var d in b.UnitDefs)
                {
                    if (d.Animations.Length == 0) continue;
                    string label = !string.IsNullOrEmpty(d.Title) ? $"{d.Title} ({d.Name})" : d.Name;
                    if (search.Length > 0 && label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (GUILayout.Button(label, d.Id == selectedDef ? EditorStyles.boldLabel : EditorStyles.label)) Select(b, d.Id);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        void Select(IGameBackend b, int id)
        {
            if (dirty && !EditorUtility.DisplayDialog("Animation editor", "Drop the unsaved nudges?", "Drop them", "Keep editing")) return;
            selectedDef = id;
            var def = b.UnitDefs[id];
            model = b.LoadModel(def.ObjectName, 0);
            nudges = AnimOverride.Load(def.ObjectName) ?? new AnimOverride { Model = def.ObjectName };
            dirty = false;
            animation = 0;
            piece = -1;
            time = 0;
            curveKey = null;
            note = "";
        }

        void Transport(UnitDef def)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int a = EditorGUILayout.Popup(Mathf.Min(animation, def.Animations.Length - 1), def.Animations, GUILayout.Width(160));
                if (a != animation) { animation = a; time = 0; curveKey = null; }
                if (GUILayout.Button(playing ? "Pause" : "Play", EditorStyles.miniButtonLeft, GUILayout.Width(56))) playing = !playing;
                if (GUILayout.Button("|<", EditorStyles.miniButtonMid, GUILayout.Width(30))) time = 0;
                if (GUILayout.Button("<", EditorStyles.miniButtonMid, GUILayout.Width(26))) { playing = false; time = Mathf.Max(0, time - 1f / 30f); }
                if (GUILayout.Button(">", EditorStyles.miniButtonRight, GUILayout.Width(26))) { playing = false; time += 1f / 30f; }
                loop = GUILayout.Toggle(loop, "Loop", GUILayout.Width(50));
                GUILayout.Label("Speed", GUILayout.Width(40));
                speed = EditorGUILayout.Slider(speed, 0.1f, 3f, GUILayout.Width(150));
                GUILayout.Label("Length", GUILayout.Width(44));
                float l = EditorGUILayout.FloatField(length, GUILayout.Width(40));
                if (!Mathf.Approximately(l, length)) { length = Mathf.Clamp(l, 0.2f, 20f); curveKey = null; }
            }
            time = EditorGUILayout.Slider("Time", time, 0, length);
        }

        string Anim(UnitDef def) => def.Animations.Length > 0 ? def.Animations[Mathf.Min(animation, def.Animations.Length - 1)] : "";

        // The script's pose at a time, free of scale, with the nudges on.
        int Pose(IGameBackend b, UnitDef def, PresentedModel pm, float t, bool withNudges = true)
        {
            int n = Mathf.Min(b.PoseModel(model, Anim(def), t, pose), pm.Pieces.Length);
            if (n <= 0)
            {
                // No script posing here: the model at rest.
                n = pm.Data.Pieces.Length;
                for (int p = 0; p < n; p++)
                {
                    var m = Matrix4x4.Translate(pm.Data.Pieces[p].Offset * pm.Data.Scale);
                    int parent = pm.Data.Pieces[p].Parent;
                    posed[p] = parent >= 0 && parent < p ? posed[parent] * m : m;
                    hidden[p] = false;
                }
            }
            else
                for (int p = 0; p < n; p++) { posed[p] = pose[p].Matrix * pm.Unscale; hidden[p] = pose[p].Hidden; }
            if (withNudges) nudges?.Apply(pm.Data.Pieces, posed, n, Anim(def));
            return n;
        }

        // Local move and turn of a piece, relative to its parent.
        static void Local(PieceInfo[] pieces, Matrix4x4[] m, int p, out Vector3 move, out Vector3 turn)
        {
            int parent = pieces[p].Parent;
            var local = parent >= 0 ? m[parent].inverse * m[p] : m[p];
            move = local.GetColumn(3);
            var r = local.rotation.eulerAngles;
            turn = new Vector3(Mathf.DeltaAngle(0, r.x), Mathf.DeltaAngle(0, r.y), Mathf.DeltaAngle(0, r.z));
        }

        void PieceTree(PresentedModel pm)
        {
            GUILayout.Label("Pieces", EditorStyles.boldLabel);
            treeScroll = EditorGUILayout.BeginScrollView(treeScroll, GUILayout.Height(240));
            var pieces = pm.Data.Pieces;
            for (int p = 0; p < pieces.Length; p++)
            {
                int depth = 0;
                for (int q = pieces[p].Parent; q >= 0 && depth < 20; q = pieces[q].Parent) depth++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(depth * 14);
                    bool nudged = nudges != null && nudges.Pieces.ContainsKey(pieces[p].Name ?? "");
                    string label = (pieces[p].Name ?? "piece " + p) + (nudged ? "  *" : "");
                    if (GUILayout.Button(label, p == piece ? EditorStyles.boldLabel : EditorStyles.label)) { piece = p; curveKey = null; }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void NudgePanel(UnitDef def, PresentedModel pm)
        {
            GUILayout.Space(6);
            if (piece < 0 || piece >= pm.Data.Pieces.Length) { GUILayout.Label("Pick a piece to nudge it."); return; }
            string name = pm.Data.Pieces[piece].Name;
            GUILayout.Label("Nudge " + name, EditorStyles.boldLabel);
            forAll = GUILayout.Toolbar(forAll ? 0 : 1, new[] { "Every animation", Anim(def) }) == 0;
            string key = forAll ? AnimOverride.All : Anim(def);
            var n = nudges.Get(name, key);
            EditorGUI.BeginChangeCheck();
            n.Move = EditorGUILayout.Vector3Field("Move (units)", n.Move);
            n.Turn = EditorGUILayout.Vector3Field("Turn (degrees)", n.Turn);
            if (EditorGUI.EndChangeCheck()) { nudges.Set(name, key, n); dirty = true; curveKey = null; }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear piece")) { nudges.Set(name, key, default); dirty = true; curveKey = null; }
                if (GUILayout.Button("Revert all")) { AnimOverride.Forget(def.ObjectName); nudges = AnimOverride.Load(def.ObjectName) ?? new AnimOverride(); dirty = false; curveKey = null; }
            }
            GUI.enabled = dirty;
            if (GUILayout.Button(dirty ? "Save nudges" : "Saved", GUILayout.Height(28)))
            {
                nudges.Save(def.ObjectName);
                dirty = false;
                note = "Saved to " + AnimOverride.PathFor(OverrideLoader.ProjectDir, def.ObjectName).Replace('\\', '/');
            }
            GUI.enabled = true;
            if (note.Length > 0) EditorGUILayout.HelpBox(note, MessageType.None);
        }

        // Six lines over the timeline: the piece's local move x, y, z and
        // turn x, y, z, with the playhead.
        void Curves(IGameBackend b, UnitDef def, PresentedModel pm, float width)
        {
            var rect = GUILayoutUtility.GetRect(width, 150);
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.15f));
            if (piece < 0 || piece >= pm.Data.Pieces.Length) { GUI.Label(rect, "  Pick a piece to see its move and turn over time.", EditorStyles.miniLabel); return; }
            string key = $"{selectedDef}/{animation}/{piece}/{length}";
            if (curveKey != key)
            {
                curveKey = key;
                moveCurve = new Vector3[Samples];
                turnCurve = new Vector3[Samples];
                for (int i = 0; i < Samples; i++)
                {
                    Pose(b, def, pm, length * i / (Samples - 1));
                    Local(pm.Data.Pieces, posed, piece, out moveCurve[i], out turnCurve[i]);
                }
                Pose(b, def, pm, time);
            }
            var half = new Rect(rect.x, rect.y, rect.width, rect.height / 2);
            Plot(half, moveCurve, "move");
            Plot(new Rect(rect.x, rect.y + rect.height / 2, rect.width, rect.height / 2), turnCurve, "turn");
            float x = rect.x + rect.width * Mathf.Clamp01(time / length);
            EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), Color.white);
        }

        static readonly Color[] Axis = { new Color(1f, 0.4f, 0.4f), new Color(0.5f, 1f, 0.5f), new Color(0.5f, 0.7f, 1f) };

        static void Plot(Rect r, Vector3[] curve, string label)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in curve) for (int k = 0; k < 3; k++) { lo = Mathf.Min(lo, v[k]); hi = Mathf.Max(hi, v[k]); }
            if (hi - lo < 1e-4f) { hi += 0.5f; lo -= 0.5f; }
            Handles.BeginGUI();
            for (int k = 0; k < 3; k++)
            {
                Handles.color = Axis[k];
                var pts = new Vector3[curve.Length];
                for (int i = 0; i < curve.Length; i++)
                    pts[i] = new Vector3(r.x + r.width * i / (curve.Length - 1), r.yMax - 4 - (r.height - 8) * (curve[i][k] - lo) / (hi - lo));
                Handles.DrawAAPolyLine(2f, pts);
            }
            Handles.EndGUI();
            GUI.Label(new Rect(r.x + 4, r.y + 2, 300, 16), $"{label}  x y z   {lo:0.##} to {hi:0.##}", EditorStyles.miniLabel);
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing && lastTime > 0)
            {
                time += (float)(now - lastTime) * speed;
                if (time > length) { if (loop) time %= length; else { time = length; playing = false; } }
            }
            lastTime = now;
        }
    }
}
