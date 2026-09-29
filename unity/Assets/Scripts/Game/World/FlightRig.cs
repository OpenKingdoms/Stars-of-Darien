// FlightRig.cs - a flyer's own fly and soar functions from its unit
// script, as clips the animator plays on its own clock. Each function is
// posed once per unit type a tick at a time, cut to one cycle and kept as
// keyframes: the turn and offset from its parent of every piece it moves.
// A soar that never settles into a cycle is held at the pose it keeps
// longest, and a flyer with no soar glides on its flap slowed.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public enum GlideSource { None, Soar, HeldSoar, SlowedFlap }

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
        // Per channel and clip: the script's own y turn stays, since the clip
        // does not beat the piece about y, and a head keeps looking about.
        public bool[] KeepY, GlideKeepY;
        public Clip Flap, Glide;    // Glide is null only on a rig built by hand
        public GlideSource GlideFrom;
        public float Top;           // the flap clip's phase at the top of the stroke
        public float Downstroke;    // and the share of it spent going down
        public float GlideSeconds => Glide != null ? Glide.Seconds : 0f;

        // How much slower than the flap a flyer with no soar beats as it glides.
        public const float SlowedFlap = 2.5f;

        // A channel's turn and offset at a flap and a glide position, crossed
        // by glide, with the script's own y turn where a clip keeps it.
        public void Sample(int ch, int fa, int fb, float fw, int ga, int gb, float gw, float glide, in Quaternion scriptY, out Quaternion q, out Vector3 at)
        {
            int c = KeepY.Length;
            q = Nlerp(Flap.Turns[fa * c + ch], Flap.Turns[fb * c + ch], fw);
            if (KeepY[ch]) q = scriptY * q;
            at = Lerp(Flap.Moves[fa * c + ch], Flap.Moves[fb * c + ch], fw);
            if (glide <= 0f || Glide == null) return;
            var gq = Nlerp(Glide.Turns[ga * c + ch], Glide.Turns[gb * c + ch], gw);
            if (GlideKeepY == null || GlideKeepY[ch]) gq = scriptY * gq;
            var gm = Lerp(Glide.Moves[ga * c + ch], Glide.Moves[gb * c + ch], gw);
            q = Nlerp(q, gq, glide);
            at = Lerp(at, gm, glide);
        }

        // Whether either clip in play keeps the script's y turn of a channel.
        public bool KeepsY(int ch, float glide) =>
            KeepY[ch] || (glide > 0f && Glide != null && (GlideKeepY == null || GlideKeepY[ch]));

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
        // The longest a function is sampled for a cycle. Posed at rest, a
        // script restores its other pieces slowly and plays idles between
        // its cycles, so the cycle is taken where it runs clean.
        public const float LongestSample = 24f;
        const float TurnEps = 2e-5f, SameMove = 1e-3f;

        // One function sampled a tick at a time, with its cycle found.
        sealed class Run
        {
            public int Count, Pieces, Cycle, Start, Longest;
            public int From, Verify;    // the stretch that repeats: its first tick and length
            public bool Held;           // no cycle: Start is the pose it keeps longest, for Cycle ticks
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
            if (f.Cycle == 0) return null;
            var g = glide != null ? Take(glide, d, n, rate, 0f, out _) : null;
            if (g != null && g.Cycle == 0) Hold(g, d);

            var rig = new FlightRig { Channel = new int[d.Pieces.Length] };
            var pieces = new System.Collections.Generic.List<int>();
            var keepY = new System.Collections.Generic.List<bool>();
            var glideKeepY = new System.Collections.Generic.List<bool>();
            bool flaps = false;
            for (int p = 0; p < rig.Channel.Length; p++)
            {
                rig.Channel[p] = -1;
                if (p >= n || d.Pieces[p].Parent < 0 || d.Pieces[p].Parent >= p) continue;
                int fy = YKind(f, p), gy = g != null ? YKind(g, p) : 0;
                // A piece a clip moves is a channel. The studio's idles leave the
                // rest differently each run, so they stay the script's, except
                // where a held soar, which moves nothing, holds them apart from fly.
                bool flapMoves = fy == 1 || Moves(f, p);
                bool glideMoves = g != null && (gy == 1 || Moves(g, p) || (g.Held && !Same(f, f.Start, g, g.Start, p)));
                if (!flapMoves && !glideMoves) continue;
                // A clip carries the y turn it beats with its cycle, and the
                // glide the y turn it holds still where fly beats it. Otherwise
                // the script's y turn stays, and a head keeps looking about.
                bool flapY = fy == 1, glideY = gy == 1 || (gy == 0 && fy == 1);
                flaps |= flapMoves;
                rig.Channel[p] = pieces.Count;
                pieces.Add(p);
                keepY.Add(!flapY);
                glideKeepY.Add(!glideY);
            }
            if (!flaps) { why = "its flap function moves nothing"; return null; }
            rig.Piece = pieces.ToArray();
            rig.KeepY = keepY.ToArray();
            rig.Flap = Keys(f, rig, rate, rig.KeepY);
            if (g != null)
            {
                rig.GlideKeepY = glideKeepY.ToArray();
                rig.Glide = Keys(g, rig, rate, rig.GlideKeepY);
                rig.GlideFrom = g.Held ? GlideSource.HeldSoar : GlideSource.Soar;
            }
            else
            {
                // No glide of its own: the flap, slower.
                rig.GlideKeepY = rig.KeepY;
                rig.Glide = new Clip { Seconds = rig.Flap.Seconds * SlowedFlap, Times = rig.Flap.Times, Turns = rig.Flap.Turns, Moves = rig.Flap.Moves };
                rig.GlideFrom = GlideSource.SlowedFlap;
            }
            Stroke(rig, f, type, d);
            return rig;
        }

        // Samples a function until a stretch of it repeats over the longest
        // cycle, the latest such stretch, where it has settled. Null when the
        // backend cannot pose it, and Cycle 0 when it never repeats.
        static Run Take(Sampler s, ModelData d, int n, float rate, float period, out string why)
        {
            why = null;
            int longest = Mathf.CeilToInt(Mathf.Max(LongestCycle, 1.6f * period) * rate);
            int most = Mathf.Max(3 * longest + 2, Mathf.CeilToInt(LongestSample * rate));
            var r = new Run
            {
                Pieces = n, Longest = longest,
                Q = new Quaternion[most * n], Xz = new Quaternion[most * n], Y = new float[most * n], M = new Vector3[most * n],
            };
            var buf = new PiecePose[EntityRenderer.MaxPieces];
            var world = new Matrix4x4[n];
            var unscale = Matrix4x4.Scale(Vector3.one / Mathf.Max(1e-12f, d.Scale));
            int looked = 0;
            for (int want = 3 * longest + 2; ; want = Mathf.Min(most, r.Count + longest))
            {
                for (int i = r.Count; i < want; i++)
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
                r.Count = want;
                var changes = Changes(r, d);
                if (changes[r.Count] == 0) { why = "it moves nothing"; return r; }
                if (Find(r, d, changes, looked, false)) return r;
                looked = want;
                if (want >= most) break;
            }
            // Idles may leave no clean stretch that long: two passes alike will do.
            if (Find(r, d, Changes(r, d), 0, true)) return r;
            why = $"its function never repeats within {most / rate:0.#} s";
            return r;
        }

        // The smallest cycle with a stretch that repeats it and moves, the
        // latest such stretch, among those ending after tick `after`. The
        // stretch is the longest cycle long, so a hold is never taken for
        // one, or with `twice` just one cycle, two passes alike.
        static bool Find(Run r, ModelData d, int[] changes, int after, bool twice)
        {
            for (int cycle = 2; cycle <= r.Longest; cycle++)
            {
                int verify = twice ? cycle : r.Longest;
                int run = 0, low = Mathf.Max(1, after - verify - 2 * cycle);
                for (int t = r.Count - cycle - 1; t >= low; t--)
                {
                    if (!Repeats(r, d, t, cycle)) { run = 0; continue; }
                    if (++run < verify) continue;
                    // The poses from t to the stretch's end plus a cycle agree,
                    // and the cycle starts on a change of pose, where a key begins.
                    int last = t + verify - cycle;
                    if (changes[last + cycle + 1] == changes[last + 1]) continue;
                    for (int u = last + 1; u <= last + cycle; u++)
                    {
                        if (changes[u + 1] == changes[u]) continue;
                        r.Cycle = cycle;
                        r.From = t;
                        r.Verify = verify;
                        r.Start = u;
                        return true;
                    }
                }
            }
            return false;
        }

        // How many ticks before each tick change pose, so a stretch's changes count at once.
        static int[] Changes(Run r, ModelData d)
        {
            var c = new int[r.Count + 1];
            for (int t = 0; t < r.Count; t++) c[t + 1] = c[t] + (t > 0 && Changed(r, d, t) ? 1 : 0);
            return c;
        }

        static bool Repeats(Run r, ModelData d, int t, int cycle)
        {
            for (int p = 0; p < r.Pieces; p++)
            {
                int parent = d.Pieces[p].Parent;
                if (parent >= 0 && parent < p && !SameXz(r, t, t + cycle, p)) return false;
            }
            return true;
        }

        // A function that never repeats is held at the pose it keeps for
        // longest in its later half.
        static void Hold(Run r, ModelData d)
        {
            int best = r.Count - 1, bestLength = 0;
            for (int t = r.Count / 2, from = t; t < r.Count; t++)
            {
                if (t > from && Changed(r, d, t)) from = t;
                if (t - from + 1 > bestLength) { best = from; bestLength = t - from + 1; }
            }
            r.Held = true;
            r.Start = r.From = best;
            r.Cycle = r.Verify = bestLength;
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
            if (r.Held) return 0;
            bool moves = false;
            float y0 = r.Y[r.At(r.Start, p)];
            for (int t = r.Start + 1; t < r.Start + r.Cycle && !moves; t++)
                moves = Mathf.Abs(Mathf.DeltaAngle(y0 * Mathf.Rad2Deg, r.Y[r.At(t, p)] * Mathf.Rad2Deg)) > 0.05f;
            if (!moves) return 0;
            for (int t = r.From; t < r.From + r.Verify; t++)
                if (Mathf.Abs(Mathf.DeltaAngle(r.Y[r.At(t, p)] * Mathf.Rad2Deg, r.Y[r.At(t + r.Cycle, p)] * Mathf.Rad2Deg)) > 0.05f) return 2;
            return 1;
        }

        // A key wherever a channel's pose changes in the cycle.
        static Clip Keys(Run r, FlightRig rig, float rate, bool[] keepY)
        {
            int c = rig.Piece.Length;
            var ticks = new System.Collections.Generic.List<int> { r.Start };
            for (int t = r.Start + 1; t < r.Start + r.Cycle; t++)
            {
                bool change = false;
                for (int j = 0; j < c && !change; j++)
                {
                    int p = rig.Piece[j], a = r.At(t, p), b = r.At(t - 1, p);
                    change = !SameTurn(keepY[j] ? r.Xz[a] : r.Q[a], keepY[j] ? r.Xz[b] : r.Q[b]) ||
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
                    var q = keepY[j] ? r.Xz[i] : r.Q[i];
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
