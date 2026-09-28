// FlightTable.cs - the numbers behind winged flight, from
// Overrides/Units/flight.json: how fast each flyer beats its wings, climbs
// and sinks, the band round the engine's height it moves in, the script
// functions it flaps and glides by, and the pieces its flight moves with
// their down, up, halfway and glide poses for a model with no such
// functions. Values merge from
// the defaults, then the unit's class, then the unit's own keys.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FlightPiece
    {
        public string Name;
        public Quaternion Down, Up, Glide;        // local turns in the model's authored frame
        public Vector3 DownMove, UpMove, GlideMove; // world units from the rest offset
        public bool HasMove;
        // Where the upstroke and the downstroke pass halfway, for a stroke
        // that folds or loops rather than swinging on one hinge.
        public Quaternion Mid, MidDown;
        public Vector3 MidMove, MidDownMove;
        public bool HasMid, HasMidDown;
        public bool KeepY;                        // keeps the script's own turn about y, as a head turner sets it
        public Vector3 Hinge;                     // the axis Down turns about to reach Up
        public float Sweep;                       // and how far, in degrees
        public float Lag;                         // share of a beat this piece trails the stroke
        public float Wobble;                      // degrees of glide correction
        public float Side = 1f;                   // wobble rate, 1 left or centre, 1.13 right
    }

    public sealed class FlightType
    {
        public string Name, Class;
        public float Period, Downstroke, Amplitude, ForcedAmplitude, ForcedPeriod;
        public float Climb, Sink, SinkSlow, Lower, Upper, Stall, ClimbForce;
        public float Ease, Blend, Jitter, WobbleHz;
        public bool Glides = true;                                  // false beats the wings all the time
        public FlightPiece[] Pieces = Array.Empty<FlightPiece>();   // mirrors included
        // State name ("flap", "glide") to the script function or clip that plays it.
        public readonly Dictionary<string, string> Clips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Clip(string state, string fallback) => Clips.TryGetValue(state, out var c) ? c : fallback;
    }

    public sealed class FlightTable
    {
        public const string FileName = "flight.json";
        // A script move of 1, an engine pixel, in world units.
        public const float MoveUnit = 1f / 16f;

        readonly Dictionary<string, FlightType> units = new Dictionary<string, FlightType>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Warnings = new List<string>();
        public IEnumerable<FlightType> Types => units.Values;
        public int Count => units.Count;

        // By unit name first, then by model name, in any case.
        public FlightType Find(string unitName, string objectName)
        {
            if (!string.IsNullOrEmpty(unitName) && units.TryGetValue(unitName, out var t)) return t;
            if (!string.IsNullOrEmpty(objectName) && units.TryGetValue(objectName, out t)) return t;
            return null;
        }

        // A script's turn, `turn piece to x-axis <x>` and so on, as the
        // engine builds it: R = Ry(-y) Rx(-x) Rz(-z).
        public static Quaternion Cob(Vector3 degrees) =>
            Quaternion.AngleAxis(-degrees.y, Vector3.up) * Quaternion.AngleAxis(-degrees.x, Vector3.right) *
            Quaternion.AngleAxis(-degrees.z, Vector3.forward);

        // The right side of a model is the left reflected across its x.
        public static Vector3 MirrorTurn(Vector3 t) => new Vector3(t.x, -t.y, -t.z);
        public static Vector3 MirrorMove(Vector3 m) => new Vector3(-m.x, m.y, m.z);

        // The engine places a script move at (-x, y, -z) from the rest offset.
        public static Vector3 MoveToLocal(Vector3 m) => new Vector3(-m.x, m.y, -m.z) * MoveUnit;

        public static void HingeOf(Quaternion down, Quaternion up, out Vector3 axis, out float degrees)
        {
            (up * Quaternion.Inverse(down)).ToAngleAxis(out degrees, out axis);
            if (degrees > 180f) { degrees = 360f - degrees; axis = -axis; }
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) axis = Vector3.forward;
        }

        public static FlightTable Parse(string json)
        {
            var table = new FlightTable();
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) { table.Warnings.Add("flight table is not a JSON object"); return table; }
            var defaults = MiniJson.Obj(root, "defaults") ?? new Dictionary<string, object>();
            var classes = MiniJson.Obj(root, "classes") ?? new Dictionary<string, object>();
            var list = MiniJson.Obj(root, "units");
            if (list == null) return table;
            foreach (var kv in list)
            {
                var t = Build(kv.Key, kv.Value as Dictionary<string, object>, defaults, classes, out string why);
                if (t == null) table.Warnings.Add($"flight entry {kv.Key} skipped: {why}");
                else table.units[kv.Key] = t;
            }
            return table;
        }

        static FlightType Build(string name, Dictionary<string, object> unit, Dictionary<string, object> defaults,
            Dictionary<string, object> classes, out string why)
        {
            why = null;
            if (unit == null) { why = "not an object"; return null; }
            string cls = MiniJson.Text(unit, "class", "");
            var clsKeys = cls.Length > 0 ? MiniJson.Obj(classes, cls) : null;
            if (cls.Length > 0 && clsKeys == null) { why = $"no class {cls}"; return null; }
            float N(string key, float fallback)
            {
                if (unit.TryGetValue(key, out var v) && v is double u) return (float)u;
                if (clsKeys != null && clsKeys.TryGetValue(key, out v) && v is double c) return (float)c;
                if (defaults.TryGetValue(key, out v) && v is double d) return (float)d;
                return fallback;
            }
            var t = new FlightType
            {
                Name = name, Class = cls,
                Period = N("period", 0f), Downstroke = N("downstroke", 0.5f),
                Amplitude = N("amplitude", 1f), ForcedAmplitude = N("forcedAmplitude", 1f), ForcedPeriod = N("forcedPeriod", 1f),
                Climb = N("climb", 0f), Sink = N("sink", 0f), SinkSlow = N("sinkSlow", 1f),
                Lower = N("lower", 0f), Upper = N("upper", 0f), Stall = N("stall", 0f), ClimbForce = N("climbForce", 0.3f),
                Ease = N("ease", 0.35f), Blend = N("blend", 0.3f), Jitter = N("jitter", 0f), WobbleHz = N("wobbleHz", 0.4f),
            };
            if (unit.TryGetValue("glides", out var gv) && gv is bool gb) t.Glides = gb;
            else if (clsKeys != null && clsKeys.TryGetValue("glides", out gv) && gv is bool gc) t.Glides = gc;
            if (!(t.Period > 0f)) why = "period must be above 0";
            else if (!(t.Downstroke > 0f && t.Downstroke < 1f)) why = "downstroke must be between 0 and 1";
            else if (!(t.Lower < t.Upper)) why = "lower must be below upper";
            else if (!(t.Climb > 0f && t.Sink > 0f)) why = "climb and sink must be above 0";
            else if (!(t.ForcedPeriod > 0f)) why = "forcedPeriod must be above 0";
            else if (!(t.Jitter >= 0f && t.Jitter < 1f)) why = "jitter must be from 0 to below 1";
            if (why != null) return null;

            float lag = N("lag", 0f);
            var pieces = new List<FlightPiece>();
            foreach (var o in MiniJson.Arr(unit, "pieces") ?? new List<object>())
            {
                var p = o as Dictionary<string, object>;
                string piece = MiniJson.Text(p, "piece", "");
                var down = Vec(p, "down");
                var up = Vec(p, "up");
                if (piece.Length == 0 || down == null || up == null) { why = $"piece {piece} needs a name, down and up"; return null; }
                var glide = Vec(p, "glide") ?? (down.Value + up.Value) * 0.5f;
                var dm = Vec(p, "downMove");
                var um = Vec(p, "upMove");
                bool moves = dm != null || um != null;
                var d = dm ?? Vector3.zero;
                var u = um ?? d;
                if (dm == null) d = u;
                var gm = Vec(p, "glideMove") ?? (d + u) * 0.5f;
                var mid = Vec(p, "mid");
                var midDown = Vec(p, "midDown");
                var mm = Vec(p, "midMove") ?? (d + u) * 0.5f;
                var mdm = Vec(p, "midDownMove") ?? (d + u) * 0.5f;
                float pieceLag = (float)MiniJson.Num(p, "lag", lag), wobble = (float)MiniJson.Num(p, "wobble", 0);
                bool keepY = p.TryGetValue("keepY", out var ky) && ky is bool kb && kb;
                var left = Piece(piece, down.Value, up.Value, glide, moves, d, u, gm, pieceLag, wobble, 1f);
                Mids(left, mid, midDown, mm, mdm, keepY);
                pieces.Add(left);
                string mirror = MiniJson.Text(p, "mirror", "");
                if (mirror.Length > 0)
                {
                    var right = Piece(mirror, MirrorTurn(down.Value), MirrorTurn(up.Value), MirrorTurn(glide), moves,
                        MirrorMove(d), MirrorMove(u), MirrorMove(gm), pieceLag, wobble, 1.13f);
                    Mids(right, mid.HasValue ? MirrorTurn(mid.Value) : (Vector3?)null, midDown.HasValue ? MirrorTurn(midDown.Value) : (Vector3?)null,
                        MirrorMove(mm), MirrorMove(mdm), keepY);
                    pieces.Add(right);
                }
            }
            t.Pieces = pieces.ToArray();
            var clips = MiniJson.Obj(unit, "clips");
            if (clips != null) foreach (var c in clips) if (c.Value is string s) t.Clips[c.Key] = s;
            return t;
        }

        static FlightPiece Piece(string name, Vector3 down, Vector3 up, Vector3 glide, bool moves, Vector3 dm, Vector3 um, Vector3 gm,
            float lag, float wobble, float side)
        {
            var p = new FlightPiece
            {
                Name = name, Down = Cob(down), Up = Cob(up), Glide = Cob(glide), HasMove = moves,
                DownMove = MoveToLocal(dm), UpMove = MoveToLocal(um), GlideMove = MoveToLocal(gm),
                Lag = lag, Wobble = wobble, Side = side,
            };
            HingeOf(p.Down, p.Up, out p.Hinge, out p.Sweep);
            return p;
        }

        static void Mids(FlightPiece p, Vector3? mid, Vector3? midDown, Vector3 midMove, Vector3 midDownMove, bool keepY)
        {
            p.HasMid = mid.HasValue;
            p.HasMidDown = midDown.HasValue;
            p.Mid = mid.HasValue ? Cob(mid.Value) : Quaternion.Slerp(p.Down, p.Up, 0.5f);
            p.MidDown = midDown.HasValue ? Cob(midDown.Value) : Quaternion.Slerp(p.Down, p.Up, 0.5f);
            p.MidMove = MoveToLocal(midMove);
            p.MidDownMove = MoveToLocal(midDownMove);
            p.KeepY = keepY;
        }

        static Vector3? Vec(Dictionary<string, object> o, string key)
        {
            var a = MiniJson.Arr(o, key);
            if (a == null || a.Count != 3 || !(a[0] is double x) || !(a[1] is double y) || !(a[2] is double z)) return null;
            return new Vector3((float)x, (float)y, (float)z);
        }

        // ---- The committed table ----

        public static string PathIn(string projectDir) =>
            Path.Combine(projectDir, OverrideIndex.Folder(OverrideKind.Unit), FileName);

        static FlightTable loaded;
        static bool tried;
        // Moves on every Forget, so a cache keyed on the table can tell.
        public static int Version { get; private set; }

        // Read once per session, empty when the file is missing or broken.
        public static FlightTable Load()
        {
            if (tried) return loaded;
            tried = true;
            string path = PathIn(OverrideLoader.ProjectDir);
            try { loaded = File.Exists(path) ? Parse(File.ReadAllText(path)) : new FlightTable(); }
            catch (Exception e) { Debug.LogWarning($"Flight table {path} was not read: {e.Message}"); loaded = new FlightTable(); }
            foreach (var w in loaded.Warnings) Debug.LogWarning(w);
            return loaded;
        }

        // Drops the cache, so the next Load reads the file again.
        public static void Forget()
        {
            tried = false;
            loaded = null;
            Version++;
            FlightPose.Forget();
        }

        // For tests and tools: use this table until the next Forget.
        public static void Use(FlightTable table)
        {
            loaded = table ?? new FlightTable();
            tried = true;
            Version++;
            FlightPose.Forget();
        }
    }
}
