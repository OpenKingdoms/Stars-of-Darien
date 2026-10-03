// SoundTap.cs - what the game would have played, frame by frame, as a log
// the offline mixer turns into the trailer's sound. The engine plays its
// sounds straight to the audio device and tells Unity nothing, so the tap
// watches what starts them instead: each unit script function as it begins
// (attack, FireWeapon, Dying carry the cues), each shot as it flies and
// lands, the orders the director gives, and the ambient emitters in view.
// The mixer reads the game's own files to know what each one plays.
//
// Lines, tab separated, frames counted from the shot's first picture:
//   U frame stableId unitName function vx vy vz nearEnemy
//   P frame shotId shooterName vx vy vz
//   H frame shotId shooterName struckName water vx vy vz
//   A frame unitName action
//   I frame wav
//   F frame featureName count
// vx vy vz are Camera.WorldToViewportPoint of the place.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed class SoundTap : IDisposable
    {
        public const int AmbientStep = 20;

        readonly IGameBackend b;
        readonly Camera cam;
        readonly StreamWriter w;
        readonly UnitState[] units = new UnitState[8192];
        readonly ProjectileState[] shots = new ProjectileState[8192];
        readonly FeatureState[] feats = new FeatureState[32768];
        readonly byte[] names = new byte[2048];
        readonly Dictionary<uint, Dictionary<string, int>> running = new Dictionary<uint, Dictionary<string, int>>();
        readonly Dictionary<int, Flight> flying = new Dictionary<int, Flight>();
        readonly HashSet<uint> alive = new HashSet<uint>();
        readonly HashSet<int> landed = new HashSet<int>();
        readonly Dictionary<string, int> emitters = new Dictionary<string, int>();
        int unitCount;
        public int Lines { get; private set; }

        struct Flight { public string Shooter; public int Player; public Vector3 At, Velocity; }

        public SoundTap(IGameBackend backend, Camera camera, string path)
        {
            b = backend;
            cam = camera;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            w = new StreamWriter(path, false, new UTF8Encoding(false));
            w.WriteLine("# Stars of Darien trailer sound log, " + TrailerDirector.Fps + " frames a second");
            // What already runs and flies at the first frame started before
            // the shot and is not heard again.
            Observe(-1, false);
        }

        public void Frame(int frame) => Observe(frame, true);

        public void Ack(int frame, int def, string action)
        {
            if (def < 0 || def >= b.UnitDefs.Count) return;
            Line($"A\t{frame}\t{b.UnitDefs[def].Name}\t{action}");
        }

        public void Ui(int frame, string wav) => Line($"I\t{frame}\t{wav}");

        void Observe(int frame, bool emit)
        {
            unitCount = b.ReadUnits(units);
            alive.Clear();
            for (int i = 0; i < unitCount; i++)
            {
                var u = units[i];
                alive.Add(u.StableId);
                var now = Running(u.Handle);
                running.TryGetValue(u.StableId, out var was);
                if (emit && now != null)
                    foreach (var kv in now)
                    {
                        int before = 0;
                        if (was != null) was.TryGetValue(kv.Key, out before);
                        for (int k = before; k < kv.Value; k++)
                        {
                            var v = cam.WorldToViewportPoint(u.Position);
                            Line($"U\t{frame}\t{u.StableId}\t{Name(u.Def)}\t{kv.Key}\t{V(v)}\t{NearEnemy(u)}");
                        }
                    }
                running[u.StableId] = now;
            }
            if (running.Count > alive.Count * 2 + 64)
            {
                var gone = new List<uint>();
                foreach (var id in running.Keys) if (!alive.Contains(id)) gone.Add(id);
                foreach (var id in gone) running.Remove(id);
            }

            int m = b.ReadProjectiles(shots);
            landed.Clear();
            foreach (var id in flying.Keys) landed.Add(id);
            for (int i = 0; i < m; i++)
            {
                var p = shots[i];
                landed.Remove(p.Id);
                if (!flying.TryGetValue(p.Id, out var f))
                {
                    var from = p.Source != Vector3.zero ? p.Source : p.Position;
                    f = new Flight { Shooter = Shooter(p.Player, from), Player = p.Player };
                    if (emit) Line($"P\t{frame}\t{p.Id}\t{f.Shooter}\t{V(cam.WorldToViewportPoint(from))}");
                }
                f.At = p.Position;
                f.Velocity = p.Velocity;
                flying[p.Id] = f;
            }
            foreach (int id in landed)
            {
                var f = flying[id];
                flying.Remove(id);
                if (!emit) continue;
                var at = f.At + f.Velocity * (1f / Mathf.Max(1, b.TicksPerSecond));
                string struck = Struck(f.Player, at);
                var t = b.Terrain;
                bool water = struck == "-" && t != null && t.SeaLevel > 0 && b.GroundHeight(at.x, at.z) < t.SeaLevel;
                Line($"H\t{frame}\t{id}\t{f.Shooter}\t{struck}\t{(water ? 1 : 0)}\t{V(cam.WorldToViewportPoint(at))}");
            }

            if (emit && frame % AmbientStep == 0) Ambient(frame);
        }

        // The emitters in view: features with no picture and no model,
        // which the maps place to carry a sound class.
        void Ambient(int frame)
        {
            emitters.Clear();
            int n = b.ReadFeatures(feats);
            for (int i = 0; i < n; i++)
            {
                var f = feats[i];
                if (f.Model >= 0 || f.Sprite >= 0 || f.Def < 0 || f.Def >= b.FeatureDefs.Count) continue;
                var d = b.FeatureDefs[f.Def];
                if (!string.IsNullOrEmpty(d.ObjectName) || !string.IsNullOrEmpty(d.SequenceName)) continue;
                var v = cam.WorldToViewportPoint(f.Position);
                if (v.z <= 0 || v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1) continue;
                emitters[d.Name] = emitters.TryGetValue(d.Name, out int c) ? c + 1 : 1;
            }
            foreach (var kv in emitters) Line($"F\t{frame}\t{kv.Key}\t{kv.Value}");
        }

        // The script functions a unit is running, each with how many threads run it.
        Dictionary<string, int> Running(int handle)
        {
            int state = OkEngine.okx_unit_anim(handle, names, names.Length);
            if (state < 0) return null;
            int end = Array.IndexOf(names, (byte)0);
            if (end <= 0) return null;
            Dictionary<string, int> set = null;
            int start = 0;
            for (int i = 0; i <= end; i++)
            {
                if (i < end && names[i] != (byte)'\n') continue;
                if (i > start)
                {
                    string s = Encoding.ASCII.GetString(names, start, i - start);
                    set ??= new Dictionary<string, int>();
                    set[s] = set.TryGetValue(s, out int c) ? c + 1 : 1;
                }
                start = i + 1;
            }
            return set;
        }

        // The shot's owner nearest where it left, as the unit that fired it.
        string Shooter(int player, Vector3 from)
        {
            float best = 36f;
            int def = -1;
            for (int i = 0; i < unitCount; i++)
            {
                if (units[i].Player != player) continue;
                float d = (units[i].Position - from).sqrMagnitude;
                if (d < best) { best = d; def = units[i].Def; }
            }
            return Name(def);
        }

        // Another player's unit where the shot came down, as the game finds it.
        string Struck(int player, Vector3 at)
        {
            float best = 2.25f;
            int def = -1;
            for (int i = 0; i < unitCount; i++)
            {
                var u = units[i];
                if (u.Player == player || (u.Flags & UnitFlags.Dying) != 0) continue;
                float dx = u.Position.x - at.x, dz = u.Position.z - at.z;
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; def = u.Def; }
            }
            return Name(def);
        }

        // The nearest enemy, whose body a sword or a claw sounds on.
        string NearEnemy(in UnitState me)
        {
            float best = 16f;
            int def = -1;
            for (int i = 0; i < unitCount; i++)
            {
                var u = units[i];
                if (u.Player == me.Player || (u.Flags & UnitFlags.Dying) != 0) continue;
                float d = (u.Position - me.Position).sqrMagnitude;
                if (d < best) { best = d; def = u.Def; }
            }
            return Name(def);
        }

        string Name(int def) => def >= 0 && def < b.UnitDefs.Count ? b.UnitDefs[def].Name : "-";

        static string V(Vector3 v) => $"{TrailerKit.F(v.x)}\t{TrailerKit.F(v.y)}\t{TrailerKit.F(v.z)}";

        void Line(string s)
        {
            w.WriteLine(s);
            Lines++;
        }

        public void Dispose() => w.Dispose();
    }
}
