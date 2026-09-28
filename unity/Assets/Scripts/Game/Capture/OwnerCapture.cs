// OwnerCapture.cs - F9 saves a PNG of the screen and Shift+F9 records five
// seconds at 12 frames a second with a contact sheet, into the capture
// folder, read back asynchronously and written on a worker. F9 because
// Keys.tdf gives F12 to ClearChat and keeps F9 for screenshots.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.Capture
{
    public sealed class OwnerCapture : MonoBehaviour
    {
        public const KeyCode Key = KeyCode.F9;
        public const float ClipSeconds = 5f;
        public const int ClipFps = 12;
        public static int ClipFrames => Mathf.RoundToInt(ClipSeconds * ClipFps);
        public const float ToastSeconds = 4f;

        public static OwnerCapture Instance { get; private set; }
        // For tests: where captures go instead of CaptureFiles.Dir.
        public static string DirOverride;

        // The newest capture written, and every capture as it is written.
        public string LastPath { get; private set; }
        // Why the last capture failed, or null.
        public string LastError { get; private set; }
        public event Action<string> Saved;
        public bool Recording => clip != null;
        public string Toast => Time.unscaledTime < toastUntil ? toast : null;

        sealed class ClipState
        {
            public ClipWriter Writer;
            public float Start, Next;
            public int Taken;
        }

        ClipState clip;
        string toast;
        float toastUntil;
        // Set for a frame that is captured, so the note is not in the picture.
        bool hideToast;
        GUIStyle toastStyle;
        Texture2D toastBack;
        readonly List<Action> afterRendering = new List<Action>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddAtStartup() => Ensure();

        public static OwnerCapture Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Owner Capture");
            DontDestroyOnLoad(go);
            return Instance = go.AddComponent<OwnerCapture>();
        }

        static string Dir => string.IsNullOrEmpty(DirOverride) ? CaptureFiles.Dir : DirOverride;

        // Shift+F9 records, F9 alone takes a picture.
        public static bool ClipKeyDown() => Input.GetKeyDown(Key) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        public static bool ShotKeyDown() => Input.GetKeyDown(Key) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift);

        void Update()
        {
            hideToast = false;
            CaptureWriter.Pump();
            if (ClipKeyDown()) StartClip();
            else if (ShotKeyDown()) Shot();
            if (clip != null) TickClip();
        }

        // ---- A picture ----

        public void Shot()
        {
            hideToast = true;
            string dir = Dir;
            var at = DateTime.Now;
            AfterThisFrame(() => Grab((px, w, h, flip) =>
            {
                string path = CaptureFiles.NewShot(dir, at);
                CaptureWriter.Shot(px, w, h, flip, path, dir, (p, e) => Done(p, e, "Saved "));
            }, null));
        }

        // Runs once this frame is on the screen: at the end of the frame, or
        // in batch mode, where that never comes, once every camera has drawn.
        void AfterThisFrame(Action a)
        {
            if (Application.isBatchMode) afterRendering.Add(a);
            else StartCoroutine(EndOfFrame(a));
        }

        static IEnumerator EndOfFrame(Action a)
        {
            yield return new WaitForEndOfFrame();
            a();
        }

        void OnEnable() => RenderPipelineManager.endContextRendering += Rendered;
        void OnDisable() => RenderPipelineManager.endContextRendering -= Rendered;

        void Rendered(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (afterRendering.Count == 0) return;
            bool screen = false;
            foreach (var c in cameras) screen |= c != null && c.targetTexture == null && c.cameraType == CameraType.Game;
            if (!screen) return;
            var due = afterRendering.ToArray();
            afterRendering.Clear();
            foreach (var a in due) a();
        }

        // The whole screen as the player sees it, menus and all, read back
        // without waiting on the GPU. failed runs when it can't be.
        void Grab(Action<byte[], int, int, bool> got, Action failed)
        {
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) { Fail("the screen has no size", failed); return; }
            var rt = new RenderTexture(w, h, 0) { name = "owner capture" };
            try { ScreenCapture.CaptureScreenshotIntoRenderTexture(rt); }
            catch (Exception e) { Destroy(rt); Fail(e.Message, failed); return; }
            // The back buffer lands upside down where textures start at the top.
            bool flip = SystemInfo.graphicsUVStartsAtTop;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
                if (req.hasError) { Fail("the picture could not be read back", failed); return; }
                got(req.GetData<byte>().ToArray(), w, h, flip);
            });
        }

        void Fail(string why, Action failed)
        {
            LastError = why;
            Say("The capture failed: " + why + ".");
            Debug.LogWarning("Capture: " + why);
            failed?.Invoke();
        }

        // ---- A clip ----

        public void StartClip()
        {
            if (clip != null) { Say("Already recording a clip."); return; }
            string dir = Dir;
            var (folder, sheet) = CaptureFiles.NewClip(dir, DateTime.Now);
            float now = Time.unscaledTime;
            clip = new ClipState
            {
                Writer = new ClipWriter(folder, sheet, ClipFrames, dir, (p, e) => Done(p, e, "Clip saved, " + ClipFrames + " frames and a contact sheet: ")),
                Start = now,
                Next = now,
            };
            Say($"Recording {ClipSeconds:0} seconds...", ClipSeconds + 1f);
        }

        void TickClip()
        {
            float now = Time.unscaledTime;
            if (now < clip.Next) return;
            int index = clip.Taken++;
            float at = now - clip.Start;
            // After a stall the next frame waits a whole step, rather than
            // several being taken at once.
            clip.Next = Mathf.Max(clip.Next + 1f / ClipFps, now + 0.5f / ClipFps);
            hideToast = true;
            var writer = clip.Writer;
            AfterThisFrame(() => Grab((px, w, h, flip) => writer.Add(index, px, w, h, flip, at), () => writer.Skip(index)));
            if (clip.Taken >= writer.Frames) clip = null;
        }

        // ---- What happened ----

        void Done(string path, Exception error, string what)
        {
            if (error != null) { LastError = error.Message; Say("The capture was not saved: " + error.Message); Debug.LogWarning("Capture: " + error); return; }
            LastPath = path;
            LastError = null;
            Say(what + path);
            Saved?.Invoke(path);
        }

        void Say(string text, float seconds = ToastSeconds)
        {
            toast = text;
            toastUntil = Time.unscaledTime + seconds;
        }

        void OnGUI()
        {
            if (hideToast || Event.current.type != EventType.Repaint) return;
            string text = Toast;
            if (text == null) return;
            if (toastStyle == null)
            {
                toastBack = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                toastBack.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
                toastBack.Apply();
                toastStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(12, 12, 8, 8) };
                toastStyle.normal.background = toastBack;
                toastStyle.normal.textColor = new Color(1f, 0.95f, 0.8f);
            }
            float width = Mathf.Min(Screen.width - 24f, 760f);
            float height = toastStyle.CalcHeight(new GUIContent(text), width);
            GUI.Box(new Rect(12, 12, width, height), text, toastStyle);
        }

        void OnApplicationQuit() => CaptureWriter.WaitIdle(10000);

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (toastBack != null) Destroy(toastBack);
        }
    }
}
