// TrailerKit.cs - the trailer director's arithmetic: camera poses and the
// eased moves between them, and where a sound sits in the mix by the
// original's rule. No engine and no scene, so the tests run it alone.
using System;
using System.Globalization;
using UnityEngine;

namespace OpenKingdomsUnity.Tests.Trailer
{
    // Where the game camera looks: its focus on the ground, how far back it
    // stands, how steeply it looks down and which way it faces.
    [Serializable]
    public struct ShotPose
    {
        public Vector3 Focus;
        public float Distance, Pitch, Yaw;

        public ShotPose(Vector3 focus, float distance, float pitch, float yaw)
        {
            Focus = focus;
            Distance = distance;
            Pitch = pitch;
            Yaw = yaw;
        }

        // Distance moves by ratio, so a push-in keeps one pace all the way.
        public static ShotPose Lerp(ShotPose a, ShotPose b, float t) => new ShotPose(
            Vector3.Lerp(a.Focus, b.Focus, t),
            Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(0.01f, a.Distance)), Mathf.Log(Mathf.Max(0.01f, b.Distance)), t)),
            Mathf.Lerp(a.Pitch, b.Pitch, t),
            Mathf.LerpAngle(a.Yaw, b.Yaw, t));

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "focus ({0:0.0}, {1:0.0}, {2:0.0}) distance {3:0.0} pitch {4:0.0} yaw {5:0.0}",
                Focus.x, Focus.y, Focus.z, Distance, Pitch, Yaw);
    }

    public static class TrailerKit
    {
        // Zero speed at both ends and no jerk, for slow pans and push-ins.
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        // A move from a to b over a shot, eased, or a still frame when a is b.
        public static Func<float, ShotPose> Move(ShotPose a, ShotPose b) => t => ShotPose.Lerp(a, b, Ease(t));

        public static Func<float, ShotPose> Hold(ShotPose a) => t => a;

        // The original's positional rule (legacy:221181-221194): full volume
        // inside the view and half outside it, no falloff, and pan from the
        // centre across the view's width. viewport is Camera.WorldToViewportPoint.
        public static void Spatial(Vector3 viewport, out int volume, out int pan)
        {
            bool inside = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
            volume = inside ? 0x7f : 0x40;
            float x = viewport.z > 0 ? viewport.x : 1f - viewport.x;
            pan = Mathf.Clamp(Mathf.RoundToInt(64f + 64f * (x - 0.5f)), 0, 127);
        }

        // The first audio sample of a video frame.
        public static long SampleOf(int frame, int fps, int rate) => (long)frame * rate / fps;

        // A number for the sound log, the same on every machine.
        public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
