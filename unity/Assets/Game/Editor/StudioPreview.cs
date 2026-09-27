// StudioPreview.cs - a small turntable for the studio windows: draws a
// model's pieces at a pose, or a drop-in model, under a sun on a ground
// disc, and turns it slowly unless the user drags it.
using System;
using System.Collections.Generic;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class StudioPreview : IDisposable
    {
        readonly PreviewRenderUtility util = new PreviewRenderUtility();
        readonly List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> draws = new List<(Mesh, int, Material, Matrix4x4)>();
        Mesh disc;
        Material discMat;
        public float Yaw = 200f, Pitch = 25f, Zoom = 1f;
        public bool Turn = true;
        Bounds bounds;
        double last;

        public StudioPreview()
        {
            util.camera.fieldOfView = 30f;
            util.camera.nearClipPlane = 0.05f;
            util.camera.farClipPlane = 500f;
            util.camera.clearFlags = CameraClearFlags.SolidColor;
            util.camera.backgroundColor = new Color(0.17f, 0.17f, 0.19f);
            util.lights[0].intensity = 1.1f;
            util.lights[0].transform.rotation = Quaternion.Euler(45f, 140f, 0);
            util.lights[1].intensity = 0.5f;
            util.ambientColor = new Color(0.45f, 0.47f, 0.52f);
            disc = new Mesh { hideFlags = HideFlags.DontSave };
            var v = new List<Vector3> { Vector3.zero };
            var t = new List<int>();
            for (int i = 0; i <= 48; i++)
            {
                float a = i * Mathf.PI * 2 / 48;
                v.Add(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)));
                if (i > 0) { t.Add(0); t.Add(i); t.Add(i + 1); }
            }
            disc.SetVertices(v);
            disc.SetTriangles(t, 0);
            disc.RecalculateNormals();
            var cols = new Color32[v.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(90, 92, 88, 255);
            disc.colors32 = cols;
            discMat = Looks.Model(null);
        }

        public void Clear()
        {
            draws.Clear();
            bounds = new Bounds(Vector3.zero, Vector3.one);
        }

        // A backend model at a pose in model space (null pose for rest).
        public void SetModel(PresentedModel model, PiecePose[] pose, int posed)
        {
            Clear();
            if (model == null) return;
            var d = model.Data;
            bool first = true;
            for (int p = 0; p < model.Pieces.Length; p++)
            {
                var mesh = model.Pieces[p];
                if (mesh == null) continue;
                Matrix4x4 m;
                if (pose != null && p < posed)
                {
                    if (pose[p].Hidden) continue;
                    m = pose[p].Matrix * model.Unscale;
                }
                else m = Rest(d, p);
                for (int s = 0; s < model.Materials[p].Length; s++) draws.Add((mesh, s, model.Materials[p][s], m));
                Grow(mesh.bounds, m, ref first);
            }
        }

        // A model at matrices already free of scale, with a piece to mark.
        public void SetPosed(PresentedModel model, Matrix4x4[] posed, bool[] hidden, int count, int highlight = -1)
        {
            Clear();
            if (model == null) return;
            bool first = true;
            for (int p = 0; p < model.Pieces.Length && p < count; p++)
            {
                var mesh = model.Pieces[p];
                if (mesh == null || (hidden != null && hidden[p])) continue;
                for (int s = 0; s < model.Materials[p].Length; s++)
                    draws.Add((mesh, s, p == highlight ? Highlight(model.Materials[p][s]) : model.Materials[p][s], posed[p]));
                Grow(mesh.bounds, posed[p], ref first);
            }
        }

        readonly Dictionary<Material, Material> highlights = new Dictionary<Material, Material>();

        Material Highlight(Material m)
        {
            if (highlights.TryGetValue(m, out var h)) return h;
            h = new Material(m) { hideFlags = HideFlags.DontSave, color = new Color(1.6f, 1.3f, 0.6f) };
            highlights[m] = h;
            return h;
        }

        static Matrix4x4 Rest(ModelData d, int p)
        {
            var m = Matrix4x4.Translate(d.Pieces[p].Offset * d.Scale);
            for (int q = d.Pieces[p].Parent; q >= 0; q = d.Pieces[q].Parent) m = Matrix4x4.Translate(d.Pieces[q].Offset * d.Scale) * m;
            return m;
        }

        // A drop-in model's template, in cells, standing at the origin.
        public void SetTemplate(GameObject template)
        {
            Clear();
            if (template == null) return;
            var o = OverrideModel.From(template, null, "");
            bool first = true;
            foreach (var part in o.Parts)
            {
                draws.Add((part.Mesh, part.Submesh, part.Material, part.NodeToRoot));
                Grow(part.Mesh.bounds, part.NodeToRoot, ref first);
            }
        }

        void Grow(Bounds b, Matrix4x4 m, ref bool first)
        {
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var w = m.MultiplyPoint3x4(c);
                if (first) { bounds = new Bounds(w, Vector3.zero); first = false; }
                else bounds.Encapsulate(w);
            }
        }

        // Draws into rect and handles dragging. Returns true while turning,
        // so the window knows to repaint.
        public bool Draw(Rect rect)
        {
            var e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag) { Yaw += e.delta.x * 0.6f; Pitch = Mathf.Clamp(Pitch + e.delta.y * 0.4f, -10f, 85f); Turn = false; e.Use(); }
                if (e.type == EventType.ScrollWheel) { Zoom = Mathf.Clamp(Zoom * (1f + e.delta.y * 0.05f), 0.3f, 4f); e.Use(); }
            }
            if (e.type != EventType.Repaint) return Turn;
            double now = EditorApplication.timeSinceStartup;
            if (Turn && last > 0) Yaw += (float)(now - last) * 25f;
            last = now;

            util.BeginPreview(rect, GUIStyle.none);
            float radius = Mathf.Max(0.5f, bounds.extents.magnitude);
            var target = bounds.center;
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            util.camera.transform.rotation = rot;
            util.camera.transform.position = target - rot * Vector3.forward * (radius * 3.2f * Zoom);
            float ground = bounds.min.y;
            util.DrawMesh(disc, Matrix4x4.TRS(new Vector3(target.x, ground - 0.01f, target.z), Quaternion.identity, Vector3.one * radius * 1.3f), discMat, 0);
            foreach (var d in draws)
            {
                // Mirrored matrices draw a mirrored mesh, as in the game.
                if (d.m.determinant < 0) util.DrawMesh(InstancedDraws.Mirrored(d.mesh), d.m * Matrix4x4.Scale(new Vector3(-1, 1, 1)), d.mat, d.sub);
                else util.DrawMesh(d.mesh, d.m, d.mat, d.sub);
            }
            util.camera.Render();
            util.EndAndDrawPreview(rect);
            return Turn;
        }

        public void Dispose()
        {
            util.Cleanup();
            foreach (var h in highlights.Values) if (h != null) UnityEngine.Object.DestroyImmediate(h);
            if (disc != null) UnityEngine.Object.DestroyImmediate(disc);
            if (discMat != null) UnityEngine.Object.DestroyImmediate(discMat);
        }
    }
}
