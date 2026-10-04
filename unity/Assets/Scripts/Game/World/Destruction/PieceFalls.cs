// PieceFalls.cs - pieces a dying unit's script explodes (EXPLODE in its COB
// script). The piece leaves the unit's pose where the event caught it: a
// FALL piece drops and lies a while, a SHATTER piece breaks into chunks,
// and SMOKE and FIRE trail behind it. The unit stops drawing it.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class PieceFalls
    {
        readonly FractureCache cache;
        readonly Debris debris;
        readonly HashSet<long> thrown = new HashSet<long>();
        readonly HashSet<int> alive = new HashSet<int>();
        readonly List<long> gone = new List<long>();
        uint seed = 1;

        public int Thrown => thrown.Count;

        public PieceFalls(FractureCache cache, Debris debris)
        {
            this.cache = cache;
            this.debris = debris;
        }

        static long Key(int unit, int piece) => (long)unit << 16 | (uint)(piece & 0xFFFF);

        // Whether a unit's piece has been thrown and is no longer its to draw.
        public bool IsThrown(int unit, int piece) => thrown.Count > 0 && thrown.Contains(Key(unit, piece));

        float Rand() => Fracture.Rand01(ref seed);
        Vector3 Jitter(float s) => new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) * (2f * s);

        // Throws one piece. show false only hides it, for a unit nobody sees.
        public void Throw(in PieceEvent e, PresentedModel model, bool building, bool show)
        {
            if (model == null || e.Piece < 0 || e.Piece >= model.Pieces.Length) return;
            // Only the picture of an explosion, the piece stays.
            if ((e.How & PieceExplode.BitmapOnly) != 0) return;
            thrown.Add(Key(e.Unit, e.Piece));
            if (!show) return;
            seed = (uint)(e.Id * 2654435761u) | 1u;
            var world = e.Pose * model.Unscale;
            var interior = building ? Interior.Stone : Interior.Scrap;
            bool shatter = (e.How & PieceExplode.Shatter) != 0;
            bool onHit = (e.How & PieceExplode.ExplodeOnHit) != 0;
            var trail = e.How & (PieceExplode.Smoke | PieceExplode.Fire);
            if (model.Override != null) { ThrowParts(e, model, world, interior, trail, onHit); return; }
            var mesh = model.Pieces[e.Piece];
            if (mesh == null) return;
            var centre = world.MultiplyPoint3x4(mesh.bounds.center);
            if (shatter)
            {
                long key = FractureCache.PieceKey(e.Model, e.Piece);
                var set = cache.Get(key);
                if (set == null) cache.Ask(key, model.Data.Name + "/" + model.Data.Pieces[e.Piece].Name, BreakKind.Piece, interior);
                else if (debris.Room(set.Count))
                {
                    for (int i = 0; i < set.Count; i++)
                    {
                        var c = world.MultiplyPoint3x4(set.Centre[i]);
                        var v = (c - centre).normalized * (2f + 3f * Rand()) + Vector3.up * (2f + 2f * Rand()) + Jitter(0.8f);
                        debris.Spawn(set.Draws[i], world * set.Place[i], set.Pivot[i], set.Support[i], v, Jitter(1f).normalized * (4f + 6f * Rand()), 0f, 0, -1, trail);
                    }
                    debris.Dust?.Burst(centre, mesh.bounds.extents.magnitude, DustPuffs.StoneDust, 3);
                    return;
                }
            }
            if (!debris.Room(1)) return;
            var mats = model.Materials[e.Piece];
            var draws = new ChunkDraw[mats.Length];
            for (int s = 0; s < mats.Length; s++) draws[s] = new ChunkDraw { Mesh = mesh, Submesh = s, Material = cache.ChunkMaterial(mats[s], interior) };
            debris.Spawn(draws, world, mesh.bounds.center, Corners(mesh.bounds), FallSpeed(shatter), Jitter(1f).normalized * (3f + 5f * Rand()), 0f, 0, -1, trail, onHit);
        }

        // A drop-in model's parts that follow the piece, thrown together: one
        // speed and spin about their common middle, so they fly as one until they land.
        void ThrowParts(in PieceEvent e, PresentedModel model, Matrix4x4 world, Interior interior, PieceExplode trail, bool onHit)
        {
            var centre = Vector3.zero;
            int n = 0;
            foreach (var part in model.Override.Parts)
                if (part.Piece == e.Piece && part.Mesh != null) { centre += (world * model.RestInverse[e.Piece] * part.NodeToRoot).MultiplyPoint3x4(part.Mesh.bounds.center); n++; }
            if (n == 0 || !debris.Room(n)) return;
            centre /= n;
            var v = FallSpeed(false);
            var w = Jitter(1f).normalized * (3f + 5f * Rand());
            foreach (var part in model.Override.Parts)
            {
                if (part.Piece != e.Piece || part.Mesh == null) continue;
                var m = world * model.RestInverse[e.Piece] * part.NodeToRoot;
                var pb = part.Mesh.bounds;
                var draws = new[] { new ChunkDraw { Mesh = part.Mesh, Submesh = part.Submesh, Material = cache.ChunkMaterial(part.Material, interior) } };
                debris.Spawn(draws, m, pb.center, Corners(pb), v + Vector3.Cross(w, m.MultiplyPoint3x4(pb.center) - centre), w, 0f, 0, -1, trail, onHit);
            }
        }

        // Up and out a little, or harder for a piece that shatters but is not split yet.
        Vector3 FallSpeed(bool hard)
        {
            float a = Rand() * Mathf.PI * 2f;
            var out_ = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            return out_ * ((hard ? 2f : 0.8f) + 1.5f * Rand()) + Vector3.up * ((hard ? 3f : 2f) + 2.5f * Rand());
        }

        static Vector3[] Corners(Bounds b)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            return c;
        }

        // Forgets the pieces of units that are gone, so a handle used again draws whole.
        public void Sweep(UnitState[] units, int count)
        {
            if (thrown.Count == 0) return;
            alive.Clear();
            for (int i = 0; i < count; i++) alive.Add(units[i].Handle);
            gone.Clear();
            foreach (long k in thrown) if (!alive.Contains((int)(k >> 16))) gone.Add(k);
            foreach (long k in gone) thrown.Remove(k);
        }

        public void Clear() => thrown.Clear();
    }
}
