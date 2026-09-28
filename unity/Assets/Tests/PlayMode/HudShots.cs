// HudShots.cs - a picture of the screen as the player sees it, drawn off
// screen at any size: the world through its camera, which may draw only
// the play area, and every canvas over it. The canvases are drawn twice,
// over black and over white, which gives each pixel's coverage exactly.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public static class HudShots
    {
        public static IEnumerator Shoot(Camera world, int w, int h, string path)
        {
            yield return null;
            var under = Render(world, w, h, true);
            var canvases = new List<(Canvas c, RenderMode mode, Camera cam, float plane)>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay)
                    canvases.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
            var go = new GameObject("Capture UI camera");
            go.transform.position = new Vector3(0, -100000f, 0);
            var ui = go.AddComponent<Camera>();
            ui.enabled = false;
            ui.orthographic = true;
            ui.nearClipPlane = 0.1f;
            ui.farClipPlane = 50f;
            ui.allowHDR = false;
            ui.allowMSAA = false;
            ui.clearFlags = CameraClearFlags.SolidColor;
            foreach (var e in canvases)
            {
                e.c.renderMode = RenderMode.ScreenSpaceCamera;
                e.c.worldCamera = ui;
                e.c.planeDistance = 10f;
            }
            ui.backgroundColor = new Color(0, 0, 0, 0);
            var black = Render(ui, w, h, false);
            ui.backgroundColor = Color.white;
            var white = Render(ui, w, h, false);
            foreach (var e in canvases)
            {
                e.c.renderMode = e.mode;
                e.c.worldCamera = e.cam;
                e.c.planeDistance = e.plane;
            }
            Object.Destroy(go);
            // Over black a pixel is colour times coverage, over white it
            // gains what shows through, so the world goes in by that much.
            var outPx = new Color32[under.Length];
            for (int i = 0; i < under.Length; i++)
            {
                Color32 b = black[i], wh = white[i], u = under[i];
                outPx[i] = new Color32(
                    (byte)Mathf.Min(255, b.r + u.r * Mathf.Max(0, wh.r - b.r) / 255),
                    (byte)Mathf.Min(255, b.g + u.g * Mathf.Max(0, wh.g - b.g) / 255),
                    (byte)Mathf.Min(255, b.b + u.b * Mathf.Max(0, wh.b - b.b) / 255), 255);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx);
            tex.Apply(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        static Color32[] Render(Camera cam, int w, int h, bool clearFirst)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            if (clearFirst)
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = null;
            }
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.targetTexture = old;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            Object.Destroy(tex);
            return px;
        }
    }
}
