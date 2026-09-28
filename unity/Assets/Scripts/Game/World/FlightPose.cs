// FlightPose.cs - poses a flyer for the animator, in place of the
// script's flight. It edits the piece matrices a unit is drawn with: each
// piece the rig's clips move takes the clip's pose at the animator's
// phase, blended with the script's by the flyer's weight, and the whole
// model rises by the visual offset. A model with no rig turns the table's
// wing pieces between their down, up and glide poses instead. Pieces
// neither moves keep the script's pose under their moved parent.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class FlightPose
    {
        const int MaxPieces = EntityRenderer.MaxPieces;
        static readonly Matrix4x4[] orig = new Matrix4x4[MaxPieces], delta = new Matrix4x4[MaxPieces];
        static readonly bool[] moved = new bool[MaxPieces], ready = new bool[MaxPieces];
        static readonly int[] carry = new int[MaxPieces];
        static readonly Dictionary<(FlightType, ModelData), int[]> bindings = new Dictionary<(FlightType, ModelData), int[]>();
        static readonly HashSet<string> warned = new HashSet<string>();

        // For each piece of the model, its entry in the type's pieces or -1.
        public static int[] Bind(FlightType t, ModelData d)
        {
            if (bindings.TryGetValue((t, d), out var drive)) return drive;
            drive = new int[d.Pieces.Length];
            for (int p = 0; p < drive.Length; p++) drive[p] = -1;
            for (int k = 0; k < t.Pieces.Length; k++)
            {
                int found = -1;
                for (int p = 0; p < d.Pieces.Length && found < 0; p++)
                    if (string.Equals(d.Pieces[p].Name, t.Pieces[k].Name, System.StringComparison.OrdinalIgnoreCase)) found = p;
                if (found >= 0 && d.Pieces[found].Parent >= 0) drive[found] = k;
                else if (warned.Add(t.Name + "/" + t.Pieces[k].Name))
                    Debug.LogWarning($"Flight table: {t.Name} lists piece {t.Pieces[k].Name}, which its model {(found < 0 ? "lacks" : "has as its root")}");
            }
            bindings[(t, d)] = drive;
            return drive;
        }

        public static void Forget() => bindings.Clear();

        // 1 at the top of the stroke (x = 0), 0 at the bottom (x = d), eased both ways.
        public static float Wave(float x, float d) => x < d
            ? 0.5f + 0.5f * Mathf.Cos(Mathf.PI * x / d)
            : 0.5f - 0.5f * Mathf.Cos(Mathf.PI * (x - d) / (1f - d));

        // A table piece's turn and offset from its parent, in world units.
        public static void Target(in Flyer f, FlightType t, FlightPiece piece, Vector3 rest, float seconds, out Quaternion rotation, out Vector3 at)
        {
            float a = Wave(Mathf.Repeat(f.Phase - piece.Lag, 1f), t.Downstroke);
            a = 0.5f + (a - 0.5f) * Mathf.Lerp(t.Amplitude, t.ForcedAmplitude, f.Force);
            var flap = Quaternion.SlerpUnclamped(piece.Down, piece.Up, a);
            var glide = piece.Glide;
            if (piece.Wobble != 0f)
            {
                float wob = piece.Wobble * Mathf.Sin(2f * Mathf.PI * t.WobbleHz * piece.Side * seconds + f.Seed + piece.Side * 2f);
                glide = Quaternion.AngleAxis(wob, piece.Hinge) * glide;
            }
            float g = Mathf.SmoothStep(0f, 1f, f.Glide);
            rotation = Quaternion.Slerp(flap, glide, g);
            at = rest + (piece.HasMove ? Vector3.Lerp(Vector3.LerpUnclamped(piece.DownMove, piece.UpMove, a), piece.GlideMove, g) : Vector3.zero);
        }

        // posed[p] is piece space to world with no scale in it, parents
        // before children, for the first n pieces of d. rig may be null.
        public static void Apply(in Flyer f, FlightType t, FlightRig rig, ModelData d, Matrix4x4[] posed, int n, float offset, float seconds)
        {
            n = Mathf.Min(n, Mathf.Min(d.Pieces.Length, MaxPieces));
            if (f.Weight > 0f)
            {
                var drive = rig != null ? rig.Channel : Bind(t, d);
                int fa = 0, fb = 0, ga = 0, gb = 0;
                float fw = 0f, gw = 0f, g = Mathf.SmoothStep(0f, 1f, f.Glide);
                if (rig != null)
                {
                    rig.Flap.Locate(Mathf.Repeat(rig.Top + f.Phase, 1f), out fa, out fb, out fw);
                    if (g > 0f && rig.Glide != null) rig.Glide.Locate(Mathf.Repeat(f.GlidePhase, 1f), out ga, out gb, out gw);
                    else g = 0f;
                }
                for (int p = 0; p < n; p++)
                {
                    moved[p] = false;
                    int parent = d.Pieces[p].Parent;
                    if (parent < 0 || parent >= p) continue;             // roots keep the engine's matrix
                    int k = p < drive.Length ? drive[p] : -1;
                    if (k < 0)
                    {
                        if (!moved[parent]) continue;
                        // Carried by the change of the nearest posed piece above it.
                        int c = carry[parent];
                        if (!ready[c])
                        {
                            RigidInverse(orig[c], out var inv);
                            Mul(posed[c], inv, out delta[c]);
                            ready[c] = true;
                        }
                        orig[p] = posed[p];
                        Mul(delta[c], orig[p], out posed[p]);
                        carry[p] = c;
                        moved[p] = true;
                        continue;
                    }
                    Quaternion q;
                    Vector3 at;
                    if (rig != null) rig.Sample(k, fa, fb, fw, ga, gb, gw, g, out q, out at);
                    else Target(f, t, t.Pieces[k], d.Pieces[p].Offset * d.Scale, seconds, out q, out at);
                    orig[p] = posed[p];
                    bool keepY = rig != null && rig.KeepY[k];
                    if (keepY || f.Weight < 1f)
                    {
                        ref var above = ref (moved[parent] ? ref orig[parent] : ref posed[parent]);
                        if (keepY)
                        {
                            // The script's own turn about y, outermost in a script's turn.
                            float l02 = above.m00 * orig[p].m02 + above.m10 * orig[p].m12 + above.m20 * orig[p].m22;
                            float l22 = above.m02 * orig[p].m02 + above.m12 * orig[p].m12 + above.m22 * orig[p].m22;
                            float half = 0.5f * Mathf.Atan2(l02, l22);
                            q = new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half)) * q;
                        }
                        if (f.Weight < 1f)
                        {
                            RigidInverse(above, out var inv);
                            Mul(inv, orig[p], out var script);
                            q = Quaternion.Slerp(script.rotation, q, f.Weight);
                            at = Vector3.Lerp(script.GetColumn(3), at, f.Weight);
                        }
                    }
                    Compose(posed[parent], q, at, out posed[p]);
                    carry[p] = p;
                    ready[p] = false;
                    moved[p] = true;
                }
            }
            Lift(posed, n, offset);
        }

        public static void Lift(Matrix4x4[] posed, int n, float offset)
        {
            if (offset == 0f) return;
            for (int p = 0; p < n; p++) posed[p].m13 += offset;
        }

        // ---- Matrices with no scale, bottom row 0 0 0 1 ----

        public static void RigidInverse(in Matrix4x4 m, out Matrix4x4 r)
        {
            r = default;
            r.m00 = m.m00; r.m01 = m.m10; r.m02 = m.m20;
            r.m10 = m.m01; r.m11 = m.m11; r.m12 = m.m21;
            r.m20 = m.m02; r.m21 = m.m12; r.m22 = m.m22;
            r.m03 = -(m.m00 * m.m03 + m.m10 * m.m13 + m.m20 * m.m23);
            r.m13 = -(m.m01 * m.m03 + m.m11 * m.m13 + m.m21 * m.m23);
            r.m23 = -(m.m02 * m.m03 + m.m12 * m.m13 + m.m22 * m.m23);
            r.m33 = 1f;
        }

        public static void Mul(in Matrix4x4 a, in Matrix4x4 b, out Matrix4x4 r)
        {
            r = default;
            r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20;
            r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21;
            r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22;
            r.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03;
            r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20;
            r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21;
            r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22;
            r.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13;
            r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20;
            r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21;
            r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22;
            r.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23;
            r.m33 = 1f;
        }

        // a * TRS(t, q, 1), with q of unit length.
        public static void Compose(in Matrix4x4 a, in Quaternion q, in Vector3 t, out Matrix4x4 r)
        {
            float x2 = q.x + q.x, y2 = q.y + q.y, z2 = q.z + q.z;
            float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2, xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
            float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;
            float b00 = 1f - (yy + zz), b01 = xy - wz, b02 = xz + wy;
            float b10 = xy + wz, b11 = 1f - (xx + zz), b12 = yz - wx;
            float b20 = xz - wy, b21 = yz + wx, b22 = 1f - (xx + yy);
            r = default;
            r.m00 = a.m00 * b00 + a.m01 * b10 + a.m02 * b20;
            r.m01 = a.m00 * b01 + a.m01 * b11 + a.m02 * b21;
            r.m02 = a.m00 * b02 + a.m01 * b12 + a.m02 * b22;
            r.m03 = a.m00 * t.x + a.m01 * t.y + a.m02 * t.z + a.m03;
            r.m10 = a.m10 * b00 + a.m11 * b10 + a.m12 * b20;
            r.m11 = a.m10 * b01 + a.m11 * b11 + a.m12 * b21;
            r.m12 = a.m10 * b02 + a.m11 * b12 + a.m12 * b22;
            r.m13 = a.m10 * t.x + a.m11 * t.y + a.m12 * t.z + a.m13;
            r.m20 = a.m20 * b00 + a.m21 * b10 + a.m22 * b20;
            r.m21 = a.m20 * b01 + a.m21 * b11 + a.m22 * b21;
            r.m22 = a.m20 * b02 + a.m21 * b12 + a.m22 * b22;
            r.m23 = a.m20 * t.x + a.m21 * t.y + a.m22 * t.z + a.m23;
            r.m33 = 1f;
        }
    }
}
