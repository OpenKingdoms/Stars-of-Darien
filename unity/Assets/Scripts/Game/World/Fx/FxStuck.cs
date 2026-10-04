// FxStuck.cs - arrows, bolts and spears that land in the ground stay there,
// stuck where they fell with their tips in the earth, for a while, then sink
// away. A shot drawn as a model is followed while it flies, and when it ends
// where an arrow's blast landed on the ground, its last pose is kept.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxStuck
    {
        public const float Seconds = 10f, Sink = 1.2f;
        // How far a stuck shot's tip goes into the ground.
        const float Bite = 0.22f;

        sealed class Flight
        {
            public int Model, Pieces, Frame;
            public Vector3 At, Vel;
            public Matrix4x4[] Matrix = new Matrix4x4[4];
            public float Age, Life;
        }

        readonly Dictionary<int, Flight> flying = new Dictionary<int, Flight>();
        readonly List<Flight> stuck = new List<Flight>();
        readonly Stack<Flight> spare = new Stack<Flight>();
        readonly List<int> ended = new List<int>();

        public int Count => stuck.Count;

        // A model shot this frame, its pieces placed in the world.
        public void Track(int shot, int model, Vector3 at, Vector3 vel, int frame, PiecePose[] poses, int pieces, Matrix4x4 unscale)
        {
            if (!flying.TryGetValue(shot, out var f))
            {
                f = spare.Count > 0 ? spare.Pop() : new Flight();
                flying[shot] = f;
            }
            f.Model = model;
            f.At = at;
            f.Vel = vel;
            f.Frame = frame;
            if (f.Matrix.Length < pieces) f.Matrix = new Matrix4x4[Mathf.Min(EntityRenderer.MaxPieces, Mathf.NextPowerOfTwo(pieces))];
            f.Pieces = Mathf.Min(pieces, f.Matrix.Length);
            for (int i = 0; i < f.Pieces; i++) f.Matrix[i] = poses[i].Hidden ? default : poses[i].Matrix * unscale;
        }

        // Shots not seen this frame have ended. Those that ended at an
        // arrow's landing stay stuck where they were, the rest are let go.
        public void Settle(int frame, List<Vector3> landings, int keep)
        {
            ended.Clear();
            foreach (var kv in flying) if (kv.Value.Frame != frame) ended.Add(kv.Key);
            foreach (int id in ended)
            {
                var f = flying[id];
                flying.Remove(id);
                bool landed = false;
                var landing = f.At;
                for (int i = 0; i < landings.Count && !landed; i++)
                {
                    var d = landings[i] - f.At;
                    d.y = 0f;
                    landed = d.sqrMagnitude < 2.5f * 2.5f;
                    if (landed) landing = landings[i];
                }
                if (!landed || keep <= 0) { spare.Push(f); continue; }
                // Moved to where it landed and driven on along its flight until
                // its tip bites the ground.
                var way = f.Vel.sqrMagnitude > 1e-4f ? f.Vel.normalized : Vector3.down;
                var on = Matrix4x4.Translate(landing - f.At + way * Bite);
                for (int i = 0; i < f.Pieces; i++) f.Matrix[i] = on * f.Matrix[i];
                f.Age = 0f;
                f.Life = Seconds;
                while (stuck.Count >= keep) { spare.Push(stuck[0]); stuck.RemoveAt(0); }
                stuck.Add(f);
            }
        }

        public void Draw(float dt, ModelCache models, InstancedDraws into, System.Func<Vector3, int, bool> hidden)
        {
            for (int s = stuck.Count - 1; s >= 0; s--)
            {
                var f = stuck[s];
                f.Age += dt;
                if (f.Age >= f.Life) { spare.Push(f); stuck.RemoveAt(s); continue; }
                if (models == null || hidden != null && hidden(f.At, -1)) continue;
                var m = models.Get(f.Model);
                if (m == null) continue;
                float down = Mathf.Clamp01((f.Age - (f.Life - Sink)) / Sink) * 0.6f;
                var drop = Matrix4x4.Translate(Vector3.down * down);
                int n = Mathf.Min(f.Pieces, m.Pieces.Length);
                for (int i = 0; i < n; i++)
                {
                    var mesh = m.Pieces[i];
                    if (mesh == null || f.Matrix[i].m33 == 0f) continue;
                    var mats = m.Materials[i];
                    var world = drop * f.Matrix[i];
                    for (int k = 0; k < mats.Length; k++) if (mats[k] != null) into.Add(mesh, k, mats[k], world);
                }
            }
        }

        public void Clear()
        {
            foreach (var f in stuck) spare.Push(f);
            stuck.Clear();
            foreach (var f in flying.Values) spare.Push(f);
            flying.Clear();
        }
    }
}
