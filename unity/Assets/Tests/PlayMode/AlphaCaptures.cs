// AlphaCaptures.cs - pictures of the alpha's main menu (its version in the
// corner, the closed doors) and of the game folder screen, at 1080p, drawn
// off screen. With the original's art when OKU_ART_DIR names the game's
// anims folder. Runs only when OKU_ALPHA_CAPTURE_DIR names a folder.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class AlphaCaptures
    {
        [TearDown]
        public void CleanUp()
        {
            BuildStamp.Reset();
            MenuScreens.SizeOverride = null;
            FadeIn.Off = false;
        }

        static IEnumerator Shoot(Canvas canvas, int w, int h, string path)
        {
            var cam = new GameObject("Alpha capture camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            canvas.renderMode = mode;
            canvas.worldCamera = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(cam.gameObject);
        }

        [UnityTest]
        public IEnumerator CaptureTheAlphaScreens()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_ALPHA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_ALPHA_CAPTURE_DIR to capture the alpha's screens");
            Directory.CreateDirectory(dir);
            string art = System.Environment.GetEnvironmentVariable("OKU_ART_DIR");
            FadeIn.Off = true;
            BuildStamp.Version = "Alpha 1 (b2b61d6)";
            BuildStamp.SkirmishOnly = true;
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
            {
                MenuScreens.SizeOverride = size;
                var root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, ArtDir = string.IsNullOrEmpty(art) ? null : art });
                yield return null;
                yield return Shoot(root.Screens.Canvas, size.x, size.y, Path.Combine(dir, $"alpha-menu-{size.y}p.png"));
                root.Screens.Lobby.Menu.Doors[2].Press();
                yield return Shoot(root.Screens.Canvas, size.x, size.y, Path.Combine(dir, $"alpha-menu-multiplayer-{size.y}p.png"));
                Object.Destroy(root.gameObject);
                yield return null;
            }

            string temp = Path.Combine(Path.GetTempPath(), "oku-alpha-capture");
            Directory.CreateDirectory(Path.Combine(temp, "Total Annihilation Kingdoms"));
            File.WriteAllBytes(Path.Combine(temp, "Total Annihilation Kingdoms", "data.hpi"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(temp, "Total Annihilation Kingdoms", "terrain.hpi"), new byte[] { 1 });
            Directory.CreateDirectory(Path.Combine(temp, "Other Game"));
            var screen = GameFolderScreen.Show("", d => { }, null);
            screen.Keep = d => { };
            yield return null;
            var canvas = screen.GetComponentInChildren<Canvas>();
            yield return Shoot(canvas, 1920, 1080, Path.Combine(dir, "alpha-folder-first-run.png"));
            screen.Field.text = Path.Combine(temp, "Missing");
            screen.Check(false);
            yield return Shoot(canvas, 1920, 1080, Path.Combine(dir, "alpha-folder-problem.png"));
            screen.Browser.SetActive(true);
            screen.Tips.gameObject.SetActive(false);
            screen.Open(GameFolder.Clean(temp));
            yield return null;
            yield return Shoot(canvas, 1920, 1080, Path.Combine(dir, "alpha-folder-browse.png"));
            yield return Shoot(canvas, 1280, 720, Path.Combine(dir, "alpha-folder-browse-720p.png"));
            Object.Destroy(screen.gameObject);
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }
}
