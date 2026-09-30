// SceneryViews.cs - where on a map the scenery named by a view is thickest,
// and the same two camera views of it wherever they are taken: the classic
// camera and a low three-quarter one, in a 1280x720 picture. A built
// player takes them with "-okSmokeViews" (SmokeRun) and the editor with
// SceneryCaptures, so the two can be laid side by side.
//
// A view is "label:anchor:near:radius". The spot is the feature whose name
// matches anchor with the most features matching near within radius units
// of it. Names match without case, * stands for any run of letters and a
// comma separates choices, so "henge:*mana*:*henge*:8" finds standing
// stones round a lodestone.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public static class SceneryViews
    {
        public const int Width = 1280, Height = 720;

        public sealed class Spec
        {
            public string Label, Anchor, Near;
            public float Radius = 8f;
        }

        public static readonly (string name, float pitch, float yaw, float zoom)[] Angles =
        {
            ("classic", GameCamera.ClassicPitch, 0f, 26f),
            ("low", 34f, 30f, 20f),
        };

        // "label:anchor:near:radius;..." to specs, skipping any that do not read.
        public static List<Spec> Parse(string text)
        {
            var list = new List<Spec>();
            foreach (var part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split(':');
                if (f.Length < 3) continue;
                var s = new Spec { Label = f[0].Trim(), Anchor = f[1].Trim(), Near = f[2].Trim() };
                if (f.Length > 3 && float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) && r > 0f) s.Radius = r;
                if (s.Label.Length > 0) list.Add(s);
            }
            return list;
        }

        public static bool Matches(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            foreach (var one in pattern.Split(','))
            {
                string rx = "^" + Regex.Escape(one.Trim()).Replace("\\*", ".*") + "$";
                if (Regex.IsMatch(name ?? "", rx, RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        // The spot on the ground for a view on the battle now, or null when
        // no feature matches its anchor.
        public static Vector3? Spot(IGameBackend b, Spec s)
        {
            var names = new Dictionary<int, string>();
            foreach (var d in b.FeatureDefs) names[d.Id] = d.Name;
            var all = new FeatureState[16384];
            int n = Mathf.Min(b.ReadFeatures(all), all.Length);
            Vector3? best = null;
            int most = -1;
            for (int i = 0; i < n; i++)
            {
                if (!names.TryGetValue(all[i].Def, out var name) || !Matches(name, s.Anchor)) continue;
                var at = all[i].Position;
                int count = 0;
                for (int j = 0; j < n; j++)
                {
                    if (!names.TryGetValue(all[j].Def, out var other) || !Matches(other, s.Near)) continue;
                    var d = all[j].Position - at;
                    if (d.x * d.x + d.z * d.z <= s.Radius * s.Radius) count++;
                }
                if (count > most) { most = count; best = at; }
            }
            return best;
        }

        // Frames each angle on the spot, waits for the camera to settle, and
        // hands back its picture as PNG bytes with the angle's name.
        public static IEnumerator Take(WorldView world, Vector3 spot, Action<string, byte[]> picture)
        {
            var gc = world.Camera;
            var cam = gc.GetComponent<Camera>();
            foreach (var (name, pitch, yaw, zoom) in Angles)
            {
                gc.focus = new Vector3(spot.x, gc.SmoothGround(spot), spot.z);
                gc.pitch = pitch;
                gc.yaw = yaw;
                gc.Zoom(zoom);
                var was = gc.transform.position;
                for (int i = 0, still = 0; i < 3000 && still < 20; i++)
                {
                    yield return null;
                    still = (gc.transform.position - was).sqrMagnitude < 1e-8f ? still + 1 : 0;
                    was = gc.transform.position;
                }
                // A few more frames, so models loaded on the way in are drawn.
                for (int i = 0; i < 10; i++) yield return null;
                var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                var target = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = target;
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply(false);
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                picture(name, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
