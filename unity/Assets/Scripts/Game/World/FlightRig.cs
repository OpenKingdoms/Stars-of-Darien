// FlightRig.cs - a flyer's own fly and soar functions from its unit
// script, as clips the animator plays on its own clock. Each function is
// posed once per unit type a tick at a time, cut to one cycle and kept as
// keyframes: the turn and offset from its parent of every piece it moves.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FlightRig
    {
        // The model posed by a function at a time in seconds, model space.
        public delegate int Sampler(float seconds, PiecePose[] into);

        public sealed class Clip
        {
            public float Seconds;       // one cycle, as the engine plays it
            public float[] Times;       // each key's start, 0 to 1 through the cycle
            public Quaternion[] Turns;  // key * channels + channel
            public Vector3[] Moves;     // the same, each piece's offset from its parent

            // The keys either side of a phase and how far it is between them.
            public void Locate(float phase, out int a, out int b, out float w)
            {
                int lo = 0, hi = Times.Length - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (Times[mid] <= phase) lo = mid; else hi = mid - 1;
                }
                a = lo;
                b = a + 1 < Times.Length ? a + 1 : 0;
                float end = a + 1 < Times.Length ? Times[a + 1] : 1f;
                w = end > Times[a] ? Mathf.Clamp01((phase - Times[a]) / (end - Times[a])) : 0f;
            }
        }

        public int[] Channel;       // per model piece, its channel or -1
        public int[] Piece;         // per channel, its model piece
        // Per channel: neither clip turns it about y, so the script's y turn
        // stays, and a head keeps looking about.
        public bool[] KeepY;
        public Clip Flap, Glide;    // Glide is null for a unit with no glide function
        public float Top;           // the flap clip's phase at the top of the stroke
        public float Downstroke;    // and the share of it spent going down
        public float GlideSeconds => Glide != null ? Glide.Seconds : 0f;

        // A channel's turn and offset at a flap and a glide position, crossed by glide.
        public void Sample(int ch, int fa, int fb, float fw, int ga, int gb, float gw, float glide, out Quaternion q, out Vector3 at)
        {
            int c = KeepY.Length;
            q = Nlerp(Flap.Turns[fa * c + ch], Flap.Turns[fb * c + ch], fw);
            at = Lerp(Flap.Moves[fa * c + ch], Flap.Moves[fb * c + ch], fw);
            if (glide <= 0f || Glide == null) return;
            var gq = Nlerp(Glide.Turns[ga * c + ch], Glide.Turns[gb * c + ch], gw);
            var gm = Lerp(Glide.Moves[ga * c + ch], Glide.Moves[gb * c + ch], gw);
            q = Nlerp(q, gq, glide);
            at = Lerp(at, gm, glide);
        }

        static Vector3 Lerp(in Vector3 a, in Vector3 b, float t) =>
            new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        public static Quaternion Nlerp(in Quaternion a, Quaternion b, float t)
        {
            if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            float x = a.x + (b.x - a.x) * t, y = a.y + (b.y - a.y) * t, z = a.z + (b.z - a.z) * t, w = a.w + (b.w - a.w) * t;
            float k = 1f / Mathf.Sqrt(x * x + y * y + z * z + w * w);
            return new Quaternion(x * k, y * k, z * k, w * k);
        }

        // ---- Baking ----

        // The longest cycle looked for, in seconds, and how close two poses must be to count as one.
        public const float LongestCycle = 3f;
        const float TurnEps = 2e-5f, SameMove = 1e-3f;

        // One function sampled a tick at a time, with its cycle found.
        sealed class Run
        {
            public int Count, Pieces, Cycle, Start, Longest;
            public Quaternion[] Q, Xz;  // each piece's turn from its parent, and that without its y turn
            public float[] Y;           // the y turn, radians
            public Vector3[] M;
            public int At(int tick, int p) => tick * Pieces + p;
        }

        public static FlightRig Bake(FlightType type, ModelData d, float rate, Sampler flap, Sampler glide) =>
            Bake(type, d, rate, flap, glide, out _);

        // The rig for a model, or null and why when there is no flap clip to play.
        public static FlightRig Bake(FlightType type, ModelData d, float rate, Sampler flap, Sampler glide, out string why)
        {
            why = null;
            if (type == null || d?.Pieces == null || flap == null || !(rate > 0f)) { why = "nothing to bake"; return null; }
            int n = Mathf.Min(d.Pieces.Length, EntityRenderer.MaxPieces);
            var f = Take(flap, d, n, rate, type.Period, out why);
            if (f == null) return null;
            var g = glide != null ? Take(glide, d, n, rate, 0f, out string glideWhy) : null;

            var rig = new FlightRig { Channel = new int[d.Pieces.Length] };
            var pieces = new System.Collections.Generic.List<int>();
            var keepY = new System.Collections.Generic.List<bool>();
            bool flaps = false;
            for (int p = 0; p < rig.Channel.Length; p++)
            {
                rig.Channel[p] = -1;
                if (p >= n || d.Pieces[p].Parent < 0 || d.Pieces[p].Parent >= p) continue;
                int fy = YKind(f, p), gy = g != null ? YKind(g, p) : 0;
                float f0 = f.Y[f.At(f.Start, p)], g0 = g != null ? g.Y[g.At(g.Start, p)] : f0;
                bool beats = fy == 1 || gy == 1 || (fy == 0 && gy == 0 && Mathf.Abs(Mathf.DeltaAngle(f0 * Mathf.Rad2Deg, g0 * Mathf.Rad2Deg)) > 0.05f);
                bool moves = beats || Moves(f, p) || (g != null && (Moves(g, p) || !Same(f, f.Start, g, g.Start, p)));
                if (!moves) continue;
                flaps |= fy == 1 || Moves(f, p);
                rig.Channel[p] = pieces.Count;
                pieces.Add(p);
                keepY.Add(!beats);
            }
            if (!flaps) { why = "its flap function moves nothing"; return null; }
            rig.Piece = pieces.ToArray();
            rig.KeepY = keepY.ToArray();
            rig.Flap = Keys(f, rig, rate);
            if (g != null) rig.Glide = Keys(g, rig, rate);
            Stroke(rig, f, type, d);
            return rig;
        }

        // Samples a function three of the longest cycles long and finds its
        // cycle among the last two, where it has settled. Null when the
        // backend cannot pose it or it never repeats.
        static Run Take(Sampler s, ModelData d, int n, float rate, float period, out string why)
        {
            why = null;
            int longest = Mathf.CeilToInt(Mathf.Max(LongestCycle, 1.6f * period) * rate);
            int count = 3 * longest + 2;
            var r = new Run
            {
                Count = count, Pieces = n, Longest = longest,
                Q = new Quaternion[count * n], Xz = new Quaternion[count * n], Y = new float[count * n], M = new Vector3[count * n],
            };
            var buf = new PiecePose[EntityRenderer.MaxPieces];
            var world = new Matrix4x4[n];
            var unscale = Matrix4x4.Scale(Vector3.one / Mathf.Max(1e-12f, d.Scale));
            for (int i = 0; i < count; i++)
            {
                if (s(i / rate, buf) < n) { why = "the backend cannot pose it"; return null; }
                for (int p = 0; p < n; p++) world[p] = buf[p].Matrix * unscale;
                for (int p = 0; p < n; p++)
                {
                    int parent = d.Pieces[p].Parent;
                    if (parent < 0 || parent >= p) continue;
                    FlightPose.RigidInverse(world[parent], out var inv);
                    FlightPose.Mul(inv, world[p], out var local);
                    float y = Mathf.Atan2(local.m02, local.m22);
                    var q = local.rotation;
                    int k = r.At(i, p);
                    r.Q[k] = q;
                    r.Y[k] = y;
                    r.Xz[k] = Quaternion.AngleAxis(-y * Mathf.Rad2Deg, Vector3.up) * q;
                    r.M[k] = local.GetColumn(3);
                }
            }
            for (int cycle = 2; cycle <= longest; cycle++)
            {
                if (!Periodic(r, d, cycle)) continue;
                r.Cycle = cycle;
                // The cycle starts on a change of pose, where a key begins.
                r.Start = count - 2 * cycle;
                for (int t = count - 2 * cycle + 1; t <= count - cycle; t++)
                    if (Changed(r, d, t)) { r.Start = t; break; }
                return r;
            }
            why = $"its function never repeats within {longest / rate:0.#} s";
            return null;
        }

        // Checked over the longest cycle, so a hold is never taken for one.
        static bool Periodic(Run r, ModelData d, int cycle)
        {
            int from = r.Count - r.Longest - cycle;
            for (int t = from; t < r.Count - cycle; t++)
                for (int p = 0; p < r.Pieces; p++)
                {
                    int parent = d.Pieces[p].Parent;
                    if (parent >= 0 && parent < p && !SameXz(r, t, t + cycle, p)) return false;
                }
            return true;
        }

        static bool Changed(Run r, ModelData d, int t)
        {
            for (int p = 0; p < r.Pieces; p++)
            {
                int parent = d.Pieces[p].Parent;
                if (parent >= 0 && parent < p && !SameXz(r, t, t - 1, p)) return true;
            }
            return false;
        }

        // Component by component, since a dot product cannot see a small
        // turn through float rounding.
        static bool SameTurn(in Quaternion a, in Quaternion b)
        {
            float s = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f ? -1f : 1f;
            return Mathf.Abs(a.x - s * b.x) <= TurnEps && Mathf.Abs(a.y - s * b.y) <= TurnEps &&
                   Mathf.Abs(a.z - s * b.z) <= TurnEps && Mathf.Abs(a.w - s * b.w) <= TurnEps;
        }

        static bool SameXz(Run r, int a, int b, int p)
        {
            int i = r.At(a, p), j = r.At(b, p);
            return SameTurn(r.Xz[i], r.Xz[j]) && (r.M[i] - r.M[j]).sqrMagnitude <= SameMove * SameMove;
        }

        static bool Same(Run a, int ta, Run b, int tb, int p)
        {
            int i = a.At(ta, p), j = b.At(tb, p);
            return SameTurn(a.Xz[i], b.Xz[j]) && (a.M[i] - b.M[j]).sqrMagnitude <= SameMove * SameMove;
        }

        // Whether a piece's turn about x and z or its offset changes in the cycle.
        static bool Moves(Run r, int p)
        {
            for (int t = r.Start + 1; t < r.Start + r.Cycle; t++)
                if (!SameXz(r, r.Start, t, p)) return true;
            return false;
        }

        // The piece's y turn through the cycle: 0 still, 1 beating with the
        // cycle, 2 moving out of step with it, as a script's head turner does.
        static int YKind(Run r, int p)
        {
            bool moves = false;
            float y0 = r.Y[r.At(r.Start, p)];
            for (int t = r.Start + 1; t < r.Start + r.Cycle && !moves; t++)
                moves = Mathf.Abs(Mathf.DeltaAngle(y0 * Mathf.Rad2Deg, r.Y[r.At(t, p)] * Mathf.Rad2Deg)) > 0.05f;
            if (!moves) return 0;
            for (int t = r.Count - r.Longest - r.Cycle; t < r.Count - r.Cycle; t++)
                if (Mathf.Abs(Mathf.DeltaAngle(r.Y[r.At(t, p)] * Mathf.Rad2Deg, r.Y[r.At(t + r.Cycle, p)] * Mathf.Rad2Deg)) > 0.05f) return 2;
            return 1;
        }

        // A key wherever a channel's pose changes in the cycle.
        static Clip Keys(Run r, FlightRig rig, float rate)
        {
            int c = rig.Piece.Length;
            var ticks = new System.Collections.Generic.List<int> { r.Start };
            for (int t = r.Start + 1; t < r.Start + r.Cycle; t++)
            {
                bool change = false;
                for (int j = 0; j < c && !change; j++)
                {
                    int p = rig.Piece[j], a = r.At(t, p), b = r.At(t - 1, p);
                    change = !SameTurn(rig.KeepY[j] ? r.Xz[a] : r.Q[a], rig.KeepY[j] ? r.Xz[b] : r.Q[b]) ||
                             (r.M[a] - r.M[b]).sqrMagnitude > SameMove * SameMove;
                }
                if (change) ticks.Add(t);
            }
            var clip = new Clip
            {
                Seconds = r.Cycle / rate, Times = new float[ticks.Count],
                Turns = new Quaternion[ticks.Count * c], Moves = new Vector3[ticks.Count * c],
            };
            for (int k = 0; k < ticks.Count; k++)
            {
                clip.Times[k] = (ticks[k] - r.Start) / (float)r.Cycle;
                for (int j = 0; j < c; j++)
                {
                    int i = r.At(ticks[k], rig.Piece[j]);
                    var q = rig.KeepY[j] ? r.Xz[i] : r.Q[i];
                    // Each key on the same side as the last, so a blend takes the short way.
                    if (k > 0)
                    {
                        var prev = clip.Turns[(k - 1) * c + j];
                        if (prev.x * q.x + prev.y * q.y + prev.z * q.z + prev.w * q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    }
                    clip.Turns[k * c + j] = q;
                    clip.Moves[k * c + j] = r.M[i];
                }
            }
            return clip;
        }

        // The top and bottom of the stroke, where the table's first wing
        // piece is nearest its up and its down pose.
        static void Stroke(FlightRig rig, Run f, FlightType type, ModelData d)
        {
            rig.Top = 0f;
            rig.Downstroke = type.Downstroke;
            FlightPiece wing = null;
            foreach (var p in type.Pieces) if (p.Wobble > 0f) { wing = p; break; }
            if (wing == null && type.Pieces.Length > 0) wing = type.Pieces[0];
            if (wing == null) return;
            int at = -1;
            for (int p = 0; p < d.Pieces.Length && at < 0; p++)
                if (string.Equals(d.Pieces[p].Name, wing.Name, System.StringComparison.OrdinalIgnoreCase)) at = p;
            if (at < 0 || rig.Channel[at] < 0) return;
            var clip = rig.Flap;
            float best = float.MinValue, worst = float.MaxValue, top = 0f, bottom = 0f;
            for (int k = 0; k < clip.Times.Length; k++)
            {
                var q = f.Q[f.At(f.Start + Mathf.RoundToInt(clip.Times[k] * f.Cycle), at)];
                float s = Quaternion.Angle(q, wing.Down) - Quaternion.Angle(q, wing.Up);
                if (s > best) { best = s; top = clip.Times[k]; }
                if (s < worst) { worst = s; bottom = clip.Times[k]; }
            }
            rig.Top = top;
            rig.Downstroke = Mathf.Clamp(Mathf.Repeat(bottom - top, 1f), 0.1f, 0.9f);
        }
    }
}
