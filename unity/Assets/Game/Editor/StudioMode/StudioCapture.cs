// StudioCapture.cs - F9 and Shift+F9 in the Studio View and the Gallery, as
// in the game: the view as drawn at its size, or five seconds of it with a
// contact sheet, into the capture folder, and a note on where it went.
using System;
using System.Collections.Generic;
using OpenKingdomsUnity.Game.Capture;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class StudioCapture
    {
        // For tests: where captures go instead of the capture folder.
        public static string DirOverride;
        public static string Dir => string.IsNullOrEmpty(DirOverride) ? CaptureFiles.Dir : DirOverride;
        public static string LastPath { get; private set; }
        public static bool Recording => clip != null;

        sealed class Reading
        {
            public AsyncGPUReadbackRequest Request;
            public RenderTexture Target;
            public int W, H;
            public Action<byte[], int, int> Got;
        }

        sealed class ClipState
        {
            public Func<RenderTexture, bool> Render;
            public Vector2Int Size;
            public ClipWriter Writer;
            public EditorWindow Window;
            public double Start, Next;
            public int Taken;
        }

        static readonly List<Reading> reading = new List<Reading>();
        static ClipState clip;

        static StudioCapture()
        {
            EditorApplication.update += () => TickAt(EditorApplication.timeSinceStartup);
        }

        // F9 or Shift+F9 in a window's OnGUI. render draws the view into the
        // texture given and says whether it could.
        public static bool HandleKeys(EditorWindow window, Func<RenderTexture, bool> render, Vector2Int size)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown || e.keyCode != OwnerCapture.Key) return false;
            if (e.shift) StartClip(window, render, size);
            else Shot(window, render, size);
            e.Use();
            return true;
        }

        // Returns the path the picture will be written to, or null.
        public static string Shot(EditorWindow window, Func<RenderTexture, bool> render, Vector2Int size)
        {
            string dir = Dir, path = CaptureFiles.NewShot(dir, DateTime.Now);
            if (!Grab(render, size, (px, w, h) => CaptureWriter.Shot(px, w, h, false, path, dir, (p, e) => Done(window, p, e, "Saved "))))
            {
                Tell(window, "Nothing to capture yet.");
                return null;
            }
            Tell(window, "Capturing...");
            return path;
        }

        public static bool StartClip(EditorWindow window, Func<RenderTexture, bool> render, Vector2Int size)
        {
            if (clip != null) { Tell(window, "Already recording a clip."); return false; }
            string dir = Dir;
            var (folder, sheet) = CaptureFiles.NewClip(dir, DateTime.Now);
            double now = EditorApplication.timeSinceStartup;
            clip = new ClipState
            {
                Render = render, Size = size, Window = window, Start = now, Next = now,
                Writer = new ClipWriter(folder, sheet, OwnerCapture.ClipFrames, dir, (p, e) => Done(window, p, e, "Clip saved, " + OwnerCapture.ClipFrames + " frames and a contact sheet: ")),
            };
            Tell(window, $"Recording {OwnerCapture.ClipSeconds:0} seconds...");
            return true;
        }

        // One editor frame: a clip's next frame when it is due, and every
        // read-back that has arrived.
        public static void TickAt(double now)
        {
            if (clip != null && now >= clip.Next)
            {
                var c = clip;
                int index = c.Taken++;
                float at = (float)(now - c.Start);
                c.Next = Math.Max(c.Next + 1.0 / OwnerCapture.ClipFps, now + 0.5 / OwnerCapture.ClipFps);
                if (c.Taken >= c.Writer.Frames) clip = null;
                if (!Grab(c.Render, c.Size, (px, w, h) => c.Writer.Add(index, px, w, h, false, at)))
                {
                    // The view went away, so the clip ends with what it has.
                    clip = null;
                    c.Writer.Finish();
                }
            }
            for (int i = reading.Count - 1; i >= 0; i--)
            {
                var r = reading[i];
                if (!r.Request.done) { r.Request.Update(); continue; }
                reading.RemoveAt(i);
                if (r.Target != null) { r.Target.Release(); UnityEngine.Object.DestroyImmediate(r.Target); }
                if (r.Request.hasError) { Debug.LogWarning("Capture: the picture could not be read back."); continue; }
                r.Got(r.Request.GetData<byte>().ToArray(), r.W, r.H);
            }
            CaptureWriter.Pump();
        }

        // For tests: every read-back and every file finished.
        public static bool Flush(int milliseconds = 20000)
        {
            foreach (var r in reading) r.Request.WaitForCompletion();
            TickAt(EditorApplication.timeSinceStartup);
            return CaptureWriter.WaitIdle(milliseconds);
        }

        // Draws the view with four samples, resolves it, and asks for it back.
        static bool Grab(Func<RenderTexture, bool> render, Vector2Int size, Action<byte[], int, int> got)
        {
            int w = Mathf.Max(16, size.x), h = Mathf.Max(16, size.y);
            var msaa = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
            var flat = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave };
            bool ok;
            try { ok = render != null && render(msaa); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            if (ok) Graphics.Blit(msaa, flat);
            msaa.Release();
            UnityEngine.Object.DestroyImmediate(msaa);
            if (!ok) { flat.Release(); UnityEngine.Object.DestroyImmediate(flat); return false; }
            reading.Add(new Reading { Request = AsyncGPUReadback.Request(flat, 0, TextureFormat.RGBA32), Target = flat, W = w, H = h, Got = got });
            return true;
        }

        static void Done(EditorWindow window, string path, Exception error, string what)
        {
            if (error != null) { Tell(window, "The capture was not saved: " + error.Message); Debug.LogWarning("Capture: " + error); return; }
            LastPath = path;
            Tell(window, what + path);
        }

        static void Tell(EditorWindow window, string text)
        {
            if (window != null && !Application.isBatchMode) window.ShowNotification(new GUIContent(text), OwnerCapture.ToastSeconds);
        }
    }
}
