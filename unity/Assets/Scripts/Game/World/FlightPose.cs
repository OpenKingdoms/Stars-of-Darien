// FlightPose.cs - turns a flyer's wing pieces for the animator, in place
// of the script's wing motion. It edits the piece matrices a unit is drawn
// with: each driven piece takes the table's pose, blended with the
// script's by the flyer's weight, its children follow, and the whole model
// rises by the visual offset. Pieces the table does not list keep the
// script's pose.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class FlightPose
    {
        const int MaxPieces = EntityRenderer.MaxPieces;
        static readonly Matrix4x4[] orig = new Matrix4x4[MaxPieces];
        static readonly bool[] moved = new bool[MaxPieces];
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

        // A driven piece's own transform, from its parent, in world units.
        public static Matrix4x4 Target(in Flyer f, FlightType t, FlightPiece piece, Vector3 rest, float seconds)
        {
            float a = Wave(Mathf.Repeat(f.Phase - piece.Lag, 1f), t.Downstroke);
            a = 0.5f + (a - 0.5f) * (f.Forced ? t.ForcedAmplitude : t.Amplitude);
            var flap = Quaternion.SlerpUnclamped(piece.Down, piece.Up, a);
            var glide = piece.Glide;
            if (piece.Wobble != 0f)
            {
                float wob = piece.Wobble * Mathf.Sin(2f * Mathf.PI * t.WobbleHz * piece.Side * seconds + f.Seed + piece.Side * 2f);
                glide = Quaternion.AngleAxis(wob, piece.Hinge) * glide;
            }
            float g = Mathf.SmoothStep(0f, 1f, f.Glide);
            var rotation = Quaternion.Slerp(flap, glide, g);
            var move = piece.HasMove ? Vector3.Lerp(Vector3.LerpUnclamped(piece.DownMove, piece.UpMove, a), piece.GlideMove, g) : Vector3.zero;
            return Matrix4x4.TRS(rest + move, rotation, Vector3.one);
        }

        // posed[p] is piece space to world with no scale in it, parents
        // before children, for the first n pieces of d.
        public static void Apply(in Flyer f, FlightType t, ModelData d, Matrix4x4[] posed, int n, float offset, float seconds)
        {
            n = Mathf.Min(n, Mathf.Min(d.Pieces.Length, MaxPieces));
            var drive = Bind(t, d);
            if (f.Weight > 0f)
            {
                System.Array.Copy(posed, orig, n);
                for (int p = 0; p < n; p++)
                {
                    moved[p] = false;
                    int parent = d.Pieces[p].Parent;
                    if (parent < 0 || parent >= p) continue;             // roots keep the engine's matrix
                    if (drive[p] < 0 && !moved[parent]) continue;
                    var script = orig[parent].inverse * orig[p];
                    Matrix4x4 local;
                    if (drive[p] < 0) local = script;
                    else
                    {
                        local = Target(f, t, t.Pieces[drive[p]], d.Pieces[p].Offset * d.Scale, seconds);
                        if (f.Weight < 1f)
                            local = Matrix4x4.TRS(Vector3.Lerp(script.GetColumn(3), local.GetColumn(3), f.Weight),
                                Quaternion.Slerp(script.rotation, local.rotation, f.Weight), Vector3.one);
                    }
                    posed[p] = posed[parent] * local;
                    moved[p] = true;
                }
            }
            if (offset != 0f)
            {
                var lift = Matrix4x4.Translate(Vector3.up * offset);
                for (int p = 0; p < n; p++) posed[p] = lift * posed[p];
            }
        }
    }
}
