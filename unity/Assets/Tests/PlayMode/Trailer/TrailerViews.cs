// TrailerViews.cs - where the camera stands for a shot: nothing between it
// and the subject (no hill, no tree), the sun behind it, the map's inland
// side ahead of it rather than its edge, and as much scenery in frame as
// the place offers.
using System;
using System.Collections.Generic;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed partial class TrailerDirector
    {
        // Which way the sunlight runs, as a camera yaw that has the sun behind it.
        public float SunYaw
        {
            get
            {
                var sun = Root.World.Atmosphere.Sun;
                if (sun == null) return 150f;
                var f = sun.transform.forward;
                f.y = 0;
                return f.sqrMagnitude > 1e-4f ? YawOf(f) : 150f;
            }
        }

        struct Blocker { public Vector3 P; public float Top, R; }

        List<Blocker> Blockers()
        {
            int n = B.ReadFeatures(featBuf);
            var list = new List<Blocker>(n);
            for (int i = 0; i < n; i++)
            {
                var f = featBuf[i];
                if (f.Model < 0 && f.Sprite < 0) continue;
                if (f.Def < 0 || f.Def >= B.FeatureDefs.Count) continue;
                var d = B.FeatureDefs[f.Def];
                float h = Mathf.Min(d.Height * 0.6f, 9f);
                if (h < 0.6f) continue;
                list.Add(new Blocker { P = f.Position, Top = f.Position.y + h + 0.5f, R = Mathf.Max(1.3f, Mathf.Max(d.Footprint.x, d.Footprint.y) * 0.6f) });
            }
            // Buildings stand in the way as much as trees do, and so do the
            // tall masts of ships.
            foreach (var u in Units(u => B.UnitDefs[u.Def].IsBuilding || B.UnitDefs[u.Def].Float != FloatKind.None && !B.UnitDefs[u.Def].CanFly))
            {
                var fp = B.UnitDefs[u.Def].Footprint;
                list.Add(new Blocker { P = u.Position, Top = u.Position.y + 5f, R = Mathf.Max(1.5f, Mathf.Max(fp.x, fp.y) * 0.6f) });
            }
            return list;
        }

        public static Vector3 Forward(float yaw) => Quaternion.Euler(0, yaw, 0) * Vector3.forward;

        public Vector3 CameraAt(ShotPose p) => p.Focus - Quaternion.Euler(p.Pitch, p.Yaw, 0) * Vector3.forward * p.Distance;

        bool OnMap(Vector3 p, float margin) =>
            p.x > margin && p.x < Map.Size.x - margin && -p.z > margin && -p.z < Map.Size.y - margin;

        bool Wet(Vector3 p)
        {
            var t = B.Terrain;
            return t != null && t.SeaLevel > 0 && B.GroundHeight(p.x, p.z) < t.SeaLevel + 0.3f;
        }

        // How good a pose is: hidden subject and the map's edge cost the most,
        // then the sun in the lens, and scenery in frame earns a little.
        // Half the view's width over its depth: 40 degrees tall at 16:9.
        const float HalfWide = 0.66f;

        float Score(ShotPose p, List<Blocker> blockers, bool land, out string why, Vector3? mustSee = null)
        {
            why = "";
            float s = 0;
            var cam = CameraAt(p);
            var focus = p.Focus;
            var flat = focus - cam;
            flat.y = 0;
            float len = flat.magnitude;
            var fwd = flat / Mathf.Max(0.01f, len);
            var right = Vector3.Cross(Vector3.up, fwd);
            if (!OnMap(cam, 2f)) { s -= 200; why += " camera off map"; }
            if (land && Wet(focus)) { s -= 120; why += " focus wet"; }
            // At sea the subject is on the water and so is the camera.
            if (!land && !Wet(focus)) { s -= 60; why += " focus ashore"; }
            if (!land && !Wet(cam)) { s -= 40; why += " camera ashore"; }
            if (B.GroundHeight(cam.x, cam.z) > cam.y - 1f) { s -= 200; why += " camera underground"; }
            // Inland ahead: the view's far ground still on the map.
            for (int k = 1; k <= 3; k++)
                if (!OnMap(focus + fwd * (k * 30f), 0f)) { s -= 25; why += " edge ahead"; break; }
            // Ground between the camera and the subject.
            for (int i = 1; i < 24; i++)
            {
                float t = i / 24f;
                var q = Vector3.Lerp(cam, focus + Vector3.up * 1.2f, t);
                if (B.GroundHeight(q.x, q.z) > q.y - 0.3f) { s -= 150; why += " hill in the way"; break; }
            }
            int hidden = 0, scenery = 0;
            foreach (var b in blockers)
            {
                var d = b.P - cam;
                d.y = 0;
                float along = Vector3.Dot(d, fwd), lat = Mathf.Abs(Vector3.Dot(d, right));
                if (along <= 0.5f) continue;
                float lineY = Mathf.Lerp(cam.y, focus.y + 1.2f, Mathf.Clamp01(along / len));
                if (along < len * 0.88f)
                {
                    // In front of the subject and tall enough to cover it, or
                    // near the lens and tall enough to fill part of the frame.
                    if (lat < b.R + 1.5f && b.Top > lineY) hidden += 2;
                    else if (along < len * 0.6f && lat < along * HalfWide + b.R && b.Top > lineY - 1f) hidden++;
                }
                else if (along < len + 55f && lat < along * HalfWide) scenery++;
            }
            if (hidden > 0) { s -= 25 * hidden; why += $" {hidden} in the way"; }
            // Ground that rises above the middle of the frame on either side.
            int bumps = 0;
            for (int i = 1; i <= 4; i++)
            {
                float along = len * (0.05f + 0.2f * i);
                float lineY = Mathf.Lerp(cam.y, focus.y, along / len);
                foreach (float side in new[] { -0.8f, -0.4f, 0.4f, 0.8f })
                {
                    var q = cam + fwd * along + right * (side * along * HalfWide);
                    if (B.GroundHeight(q.x, q.z) > lineY + 0.3f) bumps++;
                }
            }
            for (int i = 0; i <= 3; i++)
            {
                float along = len * (0.08f + 0.13f * i);
                foreach (float side in new[] { -0.5f, 0f, 0.5f })
                {
                    var q = cam + fwd * along + right * (side * along * HalfWide);
                    if (B.GroundHeight(q.x, q.z) > focus.y + 0.8f) bumps++;
                }
            }
            if (bumps > 0) { s -= 12 * bumps; why += $" {bumps} ground in frame"; }
            if (mustSee is Vector3 m)
            {
                var d = m - cam;
                d.y = 0;
                float along = Vector3.Dot(d, fwd), lat = Mathf.Abs(Vector3.Dot(d, right));
                if (along > len * 0.5f && along < len + 20f && lat < along * HalfWide * 0.8f) s += 25;
                else why += " misses its landmark";
            }
            s += Mathf.Min(scenery, 30);
            s -= Mathf.Abs(Mathf.DeltaAngle(p.Yaw, SunYaw)) * 0.4f;
            return s;
        }

        // The best of a set of poses near a subject: each yaw, and the focus
        // moved up to `jitter` about, for a pose with nothing in the way.
        public ShotPose BestPose(Vector3 focus, float distance, float pitch, IEnumerable<float> yaws, float jitter, bool land = true, string label = null, params Vector3[] subjects)
        {
            var blockers = Blockers();
            var best = new ShotPose(focus, distance, pitch, SunYaw);
            float top = float.MinValue;
            string bestWhy = "";
            foreach (float yaw in yaws)
                for (float dx = -jitter; dx <= jitter + 0.01f; dx += Mathf.Max(1f, jitter / 3f))
                    for (float dz = -jitter; dz <= jitter + 0.01f; dz += Mathf.Max(1f, jitter / 3f))
                    {
                        var f = Ground(focus.x + dx, focus.z + dz);
                        if (!land && B.Terrain.SeaLevel > f.y) f.y = B.Terrain.SeaLevel;
                        var p = new ShotPose(f, distance, pitch, yaw);
                        float s = Score(p, blockers, land, out string why) - new Vector2(dx, dz).magnitude * 0.6f;
                        var cam = CameraAt(p);
                        foreach (var subject in subjects)
                            if (!Sees(cam, subject + Vector3.up * 1.5f)) { s -= 80; why += " subject hidden"; }
                        if (s > top) { top = s; best = p; bestWhy = why; }
                    }
            Note($"{label ?? "pose"}: {best} score {top:0}{bestWhy}");
            return best;
        }

        // A place to film a march from: open ground near `near`, with the sun
        // behind the camera and scenery beyond the subject.
        public ShotPose OpenView(Vector3 near, float radius, float distance, float pitch, float yawSpread, string label, Vector3? mustSee = null)
        {
            var blockers = Blockers();
            float sun = SunYaw;
            var best = new ShotPose(near, distance, pitch, sun);
            float top = float.MinValue;
            string bestWhy = "";
            for (float yaw = sun - yawSpread; yaw <= sun + yawSpread + 0.01f; yaw += Mathf.Max(5f, yawSpread / 2f))
                for (float dx = -radius; dx <= radius; dx += 4f)
                    for (float dz = -radius; dz <= radius; dz += 4f)
                    {
                        var f = Ground(near.x + dx, near.z + dz);
                        if (!OnMap(f, 20f)) continue;
                        var p = new ShotPose(f, distance, pitch, yaw);
                        float s = Score(p, blockers, true, out string why, mustSee);
                        // The march path, from beyond the focus to it, open and dry.
                        var fwd = Forward(yaw);
                        var cam = CameraAt(p);
                        for (int k = 0; k <= 4; k++)
                        {
                            var q = f + fwd * (k * 4f);
                            if (Wet(q)) { s -= 40; why += " march wet"; break; }
                            if (Mathf.Abs(B.GroundHeight(q.x, q.z) - f.y) > 3f) { s -= 40; why += " march steep"; break; }
                            if (!Sees(cam, Ground(q.x, q.z) + Vector3.up * 1.2f)) { s -= 60; why += " march hidden"; break; }
                        }
                        s -= new Vector2(dx, dz).magnitude * 0.15f;
                        if (s > top) { top = s; best = p; bestWhy = why; }
                    }
            Note($"{label}: {best} score {top:0}{bestWhy}");
            return best;
        }

        // Whether the ground between two points leaves the line clear.
        public bool Sees(Vector3 from, Vector3 to)
        {
            for (int i = 1; i < 20; i++)
            {
                var q = Vector3.Lerp(from, to, i / 20f);
                if (B.GroundHeight(q.x, q.z) > q.y - 0.2f) return false;
            }
            return true;
        }

        // The start with the most of the map ahead of it in the sun's
        // direction, so a camera with the sun behind it looks inland.
        public int SunStart(string map)
        {
            var m = FindMap(map);
            var dir = Forward(150f);
            int best = 0;
            float most = float.MinValue;
            for (int i = 0; i < m.Starts.Length; i++)
            {
                var p = new Vector3(m.Starts[i].x, 0, -m.Starts[i].y);
                float room = 0;
                while (room < 400f)
                {
                    var q = p + dir * room;
                    if (q.x < 0 || q.x > m.Size.x || -q.z < 0 || -q.z > m.Size.y) break;
                    room += 4f;
                }
                if (room > most) { most = room; best = i; }
            }
            return best;
        }

        // Open water near a point: the wet spot with the most sea round it.
        public Vector3 OpenWater(Vector3 near, float radius)
        {
            var best = near;
            float top = float.MinValue;
            for (float dx = -radius; dx <= radius; dx += 4f)
                for (float dz = -radius; dz <= radius; dz += 4f)
                {
                    var p = Ground(near.x + dx, near.z + dz);
                    if (!Wet(p) || !OnMap(p, 12f)) continue;
                    int open = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        var q = p + Forward(k * 22.5f) * 12f;
                        if (Wet(q)) open++;
                    }
                    float s = open * 4f - new Vector2(dx, dz).magnitude * 0.3f;
                    if (s > top) { top = s; best = p; }
                }
            return new Vector3(best.x, B.Terrain.SeaLevel, best.z);
        }

        // Flat open ground near a point, for an army to stand on and a line
        // to be drawn ahead of it: no slope, no water and no scenery in the way.
        public Vector3 FlatGround(Vector3 near, float radius, float span)
        {
            var blockers = Blockers();
            var best = near;
            float top = float.MinValue;
            for (float dx = -radius; dx <= radius; dx += 4f)
                for (float dz = -radius; dz <= radius; dz += 4f)
                {
                    var c = Ground(near.x + dx, near.z + dz);
                    if (!OnMap(c, span + 10f)) continue;
                    float lo = c.y, hi = c.y;
                    bool wet = false;
                    for (float x = -span; x <= span; x += 3f)
                        for (float z = -span; z <= span; z += 3f)
                        {
                            var q = Ground(c.x + x, c.z + z);
                            lo = Mathf.Min(lo, q.y);
                            hi = Mathf.Max(hi, q.y);
                            wet |= Wet(q);
                        }
                    if (wet) continue;
                    int clutter = 0;
                    foreach (var b in blockers)
                        if (Mathf.Abs(b.P.x - c.x) < span && Mathf.Abs(b.P.z - c.z) < span) clutter++;
                    float s = -(hi - lo) * 6f - clutter * 3f - new Vector2(dx, dz).magnitude * 0.2f;
                    if (s > top) { top = s; best = c; }
                }
            Note($"flat ground at {best}, score {top:0}");
            return best;
        }

        // Yaws that look across a line of travel, then three quarters on,
        // each way round.
        public static float[] Across(Vector3 along)
        {
            float a = YawOf(along);
            return new[] { a + 90f, a - 90f, a + 60f, a - 60f, a + 120f, a - 120f };
        }
    }
}
