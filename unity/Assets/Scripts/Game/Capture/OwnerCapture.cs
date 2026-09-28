// OwnerCapture.cs - F9 saves a PNG of the screen and Shift+F9 records five
// seconds at 12 frames a second with a contact sheet, into the capture
// folder, read back asynchronously and written on a worker. F9 because
// Keys.tdf gives F12 to ClearChat and keeps F9 for screenshots.
using System;
using System.Collections;
using System.Linq;
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
        // How the last picture was taken: Screen, read from what the screen
        // shows, or Cameras, drawn again in batch mode where no frame
        // reaches a screen.
        public string LastRoute { get; private set; }
        public const string ScreenRoute = "Screen", CamerasRoute = "Cameras";
        // Whether the screen comes back upside down here, once a capture has
        // found out: 1 or 0, and -1 before.
        public static int FoundFlipped => flipped;
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
        // Whether the screen comes back upside down: -1 until a capture has
        // been checked against Unity's own picture of it, then 0 or 1.
        static int flipped = -1, checks;

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

        void Update()
        {
            hideToast = false;
            CaptureWriter.Pump();
            Handle(Input.GetKeyDown(Key), Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (clip != null) TickClip();
        }

        // F9 pressed this frame takes a picture, and with Shift records a
        // clip. Returns what it started: "shot", "clip" or null.
        public string Handle(bool keyDown, bool shift)
        {
            if (!keyDown) return null;
            if (shift) { StartClip(); return "clip"; }
            Shot();
            return "shot";
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

        // The size of a capture: the screen's, or 1280 by 720 in batch mode
        // when there is no screen.
        public static Vector2Int CaptureSize => Screen.width > 0 && Screen.height > 0 ? new Vector2Int(Screen.width, Screen.height) : new Vector2Int(1280, 720);

        // Runs once this frame is on the screen, at the end of the frame. In
        // batch mode no frame reaches a screen, so it runs now.
        void AfterThisFrame(Action a)
        {
            if (Application.isBatchMode) a();
            else StartCoroutine(EndOfFrame(a));
        }

        static IEnumerator EndOfFrame(Action a)
        {
            yield return new WaitForEndOfFrame();
            a();
        }

        // The whole screen as the player sees it, menus and all, read back
        // without waiting on the GPU. failed runs when it can't be.
        void Grab(Action<byte[], int, int, bool> got, Action failed)
        {
            if (Application.isBatchMode) { LastRoute = CamerasRoute; GrabCameras(got, failed); return; }
            LastRoute = ScreenRoute;
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) { Fail("the screen has no size", failed); return; }
            var rt = new RenderTexture(w, h, 0) { name = "owner capture" };
            try { ScreenCapture.CaptureScreenshotIntoRenderTexture(rt); }
            catch (Exception e) { Destroy(rt); Fail(e.Message, failed); return; }
            // Some graphics APIs hand the screen back upside down. The first
            // capture is checked against Unity's own picture, read the slow
            // way once, and the answer kept. A screen that reads the same both
            // ways is tried again, three times at most.
            Texture2D check = flipped < 0 && checks++ < 3 ? ScreenCapture.CaptureScreenshotAsTexture() : null;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
                if (req.hasError) { if (check != null) Destroy(check); Fail("the picture could not be read back", failed); return; }
                var px = req.GetData<byte>().ToArray();
                if (check != null)
                {
                    int found = Flipped(px, w, h, check);
                    if (found >= 0) flipped = found;
                    Destroy(check);
                }
                got(px, w, h, flipped >= 0 ? flipped == 1 : SystemInfo.graphicsUVStartsAtTop);
            });
        }

        // 1 when the rows read back come top first against the check picture,
        // which Unity keeps bottom first, 0 when bottom first, and -1 when the
        // picture reads the same both ways.
        public static int Flipped(byte[] px, int w, int h, Texture2D check)
        {
            if (check == null || check.width != w || check.height != h || px.Length < w * h * 4) return -1;
            var c = check.GetPixels32();
            long same = 0, turned = 0;
            foreach (int y in new[] { 0, h / 8, h / 4, h / 3 })
                for (int x = 0; x < w; x += Math.Max(1, w / 64))
                {
                    int o = (y * w + x) * 4;
                    Color32 a = c[y * w + x], b = c[(h - 1 - y) * w + x];
                    same += Math.Abs(px[o] - a.r) + Math.Abs(px[o + 1] - a.g) + Math.Abs(px[o + 2] - a.b);
                    turned += Math.Abs(px[o] - b.r) + Math.Abs(px[o + 1] - b.g) + Math.Abs(px[o + 2] - b.b);
                }
            if (same * 2 < turned) return 0;
            if (turned * 2 < same) return 1;
            return -1;
        }

        // In batch mode: every camera drawn in order into a texture, with the
        // overlay menus drawn by the last, as the screen would show them.
        void GrabCameras(Action<byte[], int, int, bool> got, Action failed)
        {
            var size = CaptureSize;
            var cams = Camera.allCameras.Where(c => c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0) { Fail("no camera draws the screen", failed); return; }
            var top = cams[cams.Count - 1];
            var rt = new RenderTexture(size.x, size.y, 24) { name = "owner capture" };
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).ToList();
            try
            {
                foreach (var c in overlays)
                {
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = top;
                    c.planeDistance = top.nearClipPlane + 0.1f;
                }
                Canvas.ForceUpdateCanvases();
                foreach (var cam in cams)
                {
                    var was = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = was;
                }
            }
            finally
            {
                foreach (var c in overlays) if (c != null) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
            }
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
                if (req.hasError) { Fail("the picture could not be read back", failed); return; }
                got(req.GetData<byte>().ToArray(), size.x, size.y, false);
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
            if (clip.Taken >= writer.Frames)
            {
                clip = null;
                Say("Writing the clip...", 30f);
            }
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

        // No note while a clip records, so none of its frames has it.
        void OnGUI()
        {
            if (hideToast || clip != null || Event.current.type != EventType.Repaint) return;
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

        // A clip cut short by leaving Play still gets its sheet, from the
        // frames it has.
        void EndClip()
        {
            if (clip == null) return;
            clip.Writer.Finish();
            clip = null;
        }

        void OnApplicationQuit()
        {
            EndClip();
            CaptureWriter.WaitIdle(10000);
        }

        void OnDestroy()
        {
            EndClip();
            if (Instance == this) Instance = null;
            if (toastBack != null) Destroy(toastBack);
        }
    }
}
